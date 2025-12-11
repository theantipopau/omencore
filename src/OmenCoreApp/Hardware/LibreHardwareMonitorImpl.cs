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
    /// </summary>
    public class LibreHardwareMonitorImpl : IHardwareMonitorBridge, IDisposable
    {
        private readonly Computer? _computer;
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
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(100);
        private string _lastGpuName = string.Empty;

        private readonly Action<string>? _logger;

        public LibreHardwareMonitorImpl(Action<string>? logger = null)
        {
            _logger = logger;
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsStorageEnabled = true,
                    IsControllerEnabled = true,
                    IsNetworkEnabled = false,
                    IsBatteryEnabled = true
                };
                _computer.Open();
                _initialized = true;
                _logger?.Invoke("LibreHardwareMonitor initialized successfully");
            }
            catch (Exception ex)
            {
                _initialized = false;
                _logger?.Invoke($"LibreHardwareMonitor init failed: {ex.Message}. Using WMI fallback.");
            }
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
            if (!_initialized)
            {
                // Fall back to WMI/performance counters if LibreHardwareMonitor unavailable
                UpdateViaFallback();
                return;
            }

            lock (_lock)
            {
                _computer?.Accept(new UpdateVisitor());

                foreach (var hardware in _computer?.Hardware ?? Array.Empty<IHardware>())
                {
                    hardware.Update();

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            var cpuTempSensor = GetSensor(hardware, SensorType.Temperature, "CPU Package");
                            _cachedCpuTemp = cpuTempSensor?.Value ?? 0;
                            if (_cachedCpuTemp == 0 && cpuTempSensor == null)
                            {
                                _logger?.Invoke($"CPU Package temp sensor not found in {hardware.Name}");
                            }
                            _cachedCpuLoad = GetSensor(hardware, SensorType.Load, "CPU Total")?.Value ?? 0;
                            
                            // CPU Power (package power consumption)
                            var cpuPowerSensor = GetSensor(hardware, SensorType.Power, "CPU Package")
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
                                _cachedGpuLoad = loadValue;
                            }
                            
                            // Try different sensor names for VRAM
                            var vramSensor = GetSensor(hardware, SensorType.SmallData, "GPU Memory Used") 
                                ?? GetSensor(hardware, SensorType.SmallData, "D3D Dedicated Memory Used")
                                ?? GetSensor(hardware, SensorType.Data, "GPU Memory Used");
                            _cachedVramUsage = vramSensor?.Value ?? 0;
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
                CpuLoadPercent = Math.Round(_cachedCpuLoad, 1),
                CpuPowerWatts = Math.Round(_cachedCpuPower, 1),
                CpuCoreClocksMhz = new List<double>(_cachedCoreClocks),
                GpuTemperatureC = Math.Round(_cachedGpuTemp, 1),
                GpuLoadPercent = Math.Round(_cachedGpuLoad, 1),
                GpuVramUsageMb = Math.Round(_cachedVramUsage, 0),
                RamUsageGb = Math.Round(_cachedRamUsage, 1),
                RamTotalGb = Math.Round(_cachedRamTotal, 0),
                FanRpm = Math.Round(_cachedFanRpm, 0),
                SsdTemperatureC = Math.Round(_cachedSsdTemp, 1),
                DiskUsagePercent = Math.Round(_cachedDiskUsage, 1),
                BatteryChargePercent = Math.Round(_cachedBatteryCharge, 0),
                IsOnAcPower = _cachedIsOnAc,
                BatteryDischargeRateW = Math.Round(_cachedDischargeRate, 1),
                BatteryTimeRemaining = _cachedBatteryTimeRemaining,
                Timestamp = DateTime.Now
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
            if (!_initialized)
            {
                yield break;
            }

            lock (_lock)
            {
                // Update hardware readings to get fresh fan data
                _computer?.Accept(new UpdateVisitor());

                foreach (var hardware in _computer?.Hardware ?? Array.Empty<IHardware>())
                {
                    hardware.Update();

                    // Check main hardware for fan sensors
                    var fanSensors = hardware.Sensors
                        .Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue)
                        .ToList();

                    foreach (var sensor in fanSensors)
                    {
                        yield return (sensor.Name, sensor.Value!.Value);
                    }

                    // Check subhardware (e.g., motherboard sensors)
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        var subFanSensors = subHardware.Sensors
                            .Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue)
                            .ToList();

                        foreach (var sensor in subFanSensors)
                        {
                            yield return (sensor.Name, sensor.Value!.Value);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
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
