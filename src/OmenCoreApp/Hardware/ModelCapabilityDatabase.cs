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
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 17 Series (17.3" laptops)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
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
            
            // OMEN 17-ck2xxx (2023) - 13th gen Intel, RTX 4080/4090
            // Note: ProductId 8BAD is shared with OMEN 15 (2021), but 17-ck2 model name takes precedence
            // BUG FIX v2.7.1: WMI commands return success but don't actually change fan speed on 17-ck2
            // Similar to Transcend models - requires OGH proxy or direct EC access
            AddModel(new ModelCapabilities
            {
                ProductId = "17CK2", // Virtual ID for model name matching
                ModelName = "OMEN 17-ck2xxx (2023)",
                ModelNamePattern = "17-ck2", // For model name matching
                ModelYear = 2023,
                Family = OmenModelFamily.OMEN17,
                SupportsFanControlWmi = false, // WMI returns success but fans don't respond - needs OGH proxy
                SupportsFanControlEc = true,
                SupportsFanCurves = true,
                SupportsIndependentFanCurves = true,
                HasMuxSwitch = true, // 17-ck2 with RTX 4090 has MUX
                SupportsGpuPowerBoost = true,
                SupportsAdvancedOptimus = true,
                HasFourZoneRgb = true,
                SupportsUndervolt = true, // 13th gen Intel supports undervolt
                UserVerified = true,
                Notes = "OMEN 17-ck2 series (2023) - WMI ineffective, use OGH proxy or EC access"
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
                SupportsUndervolt = false,
                HasFourZoneRgb = false,
                HasKeyboardBacklight = true,
                Notes = "Victus has limited features compared to OMEN"
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
                HasFourZoneRgb = true, // Victus 16 has 4-zone
                UserVerified = true
            });
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN Desktop Series (limited support)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new ModelCapabilities
            {
                ProductId = "DESKTOP-25L",
                ModelName = "OMEN 25L Desktop",
                ModelYear = 2021,
                Family = OmenModelFamily.Desktop,
                SupportsFanControlWmi = false, // Desktops use different EC
                SupportsFanControlEc = false,
                SupportsFanCurves = false,
                SupportsRpmReadback = false,
                SupportsPerformanceModes = true, // Via BIOS/OGH only
                HasMuxSwitch = false, // No MUX on desktop
                SupportsGpuPowerBoost = false,
                HasKeyboardBacklight = false,
                HasFourZoneRgb = false,
                Notes = "OMEN Desktop uses different EC - fan control not supported via OmenCore"
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
                SupportsRpmReadback = false,
                HasKeyboardBacklight = false,
                Notes = "OMEN Desktop uses different EC - fan control not supported via OmenCore"
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
                SupportsRpmReadback = false,
                HasKeyboardBacklight = false,
                Notes = "OMEN Desktop uses different EC - fan control not supported via OmenCore"
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
                SupportsRpmReadback = false,
                HasKeyboardBacklight = false,
                Notes = "OMEN Desktop uses different EC - fan control not supported via OmenCore"
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
