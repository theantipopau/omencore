using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
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

        [Fact]
        public async Task RuntimeIntentOverlap_TrayHotkeyAutomation_ConvergesToFinalTrayMode()
        {
            using var vm = new MainViewModel();

            vm.PowerAutomation.AcPerformanceMode = "Performance";
            vm.PowerAutomation.AcFanPreset = "Performance";

            var hotkeyHandler = typeof(MainViewModel).GetMethod("OnHotkeyToggleQuietMode", BindingFlags.Instance | BindingFlags.NonPublic);
            hotkeyHandler.Should().NotBeNull();

            var perfField = typeof(MainViewModel).GetField("_performanceModeService", BindingFlags.Instance | BindingFlags.NonPublic);
            perfField.Should().NotBeNull();
            var perfService = perfField!.GetValue(vm).Should().BeOfType<PerformanceModeService>().Subject;

            var overlap = new[]
            {
                Task.Run(() => vm.SetPerformanceModeFromTray("Performance")),
                Task.Run(() => hotkeyHandler!.Invoke(vm, new object?[] { null, EventArgs.Empty })),
                Task.Run(() => vm.PowerAutomation.ApplyPowerProfile(true, "mainviewmodel-test"))
            };

            await Task.WhenAll(overlap);

            // Allow in-flight queued work from the overlap wave to settle before sending
            // the explicit final tray intent we want to assert as authoritative.
            var settleUntil = DateTime.UtcNow.AddSeconds(2);
            var lastObservedMode = perfService.GetCurrentMode();
            while (DateTime.UtcNow < settleUntil)
            {
                await Task.Delay(100);
                var currentMode = perfService.GetCurrentMode();
                if (!string.Equals(currentMode, lastObservedMode, StringComparison.OrdinalIgnoreCase))
                {
                    lastObservedMode = currentMode;
                    settleUntil = DateTime.UtcNow.AddMilliseconds(400);
                }
            }

            vm.SetPerformanceModeFromTray("Balanced");

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (string.Equals(perfService.GetCurrentMode(), "Balanced", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(50);
            }

            perfService.GetCurrentMode().Should().Be("Balanced", "explicit final tray intent should converge as authoritative final mode");
        }

        [Fact]
        public void LatestMonitoringSample_MinorTelemetryNoise_DoesNotRaiseUnchangedSummaryProperties()
        {
            using var vm = new MainViewModel();

            var latestMonitoringProperty = typeof(MainViewModel).GetProperty(
                "LatestMonitoringSample",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            latestMonitoringProperty.Should().NotBeNull();

            var setter = latestMonitoringProperty!.GetSetMethod(true);
            setter.Should().NotBeNull();

            var changedProperties = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != null)
                {
                    changedProperties.Add(args.PropertyName);
                }
            };

            var baseline = new MonitoringSample
            {
                CpuTemperatureC = 61.2,
                CpuTemperatureState = TelemetryDataState.Valid,
                GpuTemperatureC = 54.2,
                GpuTemperatureState = TelemetryDataState.Valid,
                CpuLoadPercent = 17.2,
                GpuLoadPercent = 23.2,
                RamUsageGb = 7.12,
                RamTotalGb = 16,
                SsdTemperatureC = 40.1,
                DiskUsagePercent = 11.2,
                CpuCoreClocksMhz = new List<double> { 4200.4, 4100.2 },
                Timestamp = DateTime.UtcNow
            };

            setter!.Invoke(vm, new object?[] { baseline });
            changedProperties.Clear();

            var noisy = new MonitoringSample(baseline)
            {
                CpuTemperatureC = 61.4,
                GpuTemperatureC = 54.4,
                CpuLoadPercent = 17.4,
                GpuLoadPercent = 23.4,
                RamUsageGb = 7.14,
                SsdTemperatureC = 40.3,
                DiskUsagePercent = 11.4,
                CpuCoreClocksMhz = new List<double> { 4200.1, 4100.4 },
                Timestamp = baseline.Timestamp.AddSeconds(1)
            };

            setter.Invoke(vm, new object?[] { noisy });

            changedProperties.Should().Contain(nameof(MainViewModel.LatestMonitoringSample));
            changedProperties.Should().NotContain(nameof(MainViewModel.CpuSummary));
            changedProperties.Should().NotContain(nameof(MainViewModel.GpuSummary));
            changedProperties.Should().NotContain(nameof(MainViewModel.MemorySummary));
            changedProperties.Should().NotContain(nameof(MainViewModel.StorageSummary));
            changedProperties.Should().NotContain(nameof(MainViewModel.CpuClockSummary));
        }

        [Fact]
        public void ResolveHotkeyFanCycleMode_RecognizesCanonicalFanSlots()
        {
            using var vm = new MainViewModel();
            var fanControl = vm.FanControl;
            fanControl.Should().NotBeNull();

            var fanServiceField = typeof(FanControlViewModel).GetField("_fanService", BindingFlags.Instance | BindingFlags.NonPublic);
            fanServiceField.Should().NotBeNull();
            var fanService = fanServiceField!.GetValue(fanControl!).Should().BeOfType<FanService>().Subject;
            var currentFanModeField = typeof(FanService).GetField("_currentFanMode", BindingFlags.Instance | BindingFlags.NonPublic);
            currentFanModeField.Should().NotBeNull();

            var resolver = typeof(MainViewModel).GetMethod("ResolveHotkeyFanCycleMode", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            void SetMode(string modeName)
            {
                currentFanModeField!.SetValue(fanService, modeName);
            }

            SetMode("Auto");
            resolver!.Invoke(vm, null).Should().Be("Auto");

            SetMode("Gaming");
            resolver!.Invoke(vm, null).Should().Be("Gaming");

            SetMode("Extreme");
            resolver!.Invoke(vm, null).Should().Be("Extreme");

            SetMode("Custom");
            resolver!.Invoke(vm, null).Should().Be("Custom");

            SetMode("Quiet");
            resolver!.Invoke(vm, null).Should().Be("Quiet");
        }

        [Fact]
        public void ResolveNextHotkeyFanMode_SkipsCustomSlot_WhenNoCustomCurveExists()
        {
            using var vm = new MainViewModel();

            var resolver = typeof(MainViewModel).GetMethod("ResolveNextHotkeyFanMode", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            var args = new object?[] { "Extreme", null };
            resolver!.Invoke(vm, args).Should().Be("Quiet");
            args[1].Should().Be("Quiet");
        }

        [Fact]
        public void ResolveNextHotkeyFanMode_UsesCustomSlot_WhenCustomCurveExists()
        {
            using var vm = new MainViewModel();
            vm.FanPresets.Add(new FanPreset
            {
                Name = "Field curve",
                IsBuiltIn = false,
                Mode = FanMode.Manual,
                Curve =
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 80 }
                }
            });

            var resolver = typeof(MainViewModel).GetMethod("ResolveNextHotkeyFanMode", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            var args = new object?[] { "Extreme", null };
            resolver!.Invoke(vm, args).Should().Be("Custom");
            args[1].Should().Be("Field curve");
        }

        [Fact]
        public void StartupFanRestore_AllowsSavedCustomCurve_WhenHardwareRestoreDisabled()
        {
            var helper = typeof(MainViewModel).GetMethod(
                "ShouldRestoreFanPresetOnStartup",
                BindingFlags.Static | BindingFlags.NonPublic);
            helper.Should().NotBeNull();

            var customCurve = new FanPreset
            {
                Name = "Field curve",
                IsBuiltIn = false,
                Mode = FanMode.Manual,
                Curve =
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 85 }
                }
            };

            var builtInMax = new FanPreset
            {
                Name = "Max",
                IsBuiltIn = true,
                Mode = FanMode.Max
            };

            helper!.Invoke(null, new object?[] { customCurve, false }).Should().Be(true,
                because: "saved custom curves must become the active fan owner again on startup");
            helper.Invoke(null, new object?[] { builtInMax, false }).Should().Be(false,
                because: "built-in hardware modes should still respect the startup restore safety guard");
            helper.Invoke(null, new object?[] { builtInMax, true }).Should().Be(true);
        }
    }
}
