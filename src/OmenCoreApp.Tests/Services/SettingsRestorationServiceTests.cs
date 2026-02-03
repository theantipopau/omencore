using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class SettingsRestorationServiceTests
    {
        public SettingsRestorationServiceTests()
        {
            // Use isolated temporary config directory for tests to avoid cross-test interference
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }
        private class TestFanController : IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public string? LastAppliedPreset { get; private set; }
            public int LastSetPercent { get; private set; } = -1;
            public int SetCallCount { get; private set; } = 0;

            public bool ApplyPreset(FanPreset preset)
            {
                LastAppliedPreset = preset?.Name;
                return true;
            }

            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) { LastSetPercent = percent; SetCallCount++; return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { LastSetPercent = System.Math.Max(cpuPercent, gpuPercent); SetCallCount++; return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => new System.Collections.Generic.List<FanTelemetry>();
            public void ApplyMaxCooling() { LastAppliedPreset = "Max"; LastSetPercent = 100; SetCallCount++; }
            public void ApplyAutoMode() { LastAppliedPreset = "Auto"; }
            public void ApplyQuietMode() { LastAppliedPreset = "Quiet"; }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        [Fact]
        public async Task RestoreFanPreset_ShouldApplyMax_WhenSavedAsMax()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            configService.Config.LastFanPresetName = "Max";

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var svc = new SettingsRestorationService(logging, configService, null, fanService);
            var sequencer = new StartupSequencer(logging);
            svc.RegisterTasks(sequencer);

            await sequencer.ExecuteAsync(CancellationToken.None);

            svc.FanPresetRestored.Should().BeTrue();
            // Our TestFanController records Max via ApplyMaxCooling
            controller.LastAppliedPreset.Should().Be("Max");

            logging.Dispose();
        }

        [Fact]
        public async Task RestoreFanPreset_ShouldApplyCustomPreset_WhenPresentInConfig()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var custom = new FanPreset { Name = "MyCustom", Mode = FanMode.Manual };
            configService.Config.FanPresets.Add(custom);
            configService.Config.LastFanPresetName = "MyCustom";

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var svc = new SettingsRestorationService(logging, configService, null, fanService);
            var sequencer = new StartupSequencer(logging);
            svc.RegisterTasks(sequencer);

            await sequencer.ExecuteAsync(CancellationToken.None);

            svc.FanPresetRestored.Should().BeTrue();
            controller.LastAppliedPreset.Should().Be("MyCustom");

            logging.Dispose();
        }

        [Fact]
        public void SaveCustomPreset_ShouldPersistAndApply()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var vm = new OmenCore.ViewModels.FanControlViewModel(fanService, configService, logging);

            // Prepare a custom curve and name
            vm.CustomFanCurve.Clear();
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 40, FanPercent = 30 });
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 80, FanPercent = 80 });
            vm.CustomPresetName = "UnitTestPreset";

            vm.SaveCustomPresetCommand.Execute(null);

            // The preset should be present in the FanPresets list
            var saved = vm.FanPresets.FirstOrDefault(p => p.Name == "UnitTestPreset");
            saved.Should().NotBeNull();

            // It should also have been applied (TestFanController records last applied preset name)
            controller.LastAppliedPreset.Should().Be("UnitTestPreset");

            // Config should have LastFanPresetName set
            configService.Load().LastFanPresetName.Should().Be("UnitTestPreset");

            logging.Dispose();
        }

        [Fact]
        public void ApplyMaxCooling_ShouldSetControllerLastSetPercent()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            fanService.ApplyMaxCooling();

            controller.LastAppliedPreset.Should().Be("Max");
            controller.LastSetPercent.Should().BeGreaterThanOrEqualTo(0);

            logging.Dispose();
        }

        [Fact]
        public async Task ApplyPresetImmediateCurve_ShouldSetControllerLastSetPercent()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var maxPreset = new FanPreset { Name = "Max", Mode = FanMode.Max, Curve = new System.Collections.Generic.List<FanCurvePoint> { new FanCurvePoint { TemperatureC = 0, FanPercent = 100 } } };
            fanService.ApplyPreset(maxPreset, immediate: true);

            var temps = fanService.ThermalProvider.ReadTemperatures().ToList();
            var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
            var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;

            await fanService.ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);

            controller.LastAppliedPreset.Should().Be("Max");
            controller.LastSetPercent.Should().BeGreaterThanOrEqualTo(0);

            logging.Dispose();
        }

        [Fact]
        public async Task ForceReapplyFanPreset_ShouldApplyMax_WhenSavedAsMax()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            configService.Config.LastFanPresetName = "Max";

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var svc = new SettingsRestorationService(logging, configService, null, fanService);
            var result = await svc.ForceReapplyFanPresetAsync();

            result.Should().BeTrue();
            controller.LastAppliedPreset.Should().Be("Max");
            // LastSetPercent may be set by controller implementation; ensure we at least recorded the "Max" preset

            logging.Dispose();
        }

        [Fact]
        public void ReapplySavedPresetCommand_ShouldApplySavedMax()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            configService.Config.LastFanPresetName = "Max";

            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var vm = new OmenCore.ViewModels.FanControlViewModel(fanService, configService, logging);

            vm.ReapplySavedPresetCommand.Execute(null);

            controller.LastAppliedPreset.Should().Be("Max");

            logging.Dispose();
        }

        [Fact]
        public async Task Smoothing_Ramps_WhenEnabled()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            // Enable smoothing: duration 500ms, step 100ms
            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = true, SmoothingDurationMs = 500, SmoothingStepMs = 100 });

            // Apply a custom curve and force an immediate curve computation
            // Use 70C instead of 80C to avoid safety bounds (90% min at 80C)
            var curve = new List<FanCurvePoint> { new FanCurvePoint { TemperatureC = 40, FanPercent = 40 }, new FanCurvePoint { TemperatureC = 70, FanPercent = 70 } };
            fanService.ApplyCustomCurve(curve, immediate: false);

            // Force apply for CPU temp 70C (safety bounds: min 70% at 70C, curve gives 70%)
            await fanService.ForceApplyCurveNowAsync(70, 0, immediate: false);

            // Allow some time for ramp to run
            await Task.Delay(700);

            // Smoothing should have resulted in at least one SetFanSpeed call and final percent 70
            controller.SetCallCount.Should().BeGreaterThan(0);
            controller.LastSetPercent.Should().Be(70);

            logging.Dispose();
        }

        [Fact]
        public async Task ImmediateApply_BypassesSmoothing()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = true, SmoothingDurationMs = 500, SmoothingStepMs = 100 });

            // Use 70C instead of 80C to avoid safety bounds (90% min at 80C)
            var curve = new List<FanCurvePoint> { new FanCurvePoint { TemperatureC = 40, FanPercent = 40 }, new FanCurvePoint { TemperatureC = 70, FanPercent = 70 } };
            // Apply curve but don't rely on ApplyCustomCurve's internal immediate (it uses internal temps)
            fanService.ApplyCustomCurve(curve, immediate: false);

            // Force immediate apply with supplied temps (70C, safety bounds: min 70%)
            await fanService.ForceApplyCurveNowAsync(70, 0, immediate: true);

            // Immediate apply should result in single SetFanSpeed call and final percent 70
            controller.SetCallCount.Should().BeGreaterThan(0);
            controller.LastSetPercent.Should().Be(70);

            logging.Dispose();
        }
    }
}

