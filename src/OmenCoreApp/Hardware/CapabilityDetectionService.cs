using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Detects hardware capabilities at runtime to determine available providers.
    /// Builds a capability matrix specific to this device.
    /// </summary>
    public class CapabilityDetectionService : IDisposable
    {
        private readonly LoggingService? _logging;
        private bool _disposed;
        
        public DeviceCapabilities Capabilities { get; private set; } = new();
        
        // Provider instances for testing
        private OghServiceProxy? _oghProxy;
        private HpWmiBios? _wmiBios;

        public CapabilityDetectionService(LoggingService? logging = null)
        {
            _logging = logging;
        }

        /// <summary>
        /// Run full capability detection. Call this at startup.
        /// </summary>
        public DeviceCapabilities DetectCapabilities()
        {
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info("Starting device capability detection...");
            _logging?.Info("═══════════════════════════════════════════════════");
            
            Capabilities = new DeviceCapabilities();
            
            // Phase 1: Device identification
            DetectDeviceInfo();
            
            // Phase 1.5: Load model-specific capabilities from database
            LoadModelCapabilities();
            
            // Phase 2: Security status
            DetectSecurityStatus();
            
            // Phase 3: OGH status (important - affects other detection)
            DetectOghStatus();
            
            // Phase 4: Driver availability
            DetectDriverAvailability();
            
            // Phase 5: WMI BIOS capabilities
            DetectWmiBiosCapabilities();
            
            // Phase 6: Determine best fan control method
            DetermineFanControlMethod();
            
            // Phase 7: Thermal sensor capabilities
            DetectThermalCapabilities();
            
            // Phase 8: GPU capabilities
            DetectGpuCapabilities();
            
            // Phase 9: Undervolt capabilities
            DetectUndervoltCapabilities();
            
            // Phase 10: Lighting capabilities
            DetectLightingCapabilities();
            
            // Phase 11: Refine capabilities based on model database
            RefineCapabilitiesFromModel();
            
            // Log summary
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info("Capability detection complete:");
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info(Capabilities.GetSummary());
            
            return Capabilities;
        }
        
        /// <summary>
        /// Load model-specific capabilities from the database.
        /// </summary>
        private void LoadModelCapabilities()
        {
            _logging?.Info("Phase 1.5: Loading model-specific capabilities...");
            
            var productId = Capabilities.ProductId;
            
// First try matching by WMI model name pattern (more accurate for shared ProductIds)
            var modelNameMatch = ModelCapabilityDatabase.GetCapabilitiesByModelName(Capabilities.ModelName);
            if (modelNameMatch != null)
            {
                Capabilities.IsKnownModel = true;
                Capabilities.ModelConfig = modelNameMatch;
                _logging?.Info($"  ✓ Found model by name pattern: {Capabilities.ModelConfig.ModelName}");
                _logging?.Info($"    Year: {Capabilities.ModelConfig.ModelYear}, Family: {Capabilities.ModelConfig.Family}");
                
                if (Capabilities.ModelConfig.UserVerified)
                {
                    _logging?.Info($"    ✓ User-verified configuration");
                }
                
                return; // Found by name pattern, skip ProductId lookup
            }
            
            // Check if model is in the database by ProductId
            Capabilities.IsKnownModel = ModelCapabilityDatabase.IsKnownModel(productId);
            
            if (Capabilities.IsKnownModel)
            {
                Capabilities.ModelConfig = ModelCapabilityDatabase.GetCapabilities(productId);
                _logging?.Info($"  ✓ Found model in database: {Capabilities.ModelConfig.ModelName}");
                _logging?.Info($"    Year: {Capabilities.ModelConfig.ModelYear}, Family: {Capabilities.ModelConfig.Family}");
                
                if (Capabilities.ModelConfig.UserVerified)
                {
                    _logging?.Info($"    ✓ User-verified configuration");
                }
                else
                {
                    _logging?.Warn($"    ⚠ Configuration not yet verified by users");
                }
                
                if (!string.IsNullOrEmpty(Capabilities.ModelConfig.Notes))
                {
                    _logging?.Info($"    Note: {Capabilities.ModelConfig.Notes}");
                }
            }
            else
            {
                // Try to get capabilities by model family
                if (Capabilities.ModelFamily != OmenModelFamily.Unknown)
                {
                    Capabilities.ModelConfig = ModelCapabilityDatabase.GetCapabilitiesByFamily(Capabilities.ModelFamily);
                    _logging?.Warn($"  ⚠ Model not in database - using {Capabilities.ModelFamily} family defaults");
                }
                else
                {
                    Capabilities.ModelConfig = ModelCapabilityDatabase.DefaultCapabilities;
                    _logging?.Warn($"  ⚠ Unknown model - using default capabilities");
                }
                
                _logging?.Info($"  💡 Please report your model (Product ID: {productId}) to improve support");
            }
        }
        
        /// <summary>
        /// Refine detected capabilities based on model database.
        /// Model-specific info overrides runtime detection in some cases.
        /// </summary>
        private void RefineCapabilitiesFromModel()
        {
            if (Capabilities.ModelConfig == null) return;
            
            _logging?.Info("Phase 11: Refining capabilities from model database...");
            
            var model = Capabilities.ModelConfig;
            
            // Apply model-specific overrides
            
            // Fan control
            if (!model.SupportsFanControlWmi && !model.SupportsFanControlEc)
            {
                // Model is known to not support fan control (e.g., desktops)
                if (Capabilities.FanControl != FanControlMethod.None && Capabilities.FanControl != FanControlMethod.MonitoringOnly)
                {
                    _logging?.Warn($"  Fan control disabled per model database (not supported on {model.ModelName})");
                    Capabilities.FanControl = FanControlMethod.MonitoringOnly;
                    Capabilities.CanSetFanSpeed = false;
                }
            }
            
            // Fan count override
            if (model.FanZoneCount != Capabilities.FanCount)
            {
                _logging?.Info($"  Fan count adjusted: {Capabilities.FanCount} → {model.FanZoneCount} (per model database)");
                Capabilities.FanCount = model.FanZoneCount;
            }
            
            // MUX switch
            if (model.HasMuxSwitch && !Capabilities.HasMuxSwitch)
            {
                _logging?.Info($"  MUX switch enabled per model database");
                Capabilities.HasMuxSwitch = true;
            }
            else if (!model.HasMuxSwitch && Capabilities.HasMuxSwitch)
            {
                _logging?.Info($"  MUX switch disabled per model database (not present on {model.ModelName})");
                Capabilities.HasMuxSwitch = false;
            }
            
            // Lighting
            if (model.HasPerKeyRgb && !Capabilities.HasPerKeyLighting)
            {
                _logging?.Info($"  Per-key RGB enabled per model database");
                Capabilities.HasPerKeyLighting = true;
                Capabilities.Lighting = LightingCapability.PerKey;
            }
            else if (!model.HasFourZoneRgb && !model.HasPerKeyRgb && Capabilities.HasZoneLighting)
            {
                _logging?.Info($"  Zone lighting disabled per model database (not present on {model.ModelName})");
                Capabilities.HasZoneLighting = false;
                Capabilities.Lighting = model.HasKeyboardBacklight ? LightingCapability.SingleColor : LightingCapability.None;
            }
            
            // Undervolt
            if (!model.SupportsUndervolt && Capabilities.CanUndervolt)
            {
                _logging?.Info($"  Undervolt disabled per model database (not supported on {model.ModelName})");
                Capabilities.CanUndervolt = false;
                Capabilities.UndervoltMethod = UndervoltMethod.None;
            }
            
            // Performance modes
            if (model.PerformanceModes.Length > 0 && Capabilities.AvailablePerformanceModes.Length == 0)
            {
                Capabilities.AvailablePerformanceModes = model.PerformanceModes;
                Capabilities.HasOemPerformanceModes = true;
                _logging?.Info($"  Performance modes set from model database: {string.Join(", ", model.PerformanceModes)}");
            }
        }

        private void DetectDeviceInfo()
        {
            _logging?.Info("Phase 1: Device identification...");
            
            try
            {
                // Get system info from WMI
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    Capabilities.ModelName = obj["Model"]?.ToString() ?? "";
                    _logging?.Info($"  Model: {Capabilities.ModelName}");
                }
                
                // Detect model family based on name
                DetectModelFamily();
                
                // Get chassis type (desktop vs laptop)
                DetectChassisType();
                
                // Get BIOS info
                using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
                foreach (var obj in biosSearcher.Get())
                {
                    Capabilities.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";
                    Capabilities.SerialNumber = obj["SerialNumber"]?.ToString() ?? "";
                    _logging?.Info($"  BIOS: {Capabilities.BiosVersion}");
                }
                
                // Get baseboard info for product ID
                using var boardSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                foreach (var obj in boardSearcher.Get())
                {
                    Capabilities.ProductId = obj["Product"]?.ToString() ?? "";
                    Capabilities.BoardId = obj["SerialNumber"]?.ToString() ?? "";
                    _logging?.Info($"  Product ID: {Capabilities.ProductId}");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Device identification error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Detect OMEN model family from model name.
        /// Different families have different fan control support levels.
        /// </summary>
        private void DetectModelFamily()
        {
            var model = Capabilities.ModelName?.ToUpperInvariant() ?? "";
            var productId = Capabilities.ProductId?.ToUpperInvariant() ?? "";
            
            // First check for known HP Gaming Product IDs (for motherboard replacement cases)
            // HP uses Product IDs like 8BCA, 8B2J, etc. for OMEN laptops
            // These start with 8 for gaming laptops typically
            bool isOmenByProductId = IsOmenProductId(productId);
            
            if (isOmenByProductId && !model.Contains("OMEN") && !model.Contains("VICTUS"))
            {
                _logging?.Info($"  Note: OMEN device detected via Product ID ({productId}) - motherboard may have been replaced");
            }
            
            // OMEN Transcend models (newer ultrabook-style, may need OGH)
            if (model.Contains("TRANSCEND"))
            {
                Capabilities.ModelFamily = OmenModelFamily.Transcend;
                _logging?.Info($"  Model Family: OMEN Transcend (may require OGH proxy for fan control)");
                return;
            }
            
            // Check for desktop models
            if (model.Contains("25L") || model.Contains("30L") || model.Contains("40L") || model.Contains("45L"))
            {
                Capabilities.ModelFamily = OmenModelFamily.Desktop;
                _logging?.Info($"  Model Family: OMEN Desktop");
                return;
            }
            
            // HP Victus models
            if (model.Contains("VICTUS"))
            {
                Capabilities.ModelFamily = OmenModelFamily.Victus;
                _logging?.Info($"  Model Family: HP Victus");
                return;
            }
            
            // Check for OMEN models (by name or Product ID)
            bool isOmen = model.Contains("OMEN") || model.Contains("THETIGER") || isOmenByProductId;
            
            // Try to detect year from model for 2024+ detection
            // Models like "OMEN by HP 16-wf1xxx" where 1xxx suggests 2024+
            if (isOmen)
            {
                // Look for OMEN 16/17 with year indicators
                // wf0xxx = 2023, wf1xxx = 2024, xf0xxx = 2024, etc.
                if (model.Contains("-WF1") || model.Contains("-XF1") || model.Contains("-XF0") ||
                    model.Contains(" 14-") || // OMEN 14 is newer line
                    model.Contains("2024") ||
                    productId.StartsWith("8B"))  // 8Bxx Product IDs are typically 2023+ OMEN
                {
                    Capabilities.ModelFamily = OmenModelFamily.OMEN2024Plus;
                    _logging?.Info($"  Model Family: OMEN 2024+ (may require OGH proxy for fan control)");
                    return;
                }
                
                // Classic OMEN 16/17
                if (model.Contains(" 16") || model.Contains("16-"))
                {
                    Capabilities.ModelFamily = OmenModelFamily.OMEN16;
                    _logging?.Info($"  Model Family: OMEN 16");
                    return;
                }
                
                if (model.Contains(" 17") || model.Contains("17-"))
                {
                    Capabilities.ModelFamily = OmenModelFamily.OMEN17;
                    _logging?.Info($"  Model Family: OMEN 17");
                    return;
                }
                
                // Generic OMEN without specific model number might be legacy
                // Or motherboard replacement case - treat as standard OMEN
                Capabilities.ModelFamily = OmenModelFamily.Legacy;
                _logging?.Info($"  Model Family: OMEN (legacy/unknown generation)");
                return;
            }
            
            Capabilities.ModelFamily = OmenModelFamily.Unknown;
            _logging?.Info($"  Model Family: Unknown (non-OMEN device?)");
        }
        
        /// <summary>
        /// Check if a Product ID belongs to an OMEN/Gaming laptop.
        /// HP uses specific product ID patterns for their gaming lines.
        /// </summary>
        private static bool IsOmenProductId(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return false;
            
            // Known OMEN Product ID patterns:
            // - 8Bxx: OMEN 16/17 2023-2024 models
            // - 8Axx: OMEN 16/17 earlier models  
            // - 88xx: Some OMEN 15 models
            // - 8Cxx: Some newer OMEN variants
            // The first character is typically '8' for gaming laptops
            
            // Specific known OMEN Product IDs (add more as discovered)
            var knownOmenIds = new[]
            {
                "8BCA", "8B2J", "8B2K", "8B2L", "8B2M",  // OMEN 16 xf series
                "8B9D", "8B9E", "8B9F",                   // OMEN 17 variants
                "88D9", "88DA", "88DB",                   // OMEN 15 variants
                "8A12", "8A13", "8A14",                   // OMEN 16 wf series
                "8C3A", "8C3B", "8C3C",                   // Newer OMEN variants
            };
            
            // Check exact match first
            if (knownOmenIds.Contains(productId.ToUpperInvariant()))
                return true;
            
            // Pattern matching: 8Bxx, 8Axx, 88xx, 8Cxx are typically gaming
            if (productId.Length >= 2)
            {
                var prefix = productId.Substring(0, 2).ToUpperInvariant();
                if (prefix == "8B" || prefix == "8A" || prefix == "88" || prefix == "8C")
                    return true;
            }
            
            return false;
        }
        
        private void DetectChassisType()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (var obj in searcher.Get())
                {
                    if (obj["ChassisTypes"] is ushort[] chassisTypes && chassisTypes.Length > 0)
                    {
                        var chassisValue = chassisTypes[0];
                        Capabilities.Chassis = (ChassisType)chassisValue;

                        var formFactor = Capabilities.IsDesktop ? "Desktop" :
                                        Capabilities.IsLaptop ? "Laptop" : "Other";
                        _logging?.Info($"  Chassis: {Capabilities.Chassis} ({formFactor})");

                        // Warn about limited desktop support
                        if (Capabilities.IsDesktop)
                        {
                            _logging?.Warn("  ⚠️ Desktop PC detected - EC-based fan control may have limited support");
                            _logging?.Info("  💡 OMEN desktops (25L/30L/40L/45L) use different EC registers than laptops");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Chassis detection error: {ex.Message}");
                Capabilities.Chassis = ChassisType.Unknown;
            }
        }

        private void DetectSecurityStatus()
        {
            _logging?.Info("Phase 2: Security status...");
            
            try
            {
                // Check Secure Boot
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                if (key != null)
                {
                    var value = key.GetValue("UEFISecureBootEnabled");
                    Capabilities.SecureBootEnabled = value != null && Convert.ToInt32(value) == 1;
                }
                
                _logging?.Info($"  Secure Boot: {(Capabilities.SecureBootEnabled ? "Enabled" : "Disabled")}");
                
                if (Capabilities.SecureBootEnabled)
                {
                    _logging?.Warn("  ⚠️ Secure Boot blocks unsigned drivers (WinRing0)");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Security status detection error: {ex.Message}");
            }
        }

        private void DetectOghStatus()
        {
            _logging?.Info("Phase 3: OMEN Gaming Hub status...");
            
            try
            {
                _oghProxy = new OghServiceProxy(_logging);
                
                Capabilities.OghInstalled = _oghProxy.Status.IsInstalled;
                Capabilities.OghRunning = _oghProxy.Status.IsRunning;
                
                // If OGH is installed but not running, we might need it
                // This will be refined in Phase 5 when we test WMI BIOS
                if (Capabilities.OghInstalled && !Capabilities.OghRunning)
                {
                    _logging?.Info("  OGH installed but not running - may need to start for control");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"OGH detection error: {ex.Message}");
            }
        }

        private void DetectDriverAvailability()
        {
            _logging?.Info("Phase 4: Driver availability...");
            
            // Check PawnIO first (Secure Boot compatible)
            Capabilities.PawnIOAvailable = CheckPawnIOAvailable();
            if (Capabilities.PawnIOAvailable)
            {
                _logging?.Info($"  PawnIO: Available ✓ (Secure Boot compatible)");
            }
            
            // Check WinRing0
            Capabilities.WinRing0Available = CheckWinRing0Available();
            _logging?.Info($"  WinRing0: {(Capabilities.WinRing0Available ? "Available" : "Not available")}");
            
            // Determine overall driver status
            if (Capabilities.PawnIOAvailable)
            {
                Capabilities.DriverStatus = "PawnIO available (Secure Boot compatible)";
            }
            else if (Capabilities.WinRing0Available)
            {
                Capabilities.DriverStatus = "WinRing0 available";
            }
            else if (Capabilities.SecureBootEnabled)
            {
                Capabilities.DriverStatus = "No EC driver - Install PawnIO from pawnio.eu for Secure Boot compatible access";
            }
            else
            {
                Capabilities.DriverStatus = "No EC driver available";
            }
        }

        private bool CheckWinRing0Available()
        {
            try
            {
                // Check if WinRing0 service exists
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_SystemDriver WHERE Name = 'WinRing0_1_2_0' OR Name = 'WinRing0x64'");
                return searcher.Get().Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckPawnIOAvailable()
        {
            try
            {
                // Check if PawnIO is installed (registry or default path)
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null)
                {
                    _logging?.Info("  PawnIO: Found in registry (Secure Boot compatible EC access)");
                    return true;
                }
                
                // Check default installation path
                string defaultPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                    "PawnIO", "PawnIOLib.dll");
                if (System.IO.File.Exists(defaultPath))
                {
                    _logging?.Info("  PawnIO: Found at default path (Secure Boot compatible EC access)");
                    return true;
                }
                
                // Check if PawnIO driver is loaded
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_SystemDriver WHERE Name LIKE '%PawnIO%'");
                if (searcher.Get().Count > 0)
                {
                    _logging?.Info("  PawnIO: Driver loaded (Secure Boot compatible EC access)");
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void DetectWmiBiosCapabilities()
        {
            _logging?.Info("Phase 5: WMI BIOS capabilities...");
            
            try
            {
                _wmiBios = new HpWmiBios(_logging);
                
                if (_wmiBios.IsAvailable)
                {
                    _logging?.Info($"  WMI BIOS available - Status: {_wmiBios.Status}");
                    Capabilities.FanCount = _wmiBios.FanCount;
                    Capabilities.HasFanModes = true;
                    Capabilities.AvailableFanModes = new[] { "Default", "Performance", "Cool" };
                }
                else
                {
                    _logging?.Info($"  WMI BIOS not available: {_wmiBios.Status}");
                    
                    // WMI BIOS failed - will fall back to OGH proxy if available
                    if (Capabilities.OghRunning)
                    {
                        _logging?.Info("  → Will use OGH proxy as fallback");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"WMI BIOS detection error: {ex.Message}");
            }
        }

        private void DetermineFanControlMethod()
        {
            _logging?.Info("Phase 6: Determining fan control method...");
            
            // OmenCore is designed to be FULLY INDEPENDENT from OMEN Gaming Hub.
            // Priority order (OGH-independent methods first):
            // 1. WMI BIOS (no driver needed, works on most OMEN laptops)
            // 2. Direct EC via PawnIO (Secure Boot compatible)
            // 3. Direct EC via WinRing0 (requires Secure Boot disabled)
            // 4. OGH Proxy (LAST RESORT - only if WMI BIOS fails)
            // 5. Monitoring only
            
            // Check if model database says WMI is ineffective for this model
            // Some models (Transcend, 17-ck2xxx) have WMI that returns success but doesn't work
            var modelSaysWmiWorks = Capabilities.ModelConfig?.SupportsFanControlWmi ?? true;
            if (!modelSaysWmiWorks)
            {
                _logging?.Warn($"  ⚠ Model database indicates WMI fan control is ineffective for {Capabilities.ModelConfig?.ModelName}");
                _logging?.Info("  → Skipping WMI BIOS, trying EC or OGH proxy...");
            }
            
            // Primary: WMI BIOS - works on most OMEN models without any dependencies
            // BUT skip if model database says WMI is ineffective for this model
            if (modelSaysWmiWorks && _wmiBios?.IsAvailable == true)
            {
                Capabilities.FanControl = FanControlMethod.WmiBios;
                Capabilities.CanSetFanSpeed = true;
                Capabilities.CanReadRpm = true;
                _logging?.Info("  → Using WMI BIOS for fan control (OGH-independent)");
                return;
            }
            
            // Secondary: PawnIO for EC access (Secure Boot compatible)
            if (Capabilities.PawnIOAvailable)
            {
                Capabilities.FanControl = FanControlMethod.EcDirect;
                Capabilities.CanSetFanSpeed = true;
                Capabilities.CanReadRpm = true;
                _logging?.Info("  → Using PawnIO for EC access (OGH-independent, Secure Boot compatible)");
                return;
            }
            
            // Tertiary: WinRing0 for EC access (requires Secure Boot disabled)
            if (Capabilities.WinRing0Available)
            {
                Capabilities.FanControl = FanControlMethod.EcDirect;
                Capabilities.CanSetFanSpeed = true;
                Capabilities.CanReadRpm = true;
                _logging?.Info("  → Using WinRing0 for EC access (OGH-independent)");
                return;
            }
            
            // Last resort: OGH Proxy (requires OGH services running)
            if (Capabilities.OghRunning && _oghProxy?.IsAvailable == true)
            {
                Capabilities.FanControl = FanControlMethod.OghProxy;
                Capabilities.CanSetFanSpeed = true;
                Capabilities.CanReadRpm = true;
                Capabilities.UsingOghFallback = true;
                _logging?.Warn("  → Using OGH proxy as FALLBACK (WMI BIOS unavailable)");
                _logging?.Info("  💡 Report your model to help add native support");
                return;
            }
            
            // Suggestions if nothing works
            if (Capabilities.OghInstalled && !Capabilities.OghRunning)
            {
                Capabilities.FanControl = FanControlMethod.None;
                _logging?.Warn("  → Fan control unavailable. OGH installed but not running.");
                _logging?.Info("  💡 Start OGH services as temporary workaround, or report your model");
            }
            else if (Capabilities.SecureBootEnabled)
            {
                Capabilities.FanControl = FanControlMethod.MonitoringOnly;
                Capabilities.CanSetFanSpeed = false;
                Capabilities.CanReadRpm = true;
                _logging?.Warn("  → Fan control unavailable. Install PawnIO from pawnio.eu for Secure Boot compatible EC access.");
            }
            else
            {
                Capabilities.FanControl = FanControlMethod.MonitoringOnly;
                Capabilities.CanSetFanSpeed = false;
                Capabilities.CanReadRpm = true;
                _logging?.Warn("  → Fan control unavailable, monitoring only");
            }
        }

        private void DetectThermalCapabilities()
        {
            _logging?.Info("Phase 7: Thermal sensor capabilities...");
            
            // Thermal monitoring is typically available even without control
            Capabilities.CanReadCpuTemp = true;
            Capabilities.CanReadGpuTemp = true;
            
            if (_wmiBios?.IsAvailable == true)
            {
                Capabilities.ThermalMethod = ThermalSensorMethod.Wmi;
                _logging?.Info("  → Using WMI for thermal sensors");
            }
            else if (Capabilities.WinRing0Available || Capabilities.PawnIOAvailable)
            {
                Capabilities.ThermalMethod = ThermalSensorMethod.LibreHardwareMonitor;
                _logging?.Info("  → Using LibreHardwareMonitor for thermal sensors");
            }
            else
            {
                Capabilities.ThermalMethod = ThermalSensorMethod.Wmi;
                _logging?.Info("  → Using WMI for thermal sensors (basic)");
            }
        }

        private void DetectGpuCapabilities()
        {
            _logging?.Info("Phase 8: GPU capabilities...");
            
            try
            {
                // Detect GPU vendor
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        Capabilities.GpuVendor = GpuVendor.Nvidia;
                        Capabilities.NvApiAvailable = true;
                        _logging?.Info($"  GPU: {name} (NVIDIA)");
                    }
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || 
                             name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        Capabilities.GpuVendor = GpuVendor.Amd;
                        Capabilities.AmdAdlAvailable = true;
                        _logging?.Info($"  GPU: {name} (AMD)");
                    }
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        // Intel integrated, keep looking for discrete
                        if (Capabilities.GpuVendor == GpuVendor.Unknown)
                            Capabilities.GpuVendor = GpuVendor.Intel;
                    }
                }
                
                // Check MUX switch availability
                if (_wmiBios?.IsAvailable == true)
                {
                    Capabilities.HasMuxSwitch = true;
                    Capabilities.HasGpuPowerControl = true;
                    _logging?.Info("  MUX Switch: Available (via WMI BIOS)");
                }
                else if (Capabilities.OghRunning)
                {
                    Capabilities.HasMuxSwitch = true;
                    Capabilities.HasGpuPowerControl = true;
                    _logging?.Info("  MUX Switch: Available (via OGH)");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"GPU detection error: {ex.Message}");
            }
        }

        private void DetectUndervoltCapabilities()
        {
            _logging?.Info("Phase 9: Undervolt capabilities...");
            
            // Undervolting requires MSR access
            if (Capabilities.SecureBootEnabled)
            {
                if (Capabilities.PawnIOAvailable)
                {
                    Capabilities.CanUndervolt = true;
                    Capabilities.UndervoltMethod = UndervoltMethod.IntelMsrPawnIO;
                    _logging?.Info("  → Undervolt available via PawnIO (Secure Boot compatible)");
                }
                else
                {
                    Capabilities.CanUndervolt = false;
                    Capabilities.UndervoltMethod = UndervoltMethod.None;
                    _logging?.Warn("  → Undervolt unavailable (Secure Boot blocks MSR access)");
                }
            }
            else if (Capabilities.WinRing0Available)
            {
                Capabilities.CanUndervolt = true;
                Capabilities.UndervoltMethod = UndervoltMethod.IntelMsr;
                _logging?.Info("  → Undervolt available via WinRing0");
            }
            else
            {
                Capabilities.CanUndervolt = false;
                Capabilities.UndervoltMethod = UndervoltMethod.None;
                _logging?.Warn("  → Undervolt unavailable (no MSR access driver)");
            }
        }

        private void DetectLightingCapabilities()
        {
            _logging?.Info("Phase 10: Lighting capabilities...");
            
            // Check for HP OMEN keyboard backlight
            try
            {
                if (_wmiBios?.IsAvailable == true)
                {
                    // Try to detect backlight capability
                    Capabilities.HasKeyboardBacklight = true;
                    Capabilities.Lighting = LightingCapability.FourZone;
                    Capabilities.HasZoneLighting = true;
                    _logging?.Info("  → 4-zone keyboard backlight detected");
                }
                else if (Capabilities.OghRunning)
                {
                    Capabilities.HasKeyboardBacklight = true;
                    Capabilities.Lighting = LightingCapability.FourZone;
                    Capabilities.HasZoneLighting = true;
                    _logging?.Info("  → Lighting available via OGH");
                }
                else
                {
                    Capabilities.Lighting = LightingCapability.None;
                    _logging?.Info("  → Lighting control not available");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Lighting detection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Probe hardware capabilities at runtime to verify detection accuracy.
        /// This performs actual hardware tests to confirm what works on this specific device.
        /// Should be called after DetectCapabilities() for more accurate results.
        /// </summary>
        public void ProbeRuntimeCapabilities()
        {
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info("Probing runtime capabilities...");
            _logging?.Info("═══════════════════════════════════════════════════");
            
            int successCount = 0;
            int failCount = 0;
            
            // Test 1: Fan RPM readback
            if (Capabilities.CanReadRpm)
            {
                try
                {
                    if (_wmiBios?.IsAvailable == true)
                    {
                        var rpmResult = _wmiBios.GetFanRpmDirect();
                        
                        if (rpmResult.HasValue && (rpmResult.Value.fan1Rpm > 0 || rpmResult.Value.fan2Rpm > 0))
                        {
                            _logging?.Info($"  ✓ Fan RPM readback works: CPU={rpmResult.Value.fan1Rpm}, GPU={rpmResult.Value.fan2Rpm}");
                            successCount++;
                        }
                        else if (rpmResult.HasValue)
                        {
                            _logging?.Warn($"  ⚠ Fan RPM returned 0 - fans may be off or readback not working");
                        }
                        else
                        {
                            _logging?.Warn($"  ✗ Fan RPM readback returned null");
                            failCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"  ✗ Fan RPM probe failed: {ex.Message}");
                    failCount++;
                }
            }
            
            // Test 2: Temperature readback
            if (Capabilities.CanReadCpuTemp)
            {
                try
                {
                    if (_wmiBios?.IsAvailable == true)
                    {
                        var cpuTemp = _wmiBios.GetTemperature();
                        
                        if (cpuTemp.HasValue && cpuTemp.Value > 20 && cpuTemp.Value < 100)
                        {
                            _logging?.Info($"  ✓ CPU temperature readback works: {cpuTemp.Value}°C");
                            successCount++;
                        }
                        else if (cpuTemp.HasValue)
                        {
                            _logging?.Warn($"  ⚠ CPU temperature out of range: {cpuTemp.Value}°C");
                        }
                        else
                        {
                            _logging?.Warn($"  ✗ CPU temperature readback returned null");
                            failCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"  ✗ CPU temperature probe failed: {ex.Message}");
                    failCount++;
                }
            }
            
            // Test 3: Fan level read (verifies WMI BIOS communication)
            if (Capabilities.HasFanModes || Capabilities.CanSetFanSpeed)
            {
                try
                {
                    if (_wmiBios?.IsAvailable == true)
                    {
                        var level = _wmiBios.GetFanLevel();
                        if (level.HasValue)
                        {
                            _logging?.Info($"  ✓ Fan level readback works: Fan1={level.Value.fan1}, Fan2={level.Value.fan2}");
                            successCount++;
                        }
                        else
                        {
                            _logging?.Warn($"  ✗ Fan level readback returned null");
                            failCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"  ✗ Fan level probe failed: {ex.Message}");
                    failCount++;
                }
            }
            
            // Test 4: Check if model-specific features match reality
            if (Capabilities.ModelConfig != null)
            {
                var model = Capabilities.ModelConfig;
                
                // Verify fan zone count
                if (model.FanZoneCount == 1 && Capabilities.FanCount > 1)
                {
                    _logging?.Info($"  Note: Model database says 1 fan, but detected {Capabilities.FanCount}");
                }
                else if (model.FanZoneCount == 2 && Capabilities.FanCount == 1)
                {
                    _logging?.Info($"  Note: Model database says 2 fans, but only detected 1");
                }
            }
            
            Capabilities.RuntimeProbed = true;
            
            _logging?.Info($"Runtime probe complete: {successCount} passed, {failCount} failed");
            _logging?.Info("═══════════════════════════════════════════════════");
        }
        
        /// <summary>
        /// Get the recommended fan control provider based on detected capabilities.
        /// </summary>
        public string GetRecommendedFanProvider()
        {
            return Capabilities.FanControl switch
            {
                FanControlMethod.WmiBios => "HpWmiBios",
                FanControlMethod.OghProxy => "OghServiceProxy",
                FanControlMethod.EcDirect => "WinRing0EcAccess",
                FanControlMethod.Steps => "StepFanController",
                FanControlMethod.Percent => "PwmFanController",
                _ => "None"
            };
        }

        /// <summary>
        /// Get action recommendations for the user if control is limited.
        /// </summary>
        public List<string> GetRecommendations()
        {
            var recommendations = new List<string>();
            
            if (Capabilities.FanControl == FanControlMethod.None || 
                Capabilities.FanControl == FanControlMethod.MonitoringOnly)
            {
                if (Capabilities.OghInstalled && !Capabilities.OghRunning)
                {
                    recommendations.Add("Start OMEN Gaming Hub services for fan control");
                }
                else if (Capabilities.SecureBootEnabled)
                {
                    recommendations.Add("Secure Boot is blocking hardware drivers. Consider:");
                    recommendations.Add("  - Use OmenCore with OMEN Gaming Hub services running");
                    recommendations.Add("  - Or disable Secure Boot (may affect gaming/TPM)");
                }
                else
                {
                    recommendations.Add("Install LibreHardwareMonitor for additional control");
                }
            }
            
            if (!Capabilities.CanUndervolt)
            {
                if (Capabilities.SecureBootEnabled)
                {
                    recommendations.Add("Undervolt requires Secure Boot disabled or PawnIO driver");
                }
            }
            
            return recommendations;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _oghProxy?.Dispose();
                _wmiBios?.Dispose();
                _disposed = true;
            }
        }
    }
}
