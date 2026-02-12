using System;
using System.Collections.Generic;
using System.Threading;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    public class ThermalSensorProvider
    {
        private readonly LibreHardwareMonitorImpl? _bridge;
        private readonly WmiBiosMonitor? _wmiBiosMonitor;
        private readonly HpWmiBios? _wmiBios;
        
        /// <summary>
        /// Create ThermalSensorProvider with LibreHardwareMonitorImpl for full monitoring
        /// </summary>
        public ThermalSensorProvider(LibreHardwareMonitorImpl bridge)
        {
            _bridge = bridge;
        }
        
        /// <summary>
        /// Create ThermalSensorProvider with IHardwareMonitorBridge interface.
        /// Uses the bridge's own cached readings (which include ACPI thermal zone
        /// and NVAPI enrichment) rather than creating a separate raw HpWmiBios.
        /// </summary>
        public ThermalSensorProvider(IHardwareMonitorBridge bridge)
        {
            _bridge = bridge as LibreHardwareMonitorImpl;
            if (_bridge == null)
            {
                // Prefer WmiBiosMonitor which has ACPI-enhanced temperatures
                _wmiBiosMonitor = bridge as WmiBiosMonitor;
                if (_wmiBiosMonitor == null)
                {
                    // Last resort: raw WMI BIOS (integer-only temps)
                    _wmiBios = new HpWmiBios(null);
                }
            }
        }

        public IEnumerable<TemperatureReading> ReadTemperatures()
        {
            var list = new List<TemperatureReading>();

            double cpuTemp = 0;
            double gpuTemp = 0;
            
            // Try LibreHardwareMonitor first
            if (_bridge != null)
            {
                cpuTemp = _bridge.GetCpuTemperature();
                gpuTemp = _bridge.GetGpuTemperature();
            }
            // Use WmiBiosMonitor's ACPI-enhanced cached readings
            else if (_wmiBiosMonitor != null)
            {
                try
                {
                    var sample = _wmiBiosMonitor.ReadSampleAsync(CancellationToken.None).GetAwaiter().GetResult();
                    cpuTemp = sample.CpuTemperatureC;
                    gpuTemp = sample.GpuTemperatureC;
                }
                catch
                {
                    // If ReadSampleAsync fails, temps stay at 0
                }
            }
            // Last resort: raw WMI BIOS (integer-only, no ACPI overlay)
            else if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                var temps = _wmiBios.GetBothTemperatures();
                if (temps.HasValue)
                {
                    var (cpu, gpu) = temps.Value;
                    cpuTemp = cpu;
                    gpuTemp = gpu;
                }
            }

            if (cpuTemp > 0)
            {
                list.Add(new TemperatureReading { Sensor = "CPU Package", Celsius = cpuTemp });
            }

            if (gpuTemp > 0)
            {
                list.Add(new TemperatureReading { Sensor = "GPU", Celsius = gpuTemp });
            }

            // Fallback if no data available
            if (list.Count == 0)
            {
                list.Add(new TemperatureReading { Sensor = "CPU", Celsius = 0 });
                list.Add(new TemperatureReading { Sensor = "GPU", Celsius = 0 });
            }

            return list;
        }
    }
}
