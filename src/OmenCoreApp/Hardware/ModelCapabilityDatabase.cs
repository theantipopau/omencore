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

        /// <summary>
        /// True when this model needs the WMI thermal/performance policy write to hold OEM
        /// performance behavior if direct EC/MSR power-limit writes are unavailable.
        /// </summary>
        public bool AllowDecoupledWmiThermalPolicyFallback { get; set; } = false;

        /// <summary>
        /// Optional override for WMI V1 auto-mode floor clearing. Null follows the conservative
        /// profile default; true allows SetFanLevel(0,0) after Default/Auto handoff.
        /// </summary>
        public bool? AllowV1AutoModeFloorClear { get; set; }

        /// <summary>
        /// Optional model-specific override for how many low Max-mode telemetry checks must be
        /// observed before reasserting SetFanMax(true). Null uses the controller default.
        /// </summary>
        public int? MaxModeDropChecksBeforeReapply { get; set; }
        
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

        // ═══════════════════════════════════════════════════════════════════════════════════
        // Model-specific TDP overrides (null = use global config/default values)
        // These are used by PerformanceModeService to scale PL1/PL2/GPU limits correctly
        // for models whose firmware uses different power envelopes than the global defaults.
        // ═══════════════════════════════════════════════════════════════════════════════════

        /// <summary>CPU sustained PL1 watts for Performance mode. Null = use config default.</summary>
        public int? PerformanceCpuPl1Watts { get; set; }

        /// <summary>CPU boost PL2 watts for Performance mode. Null = use config default.</summary>
        public int? PerformanceCpuPl2Watts { get; set; }

        /// <summary>CPU sustained PL1 watts for Balanced mode. Null = use config default.</summary>
        public int? BalancedCpuPl1Watts { get; set; }

        /// <summary>CPU sustained PL1 watts for Eco/Quiet mode. Null = use config default.</summary>
        public int? EcoCpuPl1Watts { get; set; }

        /// <summary>GPU TGP watts for Performance mode. Null = use config default.</summary>
        public int? PerformanceGpuTgpWatts { get; set; }

        /// <summary>GPU TGP watts for Balanced mode. Null = use config default.</summary>
        public int? BalancedGpuTgpWatts { get; set; }

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
        private static readonly HashSet<string> _ambiguousProductIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // HP reuses 8BB1 across OMEN 17 and Victus 15-fa1xxx boards, so the WMI
            // model name is required to select the safer capability profile.
            "8BB1"
        };
        
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

            // Discord report (2026-05-23): OMEN 15-dc1077tx / ProductId 8574.
            // This legacy board reports non-functional WMI BIOS command paths but reliable
            // EC fan control via PawnIO. Keep RGB conservative (backlight only) until
            // multi-zone behavior is field-verified for this exact ProductId.
            AddModel(new ModelCapabilities
            {
                ProductId = "8574",
                ModelName = "OMEN 15-dc1xxx (2019) Intel",
                ModelNamePattern = "15-dc1",
                ModelYear = 2019,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = true,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = false,
                SupportsUndervolt = true,
                SupportsTccOffset = false,
                SupportsPowerLimits = false,
                UserVerified = false,
                Notes = "Discord field report - OMEN 15-dc1077tx (ProductId 8574): WMI BIOS command path not functional, EC fan control and PawnIO undervolt runtime available; RGB kept conservative until exact keyboard protocol is verified."
            });

            // Discord field report (2026-06-15): OMEN by HP Laptop 15-dh0xxx / ProductId 8600.
            // v3.7.1 resolved this board through FAMILY_LEGACY, leaving fan modes barely effective
            // except Max and showing missing/stale telemetry when PawnIO was absent (CPU stuck near
            // 28C, CPU power 0W, fan RPM 0). Keep direct EC writes disabled until PawnIO-backed
            // readback proves the legacy register layout; route Quick Profiles through the OEM WMI
            // thermal-policy path as the safest exact-board first pass.
            AddModel(new ModelCapabilities
            {
                ProductId = "8600",
                ModelName = "OMEN 15-dh0xxx (2019) Intel",
                ModelNamePattern = "15-dh0",
                ModelYear = 2019,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = false,
                SupportsUndervolt = true,
                SupportsPowerLimits = false,
                AllowDecoupledWmiThermalPolicyFallback = true,
                UserVerified = false,
                Notes = "Discord wafflist 2026-06-15 - OMEN by HP Laptop 15-dh0xxx / ProductId 8600. Exact conservative legacy profile added after FAMILY_LEGACY fallback, barely-effective fan modes except Max, missing PawnIO, CPU temp stuck near 28C, CPU power 0W, and fan RPM 0. Direct EC writes and RPM readback remain disabled until PawnIO/readback validation confirms the board path; WMI thermal-policy fallback enabled for Quick Profiles."
            });

            // GitHub #120: HP OMEN Laptop 15-en0038ur (2020 AMD, Ryzen 7 4800H + RTX 2060)
            // Product/Baseboard ID 8787. Reporter confirmed WMI ColorTable lighting, accepted
            // basic fan commands, MUX, and GPU power controls; fan RPM readback still reports 0.
            AddModel(new ModelCapabilities
            {
                ProductId = "8787",
                ModelName = "OMEN 15-en0038ur (2020) AMD",
                ModelNamePattern = "15-en",
                ModelYear = 2020,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "GitHub #120 - HP OMEN Laptop 15-en0038ur, ProductId 8787. Initial support from diagnostics; fan RPM readback remains pending verification."
            });

            // Discord field report (2026-06-12): OMEN Laptop 15-ek0xxx / ProductId 878C
            // (i7-10750H + GTX 1650 Ti). v3.7.1 resolved this board through FAMILY_LEGACY;
            // Quick Profile Performance/Balanced/Quiet left fans near 1900 RPM even at 99C,
            // while Custom -> Max could wake the coolers. Keep direct EC writes disabled and
            // route profile changes through the OEM WMI thermal-policy path until PL limits are
            // verified from readback logs.
            AddModel(new ModelCapabilities
            {
                ProductId = "878C",
                ModelName = "OMEN 15-ek0xxx (2020) Intel",
                ModelNamePattern = "15-ek0",
                ModelYear = 2020,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = true,
                AllowDecoupledWmiThermalPolicyFallback = true,
                UserVerified = false,
                Notes = "Discord Sky 2026-06-12 - OMEN Laptop 15-ek0xxx / ProductId 878C, i7-10750H + GTX 1650 Ti. Exact conservative legacy WMI profile added after Performance/Balanced/Quiet left fans near low RPM at 99C while Custom Max worked; direct EC writes disabled and WMI thermal-policy fallback enabled pending PL1/PL2 readback validation."
            });

            AddModel(new ModelCapabilities
            {
                ProductId = "88D2",
                ModelName = "OMEN by HP Laptop 15z-en100 (2021) AMD",
                ModelNamePattern = "15z-en100",
                ModelYear = 2021,
                Family = OmenModelFamily.Legacy,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                HasFourZoneRgb = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "GitHub #132 - ProductId 88D2 / 15z-en100. Conservative legacy WMI V1 profile; direct EC writes disabled and independent curves held off pending field verification."
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

            // OMEN 16 (2022) - n0xxx series (AMD) Hades board variant
            // GitHub Issue #121 report: Product ID 8A43 was being inferred through 8A44 pattern fallback.
            AddModel(new ModelCapabilities
            {
                ProductId = "8A43",
                ModelName = "OMEN 16 (2022) n0xxx AMD",
                ModelYear = 2022,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 60,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                SupportsUndervolt = false,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "GitHub #121 / Discord 2026-05-25 — Hades 8A43 exact ProductId profile added to avoid model-name-pattern inference. HP serial lookup reports OMEN Gaming Laptop 16-n0002ni / 6G103EA. Fan diagnostics show practical V1 ceiling near level 60 (GPU ~60, CPU ~58), so max fan level override is set to 60 for safer verification/normalization."
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

            // OMEN 16-WF1015ns / 9U8J3EA (2024 Intel) — ProductId 8C76, System SKU CND4311VNJ
            // Discord field report + logs (2026-05-04): i9-14900HX + RTX 4080 Laptop GPU,
            // BIOS F.19, WMI Thermal Policy V1, 2 fans, classic MaxFanLevel=55, 4-zone RGB,
            // MUX + GPU Power Boost available. Exact ProductId entry avoids low-confidence
            // model-name inference and prevents the wrong sibling assumptions (8BAB V2/100-level).
            AddModel(new ModelCapabilities
            {
                ProductId = "8C76",
                ModelName = "OMEN 16 (2024) wf1xxx Intel",
                ModelNamePattern = "16-wf1",
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "Discord HUrON / HP OMEN 16-WF1015ns 9U8J3EA — ProductId 8C76, i9-14900HX + RTX 4080, BIOS F.19, WMI V1/classic 55-level fan control. Exact entry replaces low-confidence inferred sibling match."
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
            // V1 ThermalPolicy, 2 fans, WMI fan control works.
            // Field validation indicates fan-level ceiling reaches 63 (~6300 RPM).
            AddModel(new ModelCapabilities
            {
                ProductId = "8BCD",
                ModelName = "OMEN 16 (2024) xd0xxx AMD",
                ModelNamePattern = "16-xd0", // For model name matching "OMEN 16-xd0xxx"
                ModelYear = 2024,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                MaxFanLevel = 63,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                AllowV1AutoModeFloorClear = true,
                UserVerified = false,
                Notes = "Discord 2026-05-20 + field follow-up 2026-05-29; Discord 2026-06-05/06 - OMEN 16-xd0xxx / ProductId 8BCD (Ryzen + RTX 4050). V1 WMI fan control with practical fan-level ceiling near 63 (~6300 RPM); direct EC and independent curves disabled pending register-layout validation. V1 auto-mode floor clear is enabled to release stale manual fan floors after load/profile handoff."
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

            // OMEN 16 (2025) - ap0xxx AMD, alternate board/ProductId reported by RC1 testers.
            // Community report: Product ID 8E35, SKU 1H85430PWY, AMD Ryzen AI 9 365 + RTX 5060.
            AddModel(new ModelCapabilities
            {
                ProductId = "8E35",
                ModelName = "OMEN 16 (2025) ap0xxx AMD",
                ModelNamePattern = "16-ap0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false,
                UserVerified = false,
                Notes = "Discord RC1 report - OMEN Gaming Laptop 16-ap0xxx / ProductId 8E35 / SKU 1H85430PWY (Ryzen AI 9 365 + RTX 5060). Same WMI V1 fan profile as 8D24; EC direct remains disabled until validated."
            });

            // OMEN 16 (2024) - am0xxx series (AMD Ryzen 7/8xxx + discrete GPU)
            // GitHub Issue #111: ProductId 8D2F, WMI model "OMEN Gaming Laptop 16-am0xxx"
            // Falls back to OMEN16 family defaults without a specific entry — add to give accurate capabilities.
            AddModel(new ModelCapabilities
            {
                ProductId = "8D2F",
                ModelName = "OMEN 16-am0xxx (8D2F)",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false,
                AllowDecoupledWmiThermalPolicyFallback = true,
                AllowV1AutoModeFloorClear = true,
                UserVerified = true,
                Notes = "GitHub #111 / Discord 2026-05-20 and 2026-05-21; Discord 2026-06-02 follow-up - OMEN Gaming Laptop 16-am0xxx, ProductId 8D2F. Exact board identity confirmed; product ID has appeared across AMD and Intel Core Ultra variants, so direct EC fan writes and independent curves remain disabled. WMI V1 fan/profile control is retained, WMI thermal-policy fallback is enabled for performance modes when EC/MSR power-limit writes are unavailable, and V1 auto-mode floor clear is enabled to let fans ramp down after load.",
            });

            // OMEN 16 (2025) - am0xxx Intel Core Ultra H + RTX 50-series
            // GitHub Issue #124: HP Omen 16-am0168ng (Core Ultra 7-255H + RTX 5070)
            // reports broad model fallback / erratic fans when ProductId is not yet known.
            AddModel(new ModelCapabilities
            {
                ProductId = "am0xxx_intel_2025_unverified",
                ModelName = "OMEN 16 (2025) am0xxx Intel Core Ultra",
                ModelNamePattern = "16-am0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = false,
                AllowDecoupledWmiThermalPolicyFallback = true,
                UserVerified = false,
                Notes = "GitHub #124 - OMEN Gaming Laptop 16-am0168ng / 16-am0xxx (Intel Core Ultra 7-255H + RTX 5070). ProductId pending; direct EC writes disabled until real hardware confirms register layout. WMI thermal-policy fallback is enabled for performance modes when direct EC/MSR power-limit writes are unavailable."
            });

            // OMEN 16 (2025) - am1xxx series (Intel i9-14900HX + RTX 5070 Ti and similar)
            // Community report: OmenCore Performance mode applies 55W (Balanced level) instead of
            // 90W sustained PL1. Model falls back to global defaults which lack the 90W PL1 for
            // this generation. Product ID not yet confirmed — matched via ModelNamePattern until a
            // user reports their Product ID via the diagnostics screen.
            // Roadmap #26.
            AddModel(new ModelCapabilities
            {
                ProductId = "am1xxx_unverified",
                ModelName = "OMEN 16 (2025) am1xxx Intel",
                ModelNamePattern = "16-am1",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = true, // Intel i9-14900HX supports MSR undervolt
                // Model-aware TDP overrides (from OGH reference: 90W PL1 / 130W PL2 for Performance)
                PerformanceCpuPl1Watts = 90,
                PerformanceCpuPl2Watts = 130,
                BalancedCpuPl1Watts = 55,
                PerformanceGpuTgpWatts = 150,
                BalancedGpuTgpWatts = 115,
                UserVerified = false,
                Notes = "Roadmap #26 — OMEN Gaming Laptop 16-am1xxx (2025 Intel, i9-14900HX + RTX 5070 Ti). " +
                        "ProductId pending community confirmation. Performance mode TDP = 90W PL1 / 130W PL2 " +
                        "per OGH reference behaviour. Set UserVerified=true once Product ID confirmed."
            });

            // OMEN Slim 16 (2025) - an0xxx series
            // GitHub Issue #145: ProductId 8D40, WMI model "OMEN Slim Gaming Laptop 16-an0xxx",
            // SKU 1H85302L6K. Falls back to broad OMEN16 family defaults without a specific entry.
            // "Slim" is a new, thinner chassis line not previously seen in this database — do not
            // assume it shares EC register layout, MUX switch, GPU TGP boost range, undervolt
            // support, or keyboard RGB surface with the standard-chassis 16-ap0/am0 siblings just
            // because it shares the same 2025 WMI command generation. Reporter confirms core WMI
            // fan/profile control already works via family fallback, so that much is shared.
            AddModel(new ModelCapabilities
            {
                ProductId = "8D40",
                ModelName = "OMEN Slim 16 (2025) an0xxx",
                ModelNamePattern = "16-an0",
                ModelYear = 2025,
                Family = OmenModelFamily.OMEN16,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = false, // Keyboard/RGB surface unconfirmed on this new thin chassis
                SupportsUndervolt = false, // CPU vendor/model not yet confirmed
                UserVerified = false,
                Notes = "GitHub #145 - OMEN Slim Gaming Laptop 16-an0xxx, ProductId 8D40, SKU 1H85302L6K. Exact conservative profile: WMI V1 fan/profile control retained (matches working family-fallback behavior), direct EC writes and independent curves disabled pending register-layout evidence, MUX/RGB/undervolt left unclaimed until this new thin-chassis line's hardware surface is confirmed. Reported Battery Care (Charge Limit) WMI failure and Performance-mode persistence are tracked separately — see 3.8.1-BUG-REPORTS.md."
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN MAX Series (2025+ flagship models)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // OMEN MAX 16 (2025) - ah0xxx series - Intel Core Ultra 9 275HX + RTX 5080/5090
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
                AllowDecoupledWmiThermalPolicyFallback = true,
                MaxModeDropChecksBeforeReapply = 1,
                PerformanceModes = new[] { "Default", "Performance", "Cool" },
                HasMuxSwitch = true, // Advanced Optimus / MUX switch available
                SupportsGpuPowerBoost = true, // RTX 50-series MAX configs support GPU Power Boost/PPAB paths
                SupportsAdvancedOptimus = true,
                HasKeyboardBacklight = true,
                HasFourZoneRgb = true, // 4-zone RGB keyboard
                HasPerKeyRgb = true,
                SupportsUndervolt = true, // Intel Core Ultra 9 275HX
                SupportsOverboost = true,
                UserVerified = true,
                Notes = "OMEN MAX Gaming Laptop 16-ah0xxx (2025) - Intel Core Ultra 9 275HX + RTX 5080/5090 variants. " +
                       "WARNING: EC registers have completely different layout than legacy OMEN models. " +
                       "Writing to legacy EC addresses (0x34, 0x62, etc.) corrupts EC state and causes " +
                       "caps lock blinking panic. Use WMI/ACPI platform_profile only. " +
                       "V2 fan commands forced. WMI thermal-policy fallback is enabled so Quick Profiles can hold OEM performance behavior when direct EC/MSR limits report unavailable. " +
                       "Discord 2026-06-07 v3.7.1 log shows Max mode levels repeatedly dropping under firmware control; use aggressive one-sample Max reassertion while staying on WMI-only paths."
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
                MaxModeDropChecksBeforeReapply = 1,
                SupportsPerformanceModes = true,
                AllowDecoupledWmiThermalPolicyFallback = true,
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
                HasPerKeyRgb = true,
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
                MaxModeDropChecksBeforeReapply = 1,
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

            // OMEN 17 (2021) Intel - product ID 8BB1 is also shared with Victus 15-fa1xxx;
            // this ID is explicitly marked ambiguous so WMI model context can select the
            // Victus disambiguation entry (8BB1-VICTUS15) when needed.
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

            // OMEN 17-ck1xxx (2022) — GitHub #134/#144 field diagnostics.
            // WMI V1 fan levels are command/readback levels, not independent physical RPM.
            // Keep direct EC and independent curves disabled until board-safe registers are verified.
            AddModel(new ModelCapabilities
            {
                ProductId = "8A18",
                ModelName = "OMEN 17-ck1xxx (2022)",
                ModelNamePattern = "17-ck1",
                ModelYear = 2022,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                SupportsRpmReadback = false,
                FanZoneCount = 2,
                MaxFanLevel = 55,
                HasMuxSwitch = true,
                SupportsGpuPowerBoost = true,
                HasFourZoneRgb = true,
                UserVerified = false,
                Notes = "GitHub #134/#144 — WMI V1 control with worker-backed CPU temperature; fan-level fallback is estimated telemetry, not physical RPM. Direct EC remains unverified."
            });

            // Virtual product ID resolved via ModelNamePattern for ambiguous 8BB1 systems.
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
                AllowV1AutoModeFloorClear = true,
                UserVerified = false,
                Notes = "GitHub #149 — OMEN Transcend 14 (2024) fb0xxx/8C58. Same Transcend 14 board family as 8E41; added AllowV1AutoModeFloorClear to match 8E41 profile. Prefer hp-wmi/ACPI paths; direct legacy EC writes are unsafe on Linux and unverified on Windows."
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
                AllowV1AutoModeFloorClear = true,
                UserVerified = false,
                Notes = "GitHub #99 / Linux reports for 8E41 (Transcend 14-fb1xxx). Discord 2026-06-02 Windows field report confirms exact board identity and WMI V1 behavior; use profile-based control paths, allow V1 auto handoff floor clear, and avoid legacy EC writes or custom curves."
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

            AddModel(new ModelCapabilities
            {
                ProductId = "8C30",
                ModelName = "HP Victus 15 (2023) fb1xxx",
                ModelNamePattern = "15-fb1",
                ModelYear = 2023,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                SupportsPowerLimits = false,
                PerformanceModes = new[] { "Quiet", "Balanced", "Performance" },
                AllowDecoupledWmiThermalPolicyFallback = true,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #135/#139 diagnostics — Victus 15-fb1xxx exact ProductId 8C30. Conservative Victus profile: WMI fan/profile control retained, direct EC writes and CPU power-limit UI disabled, WMI thermal-policy fallback enabled for Performance/Balanced/Quiet pending before/after wattage readback; single-zone backlight assumed pending broader field verification."
            });

            // GitHub Issue #138: Victus 15 ProductId 8DCD reports Performance mode still
            // leaving the CPU EC-limited around 40W. Do not guess PL1/PL2 values from a
            // title-only report; use the conservative Victus 15 control profile so
            // Performance applies through the OEM WMI thermal-policy fallback instead of
            // direct EC writes until diagnostics confirm the exact power envelope.
            AddModel(new ModelCapabilities
            {
                ProductId = "8DCD",
                ModelName = "HP Victus 15 (8DCD)",
                ModelNamePattern = "Victus 15",
                ModelYear = 2024,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                AllowDecoupledWmiThermalPolicyFallback = true,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #138 - Victus 15 ProductId 8DCD reports Performance mode remains EC-limited around 40W. Conservative exact profile disables direct EC writes and enables WMI thermal-policy fallback pending diagnostics/readback validation."
            });

            // Victus 15 (2023) - fb1xxx series
            // GitHub Issue #135: Victus 15-fb1xxx was unresolved by model identity and
            // performance mode changes could fall into the wrong backend path. Keep this
            // profile conservative: no direct EC writes, retain WMI fan/profile control,
            // and allow OEM WMI thermal-policy fallback when EC/MSR power-limit writes are
            // unavailable.
            AddModel(new ModelCapabilities
            {
                ProductId = "fb1xxx_victus15_unverified",
                ModelName = "HP Victus 15 (2023) fb1xxx",
                ModelNamePattern = "15-fb1",
                ModelYear = 2023,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                SupportsPowerLimits = false,
                PerformanceModes = new[] { "Quiet", "Balanced", "Performance" },
                AllowDecoupledWmiThermalPolicyFallback = true,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #135/#139 — Victus 15-fb1xxx. Conservative Victus profile: WMI fan/profile control retained, direct EC writes and CPU power-limit UI disabled, WMI thermal-policy fallback enabled for Performance/Balanced/Quiet pending before/after wattage readback; single-zone backlight assumed pending field verification."
            });

            // Victus 15 (2025) - fb3xxx series (AMD Ryzen 8xxx)
            // GitHub Issue #148: HP Victus 15-fb3012AX — working via Family fallback but
            // performance profile switching needs an explicit entry with WMI thermal-policy
            // fallback. No diagnostics-confirmed ProductId yet; matched on "15-fb3" pattern.
            AddModel(new ModelCapabilities
            {
                ProductId = "fb3xxx_victus15",
                ModelName = "HP Victus 15 (2025) fb3xxx",
                ModelNamePattern = "15-fb3",
                ModelYear = 2025,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 1,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                SupportsPowerLimits = false,
                PerformanceModes = new[] { "Quiet", "Balanced", "Performance" },
                AllowDecoupledWmiThermalPolicyFallback = true,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = false,
                UserVerified = false,
                Notes = "GitHub #148 — HP Victus 15-fb3012AX (2025 AMD). Pattern-matched on '15-fb3'; no diagnostics-confirmed ProductId yet. Conservative Victus profile: WMI fan/profile control, no direct EC writes, WMI thermal-policy fallback for Performance/Balanced/Quiet. User reports no RGB keyboard."
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
            
            // Victus 16-s0xxx (2023/2024) AMD Ryzen 7 7840HS + RTX 4060.
            // RC1 field log 2026-05-16: ProductId 8BD4, BIOS F.30, V1 WMI fan control,
            // two fan levels exposed, no confirmed MUX/GPU boost; RGB handled through WMI ColorTable.
            AddModel(new ModelCapabilities
            {
                ProductId = "8BD4",
                ModelName = "HP Victus 16-s0xxx AMD",
                ModelNamePattern = "16-s0",
                ModelYear = 2023,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                AllowV1AutoModeFloorClear = false,
                HasFourZoneRgb = true,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "RC1 field log - Victus 16-s0xxx (8BD4), Ryzen 7 7840HS + RTX 4060. Conservative WMI V1 fan profile; GPU boost disabled pending verification. Discord 2026-06-08 / 7Z5Z2EA reports basic keyboard RGB should be controllable through WMI ColorTable; EC keyboard writes remain disabled. Discord 2026-06-03 reported fans stuck at max after long gaming session; v3.7.1 Discord 2026-06-07 logs showed non-reactive/0 RPM fan behavior after SetFanLevel(0,0), so V1 manual-zero floor clear is disabled pending a safer handoff sequence."
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

            // Victus 16-e0xxx family reported as unresolved (Issue #128).
            // Keep conservative feature flags until confirmed by hardware reports.
            AddModel(new ModelCapabilities
            {
                ProductId = "88EC",
                ModelName = "HP Victus 16-e0xxx",
                ModelNamePattern = "16-e0",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "Issue #128 — explicit Victus 16-e0xxx mapping (88EC) to avoid low-confidence family fallback; feature flags intentionally conservative pending field verification"
            });
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            AddModel(new ModelCapabilities
            {
                ProductId = "88EE",
                ModelName = "HP Victus 16-e0194nw",
                ModelNamePattern = "16-e0",
                ModelYear = 2022,
                Family = OmenModelFamily.Victus,
                SupportsFanControlWmi = true,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsIndependentFanCurves = false,
                FanZoneCount = 2,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                SupportsUndervolt = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                UserVerified = false,
                Notes = "GitHub #140 - HP Victus 16-e0194nw / ProductId 88EE. Exact conservative sibling of 88EC added so model identity resolves by ProductId instead of low-confidence 16-e0 model-name pattern; feature flags remain conservative pending field verification."
            });

            // OMEN Desktop Series (WMI fan control + desktop RGB)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-25L",
                ModelName = "OMEN 25L Desktop",
                ModelYear = 2021,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false, // v3.6.3 safety gate: desktop fan writes disabled pending validation
                SupportsFanControlEc = false, // Desktop EC registers differ from laptops
                SupportsFanCurves = false,
                SupportsRpmReadback = true, // WMI RPM readback available
                SupportsPerformanceModes = true,
                HasMuxSwitch = false,
                SupportsGpuPowerBoost = false,
                HasKeyboardBacklight = false,
                HasFourZoneRgb = false,
                Notes = "OMEN 25L Desktop - fan writes disabled by v3.6.3 safety gate; RPM telemetry/performance modes only pending hardware validation."
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-30L",
                ModelName = "OMEN 30L Desktop",
                ModelYear = 2022,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 30L Desktop - fan writes disabled by v3.6.3 safety gate; RPM telemetry/performance modes only pending hardware validation."
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-35L",
                ModelName = "OMEN 35L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 35L Desktop - fan writes disabled by v3.6.3 safety gate; RPM telemetry/performance modes only pending hardware validation."
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-40L",
                ModelName = "OMEN 40L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 40L Desktop - fan writes disabled by v3.6.3 safety gate; RPM telemetry/performance modes only pending hardware validation."
            });
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-45L",
                ModelName = "OMEN 45L Desktop",
                ModelYear = 2023,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false,
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsRpmReadback = true,
                SupportsPerformanceModes = true,
                HasKeyboardBacklight = false,
                Notes = "OMEN 45L Desktop - fan writes disabled by v3.6.3 safety gate; RPM telemetry/performance modes only pending hardware validation."
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
        /// Resolve the best database profile from raw identity inputs.
        /// Exact ProductId wins unless the ProductId is known to be shared across model families.
        /// </summary>
        public static ModelCapabilities? GetPreferredCapabilities(string? productId, string? wmiModelName)
        {
            ModelCapabilities? productIdMatch = null;
            var hasExactProductId = !string.IsNullOrWhiteSpace(productId) &&
                _knownModels.TryGetValue(productId.Trim(), out productIdMatch);

            if (hasExactProductId && !IsAmbiguousProductId(productId!))
                return productIdMatch;

            var modelNameMatch = GetCapabilitiesByModelName(wmiModelName ?? string.Empty);
            if (modelNameMatch != null)
                return modelNameMatch;

            return hasExactProductId ? productIdMatch : null;
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
                    SupportsPerformanceModes = templateModel.SupportsPerformanceModes,
                    AllowDecoupledWmiThermalPolicyFallback = templateModel.AllowDecoupledWmiThermalPolicyFallback,
                    AllowV1AutoModeFloorClear = templateModel.AllowV1AutoModeFloorClear,
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

        public static bool IsAmbiguousProductId(string? productId)
        {
            return !string.IsNullOrWhiteSpace(productId) && _ambiguousProductIds.Contains(productId.Trim());
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
