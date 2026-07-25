using System;
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
    /// <summary>
    /// Regression coverage for GitHub #152: the Dashboard surface (sidebar live-temp chips) rendered
    /// raw sensor output while the General tab's cards rendered the normalized/stabilized sample,
    /// so the two showed different temperatures for the same instant.
    ///
    /// The fix routes both through the single sample normalized by MainViewModel, so these tests pin
    /// the two structural guarantees that make them agree: the Dashboard must NOT subscribe to the
    /// raw telemetry event, and it must accept pushed normalized samples instead.
    /// </summary>
    [Collection("Config Isolation")]
    public class DashboardTelemetrySourceTests
    {
        private sealed class MonitoringBridgeStub : IHardwareMonitorBridge
        {
            public string MonitoringSource => "DashboardTelemetrySourceStub";

            public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new MonitoringSample());
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);
            public void Dispose() { }
        }

        private static (HardwareMonitoringService monitoring, LoggingService logging, string tempDir) CreateMonitoring()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tempDir);

            var logging = new LoggingService();
            logging.Initialize();

            var monitoring = new HardwareMonitoringService(
                new MonitoringBridgeStub(),
                logging,
                new MonitoringPreferences(),
                new ResumeRecoveryDiagnosticsService());

            return (monitoring, logging, tempDir);
        }

        private static void Cleanup(string tempDir)
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // best-effort test cleanup
            }
        }

        [Fact]
        public void DashboardViewModel_DoesNotSubscribeToRawSampleEvent()
        {
            var (monitoring, _, tempDir) = CreateMonitoring();

            try
            {
                using var vm = new DashboardViewModel(monitoring);

                // Raising the raw event must not reach the dashboard: consuming it directly is what
                // bypassed MainViewModel's normalization and caused the #152 mismatch.
                var rawSpike = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 95,
                    GpuTemperatureC = 91,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureState = TelemetryDataState.Valid
                };

                RaiseSampleUpdated(monitoring, rawSpike);

                vm.LatestMonitoringSample.Should().BeNull(
                    "the dashboard must receive telemetry only via UpdateFromNormalizedSample, never the raw event");
            }
            finally
            {
                Cleanup(tempDir);
            }
        }

        [Fact]
        public void UpdateFromNormalizedSample_ProjectsThePushedSample()
        {
            var (monitoring, _, tempDir) = CreateMonitoring();

            try
            {
                using var vm = new DashboardViewModel(monitoring);
                vm.SetTelemetryProjectionEnabled(true);

                var normalized = new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 53,
                    GpuTemperatureC = 45,
                    CpuTemperatureState = TelemetryDataState.Valid,
                    GpuTemperatureState = TelemetryDataState.Valid
                };

                vm.UpdateFromNormalizedSample(normalized);
                DrainDashboardProjection(vm);

                vm.LatestMonitoringSample.Should().NotBeNull();
                vm.CpuTemperature.Should().Be(53);
                vm.GpuTemperature.Should().Be(45);
            }
            finally
            {
                Cleanup(tempDir);
            }
        }

        [Fact]
        public void UpdateFromNormalizedSample_IgnoresNull_WithoutClobberingCurrentReading()
        {
            var (monitoring, _, tempDir) = CreateMonitoring();

            try
            {
                using var vm = new DashboardViewModel(monitoring);
                vm.SetTelemetryProjectionEnabled(true);

                vm.UpdateFromNormalizedSample(new MonitoringSample
                {
                    Timestamp = DateTime.UtcNow,
                    CpuTemperatureC = 61,
                    CpuTemperatureState = TelemetryDataState.Valid
                });
                DrainDashboardProjection(vm);

                vm.UpdateFromNormalizedSample(null);
                DrainDashboardProjection(vm);

                vm.CpuTemperature.Should().Be(61, "a null push must be a no-op, not a reset to zero");
            }
            finally
            {
                Cleanup(tempDir);
            }
        }

        /// <summary>
        /// The projection is normally marshalled through the WPF dispatcher; in a headless test run
        /// there is no Application.Current, so DashboardViewModel falls back to running it inline.
        /// This drains any queued projection deterministically either way.
        /// </summary>
        private static void DrainDashboardProjection(DashboardViewModel vm)
        {
            var process = typeof(DashboardViewModel).GetMethod(
                "ProcessQueuedUiProjection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            process.Should().NotBeNull();
            process!.Invoke(vm, Array.Empty<object>());
        }

        private static void RaiseSampleUpdated(HardwareMonitoringService monitoring, MonitoringSample sample)
        {
            var eventField = typeof(HardwareMonitoringService).GetField(
                "SampleUpdated",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (eventField?.GetValue(monitoring) is EventHandler<MonitoringSample> handler)
            {
                handler(monitoring, sample);
            }

            // A null handler is itself the assertion target for the no-subscription test: with the
            // dashboard no longer subscribing, there may be no subscribers at all.
        }
    }
}
