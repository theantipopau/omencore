using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class MainViewModelTests : IDisposable
    {
        private readonly string _tempDir;

        public MainViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
        }

        private class FakeTelemetry : ITelemetryService
        {
            public bool Called { get; private set; }
            public string? ExportTelemetry()
            {
                Called = true;
                // create dummy file
                var tmp = Path.Combine(Path.GetTempPath(), "telemetry_test.json");
                File.WriteAllText(tmp, "{}\n");
                return tmp;
            }
        }

        [Fact]
        public async Task ExportTelemetryCommand_InvokesService_AndLogs()
        {
            // nothing throws during viewmodel construction, so just build one
            using var vm = new MainViewModel();
            var fake = new FakeTelemetry();
            // replace private field via reflection
            var field = typeof(MainViewModel).GetField("_telemetryService", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(vm, fake);

            vm.ExportTelemetryCommand.Execute(null);
            fake.Called.Should().BeTrue();
        }

        [Fact]
        public void Dashboard_DoesNotForceSystemControlLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsSystemControlLoaded.Should().BeFalse();

            _ = vm.Dashboard;

            vm.IsSystemControlLoaded.Should().BeFalse(
                because: "the dashboard/sidebar summary can use lightweight MainViewModel state at startup");
        }

        [Fact]
        public void General_DoesNotForceSystemControlLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsSystemControlLoaded.Should().BeFalse();

            _ = vm.General;

            vm.IsSystemControlLoaded.Should().BeFalse(
                because: "the General tab should not initialize tuning/GPU-power providers before the OMEN/Tuning paths need them");
        }

        [Fact]
        public void General_DoesNotForceFanControlLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsFanControlLoaded.Should().BeFalse();

            _ = vm.General;

            vm.IsFanControlLoaded.Should().BeFalse(
                because: "the General tab can apply profiles through FanService without constructing the advanced FanControl view-model");
        }

        [Fact]
        public void Constructor_DoesNotForceLightingLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsLightingLoaded.Should().BeFalse(
                because: "RGB/peripheral SDK and provider setup should wait for the RGB page or an explicit lighting action");
        }

        [Fact]
        public void Constructor_DoesNotStartConflictMonitoringScan()
        {
            using var vm = new MainViewModel();

            var field = typeof(MainViewModel).GetField("_conflictMonitoringStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.GetValue(vm).Should().Be(0,
                because: "conflict detection scans should be deferred until Monitoring/OMEN/Tuning/Optimizer is opened");
        }

        [Fact]
        public void SelectingTuningTab_StartsConflictMonitoringScan()
        {
            using var vm = new MainViewModel();

            vm.SelectedTabIndex = 2;

            var field = typeof(MainViewModel).GetField("_conflictMonitoringStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.GetValue(vm).Should().Be(1,
                because: "tuning-related conflict scans should start when the user opens a relevant tab, not at app startup");
        }

        [Fact]
        public void SelectingOmenTab_StartsConflictMonitoringScan()
        {
            using var vm = new MainViewModel();

            vm.SelectedTabIndex = 1;

            var field = typeof(MainViewModel).GetField("_conflictMonitoringStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.GetValue(vm).Should().Be(1,
                because: "OMEN controls can conflict with external tuning tools and should start detection on first use");
        }

        [Fact]
        public void SelectingDiagnosticsTab_DoesNotStartConflictMonitoringScan()
        {
            using var vm = new MainViewModel();

            vm.SelectedTabIndex = 3;

            var field = typeof(MainViewModel).GetField("_conflictMonitoringStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.GetValue(vm).Should().Be(0,
                because: "the Diagnostics tab should not wake the tuning-conflict monitor unless a tuning surface is opened");
        }

        [Fact]
        public void MemoryOptimizerRefreshTimer_PausesWhenLeavingMemoryTab()
        {
            const string timerName = "MemoryOptimizerRefresh";
            BackgroundTimerRegistry.Unregister(timerName);

            using var vm = new MainViewModel();

            try
            {
                vm.SelectedTabIndex = 6;
                _ = vm.MemoryOptimizer;

                BackgroundTimerRegistry.GetAll().Should().Contain(t => t.Name == timerName,
                    because: "the Memory tab needs live process and RAM telemetry while visible");

                vm.SelectedTabIndex = 0;

                BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == timerName,
                    because: "hidden Memory tab refreshes are avoidable CPU wakeups");
            }
            finally
            {
                BackgroundTimerRegistry.Unregister(timerName);
            }
        }

        [Fact]
        public void GameProfileResolvers_DoNotForceFanOrSystemControlLazyLoad()
        {
            using var vm = new MainViewModel();

            var fanResolver = typeof(MainViewModel).GetMethod("ResolveGameProfileFanPreset", BindingFlags.Instance | BindingFlags.NonPublic);
            var performanceResolver = typeof(MainViewModel).GetMethod("ResolveGameProfilePerformanceMode", BindingFlags.Instance | BindingFlags.NonPublic);

            fanResolver.Should().NotBeNull();
            performanceResolver.Should().NotBeNull();

            var fanPreset = fanResolver!.Invoke(vm, new object[] { "Gaming" }).Should().BeOfType<FanPreset>().Subject;
            fanPreset.Mode.Should().Be(FanMode.Performance);
            fanPreset.Curve.Should().NotBeEmpty();
            vm.IsFanControlLoaded.Should().BeFalse(
                because: "game profiles must apply fan presets through FanService without constructing the advanced fan page");

            _ = performanceResolver!.Invoke(vm, new object[] { "Balanced" });
            vm.IsSystemControlLoaded.Should().BeFalse(
                because: "game profiles must resolve performance modes without constructing the OMEN/Tuning view-model");
        }
    }
}
