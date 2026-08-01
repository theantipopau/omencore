using System;
using System.Collections.Generic;
using System.IO;
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
    public class GeneralViewModelTests
    {
        private sealed class StubFanController : IFanController
        {
            public bool IsAvailable { get; set; } = true;
            public string Status => "Stub";
            public string Backend => "Stub";
            public bool ApplyPresetResult { get; set; } = true;
            public bool ApplyPreset(FanPreset preset) => ApplyPresetResult;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => Array.Empty<FanTelemetry>();
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool VerifyMaxApplied(out string details) { details = "stub"; return true; }
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
        }

        private static GeneralViewModel CreateViewModel(out LoggingService logging)
            => CreateViewModel(out logging, out _);

        private static GeneralViewModel CreateViewModel(out LoggingService logging, out StubFanController controller)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            logging = new LoggingService();
            logging.Initialize();
            controller = new StubFanController();
            var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
            var fanService = new FanService(
                controller,
                thermalProvider,
                logging,
                new NotificationService(logging),
                1000,
                new ResumeRecoveryDiagnosticsService());
            var performanceService = new PerformanceModeService(
                controller,
                new PowerPlanService(logging),
                null,
                logging);

            return new GeneralViewModel(
                fanService,
                performanceService,
                new ConfigurationService(),
                logging);
        }

        [Fact]
        public void SyncRuntimeState_UpdatesGeneralProfileFromConfirmedModes()
        {
            var vm = CreateViewModel(out var logging);
            try
            {
                vm.SyncRuntimeState("Performance", "Gaming");

                vm.CurrentPerformanceMode.Should().Be("Performance");
                vm.CurrentFanMode.Should().Be("Gaming");
                vm.SelectedProfile.Should().Be("Performance");

                vm.SyncRuntimeState("Balanced", "Auto");
                vm.SelectedProfile.Should().Be("Balanced");

                vm.SyncRuntimeState("Performance", "Performance");
                vm.SelectedProfile.Should().Be("Performance",
                    "General quick profile should remain Performance when runtime fan mode is Performance");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void ApplyPerformanceProfile_WhenFanPresetRejected_UsesConfirmedFanState()
        {
            var vm = CreateViewModel(out var logging, out var controller);
            controller.ApplyPresetResult = false;

            try
            {
                vm.ApplyPerformanceProfile();

                vm.CurrentPerformanceMode.Should().Be("Performance");
                vm.CurrentFanMode.Should().Be("Auto",
                    "General must report the confirmed FanService state instead of the requested Performance cooling preset when fan apply is rejected");
                vm.SelectedProfile.Should().Be("Custom",
                    "Performance+Auto is a mixed confirmed state rather than the full Performance quick profile");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void ApplyPerformanceProfile_WhenFanPresetApplies_KeepsPerformanceSelected()
        {
            var vm = CreateViewModel(out var logging, out var controller);
            controller.ApplyPresetResult = true;

            try
            {
                vm.ApplyPerformanceProfile();

                vm.CurrentPerformanceMode.Should().Be("Performance");
                vm.CurrentFanMode.Should().Be("Performance");
                vm.SelectedProfile.Should().Be("Performance",
                    "fresh runtime confirmation should win over saved preset state after clicking the General Performance profile");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void UpdateFromMonitoringSample_UpdatesFanTelemetryWithoutPresentationTimer()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();
            try
            {
                var controller = new StubFanController();
                var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
                var fanService = new FanService(
                    controller,
                    thermalProvider,
                    logging,
                    new NotificationService(logging),
                    1000,
                    new ResumeRecoveryDiagnosticsService());
                var performanceService = new PerformanceModeService(
                    controller,
                    new PowerPlanService(logging),
                    null,
                    logging);

                var vm = new GeneralViewModel(
                    fanService,
                    performanceService,
                    new ConfigurationService(),
                    logging);

                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 61,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureC = 54,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    Fan1Rpm = 2200,
                    Fan1RpmState = TelemetryDataState.Valid,
                    Fan2Rpm = 1800,
                    Fan2RpmState = TelemetryDataState.Valid,
                    GpuFanPercent = 44,
                    CpuLoadPercent = 17,
                    GpuLoadPercent = 23,
                    RamUsageGb = 7,
                    RamTotalGb = 16
                });

                vm.CpuTemp.Should().Be(61);
                vm.GpuTemp.Should().Be(54);
                vm.CpuFanRpm.Should().Be(2200);
                vm.GpuFanRpm.Should().Be(1800);
                vm.CpuFanPercent.Should().Be(40);
                vm.GpuFanPercent.Should().Be(44);
                vm.RamPercent.Should().BeApproximately(43.75, 0.001);

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.GeneralSamplesReceived.Should().Be(1);
                counters.GeneralSamplesProjected.Should().Be(1);
                counters.GeneralSamplesSkipped.Should().Be(0);
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void UpdateFromMonitoringSample_WhenCpuPowerIsZero_ShowsUnavailablePowerDisplay()
        {
            var vm = CreateViewModel(out var logging);
            try
            {
                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 53,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    CpuLoadPercent = 4,
                    CpuPowerWatts = 0,
                    CpuPowerState = TelemetryDataState.Zero,
                    GpuTemperatureC = 45,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    GpuPowerWatts = 0
                });

                vm.CpuPowerWatts.Should().Be(0);
                vm.CpuPowerDisplay.Should().Be("--W");
                vm.IsCpuPowerAvailable.Should().BeFalse();
                vm.CpuPowerTooltip.Should().Contain("returned 0W");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void UpdateFromMonitoringSample_WhenCpuPowerIsPositive_ShowsWatts()
        {
            var vm = CreateViewModel(out var logging);
            try
            {
                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 63,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    CpuLoadPercent = 38,
                    CpuPowerWatts = 32.4,
                    CpuPowerState = TelemetryDataState.Valid,
                    GpuTemperatureC = 45,
                    GpuTemperatureState = TelemetryDataState.Valid
                });

                vm.CpuPowerDisplay.Should().Be("32W");
                vm.IsCpuPowerAvailable.Should().BeTrue();
                vm.CpuPowerTooltip.Should().Contain("package power");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void UpdateFromMonitoringSample_SuppressesMinorRapidProjectionStorms()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();
            try
            {
                var controller = new StubFanController();
                var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
                var fanService = new FanService(
                    controller,
                    thermalProvider,
                    logging,
                    new NotificationService(logging),
                    1000,
                    new ResumeRecoveryDiagnosticsService());
                var performanceService = new PerformanceModeService(
                    controller,
                    new PowerPlanService(logging),
                    null,
                    logging);

                var vm = new GeneralViewModel(
                    fanService,
                    performanceService,
                    new ConfigurationService(),
                    logging);

                var notifications = 0;
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(GeneralViewModel.CpuTemp)
                        || args.PropertyName == nameof(GeneralViewModel.GpuTemp)
                        || args.PropertyName == nameof(GeneralViewModel.CpuLoad)
                        || args.PropertyName == nameof(GeneralViewModel.GpuLoad)
                        || args.PropertyName == nameof(GeneralViewModel.RamPercent))
                    {
                        notifications++;
                    }
                };

                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 61,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureC = 54,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    Fan1Rpm = 2200,
                    Fan1RpmState = TelemetryDataState.Valid,
                    Fan2Rpm = 1800,
                    Fan2RpmState = TelemetryDataState.Valid,
                    GpuFanPercent = 44,
                    CpuLoadPercent = 17,
                    GpuLoadPercent = 23,
                    CpuPowerWatts = 32,
                    GpuPowerWatts = 41,
                    RamUsageGb = 7,
                    RamTotalGb = 16
                });

                var afterFirstProjection = notifications;

                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 61.2,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureC = 54.1,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    Fan1Rpm = 2240,
                    Fan1RpmState = TelemetryDataState.Valid,
                    Fan2Rpm = 1830,
                    Fan2RpmState = TelemetryDataState.Valid,
                    GpuFanPercent = 45,
                    CpuLoadPercent = 17.5,
                    GpuLoadPercent = 23.4,
                    CpuPowerWatts = 32.4,
                    GpuPowerWatts = 41.3,
                    RamUsageGb = 7.1,
                    RamTotalGb = 16
                });

                notifications.Should().Be(afterFirstProjection,
                    "rapid minor telemetry changes should be coalesced instead of projected to the UI immediately");
                vm.CpuTemp.Should().Be(61);
                vm.GpuTemp.Should().Be(54);

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.GeneralSamplesReceived.Should().Be(2);
                counters.GeneralSamplesProjected.Should().Be(1);
                counters.GeneralSamplesSkipped.Should().Be(1);
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void UpdateFromMonitoringSample_WhenProjectionDisabled_SkipsHiddenSurfaceWork()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();
            try
            {
                var controller = new StubFanController();
                var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
                var fanService = new FanService(
                    controller,
                    thermalProvider,
                    logging,
                    new NotificationService(logging),
                    1000,
                    new ResumeRecoveryDiagnosticsService());
                var performanceService = new PerformanceModeService(
                    controller,
                    new PowerPlanService(logging),
                    null,
                    logging);

                var vm = new GeneralViewModel(
                    fanService,
                    performanceService,
                    new ConfigurationService(),
                    logging);

                var notifications = 0;
                vm.PropertyChanged += (_, _) => notifications++;
                vm.SetTelemetryProjectionEnabled(false);

                vm.UpdateFromMonitoringSample(new MonitoringSample
                {
                    CpuTemperatureC = 61,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureC = 54,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    Fan1Rpm = 2200,
                    Fan1RpmState = TelemetryDataState.Valid,
                    Fan2Rpm = 1800,
                    Fan2RpmState = TelemetryDataState.Valid,
                    GpuFanPercent = 44,
                    CpuLoadPercent = 17,
                    GpuLoadPercent = 23,
                    CpuPowerWatts = 32,
                    GpuPowerWatts = 41,
                    RamUsageGb = 7,
                    RamTotalGb = 16
                });

                notifications.Should().Be(0);
                vm.CpuTemp.Should().Be(0);
                vm.GpuTemp.Should().Be(0);

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.GeneralSamplesReceived.Should().Be(1);
                counters.GeneralSamplesProjected.Should().Be(0);
                counters.GeneralSamplesSkipped.Should().Be(1);
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void ApplyCustomProfile_WithCustomLastPreset_SetsSelectedProfileToCustom()
        {
            // When FanControlViewModel is not wired (null), the preset lookup returns null and
            // ApplyCustomProfile takes the "navigate-only" branch — but SelectedProfile must still
            // be set to "Custom" so the profile card highlights correctly.
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();
            try
            {
                var controller = new StubFanController();
                var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
                var fanService = new FanService(
                    controller,
                    thermalProvider,
                    logging,
                    new NotificationService(logging),
                    1000,
                    new ResumeRecoveryDiagnosticsService());
                var performanceService = new PerformanceModeService(
                    controller,
                    new PowerPlanService(logging),
                    null,
                    logging);
                var configService = new ConfigurationService();
                configService.Config.LastFanPresetName = "Gaming Curve";   // user-defined preset name

                var vm = new GeneralViewModel(fanService, performanceService, configService, logging);

                vm.ApplyCustomProfile();

                vm.SelectedProfile.Should().Be("Custom",
                    "clicking the Custom profile card must select Custom even when FanControlViewModel is absent");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void ApplyCustomProfile_WithBuiltInLastPreset_StillSetsSelectedProfileToCustom()
        {
            // If the last applied preset was a built-in (e.g. "Auto"), ResolveGeneralProfile returns
            // "Balanced", so we skip the re-apply and only navigate — but SelectedProfile must still
            // land on "Custom".
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();
            try
            {
                var controller = new StubFanController();
                var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
                var fanService = new FanService(
                    controller,
                    thermalProvider,
                    logging,
                    new NotificationService(logging),
                    1000,
                    new ResumeRecoveryDiagnosticsService());
                var performanceService = new PerformanceModeService(
                    controller,
                    new PowerPlanService(logging),
                    null,
                    logging);
                var configService = new ConfigurationService();
                configService.Config.LastFanPresetName = "Auto";   // built-in preset → should not trigger re-apply

                var vm = new GeneralViewModel(fanService, performanceService, configService, logging);

                vm.ApplyCustomProfile();

                vm.SelectedProfile.Should().Be("Custom",
                    "ApplyCustomProfile always navigates to Custom regardless of the last built-in preset");
            }
            finally
            {
                logging.Dispose();
            }
        }
    }
}
