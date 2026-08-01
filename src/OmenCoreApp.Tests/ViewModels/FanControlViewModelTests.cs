using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;
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
            public int ApplyPresetCallCount { get; private set; }
            public int ApplyCustomCurveCallCount { get; private set; }

            public bool ApplyPreset(FanPreset preset)
            {
                ApplyPresetCallCount++;
                return true;
            }

            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve)
            {
                ApplyCustomCurveCallCount++;
                return true;
            }
            public bool SetFanSpeed(int percent) { LastSetPercent = percent; SetCallCount++; return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { LastSetPercent = System.Math.Max(cpuPercent, gpuPercent); SetCallCount++; return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => new System.Collections.Generic.List<FanTelemetry>();
            public bool ApplyMaxCooling() { LastSetPercent = 100; SetCallCount++;  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        private sealed class TestFanVerificationService : IFanVerificationService
        {
            public bool IsAvailable { get; set; }
            public int ApplyAndVerifyCallCount { get; private set; }

            public Task<FanApplyResult> ApplyAndVerifyFanSpeedAsync(int fanIndex, int targetPercent, System.Threading.CancellationToken ct = default)
            {
                ApplyAndVerifyCallCount++;
                return Task.FromResult(new FanApplyResult
                {
                    FanIndex = fanIndex,
                    RequestedPercent = targetPercent,
                    WmiCallSucceeded = true,
                    VerificationPassed = true,
                    ActualRpmAfter = 2200
                });
            }

            public Task<FanApplyResult> ApplyWithEnhancedVerificationAsync(int fanIndex, int targetPercent, bool autoRevertOnFailure = true, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new FanApplyResult());

            public Task<FanCalibrationResult> PerformFanCalibrationAsync(int fanIndex, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new FanCalibrationResult());

            public bool RestoreFanControlAfterCalibration() => true;

            public (int rpm, int level) GetCurrentFanState(int fanIndex) => (0, 0);

            public (int rpm, int level, RpmSource source) GetCurrentFanStateWithSource(int fanIndex) => (0, 0, RpmSource.Estimated);

            public Task<(int avg, int min, int max)> GetStableFanRpmAsync(int fanIndex, int samples = 3, System.Threading.CancellationToken ct = default)
                => Task.FromResult((0, 0, 0));
        }

        private static OmenCore.ViewModels.FanControlViewModel CreateViewModel(IFanVerificationService? verificationService = null)
        {
            var logging = new LoggingService();
            logging.Initialize();

            var configService = new ConfigurationService();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var controller = new TestFanController();
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            return new OmenCore.ViewModels.FanControlViewModel(fanService, configService, logging, verificationService);
        }

        private static FanService GetFanService(OmenCore.ViewModels.FanControlViewModel vm)
        {
            var field = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetField("_fanService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull();
            return field!.GetValue(vm).Should().BeOfType<FanService>().Subject;
        }

        private static TestFanController GetTestFanController(OmenCore.ViewModels.FanControlViewModel vm)
        {
            var service = GetFanService(vm);
            var field = typeof(FanService)
                .GetField("_fanController", BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull();
            return field!.GetValue(service).Should().BeOfType<TestFanController>().Subject;
        }

        private static void AddFanTelemetry(FanService fanService)
        {
            var field = typeof(FanService)
                .GetField("_fanTelemetry", BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull();
            var telemetry = field!.GetValue(fanService).Should().BeAssignableTo<ObservableCollection<FanTelemetry>>().Subject;
            telemetry.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1800, DutyCyclePercent = 32, Temperature = 55 });
            telemetry.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 1700, DutyCyclePercent = 30, Temperature = 53 });
        }

        private static Task RunCurveVerificationKickAsync(
            OmenCore.ViewModels.FanControlViewModel vm,
            string sourceLabel,
            System.Collections.Generic.IEnumerable<FanCurvePoint> curvePoints,
            Action reapplyCurveAction)
        {
            var method = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetMethod("RunCurveVerificationKickAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();
            return method!.Invoke(vm, new object[] { sourceLabel, curvePoints, reapplyCurveAction })
                .Should().BeAssignableTo<Task>().Subject;
        }

        private static Task ApplyCustomCurveAsync(OmenCore.ViewModels.FanControlViewModel vm)
        {
            var method = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetMethod("ApplyCustomCurveAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();
            return method!.Invoke(vm, Array.Empty<object>())
                .Should().BeAssignableTo<Task>().Subject;
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
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

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

        [Fact]
        public void FanOwnershipSummary_ExplainsCurrentFanOwner()
        {
            var vm = CreateViewModel();

            vm.FanOwnershipSummary.Should().Contain("firmware owns fan control");
            vm.FanOwnershipDetail.Should().Contain("Backend: Test");

            vm.ActiveFanMode = "Max";
            vm.FanOwnershipSummary.Should().Contain("full cooling");

            vm.ActiveFanMode = "Constant";
            vm.ConstantFanPercent = 42;
            vm.FanOwnershipSummary.Should().Contain("42%");
        }

        [Fact]
        public void DirectFanMode_ExposesConstantSliderState()
        {
            var vm = CreateViewModel();

            vm.ManualFanControlAvailable.Should().BeTrue();
            vm.ShowConstantControl.Should().BeFalse();
            vm.CurrentFanModeName.Should().NotBe("Direct");

            vm.IsConstantSelected = true;
            vm.ConstantFanPercent = 65;

            vm.ShowConstantControl.Should().BeTrue();
            vm.CurrentFanModeName.Should().Be("Direct");
            vm.DirectPresetSubtitle.Should().Be("65%");
            vm.DirectFanControlTooltip.Should().Contain("fixed");
            vm.FanProfileHeaderHint.Should().Contain("direct fan level");
        }

        [Fact]
        public async Task DirectFanMode_SliderChange_DoesNotAutoApplyUntilCommandRuns()
        {
            var vm = CreateViewModel();
            var controller = GetTestFanController(vm);

            vm.IsConstantSelected = true;
            vm.ConstantFanPercent = 65;

            controller.SetCallCount.Should().Be(0, "moving the Direct slider should only update the requested value");

            vm.ApplyConstantSpeedCommand.Execute(null);
            for (var i = 0; i < 20 && controller.SetCallCount == 0; i++)
            {
                await Task.Delay(25);
            }

            controller.SetCallCount.Should().Be(1);
            controller.LastSetPercent.Should().Be(65);
        }

        [Fact]
        public void FanCalibrationStatusText_WhenVerificationServiceMissing_ShowsInitializationReason()
        {
            var vm = CreateViewModel();

            vm.IsFanCalibrationAvailable.Should().BeFalse();
            vm.FanCalibrationStatusText.Should().Contain("not initialized");
            vm.FanCalibrationUnavailableReason.Should().Contain("not initialized");
        }

        [Fact]
        public void FanCalibrationStatusText_WhenVerificationBackendInactive_ShowsBackendContext()
        {
            var verifier = new TestFanVerificationService { IsAvailable = false };
            var vm = CreateViewModel(verifier);

            vm.IsFanCalibrationAvailable.Should().BeFalse();
            vm.FanCalibrationStatusText.Should().Contain("backend is inactive");
            vm.FanCalibrationStatusText.Should().Contain("active fan backend");
        }

        [Fact]
        public void FanCalibrationUnavailableReason_WhenVerificationAvailable_ReportsAvailable()
        {
            var verifier = new TestFanVerificationService { IsAvailable = true };
            var vm = CreateViewModel(verifier);

            vm.IsFanCalibrationAvailable.Should().BeTrue();
            vm.FanCalibrationUnavailableReason.Should().Contain("available");
        }

        [Fact]
        public async Task ApplyCustomCurve_PersistsAdHocCustomPresetAndLastCurve()
        {
            var vm = CreateViewModel();
            vm.CustomFanCurve.Clear();
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 40, FanPercent = 30 });
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 60, FanPercent = 55 });
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 85, FanPercent = 100 });

            await ApplyCustomCurveAsync(vm);

            var saved = new ConfigurationService().Load();
            saved.LastFanPresetName.Should().Be("Custom");
            saved.CustomFanCurve.Should().NotBeNull();
            saved.CustomFanCurve!.Should().HaveCount(3);
            saved.FanPresets.Should().ContainSingle(p => p.Name == "Custom" && !p.IsBuiltIn);

            vm.SelectedPreset.Should().NotBeNull();
            vm.SelectedPreset!.Name.Should().Be("Custom");
            vm.SelectedPreset.IsBuiltIn.Should().BeFalse();
        }

        [Fact]
        public void Constructor_RestoresLastAppliedAdHocCustomCurveIntoEditor()
        {
            var configService = new ConfigurationService();
            var config = configService.Load();
            config.LastFanPresetName = "Custom";
            config.CustomFanCurve = new()
            {
                new FanCurvePoint { TemperatureC = 42, FanPercent = 33 },
                new FanCurvePoint { TemperatureC = 82, FanPercent = 88 }
            };
            configService.Save(config);

            var vm = CreateViewModel();

            vm.SelectedPreset.Should().NotBeNull();
            vm.SelectedPreset!.Name.Should().Be("Custom");
            vm.CustomFanCurve.Select(p => p.TemperatureC).Should().Equal(42, 82);
            vm.CustomFanCurve.Select(p => p.FanPercent).Should().Equal(33, 88);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Renamed curve")]
        public void Constructor_MigratesSavedAdHocCurveWhenLastPresetIsMissingOrStale(string? lastPresetName)
        {
            var configService = new ConfigurationService();
            var config = configService.Load();
            config.LastFanPresetName = lastPresetName;
            config.CustomFanCurve = new()
            {
                new FanCurvePoint { TemperatureC = 45, FanPercent = 35 },
                new FanCurvePoint { TemperatureC = 85, FanPercent = 95 }
            };
            configService.Save(config);

            var vm = CreateViewModel();

            vm.SelectedPreset.Should().NotBeNull();
            vm.SelectedPreset!.Name.Should().Be("Custom");
            vm.CustomFanCurve.Select(p => p.TemperatureC).Should().Equal(45, 85);
            vm.CustomFanCurve.Select(p => p.FanPercent).Should().Equal(35, 95);
            var controller = GetTestFanController(vm);
            controller.ApplyPresetCallCount.Should().Be(0,
                "restoring the saved UI selection must not apply fan hardware during construction");
            controller.ApplyCustomCurveCallCount.Should().Be(0,
                "restoring the saved UI selection must not apply fan hardware during construction");
            new ConfigurationService().Load().LastFanPresetName.Should().Be("Custom");
        }

        [Fact]
        public void SelectPresetByNameNoApplyAndSave_UpdatesSelectionAndPersistsLastFanPresetName()
        {
            var vm = CreateViewModel();

            vm.SelectPresetByNameNoApplyAndSave("Max");

            vm.SelectedPreset!.Name.Should().Be("Max");
            var controller = GetTestFanController(vm);
            controller.ApplyPresetCallCount.Should().Be(0,
                "the preset was already applied to hardware by the caller (tray/hotkey/General quick-profile); this method must only sync UI and persist");
            new ConfigurationService().Load().LastFanPresetName.Should().Be("Max");
        }

        [Fact]
        public void SelectPresetByNameNoApply_DoesNotPersistLastFanPresetName()
        {
            var vm = CreateViewModel();
            vm.SelectPresetByNameNoApplyAndSave("Max");

            // A subsequent automatic/temporary sync (e.g. power-source automation) must not
            // overwrite the user's deliberately saved startup preference.
            vm.SelectPresetByNameNoApply("Quiet");

            vm.SelectedPreset!.Name.Should().Be("Quiet");
            new ConfigurationService().Load().LastFanPresetName.Should().Be("Max",
                "SelectPresetByNameNoApply is used for automatic/temporary preset changes and must not " +
                "silently overwrite the last deliberately saved startup preference");
        }

        [Fact]
        public void ClearDeletedPresetConfigState_RemovesStaleLastCustomCurve()
        {
            var configService = new ConfigurationService();
            var config = configService.Load();
            config.LastFanPresetName = "Field curve";
            config.CustomFanCurve = new()
            {
                new FanCurvePoint { TemperatureC = 42, FanPercent = 33 },
                new FanCurvePoint { TemperatureC = 82, FanPercent = 88 }
            };
            config.FanPresets.Add(new FanPreset
            {
                Name = "Field curve",
                IsBuiltIn = false,
                Mode = FanMode.Manual,
                Curve = config.CustomFanCurve.ToList()
            });
            configService.Save(config);

            var vm = CreateViewModel();
            var clearMethod = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetMethod("ClearDeletedPresetConfigState", BindingFlags.Instance | BindingFlags.NonPublic);
            clearMethod.Should().NotBeNull();

            clearMethod!.Invoke(vm, new object[] { "Field curve" });

            var saved = configService.Load();
            saved.LastFanPresetName.Should().Be("Auto");
            saved.CustomFanCurve.Should().BeNull();
        }

        [Fact]
        public void BuiltInFanCurves_MaxOutAtFieldVerifiedHighTemps()
        {
            var vm = CreateViewModel();

            var auto = vm.FanPresets.Single(p => p.Name == "Auto").Curve;
            var extreme = vm.FanPresets.Single(p => p.Name == "Extreme").Curve;
            var quiet = vm.FanPresets.Single(p => p.Name == "Quiet").Curve;

            auto.Single(p => p.FanPercent == 100).TemperatureC.Should().Be(75,
                "Balanced/Auto should reach full cooling before the high-80s on OMEN 16-xd0xxx");
            extreme.Single(p => p.FanPercent == 100).TemperatureC.Should().Be(75,
                "Extreme should restore its long-standing 75C full-cooling endpoint");
            quiet.Last().Should().Match<FanCurvePoint>(p => p.TemperatureC == 80 && p.FanPercent == 85,
                "Quiet should stay capped; the QuietSafetyMonitor handles emergency Max fan override");
        }

        [Fact]
        public void BuiltInPresets_DoNotExposeGhostManualPreset()
        {
            var vm = CreateViewModel();

            vm.FanPresets.Should().NotContain(p => p.Name == "Manual",
                "the advanced fan page should not manufacture a fake Manual preset that can leak into runtime state or hotkey cycling");
        }

        [Fact]
        public void GamingFanCurve_ReachesMaxAtEightyC()
        {
            var method = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetMethod("GetGamingCurve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull();

            var gaming = ((System.Collections.Generic.List<FanCurvePoint>)method!.Invoke(null, null)!)
                .OrderBy(p => p.TemperatureC)
                .ToList();

            gaming.Single(p => p.FanPercent == 100).TemperatureC.Should().Be(80);
        }

        [Fact]
        public void QuickFanModeCommands_DoNotChangeUiState_DuringDiagnosticMode()
        {
            var vm = CreateViewModel();
            var fanService = GetFanService(vm);

            fanService.EnterDiagnosticMode();
            try
            {
                var beforeMode = vm.ActiveFanMode;

                vm.ApplyGamingModeCommand.Execute(null);
                vm.ApplyFanMode("Extreme");
                vm.ApplyQuietModeCommand.Execute(null);

                vm.ActiveFanMode.Should().Be(beforeMode,
                    "quick fan mode commands should be ignored while diagnostics own the fans");
            }
            finally
            {
                fanService.ExitDiagnosticMode();
            }
        }

        [Fact]
        public void DeleteSelectedPresetCommand_Requeries_WhenSelectedPresetChanges()
        {
            var vm = CreateViewModel();
            var customPreset = new FanPreset
            {
                Name = "Field curve",
                Mode = FanMode.Manual,
                IsBuiltIn = false,
                Curve =
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 80 }
                }
            };
            var canExecuteChangedCount = 0;
            vm.FanPresets.Add(customPreset);
            vm.DeleteSelectedPresetCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

            vm.SelectedPreset = vm.FanPresets.First(p => p.IsBuiltIn);

            vm.CanDeleteSelectedPreset.Should().BeFalse();
            vm.DeleteSelectedPresetCommand.CanExecute(null).Should().BeFalse();

            vm.SelectedPreset = customPreset;

            vm.CanDeleteSelectedPreset.Should().BeTrue();
            vm.DeleteSelectedPresetCommand.CanExecute(null).Should().BeTrue();
            canExecuteChangedCount.Should().BeGreaterThan(0,
                "the delete button must re-enable when a saved custom curve is selected");
        }

        [Fact]
        public void CurrentTemperature_RefreshesSafetyFloorPreview()
        {
            var vm = CreateViewModel();
            vm.CustomFanCurve.Clear();
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 40, FanPercent = 20 });
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 95, FanPercent = 20 });
            vm.CurrentTemperature = 79;

            var changed = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.PropertyName))
                {
                    changed.Add(args.PropertyName);
                }
            };

            vm.CurrentTemperature = 82;

            vm.PredictedFanPercent.Should().Be(20);
            vm.EffectiveFanPercent.Should().Be(40);
            vm.IsSafetyFloorActive.Should().BeTrue();
            vm.SafetyFloorNoticeText.Should().Contain("curve requests 20%");
            vm.SafetyFloorNoticeText.Should().Contain("command 40%");
            changed.Should().Contain(nameof(vm.EffectiveFanPercent));
            changed.Should().Contain(nameof(vm.IsSafetyFloorActive));
            changed.Should().Contain(nameof(vm.SafetyFloorNoticeText));
            changed.Should().Contain(nameof(vm.CurveValidationMessage));
        }

        [Fact]
        public void CurveValidationMessage_PrioritizesThermalGuard_WhenSafetyFloorIsActive()
        {
            var vm = CreateViewModel();
            vm.CustomFanCurve.Clear();
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 40, FanPercent = 20 });
            vm.CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 95, FanPercent = 20 });
            vm.CurrentTemperature = 82;

            vm.CurveValidationMessage.Should().StartWith("Thermal guard active:");
            vm.CurvePreviewText.Should().Contain("requested 20%, effective 40%");
        }

        [Fact]
        public async Task CurveVerificationKick_SkipsRepeatedSameTargetWithinCooldown()
        {
            var verification = new TestFanVerificationService { IsAvailable = true };
            var vm = CreateViewModel(verification);
            AddFanTelemetry(GetFanService(vm));
            vm.CurrentTemperature = 55;
            var curve = new[]
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                new FanCurvePoint { TemperatureC = 80, FanPercent = 70 }
            };

            await RunCurveVerificationKickAsync(vm, "Preset 'Field curve'", curve, () => { });
            await RunCurveVerificationKickAsync(vm, "Preset 'Field curve'", curve, () => { });

            verification.ApplyAndVerifyCallCount.Should().Be(1,
                "automatic post-apply verification should not repeat the same expensive WMI diagnostic kick inside the cooldown window");
            vm.CurveApplyStatus.Should().Contain("verification recently checked");
        }

        [Fact]
        public void BuiltInGamingAndQuietPresets_PublishCanonicalRuntimeModes()
        {
            var vm = CreateViewModel();
            var fanService = GetFanService(vm);

            vm.FanPresets.Should().ContainSingle(p => p.Name == "Gaming" && p.IsBuiltIn);
            vm.FanPresets.Should().ContainSingle(p => p.Name == "Quiet" && p.IsBuiltIn);

            fanService.ApplyPreset(vm.FanPresets.Single(p => p.Name == "Gaming")).Should().BeTrue();
            fanService.GetCurrentFanMode().Should().Be("Gaming");

            fanService.ApplyPreset(vm.FanPresets.Single(p => p.Name == "Quiet")).Should().BeTrue();
            fanService.GetCurrentFanMode().Should().Be("Quiet");
        }

        [Fact]
        public void ExtremeCurve_StaysAtOrAboveGamingCurveOnSharedTemperaturePoints()
        {
            var vm = CreateViewModel();
            var gamingCurve = vm.FanPresets.Single(p => p.Name == "Gaming").Curve.OrderBy(p => p.TemperatureC).ToList();
            var extremeCurve = vm.FanPresets.Single(p => p.Name == "Extreme").Curve.OrderBy(p => p.TemperatureC).ToList();

            foreach (var temperature in gamingCurve.Select(p => p.TemperatureC).Where(t => t <= 80))
            {
                var gamingPoint = Interpolate(gamingCurve, temperature);
                var extremePoint = Interpolate(extremeCurve, temperature);

                extremePoint.Should().BeGreaterThanOrEqualTo(gamingPoint,
                    $"Extreme should never trail Gaming at {temperature}C");
            }

            extremeCurve.Single(p => p.FanPercent == 100).TemperatureC.Should().Be(75);
        }

        private static int Interpolate(IReadOnlyList<FanCurvePoint> curve, int temperature)
        {
            if (temperature <= curve[0].TemperatureC) return curve[0].FanPercent;
            if (temperature >= curve[^1].TemperatureC) return curve[^1].FanPercent;

            for (var i = 0; i < curve.Count - 1; i++)
            {
                var left = curve[i];
                var right = curve[i + 1];
                if (temperature < left.TemperatureC || temperature > right.TemperatureC)
                    continue;

                var ratio = (temperature - left.TemperatureC) / (double)(right.TemperatureC - left.TemperatureC);
                return (int)Math.Round(left.FanPercent + ((right.FanPercent - left.FanPercent) * ratio));
            }

            return curve[^1].FanPercent;
        }
    }
}
