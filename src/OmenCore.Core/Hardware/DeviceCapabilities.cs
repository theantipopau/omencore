using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Describes the hardware capabilities detected at runtime for this specific device.
    /// Used to determine which providers and features are available.
    /// </summary>
    public class DeviceCapabilities
    {
        // Device identification
        public string ProductId { get; set; } = "";
        public string BoardId { get; set; } = "";
        public string BiosVersion { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Model-specific capabilities (from ModelCapabilityDatabase)
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// The model-specific capability configuration loaded from the database.
        /// Provides per-model feature flags for UI visibility and functionality.
        /// </summary>
        public ModelCapabilities? ModelConfig { get; set; }
        
        /// <summary>
        /// Whether this device's model is in the known model database.
        /// If false, default capabilities are assumed and some features may not work.
        /// </summary>
        public bool IsKnownModel { get; set; }
        
        /// <summary>
        /// Whether capabilities have been verified by runtime probing.
        /// True after ProbeCapabilities() has been called and validated.
        /// </summary>
        public bool RuntimeProbed { get; set; }
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // UI Visibility Helpers (combine runtime detection with model database)
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether to show fan curve editor in UI.</summary>
        public bool ShowFanCurveEditor => CanSetFanSpeed && !FanWritesBlockedForSafety && (ModelConfig?.SupportsFanCurves ?? true);
        
        /// <summary>Whether to show independent CPU/GPU curves in UI.</summary>
        public bool ShowIndependentFanCurves => ShowFanCurveEditor && (ModelConfig?.SupportsIndependentFanCurves ?? true);
        
        /// <summary>Whether to show MUX switch controls in UI.</summary>
        public bool ShowMuxSwitch => HasMuxSwitch || (ModelConfig?.HasMuxSwitch ?? false);
        
        /// <summary>Whether to show GPU Power Boost controls in UI.</summary>
        /// Runtime detection wins; ModelConfig is only consulted for known models
        /// to prevent unknown/non-OMEN devices from showing OMEN-specific controls.
        public bool ShowGpuPowerBoost => HasGpuPowerControl ||
                                          (IsKnownModel && (ModelConfig?.SupportsGpuPowerBoost ?? false));
        
        /// <summary>Whether to show RGB lighting controls in UI.</summary>
        /// Runtime detection (HasZoneLighting/HasPerKeyLighting) always applies.
        /// ModelConfig-derived RGB features are gated on IsKnownModel so unknown
        /// devices don't show OMEN keyboard controls when runtime probing found none.
        public bool ShowRgbLighting => HasZoneLighting || HasPerKeyLighting || 
                                        (IsKnownModel && ((ModelConfig?.HasFourZoneRgb ?? false) ||
                                                          (ModelConfig?.HasPerKeyRgb ?? false)));
        
        /// <summary>Whether to show undervolt controls in UI.</summary>
        public bool ShowUndervolt => CanUndervolt && UndervoltRuntimeReady && (ModelConfig?.SupportsUndervolt ?? true);
        
        /// <summary>Whether to show performance mode selector in UI.</summary>
        public bool ShowPerformanceModes => HasOemPerformanceModes || (ModelConfig?.SupportsPerformanceModes ?? true);
        
        /// <summary>Warning message about model-specific limitations.</summary>
        public string? ModelWarning
        {
            get
            {
                if (ModelConfig == null) return null;
                
                if (!IsKnownModel)
                    return "Unknown model - some features may not work correctly. Please report your model to improve support.";
                    
                if (!ModelConfig.UserVerified)
                    return "This model's capabilities have not been fully verified. Please report any issues.";
                    
                return ModelConfig.Notes;
            }
        }
        
        // Chassis/Form factor
        public ChassisType Chassis { get; set; } = ChassisType.Unknown;
        public bool IsDesktop => Chassis == ChassisType.Desktop || Chassis == ChassisType.Tower || 
                                  Chassis == ChassisType.MiniTower || Chassis == ChassisType.AllInOne;
        public bool IsLaptop => Chassis == ChassisType.Laptop || Chassis == ChassisType.Notebook || 
                                 Chassis == ChassisType.Portable || Chassis == ChassisType.SubNotebook;

        /// <summary>
        /// Desktop OMEN fan write paths are blocked pending per-model validation.
        /// RPM telemetry and performance modes may still be available.
        /// </summary>
        public bool FanWritesBlockedForSafety =>
            IsDesktop ||
            ModelFamily == OmenModelFamily.Desktop ||
            ModelConfig?.Family == OmenModelFamily.Desktop;
        
        // Fan control capabilities
        public FanControlMethod FanControl { get; set; } = FanControlMethod.None;
        public bool CanReadRpm { get; set; }
        public bool CanSetFanSpeed { get; set; }
        public bool HasFanModes { get; set; }
        public int FanCount { get; set; } = 2;
        public string[] AvailableFanModes { get; set; } = Array.Empty<string>();
        
        // Thermal monitoring
        public bool CanReadCpuTemp { get; set; }
        public bool CanReadGpuTemp { get; set; }
        public bool CanReadOtherTemps { get; set; }
        public ThermalSensorMethod ThermalMethod { get; set; } = ThermalSensorMethod.None;
        
        // GPU capabilities
        public bool HasMuxSwitch { get; set; }
        public bool HasGpuPowerControl { get; set; }
        public GpuVendor GpuVendor { get; set; } = GpuVendor.Unknown;
        public bool NvApiAvailable { get; set; }
        public bool AmdAdlAvailable { get; set; }
        
        // Performance modes
        public bool HasOemPerformanceModes { get; set; }
        public string[] AvailablePerformanceModes { get; set; } = Array.Empty<string>();
        
        // Lighting
        public LightingCapability Lighting { get; set; } = LightingCapability.None;
        public bool HasKeyboardBacklight { get; set; }
        public bool HasZoneLighting { get; set; }
        public bool HasPerKeyLighting { get; set; }
        
        // Undervolt
        public bool CanUndervolt { get; set; }
        public bool SecureBootEnabled { get; set; }
        public UndervoltMethod UndervoltMethod { get; set; } = UndervoltMethod.None;
        
        /// <summary>
        /// Runtime readiness status for undervolt operations.
        /// May be false even if CanUndervolt is true if driver/MSR access fails at probe time.
        /// </summary>
        public bool UndervoltRuntimeReady { get; set; } = true;
        
        /// <summary>
        /// Block reason if UndervoltRuntimeReady is false. Explains why undervolt is disabled.
        /// </summary>
        public string? UndervoltBlockReason { get; set; }
        
        // OGH status (for fallback/compatibility, NOT required)
        public bool OghInstalled { get; set; }
        public bool OghRunning { get; set; }
        /// <summary>
        /// Indicates OGH proxy is being used as fallback (WMI BIOS preferred).
        /// OmenCore is designed to work WITHOUT OGH - this is only set when WMI BIOS fails.
        /// </summary>
        public bool UsingOghFallback { get; set; }
        
        // Driver status
        public bool PawnIOAvailable { get; set; }
        public string DriverStatus { get; set; } = "";

        // Structured provider health (commit 3 foundation)
        public IReadOnlyList<BackendStatus> BackendStatuses { get; set; } = Array.Empty<BackendStatus>();

        /// <summary>
        /// True when at least one critical backend capability has no healthy provider.
        /// </summary>
        public bool HasCriticalBackendDegradation =>
            !HasHealthyProviderFor(BackendCapability.Telemetry) ||
            !HasHealthyProviderFor(BackendCapability.FanControl) ||
            !HasHealthyProviderFor(BackendCapability.PerformanceProfiles);

        /// <summary>
        /// True when optional backend capabilities are degraded while critical capabilities remain healthy.
        /// </summary>
        public bool HasOptionalBackendDegradation =>
            !HasCriticalBackendDegradation &&
            (!HasHealthyProviderFor(BackendCapability.ECAccess) ||
             !HasHealthyProviderFor(BackendCapability.Undervolt));

        public string BackendDegradationSummary
        {
            get
            {
                if (HasCriticalBackendDegradation)
                {
                    return "Critical degradation: at least one required backend capability is unavailable.";
                }

                if (HasOptionalBackendDegradation)
                {
                    return "Optional degradation: core backends are healthy, but some advanced capabilities are unavailable.";
                }

                return "All tracked backend capabilities are healthy.";
            }
        }
        
        // Model family detection (helps identify potential WMI quirks)
        public OmenModelFamily ModelFamily { get; set; } = OmenModelFamily.Unknown;
        
        /// <summary>
        /// Returns true if this is a newer model that may have WMI quirks.
        /// These models are still supported via WMI BIOS - OGH is NOT required.
        /// </summary>
        public bool IsNewerModel => 
            ModelFamily == OmenModelFamily.Transcend || 
            ModelFamily == OmenModelFamily.OMEN2024Plus ||
            (ModelName?.Contains("Transcend", StringComparison.OrdinalIgnoreCase) ?? false);
        
        /// <summary>
        /// Returns true if this is a classic OMEN model with well-tested WMI BIOS support.
        /// </summary>
        public bool IsClassicOmen =>
            ModelFamily == OmenModelFamily.OMEN16 ||
            ModelFamily == OmenModelFamily.OMEN17 ||
            ModelFamily == OmenModelFamily.Victus;
        
        /// <summary>
        /// Generates a summary of capabilities for logging/display.
        /// </summary>
        public string GetSummary()
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Device: {ModelName} ({ProductId})");
            lines.AppendLine($"BIOS: {BiosVersion}");
            lines.AppendLine($"Form Factor: {Chassis} ({(IsDesktop ? "Desktop" : IsLaptop ? "Laptop" : "Unknown")})");
            lines.AppendLine();
            
            lines.AppendLine("Fan Control:");
            lines.AppendLine($"  Method: {FanControl}");
            lines.AppendLine($"  Read RPM: {(CanReadRpm ? "Yes" : "No")}");
            lines.AppendLine($"  Set Speed: {(CanSetFanSpeed ? "Yes" : "No")}");
            lines.AppendLine($"  Fan Modes: {(HasFanModes ? string.Join(", ", AvailableFanModes) : "None")}");
            lines.AppendLine();
            
            lines.AppendLine("Thermal:");
            lines.AppendLine($"  Method: {ThermalMethod}");
            lines.AppendLine($"  CPU Temp: {(CanReadCpuTemp ? "Yes" : "No")}");
            lines.AppendLine($"  GPU Temp: {(CanReadGpuTemp ? "Yes" : "No")}");
            lines.AppendLine();
            
            lines.AppendLine("GPU:");
            lines.AppendLine($"  Vendor: {GpuVendor}");
            lines.AppendLine($"  MUX Switch: {(HasMuxSwitch ? "Yes" : "No")}");
            lines.AppendLine($"  Power Control: {(HasGpuPowerControl ? "Yes" : "No")}");
            lines.AppendLine();
            
            lines.AppendLine("Undervolt:");
            lines.AppendLine($"  Method: {UndervoltMethod}");
            lines.AppendLine($"  Secure Boot: {(SecureBootEnabled ? "Enabled" : "Disabled")}");
            lines.AppendLine();
            
            lines.AppendLine("OGH Status (optional fallback):");
            lines.AppendLine($"  Installed: {(OghInstalled ? "Yes" : "No")}");
            lines.AppendLine($"  Running: {(OghRunning ? "Yes" : "No")}");
            lines.AppendLine($"  Using as Fallback: {(UsingOghFallback ? "Yes" : "No")}");
            lines.AppendLine();

            lines.AppendLine("Backend Health:");
            lines.AppendLine($"  Summary: {BackendDegradationSummary}");
            foreach (var backend in BackendStatuses)
            {
                var state = backend.Healthy ? "Healthy" : backend.Available ? "Degraded" : "Unavailable";
                lines.AppendLine($"  {backend.Name}: {state} (required: {(backend.Required ? "yes" : "no")})");
                if (!string.IsNullOrWhiteSpace(backend.FailureReason))
                {
                    lines.AppendLine($"    Reason: {backend.FailureReason}");
                }
            }
            lines.AppendLine();
            
            // Model database info
            lines.AppendLine("Model Database:");
            lines.AppendLine($"  Known Model: {(IsKnownModel ? "Yes" : "No")}");
            if (ModelConfig != null)
            {
                lines.AppendLine($"  DB Model: {ModelConfig.ModelName}");
                lines.AppendLine($"  Year: {ModelConfig.ModelYear}");
                lines.AppendLine($"  Family: {ModelConfig.Family}");
                lines.AppendLine($"  User Verified: {(ModelConfig.UserVerified ? "Yes" : "No")}");
            }
            lines.AppendLine();
            
            // UI Visibility flags
            lines.AppendLine("UI Features:");
            lines.AppendLine($"  Show Fan Curves: {(ShowFanCurveEditor ? "Yes" : "No")}");
            lines.AppendLine($"  Show Independent Curves: {(ShowIndependentFanCurves ? "Yes" : "No")}");
            lines.AppendLine($"  Show MUX Switch: {(ShowMuxSwitch ? "Yes" : "No")}");
            lines.AppendLine($"  Show GPU Power Boost: {(ShowGpuPowerBoost ? "Yes" : "No")}");
            lines.AppendLine($"  Show RGB Lighting: {(ShowRgbLighting ? "Yes" : "No")}");
            lines.AppendLine($"  Show Undervolt: {(ShowUndervolt ? "Yes" : "No")}");
            
            return lines.ToString();
        }

        private bool HasHealthyProviderFor(BackendCapability capability)
        {
            if (BackendStatuses == null || BackendStatuses.Count == 0)
            {
                return false;
            }

            return BackendStatuses.Any(status =>
                status.Available &&
                status.Healthy &&
                status.Capabilities.Any(c => c == capability));
        }
    }

    /// <summary>
    /// Method used for fan control.
    /// </summary>
    public enum FanControlMethod
    {
        None = 0,
        /// <summary>Direct EC register access (requires PawnIO)</summary>
        EcDirect,
        /// <summary>HP WMI BIOS commands (no driver needed) - PREFERRED</summary>
        WmiBios,
        /// <summary>Through OGH services (fallback only)</summary>
        OghProxy,
        /// <summary>Step-based control (discrete levels only)</summary>
        Steps,
        /// <summary>Percentage-based control (smooth PWM)</summary>
        Percent,
        /// <summary>Read-only monitoring (cannot control)</summary>
        MonitoringOnly
    }

    /// <summary>
    /// Method used for thermal sensor reading.
    /// </summary>
    public enum ThermalSensorMethod
    {
        None = 0,
        /// <summary>WMI queries (no driver needed)</summary>
        Wmi,
        /// <summary>LibreHardwareMonitor sensor path</summary>
        LibreHardwareMonitor,
        /// <summary>Direct EC reading (requires driver)</summary>
        EcDirect,
        /// <summary>Through OGH services</summary>
        OghProxy,
        /// <summary>NVIDIA NVAPI</summary>
        NvApi,
        /// <summary>AMD ADL</summary>
        AmdAdl
    }

    /// <summary>
    /// GPU vendor for determining available APIs.
    /// </summary>
    public enum GpuVendor
    {
        Unknown = 0,
        Nvidia,
        Amd,
        Intel
    }

    /// <summary>
    /// Keyboard/chassis lighting capability.
    /// </summary>
    public enum LightingCapability
    {
        None = 0,
        /// <summary>4-zone keyboard backlight</summary>
        FourZone,
        /// <summary>Per-key RGB</summary>
        PerKey,
        /// <summary>Single color backlight</summary>
        SingleColor,
        /// <summary>Multi-zone with light bar</summary>
        MultiZone
    }

    /// <summary>
    /// Method available for CPU undervolting.
    /// </summary>
    public enum UndervoltMethod
    {
        None = 0,
        /// <summary>Intel MSR via low-level driver</summary>
        IntelMsr,
        /// <summary>Intel MSR via PawnIO (Secure Boot compatible)</summary>
        IntelMsrPawnIO,
        /// <summary>AMD Curve Optimizer (BIOS only)</summary>
        AmdCurveOptimizer,
        /// <summary>Intel XTU compatibility layer</summary>
        IntelXtu
    }
    
    /// <summary>
    /// OMEN laptop model family.
    /// Different families have different WMI/EC support levels.
    /// </summary>
    public enum OmenModelFamily
    {
        Unknown = 0,
        /// <summary>Classic OMEN 16 (2021-2023)</summary>
        OMEN16,
        /// <summary>Classic OMEN 17 (2021-2023)</summary>
        OMEN17,
        /// <summary>HP Victus line</summary>
        Victus,
        /// <summary>OMEN Transcend 14/16 - newer ultrabook style, may need OGH proxy</summary>
        Transcend,
        /// <summary>2024+ OMEN models - may have different WMI interface</summary>
        OMEN2024Plus,
        /// <summary>OMEN Desktop (25L/30L/40L/45L)</summary>
        Desktop,
        /// <summary>Older OMEN models (pre-2021)</summary>
        Legacy
    }
    
    /// <summary>
    /// System chassis/enclosure type from SMBIOS.
    /// Values match Win32_SystemEnclosure ChassisTypes.
    /// </summary>
    public enum ChassisType
    {
        Unknown = 0,
        Other = 1,
        Desktop = 3,
        LowProfileDesktop = 4,
        PizzaBox = 5,
        MiniTower = 6,
        Tower = 7,
        Portable = 8,
        Laptop = 9,
        Notebook = 10,
        HandHeld = 11,
        DockingStation = 12,
        AllInOne = 13,
        SubNotebook = 14,
        SpaceSaving = 15,
        LunchBox = 16,
        MainServerChassis = 17,
        ExpansionChassis = 18,
        SubChassis = 19,
        BusExpansionChassis = 20,
        PeripheralChassis = 21,
        RaidChassis = 22,
        RackMountChassis = 23,
        SealedCasePC = 24,
        MultiSystemChassis = 25,
        CompactPci = 26,
        AdvancedTca = 27,
        Blade = 28,
        BladeEnclosure = 29,
        Tablet = 30,
        Convertible = 31,
        Detachable = 32,
        IoTGateway = 33,
        EmbeddedPC = 34,
        MiniPC = 35,
        StickPC = 36
    }
}
