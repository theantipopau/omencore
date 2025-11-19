using System;

namespace OmenCore.Models
{
    public class ThermalSample
    {
        public DateTime Timestamp { get; set; }
        public double CpuCelsius { get; set; }
        public double GpuCelsius { get; set; }
    }
}
