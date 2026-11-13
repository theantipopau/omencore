using System.Collections.Generic;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Applies and captures the non-power settings a <see cref="PerformanceMode"/> remembers
    /// (fans, GPU boost, refresh rate, lighting, charge limit).
    ///
    /// Kept as an interface in Core so <see cref="PerformanceModeService"/> can own the apply
    /// order without referencing any UI type, and so tests can substitute a recorder.
    /// Implementations must be synchronous, must not throw for a single failed sub-setting,
    /// and must report what they actually changed rather than what they attempted.
    /// </summary>
    public interface IPerformanceModeEffectApplier
    {
        /// <summary>Applies every remembered effect present on <paramref name="mode"/>.</summary>
        PerformanceModeEffectResult Apply(PerformanceMode mode);

        /// <summary>
        /// Reads the current state of the same settings so the UI can store them into a mode.
        /// Returns null when the platform cannot be read at all.
        /// </summary>
        PerformanceModeEffectSnapshot? CaptureCurrentState();
    }

    /// <summary>
    /// What an apply actually did. "Applied" is only for effects confirmed by the owning service;
    /// anything the platform could not do belongs in <see cref="Skipped"/> or <see cref="Failed"/>,
    /// so a mode switch can never claim more than it performed.
    /// </summary>
    public sealed class PerformanceModeEffectResult
    {
        public List<string> Applied { get; } = new();
        public List<string> Skipped { get; } = new();
        public List<string> Failed { get; } = new();

        public bool AnyApplied => Applied.Count > 0;

        public string AppliedSummary => string.Join(", ", Applied);
        public string SkippedSummary => string.Join(", ", Skipped);
        public string FailedSummary => string.Join(", ", Failed);
    }

    /// <summary>
    /// Point-in-time copy of the settings a mode can remember. Produced by
    /// <see cref="IPerformanceModeEffectApplier.CaptureCurrentState"/> and written into a mode by
    /// <see cref="ApplyTo"/>.
    /// </summary>
    public sealed class PerformanceModeEffectSnapshot
    {
        public string? FanPresetName { get; set; }
        public List<FanCurvePoint>? FanCurve { get; set; }
        public string? GpuPowerBoostLevel { get; set; }
        public int? RefreshRateHz { get; set; }
        public string? LightingProfileName { get; set; }
        public bool? ChargeLimitEnabled { get; set; }
        public int? ChargeThresholdPercent { get; set; }

        public void ApplyTo(PerformanceMode mode)
        {
            mode.FanPresetName = FanPresetName;
            mode.FanCurve = FanCurve;
            mode.GpuPowerBoostLevel = GpuPowerBoostLevel;
            mode.RefreshRateHz = RefreshRateHz;
            mode.LightingProfileName = LightingProfileName;
            mode.ChargeLimitEnabled = ChargeLimitEnabled;
            // A null threshold means the capture could not read one (the BIOS exposes only an
            // on/off care mode), so keep whatever percentage the mode already labels itself with.
            mode.ChargeThresholdPercent = ChargeThresholdPercent ?? mode.ChargeThresholdPercent;
        }

        public static PerformanceModeEffectSnapshot FromMode(PerformanceMode mode) => new()
        {
            FanPresetName = mode.FanPresetName,
            FanCurve = mode.FanCurve,
            GpuPowerBoostLevel = mode.GpuPowerBoostLevel,
            RefreshRateHz = mode.RefreshRateHz,
            LightingProfileName = mode.LightingProfileName,
            ChargeLimitEnabled = mode.ChargeLimitEnabled,
            ChargeThresholdPercent = mode.ChargeThresholdPercent
        };
    }
}
