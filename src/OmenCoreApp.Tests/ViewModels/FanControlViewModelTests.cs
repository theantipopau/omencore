using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
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

        private sealed class TestFanVerificationService : IFanVerificationService
        {
            public bool IsAvailable { get; set; }

            public Task<FanApplyResult> ApplyAndVerifyFanSpeedAsync(int fanIndex, int targetPercent, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new FanApplyResult());

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
        public void BuiltInFanCurves_ReserveNearMaxForHotOperation()
        {
            var vm = CreateViewModel();

            var auto = vm.FanPresets.Single(p => p.Name == "Auto").Curve;
            var extreme = vm.FanPresets.Single(p => p.Name == "Extreme").Curve;

            auto.Where(p => p.TemperatureC <= 80).Should().OnlyContain(p => p.FanPercent < 90,
                "Auto should not behave like Max at moderate gaming temperatures");
            extreme.Where(p => p.TemperatureC <= 70).Should().OnlyContain(p => p.FanPercent < 90,
                "Extreme is the highest non-Max curve, but Max remains the explicit 100% mode");
            extreme.Single(p => p.FanPercent == 100).TemperatureC.Should().BeGreaterThan(80);
        }

        [Fact]
        public void GamingFanCurve_DoesNotReachMaxBeforeHighThermals()
        {
            var method = typeof(OmenCore.ViewModels.FanControlViewModel)
                .GetMethod("GetGamingCurve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull();

            var gaming = ((System.Collections.Generic.List<FanCurvePoint>)method!.Invoke(null, null)!)
                .OrderBy(p => p.TemperatureC)
                .ToList();

            gaming.Where(p => p.TemperatureC <= 75).Should().OnlyContain(p => p.FanPercent < 90);
            gaming.Single(p => p.FanPercent == 100).TemperatureC.Should().BeGreaterThanOrEqualTo(90);
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
    }
}
