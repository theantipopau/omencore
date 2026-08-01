using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class SystemControlViewModelTests
    {
        public SystemControlViewModelTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private sealed class FakeUndervoltProvider : ICpuUndervoltProvider
        {
            public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token) => Task.CompletedTask;
            public Task ResetAsync(CancellationToken token) => Task.CompletedTask;
            public Task<UndervoltStatus> ProbeAsync(CancellationToken token) =>
                Task.FromResult(UndervoltStatus.CreateUnknown("test"));
        }

        private sealed class FakeFanController : IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new List<FanTelemetry>();
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        private static SystemControlViewModel CreateViewModel()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var configService = new ConfigurationService();
            var undervoltService = new UndervoltService(new FakeUndervoltProvider(), logging, pollIntervalMs: 60000);
            var powerPlanService = new PowerPlanService(logging);
            var performanceModeService = new PerformanceModeService(new FakeFanController(), powerPlanService, null, logging);
            var cleanupService = new OmenGamingHubCleanupService(logging);
            var restoreService = new SystemRestoreService(logging);
            var gpuSwitchService = new GpuSwitchService(logging);

            return new SystemControlViewModel(
                undervoltService,
                performanceModeService,
                cleanupService,
                restoreService,
                gpuSwitchService,
                logging,
                configService);
        }

        [Fact]
        public void SelectModeByNameNoApplyAndSave_PersistsLastPerformanceModeName()
        {
            var vm = CreateViewModel();

            vm.SelectModeByNameNoApplyAndSave("Performance");

            vm.SelectedPerformanceMode!.Name.Should().Be("Performance");
            new ConfigurationService().Load().LastPerformanceModeName.Should().Be("Performance");
        }

        [Fact]
        public void SelectModeByNameNoApply_DoesNotPersistLastPerformanceModeName()
        {
            var vm = CreateViewModel();
            vm.SelectModeByNameNoApplyAndSave("Performance");

            // A subsequent automatic/temporary sync (e.g. power-source automation) must not
            // overwrite the user's deliberately saved startup preference.
            vm.SelectModeByNameNoApply("Quiet");

            vm.SelectedPerformanceMode!.Name.Should().Be("Quiet");
            new ConfigurationService().Load().LastPerformanceModeName.Should().Be("Performance",
                "SelectModeByNameNoApply is used for automatic/temporary mode changes and must not " +
                "silently overwrite the last deliberately saved startup preference");
        }

        [Fact]
        public void SelectModeByNameNoApplyAndSave_UnknownModeName_DoesNotThrowOrPersist()
        {
            var vm = CreateViewModel();
            var before = new ConfigurationService().Load().LastPerformanceModeName;

            vm.SelectModeByNameNoApplyAndSave("NotARealMode");

            new ConfigurationService().Load().LastPerformanceModeName.Should().Be(before);
        }
    }
}
