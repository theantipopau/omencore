using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    public class MonitoringSample
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuTemperatureC { get; set; }
        public double CpuLoadPercent { get; set; }
        public List<double> CpuCoreClocksMhz { get; set; } = new();
        public double GpuTemperatureC { get; set; }
        public double GpuLoadPercent { get; set; }
        public double GpuVramUsageMb { get; set; }
        public double RamUsageGb { get; set; }
        public double RamTotalGb { get; set; }
        public double FanRpm { get; set; }
        public double SsdTemperatureC { get; set; }
        public double DiskUsagePercent { get; set; }
    }
}
