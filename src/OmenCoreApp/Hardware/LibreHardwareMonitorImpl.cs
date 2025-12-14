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
        private List<double> _cachedCoreClocks = new();
        private DateTime _lastUpdate = DateTime.MinValue;
        private TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(100);
        private string _lastGpuName = string.Empty;
        
        // Enhanced GPU metrics (v1.1)
        private double _cachedGpuPower = 0;
        private double _cachedGpuClock = 0;
        private double _cachedGpuMemoryClock = 0;
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
        private int _consecutiveZeroTempReadings = 0;
        private const int MaxZeroTempReadingsBeforeReinit = 5;
        private bool _noFanSensorsLogged = false; // Only log once to reduce spam

        public LibreHardwareMonitorImpl(Action<string>? logger = null)
        {
            _logger = logger;
            InitializeComputer();
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
            }
            catch (Exception ex)
            {
                _initialized = false;
                _logger?.Invoke($"LibreHardwareMonitor init failed: {ex.Message}. Using WMI fallback.");
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
                    hardware.Update();

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            // AMD Ryzen uses "Core (Tctl/Tdie)" or "Tdie", Intel uses "CPU Package"
                            // Ryzen AI (Phoenix/Strix Point/Hawk Point) may use different sensor names
                            // Ryzen 8940HX (Hawk Point) and RTX 5070 systems need special handling
                            // Try multiple sensor patterns for broad compatibility
                            var cpuTempSensor = GetSensorExact(hardware, SensorType.Temperature, "CPU Package")  // Intel
                                ?? GetSensorExact(hardware, SensorType.Temperature, "Core (Tctl/Tdie)")           // AMD Ryzen primary
                                ?? GetSensor(hardware, SensorType.Temperature, "Tctl/Tdie")                       // AMD Ryzen (partial match)
                                ?? GetSensor(hardware, SensorType.Temperature, "Tctl")                            // AMD older
                                ?? GetSensor(hardware, SensorType.Temperature, "Tdie")                            // AMD Ryzen alt
                                ?? GetSensorExact(hardware, SensorType.Temperature, "CPU (Tctl/Tdie)")            // AMD Ryzen variant
                                ?? GetSensor(hardware, SensorType.Temperature, "CPU")                             // AMD Ryzen AI / generic
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD1 (Tdie)")                     // AMD CCD1 with Tdie suffix
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD 1 (Tdie)")                    // AMD CCD 1 with space
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD1")                            // AMD CCD fallback
                                ?? GetSensor(hardware, SensorType.Temperature, "CCD 1")                           // AMD CCD with space
                                ?? GetSensor(hardware, SensorType.Temperature, "CCDs Max")                        // AMD multi-CCD
                                ?? GetSensor(hardware, SensorType.Temperature, "CCDs Average")                    // AMD multi-CCD avg
                                ?? GetSensor(hardware, SensorType.Temperature, "Core Max")                        // Max core temp
                                ?? GetSensor(hardware, SensorType.Temperature, "Core Average")                    // Avg core temp
                                ?? GetSensor(hardware, SensorType.Temperature, "Core #0")                         // Single core fallback
                                ?? GetSensor(hardware, SensorType.Temperature, "SoC")                             // AMD APU SoC
                                ?? GetSensor(hardware, SensorType.Temperature, "Socket")                          // Socket temp
                                ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value > 0);
                            
                            _cachedCpuTemp = cpuTempSensor?.Value ?? 0;
                            
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
                            // Prefer dedicated GPU (NVIDIA/AMD) over integrated
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
                            // Only use Intel GPU if no dedicated GPU found yet
                            if (_cachedGpuTemp == 0)
                            {
                                var intelTempSensor = GetSensor(hardware, SensorType.Temperature, "GPU Core")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                                _cachedGpuTemp = intelTempSensor?.Value ?? 0;
                                
                                // Only log when GPU changes
                                if (_lastGpuName != hardware.Name)
                                {
                                    _lastGpuName = hardware.Name;
                                    _logger?.Invoke($"Using integrated GPU: {hardware.Name} ({_cachedGpuTemp:F0}°C)");
                                }
                            }
                            
                            if (_cachedGpuLoad == 0)
                            {
                                var intelLoadSensor = GetSensor(hardware, SensorType.Load, "GPU Core")
                                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                                _cachedGpuLoad = intelLoadSensor?.Value ?? 0;
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
                using (var cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"))
                using (var ramCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes"))
                {
                    cpuCounter.NextValue(); // First call returns 0
                    System.Threading.Thread.Sleep(100);
                    _cachedCpuLoad = cpuCounter.NextValue();
                    
                    var availableMb = ramCounter.NextValue();
                    var totalRamGb = GetTotalPhysicalMemoryGB();
                    _cachedRamTotal = totalRamGb;
                    _cachedRamUsage = totalRamGb - (availableMb / 1024.0);
                }
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
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                        return bytes / 1024.0 / 1024.0 / 1024.0; // Convert to GB
                    }
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
                        
                        hardware.Update();

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
            }
            
            return results;
        }

        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
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
