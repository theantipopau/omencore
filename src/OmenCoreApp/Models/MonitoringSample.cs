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
        
        // Throttling status (v1.2)
        /// <summary>
        /// True if CPU is thermal throttling (temperature limit reached).
        /// </summary>
        public bool IsCpuThermalThrottling { get; set; }
        
        /// <summary>
        /// True if CPU is power throttling (TDP limit reached).
        /// </summary>
        public bool IsCpuPowerThrottling { get; set; }
        
        /// <summary>
        /// True if GPU is thermal throttling.
        /// </summary>
        public bool IsGpuThermalThrottling { get; set; }
        
        /// <summary>
        /// True if GPU is power throttling.
        /// </summary>
        public bool IsGpuPowerThrottling { get; set; }
        
        /// <summary>
        /// Overall throttling status summary.
        /// </summary>
        public bool IsThrottling => IsCpuThermalThrottling || IsCpuPowerThrottling || IsGpuThermalThrottling || IsGpuPowerThrottling;
        
        /// <summary>
        /// Human-readable throttling status description.
        /// </summary>
        public string ThrottlingStatus
        {
            get
            {
                if (!IsThrottling) return "Normal";
                
                var reasons = new List<string>();
                if (IsCpuThermalThrottling) reasons.Add("CPU Thermal");
                if (IsCpuPowerThrottling) reasons.Add("CPU Power");
                if (IsGpuThermalThrottling) reasons.Add("GPU Thermal");
                if (IsGpuPowerThrottling) reasons.Add("GPU Power");
                
                return string.Join(", ", reasons);
            }
        }
        
        // Computed property for RAM usage percentage
        public double RamUsagePercent => RamTotalGb > 0 ? (RamUsageGb / RamTotalGb) * 100 : 0;
        
        // Computed property for VRAM usage percentage
        public double GpuVramUsagePercent => GpuVramTotalMb > 0 ? (GpuVramUsageMb / GpuVramTotalMb) * 100 : 0;
    }
}
