using System.IO.Pipes;
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
    
    private static Computer? _computer;
    private static readonly object _lock = new();
    private static HardwareSample _lastSample = new();
    private static bool _running = true;
    private static DateTime _lastUpdate = DateTime.MinValue;
    private static int _parentProcessId = -1;
    private static readonly Dictionary<string, DateTime> _lastErrorLog = new(); // Rate-limit error logging

    static async Task Main(string[] args)
    {
        // Parse parent process ID from command line (passed by main app)
        if (args.Length > 0 && int.TryParse(args[0], out var ppid))
        {
            _parentProcessId = ppid;
            Console.WriteLine($"Parent process ID: {_parentProcessId}");
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
            
            // Start parent process monitor (exits worker if parent dies)
            var parentMonitorTask = Task.Run(MonitorParentProcess);
            
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
                Console.WriteLine($"Parent process {_parentProcessId} exited, shutting down worker...");
                File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent process {_parentProcessId} exited, worker shutting down\n");
                _running = false;
                Environment.Exit(0);
            }
        }
        catch (ArgumentException)
        {
            // Parent process doesn't exist (already exited)
            Console.WriteLine($"Parent process {_parentProcessId} not found, shutting down worker...");
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent process {_parentProcessId} not found, worker shutting down\n");
            _running = false;
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            File.AppendAllText(GetLogPath(), $"[{DateTime.Now:O}] Parent monitor error: {ex.Message}\n");
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
        
        // Log detected GPUs for diagnostics
        LogDetectedGpus();
    }
    
    private static void LogDetectedGpus()
    {
        if (_computer?.Hardware == null) return;
        
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
                var logPath = GetLogPath();
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] [GPU Detected] {gpuType}: {hw.Name}\n");
                File.AppendAllText(logPath, $"  Temp sensors: [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]\n");
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
                    hardware.Update();
                    ProcessHardware(hardware, sample);
                    
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        ProcessSubHardware(subHardware, sample);
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
                
                // Always update temperature, even if 0 (indicates sensor failure/unavailable)
                // This prevents stuck readings when sensors become temporarily unavailable
                sample.CpuTemperature = cpuTemp;
                
                var cpuLoad = GetSensorValue(hardware, SensorType.Load, "CPU Total");
                if (cpuLoad > 0) sample.CpuLoad = cpuLoad;
                
                var cpuPower = GetSensorValueMulti(hardware, SensorType.Power, 
                    "CPU Package", "Package Power");
                if (cpuPower > 0) sample.CpuPower = cpuPower;
                break;
                
            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
                // GPU Temperature - prefer Core over Hotspot (more stable)
                var gpuTemp = GetSensorValueMulti(hardware, SensorType.Temperature, 
                    "GPU Core", "Core");
                if (gpuTemp > 0) sample.GpuTemperature = gpuTemp;
                
                var gpuHotspot = GetSensorValueMulti(hardware, SensorType.Temperature, 
                    "GPU Hot Spot", "Hot Spot");
                if (gpuHotspot > 0) sample.GpuHotspot = gpuHotspot;
                
                var gpuLoad = GetSensorValueMulti(hardware, SensorType.Load, "GPU Core");
                if (gpuLoad > 0) sample.GpuLoad = gpuLoad;
                
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
                    if (intelTemp > 0) sample.GpuTemperature = intelTemp;
                    
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
                if (ramUsed > 0) sample.RamUsage = ramUsed;
                if (ramUsed > 0 && ramAvail > 0) sample.RamTotal = ramUsed + ramAvail;
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
            var value = GetSensorValue(hardware, type, name);
            if (value > 0) return value;
        }
        // Fallback to any sensor of this type
        return GetSensorValue(hardware, type);
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
                
                await HandleClient(server);
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
                
                string response;
                if (request == "PING")
                {
                    response = "PONG";
                }
                else if (request == "GET")
                {
                    lock (_lock)
                    {
                        response = JsonSerializer.Serialize(_lastSample);
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
