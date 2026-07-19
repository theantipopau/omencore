using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCoreApp.Tests.Services;

[Collection("Config Isolation")]
public class GameProfileServiceTests : IDisposable
{
    private readonly string _tempDir;

    public GameProfileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "omen_game_profile_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotStartProcessMonitor_WhenGameProfilesFeatureDisabled()
    {
        var config = new ConfigurationService();
        config.Config.Features.GameProfilesEnabled = false;
        config.Save(config.Config);
        var monitor = new ProcessMonitoringService(new LoggingService());
        using var service = new GameProfileService(new LoggingService(), monitor, config);

        await service.InitializeAsync();

        monitor.IsMonitoring.Should().BeFalse();
        service.IsAutomationEnabled.Should().BeFalse();
    }

    [Fact]
    public void ProcessDetected_PrefersExactPathMatch_OverHigherPriorityGenericProfile()
    {
        using var service = CreateService(out _);
        var generic = service.CreateProfile("Generic Game", "game.exe");
        generic.Priority = 100;
        var exact = service.CreateProfile("Exact Install", "game.exe");
        exact.Priority = 0;
        exact.ExecutablePath = @"C:\Games\Game\game.exe";

        InvokeProcessDetected(service, "game", @"C:\Games\Game\game.exe");

        service.ActiveProfile.Should().BeSameAs(exact);
        exact.LaunchCount.Should().Be(1);
        generic.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void ProcessDetected_DoesNotReapplyAlreadyActiveProfile()
    {
        using var service = CreateService(out _);
        var profile = service.CreateProfile("Game", "game.exe");
        var applyCount = 0;
        service.ProfileApplyRequested += (_, args) =>
        {
            if (args.Trigger == ProfileTrigger.GameLaunch)
            {
                applyCount++;
            }
        };

        InvokeProcessDetected(service, "game");
        InvokeProcessDetected(service, "game");

        service.ActiveProfile.Should().BeSameAs(profile);
        profile.LaunchCount.Should().Be(1);
        applyCount.Should().Be(1);
    }

    [Fact]
    public void ProcessExited_PublishesExitedProfile_ForRestorePolicy()
    {
        using var service = CreateService(out _);
        var profile = service.CreateProfile("Launcher", "launcher.exe");
        profile.RestoreDefaultsOnExit = false;
        ProfileApplyEventArgs? exitArgs = null;
        service.ProfileApplyRequested += (_, args) =>
        {
            if (args.Trigger == ProfileTrigger.GameExit)
            {
                exitArgs = args;
            }
        };

        InvokeProcessDetected(service, "launcher");
        InvokeProcessExited(service, "launcher");

        service.ActiveProfile.Should().BeNull();
        service.LastExitedProfile.Should().BeSameAs(profile);
        exitArgs.Should().NotBeNull();
        exitArgs!.ExitedProfile.Should().BeSameAs(profile);
        exitArgs.ExitedProfile!.RestoreDefaultsOnExit.Should().BeFalse();
    }

    [Fact]
    public void ProcessDetected_DisambiguatesSameExecutable_ByWindowTitle()
    {
        using var service = CreateService(out _);
        var modpackA = service.CreateProfile("Modpack A", "javaw.exe");
        modpackA.WindowTitleContains = "Modpack A";
        var modpackB = service.CreateProfile("Modpack B", "javaw.exe");
        modpackB.WindowTitleContains = "Modpack B";

        InvokeProcessDetected(service, "javaw", windowTitle: "Minecraft 1.20.1 - Modpack B");

        service.ActiveProfile.Should().BeSameAs(modpackB);
        modpackB.LaunchCount.Should().Be(1);
        modpackA.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void ProcessExited_SwitchesToStillRunningTrackedGame_InsteadOfRestoringDefaults()
    {
        using var service = CreateService(out var monitor);
        var first = service.CreateProfile("First Game", "first.exe");
        var second = service.CreateProfile("Second Game", "second.exe");

        // Second game is already running alongside the first when the first exits.
        monitor.ActiveProcesses[2222] = new ProcessInfo
        {
            ProcessId = 2222,
            ProcessName = "second",
            ExecutablePath = string.Empty,
            StartTime = DateTime.Now
        };

        InvokeProcessDetected(service, "first");
        service.ActiveProfile.Should().BeSameAs(first);

        ProfileApplyEventArgs? applyArgs = null;
        service.ProfileApplyRequested += (_, args) => applyArgs = args;

        InvokeProcessExited(service, "first");

        service.ActiveProfile.Should().BeSameAs(second);
        second.LaunchCount.Should().Be(1);
        applyArgs.Should().NotBeNull();
        applyArgs!.Trigger.Should().Be(ProfileTrigger.GameLaunch);
        applyArgs.Profile.Should().BeSameAs(second);
    }

    [Fact]
    public void ProcessExited_RestoresDefaults_WhenNoOtherTrackedGameIsRunning()
    {
        using var service = CreateService(out _);
        service.CreateProfile("Only Game", "only.exe");

        InvokeProcessDetected(service, "only");

        ProfileApplyEventArgs? applyArgs = null;
        service.ProfileApplyRequested += (_, args) => applyArgs = args;

        InvokeProcessExited(service, "only");

        service.ActiveProfile.Should().BeNull();
        applyArgs.Should().NotBeNull();
        applyArgs!.Trigger.Should().Be(ProfileTrigger.GameExit);
    }

    private static GameProfileService CreateService(out ProcessMonitoringService monitor)
    {
        var logging = new LoggingService();
        var config = new ConfigurationService();
        monitor = new ProcessMonitoringService(logging);
        return new GameProfileService(logging, monitor, config);
    }

    private static void InvokeProcessDetected(GameProfileService service, string processName, string executablePath = "", string? windowTitle = null, int processId = 0)
    {
        InvokePrivate(
            service,
            "OnProcessDetected",
            new object?[]
            {
                null,
                new ProcessDetectedEventArgs(new ProcessInfo
                {
                    ProcessName = processName,
                    ExecutablePath = executablePath,
                    WindowTitle = windowTitle ?? string.Empty,
                    ProcessId = processId,
                    StartTime = DateTime.Now
                })
            });
    }

    private static void InvokeProcessExited(GameProfileService service, string processName, string executablePath = "", string? windowTitle = null, int processId = 0)
    {
        InvokePrivate(
            service,
            "OnProcessExited",
            new object?[]
            {
                null,
                new ProcessExitedEventArgs(new ProcessInfo
                {
                    ProcessName = processName,
                    ExecutablePath = executablePath,
                    WindowTitle = windowTitle ?? string.Empty,
                    ProcessId = processId,
                    StartTime = DateTime.Now.AddMinutes(-5)
                }, TimeSpan.FromMinutes(5))
            });
    }

    private static void InvokePrivate(object target, string methodName, object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(target, args);
    }
}
