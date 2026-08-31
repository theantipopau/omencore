using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Models
{
    /// <summary>
    /// Canonical fan-mode name aliases used across view models.
    /// Keeps tray, startup-restore, and fan-page mode mapping consistent.
    /// </summary>
    public static class FanModeNameResolver
    {
        public static bool IsCustomAlias(string? value)
        {
            return ContainsAliasToken(value, "custom", "manual");
        }

        public static bool IsMaxAlias(string? value)
        {
            return ContainsAliasToken(value, "max", "maximum");
        }

        public static bool IsQuietAlias(string? value)
        {
            return ContainsAliasToken(value, "quiet", "silent", "cool", "battery");
        }

        public static bool IsAutoAlias(string? value)
        {
            return ContainsAliasToken(value, "auto", "balanced", "default");
        }

        public static bool IsPerformanceAlias(string? value)
        {
            return ContainsAliasToken(value, "performance", "turbo", "extreme", "gaming", "boost");
        }

        public static string ResolveGeneralProfileFromPresetName(string? presetName)
        {
            if (IsMaxAlias(presetName) || IsPerformanceAlias(presetName))
            {
                return "Performance";
            }

            if (IsQuietAlias(presetName))
            {
                return "Quiet";
            }

            if (IsAutoAlias(presetName))
            {
                return "Balanced";
            }

            if (IsCustomAlias(presetName))
            {
                return "Custom";
            }

            return "Custom";
        }

        public static FanMode ResolveBuiltInFanMode(string? value)
        {
            if (IsMaxAlias(value)) return FanMode.Max;
            if (IsQuietAlias(value)) return FanMode.Quiet;
            if (IsPerformanceAlias(value)) return FanMode.Performance;
            if (IsCustomAlias(value)) return FanMode.Manual;
            return FanMode.Auto;
        }

        public static List<FanCurvePoint> BuildBuiltInCurve(string? presetName, FanMode mode)
        {
            if (mode == FanMode.Max || IsMaxAlias(presetName))
            {
                return new() { new FanCurvePoint { TemperatureC = 0, FanPercent = 100 } };
            }

            if (IsQuietAlias(presetName))
            {
                // v3.7.0: Quiet profile caps at 85% at 80°C and holds there — no further ramp.
                // Thermal safety override (QuietSafetyMonitor) handles critically-high temps by
                // switching to Max fans, so the curve itself stays within silent-operation bounds.
                return new()
                {
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 25 },
                    new FanCurvePoint { TemperatureC = 65, FanPercent = 38 },
                    new FanCurvePoint { TemperatureC = 75, FanPercent = 60 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 85 }
                };
            }

            if (IsPerformanceAlias(presetName))
            {
                if (ContainsAliasToken(presetName, "gaming"))
                {
                    return new()
                    {
                        new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                        new FanCurvePoint { TemperatureC = 50, FanPercent = 42 },
                        new FanCurvePoint { TemperatureC = 60, FanPercent = 58 },
                        new FanCurvePoint { TemperatureC = 70, FanPercent = 72 },
                        new FanCurvePoint { TemperatureC = 80, FanPercent = 100 }
                    };
                }

                return new()
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 38 },
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 50 },
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 66 },
                    new FanCurvePoint { TemperatureC = 70, FanPercent = 88 },
                    new FanCurvePoint { TemperatureC = 75, FanPercent = 100 }
                };
            }

            return new()
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                new FanCurvePoint { TemperatureC = 50, FanPercent = 38 },
                new FanCurvePoint { TemperatureC = 60, FanPercent = 50 },
                new FanCurvePoint { TemperatureC = 68, FanPercent = 72 },
                new FanCurvePoint { TemperatureC = 75, FanPercent = 100 }
            };
        }

        public static string ResolveCardMode(FanPreset preset)
        {
            if (preset == null)
            {
                return "Auto";
            }

            if (IsMaxAlias(preset.Name)) return "Max";

            var token = Normalize(preset.Name);
            if (token is "extreme") return "Extreme";
            if (token is "gaming") return "Gaming";
            if (IsQuietAlias(token)) return "Silent";
            if (IsAutoAlias(token)) return "Auto";

            return preset.Mode switch
            {
                FanMode.Manual => "Custom",
                FanMode.Max => "Max",
                FanMode.Quiet => "Silent",
                FanMode.Auto => "Auto",
                _ => "Auto"
            };
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static bool ContainsAliasToken(string? value, params string[] aliases)
        {
            var normalized = Normalize(value);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            if (aliases.Contains(normalized, StringComparer.Ordinal))
            {
                return true;
            }

            var tokens = normalized
                .Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']', '/' }, StringSplitOptions.RemoveEmptyEntries);

            return tokens.Any(token => aliases.Contains(token, StringComparer.Ordinal));
        }
    }
}
