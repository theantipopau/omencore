using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    public enum TelemetryDataState
    {
        Unknown,
        Valid,
        Zero,
        Inactive,
        Unavailable,
        Stale,
        Invalid
    }

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
        public TelemetryDataState CpuTemperatureState { get; set; } = TelemetryDataState.Unknown;
        public TelemetryDataState CpuPowerState { get; set; } = TelemetryDataState.Unknown;
        public TelemetryDataState GpuTemperatureState { get; set; } = TelemetryDataState.Unknown;
        public TelemetryDataState Fan1RpmState { get; set; } = TelemetryDataState.Unknown;
        public TelemetryDataState Fan2RpmState { get; set; } = TelemetryDataState.Unknown;

        public bool IsGpuInactive => GpuTemperatureState == TelemetryDataState.Inactive;
        
        // Dual fan support (v2.2)
        public int Fan1Rpm { get; set; }
        public int Fan2Rpm { get; set; }
        
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
        
        // GPU Voltage/Current monitoring (v2.0)
        public double GpuVoltageV { get; set; }
        public double GpuCurrentA { get; set; }
        
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
        
        /// <summary>
        /// True if SSD sensor data is available (non-zero temperature).
        /// Used to hide SSD widget when LibreHardwareMonitor can't read SMART data.
        /// </summary>
        public bool IsSsdDataAvailable => SsdTemperatureC > 0;

        /// <summary>
        /// Parameterless constructor. Preserved explicitly because adding the copy constructor
        /// below suppresses the compiler-generated default.
        /// </summary>
        public MonitoringSample() { }

        /// <summary>
        /// Copy constructor. Creates an independent clone of <paramref name="source"/>.
        /// Required by NormalizeMonitoringSample (STEP-08) so normalization returns a new
        /// object rather than mutating the sample in place.
        /// </summary>
        public MonitoringSample(MonitoringSample source)
        {
            Timestamp                 = source.Timestamp;
            CpuTemperatureC           = source.CpuTemperatureC;
            CpuLoadPercent            = source.CpuLoadPercent;
            CpuPowerWatts             = source.CpuPowerWatts;
            CpuCoreClocksMhz          = new List<double>(source.CpuCoreClocksMhz);
            GpuTemperatureC           = source.GpuTemperatureC;
            GpuLoadPercent            = source.GpuLoadPercent;
            GpuVramUsageMb            = source.GpuVramUsageMb;
            RamUsageGb                = source.RamUsageGb;
            RamTotalGb                = source.RamTotalGb;
            FanRpm                    = source.FanRpm;
            CpuTemperatureState       = source.CpuTemperatureState;
            CpuPowerState             = source.CpuPowerState;
            GpuTemperatureState       = source.GpuTemperatureState;
            Fan1RpmState              = source.Fan1RpmState;
            Fan2RpmState              = source.Fan2RpmState;
            Fan1Rpm                   = source.Fan1Rpm;
            Fan2Rpm                   = source.Fan2Rpm;
            SsdTemperatureC           = source.SsdTemperatureC;
            DiskUsagePercent          = source.DiskUsagePercent;
            GpuPowerWatts             = source.GpuPowerWatts;
            GpuClockMhz               = source.GpuClockMhz;
            GpuMemoryClockMhz         = source.GpuMemoryClockMhz;
            GpuVramTotalMb            = source.GpuVramTotalMb;
            GpuFanPercent             = source.GpuFanPercent;
            GpuHotspotTemperatureC    = source.GpuHotspotTemperatureC;
            GpuName                   = source.GpuName;
            GpuVoltageV               = source.GpuVoltageV;
            GpuCurrentA               = source.GpuCurrentA;
            BatteryChargePercent      = source.BatteryChargePercent;
            IsOnAcPower               = source.IsOnAcPower;
            BatteryDischargeRateW     = source.BatteryDischargeRateW;
            BatteryTimeRemaining      = source.BatteryTimeRemaining;
            IsCpuThermalThrottling    = source.IsCpuThermalThrottling;
            IsCpuPowerThrottling      = source.IsCpuPowerThrottling;
            IsGpuThermalThrottling    = source.IsGpuThermalThrottling;
            IsGpuPowerThrottling      = source.IsGpuPowerThrottling;
        }
    }
}
