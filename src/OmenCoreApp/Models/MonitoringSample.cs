using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    public class MonitoringSample
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuTemperatureC { get; set; }
        public double CpuLoadPercent { get; set; }
        public double CpuPowerWatts { get; set; }
        public List<double> CpuCoreClocksMhz { get; set; } = new();
        public double GpuTemperatureC { get; set; }
        public double GpuLoadPercent { get; set; }
        public double GpuVramUsageMb { get; set; }
        public double RamUsageGb { get; set; }
        public double RamTotalGb { get; set; }
        public double FanRpm { get; set; }
        public double SsdTemperatureC { get; set; }
        public double DiskUsagePercent { get; set; }
        
        // Enhanced GPU metrics (v1.1)
        public double GpuPowerWatts { get; set; }
        public double GpuClockMhz { get; set; }
        public double GpuMemoryClockMhz { get; set; }
        public double GpuVramTotalMb { get; set; }
        public double GpuFanPercent { get; set; }
        public double GpuHotspotTemperatureC { get; set; }
        public string GpuName { get; set; } = string.Empty;
        
        // Battery info
        public double BatteryChargePercent { get; set; }
        public bool IsOnAcPower { get; set; }
        public double BatteryDischargeRateW { get; set; }
        public string BatteryTimeRemaining { get; set; } = "";
        
        // Computed property for RAM usage percentage
        public double RamUsagePercent => RamTotalGb > 0 ? (RamUsageGb / RamTotalGb) * 100 : 0;
        
        // Computed property for VRAM usage percentage
        public double GpuVramUsagePercent => GpuVramTotalMb > 0 ? (GpuVramUsageMb / GpuVramTotalMb) * 100 : 0;
    }
}
