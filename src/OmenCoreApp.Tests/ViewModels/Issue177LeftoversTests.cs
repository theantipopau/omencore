using System.Linq;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>#177: the two smaller reports in that issue.</summary>
    public class Issue177LeftoversTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]   // "Temperature warnings" off now silences the critical alert too
        [InlineData(false, true, false)]
        public void TemperatureAlerts_FollowTheTemperatureWarningsSwitch(bool notifications, bool tempWarnings, bool shown)
        {
            NotificationService.ShouldShowTemperatureAlert(notifications, tempWarnings).Should().Be(shown);
        }

        [Fact]
        public void HandAddedGames_ComeBackAfterRestart_WithoutDuplicatesOrMissingFiles()
        {
            var saved = new[] { @"C:\Games\A\a.exe", @"c:\games\a\A.EXE", @"C:\Gone\b.exe", "", @"D:\Other\c.exe" };
            var restored = GameLibraryViewModel.RestoreManualGames(saved, p => !p.StartsWith(@"C:\Gone"));

            restored.Select(g => g.ExecutablePath).Should().Equal(@"C:\Games\A\a.exe", @"D:\Other\c.exe");
            restored[0].Name.Should().Be("a");
            restored[0].Platform.Should().Be(GamePlatform.Manual);
        }

        [Fact]
        public void NoSavedGames_RestoresNothing()
        {
            GameLibraryViewModel.RestoreManualGames(null, _ => true).Should().BeEmpty();
        }
    }
}
