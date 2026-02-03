using System;
using System.IO;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using System.Threading.Tasks;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class FanControlViewModelTests
    {
        public FanControlViewModelTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }
        private class TestFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public int LastSetPercent { get; private set; } = -1;
            public int SetCallCount { get; private set; } = 0;

            public bool ApplyPreset(FanPreset preset)
            {
                return true;
            }

            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) { LastSetPercent = percent; SetCallCount++; return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { LastSetPercent = System.Math.Max(cpuPercent, gpuPercent); SetCallCount++; return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => new System.Collections.Generic.List<FanTelemetry>();
            public void ApplyMaxCooling() { LastSetPercent = 100; SetCallCount++; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        [Fact]
        public void SettingTransitionProperties_PersistsToConfig_And_AppliesToService()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var vm = new OmenCore.ViewModels.FanControlViewModel(fanService, configService, logging)
            {
                SmoothingDurationMs = 1500,
                SmoothingStepMs = 100,
                ImmediateApplyOnApply = true
            };

            var loaded = configService.Load();
            loaded.FanTransition.SmoothingDurationMs.Should().Be(1500);
            loaded.FanTransition.SmoothingStepMs.Should().Be(100);
            loaded.FanTransition.ApplyImmediatelyOnUserAction.Should().BeTrue();

            // FanService should reflect the settings
            fanService.SmoothingDurationMs.Should().Be(1500);
            fanService.SmoothingStepMs.Should().Be(100);

            logging.Dispose();
        }
    }
}
