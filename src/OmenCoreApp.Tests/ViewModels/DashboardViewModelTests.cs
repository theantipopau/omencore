using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;
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
    public class DashboardViewModelTests
    {
        private sealed class MonitoringBridgeStub : IHardwareMonitorBridge
        {
            public string MonitoringSource => "DashboardTestStub";

            public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new MonitoringSample());
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);
        }

        private sealed class TestFanController : IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => Array.Empty<FanTelemetry>();
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
            public void Dispose() { }
        }

        private static FanService CreateFanService(LoggingService logging)
        {
            var hwMonitor = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);
            return new FanService(new TestFanController(), thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
        }

        private static void AddFanTelemetry(FanService fanService)
        {
            var field = typeof(FanService).GetField("_fanTelemetry", BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull();

            var telemetry = field!.GetValue(fanService).Should().BeAssignableTo<ObservableCollection<FanTelemetry>>().Subject;
            telemetry.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 2400, DutyCyclePercent = 42, Temperature = 55 });
            telemetry.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 2200, DutyCyclePercent = 38, Temperature = 52 });
        }

        [Fact]
        public void OnSampleUpdated_WhenDashboardProjectionDisabled_KeepsOnlyLatestQueuedSample()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();

            try
            {
                var monitoring = new HardwareMonitoringService(
                    new MonitoringBridgeStub(),
                    logging,
                    new MonitoringPreferences(),
                    new ResumeRecoveryDiagnosticsService());
                using var vm = new DashboardViewModel(monitoring);
                vm.SetTelemetryProjectionEnabled(false);

                var onSampleUpdated = typeof(DashboardViewModel).GetMethod("OnSampleUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
                onSampleUpdated.Should().NotBeNull();

                var first = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow.AddSeconds(-1),
                    CpuTemperatureC = 61,
                    GpuTemperatureC = 54,
                    CpuLoadPercent = 18,
                    GpuLoadPercent = 27,
                    CpuPowerWatts = 34,
                    GpuPowerWatts = 46,
                    Fan1Rpm = 2200,
                    Fan2Rpm = 1800
                };

                var second = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 67,
                    GpuTemperatureC = 58,
                    CpuLoadPercent = 31,
                    GpuLoadPercent = 42,
                    CpuPowerWatts = 39,
                    GpuPowerWatts = 51,
                    Fan1Rpm = 2400,
                    Fan2Rpm = 2000
                };

                onSampleUpdated!.Invoke(vm, new object?[] { null, first });
                onSampleUpdated.Invoke(vm, new object?[] { null, second });

                vm.LatestMonitoringSample.Should().BeNull("hidden dashboard projection should not update visible-state bindings");
                vm.ThermalSamples.Should().BeEmpty("hidden dashboard projection should not mutate chart history");
                vm.FilteredThermalSamples.Should().BeEmpty("hidden dashboard projection should not churn filtered chart collections");

                var queuedSampleField = typeof(DashboardViewModel).GetField("_queuedSample", BindingFlags.Instance | BindingFlags.NonPublic);
                queuedSampleField.Should().NotBeNull();
                queuedSampleField!.GetValue(vm).Should().BeSameAs(second, "hidden dashboard work should retain only the latest sample for later resume");

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.DashboardSamplesReceived.Should().Be(2);
                counters.DashboardSamplesProjected.Should().Be(0);
                counters.DashboardSamplesSkipped.Should().Be(2);
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void SetTelemetryProjectionEnabled_DisablesUptimeTimerDuringDormancy()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();

            try
            {
                var monitoring = new HardwareMonitoringService(
                    new MonitoringBridgeStub(),
                    logging,
                    new MonitoringPreferences(),
                    new ResumeRecoveryDiagnosticsService());
                using var vm = new DashboardViewModel(monitoring);

                var timerField = typeof(DashboardViewModel).GetField("_uptimeTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                timerField.Should().NotBeNull();

                var timer = (System.Windows.Threading.DispatcherTimer)timerField!.GetValue(vm)!;
                timer.IsEnabled.Should().BeTrue();

                vm.SetTelemetryProjectionEnabled(false);
                timer.IsEnabled.Should().BeFalse("dormant dashboard projection should not keep a dispatcher timer alive");

                vm.SetTelemetryProjectionEnabled(true);
                timer.IsEnabled.Should().BeTrue("visible dashboard projection should resume live uptime labels");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void OnSampleUpdated_WithoutWpfApplication_ProjectsQueuedSampleSynchronously()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();

            try
            {
                var monitoring = new HardwareMonitoringService(
                    new MonitoringBridgeStub(),
                    logging,
                    new MonitoringPreferences(),
                    new ResumeRecoveryDiagnosticsService());
                using var vm = new DashboardViewModel(monitoring);

                var onSampleUpdated = typeof(DashboardViewModel).GetMethod("OnSampleUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
                onSampleUpdated.Should().NotBeNull();

                var sample = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 62,
                    GpuTemperatureC = 49,
                    CpuLoadPercent = 24,
                    GpuLoadPercent = 12,
                    CpuPowerWatts = 28,
                    GpuPowerWatts = 33,
                    Fan1Rpm = 2100,
                    Fan2Rpm = 1900
                };

                onSampleUpdated!.Invoke(vm, new object?[] { null, sample });

                vm.LatestMonitoringSample.Should().BeSameAs(sample);
                vm.ThermalSamples.Should().ContainSingle("headless projection should still maintain chart history");

                var pendingField = typeof(DashboardViewModel).GetField("_pendingUIUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                pendingField.Should().NotBeNull();
                pendingField!.GetValue(vm).Should().Be(false, "headless projection must not leave the coalescing gate stuck closed");

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.DashboardSamplesReceived.Should().Be(1);
                counters.DashboardSamplesProjected.Should().Be(1);
                counters.DashboardSamplesSkipped.Should().Be(0);
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void DashboardDisplayProperties_FormatPowerTotalWithoutStringConverters()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();

            try
            {
                var monitoring = new HardwareMonitoringService(
                    new MonitoringBridgeStub(),
                    logging,
                    new MonitoringPreferences(),
                    new ResumeRecoveryDiagnosticsService());
                using var vm = new DashboardViewModel(monitoring);

                var latestProperty = typeof(DashboardViewModel).GetProperty(nameof(DashboardViewModel.LatestMonitoringSample));
                latestProperty.Should().NotBeNull();

                latestProperty!.GetSetMethod(nonPublic: true)!.Invoke(vm, new object?[]
                {
                    new MonitoringSample
                    {
                        CpuTemperatureC = 67,
                        GpuTemperatureC = 74,
                        SsdTemperatureC = 41,
                        CpuPowerWatts = 32.4,
                        GpuPowerWatts = 81.6,
                        CpuTemperatureState = TelemetryDataState.Valid,
                        GpuTemperatureState = TelemetryDataState.Valid
                    }
                });

                vm.PowerTotalDisplay.Should().Be("114W");
                vm.CpuTempChipDisplay.Should().Be("67°C");
                vm.GpuTempChipDisplay.Should().Be("74°C");
                vm.SsdTempDisplay.Should().Be("41°C");
                vm.CpuSummaryDisplay.Should().Be("67°C | 0% | 32W");

                latestProperty.GetSetMethod(nonPublic: true)!.Invoke(vm, new object?[]
                {
                    new MonitoringSample
                    {
                        CpuTemperatureC = 68,
                        GpuTemperatureC = 0,
                        CpuTemperatureState = TelemetryDataState.Valid,
                        GpuTemperatureState = TelemetryDataState.Inactive
                    }
                });

                vm.GpuTempChipDisplay.Should().Be("Idle");
                vm.RefreshCommand.Should().NotBeNull("the Dashboard refresh button should be connected to the view model");
            }
            finally
            {
                logging.Dispose();
            }
        }

        [Fact]
        public void FanCurveProjection_IgnoresLikelyBrokenBiosTemperatureSentinel()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);

            var logging = new LoggingService();
            logging.Initialize();

            try
            {
                var monitoring = new HardwareMonitoringService(
                    new MonitoringBridgeStub(),
                    logging,
                    new MonitoringPreferences(),
                    new ResumeRecoveryDiagnosticsService());
                var fanService = CreateFanService(logging);
                AddFanTelemetry(fanService);

                using var vm = new DashboardViewModel(monitoring, fanService);
                var onSampleUpdated = typeof(DashboardViewModel).GetMethod("OnSampleUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
                onSampleUpdated.Should().NotBeNull();

                var sample = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 100,
                    GpuTemperatureC = 52,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureState = TelemetryDataState.Valid,
                    Fan1Rpm = 2400,
                    Fan2Rpm = 2200
                };

                onSampleUpdated!.Invoke(vm, new object?[] { null, sample });

                vm.FanCurvePoints.Should().ContainSingle();
                vm.FanCurvePoints[0].TemperatureC.Should().Be(52,
                    "a lone 100C reading paired with a normal sensor is a known broken-BIOS sentinel pattern and should not pollute fan curve averages");
                vm.FanCurveSummary.Should().Contain("52");
            }
            finally
            {
                logging.Dispose();
            }
        }
    }
}
