using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCoreApp.Services
{
    /// <summary>
    /// Applies the non-power settings a <see cref="PerformanceMode"/> remembers: fan preset or
    /// curve, GPU Power Boost level, panel refresh rate, keyboard lighting profile and battery
    /// charge limit (roadmap WP-43).
    ///
    /// Deliberately depends on services rather than view models, except for two small delegates
    /// (lighting apply/capture) because keyboard lighting is driven from the lighting view model
    /// and its apply path is asynchronous. Every branch reports what it actually changed; a
    /// setting the platform cannot do lands in Skipped, never Applied.
    /// </summary>
    public sealed class PerformanceModeEffectApplier : IPerformanceModeEffectApplier
    {
        private static readonly string[] ValidGpuBoostLevels = { "Minimum", "Medium", "Maximum", "Extended" };

        private readonly LoggingService _logging;
        private readonly FanService _fanService;
        private readonly DisplayService _displayService;
        private readonly HpWmiBios? _wmiBios;
        private readonly Func<string, FanPreset?> _resolveFanPreset;
        private readonly Action<string>? _applyLightingProfile;
        private readonly Func<string?>? _captureLightingProfileName;

        public PerformanceModeEffectApplier(
            LoggingService logging,
            FanService fanService,
            DisplayService displayService,
            HpWmiBios? wmiBios,
            Func<string, FanPreset?> resolveFanPreset,
            Action<string>? applyLightingProfile = null,
            Func<string?>? captureLightingProfileName = null)
        {
            _logging = logging;
            _fanService = fanService;
            _displayService = displayService;
            _wmiBios = wmiBios;
            _resolveFanPreset = resolveFanPreset;
            _applyLightingProfile = applyLightingProfile;
            _captureLightingProfileName = captureLightingProfileName;
        }

        public PerformanceModeEffectResult Apply(PerformanceMode mode)
        {
            var result = new PerformanceModeEffectResult();

            ApplyFan(mode, result);
            ApplyGpuPowerBoost(mode, result);
            ApplyRefreshRate(mode, result);
            ApplyLighting(mode, result);
            ApplyChargeLimit(mode, result);

            if (result.Applied.Count > 0)
            {
                _logging.Info($"✓ Mode '{mode.Name}' remembered settings applied: {result.AppliedSummary}");
            }

            if (result.Skipped.Count > 0)
            {
                _logging.Info($"ℹ️ Mode '{mode.Name}' remembered settings not available here: {result.SkippedSummary}");
            }

            if (result.Failed.Count > 0)
            {
                _logging.Warn($"⚠️ Mode '{mode.Name}' remembered settings failed: {result.FailedSummary}");
            }

            return result;
        }

        private void ApplyFan(PerformanceMode mode, PerformanceModeEffectResult result)
        {
            if (mode.FanCurve is { Count: > 0 })
            {
                try
                {
                    // ApplyCustomCurve returns void (FanService logs and records its own refusal
                    // reasons), so a throw is the only failure this caller can observe.
                    _fanService.ApplyCustomCurve(mode.FanCurve, immediate: true);
                    result.Applied.Add("remembered fan curve");
                }
                catch (Exception ex)
                {
                    result.Failed.Add($"remembered fan curve ({ex.Message})");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(mode.FanPresetName))
            {
                return;
            }

            var preset = _resolveFanPreset(mode.FanPresetName);
            if (preset == null)
            {
                result.Skipped.Add($"fan preset '{mode.FanPresetName}' (not found)");
                return;
            }

            try
            {
                if (_fanService.ApplyPreset(preset, immediate: true))
                {
                    result.Applied.Add($"fan preset '{preset.Name}'");
                }
                else
                {
                    result.Failed.Add($"fan preset '{preset.Name}'");
                }
            }
            catch (Exception ex)
            {
                result.Failed.Add($"fan preset '{preset.Name}' ({ex.Message})");
            }
        }

        private void ApplyGpuPowerBoost(PerformanceMode mode, PerformanceModeEffectResult result)
        {
            if (string.IsNullOrWhiteSpace(mode.GpuPowerBoostLevel))
            {
                return;
            }

            var level = ValidGpuBoostLevels.FirstOrDefault(l =>
                l.Equals(mode.GpuPowerBoostLevel, StringComparison.OrdinalIgnoreCase));

            if (level == null)
            {
                result.Skipped.Add($"GPU boost '{mode.GpuPowerBoostLevel}' (unknown level)");
                return;
            }

            // The level feeds the fan curve maths on the next evaluation rather than being a
            // standalone hardware write, so report it as a setting that was taken, not a write.
            _fanService.GpuPowerBoostLevel = level;
            result.Applied.Add($"GPU boost '{level}'");
        }

        private void ApplyRefreshRate(PerformanceMode mode, PerformanceModeEffectResult result)
        {
            if (mode.RefreshRateHz is not > 0)
            {
                return;
            }

            var target = mode.RefreshRateHz.Value;
            try
            {
                if (_displayService.SetRefreshRate(target))
                {
                    result.Applied.Add($"{target} Hz");
                }
                else
                {
                    result.Skipped.Add($"{target} Hz (mode not offered by this panel)");
                }
            }
            catch (Exception ex)
            {
                result.Failed.Add($"{target} Hz ({ex.Message})");
            }
        }

        private void ApplyLighting(PerformanceMode mode, PerformanceModeEffectResult result)
        {
            if (string.IsNullOrWhiteSpace(mode.LightingProfileName))
            {
                return;
            }

            if (_applyLightingProfile == null)
            {
                result.Skipped.Add($"lighting '{mode.LightingProfileName}' (lighting not wired)");
                return;
            }

            try
            {
                _applyLightingProfile(mode.LightingProfileName);
                // Lighting apply is asynchronous on the lighting view model, so this records the
                // request rather than a confirmed paint. The lighting page owns the confirmation.
                result.Applied.Add($"lighting '{mode.LightingProfileName}' requested");
            }
            catch (Exception ex)
            {
                result.Failed.Add($"lighting '{mode.LightingProfileName}' ({ex.Message})");
            }
        }

        private void ApplyChargeLimit(PerformanceMode mode, PerformanceModeEffectResult result)
        {
            if (mode.ChargeLimitEnabled is not { } enabled)
            {
                return;
            }

            if (_wmiBios == null || !_wmiBios.IsAvailable)
            {
                result.Skipped.Add("charge limit (HP WMI BIOS unavailable)");
                return;
            }

            try
            {
                if (!_wmiBios.SetBatteryCareMode(enabled))
                {
                    result.Failed.Add("charge limit (WMI command refused)");
                    return;
                }

                var observed = _wmiBios.GetBatteryCareMode();
                if (observed.HasValue && observed.Value != enabled)
                {
                    result.Failed.Add($"charge limit (read back {observed.Value})");
                    return;
                }

                result.Applied.Add(enabled
                    ? mode.ChargeThresholdPercent.HasValue
                        ? $"charge limit {mode.ChargeThresholdPercent}%"
                        : "charge limit on"
                    : "full charge");
            }
            catch (Exception ex)
            {
                result.Failed.Add($"charge limit ({ex.Message})");
            }
        }

        public PerformanceModeEffectSnapshot? CaptureCurrentState()
        {
            var snapshot = new PerformanceModeEffectSnapshot
            {
                FanPresetName = _fanService.ActivePresetName,
                GpuPowerBoostLevel = _fanService.GpuPowerBoostLevel
            };

            try
            {
                var panel = _displayService.ResolveLaptopPanelTarget();
                var rate = _displayService.GetCurrentRefreshRate(panel);
                snapshot.RefreshRateHz = rate > 0 ? rate : null;
            }
            catch (Exception ex)
            {
                _logging.Debug($"Could not read the current refresh rate while remembering mode state: {ex.Message}");
            }

            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                try
                {
                    snapshot.ChargeLimitEnabled = _wmiBios.GetBatteryCareMode();
                }
                catch (Exception ex)
                {
                    _logging.Debug($"Could not read battery care mode while remembering mode state: {ex.Message}");
                }
            }

            snapshot.LightingProfileName = _captureLightingProfileName?.Invoke();
            return snapshot;
        }
    }
}
