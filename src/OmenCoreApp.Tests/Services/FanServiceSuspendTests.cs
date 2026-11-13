// GitHub #146 — fans observed stuck at max through lid-close, followed by a BIOS
// thermal shutdown while the laptop sat in a closed bag.
//
// Root cause: the Max-mode keepalive/reassertion timer lives inside the fan
// controller backend on its own independent schedule, with no suspend awareness
// of its own. FanService.HandleSystemSuspend() previously only stopped that timer
// as a side effect of RestoreAutoControlSerialized() succeeding — if that call
// threw, or its underlying WMI write failed, or fan writes were unavailable at
// all, the timer was never told to stop and could keep reasserting Max fan mode
// for as long as the process had threads running during the suspend transition.
//
// These tests assert the timer is stopped unconditionally, in every one of those
// failure paths, without requiring physical OMEN hardware.

using System;
using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class FanServiceSuspendTests
    {
        // Leaving Max via Quiet/Auto must update the preset that resume reapplies, otherwise
        // sleep/wake brings full-speed fans back (credit: bobshmo's fork found this).
        [Theory]
        [InlineData(true, "Quiet")]
        [InlineData(false, "Auto")]
        public void LeavingMax_ResumeReappliesTheNewMode(bool quiet, string expected)
        {
            var controller = new TrackingFanController();
            using var service = CreateFanService(controller);
            service.ApplyPreset(new FanPreset { Name = "Max", Mode = FanMode.Max });
            if (quiet) service.ApplyQuietMode(); else service.ApplyAutoMode();
            service.ActivePresetName.Should().Be(expected);
            controller.AppliedPresetNames.Clear();

            service.HandleSystemSuspend();
            service.HandleSystemResume();

            controller.AppliedPresetNames.Should().ContainSingle().Which.Should().Be(expected);
        }

        [Fact]
        public void MaxAutoReturn_Timer_HandsFansBackToAuto_AfterTheChosenTime()
        {
            var controller = new TrackingFanController();
            using var service = CreateFanService(controller);
            service.MaxAutoReturnOptions = () => (MaxAutoReturnMode.Timer, 15);
            service.ApplyPreset(new FanPreset { Name = "Max", Mode = FanMode.Max });
            var t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

            service.EvaluateMaxAutoReturn(80, 70, t0);
            service.EvaluateMaxAutoReturn(80, 70, t0.AddMinutes(14));
            service.ActivePresetName.Should().Be("Max", "the timer has not run out");

            service.EvaluateMaxAutoReturn(80, 70, t0.AddMinutes(15));
            service.ActivePresetName.Should().Be("Auto");
        }

        [Fact]
        public void MaxAutoReturn_Off_NeverLeavesMax()
        {
            var controller = new TrackingFanController();
            using var service = CreateFanService(controller);
            service.MaxAutoReturnOptions = () => (MaxAutoReturnMode.Off, 15);
            service.ApplyPreset(new FanPreset { Name = "Max", Mode = FanMode.Max });
            var t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

            service.EvaluateMaxAutoReturn(40, 40, t0);
            service.EvaluateMaxAutoReturn(40, 40, t0.AddHours(3));

            service.ActivePresetName.Should().Be("Max");
        }

        [Fact]
        public void MaxAutoReturn_Cooldown_DoesNothingWhileAnotherPresetIsActive()
        {
            var controller = new TrackingFanController();
            using var service = CreateFanService(controller);
            service.MaxAutoReturnOptions = () => (MaxAutoReturnMode.Cooldown, 15);
            service.ApplyPreset(new FanPreset { Name = "Quiet", Mode = FanMode.Quiet });
            var t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

            service.EvaluateMaxAutoReturn(40, 40, t0);
            service.EvaluateMaxAutoReturn(40, 40, t0.AddMinutes(10));

            service.ActivePresetName.Should().Be("Quiet");
        }

        // GitHub #189: the factory curve must only ever drive fans that are on firmware Auto.
        private static (AutomaticFanCurveController Curve, List<(int, int)> Writes) AttachCurve(FanService service, Func<bool> wanted)
        {
            var writes = new List<(int, int)>();
            var curve = new AutomaticFanCurveController(
                FactoryFanCurve.Performance8D87(), FanMappingTable.Captured8D87(), 60,
                (c, g) => { writes.Add((c, g)); return true; }, () => { }, () => null);
            service.SetAutomaticFanCurve(curve, wanted);
            return (curve, writes);
        }

        [Fact]
        public void AutomaticCurve_DrivesTheFans_OnFirmwareAuto()
        {
            using var service = CreateFanService(new TrackingFanController());
            var (curve, writes) = AttachCurve(service, () => true);

            service.RunAutomaticFanCurve(85, 50);

            writes.Should().Equal((79, 82));
            curve.Engaged.Should().BeTrue();
        }

        [Fact]
        public void AutomaticCurve_StandsDown_WhenAUserPresetIsActive()
        {
            using var service = CreateFanService(new TrackingFanController());
            service.ApplyPreset(new FanPreset { Name = "Max", Mode = FanMode.Max });
            var (curve, writes) = AttachCurve(service, () => true);

            service.RunAutomaticFanCurve(85, 50);

            writes.Should().BeEmpty("Max was chosen by the user");
            curve.Engaged.Should().BeFalse();
        }

        [Fact]
        public void AutomaticCurve_StandsDown_DuringFanDiagnostics()
        {
            using var service = CreateFanService(new TrackingFanController());
            service.EnterDiagnosticMode();
            try
            {
                var (_, writes) = AttachCurve(service, () => true);
                service.RunAutomaticFanCurve(85, 50);
                writes.Should().BeEmpty();
            }
            finally { service.ExitDiagnosticMode(); }
        }

        [Fact]
        public void FanWriteLease_StaysHeldUntilTheLastOwnerReleases()
        {
            using var service = CreateFanService(new TrackingFanController());
            var first = service.AcquireFanWriteLease("guided run");
            var second = service.AcquireFanWriteLease("single-fan test");

            first.Dispose();
            service.IsDiagnosticModeActive.Should().BeTrue();
            service.FanWriteLeaseOwners.Should().Equal("single-fan test");
            FanService.IsAnyDiagnosticModeActive.Should().BeTrue();

            second.Dispose();
            service.IsDiagnosticModeActive.Should().BeFalse();
        }

        [Fact]
        public void FanWriteLease_DisposeTwiceDoesNotReleaseAnotherOwner()
        {
            using var service = CreateFanService(new TrackingFanController());
            var first = service.AcquireFanWriteLease("a");
            using var second = service.AcquireFanWriteLease("b");

            first.Dispose();
            first.Dispose();

            service.IsDiagnosticModeActive.Should().BeTrue();
        }

        [Fact]
        public void LegacyEnterExit_StillWorks_AndExitWithoutEnterIsANoOp()
        {
            using var service = CreateFanService(new TrackingFanController());
            service.ExitDiagnosticMode();
            service.IsDiagnosticModeActive.Should().BeFalse();

            service.EnterDiagnosticMode();
            service.EnterDiagnosticMode();
            service.IsDiagnosticModeActive.Should().BeTrue();
            service.ExitDiagnosticMode();
            service.IsDiagnosticModeActive.Should().BeFalse();
        }

        [Fact]
        public void AutomaticCurve_IsInert_WhenTheUserHasNotSwitchedItOn()
        {
            using var service = CreateFanService(new TrackingFanController());
            var (curve, writes) = AttachCurve(service, () => false);

            service.RunAutomaticFanCurve(95, 80);

            writes.Should().BeEmpty();
            curve.Engaged.Should().BeFalse();
        }

        [Fact]
        public void AutomaticCurve_HandsTheFansBack_WhenSwitchedOff()
        {
            using var service = CreateFanService(new TrackingFanController());
            var on = true;
            var (curve, _) = AttachCurve(service, () => on);
            service.RunAutomaticFanCurve(85, 50);
            curve.Engaged.Should().BeTrue();

            on = false;
            service.RunAutomaticFanCurve(85, 50);

            curve.Engaged.Should().BeFalse();
        }

        private static FanService CreateFanService(TrackingFanController controller)
        {
            var logging = new LoggingService();
            logging.Initialize();
            var notificationService = new NotificationService(logging);
            var thermalProvider = OmenCoreApp.Tests.TestThermals.NoHardware();
            return new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
        }

        [Fact]
        public void HandleSystemSuspend_StopsCountdownExtension_WhenRestoreAutoControlSucceeds()
        {
            var controller = new TrackingFanController { RestoreAutoControlResult = true };
            var svc = CreateFanService(controller);

            svc.HandleSystemSuspend();

            controller.StopCountdownExtensionCallCount.Should().Be(1,
                "the keepalive timer must always be stopped on suspend, regardless of restore outcome");
        }

        [Fact]
        public void HandleSystemSuspend_StopsCountdownExtension_WhenRestoreAutoControlReturnsFalse()
        {
            // This is the gap from #146: a non-exceptional WMI write failure during the
            // suspend transition must not leave the Max-mode reassertion timer running.
            var controller = new TrackingFanController { RestoreAutoControlResult = false };
            var svc = CreateFanService(controller);

            svc.HandleSystemSuspend();

            controller.StopCountdownExtensionCallCount.Should().Be(1,
                "a failed (but non-throwing) auto-control restore must not leave the keepalive timer running");
        }

        [Fact]
        public void HandleSystemSuspend_StopsCountdownExtension_WhenRestoreAutoControlThrows()
        {
            // The most severe gap from #146: an exception inside the restore path must not
            // skip stopping the timer, since the original code stopped it only as a side
            // effect reached deep inside a successful RestoreAutoControl() call.
            var controller = new TrackingFanController { RestoreAutoControlShouldThrow = true };
            var svc = CreateFanService(controller);

            svc.HandleSystemSuspend();

            controller.StopCountdownExtensionCallCount.Should().Be(1,
                "an exception while restoring BIOS auto control must not prevent the keepalive timer from being stopped");
        }

        [Fact]
        public void HandleSystemSuspend_StopsCountdownExtension_WhenFanWritesUnavailable()
        {
            // FanWritesAvailable gates the restore-auto-control attempt itself; the timer
            // must still be stopped even when that gate gives up before calling the
            // controller at all.
            var controller = new TrackingFanController { IsAvailableOverride = false };
            var svc = CreateFanService(controller);

            svc.HandleSystemSuspend();

            controller.StopCountdownExtensionCallCount.Should().Be(1,
                "the keepalive timer must be stopped even when fan writes are unavailable entirely");
        }

        [Fact]
        public void HandleSystemSuspend_DoesNotThrow_WhenStopCountdownExtensionItselfThrows()
        {
            var controller = new TrackingFanController { StopCountdownExtensionShouldThrow = true };
            var svc = CreateFanService(controller);

            var act = () => svc.HandleSystemSuspend();

            act.Should().NotThrow("a failure stopping the keepalive timer must be caught and logged, not bubble up to the suspend-event dispatcher");
        }

        // ─── Tracking stub controller ─────────────────────────────────────────

        private sealed class TrackingFanController : IFanController
        {
            public bool RestoreAutoControlResult { get; set; } = true;
            public bool RestoreAutoControlShouldThrow { get; set; }
            public bool StopCountdownExtensionShouldThrow { get; set; }
            public bool IsAvailableOverride { get; set; } = true;
            public int StopCountdownExtensionCallCount { get; private set; }

            public bool IsAvailable => IsAvailableOverride;
            public string Status => "Test";
            public string Backend => "Test";
            public List<string> AppliedPresetNames { get; } = new();
            public bool ApplyPreset(FanPreset preset) { AppliedPresetNames.Add(preset.Name); return true; }
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;

            public bool RestoreAutoControl()
            {
                if (RestoreAutoControlShouldThrow)
                {
                    throw new InvalidOperationException("Simulated WMI failure during suspend transition");
                }

                return RestoreAutoControlResult;
            }

            public void StopCountdownExtension()
            {
                StopCountdownExtensionCallCount++;
                if (StopCountdownExtensionShouldThrow)
                {
                    throw new InvalidOperationException("Simulated failure stopping countdown timer");
                }
            }

            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = "stub"; return false; }
        }
    }
}
