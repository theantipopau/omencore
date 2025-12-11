using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class SystemInfoService
    {
        private readonly LoggingService _logging;
        private SystemInfo? _cachedInfo;
        
        public SystemInfoService(LoggingService logging)
        {
            _logging = logging;
        }
        
        public SystemInfo GetSystemInfo()
        {
            if (_cachedInfo != null)
                return _cachedInfo;
                
            _cachedInfo = new SystemInfo();
            
            try
            {
                // CPU Information
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    var cpu = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (cpu != null)
                    {
                        _cachedInfo.CpuName = cpu["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                        _cachedInfo.CpuCores = Convert.ToInt32(cpu["NumberOfCores"] ?? 0);
                        _cachedInfo.CpuThreads = Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? 0);
                        
                        // Detect CPU vendor
                        var cpuName = _cachedInfo.CpuName.ToLowerInvariant();
                        if (cpuName.Contains("intel"))
                            _cachedInfo.CpuVendor = "Intel";
                        else if (cpuName.Contains("amd"))
                            _cachedInfo.CpuVendor = "AMD";
                        else
                            _cachedInfo.CpuVendor = "Unknown";
                    }
                }
                
                // RAM Information
                long totalRamBytes = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                {
                    foreach (var ram in searcher.Get().Cast<ManagementObject>())
                    {
                        totalRamBytes += Convert.ToInt64(ram["Capacity"] ?? 0);
                    }
                }
                _cachedInfo.RamSizeBytes = totalRamBytes;
                _cachedInfo.RamSizeFormatted = FormatBytes(totalRamBytes);
                
                // GPU Information - collect all GPUs
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (var gpu in searcher.Get().Cast<ManagementObject>())
                    {
                        var gpuInfo = new GpuInfo();
                        gpuInfo.Name = gpu["Name"]?.ToString()?.Trim() ?? "Unknown GPU";
                        
                        var adapterRam = gpu["AdapterRAM"];
                        if (adapterRam != null)
                        {
                            var vramBytes = Convert.ToInt64(adapterRam);
                            gpuInfo.MemoryFormatted = FormatBytes(vramBytes);
                        }
                        
                        // Detect GPU vendor
                        var gpuName = gpuInfo.Name.ToLowerInvariant();
                        if (gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx"))
                            gpuInfo.Vendor = "NVIDIA";
                        else if (gpuName.Contains("amd") || gpuName.Contains("radeon"))
                            gpuInfo.Vendor = "AMD";
                        else if (gpuName.Contains("intel") || gpuName.Contains("arc") || gpuName.Contains("uhd") || gpuName.Contains("iris"))
                            gpuInfo.Vendor = "Intel";
                        else
                            gpuInfo.Vendor = "Unknown";
                            
                        _cachedInfo.Gpus.Add(gpuInfo);
                    }
                }
                
                // Computer System Information
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                {
                    var system = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (system != null)
                    {
                        _cachedInfo.Manufacturer = system["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                        _cachedInfo.Model = system["Model"]?.ToString()?.Trim() ?? "Unknown";
                        _cachedInfo.SystemFamily = system["SystemFamily"]?.ToString()?.Trim() ?? "";
                        
                        // Detect if this is an HP Omen system
                        var manufacturer = _cachedInfo.Manufacturer.ToLowerInvariant();
                        var model = _cachedInfo.Model.ToLowerInvariant();
                        _cachedInfo.IsHpOmen = (manufacturer.Contains("hp") || manufacturer.Contains("hewlett")) && model.Contains("omen");
                        
                        if (_cachedInfo.IsHpOmen)
                            _logging.Info($"HP Omen system detected: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                        else
                            _logging.Warn($"Non-HP Omen system: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                    }
                }
                
                // BIOS Information (for HP BIOS update checking)
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
                {
                    var bios = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (bios != null)
                    {
                        _cachedInfo.BiosVersion = bios["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                        _cachedInfo.BiosDate = bios["ReleaseDate"]?.ToString()?.Trim() ?? "";
                        _cachedInfo.SerialNumber = bios["SerialNumber"]?.ToString()?.Trim() ?? "";
                        _logging.Info($"BIOS: {_cachedInfo.BiosVersion} (Released: {_cachedInfo.BiosDate})");
                    }
                }
                
                // Baseboard Information (for HP System SKU)
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard"))
                {
                    var baseboard = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (baseboard != null)
                    {
                        _cachedInfo.ProductName = baseboard["Product"]?.ToString()?.Trim() ?? "";
                    }
                }
                
                // Additional product info from ComputerSystemProduct
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystemProduct"))
                {
                    var product = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (product != null)
                    {
                        _cachedInfo.SystemSku = product["SKUNumber"]?.ToString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(_cachedInfo.SystemSku))
                            _cachedInfo.SystemSku = product["IdentifyingNumber"]?.ToString()?.Trim() ?? "";
                        _logging.Info($"System SKU: {_cachedInfo.SystemSku}");
                    }
                }
                
                // OS Information
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    var os = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (os != null)
                    {
                        _cachedInfo.OsVersion = os["Caption"]?.ToString()?.Trim() ?? "Unknown OS";
                    }
                }
                
                _logging.Info($"System Info: {_cachedInfo.CpuName}, {_cachedInfo.RamSizeFormatted}, {_cachedInfo.Gpus.Count} GPU(s)");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to retrieve system information: {ex.Message}");
            }
            
            return _cachedInfo;
        }
        
        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            
            return $"{size:F0} {sizes[order]}";
        }
        
        public void ClearCache()
        {
            _cachedInfo = null;
        }
    }
}
