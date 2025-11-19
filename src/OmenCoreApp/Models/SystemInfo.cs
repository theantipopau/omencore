using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty; // "NVIDIA", "AMD", "Intel", or "Unknown"
        public string MemoryFormatted { get; set; } = string.Empty;
    }
    
    public class SystemInfo
    {
        public string CpuName { get; set; } = string.Empty;
        public string CpuVendor { get; set; } = string.Empty; // "Intel", "AMD", or "Unknown"
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        
        public string RamSizeFormatted { get; set; } = string.Empty; // e.g., "16 GB"
        public long RamSizeBytes { get; set; }
        
        public List<GpuInfo> Gpus { get; set; } = new List<GpuInfo>();
        
        // Legacy properties for backward compatibility (points to first GPU)
        public string GpuName => Gpus.Count > 0 ? Gpus[0].Name : "Unknown GPU";
        public string GpuVendor => Gpus.Count > 0 ? Gpus[0].Vendor : "Unknown";
        public string GpuMemoryFormatted => Gpus.Count > 0 ? Gpus[0].MemoryFormatted : "0 B";
        
        public string OsVersion { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        
        public bool IsHpOmen { get; set; }
    }
}
