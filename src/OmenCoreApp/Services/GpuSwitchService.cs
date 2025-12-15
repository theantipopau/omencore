using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class GpuSwitchService
    {
        private readonly LoggingService _logging;
        private bool _gpuModeSupported = false;
        private string _unsupportedReason = "";

        public GpuSwitchService(LoggingService logging)
        {
            _logging = logging;
            CheckGpuModeSwitchingSupport();
        }
        
        /// <summary>
        /// Check if GPU mode switching is supported on this system.
        /// Only enable on systems with confirmed HP WMI BIOS support.
        /// </summary>
        private void CheckGpuModeSwitchingSupport()
        {
            try
            {
                // Only allow GPU switching on HP OMEN systems with confirmed WMI support
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                var systems = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                
                if (systems == null)
                {
                    _unsupportedReason = "Could not detect system information";
                    return;
                }
                
                var manufacturer = systems["Manufacturer"]?.ToString() ?? "";
                var model = systems["Model"]?.ToString() ?? "";
                
                if (!manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase))
                {
                    _unsupportedReason = "GPU mode switching only supported on HP systems";
                    return;
                }
                
                // Only allow on OMEN models - Transcend, Victus, etc. may not have proper support
                if (!model.Contains("OMEN", StringComparison.OrdinalIgnoreCase))
                {
                    _unsupportedReason = $"GPU mode switching only supported on HP OMEN models (detected: {model})";
                    _logging.Info(_unsupportedReason);
                    return;
                }
                
                // Check if HP WMI BIOS interface for GPU mode exists
                if (!HasHpGpuModeWmiSupport())
                {
                    _unsupportedReason = "HP BIOS does not support GPU mode switching via WMI";
                    return;
                }
                
                _gpuModeSupported = true;
                _logging.Info("✓ GPU mode switching supported on this HP OMEN system");
            }
            catch (Exception ex)
            {
                _unsupportedReason = $"Error checking GPU mode support: {ex.Message}";
                _logging.Error(_unsupportedReason, ex);
            }
        }
        
        private bool HasHpGpuModeWmiSupport()
        {
            try
            {
                // Check for HP BIOS interface that actually supports GPU mode
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM HPBIOS_BIOSSettingInterface");
                var results = searcher.Get();
                
                if (results.Count == 0)
                    return false;
                    
                // Try to enumerate available settings to check for GPU mode support
                using var enumSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM HPBIOS_BIOSEnumeration");
                var enumResults = enumSearcher.Get();
                
                foreach (ManagementObject obj in enumResults)
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Optimus", StringComparison.OrdinalIgnoreCase))
                    {
                        _logging.Info($"Found GPU-related BIOS setting: {name}");
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Check if GPU mode switching is supported
        /// </summary>
        public bool IsSupported => _gpuModeSupported;
        
        /// <summary>
        /// Reason why GPU mode switching is not supported (if applicable)
        /// </summary>
        public string UnsupportedReason => _unsupportedReason;

        /// <summary>
        /// Detect current GPU mode through WMI and NVIDIA/AMD control panels
        /// </summary>
        public GpuSwitchMode DetectCurrentMode()
        {
            try
            {
                // Method 1: Check NVIDIA Optimus status via WMI
                var nvidiaMode = DetectNvidiaOptimusMode();
                if (nvidiaMode.HasValue)
                {
                    _logging.Info($"Detected GPU mode via NVIDIA: {nvidiaMode.Value}");
                    return nvidiaMode.Value;
                }

                // Method 2: Check AMD Switchable Graphics via WMI
                var amdMode = DetectAmdSwitchableMode();
                if (amdMode.HasValue)
                {
                    _logging.Info($"Detected GPU mode via AMD: {amdMode.Value}");
                    return amdMode.Value;
                }

                // Method 3: Check via video controllers (active GPU count)
                var activeDisplayControllerCount = CountActiveDisplayControllers();
                if (activeDisplayControllerCount > 1)
                {
                    _logging.Info($"Multiple active display controllers detected ({activeDisplayControllerCount}) - assuming Hybrid mode");
                    return GpuSwitchMode.Hybrid;
                }

                // Default: assume hybrid if multiple GPUs exist
                _logging.Warn("Could not definitively detect GPU mode - defaulting to Hybrid");
                return GpuSwitchMode.Hybrid;
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to detect GPU mode", ex);
                return GpuSwitchMode.Hybrid;
            }
        }

        private static bool IsDisplayActive(ManagementObject gpu)
        {
            try
            {
                // These properties tend to be non-null/positive on the adapter currently driving a display.
                var h = gpu["CurrentHorizontalResolution"];
                var v = gpu["CurrentVerticalResolution"];
                var rr = gpu["CurrentRefreshRate"];
                var bpp = gpu["CurrentBitsPerPixel"];

                int hi = h != null ? Convert.ToInt32(h) : 0;
                int vi = v != null ? Convert.ToInt32(v) : 0;
                int rri = rr != null ? Convert.ToInt32(rr) : 0;
                int bppi = bpp != null ? Convert.ToInt32(bpp) : 0;

                if (hi > 0 && vi > 0)
                    return true;
                if (rri > 0 && bppi > 0)
                    return true;

                var modeDesc = gpu["VideoModeDescription"]?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(modeDesc);
            }
            catch
            {
                return false;
            }
        }

        private GpuSwitchMode? DetectNvidiaOptimusMode()
        {
            try
            {
                // Check NVIDIA GPU status
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'");
                var nvidiaGpus = searcher.Get().Cast<ManagementObject>().ToList();

                if (nvidiaGpus.Count == 0)
                    return null; // No NVIDIA GPU

                // Check for Intel iGPU - common in hybrid configurations
                using var intelSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%Intel%'");
                var intelGpus = intelSearcher.Get().Cast<ManagementObject>().ToList();
                
                // Also check for AMD iGPU (Radeon Graphics, 610M, 660M, 680M, 740M, 760M, 780M, 880M, 890M, etc.)
                // These are integrated graphics in AMD Ryzen APUs paired with NVIDIA dGPU in newer OMEN laptops
                // Common patterns: "AMD Radeon Graphics", "AMD Radeon 780M Graphics", "AMD Radeon(TM) Graphics"
                using var amdIgpuSearcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_VideoController WHERE " +
                    "(Name LIKE '%Radeon%' OR Name LIKE '%AMD%') AND " +
                    "(Name LIKE '%Graphics%' OR Name LIKE '%610M%' OR Name LIKE '%660M%' OR Name LIKE '%680M%' OR " +
                    "Name LIKE '%740M%' OR Name LIKE '%760M%' OR Name LIKE '%780M%' OR Name LIKE '%880M%' OR Name LIKE '%890M%')");
                var amdIgpus = amdIgpuSearcher.Get().Cast<ManagementObject>().ToList();
                
                // Log GPU info for diagnostics
                foreach (var nvidia in nvidiaGpus)
                {
                    var name = nvidia["Name"]?.ToString() ?? "Unknown";
                    var status = nvidia["Status"]?.ToString() ?? "Unknown";
                    var availability = nvidia["Availability"]?.ToString() ?? "Unknown";
                    _logging.Info($"NVIDIA GPU: {name}, Status: {status}, Availability: {availability}");
                }
                
                foreach (var intel in intelGpus)
                {
                    var name = intel["Name"]?.ToString() ?? "Unknown";
                    var status = intel["Status"]?.ToString() ?? "Unknown";
                    var availability = intel["Availability"]?.ToString() ?? "Unknown";
                    _logging.Info($"Intel GPU: {name}, Status: {status}, Availability: {availability}");
                }
                
                foreach (var amdIgpu in amdIgpus)
                {
                    var name = amdIgpu["Name"]?.ToString() ?? "Unknown";
                    var status = amdIgpu["Status"]?.ToString() ?? "Unknown";
                    var availability = amdIgpu["Availability"]?.ToString() ?? "Unknown";
                    _logging.Info($"AMD iGPU: {name}, Status: {status}, Availability: {availability}");
                }

                // If Intel iGPU + NVIDIA dGPU exist, decide based on which adapter is actually driving a display.
                if (intelGpus.Count > 0 && nvidiaGpus.Count > 0)
                {
                    var intelDisplayActive = intelGpus.Any(IsDisplayActive);
                    var nvidiaDisplayActive = nvidiaGpus.Any(IsDisplayActive);

                    _logging.Info($"Display activity: Intel={(intelDisplayActive ? "Active" : "Inactive")}, NVIDIA={(nvidiaDisplayActive ? "Active" : "Inactive")}");

                    if (intelDisplayActive && nvidiaDisplayActive)
                        return GpuSwitchMode.Hybrid;
                    if (!intelDisplayActive && nvidiaDisplayActive)
                        return GpuSwitchMode.Discrete;
                    if (intelDisplayActive && !nvidiaDisplayActive)
                        return GpuSwitchMode.Integrated;

                    // Unknown edge case; default to Hybrid as safest assumption.
                    return GpuSwitchMode.Hybrid;
                }
                
                // If AMD iGPU (Radeon 610M, 680M, 780M) + NVIDIA dGPU exist - AMD APU + NVIDIA hybrid setup
                if (amdIgpus.Count > 0 && nvidiaGpus.Count > 0)
                {
                    var amdIgpuDisplayActive = amdIgpus.Any(IsDisplayActive);
                    var nvidiaDisplayActive = nvidiaGpus.Any(IsDisplayActive);

                    _logging.Info($"Display activity: AMD iGPU={(amdIgpuDisplayActive ? "Active" : "Inactive")}, NVIDIA={(nvidiaDisplayActive ? "Active" : "Inactive")}");

                    if (amdIgpuDisplayActive && nvidiaDisplayActive)
                        return GpuSwitchMode.Hybrid;
                    if (!amdIgpuDisplayActive && nvidiaDisplayActive)
                        return GpuSwitchMode.Discrete;
                    if (amdIgpuDisplayActive && !nvidiaDisplayActive)
                        return GpuSwitchMode.Integrated;

                    // Default to Hybrid for AMD APU + NVIDIA setup
                    return GpuSwitchMode.Hybrid;
                }

                // Only NVIDIA GPU present
                return GpuSwitchMode.Discrete;
            }
            catch (Exception ex)
            {
                _logging.Error("Error detecting NVIDIA Optimus mode", ex);
                return null;
            }
        }

        private GpuSwitchMode? DetectAmdSwitchableMode()
        {
            try
            {
                // Get all AMD GPUs
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%AMD%' OR Name LIKE '%Radeon%'");
                var amdGpus = searcher.Get().Cast<ManagementObject>().ToList();

                if (amdGpus.Count == 0)
                    return null;

                // Check for Intel iGPU first (Intel + AMD combo)
                using var intelSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%Intel%'");
                var intelGpus = intelSearcher.Get().Cast<ManagementObject>().ToList();

                // If Intel + AMD exist, decide based on active display controller.
                if (intelGpus.Count > 0 && amdGpus.Count > 0)
                {
                    var intelDisplayActive = intelGpus.Any(IsDisplayActive);
                    var amdDisplayActive = amdGpus.Any(IsDisplayActive);

                    _logging.Info($"Display activity: Intel={(intelDisplayActive ? "Active" : "Inactive")}, AMD={(amdDisplayActive ? "Active" : "Inactive")}");

                    if (intelDisplayActive && amdDisplayActive)
                        return GpuSwitchMode.Hybrid;
                    if (!intelDisplayActive && amdDisplayActive)
                        return GpuSwitchMode.Discrete;
                    if (intelDisplayActive && !amdDisplayActive)
                        return GpuSwitchMode.Integrated;
                    return GpuSwitchMode.Hybrid;
                }
                
                // AMD + AMD combo (Ryzen iGPU + Radeon dGPU) - common in OMEN 16-ap series
                // Detect by looking for "Radeon Graphics" (iGPU) vs "Radeon RX" (dGPU) patterns
                var igpuPatterns = new[] { "Radeon Graphics", "Radeon(TM) Graphics", "AMD Radeon Graphics" };
                var dgpuPatterns = new[] { "Radeon RX", "RX 6", "RX 7", "RX 8" };
                
                var amdIgpus = amdGpus.Where(g => 
                {
                    var name = g["Name"]?.ToString() ?? "";
                    return igpuPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
                }).ToList();
                
                var amdDgpus = amdGpus.Where(g => 
                {
                    var name = g["Name"]?.ToString() ?? "";
                    return dgpuPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
                           (!igpuPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)) && 
                            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
                }).ToList();
                
                // Log AMD GPU detection for debugging
                foreach (var gpu in amdGpus)
                {
                    var name = gpu["Name"]?.ToString() ?? "Unknown";
                    var status = gpu["Status"]?.ToString() ?? "Unknown";
                    var isIgpu = amdIgpus.Contains(gpu);
                    _logging.Info($"AMD GPU: {name}, Status: {status}, Type: {(isIgpu ? "iGPU" : "dGPU")}");
                }
                
                if (amdIgpus.Count > 0 && amdDgpus.Count > 0)
                {
                    var igpuDisplayActive = amdIgpus.Any(IsDisplayActive);
                    var dgpuDisplayActive = amdDgpus.Any(IsDisplayActive);
                    
                    _logging.Info($"AMD Display activity: iGPU={(igpuDisplayActive ? "Active" : "Inactive")}, dGPU={(dgpuDisplayActive ? "Active" : "Inactive")}");
                    
                    if (igpuDisplayActive && dgpuDisplayActive)
                        return GpuSwitchMode.Hybrid;
                    if (!igpuDisplayActive && dgpuDisplayActive)
                        return GpuSwitchMode.Discrete;
                    if (igpuDisplayActive && !dgpuDisplayActive)
                        return GpuSwitchMode.Integrated;
                    return GpuSwitchMode.Hybrid;
                }

                // Only AMD GPU present (single GPU or can't differentiate)
                return GpuSwitchMode.Discrete;
            }
            catch
            {
                return null;
            }
        }

        private int CountActiveDisplayControllers()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                var controllers = searcher.Get().Cast<ManagementObject>().ToList();
                return controllers.Count(IsDisplayActive);
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Switch GPU mode - requires system restart to take effect.
        /// SAFETY: Only works on HP OMEN systems with verified WMI BIOS support.
        /// </summary>
        public bool Switch(GpuSwitchMode mode)
        {
            // Safety check - don't allow switching on unsupported systems
            if (!_gpuModeSupported)
            {
                _logging.Warn($"GPU mode switching blocked - {_unsupportedReason}");
                return false;
            }
            
            try
            {
                // HP Omen systems use HP BIOS WMI for GPU mode switching
                // This is the ONLY safe method - registry and other hacks can corrupt drivers
                
                if (TrySwitchViaHpWmi(mode))
                {
                    _logging.Info($"✓ GPU mode switched to {mode} via HP WMI BIOS");
                    return true;
                }

                _logging.Warn($"GPU mode switching failed. HP BIOS WMI did not accept the change.");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to switch GPU mode to {mode}", ex);
                return false;
            }
        }

        private bool TrySwitchViaHpWmi(GpuSwitchMode mode)
        {
            try
            {
                // HP-specific WMI namespace for BIOS settings
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM HPBIOS_BIOSSettingInterface");
                var results = searcher.Get();
                
                if (results.Count == 0)
                {
                    _logging.Warn("HP BIOS WMI interface not found");
                    return false;
                }

                foreach (ManagementObject obj in results)
                {
                    var modeValue = mode switch
                    {
                        GpuSwitchMode.Discrete => "Discrete",
                        GpuSwitchMode.Integrated => "Integrated",
                        _ => "Hybrid"
                    };

                    // HP BIOS setting name varies by model - try known names
                    var settingNames = new[] { "GPU Mode", "Graphics Mode", "Switchable Graphics" };
                    
                    foreach (var setting in settingNames)
                    {
                        try
                        {
                            var inParams = obj.GetMethodParameters("SetBIOSSetting");
                            inParams["Name"] = setting;
                            inParams["Value"] = modeValue;
                            inParams["Password"] = ""; // Most systems don't have BIOS password set
                            
                            var outParams = obj.InvokeMethod("SetBIOSSetting", inParams, null);
                            var returnCode = outParams?["Return"];
                            
                            if (returnCode != null && Convert.ToUInt32(returnCode) == 0)
                            {
                                _logging.Info($"Successfully set HP BIOS setting '{setting}' to '{modeValue}'");
                                return true;
                            }
                            else
                            {
                                _logging.Info($"HP BIOS setting '{setting}' returned code: {returnCode}");
                            }
                        }
                        catch (ManagementException ex)
                        {
                            _logging.Info($"HP BIOS setting '{setting}' not available: {ex.Message}");
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"HP WMI GPU mode switch failed: {ex.Message}", ex);
                return false;
            }
        }
        
        // REMOVED: TrySwitchViaGpuControlPanel - Opening control panels doesn't actually switch modes
        // REMOVED: TrySwitchViaRegistry - Registry modifications can corrupt GPU drivers!
    }
}
