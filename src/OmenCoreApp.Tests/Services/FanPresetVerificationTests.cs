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
            public List<FanPreset> AppliedPresets { get; } = new();

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
            public bool RestoreAutoControl() => true;
            public virtual IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public void ApplyMaxCooling() { }
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
        public void ApplyPreset_VerificationFails_RollsBackToPreviousPreset()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

            // Apply initial preset
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Attempt to apply presetB — controller accepts command but ReadFanSpeeds shows no change => verification fails
            fanService.ApplyPreset(presetB);

            // Should have rolled back to presetA
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Controller should have seen both the attempted preset and the rollback to previous preset
            controller.AppliedPresets.Count.Should().BeGreaterThanOrEqualTo(2);
            controller.AppliedPresets[^1].Name.Should().Be(presetA.Name);

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
    }
}