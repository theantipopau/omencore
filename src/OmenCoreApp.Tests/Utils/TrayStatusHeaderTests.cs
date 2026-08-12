using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// Pure tray-header formatting tests for performance and monitoring health labels.
    /// Ensures status text is stable even with whitespace/empty values.
    /// </summary>
    public class TrayStatusHeaderTests
    {
        [Fact]
        public void PerformanceHeader_UsesProvidedMode()
        {
            var header = TrayIconService.BuildPerformanceModeHeaderText("Balanced");
            header.Should().Be("Performance ▶ Balanced");
        }

        [Fact]
        public void PerformanceHeader_TrimmedMode()
        {
            var header = TrayIconService.BuildPerformanceModeHeaderText("  Performance  ");
            header.Should().Be("Performance ▶ Performance");
        }

        [Fact]
        public void PerformanceHeader_EmptyMode_FallsBackToUnknown()
        {
            var header = TrayIconService.BuildPerformanceModeHeaderText("   ");
            header.Should().Be("Performance ▶ Unknown");
        }

        [Fact]
        public void MonitoringHeader_UsesProvidedHealth()
        {
            var header = TrayIconService.BuildMonitoringHealthHeaderText("Healthy");
            header.Should().Be("Monitor: Healthy");
        }

        [Fact]
        public void MonitoringHeader_TrimmedHealth()
        {
            var header = TrayIconService.BuildMonitoringHealthHeaderText("  Degraded  ");
            header.Should().Be("Monitor: Degraded");
        }

        [Fact]
        public void MonitoringHeader_EmptyHealth_FallsBackToUnknown()
        {
            var header = TrayIconService.BuildMonitoringHealthHeaderText(string.Empty);
            header.Should().Be("Monitor: Unknown");
        }
    }
}
