using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class SystemInfoService
    {
        private readonly LoggingService _logging;
        private SystemInfo? _cachedInfo;
        private DependencyAudit? _cachedAudit;
        
        public SystemInfoService(LoggingService logging)
        {
            _logging = logging;
        }
        
        /// <summary>
        /// Perform a dependency audit to check for standalone operation.
        /// </summary>
        public DependencyAudit PerformDependencyAudit()
        {
            if (_cachedAudit != null && (DateTime.Now - _cachedAudit.AuditTime).TotalMinutes < 5)
                return _cachedAudit;
                
            _logging.Info("═══════════════════════════════════════════════════");
            _logging.Info("Starting Standalone Dependency Audit...");
            
            var audit = new DependencyAudit();
            
            // Check 1: HP WMI BIOS Provider (required for fan control)
            audit.Checks.Add(CheckHpWmiBios());
            
            // Check 2: OMEN Gaming Hub service
            audit.Checks.Add(CheckOmenGamingHub());
            
            // Check 3: HP System Event Utility
            audit.Checks.Add(CheckHpSystemEventUtility());
            
            // Check 4: LibreHardwareMonitor availability
            audit.Checks.Add(CheckLibreHardwareMonitor());
            
            // Check 5: PawnIO driver
            audit.Checks.Add(CheckPawnIODriver());
            
            // Check 6: WinRing0 driver (legacy, not needed)
            audit.Checks.Add(CheckWinRing0Driver());
            
            // Determine overall status
            var requiredMissing = audit.Checks.Where(c => c.IsRequired && !c.IsDetected).ToList();
            var optionalMissing = audit.Checks.Where(c => c.IsOptional && !c.IsDetected).ToList();
            
            if (requiredMissing.Any())
            {
                audit.Status = StandaloneStatus.Limited;
                audit.StatusText = "Limited";
                audit.StatusColor = "#FF6B6B"; // Red
                audit.Summary = $"Missing {requiredMissing.Count} required component(s): {string.Join(", ", requiredMissing.Select(c => c.Name))}";
            }
            else if (optionalMissing.Count >= 2)
            {
                audit.Status = StandaloneStatus.Degraded;
                audit.StatusText = "Degraded";
                audit.StatusColor = "#FFD93D"; // Yellow
                audit.Summary = $"Fully standalone, but {optionalMissing.Count} optional component(s) unavailable";
            }
            else
            {
                audit.Status = StandaloneStatus.OK;
                audit.StatusText = "Standalone";
                audit.StatusColor = "#00FF88"; // Green
                audit.Summary = "OmenCore is running fully standalone without HP dependencies";
            }
            
            _logging.Info($"Dependency Audit Complete: {audit.StatusText}");
            _logging.Info($"  Summary: {audit.Summary}");
            _logging.Info("═══════════════════════════════════════════════════");
            
            _cachedAudit = audit;
            return audit;
        }
        
        private DependencyCheck CheckHpWmiBios()
        {
            var check = new DependencyCheck
            {
                Name = "HP WMI BIOS",
                Description = "HP WMI interface for fan/thermal control",
                IsRequired = true,
                IsOptional = false
            };
            
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM hpqBIntM");
                var results = searcher.Get();
                check.IsDetected = results.Count > 0;
                check.Status = check.IsDetected ? "OK" : "Missing";
                check.Details = check.IsDetected 
                    ? "HP WMI BIOS interface available" 
                    : "HP WMI BIOS not found - fan control will be limited";
            }
            catch
            {
                check.IsDetected = false;
                check.Status = "Missing";
                check.Details = "HP WMI BIOS query failed";
            }
            
            _logging.Info($"  [{(check.IsDetected ? "✓" : "✗")}] {check.Name}: {check.Status}");
            return check;
        }
        
        private DependencyCheck CheckOmenGamingHub()
        {
            var check = new DependencyCheck
            {
                Name = "OMEN Gaming Hub",
                Description = "HP OMEN Gaming Hub (OGH) - NOT required for standalone operation",
                IsRequired = false,
                IsOptional = true
            };
            
            try
            {
                // Check for OGH service
                var services = ServiceController.GetServices();
                var oghService = services.FirstOrDefault(s => 
                    s.ServiceName.Contains("OMEN", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("HPOmen", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("OmenAgent", StringComparison.OrdinalIgnoreCase));
                    
                if (oghService != null)
                {
                    check.IsDetected = true;
                    check.Status = oghService.Status == ServiceControllerStatus.Running ? "Running" : "Stopped";
                    check.Details = $"OGH service found: {oghService.ServiceName} ({check.Status})";
                }
                else
                {
                    // Check for OGH process
                    var oghProcess = Process.GetProcesses().FirstOrDefault(p =>
                        p.ProcessName.Contains("OmenCommand", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Contains("HPOmen", StringComparison.OrdinalIgnoreCase));
                        
                    check.IsDetected = oghProcess != null;
                    check.Status = check.IsDetected ? "Running" : "Not Installed";
                    check.Details = check.IsDetected 
                        ? "OGH process detected - OmenCore can coexist"
                        : "OGH not installed - OmenCore running standalone ✓";
                }
            }
            catch (Exception ex)
            {
                check.IsDetected = false;
                check.Status = "Unknown";
                check.Details = $"Could not check OGH status: {ex.Message}";
            }
            
            _logging.Info($"  [{(check.IsDetected ? "!" : "✓")}] {check.Name}: {check.Status}");
            return check;
        }
        
        private DependencyCheck CheckHpSystemEventUtility()
        {
            var check = new DependencyCheck
            {
                Name = "HP System Event Utility",
                Description = "Required for OMEN key handling on some models",
                IsRequired = false,
                IsOptional = true
            };
            
            try
            {
                var services = ServiceController.GetServices();
                var hpSeu = services.FirstOrDefault(s => 
                    s.ServiceName.Equals("HPSysEventSvc", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("HPSystemEvent", StringComparison.OrdinalIgnoreCase));
                    
                check.IsDetected = hpSeu != null;
                check.Status = check.IsDetected 
                    ? (hpSeu!.Status == ServiceControllerStatus.Running ? "Running" : "Stopped")
                    : "Not Installed";
                check.Details = check.IsDetected
                    ? $"HP System Event Utility: {check.Status}"
                    : "HP System Event Utility not installed - OMEN key may use fallback";
            }
            catch
            {
                check.IsDetected = false;
                check.Status = "Unknown";
                check.Details = "Could not check HP System Event Utility";
            }
            
            _logging.Info($"  [{(check.IsDetected ? "✓" : "○")}] {check.Name}: {check.Status}");
            return check;
        }
        
        private DependencyCheck CheckLibreHardwareMonitor()
        {
            var check = new DependencyCheck
            {
                Name = "LibreHardwareMonitor",
                Description = "Hardware monitoring library (no longer required — self-sustaining mode)",
                IsRequired = false,
                IsOptional = true
            };
            
            try
            {
                // Check if LHM DLL exists (optional enhancement, not required)
                var lhmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibreHardwareMonitorLib.dll");
                check.IsDetected = File.Exists(lhmPath);
                check.Status = check.IsDetected ? "OK" : "Not Needed";
                check.Details = check.IsDetected
                    ? "LibreHardwareMonitor library present (optional)"
                    : "Not required — OmenCore uses self-sustaining monitoring (WMI BIOS + NVAPI)";
            }
            catch
            {
                check.IsDetected = false;
                check.Status = "Not Needed";
                check.Details = "Not required — OmenCore uses self-sustaining monitoring";
            }
            
            var symbol = check.IsDetected ? "✓" : "○";
            _logging.Info($"  [{symbol}] {check.Name}: {check.Status}");
            return check;
        }
        
private DependencyCheck CheckPawnIODriver()
        {
            var check = new DependencyCheck
            {
                Name = "PawnIO Driver",
                Description = "Ring0 driver for EC access (optional)",
                IsRequired = false,
                IsOptional = true
            };
            
            try
            {
                // Method 1: Check for PawnIO service
                var services = ServiceController.GetServices();
                var pawnio = services.FirstOrDefault(s => 
                    s.ServiceName.Equals("PawnIO", StringComparison.OrdinalIgnoreCase));
                
                if (pawnio != null)
                {
                    check.IsDetected = true;
                    check.Status = pawnio.Status == ServiceControllerStatus.Running ? "Running" : "Stopped";
                    check.Details = $"PawnIO driver: {check.Status} - EC access available";
                }
                else
                {
                    // Method 2: Check registry (same as CapabilityDetectionService)
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                    if (key != null)
                    {
                        check.IsDetected = true;
                        check.Status = "Installed";
                        check.Details = "PawnIO installed (found in registry)";
                    }
                    else
                    {
                        // Method 3: Check default installation path
                        string defaultPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                            "PawnIO", "PawnIOLib.dll");
                        if (System.IO.File.Exists(defaultPath))
                        {
                            check.IsDetected = true;
                            check.Status = "Installed";
                            check.Details = "PawnIO installed (found at default path)";
                        }
                        else
                        {
                            check.IsDetected = false;
                            check.Status = "Not Installed";
                            check.Details = "PawnIO not installed - using WMI fallback";
                        }
                    }
                }
            }
            catch
            {
                check.IsDetected = false;
                check.Status = "Unknown";
                check.Details = "Could not check PawnIO status";
            }
            
            _logging.Info($"  [{(check.IsDetected ? "✓" : "○")}] {check.Name}: {check.Status}");
            return check;
        }
        
        private DependencyCheck CheckWinRing0Driver()
        {
            var check = new DependencyCheck
            {
                Name = "WinRing0 Driver",
                Description = "Legacy Ring0 driver (NOT used by OmenCore)",
                IsRequired = false,
                IsOptional = false // We don't use this
            };
            
            try
            {
                // Check for WinRing0 service
                var services = ServiceController.GetServices();
                var winring0 = services.FirstOrDefault(s => 
                    s.ServiceName.Contains("WinRing", StringComparison.OrdinalIgnoreCase));
                    
                check.IsDetected = winring0 != null;
                check.Status = check.IsDetected ? "Detected" : "Not Present";
                check.Details = check.IsDetected
                    ? "WinRing0 detected - OmenCore does not use this driver"
                    : "WinRing0 not present - this is expected ✓";
            }
            catch
            {
                check.IsDetected = false;
                check.Status = "Unknown";
                check.Details = "Could not check WinRing0 status";
            }
            
            _logging.Info($"  [{(!check.IsDetected ? "✓" : "○")}] {check.Name}: {check.Status}");
            return check;
        }
        
        /// <summary>
        /// Clear the dependency audit cache to force a fresh check.
        /// </summary>
        public void ClearAuditCache()
        {
            _cachedAudit = null;
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

                        // detect Strix Point (14th Gen Intel) by name pattern
                        _cachedInfo.IsStrixPointCpu = IsStrixPointCpu(cpuName);
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
                        var gpuInfo = new GpuInfo
                        {
                            Name = gpu["Name"]?.ToString()?.Trim() ?? "Unknown GPU"
                        };

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
                        
                        // Get driver version from WMI
                        var driverVersion = gpu["DriverVersion"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(driverVersion))
                        {
                            // For NVIDIA, format the driver version (e.g., "32.0.15.6614" -> "566.14")
                            if (gpuInfo.Vendor == "NVIDIA" && driverVersion.Contains("."))
                            {
                                gpuInfo.DriverVersion = FormatNvidiaDriverVersion(driverVersion);
                            }
                            else
                            {
                                gpuInfo.DriverVersion = driverVersion;
                            }
                        }
                            
                        _cachedInfo.Gpus.Add(gpuInfo);
                    }
                }
                
                // Log GPU collection results
                if (_cachedInfo.Gpus.Count > 0)
                {
                    _logging.Info($"Collected {_cachedInfo.Gpus.Count} GPU(s) from WMI:");
                    foreach (var g in _cachedInfo.Gpus)
                    {
                        _logging.Info($"  → {g.Name} (Vendor: {g.Vendor}, Driver: {g.DriverVersion})");
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
                        
                        // Detect HP Gaming laptops (OMEN and Victus) and HP Spectre
                        var manufacturer = _cachedInfo.Manufacturer.ToLowerInvariant();
                        var model = _cachedInfo.Model.ToLowerInvariant();
                        var isHp = manufacturer.Contains("hp") || manufacturer.Contains("hewlett");
                        
                        // Also check for generic HP motherboard names (replacement cases)
                        // "Thetiger" is an HP internal codename used on some replaced motherboards
                        bool hasOmenCodename = model.Contains("thetiger") || model.Contains("dragonfire") || 
                                               model.Contains("shadowcat") || model.Contains("victusdragon");
                        
                        _cachedInfo.IsHpOmen = isHp && (model.Contains("omen") || hasOmenCodename);
                        _cachedInfo.IsHpVictus = isHp && model.Contains("victus");
                        _cachedInfo.IsHpSpectre = isHp && model.Contains("spectre");
                        
                        if (_cachedInfo.IsHpOmen)
                        {
                            if (hasOmenCodename && !model.Contains("omen"))
                                _logging.Info($"HP OMEN system detected (via codename): {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                            else
                                _logging.Info($"HP OMEN system detected: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                        }
                        else if (_cachedInfo.IsHpVictus)
                            _logging.Info($"HP Victus system detected: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                        else if (_cachedInfo.IsHpSpectre)
                            _logging.Info($"HP Spectre system detected: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                        else if (isHp)
                            _logging.Warn($"Non-gaming HP system: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
                        else
                            _logging.Warn($"Non-HP system: {_cachedInfo.Manufacturer} {_cachedInfo.Model}");
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
                
                _logging.Info($"System Info: {_cachedInfo.CpuName}, {_cachedInfo.CpuCores} cores / {_cachedInfo.CpuThreads} threads, {_cachedInfo.RamSizeFormatted}, {_cachedInfo.Gpus.Count} GPU(s)");
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
                size /= 1024;
            }
            
            return $"{size:F0} {sizes[order]}";
        }
        
        /// <summary>
        /// Formats NVIDIA driver version from WMI format (e.g., "32.0.15.6614") to user-friendly format (e.g., "566.14").
        /// </summary>
        private string FormatNvidiaDriverVersion(string wmiVersion)
        {
            try
            {
                // NVIDIA WMI driver version format: "32.0.15.6614" or "31.0.15.5525"
                // The last 5 digits represent the actual driver version: XXXYZ -> XXX.YZ
                var parts = wmiVersion.Split('.');
                if (parts.Length >= 4)
                {
                    var lastPart = parts[2] + parts[3]; // Combine last two parts
                    if (lastPart.Length >= 5)
                    {
                        // Take last 5 digits and format as XXX.YY
                        var versionDigits = lastPart.Substring(lastPart.Length - 5);
                        var major = versionDigits.Substring(0, 3).TrimStart('0');
                        var minor = versionDigits.Substring(3, 2);
                        if (string.IsNullOrEmpty(major)) major = "0";
                        return $"{major}.{minor}";
                    }
                }
                return wmiVersion;
            }
            catch
            {
                return wmiVersion;
            }
        }
        
        public void ClearCache()
        {
            _cachedInfo = null;
        }

        /// <summary>
        /// Determines if a CPU name string corresponds to Intel Strix Point (14th Gen mobile).
        /// Exposed as static helper for unit tests.
        /// </summary>
        public static bool IsStrixPointCpu(string cpuName)
        {
            if (string.IsNullOrWhiteSpace(cpuName)) return false;
            var lower = cpuName.ToLowerInvariant();
            return lower.Contains("strix point") || lower.Contains("14th gen");
        }
    }
}
