using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
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
        public void ExportTelemetryCommand_InvokesService_AndLogs()
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

        private static void SetSystemInfo(MainViewModel vm, bool isHpOmen, bool isHpVictus)
        {
            var prop = typeof(MainViewModel).GetProperty(nameof(MainViewModel.SystemInfo))
                ?? throw new Exception("SystemInfo property not found");
            prop.SetValue(vm, new SystemInfo { IsHpOmen = isHpOmen, IsHpVictus = isHpVictus });
        }

        private static void SetDetectedCapabilities(MainViewModel vm, DeviceCapabilities? capabilities)
        {
            var prop = typeof(MainViewModel).GetProperty(nameof(MainViewModel.DetectedCapabilities))
                ?? throw new Exception("DetectedCapabilities property not found");
            prop.SetValue(vm, capabilities);
        }

        [Theory]
        [InlineData(false, false, "not HP OMEN or Victus at all")]
        public void ShowUnsupportedSystemBanner_TrueForNonHpGamingSystems(bool isOmen, bool isVictus, string because)
        {
            using var vm = new MainViewModel();
            SetSystemInfo(vm, isOmen, isVictus);

            vm.ShowUnsupportedSystemBanner.Should().BeTrue(because);
            vm.ShowUnverifiedModelBanner.Should().BeFalse("a non-HP-gaming system isn't a 'verified vs unverified HP model' question at all");
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ShowUnsupportedSystemBanner_FalseForHpGamingSystems(bool isOmen, bool isVictus)
        {
            using var vm = new MainViewModel();
            SetSystemInfo(vm, isOmen, isVictus);

            vm.ShowUnsupportedSystemBanner.Should().BeFalse("a real HP OMEN/Victus system is supported, even if its exact model isn't verified yet");
        }

        [Fact]
        public void ShowUnverifiedModelBanner_TrueForKnownHpGamingModel_NotYetUserVerified()
        {
            // Regression test for the banner-conflation bug: a genuinely supported HP OMEN/Victus
            // whose specific database entry hasn't been field-confirmed previously got NO banner
            // at all, despite the (single, shared) banner text claiming to cover exactly this case
            // - because visibility was gated only on IsHpGaming, not on verification status.
            using var vm = new MainViewModel();
            SetSystemInfo(vm, isHpOmen: true, isHpVictus: false);
            SetDetectedCapabilities(vm, new DeviceCapabilities
            {
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities { UserVerified = false }
            });

            vm.ShowUnverifiedModelBanner.Should().BeTrue("this is a real OMEN whose specific model entry hasn't been field-confirmed");
            vm.ShowUnsupportedSystemBanner.Should().BeFalse("this system is a supported HP OMEN, not an unsupported one");
        }

        [Fact]
        public void ShowUnverifiedModelBanner_TrueForUnknownModel_OnHpGamingSystem()
        {
            using var vm = new MainViewModel();
            SetSystemInfo(vm, isHpOmen: true, isHpVictus: false);
            SetDetectedCapabilities(vm, new DeviceCapabilities { IsKnownModel = false, ModelConfig = null });

            vm.ShowUnverifiedModelBanner.Should().BeTrue("the model fell back to generic defaults with no database entry at all");
        }

        [Fact]
        public void ShowUnverifiedModelBanner_FalseForKnownAndUserVerifiedModel()
        {
            using var vm = new MainViewModel();
            SetSystemInfo(vm, isHpOmen: true, isHpVictus: false);
            SetDetectedCapabilities(vm, new DeviceCapabilities
            {
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities { UserVerified = true }
            });

            vm.ShowUnverifiedModelBanner.Should().BeFalse("this model has been field-confirmed, so neither warning banner applies");
            vm.ShowUnsupportedSystemBanner.Should().BeFalse();
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
        public void ResolveInitialPerformanceModeForDisplay_UsesSavedMode()
        {
            var modes = new[]
            {
                new PerformanceMode { Name = "Quiet" },
                new PerformanceMode { Name = "Balanced" },
                new PerformanceMode { Name = "Performance" },
                new PerformanceMode { Name = "Turbo" }
            };

            var selected = MainViewModel.ResolveInitialPerformanceModeForDisplay(modes, "Performance");

            selected.Should().NotBeNull();
            selected!.Name.Should().Be("Performance");
        }

        [Fact]
        public void ResolveInitialPerformanceModeForDisplay_FallsBackToBalanced()
        {
            var modes = new[]
            {
                new PerformanceMode { Name = "Quiet" },
                new PerformanceMode { Name = "Balanced" },
                new PerformanceMode { Name = "Performance" }
            };

            var selected = MainViewModel.ResolveInitialPerformanceModeForDisplay(modes, "Missing");

            selected.Should().NotBeNull();
            selected!.Name.Should().Be("Balanced");
        }

        [Fact]
        public void BuildPerformanceModesForUi_PreservesConfiguredPowerLimitsAndTurbo()
        {
            var config = new AppConfig();
            config.PerformanceModes.AddRange(new[]
            {
                new PerformanceMode { Name = "Quiet", CpuPowerLimitWatts = 25, GpuPowerLimitWatts = 45 },
                new PerformanceMode { Name = "Balanced", CpuPowerLimitWatts = 45, GpuPowerLimitWatts = 85 },
                new PerformanceMode { Name = "Performance", CpuPowerLimitWatts = 65, GpuPowerLimitWatts = 115 },
                new PerformanceMode { Name = "Turbo", CpuPowerLimitWatts = 80, GpuPowerLimitWatts = 140 }
            });

            var modes = SystemControlViewModel.BuildPerformanceModesForUi(config);

            modes.Should().HaveCount(4);
            modes.Single(mode => mode.Name == "Performance").CpuPowerLimitWatts.Should().Be(65);
            modes.Single(mode => mode.Name == "Turbo").GpuPowerLimitWatts.Should().Be(140);
            modes.Should().OnlyContain(mode => !string.IsNullOrWhiteSpace(mode.Description));
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

            perfService.GetCurrentMode().Should().Be("Balanced");
        }

        [Fact]
        public void ResolveTrayFanPreset_QuietRequest_PrefersQuietPresetOverAutoAliasPreset()
        {
            using var vm = new MainViewModel();

            vm.FanPresets.Clear();
            vm.FanPresets.Add(new FanPreset
            {
                Name = "Auto Quiet Hybrid",
                Mode = FanMode.Auto,
                IsBuiltIn = false,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 30 }
                }
            });
            vm.FanPresets.Add(new FanPreset
            {
                Name = "Quiet",
                Mode = FanMode.Quiet,
                IsBuiltIn = true,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 25 }
                }
            });

            var resolver = typeof(MainViewModel).GetMethod("ResolveTrayFanPreset", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            var resolved = resolver!.Invoke(vm, new object[] { "Quiet" }).Should().BeOfType<FanPreset>().Subject;
            resolved.Name.Should().Be("Quiet");
            resolved.Mode.Should().Be(FanMode.Quiet);
        }

        [Fact]
        public void ResolveQuickAccessConfirmedMode_NonAutoRequest_WithAutoReadback_UsesRequestedPresetName()
        {
            var targetPreset = new FanPreset
            {
                Name = "Quiet",
                Mode = FanMode.Quiet,
                IsBuiltIn = true,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 25 }
                }
            };

            var resolver = typeof(MainViewModel).GetMethod("ResolveQuickAccessConfirmedMode", BindingFlags.Static | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            var resolved = resolver!.Invoke(null, new object?[] { "Quiet", targetPreset, "Auto", "Auto" }) as string;
            resolved.Should().Be("Quiet");
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
        public void ResolveNextHotkeyPerformanceProfile_SkipsCustom_WhenNoCustomCurveExists()
        {
            using var vm = new MainViewModel();

            var resolver = typeof(MainViewModel).GetMethod("ResolveNextHotkeyPerformanceProfile", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            resolver!.Invoke(vm, new object[] { "Quiet" }).Should().Be("Balanced",
                "the profile hotkey should not advertise Custom unless there is a real curve to apply");
            resolver.Invoke(vm, new object[] { "Custom" }).Should().Be("Balanced",
                "a stale label-only Custom state should cycle back to the canonical profile list");
        }

        [Fact]
        public void ResolveNextHotkeyPerformanceProfile_UsesCustom_WhenCustomCurveExists()
        {
            using var vm = new MainViewModel();
            vm.FanPresets.Add(new FanPreset
            {
                Name = "Desk curve",
                IsBuiltIn = false,
                Mode = FanMode.Manual,
                Curve =
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 32 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 90 }
                }
            });

            var resolver = typeof(MainViewModel).GetMethod("ResolveNextHotkeyPerformanceProfile", BindingFlags.Instance | BindingFlags.NonPublic);
            resolver.Should().NotBeNull();

            resolver!.Invoke(vm, new object[] { "Quiet" }).Should().Be("Custom",
                "a saved custom curve gives Ctrl+Shift+E a real Custom target instead of a label-only state");
        }

        [Fact]
        public void StartupFanRestore_RequiresGlobalAndFanCategoryOptIn()
        {
            var config = new AppConfig
            {
                EnableStartupHardwareRestore = true,
                StartupRestoreFansEnabled = false
            };

            StartupRestorePolicy.IsEnabled(config, StartupRestoreCategory.Fans).Should().BeFalse(
                because: "fan startup restore is now a category opt-in, including custom curves");

            config.StartupRestoreFansEnabled = true;
            StartupRestorePolicy.IsEnabled(config, StartupRestoreCategory.Fans).Should().BeTrue();

            config.EnableStartupHardwareRestore = false;
            StartupRestorePolicy.IsEnabled(config, StartupRestoreCategory.Fans).Should().BeFalse(
                because: "the broad safety gate remains the master switch");
        }

        // ── The adapter panel's explanation text ──────────────────────────────────────────────
        //
        // The panel exists to tell a user what the firmware said about their supply, so the one
        // thing it must not do is attribute a verdict to the firmware that the firmware did not
        // reach. Both cases below are real captures from board 8D87.

        private static void SetAdapterInfo(MainViewModel vm, byte[] reply)
        {
            var decoded = HpWmiBios.DecodeAdapterData(reply);
            decoded.Should().NotBeNull(because: "the capture is a valid 4-byte reply");

            typeof(MainViewModel)
                .GetField("_adapterInfo", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, decoded);
        }

        [Fact]
        public void PowerAdapterExplanation_Quotes_The_Firmware_On_A_Barrel_Adapter()
        {
            using var vm = new MainViewModel();
            SetAdapterInfo(vm, new byte[] { 0x02, 0xC2, 0x00, 0x38 });   // 280 W, BelowRequirement

            var text = vm.PowerAdapterExplanation;

            text.Should().NotBeNull();
            text.Should().Contain("280 W");
            text.Should().Contain("firmware reports",
                because: "on a barrel adapter the firmware really does say BelowRequirement");
        }

        [Fact]
        public void PowerAdapterExplanation_Does_Not_Put_BelowRequirement_In_The_Firmwares_Mouth_On_UsbC()
        {
            using var vm = new MainViewModel();
            SetAdapterInfo(vm, new byte[] { 0x05, 0xC2, 0x00, 0x14 });   // 100 W dock, ConnectedTypeC

            var text = vm.PowerAdapterExplanation;

            // The warning must still appear - HP's rule does call this supply under-rated, and the
            // GPU really is clamped - so the bug would be fixed just as wrongly by silencing it.
            text.Should().NotBeNull(because: "the barrel special case makes this supply low-wattage");
            text.Should().Contain("100 W");
            text.Should().Contain("USB-C");

            text.Should().NotContain("adapter as below this machine's requirement",
                because: "the firmware reported ConnectedTypeC, a description of the supply; the " +
                         "under-rated judgement is HP's rule about the chassis, not the firmware's verdict");
        }

        // ── The limit shown before the restart button ─────────────────────────────────────────
        //
        // The restart costs a black screen and every GPU context on the machine, so the panel has to
        // say whether there is anything to gain before it is pressed. The readings below are the
        // measured ones from board 8D87: 35 W enforced against an 80 W default while clamped, and
        // 80 W against 80 W once the driver has restarted without the verdict.

        private static void SetGpuPowerLimits(MainViewModel vm, double? enforced, double? standard)
        {
            typeof(MainViewModel)
                .GetField("_gpuPowerLimits", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new AdapterPowerOverrideService.PowerLimits(enforced, standard));
        }

        [Fact]
        public void GpuPowerLimit_Shows_The_Enforced_Limit_Against_The_Cards_Own()
        {
            using var vm = new MainViewModel();
            SetGpuPowerLimits(vm, 35.0, 80.0);

            vm.HasGpuPowerLimitReading.Should().BeTrue();
            vm.GpuPowerLimitSummary.Should().Contain("35 W");
            vm.GpuPowerLimitSummary.Should().Contain("80 W",
                because: "35 W alone says nothing; the gap to the card's own limit is the evidence");
        }

        [Fact]
        public void GpuPowerLimit_Names_The_Clamp_Without_Ruling_Out_Another_Tool()
        {
            using var vm = new MainViewModel();
            SetGpuPowerLimits(vm, 35.0, 80.0);

            var text = vm.GpuPowerLimitAttribution;

            text.Should().Contain("45 W below", because: "the size of the gap is the finding");
            text.Should().Contain("clamp");
            text.Should().Contain("Another tool",
                because: "a third-party power limit looks identical from here, and this panel cannot " +
                         "tell them apart; claiming the adapter did it would be a diagnosis it has " +
                         "not earned");
        }

        [Fact]
        public void GpuPowerLimit_Says_When_There_Is_Nothing_To_Discard()
        {
            using var vm = new MainViewModel();
            SetGpuPowerLimits(vm, 80.0, 80.0);

            // The state after a successful restart, and the state on a board that never clamps.
            // Someone reading this must not be left thinking a restart is still owed to them.
            vm.GpuPowerLimitAttribution.Should().Contain("no clamp to discard");
            vm.GpuPowerLimitAttribution.Should().NotContain("below its own limit");
        }

        [Fact]
        public void GpuPowerLimit_Does_Not_Guess_When_The_Card_Withheld_Its_Default()
        {
            using var vm = new MainViewModel();
            SetGpuPowerLimits(vm, 35.0, null);

            vm.GpuPowerLimitSummary.Should().Contain("35 W");
            vm.GpuPowerLimitAttribution.Should().Contain("cannot be told",
                because: "35 W is only low relative to something, and that something was not reported");
        }
    }
}
