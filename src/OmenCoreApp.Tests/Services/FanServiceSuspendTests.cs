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
        private static FanService CreateFanService(TrackingFanController controller)
        {
            var logging = new LoggingService();
            logging.Initialize();
            var notificationService = new NotificationService(logging);
            var hwImpl = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwImpl);
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
            public bool ApplyPreset(FanPreset preset) => true;
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
