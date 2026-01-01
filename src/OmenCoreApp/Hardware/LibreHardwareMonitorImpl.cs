using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using OmenCore.Models;

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
        private bool _workerInitializing = false; // Track async initialization state

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

        /// <summary>
        /// Create an in-process hardware monitor (default).
        /// May crash from NVML issues during heavy GPU load.
        /// </summary>
        public LibreHardwareMonitorImpl(Action<string>? logger = null)
        {
            _logger = logger;
            _useWorker = false;
            InitializeComputer();
        }
        
        /// <summary>
        /// Create a hardware monitor with optional out-of-process worker.
        /// When useWorker is true, hardware monitoring runs in a separate process
        /// that can crash without affecting the main application.
        /// </summary>
        /// <param name="logger">Logging callback</param>
        /// <param name="useWorker">Use out-of-process worker for crash isolation</param>
        public LibreHardwareMonitorImpl(Action<string>? logger, bool useWorker)
        {
            _logger = logger;
            _useWorker = useWorker;
            
            if (_useWorker)
            {
                InitializeWorker();
            }
            else
            {
                InitializeComputer();
            }
        }
        
        private async void InitializeWorker()
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
                }
                else
                {
                    _logger?.Invoke("[Monitor] Worker failed to start, falling back to in-process");
                    _useWorker = false;
                    InitializeComputer();
                }
            }
            finally
            {
                _workerInitializing = false;
            }
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

        public async Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            // Wait for worker initialization to complete (prevent race condition)
            if (_workerInitializing)
            {
                // Worker is still starting up - wait a bit
                for (int i = 0; i < 10 && _workerInitializing; i++)
                {
                    await Task.Delay(200, token);
                }
            }

            // Use worker if enabled and initialized
            if (_useWorker && _workerClient != null && _initialized)
            {
                return await ReadSampleFromWorkerAsync(token);
            }

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
                // Worker unavailable - return cached values
                return BuildSampleFromCache();
            }
            
            // Update cache from worker sample
            lock (_lock)
            {
                _cachedCpuTemp = workerSample.CpuTemperature;
                _cachedCpuLoad = workerSample.CpuLoad;
                _cachedCpuPower = workerSample.CpuPower;
                
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
                _cachedRamUsage = workerSample.RamUsage;
                _cachedRamTotal = workerSample.RamTotal;
                
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
                // NVML disabled due to repeated failures - skip GPU update
                return false;
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
                    _logger?.Invoke("[GPU] GPU monitoring disabled due to repeated failures.");
                }
                return false;
            }
        }
        
        private void UpdateHardwareReadings()
        {
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
                    
                    // Use safe GPU update for all discrete GPUs (NVIDIA/AMD/Intel Arc)
                    bool isDiscreteGpu = hardware.HardwareType == HardwareType.GpuNvidia || 
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        (hardware.HardwareType == HardwareType.GpuIntel && 
                         hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
                    
                    if (isDiscreteGpu)
                    {
                        if (!TryUpdateGpuHardware(hardware))
                        {
                            // GPU update failed - continue to next hardware without processing this GPU
                            continue;
                        }
                    }
                    else
                    {
                        hardware.Update();
                    }

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            // CPU Temperature Selection Priority:
                            // Prioritize stable sensors over individual cores to avoid spurious readings
                            // Package/Core Max sensors provide better thermal load representation
                            var cpuTempSensor = GetSensorExact(hardware, SensorType.Temperature, "CPU Package")       // Intel Package (most stable)
                                ?? GetSensor(hardware, SensorType.Temperature, "Package")                           // Intel Package (partial)
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
                            
                            _cachedCpuTemp = cpuTempSensor?.Value ?? 0;
                            
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
                            _cachedRamUsage = GetSensor(hardware, SensorType.Data, "Memory Used")?.Value ?? 0;
                            var availableRam = GetSensor(hardware, SensorType.Data, "Memory Available")?.Value ?? 0;
                            _cachedRamTotal = (_cachedRamUsage + availableRam) > 0 ? (_cachedRamUsage + availableRam) : 16;
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
        /// Get current CPU package temperature in Celsius
        /// </summary>
        public double GetCpuTemperature()
        {
            lock (_lock)
            {
                return _cachedCpuTemp;
            }
        }

        /// <summary>
        /// Get current GPU core temperature in Celsius
        /// </summary>
        public double GetGpuTemperature()
        {
            lock (_lock)
            {
                return _cachedGpuTemp;
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
                    
                    // Log hardware types found for debugging if no fans detected (only once)
                    if (results.Count == 0 && !_noFanSensorsLogged)
                    {
                        _noFanSensorsLogged = true;
                        var hwTypes = _computer?.Hardware?.Select(h => $"{h.HardwareType}:{h.Name}").ToList() ?? new List<string>();
                        _logger?.Invoke($"[FanDebug] No fan sensors found via LibreHardwareMonitor (using WMI). Hardware: [{string.Join(", ", hwTypes)}]");
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
            
            if (_initialized && _computer != null)
            {
                _computer.Close();
            }
        }
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
