using System;
using System.Collections.Generic;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    public class ThermalSensorProvider
    {
        private readonly LibreHardwareMonitorImpl _bridge;

        public ThermalSensorProvider(LibreHardwareMonitorImpl bridge)
        {
            _bridge = bridge;
        }

        public IEnumerable<TemperatureReading> ReadTemperatures()
        {
            var list = new List<TemperatureReading>();

            // Get temperatures from hardware monitor
            var cpuTemp = _bridge.GetCpuTemperature();
            var gpuTemp = _bridge.GetGpuTemperature();

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
