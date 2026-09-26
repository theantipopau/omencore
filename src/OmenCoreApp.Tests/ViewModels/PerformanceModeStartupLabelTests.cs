using FluentAssertions;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// GitHub #199: with startup performance restore disabled, the saved mode was pre-selected
    /// and reported as active ("Performance") while firmware - and every runtime-confirmed label -
    /// was still on "Default".
    /// </summary>
    public class PerformanceModeStartupLabelTests
    {
        [Fact]
        public void MainLabel_RestoreDisabled_NothingApplied_ReportsDefault()
        {
            MainViewModel.ResolveInitialCurrentPerformanceModeLabel(null, "Performance", startupRestoreWillApply: false)
                .Should().Be("Default");
        }

        [Fact]
        public void MainLabel_RestoreEnabled_ReportsSavedMode()
        {
            MainViewModel.ResolveInitialCurrentPerformanceModeLabel(null, "Performance", startupRestoreWillApply: true)
                .Should().Be("Performance");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MainLabel_RuntimeAppliedMode_AlwaysWins(bool restoreWillApply)
        {
            MainViewModel.ResolveInitialCurrentPerformanceModeLabel("Quiet", "Performance", restoreWillApply)
                .Should().Be("Quiet");
        }

        [Fact]
        public void SystemControlLabel_SavedModeNotApplied_ReportsDefault()
        {
            SystemControlViewModel.ResolveCurrentPerformanceModeName("Performance", savedModeNotAppliedAtStartup: true)
                .Should().Be("Default");
        }

        [Fact]
        public void SystemControlLabel_Applied_ReportsSelectedMode()
        {
            SystemControlViewModel.ResolveCurrentPerformanceModeName("Performance", savedModeNotAppliedAtStartup: false)
                .Should().Be("Performance");
        }

        [Fact]
        public void SystemControlLabel_NoSelection_KeepsHistoricalAuto()
        {
            SystemControlViewModel.ResolveCurrentPerformanceModeName(null, savedModeNotAppliedAtStartup: false)
                .Should().Be("Auto");
        }
    }
}
