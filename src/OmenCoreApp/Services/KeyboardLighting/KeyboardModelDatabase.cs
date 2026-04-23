using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Model-specific keyboard configuration.
    /// </summary>
    public class KeyboardModelConfig
    {
        /// <summary>HP Product ID (e.g., "8A14", "8BAD", "8CD1").</summary>
        public string ProductId { get; set; } = "";
        
        /// <summary>Human-readable model name.</summary>
        public string ModelName { get; set; } = "";
        
        /// <summary>Keyboard type classification.</summary>
        public KeyboardType KeyboardType { get; set; } = KeyboardType.Unknown;
        
        /// <summary>Preferred backend method for this model.</summary>
        public KeyboardMethod PreferredMethod { get; set; } = KeyboardMethod.Unknown;
        
        /// <summary>Fallback methods to try if preferred fails.</summary>
        public KeyboardMethod[] FallbackMethods { get; set; } = Array.Empty<KeyboardMethod>();
        
        /// <summary>EC registers for zone colors (if using EcDirect).</summary>
        public byte[]? EcColorRegisters { get; set; }
        
        /// <summary>EC register for brightness control.</summary>
        public byte? EcBrightnessRegister { get; set; }
        
        /// <summary>Whether backlight toggle is required before color changes.</summary>
        public bool RequiresBacklightToggle { get; set; }
        
        /// <summary>Notes about this model's quirks.</summary>
        public string? Notes { get; set; }
        
        /// <summary>
        /// Model name pattern used to disambiguate when multiple models share the same product ID.
        /// If set, the lookup will prefer this entry when the WMI model name contains this substring.
        /// </summary>
        public string? ModelNamePattern { get; set; }
        
        /// <summary>Verified by users (false = theoretical/untested).</summary>
        public bool UserVerified { get; set; }
        
        /// <summary>Model year (approximate).</summary>
        public int ModelYear { get; set; }
    }

    /// <summary>
    /// Database of known HP OMEN keyboard models and their configurations.
    /// This is built from reverse engineering and user reports.
    /// </summary>
    public static class KeyboardModelDatabase
    {
        private static readonly Dictionary<string, KeyboardModelConfig> _knownModels = new(StringComparer.OrdinalIgnoreCase);
        
            /// <summary>
            /// Product IDs known to be shared across different model families.
            /// When a product ID is in this set, model-name disambiguation is attempted before
            /// returning the product-ID-matched config.
            /// </summary>
            private static readonly HashSet<string> _ambiguousProductIds = new(StringComparer.OrdinalIgnoreCase)
            {
                // 8BB1 is used by both OMEN 17 (2021) Intel and Victus 15-fa1xxx (2022)
                "8BB1",
            };
        
        static KeyboardModelDatabase()
        {
            InitializeDatabase();
        }
        
        private static void InitializeDatabase()
        {
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 15 Series (15.6" laptops)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // OMEN 15 (2020) - Intel/AMD, 4-zone RGB
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A14",
                ModelName = "OMEN 15 (2020) Intel",
                KeyboardType = KeyboardType.FourZoneTkl,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                EcColorRegisters = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC },
                EcBrightnessRegister = 0xBD,
                ModelYear = 2020,
                Notes = "Standard 128-byte ColorTable format"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A15",
                ModelName = "OMEN 15 (2020) AMD",
                KeyboardType = KeyboardType.FourZoneTkl,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                EcColorRegisters = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC },
                EcBrightnessRegister = 0xBD,
                ModelYear = 2020
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BAD",
                ModelName = "OMEN 15/17 (2021-2023) Intel",
                KeyboardType = KeyboardType.FourZoneTkl,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                EcColorRegisters = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC },
                EcBrightnessRegister = 0xBD,
                ModelYear = 2021
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BAE",
                ModelName = "OMEN 15 (2021) AMD",
                KeyboardType = KeyboardType.FourZoneTkl,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2021
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 16 Series (16.1" laptops)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BAF",
                ModelName = "OMEN 16 (2021) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2021
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB0",
                ModelName = "OMEN 16 (2021) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2021
            });
            
            // OMEN 16 (2022)
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8CD0",
                ModelName = "OMEN 16 (2022) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2022
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8CD1",
                ModelName = "OMEN 16 (2022) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2022,
                Notes = "Confirmed working by user reports"
            });

            // OMEN 16 (2022) - n0xxx series
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A44",
                ModelName = "OMEN 16-n0xxx (2022) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023, KeyboardMethod.EcDirect },
                ModelYear = 2022,
                UserVerified = false,
                Notes = "GitHub #112 — inferred from adjacent OMEN 16 generations; verify on real hardware"
            });
            
            // OMEN 16 (2023) - May use new interface
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8E67",
                ModelName = "OMEN 16 (2023) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.NewWmi2023,
                FallbackMethods = new[] { KeyboardMethod.ColorTable2020, KeyboardMethod.EcDirect },
                ModelYear = 2023,
                Notes = "May require new WMI interface - needs testing"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8E68",
                ModelName = "OMEN 16 (2023) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.NewWmi2023,
                FallbackMethods = new[] { KeyboardMethod.ColorTable2020, KeyboardMethod.EcDirect },
                ModelYear = 2023
            });
            
            // OMEN 16 (2024) - xd0xxx series
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BCD",
                ModelName = "OMEN 16-xd0xxx (2024) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023, KeyboardMethod.EcDirect },
                ModelYear = 2024,
                Notes = "Ryzen 7 7840HS - user reported AC detection issues"
            });

            // OMEN 16 (2024) - am0xxx series
            // GitHub Issue #111: no keyboard match for 8D2F / OMEN Gaming Laptop 16-am0xxx
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8D2F",
                ModelName = "OMEN 16-am0xxx (2024) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023, KeyboardMethod.EcDirect },
                ModelYear = 2024,
                UserVerified = false,
                Notes = "GitHub #111 — OMEN Gaming Laptop 16-am0xxx. Keyboard config mirrors xd0/ap0 sibling generation."
            });

            // OMEN 16 (2025) - ap0xxx series (AMD Ryzen AI + RTX 50-series)
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8D24",
                ModelName = "OMEN 16-ap0xxx (2025) AMD",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023, KeyboardMethod.EcDirect },
                ModelYear = 2025,
                Notes = "Ryzen AI 9 365 + RTX 5060 - V1 WMI keyboard interface"
            });

            // OMEN Transcend 14 (2024) - fb1xxx series
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8E41",
                ModelName = "OMEN Transcend 14-fb1xxx (2024)",
                KeyboardType = KeyboardType.PerKeyRgb,
                PreferredMethod = KeyboardMethod.NewWmi2023,
                FallbackMethods = new[] { KeyboardMethod.ColorTable2020, KeyboardMethod.EcDirect },
                ModelYear = 2024,
                UserVerified = false,
                Notes = "GitHub #99 / Discord Linux reports — Transcend 14-fb1xxx; prefer newer WMI path"
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 17 Series (17.3" laptops)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN 17 Series (17.3" laptops)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A22",
                ModelName = "OMEN 17 (2020) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2020
            });
            
            // OMEN 17 (2021) Intel — product ID 8BB1 is also shared with Victus 15-fa1xxx.
            // The Victus entry (8BB1-VICTUS15) is resolved first via ModelNamePattern matching.
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB1",
                ModelName = "OMEN 17 (2021) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2021
            });
            
            // Victus 15-fa1xxx shares product ID 8BB1 with OMEN 17 (2021) Intel.
            // Stored under a virtual key; resolved by model-name pattern during ambiguous lookup.
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB1-VICTUS15",
                ModelName = "HP Victus 15-fa1xxx (2022)",
                ModelNamePattern = "fa1",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2022,
                UserVerified = false,
                Notes = "Victus 15-fa1xxx — single-color backlight; 8BB1 product ID shared with OMEN 17 (2021)"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8CD2",
                ModelName = "OMEN 17 (2022) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2022
            });
            
            // OMEN 17 (ck2xxx) - 2024 models
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8E69",
                ModelName = "OMEN 17-ck2xxx (2024)",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.NewWmi2023,
                FallbackMethods = new[] { KeyboardMethod.ColorTable2020, KeyboardMethod.EcDirect },
                ModelYear = 2024,
                Notes = "Intel Core Ultra 9 275HX - user reported undervolt locked"
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN Max 16 (Premium 2025 model)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "OMENMAX16",
                ModelName = "OMEN Max 16 (2025)",
                ModelNamePattern = "max 16",
                KeyboardType = KeyboardType.PerKeyRgb,
                PreferredMethod = KeyboardMethod.HidPerKey,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023 },
                ModelYear = 2025,
                Notes = "Per-key RGB, RTX 5080, Intel Core Ultra 9 275HX"
            });

            // OMEN MAX 16 (2025) - ak0xxx family (GitHub #117)
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8D87",
                ModelName = "OMEN MAX 16-ak0xxx (2025) AMD",
                ModelNamePattern = "16-ak0",
                KeyboardType = KeyboardType.PerKeyRgb,
                PreferredMethod = KeyboardMethod.HidPerKey,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023, KeyboardMethod.ColorTable2020 },
                ModelYear = 2025,
                UserVerified = false,
                Notes = "GitHub #117 — OMEN MAX Gaming Laptop 16-ak0xxx (Product ID 8D87). Keyboard profile inferred from MAX 16 generation; verify per-key behavior on hardware."
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // OMEN Desktop PCs (25L, 30L, 40L, 45L series)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A16",
                ModelName = "OMEN 25L Desktop",
                KeyboardType = KeyboardType.Desktop,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2020,
                Notes = "Desktop chassis - external keyboard may vary"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB3",
                ModelName = "OMEN 30L Desktop",
                KeyboardType = KeyboardType.Desktop,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2021,
                Notes = "Desktop - may control internal RGB, not keyboard"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8CD4",
                ModelName = "OMEN 40L Desktop",
                KeyboardType = KeyboardType.Desktop,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2022
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8E6A",
                ModelName = "OMEN 45L Desktop",
                KeyboardType = KeyboardType.Desktop,
                PreferredMethod = KeyboardMethod.NewWmi2023,
                FallbackMethods = new[] { KeyboardMethod.ColorTable2020 },
                ModelYear = 2023
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // HP Victus Series (Budget gaming line)
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A23",
                ModelName = "HP Victus 15 (2021)",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2021,
                Notes = "Single-color backlight only, no RGB zones"
            });

            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A3E",
                ModelName = "HP Victus 15-fb0xxx (2022)",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2022,
                UserVerified = false,
                Notes = "GitHub #105 — Victus 15-fb0xxx; conservative single-zone backlight profile"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB4",
                ModelName = "HP Victus 16 (2022)",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2022,
                Notes = "Single-color backlight only"
            });

            // Reported by users: Victus model where keyboard zones were not applied (PN: 8BD5)
            // Previously fell back to generic Victus defaults which caused only the lightbar to update.
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BD5",
                ModelName = "HP Victus 16 (2023) - 8BD5",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2023,
                UserVerified = false,
                Notes = "Added from user report — ensures keyboard zones are applied instead of falling back to Victus defaults"
            });

            // Additional Victus model reported by community (PN: 8A26)
            // Ensures per-zone ColorTable method is attempted instead of falling back to single-color defaults.
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8A26",
                ModelName = "HP Victus 16 (2024) - 8A26",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2024,
                UserVerified = false,
                Notes = "Added from user report — ensures Victus 16 PN:8A26 applies keyboard zones correctly"
            });

            // Victus 16 Ryzen (2024+) - r0xxx series - 4-zone RGB keyboard
            // GitHub Issue #89: Victus 16-r0xxx keyboard light control not working
            // Common Ryzen variant models with FourZone RGB support
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8C2F",
                ModelName = "HP Victus 16 (2024+) Ryzen r0xxx",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2024,
                UserVerified = false,
                Notes = "GitHub #89 — Victus 16-r0xxx (Ryzen 2024+) with 4-zone RGB keyboard support"
            });

            // ═══════════════════════════════════════════════════════════════════════════════════
            // Older OMEN Models (2018-2019) - Backlight only
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8575",
                ModelName = "OMEN 15 (2018)",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2018,
                Notes = "Pre-RGB era, backlight toggle only"
            });
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "860C",
                ModelName = "OMEN 17 (2019)",
                KeyboardType = KeyboardType.BacklightOnly,
                PreferredMethod = KeyboardMethod.BacklightOnly,
                FallbackMethods = Array.Empty<KeyboardMethod>(),
                ModelYear = 2019
            });
        }
        
        private static void AddModel(KeyboardModelConfig config)
        {
            if (!string.IsNullOrEmpty(config.ProductId))
            {
                _knownModels[config.ProductId] = config;
            }
        }
        
        /// <summary>
        /// Get configuration for a specific product ID.
        /// </summary>
        public static KeyboardModelConfig? GetConfig(string productId)
            {
                return GetConfig(productId, wmiModelName: null);
            }
        
            /// <summary>
            /// Get configuration for a specific product ID, with optional model-name disambiguation.
            /// When the product ID is in the ambiguous set and <paramref name="wmiModelName"/> is
            /// provided, entries whose <see cref="KeyboardModelConfig.ModelNamePattern"/> matches the
            /// model name take priority over the plain product-ID hit.
            /// </summary>
            public static KeyboardModelConfig? GetConfig(string productId, string? wmiModelName)
        {
            if (string.IsNullOrEmpty(productId))
                return null;
                
            // Try exact match first
            if (_knownModels.TryGetValue(productId, out var config))
                {
                    // If the product ID is ambiguous and we have a model name, check for a more
                    // specific override entry via its ModelNamePattern field.
                    if (!string.IsNullOrEmpty(wmiModelName) && _ambiguousProductIds.Contains(productId))
                    {
                        var disamb = TryDisambiguateByModelName(productId, wmiModelName);
                        if (disamb != null)
                            return disamb;
                    }
                    return config;
                }
            
            // Try partial match (some product IDs are truncated)
            var partialMatch = _knownModels.Values
                .FirstOrDefault(c => productId.Contains(c.ProductId, StringComparison.OrdinalIgnoreCase) ||
                                    c.ProductId.Contains(productId, StringComparison.OrdinalIgnoreCase));
            
            return partialMatch;
        }
        
            /// <summary>Returns true if this product ID is shared across different model families.</summary>
            public static bool IsAmbiguousProductId(string productId) =>
                !string.IsNullOrEmpty(productId) && _ambiguousProductIds.Contains(productId);
        
            /// <summary>
            /// Scans all models whose ProductId starts with the given prefix (virtual disambiguation
            /// keys such as "8BB1-VICTUS15") and returns the first one whose ModelNamePattern is a
            /// case-insensitive substring of <paramref name="wmiModelName"/>.
            /// </summary>
            private static KeyboardModelConfig? TryDisambiguateByModelName(string productId, string wmiModelName)
            {
                var lowerModel = wmiModelName.ToLowerInvariant();
                var prefix = productId + "-";
                return _knownModels.Values.FirstOrDefault(c =>
                    c.ProductId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(c.ModelNamePattern) &&
                    lowerModel.Contains(c.ModelNamePattern.ToLowerInvariant()));
            }
        
        /// <summary>
        /// Get configuration based on model name (fuzzy match).
        /// Supports both full containment and keyword-based matching for model series.
        /// For example, matches "OMEN by HP Gaming Laptop 16-xd0xxx" against "OMEN 16-xd0xxx (2024) AMD"
        /// by extracting the model series identifier (e.g., "16-xd0xxx").
        /// </summary>
        public static KeyboardModelConfig? GetConfigByModelName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return null;
            
            var lowerName = modelName.ToLowerInvariant();

            // Try explicit model-name pattern matches first (used by virtual entries
            // where the HP baseboard product ID may not uniquely identify the keyboard).
            var patternMatch = _knownModels.Values.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.ModelNamePattern) &&
                lowerName.Contains(c.ModelNamePattern.ToLowerInvariant()));

            if (patternMatch != null)
                return patternMatch;
            
            // Try exact containment match first (original behavior)
            var exactMatch = _knownModels.Values.FirstOrDefault(c => 
                lowerName.Contains(c.ModelName.ToLowerInvariant()) ||
                c.ModelName.ToLowerInvariant().Contains(lowerName));
            
            if (exactMatch != null)
                return exactMatch;
            
            // Try keyword-based matching: extract the model series identifier
            // HP WMI model names look like "OMEN by HP Gaming Laptop 16-xd0xxx"
            // Our DB names look like "OMEN 16-xd0xxx (2024) AMD"
            // The common part is the series identifier like "16-xd0xxx", "17-ck0xxx", etc.
            // Match on the model series pattern (e.g., "16-xd0xxx", "15-ek0xxx", "17-cm0xxx")
            var seriesPattern = System.Text.RegularExpressions.Regex.Match(lowerName, @"\d{2}-[a-z]{2}\d{1,4}[a-z]*");
            if (seriesPattern.Success)
            {
                var series = seriesPattern.Value;
                var seriesMatch = _knownModels.Values.FirstOrDefault(c => 
                    c.ModelName.ToLowerInvariant().Contains(series));
                if (seriesMatch != null)
                    return seriesMatch;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get a default configuration for unknown models based on approximate year.
        /// </summary>
        public static KeyboardModelConfig GetDefaultConfig(int? estimatedYear = null)
        {
            var year = estimatedYear ?? DateTime.Now.Year;
            
            if (year >= 2023)
            {
                return new KeyboardModelConfig
                {
                    ProductId = "UNKNOWN",
                    ModelName = "Unknown OMEN (2023+)",
                    KeyboardType = KeyboardType.FourZone,
                    PreferredMethod = KeyboardMethod.NewWmi2023,
                    FallbackMethods = new[] { KeyboardMethod.ColorTable2020, KeyboardMethod.EcDirect },
                    ModelYear = year,
                    Notes = "Auto-detected unknown model - trying all methods"
                };
            }
            else if (year >= 2020)
            {
                return new KeyboardModelConfig
                {
                    ProductId = "UNKNOWN",
                    ModelName = "Unknown OMEN (2020-2022)",
                    KeyboardType = KeyboardType.FourZone,
                    PreferredMethod = KeyboardMethod.ColorTable2020,
                    FallbackMethods = new[] { KeyboardMethod.EcDirect },
                    ModelYear = year,
                    Notes = "Auto-detected unknown model"
                };
            }
            else
            {
                return new KeyboardModelConfig
                {
                    ProductId = "UNKNOWN",
                    ModelName = "Unknown OMEN (Pre-2020)",
                    KeyboardType = KeyboardType.BacklightOnly,
                    PreferredMethod = KeyboardMethod.BacklightOnly,
                    FallbackMethods = Array.Empty<KeyboardMethod>(),
                    ModelYear = year,
                    Notes = "Older model - may only support backlight toggle"
                };
            }
        }
        
        /// <summary>
        /// Get all known models.
        /// </summary>
        public static IEnumerable<KeyboardModelConfig> GetAllModels() => _knownModels.Values;
        
        /// <summary>
        /// Get models by year range.
        /// </summary>
        public static IEnumerable<KeyboardModelConfig> GetModelsByYear(int startYear, int endYear)
        {
            return _knownModels.Values.Where(c => c.ModelYear >= startYear && c.ModelYear <= endYear);
        }
        
        /// <summary>
        /// Get models by keyboard type.
        /// </summary>
        public static IEnumerable<KeyboardModelConfig> GetModelsByType(KeyboardType type)
        {
            return _knownModels.Values.Where(c => c.KeyboardType == type);
        }
    }
}
