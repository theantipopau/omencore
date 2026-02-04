using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty; // "NVIDIA", "AMD", "Intel", or "Unknown"
        public string MemoryFormatted { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Individual dependency check result.
    /// </summary>
    public class DependencyCheck
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsDetected { get; set; }
        public bool IsRequired { get; set; }
        public bool IsOptional { get; set; }
        public string Status { get; set; } = string.Empty; // "OK", "Missing", "Optional"
        public string Details { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Overall standalone status summary.
    /// </summary>
    public enum StandaloneStatus
    {
        /// <summary>All required components present, no external dependencies.</summary>
        OK,
        /// <summary>Core features work but some optional features unavailable.</summary>
        Degraded,
        /// <summary>Critical dependencies missing, some features may not work.</summary>
        Limited,
        /// <summary>Cannot operate - missing essential components.</summary>
        Failed
    }
    
    /// <summary>
    /// Full dependency audit result.
    /// </summary>
    public class DependencyAudit
    {
        public StandaloneStatus Status { get; set; } = StandaloneStatus.OK;
        public string StatusText { get; set; } = "Standalone";
        public string StatusColor { get; set; } = "#00FF88"; // Green
        public List<DependencyCheck> Checks { get; set; } = new();
        public DateTime AuditTime { get; set; } = DateTime.Now;
        public string Summary { get; set; } = string.Empty;
        
        /// <summary>
        /// True if OmenCore can operate fully without external HP software.
        /// </summary>
        public bool IsFullyStandalone => Status == StandaloneStatus.OK;
        
        /// <summary>
        /// Count of missing optional dependencies.
        /// </summary>
        public int MissingOptionalCount => Checks.FindAll(c => c.IsOptional && !c.IsDetected).Count;
        
        /// <summary>
        /// Count of missing required dependencies.
        /// </summary>
        public int MissingRequiredCount => Checks.FindAll(c => c.IsRequired && !c.IsDetected).Count;
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
        public bool IsHpVictus { get; set; }
        public bool IsHpSpectre { get; set; }
        
        /// <summary>
        /// True if this is any HP Gaming laptop (OMEN or Victus)
        /// </summary>
        public bool IsHpGaming => IsHpOmen || IsHpVictus;
        
        // HP-specific properties for BIOS/driver updates (v1.1)
        public string BiosVersion { get; set; } = string.Empty;
        public string BiosDate { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string SystemSku { get; set; } = string.Empty;  // HP Product ID (e.g., "6Y7K8PA#ABG")
        public string ProductName { get; set; } = string.Empty; // Full product name from BIOS
        public string SystemFamily { get; set; } = string.Empty; // e.g., "OMEN by HP Laptop 16-wd0000"
    }
}
