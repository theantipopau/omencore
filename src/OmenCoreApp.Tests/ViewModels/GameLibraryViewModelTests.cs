using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels;

[Collection("Config Isolation")]
public class GameLibraryViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public GameLibraryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "omen_game_library_vm_tests_" + Guid.NewGuid().ToString("N"));
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
    public async Task Constructor_TriggersLibraryScanAutomatically_SoGamesTabIsNotEmptyOnFirstOpen()
    {
        // Regression test for GitHub #177: GameLibraryService keeps its detected-games list
        // in memory only (by design - re-scanning is the source of truth, not a stale cache),
        // and GameLibraryViewModel is constructed lazily on first navigation to the Games tab
        // (see MainViewModel.GameLibrary). Before this fix, nothing scanned automatically, so
        // every app restart left the tab empty until the user manually clicked "Scan Library".
        var logging = new LoggingService();
        var config = new ConfigurationService();
        var monitor = new ProcessMonitoringService(logging);
        using var profileService = new GameProfileService(logging, monitor, config);
        var libraryService = new GameLibraryService(logging);

        var viewModel = new GameLibraryViewModel(logging, libraryService, profileService);

        // ScanLibraryAsync() sets IsScanning = true synchronously before its first await, so this
        // is true immediately once the constructor's fire-and-forget call has been issued -
        // no need to wait for the real (registry/filesystem-touching) scan to finish.
        viewModel.IsScanning.Should().BeTrue(
            "opening the Games tab should kick off a background scan automatically instead of showing an empty list");

        // Let the fire-and-forget scan actually finish so it doesn't outlive the test.
        for (var i = 0; i < 100 && viewModel.IsScanning; i++)
        {
            await Task.Delay(50);
        }
    }
}
