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
            // OMEN 17 Series
            // ═══════════════════════════════════════════════════════════════════════════════════

            // OMEN 17-ck2xxx (2023 Intel, 13th Gen) — verified by user (Product ID 8BAD shared with OMEN 15)
            AddModel(new KeyboardModelConfig
            {
                ProductId = "17CK2",  // Virtual ID — matched via model name pattern, not product ID
                ModelName = "OMEN 17-ck2xxx (2023)",
                KeyboardType = KeyboardType.FourZoneTkl,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                EcColorRegisters = new byte[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC },
                EcBrightnessRegister = 0xBD,
                ModelYear = 2023
            });
            
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
            
            AddModel(new KeyboardModelConfig
            {
                ProductId = "8BB1",
                ModelName = "OMEN 17 (2021) Intel",
                KeyboardType = KeyboardType.FourZone,
                PreferredMethod = KeyboardMethod.ColorTable2020,
                FallbackMethods = new[] { KeyboardMethod.EcDirect },
                ModelYear = 2021
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
                ProductId = "ah0097nr",
                ModelName = "OMEN Max 16 (2025)",
                KeyboardType = KeyboardType.PerKeyRgb,
                PreferredMethod = KeyboardMethod.HidPerKey,
                FallbackMethods = new[] { KeyboardMethod.NewWmi2023 },
                ModelYear = 2025,
                Notes = "Per-key RGB, RTX 5080, Intel Core Ultra 9 275HX"
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
            if (string.IsNullOrEmpty(productId))
                return null;
                
            // Try exact match first
            if (_knownModels.TryGetValue(productId, out var config))
                return config;
            
            // Try partial match (some product IDs are truncated)
            var partialMatch = _knownModels.Values
                .FirstOrDefault(c => productId.Contains(c.ProductId, StringComparison.OrdinalIgnoreCase) ||
                                    c.ProductId.Contains(productId, StringComparison.OrdinalIgnoreCase));
            
            return partialMatch;
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
