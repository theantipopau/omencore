using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    // Reported on board 8D87: the dashboard "Fan Speeds" card read 28 while the fans were turning
    // at roughly 2800 RPM. The series was plotting HardwareMetrics.FanEfficiency - a 0-100 proxy
    // computed as avgRpm/50 - under an axis that GetLabelForChartType labels "RPM", so every reading
    // was understated by ~50x. These tests pin the axis label and the plotted value to the same
    // unit, in both directions, so the two cannot drift apart again.
    public class FanChartUnitsTests
    {
        private sealed class StubBridge : IHardwareMonitorBridge
        {
            public string MonitoringSource => "StubBridge";
            public System.Threading.Tasks.Task<MonitoringSample> ReadSampleAsync(System.Threading.CancellationToken token)
                => System.Threading.Tasks.Task.FromResult(new MonitoringSample());
            public System.Threading.Tasks.Task<bool> TryRestartAsync() => System.Threading.Tasks.Task.FromResult(true);
        }

        private static HardwareMonitoringService CreateService()
        {
            var logging = new LoggingService();
            logging.Initialize();
            return new HardwareMonitoringService(
                new StubBridge(), logging, new MonitoringPreferences(), new ResumeRecoveryDiagnosticsService());
        }

        private static object? Invoke(HardwareMonitoringService svc, string name, params object[] args)
        {
            var method = typeof(HardwareMonitoringService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull($"{name} is the method under test");
            return method!.Invoke(svc, args);
        }

        [Fact]
        public void FanSpeedsChart_PlotsRpm_NotTheZeroToHundredProxy()
        {
            using var svc = CreateService();

            var metrics = new HardwareMetrics
            {
                FanRpmAverage = 2800,
                FanEfficiency = 56 // the proxy for the same reading: 2800 / 50
            };

            var value = (double)Invoke(svc, "GetValueForChartType", metrics, ChartType.FanSpeeds)!;

            value.Should().Be(2800, "the axis is labelled RPM, so the series must carry RPM");
            value.Should().NotBe(56, "plotting the 0-100 proxy under an RPM axis is what rendered 2800 RPM as 28");
        }

        [Fact]
        public void FanSpeedsChart_LabelAndValue_AgreeOnTheUnit()
        {
            using var svc = CreateService();

            var label = (string)Invoke(svc, "GetLabelForChartType", ChartType.FanSpeeds)!;
            label.Should().Be("RPM");

            // A mid-range fan speed must land in RPM's order of magnitude, not a percentage's.
            var metrics = new HardwareMetrics { FanRpmAverage = 3200, FanEfficiency = 64 };
            var value = (double)Invoke(svc, "GetValueForChartType", metrics, ChartType.FanSpeeds)!;

            value.Should().BeGreaterThan(100,
                "a real fan speed exceeds any percentage scale; a value under 100 here means the proxy leaked back in");
        }

        [Fact]
        public void FanSpeedsChart_ReportsZero_WhenNoFanTelemetryIsAvailable()
        {
            // Distinct from the defect above: zero must stay zero rather than become a floor or an
            // estimate. On boards with no usable tachometer this is the honest reading, and the
            // dashboard should not invent motion it cannot measure.
            using var svc = CreateService();

            var value = (double)Invoke(svc, "GetValueForChartType",
                new HardwareMetrics { FanRpmAverage = 0, FanEfficiency = 0 }, ChartType.FanSpeeds)!;

            value.Should().Be(0);
        }
    }
}
