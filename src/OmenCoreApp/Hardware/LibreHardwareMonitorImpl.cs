using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Real LibreHardwareMonitor implementation - integrates with actual hardware sensors.
    /// TODO: Add LibreHardwareMonitor NuGet package to enable this implementation.
    /// Package: LibreHardwareMonitorLib (community fork of OpenHardwareMonitor)
    /// </summary>
    public class LibreHardwareMonitorImpl : IHardwareMonitorBridge, IDisposable
    {
        // TODO: Uncomment when LibreHardwareMonitorLib NuGet package is installed
        // private readonly Computer _computer;
        private bool _initialized;
        private readonly object _lock = new();

        // Cache for performance
        private double _cachedCpuTemp = 0;
        private double _cachedGpuTemp = 0;
        private double _cachedCpuLoad = 0;
        private double _cachedGpuLoad = 0;
        private double _cachedVramUsage = 0;
        private double _cachedRamUsage = 0;
        private double _cachedRamTotal = 0;
        private double _cachedFanRpm = 0;
        private double _cachedSsdTemp = 0;
        private double _cachedDiskUsage = 0;
        private List<double> _cachedCoreClocks = new();
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(100);

        public LibreHardwareMonitorImpl()
        {
            // TODO: Initialize LibreHardwareMonitor Computer object
            /*
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = false
            };
            _computer.Open();
            */
            _initialized = false;
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

            // TODO: Real LibreHardwareMonitor implementation
            /*
            lock (_lock)
            {
                _computer.Accept(new UpdateVisitor());

                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    switch (hardware.HardwareType)
                    {
                        case HardwareType.Cpu:
                            _cachedCpuTemp = GetSensor(hardware, SensorType.Temperature, "CPU Package")?.Value ?? 0;
                            _cachedCpuLoad = GetSensor(hardware, SensorType.Load, "CPU Total")?.Value ?? 0;
                            
                            _cachedCoreClocks.Clear();
                            for (int i = 0; i < hardware.Sensors.Count(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core")); i++)
                            {
                                var clockSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains($"Core #{i + 1}"));
                                if (clockSensor != null && clockSensor.Value.HasValue)
                                {
                                    _cachedCoreClocks.Add(clockSensor.Value.Value);
                                }
                            }
                            break;

                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                            _cachedGpuTemp = GetSensor(hardware, SensorType.Temperature, "GPU Core")?.Value ?? 0;
                            _cachedGpuLoad = GetSensor(hardware, SensorType.Load, "GPU Core")?.Value ?? 0;
                            _cachedVramUsage = GetSensor(hardware, SensorType.SmallData, "GPU Memory Used")?.Value ?? 0;
                            break;

                        case HardwareType.Memory:
                            _cachedRamUsage = GetSensor(hardware, SensorType.Data, "Memory Used")?.Value ?? 0;
                            _cachedRamTotal = GetSensor(hardware, SensorType.Data, "Memory Available")?.Value ?? 16;
                            break;

                        case HardwareType.Storage:
                            if (hardware.Name.Contains("NVMe") || hardware.Name.Contains("SSD"))
                            {
                                _cachedSsdTemp = GetSensor(hardware, SensorType.Temperature)?.Value ?? 0;
                                _cachedDiskUsage = GetSensor(hardware, SensorType.Load)?.Value ?? 0;
                            }
                            break;
                    }

                    // Fan RPM from motherboard
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
            */

            // Temporary fallback until LibreHardwareMonitor is integrated
            UpdateViaFallback();
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
                CpuCoreClocksMhz = new List<double>(_cachedCoreClocks),
                GpuTemperatureC = Math.Round(_cachedGpuTemp, 1),
                GpuLoadPercent = Math.Round(_cachedGpuLoad, 1),
                GpuVramUsageMb = Math.Round(_cachedVramUsage, 0),
                RamUsageGb = Math.Round(_cachedRamUsage, 1),
                RamTotalGb = Math.Round(_cachedRamTotal, 0),
                FanRpm = Math.Round(_cachedFanRpm, 0),
                SsdTemperatureC = Math.Round(_cachedSsdTemp, 1),
                DiskUsagePercent = Math.Round(_cachedDiskUsage, 1),
                Timestamp = DateTime.Now
            };
        }

        /* TODO: Helper method for LibreHardwareMonitor sensor lookup
        private ISensor GetSensor(IHardware hardware, SensorType type, string namePattern = null)
        {
            var sensors = hardware.Sensors.Where(s => s.SensorType == type);
            if (!string.IsNullOrEmpty(namePattern))
            {
                sensors = sensors.Where(s => s.Name.Contains(namePattern));
            }
            return sensors.FirstOrDefault();
        }
        */

        public void Dispose()
        {
            // TODO: Dispose LibreHardwareMonitor computer
            // _computer?.Close();
        }
    }

    /// <summary>
    /// TODO: UpdateVisitor for LibreHardwareMonitor
    /// </summary>
    /*
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
    */
}
