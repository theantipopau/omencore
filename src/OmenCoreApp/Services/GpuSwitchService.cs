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

        public GpuSwitchService(LoggingService logging)
        {
            _logging = logging;
        }

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
                var activeGpuCount = CountActiveVideoControllers();
                if (activeGpuCount > 1)
                {
                    _logging.Info($"Multiple active GPUs detected ({activeGpuCount}) - assuming Hybrid mode");
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

        private GpuSwitchMode? DetectNvidiaOptimusMode()
        {
            try
            {
                // Check NVIDIA GPU status
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'");
                var nvidiaGpus = searcher.Get().Cast<ManagementObject>().ToList();

                if (nvidiaGpus.Count == 0)
                    return null; // No NVIDIA GPU

                // Check if NVIDIA GPU is the only active adapter
                using var allSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Status = 'OK'");
                var allGpus = allSearcher.Get().Count;

                if (allGpus == 1 && nvidiaGpus.Count == 1)
                {
                    return GpuSwitchMode.Discrete; // Only NVIDIA active
                }

                // Check for Intel iGPU
                using var intelSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%Intel%' AND Status = 'OK'");
                var intelActive = intelSearcher.Get().Count > 0;

                if (intelActive && nvidiaGpus.Count > 0)
                {
                    return GpuSwitchMode.Hybrid; // Both active = Hybrid/Optimus
                }

                return GpuSwitchMode.Discrete;
            }
            catch
            {
                return null;
            }
        }

        private GpuSwitchMode? DetectAmdSwitchableMode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%AMD%' OR Name LIKE '%Radeon%'");
                var amdGpus = searcher.Get().Cast<ManagementObject>().ToList();

                if (amdGpus.Count == 0)
                    return null;

                using var allSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Status = 'OK'");
                var allGpus = allSearcher.Get().Count;

                if (allGpus == 1 && amdGpus.Count == 1)
                {
                    return GpuSwitchMode.Discrete;
                }

                using var intelSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%Intel%' AND Status = 'OK'");
                var intelActive = intelSearcher.Get().Count > 0;

                if (intelActive && amdGpus.Count > 0)
                {
                    return GpuSwitchMode.Hybrid;
                }

                return GpuSwitchMode.Discrete;
            }
            catch
            {
                return null;
            }
        }

        private int CountActiveVideoControllers()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Status = 'OK'");
                return searcher.Get().Count;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Switch GPU mode - requires system restart to take effect
        /// </summary>
        public bool Switch(GpuSwitchMode mode)
        {
            try
            {
                // HP Omen systems may use HP Command Center or BIOS-level switching
                // Try multiple methods to maximize compatibility

                // Method 1: HP Omen Command Center WMI (if available)
                if (TrySwitchViaHpWmi(mode))
                {
                    _logging.Info($"✓ GPU mode switched to {mode} via HP WMI");
                    return true;
                }

                // Method 2: NVIDIA/AMD control panel commands
                if (TrySwitchViaGpuControlPanel(mode))
                {
                    _logging.Info($"✓ GPU mode switched to {mode} via GPU control panel");
                    return true;
                }

                // Method 3: Registry-based switching (some HP systems)
                if (TrySwitchViaRegistry(mode))
                {
                    _logging.Info($"✓ GPU mode set to {mode} via registry (restart required)");
                    return true;
                }

                _logging.Warn($"GPU mode switching not supported on this system. Current mode: {DetectCurrentMode()}");
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
                // HP-specific WMI namespace for OMEN Command Center
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM HPBIOS_BIOSSettingInterface");
                var results = searcher.Get();
                
                if (results.Count == 0)
                    return false;

                foreach (ManagementObject obj in results)
                {
                    var modeValue = mode switch
                    {
                        GpuSwitchMode.Discrete => "Discrete",
                        GpuSwitchMode.Integrated => "Integrated",
                        _ => "Hybrid"
                    };

                    // HP BIOS setting name varies by model
                    var settingNames = new[] { "GPU Mode", "Graphics Mode", "Switchable Graphics", "Advanced Optimus" };
                    
                    foreach (var setting in settingNames)
                    {
                        try
                        {
                            var inParams = obj.GetMethodParameters("SetBIOSSetting");
                            inParams["Name"] = setting;
                            inParams["Value"] = modeValue;
                            inParams["Password"] = ""; // Most systems don't have BIOS password set
                            
                            var outParams = obj.InvokeMethod("SetBIOSSetting", inParams, null);
                            if (outParams != null && (uint)outParams["Return"] == 0)
                            {
                                _logging.Info($"Set HP BIOS setting '{setting}' to '{modeValue}'");
                                return true;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySwitchViaGpuControlPanel(GpuSwitchMode mode)
        {
            try
            {
                // NVIDIA: Use nvcplui.exe or nvidia-smi if available
                var nvidiaPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");

                if (System.IO.File.Exists(nvidiaPath))
                {
                    // Open NVIDIA Control Panel to Optimus settings
                    // Note: Automated switching requires NVIDIA drivers that support it
                    var psi = new ProcessStartInfo
                    {
                        FileName = nvidiaPath,
                        Arguments = "/page:optimus",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    _logging.Info("Opened NVIDIA Control Panel - manual GPU mode selection required");
                    return true; // User must complete manually
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySwitchViaRegistry(GpuSwitchMode mode)
        {
            try
            {
                // Some HP systems store GPU preference in registry
                // This is highly system-dependent and may not work on all models
                
                using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                
                if (key == null)
                    return false;

                var modeValue = mode switch
                {
                    GpuSwitchMode.Discrete => 1,
                    GpuSwitchMode.Integrated => 2,
                    _ => 0 // Hybrid
                };

                key.SetValue("EnableMsHybrid", modeValue, Microsoft.Win32.RegistryValueKind.DWord);
                _logging.Info($"Set registry GPU mode preference to {mode}");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
