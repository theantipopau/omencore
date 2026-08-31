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
        private readonly Hardware.HpWmiBios? _wmiBios;
        private bool _gpuModeSupported = false;
        private string _unsupportedReason = "";

        /// <summary>
        /// <paramref name="wmiBios"/> is optional so the existing call sites - and the tests - keep
        /// working without it. When supplied, <see cref="DetectCurrentMode"/> asks the firmware
        /// instead of inferring the mode from which adapter happens to be painting a display.
        /// </summary>
        public GpuSwitchService(LoggingService logging, Hardware.HpWmiBios? wmiBios = null)
        {
            _logging = logging;
            _wmiBios = wmiBios;
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
                
                // Allow on OMEN models - also check for HP codenames used on replacement motherboards
                var modelUpper = model.ToUpperInvariant();
                bool isOmenModel = modelUpper.Contains("OMEN") || 
                                   modelUpper.Contains("THETIGER") ||  // HP codename for OMEN
                                   modelUpper.Contains("DRAGONFIRE") || // HP codename variant
                                   modelUpper.Contains("SHADOWCAT");    // HP codename variant
                
                if (!isOmenModel)
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
        /// Detect the current GPU mode, preferring the firmware's own answer over inference.
        ///
        /// <para><b>Why the firmware comes first.</b> The adapter-activity methods below decide the
        /// mode from which GPU is currently driving a display, and a healthy Optimus laptop spends
        /// most of its time with the dGPU powered down by RTD3 - <c>Win32_VideoController</c> reports
        /// it <c>Availability: 8</c> (Off Line) with no resolution, refresh rate or bit depth. That is
        /// indistinguishable, to those methods, from a machine whose dGPU is switched off in firmware,
        /// so Hybrid gets reported as Integrated whenever the dGPU happens to be asleep. Waking it to
        /// find out would be worse than the wrong answer.</para>
        ///
        /// <para>Adapter activity stays as a fallback and as corroboration, because a zero reply from
        /// <c>Legacy 0x52</c> is not unambiguous either: zero decodes to Hybrid and is also what an
        /// ACPI timeout leaves in the buffer (see the remark on <c>HpWmiBios.SendBiosCommand</c>). So
        /// a firmware Hybrid reading is cross-checked, and only a disagreement is logged - the
        /// firmware still wins, since a sleeping dGPU is the far more likely explanation.</para>
        /// </summary>
        public GpuSwitchMode DetectCurrentMode()
        {
            try
            {
                // Method 0: ask the firmware. Authoritative for Discrete/Optimus; cross-checked for
                // Hybrid, which shares its encoding with an ACPI timeout.
                var firmwareMode = DetectFirmwareGpuMode();
                if (firmwareMode.HasValue)
                    return firmwareMode.Value;

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

        /// <summary>
        /// The firmware's own GPU mode via <c>Legacy 0x52</c>, or null when it cannot answer or
        /// answers something this maps no meaning onto.
        ///
        /// Returning null on an unrecognised byte is deliberate rather than defensive. The firmware
        /// enum covers Hybrid, Discrete and Optimus; a machine routed to iGPU-only (the BIOS calls it
        /// UMA on board 8D87) is a state <c>GpuMode</c> does not declare, and no capture has pinned
        /// what 0x52 reads there. Falling through to adapter inference is the honest answer for a byte
        /// nobody has mapped - and inference is at its most reliable in exactly that case, because a
        /// machine with no usable dGPU really does have only one adapter driving displays.
        /// </summary>
        /// <summary>
        /// Map the firmware's <see cref="Hardware.HpWmiBios.GpuMode"/> onto the UI's
        /// <see cref="GpuSwitchMode"/>, or null for a value this does not map.
        ///
        /// Static and public so the mapping is test-covered without a WMI round trip, following the
        /// same pattern as <c>HpWmiBios.DecodeAdapterData</c> and <c>DecodeGpuMode</c>.
        /// </summary>
        public static GpuSwitchMode? MapFirmwareGpuMode(Hardware.HpWmiBios.GpuMode mode) => mode switch
        {
            Hardware.HpWmiBios.GpuMode.Discrete => GpuSwitchMode.Discrete,
            // Optimus is a hybrid arrangement with dGPU-direct display routing available;
            // GpuSwitchMode has no separate member, and Hybrid is what the UI means by it.
            Hardware.HpWmiBios.GpuMode.Optimus => GpuSwitchMode.Hybrid,
            Hardware.HpWmiBios.GpuMode.Hybrid => GpuSwitchMode.Hybrid,
            _ => null
        };

        private GpuSwitchMode? DetectFirmwareGpuMode()
        {
            if (_wmiBios == null) return null;

            try
            {
                var mode = _wmiBios.GetGpuMode();
                if (mode == null) return null;

                var mapped = MapFirmwareGpuMode(mode.Value);
                if (mapped == null) return null;

                // A Hybrid reading is the ambiguous one - 0x00 is also what an ACPI timeout leaves
                // behind. Corroborate it, but do not overturn it: a dGPU asleep under RTD3 is the
                // ordinary reason the adapter check would disagree here.
                if (mode.Value == Hardware.HpWmiBios.GpuMode.Hybrid)
                {
                    var inferred = DetectNvidiaOptimusMode();
                    if (inferred.HasValue && inferred.Value != GpuSwitchMode.Hybrid)
                    {
                        _logging.Info(
                            $"GPU mode: firmware reports Hybrid, adapter activity suggests {inferred.Value} " +
                            "- using the firmware's answer (a dGPU idled by RTD3 reads as inactive)");
                    }
                    else
                    {
                        _logging.Info("Detected GPU mode via firmware (Legacy 0x52): Hybrid");
                    }

                    return GpuSwitchMode.Hybrid;
                }

                _logging.Info($"Detected GPU mode via firmware (Legacy 0x52): {mode.Value}");
                return mapped.Value;
            }
            catch (Exception ex)
            {
                _logging.Debug($"Firmware GPU mode query failed, falling back to adapter inference: {ex.Message}");
                return null;
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
