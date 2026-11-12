using System;
using System.IO;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The report is the hand-off between two processes (the app writes it, the CLI reads it), so
    /// what matters is that what survives the file is what the app measured, and that a stale or
    /// broken file reads as "nothing" rather than as numbers from a different run.
    /// </summary>
    public class NavigationTimingReportTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"omencore-nav-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static StartupAndNavigationSnapshot Snapshot(double? startupMs = 1234.5) => new(
            startupMs,
            new[]
            {
                new TabSwitchSnapshot("General", 3, 12.34, 40.0),
                new TabSwitchSnapshot("Monitoring", 3, 87.66, 120.0),
            });

        [Fact]
        public void FromSnapshot_CopiesPagesPassesAndStartup()
        {
            var report = NavigationTimingReport.FromSnapshot(Snapshot(), passes: 3);

            report.Passes.Should().Be(3);
            report.StartupTimeToInteractiveMs.Should().Be(1234.5);
            report.Pages.Should().HaveCount(2);
            report.Pages[1].Page.Should().Be("Monitoring");
            report.Pages[1].Samples.Should().Be(3);
            report.Pages[1].AverageMs.Should().Be(87.7, "the report rounds to a tenth of a millisecond");
        }

        [Fact]
        public void Slowest_IsTheHighestAverage()
        {
            NavigationTimingReport.FromSnapshot(Snapshot(), passes: 1).Slowest!.Page.Should().Be("Monitoring");
            NavigationTimingReport.FromSnapshot(new StartupAndNavigationSnapshot(null, Array.Empty<TabSwitchSnapshot>()), 1)
                .Slowest.Should().BeNull();
        }

        [Fact]
        public void SaveThenTryRead_RoundTrips()
        {
            var written = NavigationTimingReport.FromSnapshot(Snapshot(), passes: 2);
            written.Save(_path);

            var read = NavigationTimingReport.TryRead(_path);

            read.Should().NotBeNull();
            read!.Passes.Should().Be(2);
            read.GeneratedAtUtc.Should().Be(written.GeneratedAtUtc);
            read.Pages.Should().HaveCount(2);
            read.Pages[0].Page.Should().Be("General");
            read.Pages[0].MaxMs.Should().Be(40.0);
        }

        [Fact]
        public void TryRead_MissingFile_ReturnsNull()
        {
            NavigationTimingReport.TryRead(_path).Should().BeNull();
        }

        [Fact]
        public void TryRead_UnreadableFile_ReturnsNull()
        {
            File.WriteAllText(_path, "{ this is not a report");

            NavigationTimingReport.TryRead(_path).Should().BeNull();
        }

        [Fact]
        public void DefaultPath_LivesWithTheLogs()
        {
            NavigationTimingReport.DefaultPath.Should().EndWith(Path.Combine("OmenCore", "nav-report.json"));
        }
    }
}
