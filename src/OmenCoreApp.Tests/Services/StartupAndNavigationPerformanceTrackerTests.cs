using System;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("NonParallel")]
    public class StartupAndNavigationPerformanceTrackerTests : IDisposable
    {
        public StartupAndNavigationPerformanceTrackerTests()
        {
            StartupAndNavigationPerformanceTracker.ResetForTests();
        }

        public void Dispose()
        {
            StartupAndNavigationPerformanceTracker.ResetForTests();
        }

        [Fact]
        public void GetSnapshot_BeforeAnyRecording_HasNoStartupTimeAndNoTabs()
        {
            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();

            snapshot.StartupTimeToInteractiveMs.Should().BeNull();
            snapshot.TabSwitches.Should().BeEmpty();
        }

        [Fact]
        public void RecordStartupTimeToInteractive_FirstCall_IsRecorded()
        {
            StartupAndNavigationPerformanceTracker.RecordStartupTimeToInteractive(TimeSpan.FromMilliseconds(842));

            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();
            snapshot.StartupTimeToInteractiveMs.Should().Be(842);
        }

        [Fact]
        public void RecordStartupTimeToInteractive_SecondCall_IsIgnored()
        {
            // There is only one startup per process - a second call (e.g. a stray extra
            // ContentRendered firing on a window re-show) must not overwrite the real number.
            StartupAndNavigationPerformanceTracker.RecordStartupTimeToInteractive(TimeSpan.FromMilliseconds(842));
            StartupAndNavigationPerformanceTracker.RecordStartupTimeToInteractive(TimeSpan.FromMilliseconds(50));

            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();
            snapshot.StartupTimeToInteractiveMs.Should().Be(842);
        }

        [Fact]
        public void RecordTabSwitch_AccumulatesCountAverageAndMax()
        {
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("Diagnostics", TimeSpan.FromMilliseconds(10));
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("Diagnostics", TimeSpan.FromMilliseconds(30));
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("Diagnostics", TimeSpan.FromMilliseconds(20));

            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();
            var diagnostics = snapshot.TabSwitches.Should().ContainSingle(t => t.TabName == "Diagnostics").Subject;

            diagnostics.Count.Should().Be(3);
            diagnostics.AverageMs.Should().Be(20);
            diagnostics.MaxMs.Should().Be(30);
        }

        [Fact]
        public void RecordTabSwitch_TracksMultipleTabsIndependently()
        {
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("General", TimeSpan.FromMilliseconds(5));
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("Monitoring", TimeSpan.FromMilliseconds(15));

            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();

            snapshot.TabSwitches.Should().HaveCount(2);
            snapshot.TabSwitches.Should().Contain(t => t.TabName == "General" && t.Count == 1 && t.AverageMs == 5);
            snapshot.TabSwitches.Should().Contain(t => t.TabName == "Monitoring" && t.Count == 1 && t.AverageMs == 15);
        }

        [Fact]
        public void RecordTabSwitch_EmptyOrNullTabName_IsIgnored()
        {
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("", TimeSpan.FromMilliseconds(10));
            StartupAndNavigationPerformanceTracker.RecordTabSwitch(null!, TimeSpan.FromMilliseconds(10));

            StartupAndNavigationPerformanceTracker.GetSnapshot().TabSwitches.Should().BeEmpty();
        }

        [Fact]
        public void ResetForTests_ClearsBothStartupTimeAndTabSwitches()
        {
            StartupAndNavigationPerformanceTracker.RecordStartupTimeToInteractive(TimeSpan.FromMilliseconds(500));
            StartupAndNavigationPerformanceTracker.RecordTabSwitch("General", TimeSpan.FromMilliseconds(5));

            StartupAndNavigationPerformanceTracker.ResetForTests();

            var snapshot = StartupAndNavigationPerformanceTracker.GetSnapshot();
            snapshot.StartupTimeToInteractiveMs.Should().BeNull();
            snapshot.TabSwitches.Should().BeEmpty();
        }
    }
}
