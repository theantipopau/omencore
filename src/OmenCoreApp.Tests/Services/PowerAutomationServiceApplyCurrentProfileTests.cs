// GitHub Discord/BUG-3820-005 — PowerAutomationService.ApplyCurrentProfile() existed
// ("useful for initial setup") but had no caller anywhere in the codebase, so a user
// with Power Automation enabled kept whatever fan/performance state was last manually
// set until the next AC<->battery transition, regardless of their configured profile.
// MainViewModel.RestoreSettingsOnStartupAsync() now calls it once at startup. That call
// site is impractical to unit test directly (MainViewModel has a very large constructor
// dependency graph), so this instead pins down the behavior that call site relies on:
// ApplyCurrentProfile() must be a no-op when Power Automation is disabled, and must
// apply the configured profile for the current power source when enabled.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class PowerAutomationServiceApplyCurrentProfileTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<IDisposable> _disposables = new();

        public PowerAutomationServiceApplyCurrentProfileTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            // FanService starts a background MonitorLoop on construction; leaving it
            // undisposed keeps polling on a background thread after the test (and this
            // class's temp config dir) is gone, which previously crashed the test host
            // with an unhandled exception from a much later, unrelated test run.
            foreach (var disposable in _disposables)
            {
                try { disposable.Dispose(); } catch { /* best effort cleanup */ }
            }

            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }

        private sealed class RecordingFanController : IFanController
        {
            public List<FanPreset> AppliedPresets { get; } = new();

            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public bool ApplyPreset(FanPreset preset)
            {
                AppliedPresets.Add(preset);
                return true;
            }
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = "stub"; return false; }
        }

        private (PowerAutomationService service, RecordingFanController controller, ConfigurationService configService, PerformanceModeService performanceModeService) CreateService(bool enabled)
        {
            var logging = new LoggingService();
            logging.Initialize();
            _disposables.Add(logging);

            var configService = new ConfigurationService();
            var config = configService.Load();
            config.PowerAutomation ??= new PowerAutomationSettings();
            config.PowerAutomation.Enabled = enabled;
            config.PowerAutomation.AcFanPreset = "Performance";
            config.PowerAutomation.AcPerformanceMode = "Performance";
            config.PowerAutomation.BatteryFanPreset = "Quiet";
            config.PowerAutomation.BatteryPerformanceMode = "Silent";
            configService.Save(config);

            var controller = new RecordingFanController();
            var powerPlanService = new PowerPlanService(logging);
            var performanceModeService = new PerformanceModeService(controller, powerPlanService, null, logging);

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            _disposables.Add(fanService);

            var service = new PowerAutomationService(logging, fanService, performanceModeService, configService);
            _disposables.Add(service);
            return (service, controller, configService, performanceModeService);
        }

        [Fact]
        public void ApplyCurrentProfile_NoOp_WhenPowerAutomationDisabled()
        {
            var (service, controller, _, _) = CreateService(enabled: false);

            service.ApplyCurrentProfile();

            controller.AppliedPresets.Should().BeEmpty(
                "ApplyCurrentProfile must do nothing when Power Automation is disabled in config");
        }

        [Fact]
        public void ApplyCurrentProfile_AppliesConfiguredFanPreset_ForCurrentPowerSource_WhenEnabled()
        {
            var (service, controller, _, _) = CreateService(enabled: true);

            service.ApplyCurrentProfile();

            var expectedPresetName = service.IsOnAcPower ? "Performance" : "Quiet";
            controller.AppliedPresets.Should().ContainSingle(
                p => p.Name == expectedPresetName,
                "a successful startup apply must use the fan preset configured for whichever power source is currently active");
        }

        // GitHub #145 item 2 ("Silent" battery profile applies as "Custom"/"Balanced" instead) —
        // root-caused this cycle: ApplyPowerProfile used to build a bare
        // `new PerformanceMode { Name = perfMode }` and call Apply() directly, which carries 0W
        // CPU / 0W GPU. Apply()'s "both limits non-positive" guard then skips the EC power-limit
        // step outright on any board without a per-model wattage override (57 of 59 in the
        // database) - so this feature's performance-mode step silently did nothing but leave a
        // mode name behind, never actually lowering CPU/GPU power on battery. Fixed by routing
        // through SetPerformanceMode(string), the same alias-normalizing, real-wattage entry
        // point manual UI mode selection already uses.
        [Fact]
        public void ApplyCurrentProfile_AppliesRealWattage_NotBareZeroWattObject()
        {
            var (service, _, _, performanceModeService) = CreateService(enabled: true);

            service.ApplyCurrentProfile();

            var applied = performanceModeService.CurrentMode;
            applied.Should().NotBeNull("Power Automation must actually apply a performance mode on startup sync");
            applied!.CpuPowerLimitWatts.Should().BeGreaterThan(0,
                "a bare Name-only PerformanceMode object (the pre-fix bug) carries 0W and gets silently skipped by the EC apply guard");
            applied.GpuPowerLimitWatts.Should().BeGreaterThan(0,
                "GPU wattage must also be real, not the 0W default from a bare Name-only object");

            var expectedName = service.IsOnAcPower ? "Performance" : "Quiet";
            applied.Name.Should().Be(expectedName,
                "SetPerformanceMode must normalize the configured alias (e.g. 'Silent') to its canonical mode name");
        }

        // GitHub #177 ("custom fan curve not restored on restart") - root cause traced this cycle:
        // ApplyCurrentProfile() force-applied unconditionally on every startup whenever automation
        // was enabled, silently overriding a user's last manual fan/performance selection even when
        // the power source never actually changed between sessions. Fixed via
        // TransitionOccurredSincePriorSession, which compares the freshly-detected AC state against
        // what PowerAutomationService persisted at the end of the prior session.
        [Fact]
        public void ApplyCurrentProfile_SkipsApply_WhenPowerSourceUnchangedSincePriorSession()
        {
            // "Session" 1: no prior persisted baseline exists yet, so this is treated the same as
            // the always-apply behavior before this fix - it applies once and persists the
            // currently-detected (real) AC/Battery state as the new baseline for next time.
            var (service1, controller1, _, _) = CreateService(enabled: true);
            service1.ApplyCurrentProfile();
            controller1.AppliedPresets.Should().NotBeEmpty(
                "a first-ever session has no persisted baseline to compare against, so it should still apply once");

            // "Session" 2: same config dir, same process - GetSystemPowerStatus can't have changed
            // between the two constructions above, so the baseline session 1 just persisted should
            // exactly match session 2's freshly-detected state, meaning no real transition happened.
            var (service2, controller2, _, _) = CreateService(enabled: true);
            service2.TransitionOccurredSincePriorSession.Should().BeFalse(
                "the power source did not actually change between the two sessions in this test");

            service2.ApplyCurrentProfile();
            controller2.AppliedPresets.Should().BeEmpty(
                "a startup with no real power-source transition must not override the user's last manual selection");
        }
    }
}
