using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Roadmap WP-43 (5.0.0): a performance mode can remember the fan preset/curve, GPU Power
    /// Boost level, refresh rate, lighting profile and charge-limit state that were selected while
    /// it was active, so switching modes restores a whole machine state rather than wattages only.
    /// </summary>
    public class PerformanceModeRememberedEffectsTests
    {
        private sealed class NullFanController : IFanController
        {
            public bool IsAvailable => false;
            public string Status => "null";
            public string Backend => "null";
            public bool ApplyPreset(FanPreset preset) => false;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => false;
            public bool SetFanSpeed(int percent) => false;
            public bool SetFanSpeeds(int cpu, int gpu) => false;
            public bool SetMaxFanSpeed(bool enabled) => false;
            public bool SetPerformanceMode(string modeName) => false;
            public bool RestoreAutoControl() => false;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry() };
            public bool ApplyMaxCooling() => true;
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => false;
            public bool ApplyThrottlingMitigation() => false;
            public bool VerifyMaxApplied(out string details) { details = ""; return false; }
            public void Dispose() { }
        }

        private sealed class RecordingEffectApplier : IPerformanceModeEffectApplier
        {
            public int ApplyCount { get; private set; }
            public PerformanceMode? LastMode { get; private set; }
            public bool ThrowOnApply { get; set; }

            public PerformanceModeEffectResult Apply(PerformanceMode mode)
            {
                ApplyCount++;
                LastMode = mode;

                if (ThrowOnApply)
                {
                    throw new InvalidOperationException("fans on fire");
                }

                var result = new PerformanceModeEffectResult();
                if (!string.IsNullOrWhiteSpace(mode.FanPresetName))
                {
                    result.Applied.Add($"fan preset '{mode.FanPresetName}'");
                }
                if (mode.RefreshRateHz is > 0)
                {
                    result.Skipped.Add($"{mode.RefreshRateHz} Hz (not offered)");
                }

                return result;
            }

            public PerformanceModeEffectSnapshot? CaptureCurrentState() => new()
            {
                FanPresetName = "Auto",
                GpuPowerBoostLevel = "Medium",
                RefreshRateHz = 165,
                ChargeLimitEnabled = true,
                ChargeThresholdPercent = 80
            };
        }

        private static PerformanceModeService BuildService(RecordingEffectApplier? applier = null)
        {
            var logging = new LoggingService();
            var service = new PerformanceModeService(
                new NullFanController(),
                new PowerPlanService(logging),
                null,
                logging);

            service.EffectApplier = applier;
            return service;
        }

        private static PerformanceMode ModeWithRememberedSettings() => new()
        {
            Name = "Performance",
            CpuPowerLimitWatts = 0,
            GpuPowerLimitWatts = 0,
            FanPresetName = "Performance",
            GpuPowerBoostLevel = "Maximum",
            RefreshRateHz = 165
        };

        [Fact]
        public void Apply_WithRememberedSettings_InvokesApplierAndRecordsWhatItDid()
        {
            var applier = new RecordingEffectApplier();
            var service = BuildService(applier);

            service.Apply(ModeWithRememberedSettings());

            applier.ApplyCount.Should().Be(1);
            applier.LastMode!.Name.Should().Be("Performance");

            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;
            trace.AppliedRememberedEffects.Should().Contain("fan preset 'Performance'");
            trace.SkippedRememberedEffects.Should().Contain("165 Hz");
            trace.RememberedEffects.Should().Contain("GPU boost 'Maximum'");
        }

        [Fact]
        public void Apply_WithRememberedSettings_ReportsThemAsHardwareEvidence()
        {
            var service = BuildService(new RecordingEffectApplier());

            service.Apply(ModeWithRememberedSettings());

            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;
            trace.ApplicationEvidence.Should().Be(
                "Remembered mode settings applied; firmware power policy was not recorded.",
                "a mode switch that only changed remembered settings must not claim a firmware power policy");
            service.GetApplyTraceReport().Should().Contain("remembered:");
        }

        [Fact]
        public void Apply_WithoutRememberedSettings_DoesNotInvokeApplier()
        {
            var applier = new RecordingEffectApplier();
            var service = BuildService(applier);

            service.Apply(new PerformanceMode
            {
                Name = "Balanced",
                CpuPowerLimitWatts = 0,
                GpuPowerLimitWatts = 0
            });

            applier.ApplyCount.Should().Be(0,
                "modes that remember nothing must behave exactly as they did before 5.0.0");
        }

        [Fact]
        public void Apply_WhenApplierThrows_StillCompletesAndRecordsTheFailure()
        {
            var applier = new RecordingEffectApplier { ThrowOnApply = true };
            var service = BuildService(applier);

            var act = () => service.Apply(ModeWithRememberedSettings());

            act.Should().NotThrow("a failing remembered setting must not abort the whole mode switch");
            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;
            trace.FailedRememberedEffects.Should().Contain("fans on fire");
        }

        [Fact]
        public void ResolveEffectiveMode_KeepsRememberedSettingsWhenTdpIsOverridden()
        {
            var caps = new ModelCapabilities
            {
                ModelName = "Test",
                PerformanceCpuPl1Watts = 90,
                PerformanceGpuTgpWatts = 150
            };
            var logging = new LoggingService();
            var service = new PerformanceModeService(
                new NullFanController(),
                new PowerPlanService(logging),
                null,
                logging,
                modelCapabilities: caps);

            var effective = service.ResolveEffectiveMode(ModeWithRememberedSettings());

            effective.CpuPowerLimitWatts.Should().Be(90);
            effective.FanPresetName.Should().Be("Performance");
            effective.GpuPowerBoostLevel.Should().Be("Maximum");
            effective.RefreshRateHz.Should().Be(165);
        }

        [Fact]
        public void RememberedEffectsSummary_ListsEveryConfiguredSetting()
        {
            var mode = new PerformanceMode
            {
                Name = "Performance",
                FanPresetName = "Performance",
                GpuPowerBoostLevel = "Maximum",
                RefreshRateHz = 165,
                LightingProfileName = "Gaming",
                ChargeLimitEnabled = true,
                ChargeThresholdPercent = 80
            };

            mode.HasRememberedEffects.Should().BeTrue();
            mode.RememberedEffectsSummary.Should().Contain("fan preset 'Performance'");
            mode.RememberedEffectsSummary.Should().Contain("165 Hz");
            mode.RememberedEffectsSummary.Should().Contain("lighting 'Gaming'");
            mode.RememberedEffectsSummary.Should().Contain("charge limit 80%");
        }

        [Fact]
        public void RememberedEffectsSummary_EmptyMode_SaysPowerLimitsOnly()
        {
            var mode = new PerformanceMode { Name = "Balanced", CpuPowerLimitWatts = 45 };

            mode.HasRememberedEffects.Should().BeFalse();
            mode.RememberedEffectsSummary.Should().Be("Nothing yet - power limits only");
        }

        [Fact]
        public void FramelessMode_FanCurveTakesPrecedenceInSummary()
        {
            var mode = new PerformanceMode
            {
                Name = "Performance",
                FanPresetName = "Performance",
                FanCurve = new List<FanCurvePoint>
                {
                    new() { TemperatureC = 60, FanPercent = 50 },
                    new() { TemperatureC = 85, FanPercent = 90 }
                }
            };

            mode.RememberedEffectLabels.Should().ContainSingle()
                .Which.Should().Be("fan curve");
        }

        [Fact]
        public void BuildPerformanceModesForUi_CopiesRememberedSettingsToTheCards()
        {
            var config = new AppConfig
            {
                PerformanceModes = new List<PerformanceMode> { ModeWithRememberedSettings() }
            };

            var uiMode = SystemControlViewModel.BuildPerformanceModesForUi(config).Single();

            uiMode.FanPresetName.Should().Be("Performance");
            uiMode.GpuPowerBoostLevel.Should().Be("Maximum");
            uiMode.RefreshRateHz.Should().Be(165);
            uiMode.Description.Should().NotBeNullOrWhiteSpace("a default description is still filled in");
        }

        [Fact]
        public void CaptureCurrentState_DelegatesToTheApplier()
        {
            var service = BuildService(new RecordingEffectApplier());

            var snapshot = service.CaptureCurrentState();

            snapshot.Should().NotBeNull();
            snapshot!.RefreshRateHz.Should().Be(165);
            snapshot.ChargeLimitEnabled.Should().BeTrue();
        }

        [Fact]
        public void SnapshotApplyTo_WritesSettingsOntoAMode()
        {
            var mode = new PerformanceMode { Name = "Quiet", ChargeThresholdPercent = 70 };
            var snapshot = new PerformanceModeEffectSnapshot
            {
                FanPresetName = "Quiet",
                GpuPowerBoostLevel = "Minimum",
                RefreshRateHz = 60,
                ChargeLimitEnabled = true
            };

            snapshot.ApplyTo(mode);

            mode.FanPresetName.Should().Be("Quiet");
            mode.GpuPowerBoostLevel.Should().Be("Minimum");
            mode.RefreshRateHz.Should().Be(60);
            mode.ChargeLimitEnabled.Should().BeTrue();
            mode.ChargeThresholdPercent.Should().Be(70,
                "a capture that could not read a percentage must keep the mode's own label");
        }

        [Fact]
        public void Apply_WithRememberedSettingsButNoApplier_CompletesAndClaimsNothingApplied()
        {
            var service = BuildService();

            var act = () => service.Apply(ModeWithRememberedSettings());

            act.Should().NotThrow("a missing applier is a wiring state, not a reason to fail a mode switch");
            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;
            trace.RememberedEffects.Should().Contain("fan preset 'Performance'");
            trace.AppliedRememberedEffects.Should().BeEmpty(
                "a mode must not claim settings it did not apply");
            trace.FailedRememberedEffects.Should().BeEmpty();
            service.CaptureCurrentState().Should().BeNull("no applier means nothing can be captured");
        }

        [Fact]
        public void CloneAndCopyRememberedEffects_KeepTheBundleTogether()
        {
            var original = ModeWithRememberedSettings();
            original.LightingProfileName = "Gaming";
            original.ChargeLimitEnabled = true;
            original.ChargeThresholdPercent = 80;

            var clone = original.Clone();
            clone.FanCurve = new List<FanCurvePoint> { new() { TemperatureC = 70, FanPercent = 60 } };

            original.FanCurve.Should().BeNull("a clone must not share the curve list with its source");

            var target = new PerformanceMode { Name = "Quiet" };
            target.CopyRememberedEffectsFrom(clone);

            target.LightingProfileName.Should().Be("Gaming");
            target.ChargeThresholdPercent.Should().Be(80);
            target.FanCurve.Should().NotBeNull();
        }
    }
}
