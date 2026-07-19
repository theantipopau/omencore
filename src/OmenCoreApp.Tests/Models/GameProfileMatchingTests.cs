using FluentAssertions;
using OmenCore.Models;

namespace OmenCoreApp.Tests.Models;

public class GameProfileMatchingTests
{
    [Theory]
    [InlineData("ExampleGame", "ExampleGame.exe")]
    [InlineData("ExampleGame.exe", "ExampleGame")]
    public void MatchesProcess_NormalizesExeSuffix(string processName, string executableName)
    {
        var profile = new GameProfile { ExecutableName = executableName };

        profile.MatchesProcess(processName).Should().BeTrue();
        profile.GetProcessMatchScore(processName).Should().Be(1);
    }

    [Fact]
    public void MatchesProcess_RequiresExactPath_WhenProfilePathIsConfigured()
    {
        var profile = new GameProfile
        {
            ExecutableName = "game.exe",
            ExecutablePath = @"C:\Games\Game\game.exe"
        };

        profile.MatchesProcess("game").Should().BeFalse();
        profile.MatchesProcess("game", @"D:\Other\game.exe").Should().BeFalse();
        profile.MatchesProcess("game", @"C:\Games\Game\game.exe").Should().BeTrue();
        profile.GetProcessMatchScore("game", @"C:\Games\Game\game.exe").Should().Be(2);
    }

    [Fact]
    public void GetProcessMatchScore_RequiresWindowTitleMatch_WhenDisambiguatorConfigured()
    {
        var profile = new GameProfile
        {
            ExecutableName = "javaw.exe",
            WindowTitleContains = "Modpack B"
        };

        profile.MatchesProcess("javaw", windowTitle: "Minecraft 1.20.1 - Modpack A").Should().BeFalse();
        profile.MatchesProcess("javaw", windowTitle: null).Should().BeFalse();
        profile.MatchesProcess("javaw", windowTitle: "Minecraft 1.20.1 - Modpack B").Should().BeTrue();
        profile.GetProcessMatchScore("javaw", windowTitle: "Minecraft 1.20.1 - Modpack B").Should().Be(3);
    }

    [Fact]
    public void GetProcessMatchScore_CombinesPathAndWindowTitle_ForHighestScore()
    {
        var profile = new GameProfile
        {
            ExecutableName = "game.exe",
            ExecutablePath = @"C:\Games\Game\game.exe",
            WindowTitleContains = "Arena"
        };

        profile.GetProcessMatchScore("game", @"C:\Games\Game\game.exe", "Arena Mode").Should().Be(4);
        profile.GetProcessMatchScore("game", @"C:\Games\Game\game.exe", "Campaign Mode").Should().Be(0);
    }

    [Fact]
    public void Clone_PreservesRestoreDefaultsOnExitPolicy()
    {
        var profile = new GameProfile
        {
            Name = "Launcher",
            ExecutableName = "launcher.exe",
            RestoreDefaultsOnExit = false,
            WindowTitleContains = "My Game"
        };

        var clone = profile.Clone();

        clone.RestoreDefaultsOnExit.Should().BeFalse();
        clone.Id.Should().NotBe(profile.Id);
        clone.WindowTitleContains.Should().Be("My Game");
    }
}
