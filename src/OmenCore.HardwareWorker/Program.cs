using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace OmenCore.HardwareWorker;

/// <summary>
/// Out-of-process hardware monitoring worker.
/// Hosts LibreHardwareMonitor in a separate process to isolate crashes (especially NVML).
/// Communicates with main OmenCore app via named pipes.
/// 
/// If this process crashes due to driver issues, the main app continues running
/// and can restart this worker automatically.
/// </summary>
[SupportedOSPlatform("windows")]
class Program
{
    private const string PipeName = "OmenCore_HardwareWorker";
    private const int UpdateIntervalMs = 500;
    private const int OrphanTimeoutMs = 5 * 60 * 1000; // 5 minutes with no client before self-exit
    private const string MutexName = "Global\\OmenCore_HardwareWorker_Mutex";
    
    private static Computer? _computer;
    private static readonly object _lock = new();
    private static HardwareSample _lastSample = new();
    private static bool _running = true;
    private static DateTime _lastUpdate = DateTime.MinValue;
    private static int _parentProcessId = -1;
    private static readonly Dictionary<string, DateTime> _lastErrorLog = new(); // Rate-limit error logging
    private static bool _hasInitialized = false; // Track if we've done at least one full hardware update cycle
    
    // Resilience: worker survives parent exit and waits for new connections
    private static bool _parentAlive = false;
    private static DateTime _lastClientActivity = DateTime.Now;
    private static Mutex? _singleInstanceMutex;
    
    // PawnIO fallback for CPU temp when LibreHardwareMonitor fails (Defender blocks WinRing0)
    private static PawnIOCpuTemp? _pawnIOCpuTemp;
    private static bool _pawnIOFallbackActive = false;
    private static int _consecutiveNullCpuTemp = 0;
    private const int NullTempThresholdForFallback = 3; // Switch to PawnIO after 3 null readings

    static async Task Main(string[] args)
    {
        // Parse parent process ID from command line (passed by main app)
        if (args.Length > 0 && int.TryParse(args[0], out var ppid))
        {
            _parentProcessId = ppid;
            _parentAlive = true;
            Console.WriteLine($"Parent process ID: {_parentProcessId}");
        }
        
        // Single-instance check: if another worker is already running, exit quietly
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                Console.WriteLine("Another HardwareWorker is already running. Exiting.");
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Another worker already running (mutex held). Exiting duplicate.\n");
                return;
            }
        }
        catch (Exception ex)
        {
            // Can't acquire mutex — proceed anyway (don't block on mutex issues)
            Console.WriteLine($"Mutex check failed: {ex.Message}");
        }
        
        // Rotate old log files
        RotateLogIfNeeded();
        
        // Set up crash handler to log before exit
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] WORKER CRASH: {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}\n");
        };

        Console.WriteLine($"OmenCore Hardware Worker starting... PID={Environment.ProcessId}");
        
        try
        {
            InitializeHardware();
            
            // Start background update thread
            var updateTask = Task.Run(UpdateLoop);
            
            // Start parent process monitor (notifies worker but does NOT exit)
            var parentMonitorTask = Task.Run(MonitorParentProcess);
            
            // Start orphan watchdog (exits if no client connects for 5 minutes after parent dies)
            var orphanWatchdogTask = Task.Run(OrphanWatchdog);
            
            // Run pipe server
            await RunPipeServer();
        }
        catch (Exception ex)
        {
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] WORKER ERROR: {ex}\n");
            throw;
        }
        finally
        {
            CleanupHardware();
        }
    }
    
    /// <summary>
    /// Monitor parent process and exit if it dies (prevents orphaned workers).
    /// Worker no longer exits when parent dies — it continues running and waits
    /// for a new OmenCore instance to connect. This eliminates temperature gaps
    /// across app restarts. The OrphanWatchdog handles cleanup if no client reconnects.
    /// </summary>
    private static async Task MonitorParentProcess()
    {
        if (_parentProcessId <= 0) return;
        
        try
        {
            using var parentProcess = System.Diagnostics.Process.GetProcessById(_parentProcessId);
            
            // Poll every 2 seconds to check if parent is still alive
            while (_running && !parentProcess.HasExited)
            {
                await Task.Delay(2000);
            }
            
            if (parentProcess.HasExited)
            {
                Console.WriteLine($"Parent process {_parentProcessId} exited. Worker continuing — waiting for new connection...");
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent process {_parentProcessId} exited. Worker staying alive for reconnection.\n");
                _parentAlive = false;
                // Do NOT exit — keep running so the next OmenCore instance can reuse us
                // The OrphanWatchdog will handle cleanup if no client reconnects
            }
        }
        catch (ArgumentException)
        {
            // Parent process doesn't exist (already exited)
            Console.WriteLine($"Parent process {_parentProcessId} not found. Worker continuing — waiting for new connection...");
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent process {_parentProcessId} not found. Worker staying alive for reconnection.\n");
            _parentAlive = false;
        }
        catch (Exception ex)
        {
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent monitor error: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Watchdog that exits the worker if no client connects for OrphanTimeoutMs after parent dies.
    /// Prevents orphaned worker processes from running indefinitely.
    /// </summary>
    private static async Task OrphanWatchdog()
    {
        while (_running)
        {
            await Task.Delay(30_000); // Check every 30 seconds
            
            // Only enforce timeout if parent is dead
            if (_parentAlive) continue;
            
            var timeSinceActivity = DateTime.Now - _lastClientActivity;
            if (timeSinceActivity.TotalMilliseconds > OrphanTimeoutMs)
            {
                Console.WriteLine($"No client activity for {timeSinceActivity.TotalMinutes:F1} minutes. Exiting orphaned worker.");
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Orphan timeout: no client for {timeSinceActivity.TotalMinutes:F1} min. Worker exiting.\n");
                _running = false;
                
                try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
                Environment.Exit(0);
            }
        }
    }

    private static string GetLogPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "OmenCore");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, "HardwareWorker.log");
    }
    
    /// <summary>
    /// Rotate log file if it's too large (> 1MB) or too old (> 7 days).
    /// </summary>
    private static void RotateLogIfNeeded()
    {
        try
        {
            var logPath = GetLogPath();
            if (!File.Exists(logPath)) return;
            
            var info = new FileInfo(logPath);
            var shouldRotate = info.Length > 1024 * 1024 || // > 1MB
                               info.LastWriteTime < DateTime.Now.AddDays(-7); // > 7 days old
            
            if (shouldRotate)
            {
                var backupPath = logPath + ".old";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(logPath, backupPath);
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    private static void InitializeHardware()
    {
        Console.WriteLine("Initializing LibreHardwareMonitor...");
        
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsBatteryEnabled = true,
            IsMotherboardEnabled = true
        };
        
        _computer.Open();
        Console.WriteLine("LibreHardwareMonitor initialized.");
        
        // Initialize PawnIO fallback for CPU temp (in case Defender blocks WinRing0)
        try
        {
            _pawnIOCpuTemp = new PawnIOCpuTemp();
            if (_pawnIOCpuTemp.IsAvailable)
            {
                Console.WriteLine("PawnIO CPU temp fallback available.");
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] PawnIO CPU temp fallback initialized (available if WinRing0 blocked)\n");
            }
            else
            {
                Console.WriteLine("PawnIO not available (WinRing0 will be used for CPU temp).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PawnIO init failed: {ex.Message}");
            _pawnIOCpuTemp = null;
        }
        
        // Log detected GPUs for diagnostics
        LogDetectedHardware();
    }
    
    private static void LogDetectedHardware()
    {
        if (_computer?.Hardware == null) return;
        
        var logPath = GetLogPath();
        
        // Log CPU sensors first (critical for fan control)
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.Cpu)
            {
                hw.Update();
                var tempSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
                var loadSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Load).ToList();
                var powerSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
                
                Console.WriteLine($"[CPU Detected] {hw.Name}");
                Console.WriteLine($"  Temp sensors ({tempSensors.Count}): [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]");
                Console.WriteLine($"  Load sensors ({loadSensors.Count}): [{string.Join(", ", loadSensors.Select(s => $"{s.Name}={s.Value:F0}%"))}]");
                Console.WriteLine($"  Power sensors ({powerSensors.Count}): [{string.Join(", ", powerSensors.Select(s => $"{s.Name}={s.Value:F1}W"))}]");
                
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] [CPU Detected] {hw.Name}\n");
                File.AppendAllText(logPath, $"  Temp sensors ({tempSensors.Count}): [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]\n");
                
                if (tempSensors.Count == 0)
                {
                    File.AppendAllText(logPath, $"  ⚠️ WARNING: No CPU temperature sensors detected!\n");
                    Console.WriteLine("  ⚠️ WARNING: No CPU temperature sensors detected!");
                }
            }
        }
        
        // Log GPU sensors
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.GpuNvidia ||
                hw.HardwareType == HardwareType.GpuAmd ||
                hw.HardwareType == HardwareType.GpuIntel)
            {
                hw.Update();
                var tempSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
                var loadSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Load).ToList();
                var powerSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Power).ToList();
                
                var gpuType = hw.HardwareType switch
                {
                    HardwareType.GpuNvidia => "NVIDIA",
                    HardwareType.GpuAmd => "AMD",
                    HardwareType.GpuIntel => hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? "Intel Arc" : "Intel iGPU",
                    _ => "Unknown"
                };
                
                Console.WriteLine($"[GPU Detected] {gpuType}: {hw.Name}");
                Console.WriteLine($"  Temp sensors: [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]");
                Console.WriteLine($"  Load sensors: [{string.Join(", ", loadSensors.Select(s => $"{s.Name}={s.Value:F0}%"))}]");
                Console.WriteLine($"  Power sensors: [{string.Join(", ", powerSensors.Select(s => $"{s.Name}={s.Value:F1}W"))}]");
                
                // Also log to file
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] [GPU Detected] {gpuType}: {hw.Name}\n");
                File.AppendAllText(logPath, $"  Temp sensors: [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]\n");
            }
        }
        
        // Log Memory sensors
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType == HardwareType.Memory)
            {
                hw.Update();
                var dataSensors = hw.Sensors.Where(s => s.SensorType == SensorType.Data).ToList();
                
                Console.WriteLine($"[Memory Detected] {hw.Name}");
                Console.WriteLine($"  Data sensors ({dataSensors.Count}): [{string.Join(", ", dataSensors.Select(s => $"{s.Name}={s.Value:F0}MB"))}]");
                
                // Also log to file
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] [Memory Detected] {hw.Name}\n");
                File.AppendAllText(logPath, $"  Data sensors ({dataSensors.Count}): [{string.Join(", ", dataSensors.Select(s => $"{s.Name}={s.Value:F0}MB"))}]\n");
            }
        }
    }

    private static void CleanupHardware()
    {
        _running = false;
        try
        {
            _computer?.Close();
        }
        catch { }
    }

    private static async Task UpdateLoop()
    {
        while (_running)
        {
            try
            {
                UpdateHardwareReadings();
            }
            catch (AccessViolationException ex)
            {
                // NVML crash - log and continue (may crash process anyway)
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] NVML ACCESS VIOLATION: {ex.Message}\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Update error: {ex.Message}\n");
            }
            
            await Task.Delay(UpdateIntervalMs);
        }
    }

    private static void UpdateHardwareReadings()
    {
        if (_computer == null) return;
        
        lock (_lock)
        {
            // Track if CPU hardware update succeeded this cycle
            var previousCpuTemp = _lastSample.CpuTemperature;
            
            // Start with last known values to preserve data if some hardware fails
            var sample = new HardwareSample
            {
                CpuTemperature = _lastSample.CpuTemperature,
                CpuLoad = _lastSample.CpuLoad,
                CpuPower = _lastSample.CpuPower,
                GpuName = _lastSample.GpuName,
                GpuTemperature = _lastSample.GpuTemperature,
                GpuHotspot = _lastSample.GpuHotspot,
                GpuLoad = _lastSample.GpuLoad,
                GpuPower = _lastSample.GpuPower,
                GpuClock = _lastSample.GpuClock,
                GpuMemoryClock = _lastSample.GpuMemoryClock,
                GpuVoltage = _lastSample.GpuVoltage,
                GpuCurrent = _lastSample.GpuCurrent,
                VramUsage = _lastSample.VramUsage,
                VramTotal = _lastSample.VramTotal,
                RamUsage = _lastSample.RamUsage,
                RamTotal = _lastSample.RamTotal,
                SsdTemperature = _lastSample.SsdTemperature,
                BatteryCharge = _lastSample.BatteryCharge,
                BatteryDischargeRate = _lastSample.BatteryDischargeRate,
                IsOnAc = _lastSample.IsOnAc,
                FanSpeeds = new Dictionary<string, double>(_lastSample.FanSpeeds),
                IsFresh = true,  // Assume fresh until proven otherwise
                StaleCount = 0
            };
            
            // UpdateVisitor can throw if storage goes to sleep - catch and continue
            try
            {
                _computer.Accept(new UpdateVisitor());
            }
            catch (ObjectDisposedException)
            {
                // Storage drive went to sleep - this is normal, continue with individual hardware updates
            }
            catch (Exception ex) when (ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
                                        ex.Message.Contains("SafeFileHandle", StringComparison.OrdinalIgnoreCase))
            {
                // Also catch nested disposed errors
            }
            
            int hardwareUpdated = 0;
            bool cpuHardwareUpdateSucceeded = false;
            
            foreach (var hardware in _computer.Hardware)
            {
                try
                {
                    // CRITICAL: Isolate each hardware device update in its own try-catch
                    // If storage drives go to sleep, their SafeFileHandle gets disposed,
                    // but we must not let that crash CPU/GPU monitoring
                    
                    // First, try to update the hardware device itself
                    try
                    {
                        hardware.Update();
                    }
                    catch (ObjectDisposedException) when (hardware.HardwareType == HardwareType.Storage)
                    {
                        // Storage drive went to sleep - this is NORMAL, skip this device
                        continue;
                    }
                    catch (Exception ex) when ((hardware.HardwareType == HardwareType.Storage) &&
                                               (ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
                                                ex.Message.Contains("SafeFileHandle", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Storage SafeFileHandle disposed - skip this device
                        continue;
                    }
                    
                    // Process the hardware data (CPU, GPU, RAM, etc.)
                    ProcessHardware(hardware, sample);
                    
                    // Update sub-hardware (fans, sensors)
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        try
                        {
                            subHardware.Update();
                            ProcessSubHardware(subHardware, sample);
                        }
                        catch (ObjectDisposedException)
                        {
                            // Sub-device went to sleep - skip it
                            continue;
                        }
                        catch (Exception ex) when (ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("SafeFileHandle", StringComparison.OrdinalIgnoreCase))
                        {
                            // Sub-device SafeFileHandle disposed - skip it
                            continue;
                        }
                    }
                    
                    hardwareUpdated++;
                    
                    // Track if CPU hardware update succeeded (not just temp changed)
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        cpuHardwareUpdateSucceeded = true;
                    }
                }
                catch (Exception ex)
                {
                    // Filter out known benign errors
                    var isDisposedError = ex is ObjectDisposedException || 
                                          ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
                                          ex.Message.Contains("SafeFileHandle", StringComparison.OrdinalIgnoreCase);
                    
                    // Only log non-disposed errors, or disposed errors once per hardware per hour
                    var errorKey = $"{hardware.Name}:{ex.GetType().Name}";
                    var shouldLog = !isDisposedError;
                    
                    if (isDisposedError)
                    {
                        // Rate-limit disposed errors (drives going to sleep is normal)
                        if (!_lastErrorLog.TryGetValue(errorKey, out var lastLog) || 
                            DateTime.Now - lastLog > TimeSpan.FromHours(1))
                        {
                            _lastErrorLog[errorKey] = DateTime.Now;
                            shouldLog = true;
                        }
                    }
                    
                    if (shouldLog)
                    {
                        File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Error updating {hardware.Name}: {ex.Message}\n");
                    }
                    
                    // CRITICAL: Even if this hardware device failed, continue to next device
                    // This prevents storage failures from stopping CPU/GPU monitoring
                }
            }
            
            // Detect stale data - if CPU hardware update failed (not just temp unchanged), mark as stale
            // Note: Stable temps during idle are NORMAL, don't flag as stale just because temp didn't change
            if (!cpuHardwareUpdateSucceeded && sample.CpuTemperature > 0)
            {
                sample.StaleCount = _lastSample.StaleCount + 1;
                
                // If CPU update failed for 20+ cycles (about 30+ seconds), mark as not fresh
                if (sample.StaleCount >= 20)
                {
                    sample.IsFresh = false;
                    
                    // Log once when we first detect staleness
                    if (_lastSample.IsFresh)
                    {
                        File.AppendAllText(GetLogPath(), 
                            $"[{DateTime.Now:O}] ⚠️ CPU hardware update failed for {sample.StaleCount} cycles (temp={sample.CpuTemperature:F1}°C). Reinitializing sensors...\n");
                        
                        // Try to reinitialize CPU sensors
                        try
                        {
                            var cpuHw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                            if (cpuHw != null)
                            {
                                cpuHw.Update();
                            }
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // CPU hardware updated successfully - reset stale count even if temp unchanged
                sample.StaleCount = 0;
                sample.IsFresh = true;
            }
            
            sample.Timestamp = DateTime.Now;
            _lastSample = sample;
            _lastUpdate = DateTime.Now;
            
            // Mark as initialized after first update cycle completes
            if (!_hasInitialized && sample.CpuTemperature > 0)
            {
                _hasInitialized = true;
                Console.WriteLine("[Worker] Hardware monitoring initialized - first sample complete");
            }
        }
    }

    private static void ProcessHardware(IHardware hardware, HardwareSample sample)
    {
        switch (hardware.HardwareType)
        {
            case HardwareType.Cpu:
                // CPU Temperature - prioritize stable sensors over individual cores
                var cpuTemp = GetSensorValueMulti(hardware, SensorType.Temperature, 
                    "CPU Package", "Package",                                    // Intel Package (most stable)
                    "Core (Tctl/Tdie)", "Tctl/Tdie",                          // AMD Ryzen primary
                    "Core Max", "Core Average",                               // Stable aggregates
                    "Core #1", "Core #0",                                    // Individual cores (fallback)
                    "Tctl", "Tdie",                                          // AMD variants
                    "CPU (Tctl/Tdie)",                                       // AMD Ryzen variant
                    "CCD1 (Tdie)", "CCD 1 (Tdie)",                          // AMD CCD with Tdie
                    "CCD1", "CCD 1",                                        // AMD CCD fallback
                    "CCDs Max", "CCDs Average",                             // AMD multi-CCD
                    "CPU", "SoC", "Socket");                                // Generic fallbacks
                
                // PawnIO fallback: If LibreHardwareMonitor returns 0 (WinRing0 blocked by Defender),
                // try reading CPU temp via PawnIO MSR instead
                if (cpuTemp <= 0 && _pawnIOCpuTemp != null && _pawnIOCpuTemp.IsAvailable)
                {
                    _consecutiveNullCpuTemp++;
                    
                    if (_consecutiveNullCpuTemp >= NullTempThresholdForFallback)
                    {
                        if (!_pawnIOFallbackActive)
                        {
                            _pawnIOFallbackActive = true;
                            File.AppendAllText(GetLogPath(), 
                                $"[{DateTime.Now:O}] ⚠️ LibreHardwareMonitor CPU temp null for {_consecutiveNullCpuTemp} readings. " +
                                $"Switching to PawnIO fallback (WinRing0 likely blocked by Defender).\n");
                            Console.WriteLine("Switching to PawnIO CPU temp fallback");
                        }
                        
                        try
                        {
                            cpuTemp = _pawnIOCpuTemp.ReadCpuTemperature();
                            if (cpuTemp > 0)
                            {
                                Console.WriteLine($"CPU Temp (PawnIO): {cpuTemp}°C");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Rate-limit PawnIO error logging
                            var errorKey = "PawnIO_CpuTemp";
                            if (!_lastErrorLog.TryGetValue(errorKey, out var lastLog) || 
                                DateTime.Now - lastLog > TimeSpan.FromMinutes(5))
                            {
                                _lastErrorLog[errorKey] = DateTime.Now;
                                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] PawnIO CPU temp read failed: {ex.Message}\n");
                            }
                        }
                    }
                }
                else if (cpuTemp > 0)
                {
                    // LibreHardwareMonitor working again - reset fallback state
                    if (_pawnIOFallbackActive)
                    {
                        _pawnIOFallbackActive = false;
                        File.AppendAllText(GetLogPath(), 
                            $"[{DateTime.Now:O}] ✓ LibreHardwareMonitor CPU temp restored. Disabling PawnIO fallback.\n");
                    }
                    _consecutiveNullCpuTemp = 0;
                }
                
                // Always update temperature, even if 0 (indicates sensor failure/unavailable)
                // This prevents stuck readings when sensors become temporarily unavailable
                sample.CpuTemperature = cpuTemp;
                Console.WriteLine($"CPU Temp: {cpuTemp}°C");
                
                var cpuLoad = GetSensorValue(hardware, SensorType.Load, "CPU Total");
                // Always assign load - 0 is a valid idle reading
                sample.CpuLoad = cpuLoad;
                
                var cpuPower = GetSensorValueMulti(hardware, SensorType.Power, 
                    "CPU Package", "Package Power");
                if (cpuPower > 0) sample.CpuPower = cpuPower;
                
                // Collect CPU core clocks
                var coreClocks = hardware.Sensors
                    .Where(s => s.SensorType == SensorType.Clock && 
                           (s.Name.StartsWith("CPU Core #") || s.Name.StartsWith("Core #")))
                    .Select(s => (double)(s.Value ?? 0))
                    .Where(v => v > 0)
                    .ToList();
                if (coreClocks.Count > 0)
                {
                    sample.CpuCoreClocks = coreClocks;
                }
                break;
                
            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
                // GPU Temperature - prefer Core over Hotspot (more stable)
                var gpuTemp = GetSensorValueMulti(hardware, SensorType.Temperature, 
                    "GPU Core", "Core");
                // Always update temperature, even if 0 (indicates sensor failure/unavailable)
                sample.GpuTemperature = gpuTemp;
                Console.WriteLine($"GPU Temp: {gpuTemp}°C");
                
                var gpuHotspot = GetSensorValueMulti(hardware, SensorType.Temperature, 
                    "GPU Hot Spot", "Hot Spot");
                // Always update hotspot, even if 0
                sample.GpuHotspot = gpuHotspot;
                
                var gpuLoad = GetSensorValueMulti(hardware, SensorType.Load, "GPU Core");
                // Always assign load - 0 is a valid idle reading
                sample.GpuLoad = gpuLoad;
                
                var gpuPower = GetSensorValueMulti(hardware, SensorType.Power, "GPU Power");
                if (gpuPower > 0) sample.GpuPower = gpuPower;
                
                var gpuVoltage = GetSensorValueMulti(hardware, SensorType.Voltage, "GPU Core");
                if (gpuVoltage > 0) sample.GpuVoltage = gpuVoltage;
                
                var gpuCurrent = GetSensorValueMulti(hardware, SensorType.Current, "GPU Core");
                if (gpuCurrent > 0) sample.GpuCurrent = gpuCurrent;
                
                var gpuClock = GetSensorValue(hardware, SensorType.Clock, "GPU Core");
                if (gpuClock > 0) sample.GpuClock = gpuClock;
                
                var gpuMemClock = GetSensorValue(hardware, SensorType.Clock, "GPU Memory");
                if (gpuMemClock > 0) sample.GpuMemoryClock = gpuMemClock;
                
                var vramUsed = GetSensorValueMulti(hardware, SensorType.SmallData, 
                    "GPU Memory Used", "D3D Dedicated Memory Used");
                if (vramUsed > 0) sample.VramUsage = vramUsed;
                
                var vramTotal = GetSensorValue(hardware, SensorType.SmallData, "GPU Memory Total");
                if (vramTotal > 0) sample.VramTotal = vramTotal;
                
                sample.GpuName = hardware.Name;
                break;
                
            case HardwareType.GpuIntel:
                // Intel Arc (dedicated) or integrated
                bool isArc = hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                
                // Only override if Arc or no dedicated GPU found
                if (isArc || sample.GpuTemperature == 0)
                {
                    var intelTemp = GetSensorValueMulti(hardware, SensorType.Temperature, 
                        "GPU Core", "GPU Package", "GPU");
                    // Always update temperature, even if 0
                    sample.GpuTemperature = intelTemp;
                    Console.WriteLine($"Intel GPU Temp: {intelTemp}°C");
                    
                    if (isArc || string.IsNullOrEmpty(sample.GpuName))
                        sample.GpuName = hardware.Name;
                }
                
                if (isArc || sample.GpuLoad == 0)
                {
                    var intelLoad = GetSensorValueMulti(hardware, SensorType.Load, "GPU Core", "D3D 3D");
                    if (intelLoad > 0) sample.GpuLoad = intelLoad;
                }
                
                // Intel Arc power and clock metrics
                if (isArc)
                {
                    var arcPower = GetSensorValueMulti(hardware, SensorType.Power, 
                        "GPU Power", "GPU Package");
                    if (arcPower > 0) sample.GpuPower = arcPower;
                    
                    var arcClock = GetSensorValue(hardware, SensorType.Clock, "GPU Core");
                    if (arcClock > 0) sample.GpuClock = arcClock;
                    
                    var arcMemClock = GetSensorValue(hardware, SensorType.Clock, "GPU Memory");
                    if (arcMemClock > 0) sample.GpuMemoryClock = arcMemClock;
                }
                break;
                
            case HardwareType.Memory:
                var ramUsed = GetSensorValue(hardware, SensorType.Data, "Memory Used");
                var ramAvail = GetSensorValue(hardware, SensorType.Data, "Memory Available");
                
                // LibreHardwareMonitor sometimes returns garbage values (e.g. 16MB instead of 16GB)
                // Use WMI fallback if values are unreasonable (< 1GB total is clearly wrong)
                var sensorTotal = ramUsed + ramAvail;
                if (sensorTotal >= 1.0) // At least 1 GB - seems valid
                {
                    sample.RamUsage = ramUsed;
                    sample.RamTotal = sensorTotal;
                }
                
                // Always use WMI if sensor data is missing or unreasonable
                if (sample.RamTotal < 1.0)
                {
                    var (wmiTotal, wmiUsed) = GetRamFromWmi();
                    if (wmiTotal > 0)
                    {
                        sample.RamTotal = wmiTotal;
                        sample.RamUsage = wmiUsed > 0 ? wmiUsed : wmiTotal * 0.5; // Estimate 50% if usage unknown
                    }
                }
                break;
                
            case HardwareType.Storage:
                if (hardware.Name.Contains("NVMe") || hardware.Name.Contains("SSD"))
                {
                    var ssdTemp = GetSensorValue(hardware, SensorType.Temperature);
                    if (ssdTemp > 0 && sample.SsdTemperature == 0) sample.SsdTemperature = ssdTemp;
                }
                break;
                
            case HardwareType.Battery:
                var charge = GetSensorValueMulti(hardware, SensorType.Level, "Charge Level");
                if (charge > 0) sample.BatteryCharge = charge;
                
                var discharge = GetSensorValueMulti(hardware, SensorType.Power, "Discharge Rate");
                sample.BatteryDischargeRate = discharge;
                sample.IsOnAc = discharge <= 0;
                break;
        }
    }

    private static void ProcessSubHardware(IHardware subHardware, HardwareSample sample)
    {
        // Fan RPM from EC/motherboard
        foreach (var sensor in subHardware.Sensors.Where(s => s.SensorType == SensorType.Fan))
        {
            if (sensor.Value.HasValue && sensor.Value.Value > 0)
            {
                sample.FanSpeeds[sensor.Name] = sensor.Value.Value;
            }
        }
    }

    private static double GetSensorValue(IHardware hardware, SensorType type, string? name = null)
    {
        ISensor? sensor;
        if (name != null)
        {
            sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type);
        }
        
        return sensor?.Value ?? 0;
    }
    
    /// <summary>
    /// Get sensor value, trying multiple name patterns
    /// </summary>
    private static double GetSensorValueMulti(IHardware hardware, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (sensor != null)
            {
                return sensor.Value ?? 0;  // Return actual value even if 0
            }
        }
        // Fallback to any sensor of this type
        var fallback = hardware.Sensors.FirstOrDefault(s => s.SensorType == type);
        return fallback?.Value ?? 0;
    }

    /// <summary>
    /// Get RAM information directly from WMI when LibreHardwareMonitor sensors unavailable.
    /// This fixes the "0/0 GB" RAM display issue on some systems.
    /// </summary>
    private static (double TotalGb, double UsedGb) GetRamFromWmi()
    {
        try
        {
            double totalGb = 0;
            double freeGb = 0;
            
            // Get total RAM from Win32_ComputerSystem
            using (var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    totalGb = bytes / 1024.0 / 1024.0 / 1024.0;
                    break;
                }
            }
            
            // Get free RAM from Win32_OperatingSystem
            using (var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var freeKb = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                    freeGb = freeKb / 1024.0 / 1024.0;
                    break;
                }
            }
            
            double usedGb = totalGb > 0 ? totalGb - freeGb : 0;
            return (totalGb, usedGb);
        }
        catch
        {
            return (16, 8); // Default assumption: 16 GB total, 8 GB used
        }
    }

    private static async Task RunPipeServer()
    {
        Console.WriteLine($"Starting named pipe server: {PipeName}");
        
        while (_running)
        {
            try
            {
                // CurrentUserOnly restricts the pipe to the current user session for security (audit_1 critical #2)
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                
                Console.WriteLine("Waiting for client connection...");
                await server.WaitForConnectionAsync();
                Console.WriteLine("Client connected.");
                _lastClientActivity = DateTime.Now;
                
                await HandleClient(server);
                
                // Client disconnected — update activity timestamp
                // so orphan watchdog starts counting from NOW, not from last request
                _lastClientActivity = DateTime.Now;
                Console.WriteLine("Client session ended. Waiting for next connection...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipe error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private static async Task HandleClient(NamedPipeServerStream server)
    {
        var buffer = new byte[4096];
        
        try
        {
            while (server.IsConnected && _running)
            {
                // Read request
                var bytesRead = await server.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                
                var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                _lastClientActivity = DateTime.Now;
                
                string response;
                if (request == "PING")
                {
                    response = "PONG";
                }
                else if (request == "GET")
                {
                    lock (_lock)
                    {
                        // Mark sample as stale if we haven't initialized yet (first update not complete)
                        if (!_hasInitialized)
                        {
                            _lastSample.IsFresh = false;
                            _lastSample.StaleCount = 999;  // High stale count to signal "not initialized"
                        }
                        response = JsonSerializer.Serialize(_lastSample);
                    }
                }
                else if (request.StartsWith("SET_PARENT ", StringComparison.Ordinal))
                {
                    // New OmenCore instance registering as parent
                    var pidStr = request.Substring("SET_PARENT ".Length).Trim();
                    if (int.TryParse(pidStr, out var newPid) && newPid > 0)
                    {
                        var oldPid = _parentProcessId;
                        _parentProcessId = newPid;
                        _parentAlive = true;
                        
                        Console.WriteLine($"New parent registered: PID {newPid} (was {oldPid})");
                        File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] New parent registered: PID {newPid} (was {oldPid}). Worker reattached.\n");
                        
                        // Start monitoring new parent in background
                        _ = Task.Run(MonitorParentProcess);
                        
                        response = "OK";
                    }
                    else
                    {
                        response = "ERROR:INVALID_PID";
                    }
                }
                else if (request == "SHUTDOWN")
                {
                    response = "OK";
                    _running = false;
                }
                else
                {
                    response = "UNKNOWN";
                }
                
                // Send response
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await server.WriteAsync(responseBytes, 0, responseBytes.Length);
                await server.FlushAsync();
            }
        }
        catch (IOException)
        {
            // Client disconnected
            Console.WriteLine("Client disconnected.");
        }
    }
}

/// <summary>
/// Visitor pattern for LibreHardwareMonitor updates
/// </summary>
internal class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        try
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }
        catch (ObjectDisposedException)
        {
            // Hardware went to sleep (storage drives) - skip this hardware
        }
        catch (Exception ex) when (ex.Message.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("SafeFileHandle", StringComparison.OrdinalIgnoreCase))
        {
            // Also catch nested disposed errors from storage drives
        }
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

/// <summary>
/// Hardware sample data transferred via IPC
/// </summary>
public class HardwareSample
{
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Indicates if the sample contains fresh data from hardware sensors.
    /// False if sensors could not be read (stale data from last successful read).
    /// </summary>
    public bool IsFresh { get; set; } = true;
    
    /// <summary>
    /// Number of consecutive stale readings (for client-side staleness detection)
    /// </summary>
    public int StaleCount { get; set; } = 0;
    
    // CPU
    public double CpuTemperature { get; set; }
    public double CpuLoad { get; set; }
    public double CpuPower { get; set; }
    public List<double> CpuCoreClocks { get; set; } = new();
    
    // GPU
    public string GpuName { get; set; } = "";
    public double GpuTemperature { get; set; }
    public double GpuHotspot { get; set; }
    public double GpuLoad { get; set; }
    public double GpuPower { get; set; }
    public double GpuClock { get; set; }
    public double GpuMemoryClock { get; set; }
    public double GpuVoltage { get; set; }
    public double GpuCurrent { get; set; }
    public double VramUsage { get; set; }
    public double VramTotal { get; set; }
    
    // Memory
    public double RamUsage { get; set; }
    public double RamTotal { get; set; }
    
    // Storage
    public double SsdTemperature { get; set; }
    
    // Battery
    public double BatteryCharge { get; set; }
    public double BatteryDischargeRate { get; set; }
    public bool IsOnAc { get; set; } = true;
    
    // Fans (name -> RPM)
    public Dictionary<string, double> FanSpeeds { get; set; } = new();
}

/// <summary>
/// PawnIO-based CPU temperature reader as fallback when LibreHardwareMonitor fails.
/// LibreHardwareMonitor uses WinRing0 which Windows Defender often blocks.
/// PawnIO is a signed driver that works with Secure Boot and Defender.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PawnIOCpuTemp : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private IntPtr _pawnIOLib = IntPtr.Zero;
    private bool _moduleLoaded;
    private bool _disposed;
    private int _tjMax = 100; // Default TjMax, will try to detect actual value
    
    // MSR addresses
    private const uint MSR_IA32_THERM_STATUS = 0x19C;      // Per-core temperature
    private const uint MSR_IA32_TEMPERATURE_TARGET = 0x1A2; // TjMax
    private const uint MSR_IA32_PACKAGE_THERM_STATUS = 0x1B1; // Package temperature (Intel only)
    
    // Embedded IntelMSR module binary
    private static byte[]? _intelMsrModule;
    
    // Function delegates
    private delegate int PawnioOpen(out IntPtr handle);
    private delegate int PawnioLoad(IntPtr handle, byte[] blob, IntPtr size);
    private delegate int PawnioExecute(IntPtr handle, string name, ulong[] input, IntPtr inSize, ulong[] output, IntPtr outSize, out IntPtr returnSize);
    private delegate int PawnioClose(IntPtr handle);
    
    private PawnioOpen? _pawnioOpen;
    private PawnioLoad? _pawnioLoad;
    private PawnioExecute? _pawnioExecute;
    private PawnioClose? _pawnioClose;
    
    public bool IsAvailable => _handle != IntPtr.Zero && _moduleLoaded;
    
    public PawnIOCpuTemp()
    {
        Initialize();
    }
    
    private bool Initialize()
    {
        try
        {
            // Try bundled PawnIOLib.dll first (in drivers folder next to exe)
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundledLibPath = Path.Combine(appDir, "drivers", "PawnIOLib.dll");
            string? libPath = null;
            
            if (File.Exists(bundledLibPath))
            {
                libPath = bundledLibPath;
            }
            else
            {
                // Fall back to PawnIO installation
                string? pawnIOPath = FindPawnIOInstallation();
                if (pawnIOPath != null)
                {
                    string installedLibPath = Path.Combine(pawnIOPath, "PawnIOLib.dll");
                    if (File.Exists(installedLibPath))
                    {
                        libPath = installedLibPath;
                    }
                }
            }
            
            if (libPath == null) return false;
            
            _pawnIOLib = NativeMethods.LoadLibrary(libPath);
            if (_pawnIOLib == IntPtr.Zero) return false;
            
            // Resolve functions
            if (!ResolveFunctions()) return false;
            
            // Open PawnIO handle
            int hr = _pawnioOpen!(out _handle);
            if (hr < 0 || _handle == IntPtr.Zero) return false;
            
            // Load IntelMSR module
            if (!LoadMsrModule())
            {
                _pawnioClose!(_handle);
                _handle = IntPtr.Zero;
                return false;
            }
            
            _moduleLoaded = true;
            
            // Try to detect TjMax
            try
            {
                _tjMax = ReadTjMax();
            }
            catch
            {
                _tjMax = 100; // Default for most Intel CPUs
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private string? FindPawnIOInstallation()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if (key != null)
            {
                string? installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                {
                    return installLocation;
                }
            }
        }
        catch { }
        
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
        if (Directory.Exists(defaultPath)) return defaultPath;
        
        return null;
    }
    
    private bool ResolveFunctions()
    {
        IntPtr openPtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_open");
        IntPtr loadPtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_load");
        IntPtr executePtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_execute");
        IntPtr closePtr = NativeMethods.GetProcAddress(_pawnIOLib, "pawnio_close");
        
        if (openPtr == IntPtr.Zero || loadPtr == IntPtr.Zero || 
            executePtr == IntPtr.Zero || closePtr == IntPtr.Zero)
        {
            return false;
        }
        
        _pawnioOpen = Marshal.GetDelegateForFunctionPointer<PawnioOpen>(openPtr);
        _pawnioLoad = Marshal.GetDelegateForFunctionPointer<PawnioLoad>(loadPtr);
        _pawnioExecute = Marshal.GetDelegateForFunctionPointer<PawnioExecute>(executePtr);
        _pawnioClose = Marshal.GetDelegateForFunctionPointer<PawnioClose>(closePtr);
        
        return true;
    }
    
    private bool LoadMsrModule()
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] moduleNames = { "IntelMSR.bin", "IntelMSR.amx" };
            
            foreach (var moduleName in moduleNames)
            {
                string modulePath = Path.Combine(appDir, "drivers", moduleName);
                if (File.Exists(modulePath))
                {
                    _intelMsrModule = File.ReadAllBytes(modulePath);
                    break;
                }
            }
            
            if (_intelMsrModule == null || _intelMsrModule.Length == 0)
            {
                string? pawnIOPath = FindPawnIOInstallation();
                if (pawnIOPath != null)
                {
                    foreach (var moduleName in moduleNames)
                    {
                        string installedModule = Path.Combine(pawnIOPath, "modules", moduleName);
                        if (File.Exists(installedModule))
                        {
                            _intelMsrModule = File.ReadAllBytes(installedModule);
                            break;
                        }
                    }
                }
            }
            
            if (_intelMsrModule == null || _intelMsrModule.Length == 0) return false;
            
            int hr = _pawnioLoad!(_handle, _intelMsrModule, (IntPtr)_intelMsrModule.Length);
            return hr >= 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Read CPU temperature using MSR 0x19C (IA32_THERM_STATUS) or 0x1B1 (Package).
    /// Returns temperature in Celsius.
    /// </summary>
    public double ReadCpuTemperature()
    {
        if (!IsAvailable) return 0;
        
        try
        {
            // Try package temperature first (MSR 0x1B1) - more stable than per-core
            ulong pkgTherm = ReadMsr(MSR_IA32_PACKAGE_THERM_STATUS);
            
            // Check if reading is valid (bit 31 = Reading Valid)
            if ((pkgTherm & 0x80000000) != 0)
            {
                // Digital Readout is bits 22:16 (7 bits)
                int digitalReadout = (int)((pkgTherm >> 16) & 0x7F);
                double temp = _tjMax - digitalReadout;
                if (temp > 0 && temp < 150) return temp;
            }
            
            // Fallback to core 0 temperature (MSR 0x19C)
            ulong thermStatus = ReadMsr(MSR_IA32_THERM_STATUS);
            
            // Check if reading is valid (bit 31 = Reading Valid)
            if ((thermStatus & 0x80000000) != 0)
            {
                // Digital Readout is bits 22:16 (7 bits)
                int digitalReadout = (int)((thermStatus >> 16) & 0x7F);
                double temp = _tjMax - digitalReadout;
                if (temp > 0 && temp < 150) return temp;
            }
            
            return 0;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Read TjMax from MSR 0x1A2 (IA32_TEMPERATURE_TARGET).
    /// TjMax is the maximum junction temperature - actual temp = TjMax - digital readout.
    /// </summary>
    private int ReadTjMax()
    {
        try
        {
            ulong value = ReadMsr(MSR_IA32_TEMPERATURE_TARGET);
            // TjMax is in bits 23:16
            int tjMax = (int)((value >> 16) & 0xFF);
            if (tjMax > 50 && tjMax < 150) return tjMax;
        }
        catch { }
        
        return 100; // Default for most Intel CPUs
    }
    
    private ulong ReadMsr(uint index)
    {
        ulong[] input = { index };
        ulong[] output = new ulong[2]; // low, high
        
        int hr = _pawnioExecute!(_handle, "ioctl_msr_read", input, (IntPtr)1, output, (IntPtr)2, out IntPtr returnSize);
        if (hr < 0)
        {
            throw new InvalidOperationException($"PawnIO MSR read failed: HRESULT 0x{hr:X8}");
        }
        
        return output[0] | (output[1] << 32);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_handle != IntPtr.Zero && _pawnioClose != null)
        {
            try { _pawnioClose(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
        
        if (_pawnIOLib != IntPtr.Zero)
        {
            try { NativeMethods.FreeLibrary(_pawnIOLib); } catch { }
            _pawnIOLib = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Native methods for PawnIO library loading
/// </summary>
internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}
