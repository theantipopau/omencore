using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.IO;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Real LibreHardwareMonitor implementation - integrates with actual hardware sensors.
    /// Uses LibreHardwareMonitorLib NuGet package for accurate hardware monitoring.
    /// 
    /// Supports two modes:
    /// 1. In-process (default): Direct LibreHardwareMonitor calls. Fast but can crash from NVML issues.
    /// 2. Out-of-process: Uses HardwareWorker process via IPC. Crash-proof but slightly slower.
    /// 
    /// Performance considerations:
    /// - Hardware updates cause kernel driver calls which can trigger DPC latency
    /// - The _updateInterval controls minimum time between hardware updates
    /// - Low overhead mode extends cache lifetime to reduce hardware polling
    /// </summary>
    public class LibreHardwareMonitorImpl : IHardwareMonitorBridge, IDisposable
    {
        private Computer? _computer;
        private bool _initialized;
        private readonly object _lock = new();
        
        // Out-of-process worker for crash isolation
        private HardwareWorkerClient? _workerClient;
        private bool _useWorker = false;
        private volatile bool _workerInitializing = false; // Track async initialization state (volatile for thread safety)
        private bool _disableBatteryMonitoring = false; // Dead battery protection

        // Cache for performance
        private double _cachedCpuTemp = 0;
        private double _cachedGpuTemp = 0;
        private double _cachedCpuLoad = 0;
        private double _cachedGpuLoad = 0;
        private double _cachedCpuPower = 0;
        private double _cachedVramUsage = 0;
        private double _cachedRamUsage = 0;
        private double _cachedRamTotal = 0;
        private double _cachedFanRpm = 0;
        private double _cachedSsdTemp = 0;
        private double _cachedDiskUsage = 0;
        private double _cachedBatteryCharge = 100;
        private bool _cachedIsOnAc = true;
        private double _cachedDischargeRate = 0;
        private string _cachedBatteryTimeRemaining = "";
        private readonly List<double> _cachedCoreClocks = new();
        private DateTime _lastUpdate = DateTime.MinValue;
        private TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(100);
        private string _lastGpuName = string.Empty;
        
        // Enhanced GPU metrics (v1.1)
        private double _cachedGpuPower = 0;
        private double _cachedGpuClock = 0;
        private double _cachedGpuMemoryClock = 0;
        private double _cachedGpuVoltage = 0;
        private double _cachedGpuCurrent = 0;
        private double _cachedVramTotal = 0;
        private double _cachedGpuFan = 0;
        private double _cachedGpuHotspot = 0;
        
        // Throttling detection (v1.2)
        private bool _cachedCpuThermalThrottling = false;
        private bool _cachedCpuPowerThrottling = false;
        private bool _cachedGpuThermalThrottling = false;
        private bool _cachedGpuPowerThrottling = false;
        
        // Throttling thresholds
        private const double CpuThermalThrottleThreshold = 95.0; // Most CPUs throttle around 95-100°C
        private const double GpuThermalThrottleThreshold = 83.0; // NVIDIA throttles around 83°C
        
        private IMsrAccess? _msrAccess;
        
        // DPC latency mitigation
        private bool _lowOverheadMode = false;
        private static readonly TimeSpan _normalCacheLifetime = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan _lowOverheadCacheLifetime = TimeSpan.FromMilliseconds(3000); // 3 seconds in low overhead

        private readonly Action<string>? _logger;
        // Temperature smoothing
        private readonly List<double> _cpuTempHistory = new();
        private const int CpuTempSmoothingWindow = 3; // 3 readings average
        private double _smoothedCpuTemp = 0;
        private int _consecutiveZeroTempReadings = 0;
        private const int MaxZeroTempReadingsBeforeReinit = 6;
        private bool _noFanSensorsLogged = false;
        
        // BUG FIX #36: Track stuck-at-TjMax temperature readings
        private int _stuckTempReadings = 0;
        
        // BUG FIX v2.6.1: Track stuck temperature readings (any value) with improved thresholds
        private double _lastCpuTempReading = 0;
        private int _consecutiveSameTempReadings = 0;
        private const int MaxSameTempReadingsBeforeLog = 5; // 5 identical readings = stuck sensor (10s @ 2s poll)
        private const int MaxSameTempReadingsBeforeReinit = 10; // After trying alternatives, reinitialize (20s @ 2s poll)
        private int _reinitializeAttempts = 0;
        private const int MaxReinitializeAttempts = 3; // After 3 failed reinits, switch to WMI-only mode
        private bool _forceWmiBiosMode = false; // If true, skip LHM and use WMI BIOS only for temps
        
        // GPU stuck detection
        private double _lastGpuTempReading = 0;
        private int _consecutiveSameGpuTempReadings = 0;

        // PawnIO CPU temperature fallback
        private PawnIOCpuTemp? _pawnIoCpuTemp;

        // System RAM total for fallback when sensors are missing
        private double _systemRamTotalGb = 0;

        /// <summary>
        /// Create an in-process hardware monitor (default).
        /// May crash from NVML issues during heavy GPU load.
        /// </summary>
        public LibreHardwareMonitorImpl(Action<string>? logger = null, IMsrAccess? msrAccess = null)
        {
            _logger = logger;
            _msrAccess = msrAccess;
            _useWorker = false;
            InitializePawnIO();
            InitializeComputer();
        }

        public string MonitoringSource
        {
            get
            {
                if (_forceWmiBiosMode)
                {
                    return "WMI BIOS (Fallback)";
                }

                if (_useWorker)
                {
                    if (_workerInitializing)
                    {
                        return "LibreHardwareMonitor (Worker Init)";
                    }

                    return _workerClient?.IsConnected == true
                        ? "LibreHardwareMonitor (Worker)"
                        : "LibreHardwareMonitor (Worker - Disconnected)";
                }

                return _initialized
                    ? "LibreHardwareMonitor (In-Process)"
                    : "LibreHardwareMonitor (Fallback)";
            }
        }
        
        /// <summary>
        /// Create a hardware monitor with optional out-of-process worker.
        /// When useWorker is true, hardware monitoring runs in a separate process
        /// that can crash without affecting the main application.
        /// </summary>
        /// <param name="logger">Logging callback</param>
        /// <param name="useWorker">Use out-of-process worker for crash isolation</param>
        /// <param name="msrAccess">Optional MSR access for enhanced throttling detection</param>
        public LibreHardwareMonitorImpl(Action<string>? logger, bool useWorker, IMsrAccess? msrAccess = null)
        {
            _logger = logger;
            _msrAccess = msrAccess;
            _useWorker = useWorker;
            InitializePawnIO();
            
            if (_useWorker)
            {
                InitializeWorker();
            }
            else
            {
                InitializeComputer();
            }
        }
        
        /// <summary>
        /// Disable battery monitoring in both in-process LHM and out-of-process worker.
        /// Call this for systems with dead/removed batteries to prevent EC timeout errors.
        /// </summary>
        public async Task DisableBatteryMonitoringAsync()
        {
            _disableBatteryMonitoring = true;
            
            // If in-process Computer is initialized, disable battery sensor
            if (_computer != null)
            {
                _computer.IsBatteryEnabled = false;
            }
            
            // If worker is connected, send disable command
            if (_workerClient != null)
            {
                await _workerClient.SendDisableBatteryAsync();
            }
        }
        
        private async Task InitializeWorkerAsync()
        {
            _workerInitializing = true;
            try
            {
                _workerClient = new HardwareWorkerClient(_logger);
                var started = await _workerClient.StartAsync();
                
                if (started)
                {
                    _logger?.Invoke("[Monitor] Using out-of-process hardware worker (crash-isolated)");
                    _initialized = true;
                    
                    // If battery monitoring was requested to be disabled, send command to worker
                    if (_disableBatteryMonitoring)
                    {
                        await _workerClient.SendDisableBatteryAsync();
                    }
                }
                else
                {
                    _logger?.Invoke("[Monitor] Worker failed to start, falling back to in-process");
                    _useWorker = false;
                    InitializeComputer();
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Monitor] Worker initialization failed: {ex.Message}, falling back to in-process");
                _useWorker = false;
                InitializeComputer();
            }
            finally
            {
                _workerInitializing = false;
            }
        }
        
        /// <summary>
        /// Fire-and-forget wrapper for InitializeWorkerAsync that handles exceptions safely.
        /// Used from constructor where we cannot await.
        /// </summary>
        private void InitializeWorker()
        {
            // Start initialization but don't block constructor
            // Exceptions are caught and logged within InitializeWorkerAsync
            _ = InitializeWorkerAsync();
        }
        
        /// <summary>
        /// Enable or disable low overhead mode.
        /// When enabled, hardware polling is significantly reduced to minimize DPC latency.
        /// </summary>
        public void SetLowOverheadMode(bool enabled)
        {
            _lowOverheadMode = enabled;
            _cacheLifetime = enabled ? _lowOverheadCacheLifetime : _normalCacheLifetime;
            _logger?.Invoke($"LibreHardwareMonitor low overhead mode: {enabled} (cache lifetime: {_cacheLifetime.TotalMilliseconds}ms)");
        }
        
        private void InitializeComputer()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsStorageEnabled = true,
                    IsControllerEnabled = true,
                    IsMotherboardEnabled = true,  // Required for laptop fan sensors (EC)
                    IsNetworkEnabled = false,
                    IsBatteryEnabled = true
                };
                _computer.Open();
                _initialized = true;
                _consecutiveZeroTempReadings = 0;
                
                // Cache system RAM total for fallback when sensors are missing
                _systemRamTotalGb = GetTotalPhysicalMemoryGB();
                
                _logger?.Invoke("LibreHardwareMonitor initialized successfully");
                
                // Log detected GPUs for diagnostic purposes
                LogDetectedGpus();
            }
            catch (Exception ex)
            {
                _initialized = false;
                _logger?.Invoke($"LibreHardwareMonitor init failed: {ex.Message}. Using WMI fallback.");
            }
        }
        
        private void InitializePawnIO()
        {
            try
            {
                _pawnIoCpuTemp = new PawnIOCpuTemp();
                if (_pawnIoCpuTemp.IsAvailable)
                {
                    _logger?.Invoke("PawnIO CPU temperature fallback initialized successfully");
                }
                else
                {
                    _logger?.Invoke("PawnIO CPU temperature fallback unavailable - will use LibreHardwareMonitor only");
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"PawnIO CPU temperature fallback initialization failed: {ex.Message}");
                _pawnIoCpuTemp = null;
            }
        }
        
        /// <summary>
        /// Log all detected GPUs and their sensor availability for diagnostics.
        /// </summary>
        private void LogDetectedGpus()
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
                    
                    _logger?.Invoke($"[GPU Detected] {gpuType}: {hw.Name}");
                    _logger?.Invoke($"  - Temp sensors: [{string.Join(", ", tempSensors.Select(s => $"{s.Name}={s.Value:F0}°C"))}]");
                    _logger?.Invoke($"  - Load sensors: [{string.Join(", ", loadSensors.Select(s => $"{s.Name}={s.Value:F0}%"))}]");
                    _logger?.Invoke($"  - Power sensors: [{string.Join(", ", powerSensors.Select(s => $"{s.Name}={s.Value:F1}W"))}]");
                }
            }
        }
        
        /// <summary>
        /// Reinitialize hardware monitor if sensors are returning 0.
        /// Useful after system resume from sleep or when sensors become stale.
        /// </summary>
        public void Reinitialize()
        {
            _logger?.Invoke("Reinitializing LibreHardwareMonitor...");
            try
            {
                _computer?.Close();
            }
            catch { /* Ignore close errors */ }
            
            InitializeComputer();
        }

        /// <summary>
        /// Attempt to restart the hardware monitoring bridge.
        /// Called by HardwareMonitoringService when consecutive timeouts indicate the bridge is stuck.
        /// For worker mode: restarts the out-of-process worker.
        /// For in-process mode: reinitializes LibreHardwareMonitor.
        /// </summary>
        public async Task<bool> TryRestartAsync()
        {
            _logger?.Invoke("[Monitor] TryRestartAsync called - attempting to restart hardware monitoring...");
            
            try
            {
                if (_useWorker && _workerClient != null)
                {
                    // Worker mode: try to restart the worker process
                    _logger?.Invoke("[Monitor] Restarting out-of-process worker...");
                    await _workerClient.StopAsync();
                    await Task.Delay(500); // Brief cooldown
                    var started = await _workerClient.StartAsync();
                    
                    if (started)
                    {
                        _logger?.Invoke("[Monitor] ✅ Worker restarted successfully");
                        _initialized = true;
                        return true;
                    }
                    else
                    {
                        _logger?.Invoke("[Monitor] ⚠️ Worker restart failed, falling back to in-process mode");
                        _useWorker = false;
                        Reinitialize();
                        return _initialized;
                    }
                }
                else
                {
                    // In-process mode: reinitialize
                    _logger?.Invoke("[Monitor] Reinitializing in-process hardware monitor...");
                    Reinitialize();
                    _logger?.Invoke($"[Monitor] Reinitialization complete, initialized={_initialized}");
                    return _initialized;
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Monitor] ❌ TryRestartAsync failed: {ex.Message}");
                return false;
            }
        }

        public async Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            // Wait for worker initialization to complete (prevent race condition)
            if (_workerInitializing)
            {
                _logger?.Invoke($"[Monitor] ReadSampleAsync: Worker is initializing, waiting...");
                // Worker is still starting up - wait longer
                for (int i = 0; i < 20 && _workerInitializing; i++)
                {
                    await Task.Delay(200, token);
                }
                _logger?.Invoke($"[Monitor] ReadSampleAsync: Worker initialization wait complete. _initialized={_initialized}");
            }

            // Use worker if enabled and initialized
            if (_useWorker && _workerClient != null && _initialized)
            {
                _logger?.Invoke($"[Monitor] ReadSampleAsync: Using worker (useWorker={_useWorker}, client!=null={_workerClient != null}, initialized={_initialized})");
                return await ReadSampleFromWorkerAsync(token);
            }

            _logger?.Invoke($"[Monitor] ReadSampleAsync: Using fallback to in-process (useWorker={_useWorker}, client!=null={_workerClient != null}, initialized={_initialized})");

            // Check cache freshness to reduce CPU overhead
            lock (_lock)
            {
                if (DateTime.Now - _lastUpdate < _cacheLifetime)
                {
                    return BuildSampleFromCache();
                }
            }

            await Task.Run(() => UpdateHardwareReadings(), token);

            lock (_lock)
            {
                _lastUpdate = DateTime.Now;
                return BuildSampleFromCache();
            }
        }
        
        /// <summary>
        /// Read hardware sample from out-of-process worker
        /// </summary>
        private async Task<MonitoringSample> ReadSampleFromWorkerAsync(CancellationToken token)
        {
            var workerSample = await _workerClient!.GetSampleAsync();
            
            if (workerSample == null)
            {
                // Worker unavailable - check if worker is permanently disabled (cooldown)
                if (_workerClient != null && !_workerClient.IsEnabled)
                {
                    _logger?.Invoke("[Monitor] ⚠️ Worker is in cooldown/disabled state — falling back to in-process mode");
                    _useWorker = false;
                    Reinitialize();
                    if (_initialized)
                    {
                        _logger?.Invoke("[Monitor] ✓ Successfully fell back to in-process monitoring");
                        return await ReadSampleAsync(token);
                    }
                }
                
                // Worker unavailable - return cached values
                _logger?.Invoke("[Monitor] Worker returned null sample - using cached values");
                return BuildSampleFromCache();
            }
            
            // Check for initialization in progress (StaleCount == 999)
            if (workerSample.StaleCount == 999)
            {
                _logger?.Invoke("[Monitor] ℹ️ Worker still initializing hardware sensors - using cached values");
                return BuildSampleFromCache();
            }
            
            // Check for stale data from worker
            if (!workerSample.IsFresh || workerSample.StaleCount > 30)
            {
                _logger?.Invoke($"[Monitor] ⚠️ Worker data is stale (StaleCount={workerSample.StaleCount}, IsFresh={workerSample.IsFresh}). Restarting worker...");
                
                // Restart the worker process to get fresh sensors
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _workerClient.StopAsync();
                        await Task.Delay(500);
                        await _workerClient.StartAsync();
                        _logger?.Invoke("[Monitor] Worker restarted successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"[Monitor] Failed to restart worker: {ex.Message}");
                    }
                });
            }
            
            // Update cache from worker sample
            lock (_lock)
            {
                _cachedCpuTemp = workerSample.CpuTemperature;
                _cachedCpuLoad = workerSample.CpuLoad;
                _cachedCpuPower = workerSample.CpuPower;
                
                // Update core clocks from worker
                if (workerSample.CpuCoreClocks != null && workerSample.CpuCoreClocks.Count > 0)
                {
                    _cachedCoreClocks.Clear();
                    _cachedCoreClocks.AddRange(workerSample.CpuCoreClocks);
                }
                
                _cachedGpuTemp = workerSample.GpuTemperature;
                _cachedGpuLoad = workerSample.GpuLoad;
                _cachedGpuPower = workerSample.GpuPower;
                _cachedGpuClock = workerSample.GpuClock;
                _cachedGpuMemoryClock = workerSample.GpuMemoryClock;
                _cachedGpuVoltage = workerSample.GpuVoltage;
                _cachedGpuCurrent = workerSample.GpuCurrent;
                _cachedGpuHotspot = workerSample.GpuHotspot;
                
                _cachedVramUsage = workerSample.VramUsage;
                _cachedVramTotal = workerSample.VramTotal;
                
                // RAM: Accept worker values only if valid, otherwise fallback will be triggered later
                if (workerSample.RamTotal > 0)
                {
                    _cachedRamUsage = workerSample.RamUsage;
                    _cachedRamTotal = workerSample.RamTotal;
                }
                // Immediate fallback if worker returned 0/0 GB
                else if (_cachedRamTotal <= 0)
                {
                    var totalGb = GetTotalPhysicalMemoryGB();
                    if (totalGb > 0)
                    {
                        _cachedRamTotal = totalGb;
                        // Estimate usage via WMI
                        try
                        {
                            using var searcher = new System.Management.ManagementObjectSearcher(
                                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                            foreach (System.Management.ManagementObject obj in searcher.Get())
                            {
                                var freeKb = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                                _cachedRamUsage = totalGb - (freeKb / 1024.0 / 1024.0);
                                break;
                            }
                        }
                        catch { _cachedRamUsage = totalGb * 0.5; }
                    }
                }
                
                _cachedSsdTemp = workerSample.SsdTemperature;
                
                _cachedBatteryCharge = workerSample.BatteryCharge;
                _cachedIsOnAc = workerSample.IsOnAc;
                _cachedDischargeRate = workerSample.BatteryDischargeRate;
                
                if (!string.IsNullOrEmpty(workerSample.GpuName) && _lastGpuName != workerSample.GpuName)
                {
                    _lastGpuName = workerSample.GpuName;
                    _logger?.Invoke($"[Worker] GPU detected: {workerSample.GpuName}");
                }
                
                _lastUpdate = DateTime.Now;
            }
            
            return BuildSampleFromCache();
        }

        // Track consecutive NVML failures to avoid spam
        private int _nvmlFailures = 0;
        private const int MaxNvmlFailuresBeforeDisable = 3;
        private bool _nvmlDisabled = false;
        private DateTime _nvmlDisabledAt = DateTime.MinValue;  // v2.8.6: Track when NVML was disabled
        private const int NvmlRetryCooldownSeconds = 60;       // v2.8.6: Retry NVML after 60s cooldown

        /// <summary>
        /// Safely updates GPU hardware with timeout protection.
        /// NVML can hang or throw exceptions during high GPU load (e.g., benchmarks).
        /// </summary>
        /// <remarks>
        /// In .NET 8, AccessViolationException is a Corrupted State Exception (CSE) that
        /// cannot be caught. This method uses a timeout to detect hangs and catches
        /// regular exceptions that can be handled.
        /// 
        /// Known issue: If NVML crashes with AccessViolationException, the app will crash.
        /// This is a limitation of NVIDIA's NVML library and cannot be fixed in managed code.
        /// Users experiencing crashes during benchmarks should update their NVIDIA drivers.
        /// </remarks>
        private bool TryUpdateGpuHardware(IHardware hardware)
        {
            if (_nvmlDisabled)
            {
                // v2.8.6: NVML disabled due to repeated failures—check if cooldown expired
                if ((DateTime.Now - _nvmlDisabledAt).TotalSeconds >= NvmlRetryCooldownSeconds)
                {
                    _nvmlDisabled = false;
                    _nvmlFailures = 0;
                    _logger?.Invoke($"[GPU] NVML cooldown expired ({NvmlRetryCooldownSeconds}s)—retrying GPU hardware update");
                }
                else
                {
                    return false;
                }
            }
            
            try
            {
                hardware.Update();
                _nvmlFailures = 0; // Reset on success
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _nvmlFailures++;
                _logger?.Invoke($"[GPU] Hardware update failed (failure {_nvmlFailures}/{MaxNvmlFailuresBeforeDisable}): {ex.GetType().Name}: {ex.Message}");
                
                if (_nvmlFailures >= MaxNvmlFailuresBeforeDisable)
                {
                    _nvmlDisabled = true;
                    _nvmlDisabledAt = DateTime.Now;  // v2.8.6: Record disable time for cooldown retry
                    _logger?.Invoke($"[GPU] GPU monitoring paused due to repeated failures. Will retry in {NvmlRetryCooldownSeconds}s.");
                }
                return false;
            }
        }
        
        private void UpdateHardwareReadings()
        {
            // BUG FIX v2.6.1: If in WMI-only mode due to repeated stuck temps, bypass LHM
            if (_forceWmiBiosMode)
            {
                UpdateViaWmiBiosFallback();
                return;
            }
            
            if (!_initialized || _disposed)
            {
                // Fall back to WMI/performance counters if LibreHardwareMonitor unavailable
                if (!_disposed) UpdateViaFallback();
                return;
            }

            lock (_lock)
            {
                if (_disposed) return; // Double-check after acquiring lock
                
                _computer?.Accept(new UpdateVisitor());

                foreach (var hardware in _computer?.Hardware ?? Array.Empty<IHardware>())
                {
                    if (_disposed) return; // Check before each hardware update
                    
                    try
                    {
                        // Use safe GPU update for all discrete GPUs (NVIDIA/AMD/Intel Arc)
                        bool isDiscreteGpu = hardware.HardwareType == HardwareType.GpuNvidia || 
                            hardware.HardwareType == HardwareType.GpuAmd ||
                            (hardware.HardwareType == HardwareType.GpuIntel && 
                             hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
                        
                        if (isDiscreteGpu)
                        {
                            if (!TryUpdateGpuHardware(hardware))
                            {
                                // GPU update failed — if NVML is disabled, try WMI BIOS fallback for GPU temp
                                // to prevent the cached value from going permanently stale (v2.8.6)
                                if (_nvmlDisabled)
                                {
                                    UpdateViaWmiBiosFallback();
                                }
                                continue;
                            }
                        }
                        else
                        {
                            hardware.Update();
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Handle SafeFileHandle disposal and other hardware access errors
                        // This commonly happens when drives sleep or hardware becomes temporarily unavailable
                        _logger?.Invoke($"[Hardware] Error updating {hardware.HardwareType} '{hardware.Name}': {ex.GetType().Name}: {ex.Message}");
                        
                        // If this is a SafeFileHandle/ObjectDisposedException, it likely means drives slept
                        // Continue to next hardware instead of failing completely
                        if (ex is ObjectDisposedException || ex.Message.Contains("SafeFileHandle") || ex.Message.Contains("disposed"))
                        {
                            _logger?.Invoke($"[Hardware] SafeFileHandle disposed - likely drive sleep. Skipping {hardware.Name} and using WMI BIOS fallback...");
                            // Use WMI BIOS as fallback for temperature when drives sleep
                            UpdateViaWmiBiosFallback();
                            continue;
                        }
                        
                        // For other exceptions, re-throw to trigger fallback
                        throw;
                    }

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            // CPU Temperature Selection Priority:
                            // Prioritize stable sensors over individual cores to avoid spurious readings
                            // Package/Core Max sensors provide better thermal load representation
                            var cpuTempSensor = GetSensorExact(hardware, SensorType.Temperature, "CPU Package")       // Intel Package (most stable)
                                ?? GetSensor(hardware, SensorType.Temperature, "Package")                           // Intel Package (partial)
                                ?? GetSensorExact(hardware, SensorType.Temperature, "CPU DTS")                      // Intel DTS (Arrow Lake / Core Ultra)
                                ?? GetSensorExact(hardware, SensorType.Temperature, "Core (Tctl/Tdie)")             // AMD Ryzen primary (stable)
                                ?? GetSensor(hardware, SensorType.Temperature, "Tctl/Tdie")                         // AMD Ryzen (partial)
                                ?? GetSensor(hardware, SensorType.Temperature, "Core Max")                          // Max core temp (stable)
                                ?? GetSensor(hardware, SensorType.Temperature, "Core Average")                      // Average core temp
                                ?? GetSensor(hardware, SensorType.Temperature, "Core #1")                           // Intel Core #1
                                ?? GetSensor(hardware, SensorType.Temperature, "Core #0")                           // Intel Core #0
                                ?? GetSensor(hardware, SensorType.Temperature, "Tctl")                              // AMD older
                                ?? GetSensor(hardware, SensorType.Temperature, "Tdie")                              // AMD Ryzen alt
                                ?? GetSensorExact(hardware, SensorType.Temperature, "CPU (Tctl/Tdie)")              // AMD Ryzen variant
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD1 (Tdie)")                       // AMD CCD1 with Tdie suffix
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD 1 (Tdie)")                      // AMD CCD 1 with space
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD1")                              // AMD CCD fallback
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD 1")                             // AMD CCD with space
                                ?? GetSensor(hardware, SensorType.Temperature, "CCDs Max")                          // AMD multi-CCD
                                ?? GetSensor(hardware, SensorType.Temperature, "CCDs Average")                      // AMD multi-CCD avg
                                ?? GetSensor(hardware, SensorType.Temperature, "CPU")                               // AMD Ryzen AI / generic
                                ?? GetSensor(hardware, SensorType.Temperature, "SoC")                               // AMD APU SoC
                                ?? GetSensor(hardware, SensorType.Temperature, "Socket")                            // Socket temp
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value > 0);
                            
                            var rawCpuTemp = cpuTempSensor?.Value ?? 0;
                            
                            // v2.8.6: Safety net for Intel Core Ultra / Arrow Lake CPUs
                            // If primary sensor returns 0 but we previously had a valid reading,
                            // sweep ALL temperature sensors for a plausible value
                            if (rawCpuTemp <= 0 && _cachedCpuTemp > 5)
                            {
                                var allTempSensors = hardware.Sensors
                                    .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 5 && s.Value.Value < 120)
                                    .OrderByDescending(s => s.Value!.Value)
                                    .ToList();
                                if (allTempSensors.Count > 0)
                                {
                                    rawCpuTemp = allTempSensors[0].Value!.Value;
                                    _logger?.Invoke($"⚠️ CPU temp sensor returned 0 (prev={_cachedCpuTemp:F1}°C). Using fallback: {allTempSensors[0].Name}={rawCpuTemp:F1}°C");
                                }
                            }
                            
                            // BUG FIX #36: Validate temperature is not stuck at TjMax (96°C or 100°C)
                            // Some sensors report TjMax instead of current temperature
                            if (rawCpuTemp >= 95 && rawCpuTemp <= 100)
                            {
                                _stuckTempReadings++;
                                if (_stuckTempReadings >= 5) // 5 consecutive readings at TjMax
                                {
                                    // Temperature likely stuck at TjMax - try alternative sensors
                                    var altSensor = hardware.Sensors
                                        .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 20 && s.Value.Value < 90)
                                        .OrderByDescending(s => s.Value!.Value)
                                        .FirstOrDefault();
                                    
                                    if (altSensor != null)
                                    {
                                        _logger?.Invoke($"CPU temp stuck at {rawCpuTemp}°C (TjMax), using alternative: {altSensor.Name}={altSensor.Value}°C");
                                        rawCpuTemp = altSensor.Value!.Value;
                                    }
                                    else
                                    {
                                        _logger?.Invoke($"CPU temp appears stuck at TjMax ({rawCpuTemp}°C), no alternative sensor available");
                                    }
                                }
                            }
                            else
                            {
                                _stuckTempReadings = 0;
                            }
                            
                            _cachedCpuTemp = rawCpuTemp;
                            
                            // BUG FIX v2.6.1: Detect stuck temperature readings (same value repeating) with improved fallback
                            if (_cachedCpuTemp > 0 && Math.Abs(_cachedCpuTemp - _lastCpuTempReading) < 0.1)
                            {
                                _consecutiveSameTempReadings++;
                                if (_consecutiveSameTempReadings >= MaxSameTempReadingsBeforeReinit)
                                {
                                    _logger?.Invoke($"🚨 CPU temp stuck at {_cachedCpuTemp:F1}°C for {_consecutiveSameTempReadings} readings. Trying WMI BIOS fallback...");
                                    
                                    // Try WMI BIOS fallback first before full reinitialize
                                    UpdateViaWmiBiosFallback();
                                    
                                    // If WMI gave us a different temperature, use it and reset counter
                                    if (Math.Abs(_cachedCpuTemp - _lastCpuTempReading) > 1)
                                    {
                                        _logger?.Invoke($"✅ WMI BIOS fallback provided different temperature: {_cachedCpuTemp:F1}°C");
                                        _consecutiveSameTempReadings = 0;
                                        _reinitializeAttempts = 0; // Reset reinit counter on success
                                    }
                                    else
                                    {
                                        _reinitializeAttempts++;
                                        if (_reinitializeAttempts >= MaxReinitializeAttempts)
                                        {
                                            // Too many failed reinits - switch to WMI-only mode for temperature
                                            _logger?.Invoke($"⚠️ {MaxReinitializeAttempts} reinitialize attempts failed. Switching to WMI-only temperature mode.");
                                            _forceWmiBiosMode = true;
                                            _consecutiveSameTempReadings = 0;
                                        }
                                        else
                                        {
                                            // WMI didn't help, do full reinitialize
                                            _logger?.Invoke($"WMI BIOS fallback didn't help (attempt {_reinitializeAttempts}/{MaxReinitializeAttempts}). Triggering hardware reinitialize...");
                                            Task.Run(() => Reinitialize());
                                            _consecutiveSameTempReadings = 0;
                                        }
                                    }
                                }
                                else if (_consecutiveSameTempReadings >= MaxSameTempReadingsBeforeLog)
                                {
                                    _logger?.Invoke($"⚠️ CPU temp appears stuck at {_cachedCpuTemp:F1}°C for {_consecutiveSameTempReadings} readings. Trying WMI BIOS first...");
                                    
                                    // Try WMI BIOS fallback immediately before alternative sensors
                                    UpdateViaWmiBiosFallback();
                                    if (Math.Abs(_cachedCpuTemp - _lastCpuTempReading) > 1)
                                    {
                                        _logger?.Invoke($"✅ WMI BIOS unstuck temperature: {_cachedCpuTemp:F1}°C");
                                        _consecutiveSameTempReadings = 0;
                                    }
                                    else
                                    {
                                        // WMI didn't help, force hardware update and try alternative sensor
                                        hardware.Update();
                                    
                                        // Try alternative sensor
                                        var altSensor = hardware.Sensors
                                            .Where(s => s.SensorType == SensorType.Temperature && 
                                                       s.Value.HasValue && 
                                                       s.Value.Value > 20 && 
                                                       s.Value.Value < 100 &&
                                                       Math.Abs(s.Value.Value - _cachedCpuTemp) > 1) // Different from stuck value
                                            .OrderByDescending(s => s.Value!.Value)
                                            .FirstOrDefault();
                                    
                                        if (altSensor != null)
                                        {
                                            _logger?.Invoke($"Using alternative CPU temp sensor: {altSensor.Name}={altSensor.Value:F1}°C");
                                            _cachedCpuTemp = altSensor.Value!.Value;
                                            _consecutiveSameTempReadings = 0;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                _consecutiveSameTempReadings = 0;
                            }
                            _lastCpuTempReading = _cachedCpuTemp;
                            
                            // Apply temperature smoothing to reduce spurious readings
                            if (_cachedCpuTemp > 0)
                            {
                                _cpuTempHistory.Add(_cachedCpuTemp);
                                if (_cpuTempHistory.Count > CpuTempSmoothingWindow)
                                {
                                    _cpuTempHistory.RemoveAt(0);
                                }
                                _smoothedCpuTemp = _cpuTempHistory.Average();
                                _cachedCpuTemp = _smoothedCpuTemp;
                            }
                            
                            // If still 0, try refreshing the hardware
                            if (_cachedCpuTemp == 0)
                            {
                                // Force re-scan of CPU sensors
                                hardware.Update();
                                cpuTempSensor = hardware.Sensors.FirstOrDefault(s => 
                                    s.SensorType == SensorType.Temperature && 
                                    s.Value.HasValue && s.Value.Value > 0);
                                _cachedCpuTemp = cpuTempSensor?.Value ?? 0;
                            }
                            
                            if (_cachedCpuTemp == 0)
                            {
                                // Log all available temp sensors for debugging
                                var availableTempSensors = hardware.Sensors
                                    .Where(s => s.SensorType == SensorType.Temperature)
                                    .Select(s => $"{s.Name}={s.Value}")
                                    .ToList();
                                _logger?.Invoke($"CPU temp sensor issue in {hardware.Name}. Available: [{string.Join(", ", availableTempSensors)}]");
                                
                                // Try to get any temperature reading above 0
                                var anyTempSensor = hardware.Sensors
                                    .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 10)
                                    .OrderByDescending(s => s.Value!.Value)
                                    .FirstOrDefault();
                                if (anyTempSensor != null)
                                {
                                    _cachedCpuTemp = anyTempSensor.Value!.Value;
                                    _logger?.Invoke($"Using fallback CPU temp sensor: {anyTempSensor.Name}={_cachedCpuTemp}°C");
                                }
                            }
                            
                            // If still 0 after all LibreHardwareMonitor attempts, try PawnIO MSR fallback
                            if (_cachedCpuTemp == 0 && _pawnIoCpuTemp != null && _pawnIoCpuTemp.IsAvailable)
                            {
                                double pawnIoTemp = _pawnIoCpuTemp.ReadCpuTemperature();
                                if (pawnIoTemp > 0 && pawnIoTemp < 150)
                                {
                                    _cachedCpuTemp = pawnIoTemp;
                                    _logger?.Invoke($"Using PawnIO CPU temperature fallback: {_cachedCpuTemp:F1}°C");
                                }
                                else if (pawnIoTemp == 0)
                                {
                                    _logger?.Invoke("PawnIO CPU temperature fallback returned 0°C - MSR access may be unavailable");
                                }
                            }
                            
                            // Track consecutive 0°C readings and auto-reinitialize if needed
                            if (_cachedCpuTemp == 0)
                            {
                                _consecutiveZeroTempReadings++;
                                if (_consecutiveZeroTempReadings >= MaxZeroTempReadingsBeforeReinit)
                                {
                                    _logger?.Invoke($"CPU temp stuck at 0°C for {_consecutiveZeroTempReadings} readings. Reinitializing hardware monitor...");
                                    // Queue reinitialization (will happen on next update cycle)
                                    Task.Run(() => Reinitialize());
                                    _consecutiveZeroTempReadings = 0;
                                }
                            }
                            else
                            {
                                _consecutiveZeroTempReadings = 0;
                            }
                            
                            var cpuLoadRaw = GetSensor(hardware, SensorType.Load, "CPU Total")?.Value ?? 0;
                            _cachedCpuLoad = Math.Clamp(cpuLoadRaw, 0, 100);
                            
                            // CPU Power - AMD uses "Package Power", Intel uses "CPU Package"
                            var cpuPowerSensor = GetSensor(hardware, SensorType.Power, "CPU Package")    // Intel
                                ?? GetSensor(hardware, SensorType.Power, "Package Power")                 // AMD
                                ?? GetSensor(hardware, SensorType.Power, "Package")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                            _cachedCpuPower = cpuPowerSensor?.Value ?? 0;
                            
                            _cachedCoreClocks.Clear();
                            var coreClockSensors = hardware.Sensors
                                .Where(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                                .OrderBy(s => s.Name)
                                .ToList();
                            
                            foreach (var sensor in coreClockSensors)
                            {
                                if (sensor.Value.HasValue)
                                {
                                    _cachedCoreClocks.Add(sensor.Value.Value);
                                }
                            }
                            
                            // Throttling detection for CPU
                            // Check for thermal throttling indicator sensor first
                            var thermalThrottleSensor = GetSensor(hardware, SensorType.Factor, "Thermal Throttling")
                                ?? GetSensor(hardware, SensorType.Factor, "Thermal Throttle");
                            _cachedCpuThermalThrottling = thermalThrottleSensor?.Value > 0 
                                || _cachedCpuTemp >= CpuThermalThrottleThreshold;
                            
                            // Check for power throttling (Intel) or PROCHOT
                            var powerThrottleSensor = GetSensor(hardware, SensorType.Factor, "Power Limit Exceeded")
                                ?? GetSensor(hardware, SensorType.Factor, "Power Throttling")
                                ?? GetSensor(hardware, SensorType.Factor, "PROCHOT");
                            _cachedCpuPowerThrottling = powerThrottleSensor?.Value > 0;
                            
                            // MSR-based throttling detection as fallback/enhancement
                            if (!_cachedCpuPowerThrottling && _msrAccess?.IsAvailable == true)
                            {
                                _cachedCpuPowerThrottling = _msrAccess.ReadPowerThrottlingStatus();
                            }
                            
                            break;

                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                            // GPU Temperature Selection:
                            // Use "GPU Core" not "Hotspot" - Core is more stable for fan control
                            // Hotspot spikes during brief load bursts cause unnecessary fan ramp
                            // Hotspot is still tracked separately (_cachedGpuHotspot) for alerts
                            var gpuTempSensor = GetSensor(hardware, SensorType.Temperature, "GPU Core")
                                ?? GetSensor(hardware, SensorType.Temperature, "Core")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                            
                            var tempValue = gpuTempSensor?.Value ?? 0;
                            if (tempValue > 0)
                            {
                                _cachedGpuTemp = tempValue;
                                // Only log when GPU changes (first detection or different GPU)
                                if (_lastGpuName != hardware.Name)
                                {
                                    _lastGpuName = hardware.Name;
                                    _logger?.Invoke($"Using dedicated GPU: {hardware.Name} ({tempValue:F0}°C)");
                                }
                                
                                // GPU stuck detection
                                if (Math.Abs(_cachedGpuTemp - _lastGpuTempReading) < 0.1)
                                {
                                    _consecutiveSameGpuTempReadings++;
                                    if (_consecutiveSameGpuTempReadings >= MaxSameTempReadingsBeforeReinit)
                                    {
                                        _logger?.Invoke($"🚨 GPU temp stuck at {_cachedGpuTemp:F1}°C for {_consecutiveSameGpuTempReadings} readings. Triggering hardware reinitialize...");
                                        Task.Run(() => Reinitialize());
                                        _consecutiveSameGpuTempReadings = 0;
                                    }
                                }
                                else
                                {
                                    _consecutiveSameGpuTempReadings = 0;
                                }
                                _lastGpuTempReading = _cachedGpuTemp;
                            }
                            
                            var gpuLoadSensor = GetSensor(hardware, SensorType.Load, "GPU Core")
                                ?? GetSensor(hardware, SensorType.Load, "Core")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                            
                            var loadValue = gpuLoadSensor?.Value ?? 0;
                            if (loadValue > 0 || _cachedGpuLoad == 0)
                            {
                                _cachedGpuLoad = Math.Clamp(loadValue, 0, 100);
                            }
                            
                            // Try different sensor names for VRAM
                            var vramSensor = GetSensor(hardware, SensorType.SmallData, "GPU Memory Used") 
                                ?? GetSensor(hardware, SensorType.SmallData, "D3D Dedicated Memory Used")
                                ?? GetSensor(hardware, SensorType.Data, "GPU Memory Used");
                            _cachedVramUsage = vramSensor?.Value ?? 0;
                            
                            // Enhanced GPU metrics (v1.1)
                            // GPU Power
                            var gpuPowerSensor = GetSensor(hardware, SensorType.Power, "GPU Power")
                                ?? GetSensor(hardware, SensorType.Power, "Board Power")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                            _cachedGpuPower = gpuPowerSensor?.Value ?? 0;
                            
                            // GPU Core Clock
                            var gpuClockSensor = GetSensor(hardware, SensorType.Clock, "GPU Core")
                                ?? GetSensor(hardware, SensorType.Clock, "Core")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"));
                            _cachedGpuClock = gpuClockSensor?.Value ?? 0;
                            
                            // GPU Memory Clock
                            var gpuMemClockSensor = GetSensor(hardware, SensorType.Clock, "GPU Memory")
                                ?? GetSensor(hardware, SensorType.Clock, "Memory")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Memory"));
                            _cachedGpuMemoryClock = gpuMemClockSensor?.Value ?? 0;
                            
                            // Total VRAM
                            var vramTotalSensor = GetSensor(hardware, SensorType.SmallData, "GPU Memory Total")
                                ?? GetSensor(hardware, SensorType.SmallData, "D3D Dedicated Memory Total")
                                ?? GetSensor(hardware, SensorType.Data, "GPU Memory Total");
                            _cachedVramTotal = vramTotalSensor?.Value ?? 0;
                            
                            // GPU Fan
                            var gpuFanSensor = GetSensor(hardware, SensorType.Control, "GPU Fan")
                                ?? GetSensor(hardware, SensorType.Load, "GPU Fan")
                                ?? hardware.Sensors.FirstOrDefault(s => s.Name.Contains("Fan") && s.SensorType == SensorType.Control);
                            _cachedGpuFan = gpuFanSensor?.Value ?? 0;
                            
                            // GPU Hotspot Temperature
                            var gpuHotspotSensor = GetSensor(hardware, SensorType.Temperature, "GPU Hot Spot")
                                ?? GetSensor(hardware, SensorType.Temperature, "Hot Spot")
                                ?? GetSensor(hardware, SensorType.Temperature, "GPU Hotspot");
                            _cachedGpuHotspot = gpuHotspotSensor?.Value ?? 0;
                            
                            // GPU Throttling detection
                            // Check for explicit throttling sensors first
                            var gpuThermalThrottleSensor = GetSensor(hardware, SensorType.Factor, "Thermal Throttling")
                                ?? GetSensor(hardware, SensorType.Factor, "Thermal Limit");
                            var gpuHotspotForThrottle = _cachedGpuHotspot > 0 ? _cachedGpuHotspot : _cachedGpuTemp;
                            _cachedGpuThermalThrottling = gpuThermalThrottleSensor?.Value > 0
                                || gpuHotspotForThrottle >= GpuThermalThrottleThreshold;
                            
                            // GPU Power throttling
                            var gpuPowerThrottleSensor = GetSensor(hardware, SensorType.Factor, "Power Limit")
                                ?? GetSensor(hardware, SensorType.Factor, "Power Throttling")
                                ?? GetSensor(hardware, SensorType.Factor, "TDP Limit");
                            _cachedGpuPowerThrottling = gpuPowerThrottleSensor?.Value > 0;
                            break;
                            
                        case HardwareType.GpuIntel:
                            // Intel Arc GPUs are dedicated GPUs - treat them like NVIDIA/AMD
                            // Intel UHD/Iris are integrated - only use as fallback
                            bool isIntelArc = hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                            
                            // For Intel Arc (dedicated) or if no other GPU found yet
                            if (isIntelArc || _cachedGpuTemp == 0)
                            {
                                // Use safe update for Intel Arc (similar to NVIDIA/AMD)
                                if (isIntelArc && !TryUpdateGpuHardware(hardware))
                                {
                                    break; // Skip if update failed
                                }
                                
                                var intelTempSensor = GetSensor(hardware, SensorType.Temperature, "GPU Core")
                                    ?? GetSensor(hardware, SensorType.Temperature, "GPU Package")
                                    ?? GetSensor(hardware, SensorType.Temperature, "GPU")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                                
                                var tempVal = intelTempSensor?.Value ?? 0;
                                if (tempVal > 0)
                                {
                                    _cachedGpuTemp = tempVal;
                                    
                                    // Only log when GPU changes
                                    if (_lastGpuName != hardware.Name)
                                    {
                                        _lastGpuName = hardware.Name;
                                        var gpuType = isIntelArc ? "Intel Arc" : "integrated Intel GPU";
                                        _logger?.Invoke($"Using {gpuType}: {hardware.Name} ({_cachedGpuTemp:F0}°C)");
                                    }
                                    
                                    // GPU stuck detection for Intel GPUs
                                    if (Math.Abs(_cachedGpuTemp - _lastGpuTempReading) < 0.1)
                                    {
                                        _consecutiveSameGpuTempReadings++;
                                        if (_consecutiveSameGpuTempReadings >= MaxSameTempReadingsBeforeReinit)
                                        {
                                            _logger?.Invoke($"🚨 Intel GPU temp stuck at {_cachedGpuTemp:F1}°C for {_consecutiveSameGpuTempReadings} readings. Triggering hardware reinitialize...");
                                            Task.Run(() => Reinitialize());
                                            _consecutiveSameGpuTempReadings = 0;
                                        }
                                    }
                                    else
                                    {
                                        _consecutiveSameGpuTempReadings = 0;
                                    }
                                    _lastGpuTempReading = _cachedGpuTemp;
                                }
                                else if (isIntelArc)
                                {
                                    // Log available sensors for debugging Intel Arc issues
                                    var availableSensors = hardware.Sensors
                                        .Where(s => s.SensorType == SensorType.Temperature)
                                        .Select(s => $"{s.Name}={s.Value}")
                                        .ToList();
                                    _logger?.Invoke($"[Intel Arc] No temp reading from {hardware.Name}. Available: [{string.Join(", ", availableSensors)}]");
                                }
                            }
                            
                            if (isIntelArc || _cachedGpuLoad == 0)
                            {
                                var intelLoadSensor = GetSensor(hardware, SensorType.Load, "GPU Core")
                                    ?? GetSensor(hardware, SensorType.Load, "D3D 3D")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                                var loadVal = intelLoadSensor?.Value ?? 0;
                                if (loadVal > 0 || _cachedGpuLoad == 0)
                                {
                                    _cachedGpuLoad = Math.Clamp(loadVal, 0, 100);
                                }
                            }
                            
                            // Intel Arc power/clock metrics (similar to NVIDIA/AMD)
                            if (isIntelArc)
                            {
                                var arcPowerSensor = GetSensor(hardware, SensorType.Power, "GPU Power")
                                    ?? GetSensor(hardware, SensorType.Power, "GPU Package")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                                if (arcPowerSensor?.Value > 0) _cachedGpuPower = arcPowerSensor.Value.Value;
                                
                                var arcClockSensor = GetSensor(hardware, SensorType.Clock, "GPU Core")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
                                if (arcClockSensor?.Value > 0) _cachedGpuClock = arcClockSensor.Value.Value;
                            }
                            break;

                        case HardwareType.Memory:
                            var sensorRamUsed = GetSensor(hardware, SensorType.Data, "Memory Used")?.Value ?? 0;
                            var sensorRamAvail = GetSensor(hardware, SensorType.Data, "Memory Available")?.Value ?? 0;
                            var sensorRamTotal = sensorRamUsed + sensorRamAvail;
                            
                            // LibreHardwareMonitor sometimes returns garbage values (e.g. 16MB instead of 16GB)
                            // Only use sensor values if they're reasonable (>= 1 GB total)
                            if (sensorRamTotal >= 1.0)
                            {
                                _cachedRamUsage = sensorRamUsed;
                                _cachedRamTotal = sensorRamTotal;
                            }
                            else
                            {
                                // Fallback to WMI for accurate RAM info
                                if (_systemRamTotalGb > 0)
                                {
                                    _cachedRamTotal = _systemRamTotalGb;
                                    // Calculate usage via WMI
                                    try
                                    {
                                        using var searcher = new System.Management.ManagementObjectSearcher(
                                            "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                                        foreach (System.Management.ManagementObject obj in searcher.Get())
                                        {
                                            var freeKb = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                                            _cachedRamUsage = _systemRamTotalGb - (freeKb / 1024.0 / 1024.0);
                                            break;
                                        }
                                    }
                                    catch { _cachedRamUsage = _systemRamTotalGb * 0.5; }
                                }
                                else
                                {
                                    _cachedRamTotal = 16; // Default fallback
                                }
                            }
                            break;

                        case HardwareType.Storage:
                            if (hardware.Name.Contains("NVMe") || hardware.Name.Contains("SSD"))
                            {
                                _cachedSsdTemp = GetSensor(hardware, SensorType.Temperature)?.Value ?? 0;
                                _cachedDiskUsage = GetSensor(hardware, SensorType.Load)?.Value ?? 0;
                            }
                            break;
                            
                        case HardwareType.Battery:
                            // Battery charge level
                            var chargeSensor = GetSensor(hardware, SensorType.Level, "Charge Level")
                                ?? GetSensor(hardware, SensorType.Level)
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Level);
                            if (chargeSensor?.Value.HasValue == true)
                            {
                                _cachedBatteryCharge = chargeSensor.Value.Value;
                            }
                            
                            // Discharge rate (negative when charging)
                            var powerSensor = GetSensor(hardware, SensorType.Power, "Discharge Rate")
                                ?? GetSensor(hardware, SensorType.Power)
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
                            if (powerSensor?.Value.HasValue == true)
                            {
                                _cachedDischargeRate = powerSensor.Value.Value;
                                _cachedIsOnAc = _cachedDischargeRate <= 0;
                            }
                            
                            // Time remaining
                            var timeSensor = GetSensor(hardware, SensorType.TimeSpan, "Remaining Time")
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.TimeSpan);
                            if (timeSensor?.Value.HasValue == true)
                            {
                                var minutes = timeSensor.Value.Value;
                                if (minutes > 0)
                                {
                                    var hours = (int)(minutes / 60);
                                    var mins = (int)(minutes % 60);
                                    _cachedBatteryTimeRemaining = hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
                                }
                                else
                                {
                                    _cachedBatteryTimeRemaining = _cachedIsOnAc ? "Charging" : "";
                                }
                            }
                            break;
                    }

                    // Fan RPM from motherboard or subhardware
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        var fanSensor = subHardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Name.Contains("CPU"));
                        if (fanSensor != null && fanSensor.Value.HasValue)
                        {
                            _cachedFanRpm = fanSensor.Value.Value;
                        }
                    }
                }
            }
        }

        private void UpdateViaFallback()
        {
            // Use WMI and Performance Counters as fallback
            try
            {
                // CPU temp via ACPI thermal zones (limited but works on many systems)
                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                {
                    var temps = new List<double>();
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var raw = (uint)obj["CurrentTemperature"];
                        var celsius = raw / 10.0 - 273.15;
                        temps.Add(celsius);
                    }
                    if (temps.Count > 0)
                    {
                        _cachedCpuTemp = temps.Average();
                    }
                }

                // Performance counters for CPU/RAM
                using var cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                using var ramCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                cpuCounter.NextValue(); // First call returns 0
                System.Threading.Thread.Sleep(100);
                _cachedCpuLoad = cpuCounter.NextValue();

                var availableMb = ramCounter.NextValue();
                var totalRamGb = GetTotalPhysicalMemoryGB();
                _cachedRamTotal = totalRamGb;
                _cachedRamUsage = totalRamGb - (availableMb / 1024.0);
            }
            catch
            {
                // Silent fallback to previous values
            }
        }

        private double GetTotalPhysicalMemoryGB()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    return bytes / 1024.0 / 1024.0 / 1024.0; // Convert to GB
                }
            }
            catch
            {
                return 16; // Default assumption
            }
            return 16;
        }

        private MonitoringSample BuildSampleFromCache()
        {
            // RAM fallback: if worker reported 0 RAM usage, try PerformanceCounter fallback
            if (_cachedRamUsage <= 0 && _useWorker)
            {
                try
                {
                    using var ramCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                    ramCounter.NextValue(); // First call returns 0
                    System.Threading.Thread.Sleep(10);
                    var availableMb = ramCounter.NextValue();
                    var totalRamGb = GetTotalPhysicalMemoryGB();
                    if (totalRamGb > 0)
                    {
                        _cachedRamUsage = totalRamGb - (availableMb / 1024.0);
                        _cachedRamTotal = totalRamGb;
                        _logger?.Invoke($"[Monitor] RAM fallback used: {availableMb:F0} MB available, {totalRamGb:F1} GB total, { _cachedRamUsage:F1} GB used");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"[Monitor] RAM fallback failed: {ex.Message}");
                }
            }

            return new MonitoringSample
            {
                CpuTemperatureC = Math.Round(_cachedCpuTemp, 1),
                CpuLoadPercent = Math.Round(Math.Clamp(_cachedCpuLoad, 0, 100), 1),  // Clamp to valid range
                CpuPowerWatts = Math.Round(_cachedCpuPower, 1),
                CpuCoreClocksMhz = new List<double>(_cachedCoreClocks),
                GpuTemperatureC = Math.Round(_cachedGpuTemp, 1),
                GpuLoadPercent = Math.Round(Math.Clamp(_cachedGpuLoad, 0, 100), 1),  // Clamp to valid range
                GpuVramUsageMb = Math.Round(_cachedVramUsage, 0),
                RamUsageGb = Math.Round(_cachedRamUsage, 1),
                RamTotalGb = Math.Round(_cachedRamTotal, 0),
                FanRpm = Math.Round(_cachedFanRpm, 0),
                SsdTemperatureC = Math.Round(_cachedSsdTemp, 1),
                DiskUsagePercent = Math.Round(Math.Clamp(_cachedDiskUsage, 0, 100), 1),  // Clamp to valid range
                BatteryChargePercent = Math.Round(Math.Clamp(_cachedBatteryCharge, 0, 100), 0),  // Clamp to valid range
                IsOnAcPower = _cachedIsOnAc,
                BatteryDischargeRateW = Math.Round(_cachedDischargeRate, 1),
                BatteryTimeRemaining = _cachedBatteryTimeRemaining,
                Timestamp = DateTime.Now,
                // Enhanced GPU metrics (v1.1)
                GpuPowerWatts = Math.Round(_cachedGpuPower, 1),
                GpuClockMhz = Math.Round(_cachedGpuClock, 0),
                GpuMemoryClockMhz = Math.Round(_cachedGpuMemoryClock, 0),
                GpuVoltageV = Math.Round(_cachedGpuVoltage, 3),
                GpuCurrentA = Math.Round(_cachedGpuCurrent, 2),
                GpuVramTotalMb = Math.Round(_cachedVramTotal, 0),
                GpuFanPercent = Math.Round(Math.Clamp(_cachedGpuFan, 0, 100), 0),  // Clamp to valid range
                GpuHotspotTemperatureC = Math.Round(_cachedGpuHotspot, 1),
                GpuName = _lastGpuName,
                // Throttling status (v1.2)
                IsCpuThermalThrottling = _cachedCpuThermalThrottling,
                IsCpuPowerThrottling = _cachedCpuPowerThrottling,
                IsGpuThermalThrottling = _cachedGpuThermalThrottling,
                IsGpuPowerThrottling = _cachedGpuPowerThrottling
            };
        }

        private ISensor? GetSensor(IHardware hardware, SensorType type, string? namePattern = null)
        {
            var sensors = hardware.Sensors.Where(s => s.SensorType == type);
            if (!string.IsNullOrEmpty(namePattern))
            {
                sensors = sensors.Where(s => s.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase));
            }
            return sensors.FirstOrDefault();
        }
        
        /// <summary>
        /// Get sensor with exact name match (case-insensitive).
        /// Use this for AMD sensors with special characters like "Core (Tctl/Tdie)".
        /// </summary>
        private ISensor? GetSensorExact(IHardware hardware, SensorType type, string exactName)
        {
            return hardware.Sensors.FirstOrDefault(s => 
                s.SensorType == type && 
                s.Name.Equals(exactName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Set MSR access for enhanced throttling detection.
        /// Can be called after construction if MSR access becomes available later.
        /// </summary>
        public void SetMsrAccess(IMsrAccess? msrAccess)
        {
            _msrAccess = msrAccess;
        }

        /// <summary>
        /// Get current CPU temperature in Celsius.
        /// Ensures cache is fresh before returning value.
        /// </summary>
        public double GetCpuTemperature()
        {
            EnsureCacheFresh();
            lock (_lock)
            {
                return _cachedCpuTemp;
            }
        }

        /// <summary>
        /// Get current GPU core temperature in Celsius.
        /// Ensures cache is fresh before returning value.
        /// </summary>
        public double GetGpuTemperature()
        {
            EnsureCacheFresh();
            lock (_lock)
            {
                return _cachedGpuTemp;
            }
        }
        
        /// <summary>
        /// Ensures the hardware cache is fresh before reading values.
        /// If cache is stale, triggers a synchronous update.
        /// </summary>
        private void EnsureCacheFresh()
        {
            bool needsUpdate;
            lock (_lock)
            {
                needsUpdate = DateTime.Now - _lastUpdate > _cacheLifetime;
            }
            
            if (needsUpdate && _initialized && !_disposed)
            {
                if (_useWorker && _workerClient != null)
                {
                    // Request fresh sample from worker (fire-and-forget async but sync wait briefly)
                    try
                    {
                        var task = _workerClient.GetSampleAsync();
                        if (task.Wait(TimeSpan.FromMilliseconds(500)))
                        {
                            var sample = task.Result;
                            if (sample != null)
                            {
                                lock (_lock)
                                {
                                    _cachedCpuTemp = sample.CpuTemperature;
                                    _cachedGpuTemp = sample.GpuTemperature;
                                    _cachedCpuLoad = sample.CpuLoad;
                                    _cachedGpuLoad = sample.GpuLoad;
                                    _cachedCpuPower = sample.CpuPower;
                                    _cachedGpuPower = sample.GpuPower;
                                    _lastUpdate = DateTime.Now;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Timeout or error - use cached value
                    }
                }
                else
                {
                    // In-process mode - trigger direct update
                    UpdateHardwareReadings();
                    lock (_lock)
                    {
                        _lastUpdate = DateTime.Now;
                    }
                }
            }
        }

        /// <summary>
        /// Get fan speeds as (Name, RPM) tuples
        /// </summary>
        public IEnumerable<(string Name, double Rpm)> GetFanSpeeds()
        {
            if (!_initialized || _disposed)
            {
                return Array.Empty<(string, double)>();
            }

            var results = new List<(string Name, double Rpm)>();

            lock (_lock)
            {
                if (_disposed) return results;
                
                try
                {
                    // Update hardware readings to get fresh fan data
                    _computer?.Accept(new UpdateVisitor());

                    foreach (var hardware in _computer?.Hardware ?? Array.Empty<IHardware>())
                    {
                        if (_disposed) return results;
                        
                        // Use safe GPU update for all discrete GPUs (NVIDIA/AMD/Intel Arc)
                        bool isDiscreteGpu = hardware.HardwareType == HardwareType.GpuNvidia || 
                            hardware.HardwareType == HardwareType.GpuAmd ||
                            (hardware.HardwareType == HardwareType.GpuIntel && 
                             hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
                        
                        if (isDiscreteGpu)
                        {
                            if (!TryUpdateGpuHardware(hardware))
                            {
                                continue; // Skip failed GPU
                            }
                        }
                        else
                        {
                            hardware.Update();
                        }

                        // Check main hardware for fan sensors (CPU, GPU have built-in fans on some laptops)
                        var fanSensors = hardware.Sensors
                            .Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue)
                            .ToList();

                        foreach (var sensor in fanSensors)
                        {
                            results.Add(($"{hardware.Name} - {sensor.Name}", sensor.Value!.Value));
                        }

                        // Check subhardware (e.g., motherboard EC for laptop fan sensors)
                        foreach (var subHardware in hardware.SubHardware)
                        {
                            if (_disposed) return results;
                            
                            subHardware.Update();
                            var subFanSensors = subHardware.Sensors
                                .Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue)
                                .ToList();

                            foreach (var sensor in subFanSensors)
                            {
                                results.Add(($"{subHardware.Name} - {sensor.Name}", sensor.Value!.Value));
                            }
                        }
                    }
                    
                    // Log hardware types found for debugging if no fans detected
                    if (results.Count == 0)
                    {
                        var hwTypes = _computer?.Hardware?.Select(h => $"{h.HardwareType}:{h.Name}").ToList() ?? new List<string>();
                        if (!_noFanSensorsLogged)
                        {
                            _noFanSensorsLogged = true;
                            _logger?.Invoke($"[FanDebug] No fan sensors found via LibreHardwareMonitor. Hardware: [{string.Join(", ", hwTypes)}]");
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Gracefully handle disposal during iteration (e.g., app shutdown)
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // Log but don't crash on unexpected errors
                    _logger?.Invoke($"[Hardware] Error in GetFanSpeeds: {ex.GetType().Name}: {ex.Message}");
                }
            }
            
            return results;
        }

        /// <summary>
        /// Fallback update method using WMI BIOS when LibreHardwareMonitor fails.
        /// Used when SafeFileHandle exceptions occur (drive sleep) or other hardware access issues.
        /// </summary>
        private void UpdateViaWmiBiosFallback()
        {
            try
            {
                // Try to get CPU and GPU temperature from WMI BIOS
                var wmiBios = new HpWmiBios(_logger != null ? new LoggingService() : null);
                
                // Try the new GetBothTemperatures method first for efficiency
                var temps = wmiBios.GetBothTemperatures();
                
                if (temps.HasValue)
                {
                    var (cpuTemp, gpuTemp) = temps.Value;
                    
                    if (cpuTemp > 0)
                    {
                        _cachedCpuTemp = cpuTemp;
                        _logger?.Invoke($"[Fallback] Using WMI BIOS CPU temperature: {_cachedCpuTemp}°C");
                    }
                    else
                    {
                        // Fallback to the original GetTemperature method
                        var wmiTemp = wmiBios.GetTemperature();
                        if (wmiTemp.HasValue && wmiTemp.Value > 0)
                        {
                            _cachedCpuTemp = wmiTemp.Value;
                            _logger?.Invoke($"[Fallback] Using WMI BIOS temperature (legacy): {_cachedCpuTemp}°C");
                        }
                        else
                        {
                            _logger?.Invoke("[Fallback] WMI BIOS CPU temperature unavailable");
                        }
                    }
                    
                    if (gpuTemp > 0)
                    {
                        _cachedGpuTemp = gpuTemp;
                        _logger?.Invoke($"[Fallback] Using WMI BIOS GPU temperature: {_cachedGpuTemp}°C");
                    }
                    else
                    {
                        // Try dedicated GPU temperature method
                        var gpuTempAlt = wmiBios.GetGpuTemperature();
                        if (gpuTempAlt.HasValue && gpuTempAlt.Value > 0)
                        {
                            _cachedGpuTemp = gpuTempAlt.Value;
                            _logger?.Invoke($"[Fallback] Using WMI BIOS GPU temperature (alt): {_cachedGpuTemp}°C");
                        }
                    }
                }
                
                wmiBios.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Fallback] WMI BIOS fallback failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
            
            // Clean up worker if used
            if (_workerClient != null)
            {
                _workerClient.Dispose();
                _workerClient = null;
            }
            
            // Clean up PawnIO
            if (_pawnIoCpuTemp != null)
            {
                _pawnIoCpuTemp.Dispose();
                _pawnIoCpuTemp = null;
            }
            
            if (_initialized && _computer != null)
            {
                _computer.Close();
            }
        }
    }

    /// <summary>
    /// PawnIO-based CPU temperature reader as fallback when LibreHardwareMonitor fails.
    /// LibreHardwareMonitor uses WinRing0 which Windows Defender often blocks.
    /// PawnIO is a signed driver that works with Secure Boot and Defender.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class PawnIOCpuTemp : IDisposable
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

    /// <summary>
    /// Visitor pattern implementation for LibreHardwareMonitor to update all hardware sensors.
    /// </summary>
    internal class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
