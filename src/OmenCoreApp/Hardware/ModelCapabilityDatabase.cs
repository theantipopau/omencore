using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Model-specific capability configuration.
    /// Stores known feature support for each HP OMEN/Victus model.
    /// </summary>
    public class ModelCapabilities
    {
        /// <summary>HP Product ID (e.g., "8A14", "8BAD", "8CD1").</summary>
        public string ProductId { get; set; } = "";
        
/// <summary>Human-readable model name.</summary>
        public string ModelName { get; set; } = "";
        
        /// <summary>Model name pattern for matching (e.g., "17-ck2" matches "OMEN by HP Laptop 17-ck2xxx").</summary>
        public string? ModelNamePattern { get; set; }
        
        /// <summary>Model year (approximate).</summary>
        public int ModelYear { get; set; }
        
        /// <summary>Model family classification.</summary>
        public OmenModelFamily Family { get; set; } = OmenModelFamily.Unknown;
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Fan Control Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether WMI BIOS fan control is supported.</summary>
        public bool SupportsFanControlWmi { get; set; } = true;
        
        /// <summary>Whether direct EC fan control is supported.</summary>
        public bool SupportsFanControlEc { get; set; } = true;
        
        /// <summary>Whether custom fan curves are supported.</summary>
        public bool SupportsFanCurves { get; set; } = true;
        
        /// <summary>Whether independent CPU/GPU fan curves are supported.</summary>
        public bool SupportsIndependentFanCurves { get; set; } = true;
        
        /// <summary>Whether RPM readback is available.</summary>
        public bool SupportsRpmReadback { get; set; } = true;
        
        /// <summary>Number of fan zones (typically 2: CPU + GPU).</summary>
        public int FanZoneCount { get; set; } = 2;
        
        /// <summary>Maximum fan speed percentage supported.</summary>
        public int MaxFanSpeedPercent { get; set; } = 100;

        /// <summary>
        /// Optional model-specific max fan level override (0-100). Null means auto-detect.
        /// </summary>
        public int? MaxFanLevel { get; set; }
        
        /// <summary>Minimum fan speed percentage (some models don't allow 0%).</summary>
        public int MinFanSpeedPercent { get; set; } = 0;
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Performance Mode Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether OEM performance modes are available.</summary>
        public bool SupportsPerformanceModes { get; set; } = true;
        
        /// <summary>Available performance mode names.</summary>
        public string[] PerformanceModes { get; set; } = new[] { "Default", "Performance", "Cool" };
        
        /// <summary>Whether system throttling state can be read.</summary>
        public bool SupportsThrottleDetection { get; set; } = true;
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // GPU Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether GPU MUX switch is present.</summary>
        public bool HasMuxSwitch { get; set; } = false;
        
        /// <summary>Whether GPU Power Boost control is available via WMI.</summary>
        public bool SupportsGpuPowerBoost { get; set; } = true;
        
        /// <summary>Whether GPU can be disabled entirely.</summary>
        public bool SupportsGpuDisable { get; set; } = false;
        
        /// <summary>Whether Advanced Optimus is supported.</summary>
        public bool SupportsAdvancedOptimus { get; set; } = false;
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Lighting Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether keyboard backlight is present.</summary>
        public bool HasKeyboardBacklight { get; set; } = true;
        
        /// <summary>Whether 4-zone RGB is supported.</summary>
        public bool HasFourZoneRgb { get; set; } = true;
        
        /// <summary>Whether per-key RGB is supported.</summary>
        public bool HasPerKeyRgb { get; set; } = false;
        
        /// <summary>Whether light bar is present (some OMEN models).</summary>
        public bool HasLightBar { get; set; } = false;
        
        /// <summary>Preferred lighting control method.</summary>
        public string PreferredLightingMethod { get; set; } = "WmiBios";
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Undervolt/Power Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether CPU undervolting is possible (not all Intel CPUs support it).</summary>
        public bool SupportsUndervolt { get; set; } = true;
        
        /// <summary>Whether TCC offset adjustment is available.</summary>
        public bool SupportsTccOffset { get; set; } = true;
        
        /// <summary>Whether power limit adjustment is available.</summary>
        public bool SupportsPowerLimits { get; set; } = true;
        
        // ═══════════════════════════════════════════════════════════════════════════════════
        // Other Capabilities
        // ═══════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether webcam kill switch is present.</summary>
        public bool HasWebcamKillSwitch { get; set; } = false;
        
        /// <summary>Whether network boost feature is available.</summary>
        public bool SupportsNetworkBoost { get; set; } = true;
        
        /// <summary>Whether overboost (extreme performance) mode exists.</summary>
        public bool SupportsOverboost { get; set; } = false;
        
        /// <summary>Notes about this model's quirks or limitations.</summary>
        public string? Notes { get; set; }
        
        /// <summary>Whether this configuration has been verified by users.</summary>
        public bool UserVerified { get; set; } = false;
    }
    
    /// <summary>
    /// Database of known HP OMEN/Victus model capabilities.
    /// Built from reverse engineering and community reports.
    /// </summary>
    public static class ModelCapabilityDatabase
    {
        private static readonly Dictionary<string, ModelCapabilities> _knownModels = new(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Default capabilities used when model is not in the database.
        /// Assumes standard OMEN laptop capabilities.
        /// </summary>
        public static ModelCapabilities DefaultCapabilities { get; } = new ModelCapabilities
        {
            ProductId = "DEFAULT",
            ModelName = "Unknown OMEN",
            ModelYear = 2023,
            Family = OmenModelFamily.Unknown,
            SupportsFanControlWmi = true,
            SupportsFanControlEc = true,
            SupportsFanCurves = true,
            SupportsIndependentFanCurves = true,
            SupportsRpmReadback = true,
            FanZoneCount = 2,
            SupportsPerformanceModes = true,
            PerformanceModes = new[] { "Default", "Performance", "Cool" },
            SupportsGpuPowerBoost = true,
            HasKeyboardBacklight = true,
            HasFourZoneRgb = true,
            Notes = "Default configuration - some features may not work on your model"
        };
        
        static ModelCapabilityDatabase()
        {
            InitializeDatabase();
        }
        
        private static void InitializeDatabase()
        {
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 15 Series (15.6" laptops, 2020-2021)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8A14",
                ModelName = "OMEN 15 (2020) Intel",
                ModelYear = 2020,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = true,
                Notes = "Well-tested model with full WMI BIOS support"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8A15",
                ModelName = "OMEN 15 (2020) AMD",
                ModelYear = 2020,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsUndervolt = false, // AMD CPUs don't support Intel-style undervolting
                HasFourZoneRgb = true,
                UserVerified = true
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8BAD",
                ModelName = "OMEN 15 (2021) Intel",
                ModelYear = 2021,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasFourZoneRgb = true,
                UserVerified = true
            });
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 16 Series (16.1" laptops, 2021+)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8BAF",
                ModelName = "OMEN 16 (2021) Intel",
                ModelYear = 2021,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = true
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8BB0",
                ModelName = "OMEN 16 (2021) AMD",
                ModelYear = 2021,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsUndervolt = false,
                HasFourZoneRgb = true,
                UserVerified = true
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8CD0",
                ModelName = "OMEN 16 (2022) Intel",
                ModelYear = 2022,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasFourZoneRgb = true,
                UserVerified = true
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8CD1",
                ModelName = "OMEN 16 (2022) AMD",
                ModelYear = 2022,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsUndervolt = false,
                SupportsAdvancedOptimus = true,
                HasFourZoneRgb = true,
                UserVerified = true
            });

            // OMEN 16 (2022) - n0xxx series (AMD)
            // GitHub Issue #112: Product ID 8A44 missing from capability database.
            AddModel(new ModelCapabilities
            {
                ProductId = "8A44",
                ModelName = "OMEN 16 (2022) n0xxx AMD",
                ModelNamePattern = "16-n0",
                ModelYear = 2022,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsUndervolt = false,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "GitHub #112 — OMEN 16-n0xxx. Capabilities inferred from adjacent OMEN 16 generations; needs user verification."
            });
            
            // OMEN 16 (2023) - wf series
            AddModel(new ModelCapabilities
            {
                ProductId = "8BCA",
                ModelName = "OMEN 16 (2023) wf0xxx Intel",
                ModelYear = 2023,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = true
            });

            // OMEN 16 (2024) - wf1 series (Intel) — Issue #68: ProductId 8BAB, Board 8C78, BIOS F.29
            // Same WMI fan control path as wf0xxx (8BCA). V2 percentage-based fan levels.
            AddModel(new ModelCapabilities
            {
                ProductId = "8BAB",
                ModelName = "OMEN 16 (2024) wf1xxx Intel",
                ModelNamePattern = "16-wf1",
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 100,          // V2 percentage-based (same generation as wf0xxx)
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "OMEN 16-wf1xxx (2024 Intel) — Board 8C78. Added for Issue #68. Set UserVerified=true after community confirmation."
            });
            
            // OMEN 16 (2024) - xf series
            AddModel(new ModelCapabilities
            {
                ProductId = "8B2J",
                ModelName = "OMEN 16 (2024) xf0xxx Intel",
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN2024Plus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsOverboost = true,
                HasFourZoneRgb = true,
                Notes = "2024 model - may have WMI quirks on older BIOS versions"
            });
            
            // OMEN 16 (2024) - xd series (AMD)
            // Community report: Product ID 8BCD, RTX 4050 + AMD Radeon iGPU
            // V1 ThermalPolicy, MaxFanLevel=55, 2 fans, WMI fan control works
            AddModel(new ModelCapabilities
            {
                ProductId = "8BCD",
                ModelName = "OMEN 16 (2024) xd0xxx AMD",
                ModelNamePattern = "16-xd0", // For model name matching "OMEN 16-xd0xxx"
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                Notes = "2024 AMD model - V1 fan control, MaxFanLevel=55"
            });
            
            // OMEN 16 (2025) - ap0xxx series (AMD Ryzen AI + RTX 50-series)
            // Community report: Product ID 8D24, AMD Ryzen AI 9 365 + RTX 5060 Laptop GPU
            // BIOS F.11, V1 ThermalPolicy, MaxFanLevel=55, 2 fans, Secure Boot enabled
            // PawnIO: found in registry but driver init needs reboot after first install
            AddModel(new ModelCapabilities
            {
                ProductId = "8D24",
                ModelName = "OMEN 16 (2025) ap0xxx AMD",
                ModelNamePattern = "16-ap0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false, // EC layout unverified on this generation
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false, // AMD Ryzen AI — no Intel MSR undervolt
                UserVerified = false,
                Notes = "2025 AMD model (Ryzen AI 9 365 + RTX 5060). V1 fan control, MaxFanLevel=55. PawnIO requires reboot after first install to activate driver."
            });

            // OMEN 16 (2024) - am0xxx series (AMD Ryzen 7/8xxx + discrete GPU)
            // GitHub Issue #111: ProductId 8D2F, WMI model "OMEN Gaming Laptop 16-am0xxx"
            // Falls back to OMEN16 family defaults without a specific entry — add to give accurate capabilities.
            AddModel(new ModelCapabilities
            {
                ProductId = "8D2F",
                ModelName = "OMEN 16 (2024) am0xxx AMD",
                ModelNamePattern = "16-am0",
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false, // AMD — no Intel MSR undervolt
                UserVerified = false,
                Notes = "GitHub #111 — OMEN Gaming Laptop 16-am0xxx (2024 AMD). Capabilities inferred from 16-xd0 sibling; verify EC fan control before enabling."
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN MAX Series (2025+ flagship models)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // OMEN MAX 16 (2025) - ah0xxx series - Intel Core Ultra 9 275HX + RTX 5080
            // GitHub Issue #61: Model not in database
            // GitHub Issue #60: EC registers have different layout on 2025 Max models!
            // Direct EC access causes EC panic (caps lock blinking) - use WMI only.
            AddModel(new ModelCapabilities
            {
                ProductId = "8D41",
                ModelName = "OMEN MAX 16 (2025) ah0xxx Intel",
                ModelNamePattern = "16-ah0", // For model name matching "OMEN MAX Gaming Laptop 16-ah0xxx"
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN2024Plus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false, // EC registers have different layout! Writing causes EC panic.
                SupportsFanCurves = false, // No direct fan speed control via EC - uses ACPI platform_profile
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 100,
                SupportsPerformanceModes = true,
                PerformanceModes = new[] { "Default", "Performance", "Cool" },
                HasMuxSwitch = true, // Advanced Optimus / MUX switch available
                SupportsGpuPowerBoost = true, // RTX 5080 supports extended +25W boost
                SupportsAdvancedOptimus = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = true, // 4-zone RGB keyboard
                SupportsUndervolt = true, // Intel Core Ultra 9 275HX
                SupportsOverboost = true,
                UserVerified = true,
                Notes = "OMEN MAX Gaming Laptop 16-ah0xxx (2025) - Intel Core Ultra 9 275HX + RTX 5080. " +
                       "WARNING: EC registers have completely different layout than legacy OMEN models. " +
                       "Writing to legacy EC addresses (0x34, 0x62, etc.) corrupts EC state and causes " +
                       "caps lock blinking panic. Use WMI/ACPI platform_profile only. " +
                       "V2 fan commands forced."
            });
            
            // OMEN MAX 16t (2025) - 16t-ah000 variant - Intel Core Ultra 7 255HX + RTX 5070 Ti
            // GitHub Issue #60: Direct EC access causes EC panic
            // User confirmed ACPI platform_profile and hp-wmi hwmon pwm_enable work.
            AddModel(new ModelCapabilities
            {
                ProductId = "8D42", // Placeholder - actual product ID may differ
                ModelName = "OMEN MAX 16t (2025) ah000 Intel",
                ModelNamePattern = "16t-ah0", // For model name matching "OMEN MAX Gaming Laptop 16t-ah000"
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN2024Plus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false, // EC registers have different layout! Writing causes EC panic.
                SupportsFanCurves = false, // No direct fan speed control - uses ACPI platform_profile
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 100,
                SupportsPerformanceModes = true,
                PerformanceModes = new[] { "Default", "Performance", "Cool" },
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = true,
                SupportsOverboost = true,
                UserVerified = true,
                Notes = "OMEN MAX Gaming Laptop 16t-ah000 (2025) - Intel Core Ultra 7 255HX + RTX 5070 Ti. " +
                       "EC register layout incompatible with legacy addresses. Use ACPI platform_profile " +
                       "(low-power/balanced/performance) and hp-wmi hwmon pwm_enable (0=full,2=auto) for fan control."
            });

            // OMEN MAX 16 (ak0003nr) - reported by user (AMD HX 375 + RTX 5080)
            // Newer MAX models use WMI ThermalPolicy V2 and incompatible EC register layout.
            AddModel(new ModelCapabilities
            {
                ProductId = "AK0003NR",
                ModelName = "OMEN MAX 16 (ak0003nr) AMD",
                ModelNamePattern = "max 16 ak0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN2024Plus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false, // EC register layout incompatible — prefer WMI V2/ACPI platform_profile
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 100,
                SupportsPerformanceModes = true,
                PerformanceModes = new[] { "Default", "Performance", "Cool", "L5P" },
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false, // AMD Ryzen — Intel-style undervolt unsupported
                UserVerified = false,
                Notes = "OMEN MAX 16 ak0003nr — AMD HX 375 + RTX 5080. ThermalPolicy V2 (WMI V2) support; avoid EC writes that target legacy registers."
            });

            // OMEN MAX 16 (2025) - ak0xxx family (GitHub #117 / Product ID 8D87)
            AddModel(new ModelCapabilities
            {
                ProductId = "8D87",
                ModelName = "OMEN MAX 16 (2025) ak0xxx AMD",
                ModelNamePattern = "16-ak0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN2024Plus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false, // MAX-series EC layout diverges from legacy mappings
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 100,
                SupportsPerformanceModes = true,
                PerformanceModes = new[] { "Default", "Performance", "Cool", "L5P" },
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasKeyboardBacklight = true,
                HasPerKeyRgb = true,
                SupportsUndervolt = false, // AMD Ryzen AI path does not use Intel MSR undervolt
                UserVerified = false,
                Notes = "GitHub #117 — OMEN MAX Gaming Laptop 16-ak0xxx, Product ID 8D87. Model profile inferred from adjacent MAX ak/ah generation; verify keyboard/fan behavior on real hardware."
            });
            // -----------------------------------------------------------------------------------
            // OMEN 17 Series (17.3" laptops)
            // -----------------------------------------------------------------------------------

            // OMEN 17 (2021) Intel — product ID 8BB1 is also shared with Victus 15-fa1xxx;
            // the Victus disambiguation entry (8BB1-VICTUS15) is resolved first via
            // ModelNamePattern matching in CapabilityDetectionService.LoadModelCapabilities().
            AddModel(new ModelCapabilities
            {
                ProductId = "8BB1",
                ModelName = "OMEN 17 (2021) Intel",
                ModelYear = 2021,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "8BB1 is shared with Victus 15-fa1xxx; OMEN 17 profile selected when model name lacks 15-fa1 substring"
            });

            // Virtual product ID resolved via ModelNamePattern before ProductId lookup.
            // Matches WMI model names containing "15-fa1" (e.g., HP Victus 15-fa1xxx).
            AddModel(new ModelCapabilities
            {
                ProductId = "8BB1-VICTUS15",
                ModelName = "HP Victus 15-fa1xxx (2022)",
                ModelNamePattern = "15-fa1",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "Victus 15-fa1xxx — single-color backlight; shares 8BB1 product ID with OMEN 17 (2021)"
            });

            AddModel(new ModelCapabilities
            {
                ProductId = "8B9D",
                ModelName = "OMEN 17 (2023) Intel",
                ModelYear = 2023,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                HasLightBar = true,
                UserVerified = true
            });

            // OMEN 17-ck2xxx (2023) — WMI fans non-functional, use EC or OGH proxy
            AddModel(new ModelCapabilities
            {
                ProductId = "17CK2",
                ModelName = "OMEN 17-ck2xxx (2023)",
                ModelNamePattern = "17-ck2",
                ModelYear = 2023,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = true,
                UserVerified = true,
                Notes = "OMEN 17-ck2 series (2023) � WMI ineffective, use OGH proxy or EC access"
            });

            AddModel(new ModelCapabilities
            {
                ProductId = "8B9E",
                ModelName = "OMEN 17 (2023) AMD",
                ModelYear = 2023,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                SupportsUndervolt = false,
                HasFourZoneRgb = true,
                HasLightBar = true,
                UserVerified = true
            });

            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN Transcend Series (ultrabook-style, 2023+)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8C3A",
                ModelName = "OMEN Transcend 14 (2023)",
                ModelYear = 2023,
                Family = OmenModelFamily.Transcend,
                SupportsFanControlWmi = false, // Transcend often needs OGH proxy
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false, // Single fan design
                FanZoneCount = 1,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = false, // Per-key RGB on Transcend
                HasPerKeyRgb = true,
                Notes = "Transcend uses different WMI interface - may require OGH proxy for fan control"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "8C3B",
                ModelName = "OMEN Transcend 16 (2023)",
                ModelYear = 2023,
                Family = OmenModelFamily.Transcend,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                HasMuxSwitch = true,
                HasFourZoneRgb = false,
                HasPerKeyRgb = true,
                Notes = "Transcend uses different WMI interface - may require OGH proxy for fan control"
            });

            // OMEN Transcend 14 (2024) - fb1xxx series
            // GitHub Issue #99 / Linux reports: board IDs 8C58 and 8E41 map to Transcend 14 family.
            AddModel(new ModelCapabilities
            {
                ProductId = "8C58",
                ModelName = "OMEN Transcend 14 (2024) fb1xxx",
                ModelNamePattern = "14-fb1",
                ModelYear = 2024,
                Family = OmenModelFamily.Transcend,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = false,
                HasPerKeyRgb = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "Transcend 14 board family (8C58). Prefer hp-wmi/ACPI paths; direct legacy EC writes are unsafe on Linux and unverified on Windows."
            });

            AddModel(new ModelCapabilities
            {
                ProductId = "8E41",
                ModelName = "OMEN Transcend 14 (2024) fb1xxx",
                ModelNamePattern = "14-fb1",
                ModelYear = 2024,
                Family = OmenModelFamily.Transcend,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = false,
                HasPerKeyRgb = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "GitHub #99 / Linux reports for 8E41 (Transcend 14-fb1xxx). Use profile-based control paths; avoid legacy EC writes."
            });
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // HP Victus Series (entry-level gaming)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "88D9",
                ModelName = "HP Victus 15 (2022) Intel",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false, // Single fan on some Victus
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                HasFourZoneRgb = false, // Single-zone white backlight
                HasKeyboardBacklight = true,
                Notes = "Victus has limited features compared to OMEN"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "88DA",
                ModelName = "HP Victus 15 (2022) AMD",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                Notes = "Victus has limited features compared to OMEN"
            });

            // Victus 15 (2022) - fb0xxx series (AMD)
            // GitHub Issue #105: Product ID 8A3E missing from capability database.
            AddModel(new ModelCapabilities
            {
                ProductId = "8A3E",
                ModelName = "HP Victus 15 (2022) fb0xxx AMD",
                ModelNamePattern = "15-fb0",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #105 — Victus 15-fb0xxx. Conservative Victus profile (single-zone backlight)."
            });

            // Victus 16 (2023/2024) - d1xxx series
            // GitHub Issue #66: Product ID 8A26 requested for capability DB.
            AddModel(new ModelCapabilities
            {
                ProductId = "8A26",
                ModelName = "HP Victus 16 (2023/2024) d1xxx",
                ModelNamePattern = "16-d1",
                ModelYear = 2023,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = true,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #66 — Victus 16-d1xxx (8A26). Capabilities inferred from nearby Victus 16 entries; awaiting user confirmation."
            });
            
            // Victus 16 (2024+) Ryzen r0xxx series
            // GitHub Issue #110: Victus by HP Gaming Laptop 16-r0xxx — model not in capability database
            AddModel(new ModelCapabilities
            {
                ProductId = "8C2F",
                ModelName = "HP Victus 16 (2024+) Ryzen r0xxx",
                ModelNamePattern = "16-r0",
                ModelYear = 2024,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false, // Ryzen AMD
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "GitHub #110 — Victus 16-r0xxx (Ryzen 2024+). Keyboard entry 8C2F already present in KeyboardModelDatabase."
            });

            AddModel(new ModelCapabilities
            {
                ProductId = "88DB",
                ModelName = "HP Victus 16 (2022)",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = true, // Victus 16 has 4-zone
                UserVerified = true
            });
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN Desktop Series (WMI fan control + desktop RGB)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-25L",
                ModelName = "OMEN 25L Desktop",
                ModelYear = 2021,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = true, // WMI fan control works on OMEN desktops
                SupportsFanControlEc = false, // Desktop EC registers differ from laptops
                SupportsFanCurves = true, // Via WMI fan level commands
                SupportsRpmReadback = true, // WMI RPM readback available
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                HasKeyboardBacklight = false,
                HasFourZoneRgb = false,
                Notes = "OMEN 25L Desktop — WMI fan control supported, desktop RGB via USB HID"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-30L",
                ModelName = "OMEN 30L Desktop",
                ModelYear = 2022,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 30L Desktop — WMI fan control supported, desktop RGB via USB HID"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-35L",
                ModelName = "OMEN 35L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 35L Desktop — WMI fan control supported, desktop RGB via USB HID"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-40L",
                ModelName = "OMEN 40L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 40L Desktop — WMI fan control supported, desktop RGB via USB HID"
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-45L",
                ModelName = "OMEN 45L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 45L Desktop — WMI fan control supported, desktop RGB via USB HID"
            });
        }
        
        private static void AddModel(ModelCapabilities model)
        {
            _knownModels[model.ProductId] = model;
        }
        
/// <summary>
        /// Get capabilities for a specific model by Product ID.
        /// Returns default capabilities if model not found.
        /// </summary>
        public static ModelCapabilities GetCapabilities(string productId)
        {
            if (string.IsNullOrEmpty(productId))
                return DefaultCapabilities;
                
            if (_knownModels.TryGetValue(productId.ToUpperInvariant(), out var caps))
                return caps;
                
            return DefaultCapabilities;
        }
        
        /// <summary>
        /// Get capabilities by matching the WMI model name pattern.
        /// Use this when ProductId doesn't accurately identify the model.
        /// </summary>
        public static ModelCapabilities? GetCapabilitiesByModelName(string wmiModelName)
        {
            if (string.IsNullOrEmpty(wmiModelName))
                return null;
                
            // Check all models for pattern match
            foreach (var model in _knownModels.Values)
            {
                if (!string.IsNullOrEmpty(model.ModelNamePattern) &&
                    wmiModelName.Contains(model.ModelNamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return model;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Get capabilities by model family (fallback when Product ID not known).
        /// </summary>
        public static ModelCapabilities GetCapabilitiesByFamily(OmenModelFamily family)
        {
            // Find first model of this family as template
            var templateModel = _knownModels.Values.FirstOrDefault(m => m.Family == family);
            if (templateModel != null)
            {
                // Clone the template but mark as not user-verified
                return new ModelCapabilities
                {
                    ProductId = "FAMILY_" + family.ToString().ToUpperInvariant(),
                    ModelName = $"Unknown {family} Model",
                    ModelYear = templateModel.ModelYear,
                    Family = family,
                    SupportsFanControlWmi = templateModel.SupportsFanControlWmi,
                    SupportsFanControlEc = templateModel.SupportsFanControlEc,
                    SupportsFanCurves = templateModel.SupportsFanCurves,
                    SupportsIndependentFanCurves = templateModel.SupportsIndependentFanCurves,
                    FanZoneCount = templateModel.FanZoneCount,
                    HasMuxSwitch = templateModel.HasMuxSwitch,
                    SupportsGpuPowerBoost = templateModel.SupportsGpuPowerBoost,
                    HasFourZoneRgb = templateModel.HasFourZoneRgb,
                    HasPerKeyRgb = templateModel.HasPerKeyRgb,
                    SupportsUndervolt = templateModel.SupportsUndervolt,
                    UserVerified = false,
                    Notes = $"Based on typical {family} model - actual capabilities may vary"
                };
            }
            
            return DefaultCapabilities;
        }
        
        /// <summary>
        /// Check if a Product ID is in the database.
        /// </summary>
        public static bool IsKnownModel(string productId)
        {
            return !string.IsNullOrEmpty(productId) && 
                   _knownModels.ContainsKey(productId.ToUpperInvariant());
        }
        
        /// <summary>
        /// Get all known models (for diagnostics/reporting).
        /// </summary>
        public static IReadOnlyCollection<ModelCapabilities> GetAllModels()
        {
            return _knownModels.Values.ToList().AsReadOnly();
        }
        
        /// <summary>
        /// Get models by family.
        /// </summary>
        public static IEnumerable<ModelCapabilities> GetModelsByFamily(OmenModelFamily family)
        {
            return _knownModels.Values.Where(m => m.Family == family);
        }
    }
}
