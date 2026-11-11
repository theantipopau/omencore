using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace OmenCore.Models
{
    public class PerformanceMode
    {
        public string Name { get; set; } = string.Empty;
        public int CpuPowerLimitWatts { get; set; }
        public int? CpuBoostPowerLimitWatts { get; set; }
        public int GpuPowerLimitWatts { get; set; }
        public string LinkedPowerPlanGuid { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // ─── Remembered effects (5.0.0, roadmap WP-43) ───────────────────────────────
        //
        // Before 5.0.0 a mode was only a name, two/three wattages and a power plan, so
        // "Quiet" and "Performance" differed in power limits alone and every other choice
        // (fans, GPU boost, lighting, refresh rate, charge limit) stayed wherever the user
        // had last left it. Each null below means "this mode does not touch that setting",
        // which is also what every 4.x config deserialises to - the format is additive, so
        // existing config files keep loading with no migration step.

        /// <summary>Fan preset to apply with this mode (name from <see cref="AppConfig.FanPresets"/>).</summary>
        public string? FanPresetName { get; set; }

        /// <summary>Custom fan curve to apply with this mode. Takes precedence over <see cref="FanPresetName"/>.</summary>
        public List<FanCurvePoint>? FanCurve { get; set; }

        /// <summary>GPU Power Boost level ("Minimum", "Medium", "Maximum", "Extended").</summary>
        public string? GpuPowerBoostLevel { get; set; }

        /// <summary>Panel refresh rate in Hz to switch to with this mode.</summary>
        public int? RefreshRateHz { get; set; }

        /// <summary>Keyboard lighting profile to apply with this mode.</summary>
        public string? LightingProfileName { get; set; }

        /// <summary>Battery charge limit state for this mode. Null leaves it untouched.</summary>
        public bool? ChargeLimitEnabled { get; set; }

        /// <summary>Charge threshold percentage when <see cref="ChargeLimitEnabled"/> is true.</summary>
        public int? ChargeThresholdPercent { get; set; }

        [JsonIgnore]
        public bool HasRememberedEffects =>
            !string.IsNullOrWhiteSpace(FanPresetName) ||
            (FanCurve is { Count: > 0 }) ||
            !string.IsNullOrWhiteSpace(GpuPowerBoostLevel) ||
            RefreshRateHz is > 0 ||
            !string.IsNullOrWhiteSpace(LightingProfileName) ||
            ChargeLimitEnabled.HasValue;

        /// <summary>
        /// Human-readable labels for what this mode carries. Kept here rather than in the UI so
        /// the diagnostics export and the mode card describe the same thing.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<string> RememberedEffectLabels
        {
            get
            {
                var labels = new List<string>();
                if (FanCurve is { Count: > 0 })
                {
                    labels.Add("fan curve");
                }
                else if (!string.IsNullOrWhiteSpace(FanPresetName))
                {
                    labels.Add($"fan preset '{FanPresetName}'");
                }

                if (!string.IsNullOrWhiteSpace(GpuPowerBoostLevel))
                {
                    labels.Add($"GPU boost '{GpuPowerBoostLevel}'");
                }

                if (RefreshRateHz is > 0)
                {
                    labels.Add($"{RefreshRateHz} Hz");
                }

                if (!string.IsNullOrWhiteSpace(LightingProfileName))
                {
                    labels.Add($"lighting '{LightingProfileName}'");
                }

                if (ChargeLimitEnabled == true)
                {
                    labels.Add(ChargeThresholdPercent.HasValue
                        ? $"charge limit {ChargeThresholdPercent}%"
                        : "charge limit on");
                }
                else if (ChargeLimitEnabled == false)
                {
                    labels.Add("full charge");
                }

                return labels;
            }
        }

        [JsonIgnore]
        public string RememberedEffectsSummary =>
            RememberedEffectLabels.Count == 0
                ? "Nothing yet - power limits only"
                : string.Join(", ", RememberedEffectLabels);

        /// <summary>
        /// Copies the remembered-effect block from <paramref name="source"/>. Used when the UI
        /// rebuilds its mode list from config and when a snapshot is captured into a mode.
        /// </summary>
        public void CopyRememberedEffectsFrom(PerformanceMode source)
        {
            FanPresetName = source.FanPresetName;
            FanCurve = source.FanCurve?
                .Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent })
                .ToList();
            GpuPowerBoostLevel = source.GpuPowerBoostLevel;
            RefreshRateHz = source.RefreshRateHz;
            LightingProfileName = source.LightingProfileName;
            ChargeLimitEnabled = source.ChargeLimitEnabled;
            ChargeThresholdPercent = source.ChargeThresholdPercent;
        }

        public PerformanceMode Clone() => new()
        {
            Name = Name,
            CpuPowerLimitWatts = CpuPowerLimitWatts,
            CpuBoostPowerLimitWatts = CpuBoostPowerLimitWatts,
            GpuPowerLimitWatts = GpuPowerLimitWatts,
            LinkedPowerPlanGuid = LinkedPowerPlanGuid,
            Description = Description,
            FanPresetName = FanPresetName,
            FanCurve = FanCurve?
                .Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent })
                .ToList(),
            GpuPowerBoostLevel = GpuPowerBoostLevel,
            RefreshRateHz = RefreshRateHz,
            LightingProfileName = LightingProfileName,
            ChargeLimitEnabled = ChargeLimitEnabled,
            ChargeThresholdPercent = ChargeThresholdPercent
        };

        public override string ToString() => Name;
    }
}
