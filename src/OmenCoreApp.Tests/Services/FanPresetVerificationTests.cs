using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class FanPresetVerificationTests
    {
        public FanPresetVerificationTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class NoEffectController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public bool IsHoldActive { get; set; }
            public List<FanPreset> AppliedPresets { get; } = new();
            public int ApplyMaxCoolingCount { get; private set; }
            public int RestoreAutoControlCount { get; private set; }
            public DateTime? LastMaxModeExternalResetUtc { get; set; }
            public string LastMaxModeExternalResetDetails { get; set; } = "No external Max-mode reset detected.";

            public bool ApplyPreset(FanPreset preset)
            {
                AppliedPresets.Add(preset);
                return true; // pretend controller accepted the command but has no effect
            }

            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl()
            {
                RestoreAutoControlCount++;
                return true;
            }
            public virtual IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public void ApplyMaxCooling() { ApplyMaxCoolingCount++; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public virtual bool VerifyMaxApplied(out string details) { details = "not supported"; return false; }
        }

        private class ReactiveController : NoEffectController
        {
            private int _stage = 0;
            public override IEnumerable<FanTelemetry> ReadFanSpeeds()
            {
                // first call: baseline 1000rpm, subsequent calls: 3500rpm to simulate success
                if (_stage++ == 0)
                    return new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
                return new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 } };
            }

            // When preset.Mode == FanMode.Max, FanService routes through VerifyMaxApplied.
            // The base NoEffectController returns false; override here to simulate success.
            public override bool VerifyMaxApplied(out string details)
            {
                details = "ReactiveController: simulated max verification ok";
                return true;
            }
        }

        [Fact]
        public void ApplyPreset_PerformanceWithoutCurve_SucceedsWithoutRollback()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Performance };

            // Apply initial preset
            fanService.ApplyPreset(presetA).Should().BeTrue();
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Performance-mode preset without a curve should not be rejected just because
            // immediate RPM telemetry did not change.
            fanService.ApplyPreset(presetB).Should().BeTrue();

            fanService.ActivePresetName.Should().Be(presetB.Name);
            fanService.GetCurrentFanMode().Should().Be("Performance");

            logging.Dispose();
        }

        [Fact]
        public void ApplyPreset_UnsupportedNoCurvePreset_RollsBackToPreviousPreset()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "UnknownManualNoCurve", Mode = FanMode.Manual };

            fanService.ApplyPreset(presetA).Should().BeTrue();
            fanService.ApplyPreset(presetB).Should().BeFalse();

            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Controller should have seen both the attempted preset and the rollback to previous preset
            controller.AppliedPresets.Count.Should().BeGreaterThanOrEqualTo(2);
            controller.AppliedPresets[^1].Name.Should().Be(presetA.Name);

            logging.Dispose();
        }

        [Fact]
        public void ApplyPreset_MaxVerificationStale_KeepsMaxRequestedInsteadOfRollingBack()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Max", Mode = FanMode.Max };

            fanService.ApplyPreset(presetA).Should().BeTrue();
            fanService.ApplyPreset(presetB).Should().BeTrue("Max readback can lag behind successful SetFanMax on some firmware");

            fanService.ActivePresetName.Should().Be(presetB.Name);
            fanService.GetCurrentFanMode().Should().Be("Max");
            controller.ApplyMaxCoolingCount.Should().Be(1);

            logging.Dispose();
        }

        [Fact]
        public void ApplyPreset_AutoWithCurvePayload_DoesNotRestoreBiosDefaults()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var preset = new FanPreset
            {
                Name = "Auto",
                Mode = FanMode.Auto,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 45, FanPercent = 30 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 70 }
                }
            };

            fanService.ApplyPreset(preset).Should().BeTrue();

            controller.AppliedPresets.Should().ContainSingle(p => p.Name == "Auto");
            controller.RestoreAutoControlCount.Should().Be(0,
                "Auto presets with explicit curve payloads are already applied by the controller and must not drop the active thermal policy back to BIOS defaults");
            fanService.ActivePresetName.Should().Be("Auto");
            fanService.GetCurrentFanMode().Should().Be("Auto");
            fanService.IsCurveActive.Should().BeTrue(
                "explicit Auto curve payloads need a clear fan owner so fans can ramp down as the curve target drops");

            logging.Dispose();
        }

        [Fact]
        public void ApplyPreset_VerificationSucceeds_LeavesNewPresetActive()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new ReactiveController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

            // Apply initial preset
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Apply presetB — controller will show reactive change on read => verification succeeds
            fanService.ApplyPreset(presetB);

            // New preset should remain active
            fanService.ActivePresetName.Should().Be(presetB.Name);

            logging.Dispose();
        }

        [Fact]
        public void CommandHistory_RecordsPresetApplyAndDiagnosticReport()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new ReactiveController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            controller.LastMaxModeExternalResetUtc = new DateTime(2026, 5, 1, 8, 9, 54, DateTimeKind.Utc);
            controller.LastMaxModeExternalResetDetails = "Max telemetry dropped while Max mode was active; firmware reset suspected.";

            var preset = new FanPreset { Name = "Max", Mode = FanMode.Max };

            fanService.ApplyPreset(preset).Should().BeTrue();

            var history = fanService.GetCommandHistorySnapshot();
            history.Should().Contain(entry => entry.Command == "ApplyPreset.Controller" && entry.Target == "Max" && entry.Success);
            history.Should().Contain(entry => entry.Command == "ApplyPreset.Verify" && entry.Target == "Max" && entry.Success);
            history.Should().Contain(entry => entry.Command == "ApplyMaxCooling" && entry.Target == "Max" && entry.Success);

            var report = fanService.GetFanCommandHistoryReport();
            report.Should().Contain("Fan Command History");
            report.Should().Contain("ApplyPreset.Controller");
            report.Should().Contain("backend=Test");
            report.Should().Contain("Last Max external reset: 2026-05-01T08:09:54.0000000Z");
            report.Should().Contain("firmware reset suspected");

            logging.Dispose();
        }

        [Fact]
        public void CommandHistory_IsBoundedToMostRecentEntries()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new ReactiveController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            for (var i = 0; i < 95; i++)
            {
                fanService.ForceSetFanSpeed(i % 101);
            }

            var history = fanService.GetCommandHistorySnapshot();

            history.Should().HaveCount(80);
            history.First().Target.Should().Be("15%");
            history.Last().Target.Should().Be("94%");
            history.Should().OnlyContain(entry => entry.Command == "ForceSetFanSpeed");

            logging.Dispose();
        }

        [Fact]
        public void IsCurveOrHoldActive_True_WhenControllerHoldIsActiveWithoutCurve()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController { IsHoldActive = true };
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            fanService.IsCurveActive.Should().BeFalse();
            fanService.IsHoldActive.Should().BeTrue();
            fanService.IsCurveOrHoldActive.Should().BeTrue();

            logging.Dispose();
        }

        [Fact]
        public void IsCurveOrHoldActive_False_WhenNoCurveAndNoHold()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController { IsHoldActive = false };
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            fanService.IsCurveActive.Should().BeFalse();
            fanService.IsHoldActive.Should().BeFalse();
            fanService.IsCurveOrHoldActive.Should().BeFalse();

            logging.Dispose();
        }

        [Fact]
        public void CommandHistory_RecordsHoldStateTransition_WhenHoldFlagChangesBetweenCommands()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController { IsHoldActive = false };
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            fanService.ForceSetFanSpeed(40);
            controller.IsHoldActive = true;
            fanService.ForceSetFanSpeed(41);

            var history = fanService.GetCommandHistorySnapshot();
            history.Should().Contain(entry => entry.Command == "HoldStateTransition" && entry.Target == "Active");

            var report = fanService.GetFanCommandHistoryReport();
            report.Should().Contain("HoldStateTransition");
            report.Should().Contain("curveOrHold=");

            logging.Dispose();
        }

        [Fact]
        public void FanActivityStateChanged_Fires_WhenCustomCurveStartsAndStops()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController { IsHoldActive = false };
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            var states = new List<bool>();
            fanService.FanActivityStateChanged += (_, active) => states.Add(active);

            fanService.ApplyCustomCurve(new[]
            {
                new FanCurvePoint { TemperatureC = 30, FanPercent = 30 },
                new FanCurvePoint { TemperatureC = 80, FanPercent = 70 }
            });
            fanService.DisableCurve();

            states.Should().ContainInOrder(true, false);
            logging.Dispose();
        }

        [Fact]
        public void FanActivityStateChanged_Fires_WhenBackendHoldChangesBetweenCommands()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController { IsHoldActive = false };
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            var states = new List<bool>();
            fanService.FanActivityStateChanged += (_, active) => states.Add(active);

            fanService.ForceSetFanSpeed(40);
            controller.IsHoldActive = true;
            fanService.ForceSetFanSpeed(41);
            controller.IsHoldActive = false;
            fanService.ForceSetFanSpeed(42);

            states.Should().ContainInOrder(false, true, false);
            logging.Dispose();
        }
    }

    // ---------------------------------------------------------------------------
    // RPM sanity-check regression tests (GitHub #106)
    // ---------------------------------------------------------------------------
    [Collection("Config Isolation")]
    public class RpmSanityCheckTests
    {
        public RpmSanityCheckTests()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private static FanService MakeFanService(out LoggingService logging)
        {
            logging = new LoggingService();
            logging.Initialize();

            var controller = new StubFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            return new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
        }

        private class StubFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Stub";
            public string Backend => "Stub";
            public bool IsHoldActive => false;
            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => Enumerable.Empty<FanTelemetry>();
            public void ApplyMaxCooling() { }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        [Fact]
        public void CheckRpmSanity_DoesNotRaiseEvent_WhenDutyIsZero()
        {
            var fanService = MakeFanService(out var logging);
            bool eventRaised = false;
            fanService.RpmSanityCheckWarning += (_, __) => eventRaised = true;

            // Call many times with zero duty — should never raise
            for (int i = 0; i < 100; i++)
                fanService.CheckRpmSanity(dutyPercent: 0, rpmReading: 0);

            eventRaised.Should().BeFalse("duty=0 means fans are commanded off; zero RPM is expected");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_DoesNotRaiseEvent_WhenRpmIsNonZero()
        {
            var fanService = MakeFanService(out var logging);
            bool eventRaised = false;
            fanService.RpmSanityCheckWarning += (_, __) => eventRaised = true;

            // Active duty with healthy RPM — should never raise
            for (int i = 0; i < 100; i++)
                fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 2500);

            eventRaised.Should().BeFalse("RPM > 0 with duty > 0 is healthy state");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_DoesNotRaiseEvent_BeforeThresholdElapsed()
        {
            var fanService = MakeFanService(out var logging);
            bool eventRaised = false;
            fanService.RpmSanityCheckWarning += (_, __) => eventRaised = true;

            // First call starts the timer
            fanService.CheckRpmSanity(dutyPercent: 50, rpmReading: 0);

            // Immediately check again — not enough time has passed
            fanService.CheckRpmSanity(dutyPercent: 50, rpmReading: 0);

            eventRaised.Should().BeFalse("warning should not fire before 30-second threshold has elapsed");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_RaisesEvent_After30Seconds_Of_ZeroRpmWithActiveDuty()
        {
            var fanService = MakeFanService(out var logging);
            OmenCore.Services.RpmSanityCheckEventArgs? capturedArgs = null;
            fanService.RpmSanityCheckWarning += (_, args) => capturedArgs = args;

            // Start the zero-RPM timer
            fanService.CheckRpmSanity(dutyPercent: 75, rpmReading: 0);

            // Backdate the timer start to simulate 35 seconds having passed
            var field = typeof(FanService).GetField("_zeroRpmWithDutyStartTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull("_zeroRpmWithDutyStartTime field must exist for time simulation");
            field!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-35));

            // Check again — threshold should be exceeded now
            fanService.CheckRpmSanity(dutyPercent: 75, rpmReading: 0);

            capturedArgs.Should().NotBeNull("warning event must fire after 30-second threshold");
            capturedArgs!.DutyPercent.Should().Be(75);
            capturedArgs.RpmReading.Should().Be(0);
            capturedArgs.DurationAtZero.TotalSeconds.Should().BeGreaterThanOrEqualTo(30);
            capturedArgs.Message.Should().Contain("duty cycle at 75%");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_RaisesEvent_OnlyOnce_EvenWithRepeatedCalls()
        {
            var fanService = MakeFanService(out var logging);
            int eventCount = 0;
            fanService.RpmSanityCheckWarning += (_, __) => eventCount++;

            // Start timer and backdate
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);
            var field = typeof(FanService).GetField("_zeroRpmWithDutyStartTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-40));

            // Multiple calls should still only raise the event once
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);

            eventCount.Should().Be(1, "warning should fire exactly once per zero-RPM episode");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_ClearsWarning_WhenRpmRecovers()
        {
            var fanService = MakeFanService(out var logging);
            bool eventRaised = false;
            fanService.RpmSanityCheckWarning += (_, __) => eventRaised = true;

            // Start and expire the timer
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);
            var field = typeof(FanService).GetField("_zeroRpmWithDutyStartTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-35));
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 0);
            eventRaised.Should().BeTrue();

            // Simulate RPM recovery — clear the warning flag
            fanService.DismissRpmSanityWarning();

            // Re-introduce healthy RPM: subsequent checks should stay silent
            int eventCount = 0;
            fanService.RpmSanityCheckWarning += (_, __) => eventCount++;
            fanService.CheckRpmSanity(dutyPercent: 60, rpmReading: 3200);

            eventCount.Should().Be(0, "warning should not re-fire after RPM has recovered");
            logging.Dispose();
        }

        [Fact]
        public void CheckRpmSanity_RaisesEvent_After30Seconds_Of_HighRpmWithZeroDuty()
        {
            var fanService = MakeFanService(out var logging);
            OmenCore.Services.RpmSanityCheckEventArgs? capturedArgs = null;
            fanService.RpmSanityCheckWarning += (_, args) => capturedArgs = args;

            fanService.CheckRpmSanity(dutyPercent: 0, rpmReading: 3200);

            var field = typeof(FanService).GetField("_highRpmWithZeroDutyStartTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull("_highRpmWithZeroDutyStartTime field must exist for time simulation");
            field!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-35));

            fanService.CheckRpmSanity(dutyPercent: 0, rpmReading: 3200);

            capturedArgs.Should().NotBeNull("warning event must fire after 30-second threshold for high RPM with 0% duty");
            capturedArgs!.DutyPercent.Should().Be(0);
            capturedArgs.RpmReading.Should().Be(3200);
            capturedArgs.Message.Should().Contain("requested duty is 0%");
            logging.Dispose();
        }
    }
}
