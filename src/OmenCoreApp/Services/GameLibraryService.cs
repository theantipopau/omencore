using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Discovers installed games from Steam, GOG, Epic, and other platforms.
    /// </summary>
    public class GameLibraryService
    {
        private readonly LoggingService _logging;
        private readonly List<DetectedGame> _detectedGames = new();

        public IReadOnlyList<DetectedGame> DetectedGames => _detectedGames.AsReadOnly();

        /// <summary>
        /// Fired when game library scan completes.
        /// </summary>
        public event EventHandler<GameLibraryScanEventArgs>? ScanCompleted;

        public GameLibraryService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Scan all known game platforms for installed games.
        /// </summary>
        public async Task<List<DetectedGame>> ScanAllPlatformsAsync()
        {
            _detectedGames.Clear();
            var allGames = new List<DetectedGame>();

            try
            {
                // Scan each platform in parallel
                var tasks = new List<Task<List<DetectedGame>>>
                {
                    ScanSteamAsync(),
                    ScanGogAsync(),
                    ScanEpicAsync(),
                    ScanXboxAsync(),
                    ScanUbisoftAsync(),
                    ScanEaAsync()
                };

                var results = await Task.WhenAll(tasks);
                
                foreach (var games in results)
                {
                    allGames.AddRange(games);
                }

                // Remove duplicates (same executable path)
                allGames = allGames
                    .GroupBy(g => g.ExecutablePath?.ToLowerInvariant() ?? g.Name)
                    .Select(g => g.First())
                    .OrderBy(g => g.Name)
                    .ToList();

                _detectedGames.AddRange(allGames);
                _logging.Info($"Game library scan complete: {allGames.Count} games found");
                
                ScanCompleted?.Invoke(this, new GameLibraryScanEventArgs(allGames.Count));
            }
            catch (Exception ex)
            {
                _logging.Error($"Game library scan failed: {ex.Message}", ex);
            }

            return allGames;
        }

        /// <summary>
        /// Scan Steam library for installed games.
        /// </summary>
        public async Task<List<DetectedGame>> ScanSteamAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // Find Steam installation path
                var steamPath = GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    _logging.Info("Steam not found");
                    return games;
                }

                // Get all Steam library folders
                var libraryFolders = await GetSteamLibraryFoldersAsync(steamPath);
                
                foreach (var libraryPath in libraryFolders)
                {
                    var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamAppsPath)) continue;

                    // Parse each .acf manifest file
                    var manifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                    foreach (var manifestPath in manifests)
                    {
                        try
                        {
                            var game = await ParseSteamManifestAsync(manifestPath, steamAppsPath);
                            if (game != null)
                            {
                                games.Add(game);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"Failed to parse Steam manifest {manifestPath}: {ex.Message}");
                        }
                    }
                }

                _logging.Info($"Steam: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"Steam scan failed: {ex.Message}", ex);
            }

            return games;
        }

        /// <summary>
        /// Scan GOG Galaxy library.
        /// </summary>
        public async Task<List<DetectedGame>> ScanGogAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // GOG Galaxy database location
                var gogDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

                if (!File.Exists(gogDbPath))
                {
                    // Try registry for installed games
                    games.AddRange(await ScanGogRegistryAsync());
                    return games;
                }

                // Note: GOG uses SQLite database - would need SQLite library
                // For now, use registry fallback
                games.AddRange(await ScanGogRegistryAsync());

                _logging.Info($"GOG: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"GOG scan failed: {ex.Message}", ex);
            }

            return games;
        }

        /// <summary>
        /// Scan Epic Games library.
        /// </summary>
        public async Task<List<DetectedGame>> ScanEpicAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // Epic Games manifest location
                var epicManifestPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "EpicGamesLauncher", "Data", "Manifests");

                if (!Directory.Exists(epicManifestPath))
                {
                    _logging.Info("Epic Games not found");
                    return games;
                }

                var manifests = Directory.GetFiles(epicManifestPath, "*.item");
                foreach (var manifestPath in manifests)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(manifestPath);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var displayName = root.TryGetProperty("DisplayName", out var name) ? name.GetString() : null;
                        var installLocation = root.TryGetProperty("InstallLocation", out var loc) ? loc.GetString() : null;
                        var launchExecutable = root.TryGetProperty("LaunchExecutable", out var exe) ? exe.GetString() : null;
                        var appName = root.TryGetProperty("AppName", out var app) ? app.GetString() : null;

                        if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(installLocation))
                        {
                            var exePath = !string.IsNullOrEmpty(launchExecutable) 
                                ? Path.Combine(installLocation, launchExecutable) 
                                : null;

                            games.Add(new DetectedGame
                            {
                                Name = displayName,
                                Platform = GamePlatform.Epic,
                                InstallPath = installLocation,
                                ExecutablePath = exePath,
                                ExecutableName = Path.GetFileName(exePath),
                                PlatformId = appName
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Error($"Failed to parse Epic manifest {manifestPath}: {ex.Message}");
                    }
                }

                _logging.Info($"Epic: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"Epic scan failed: {ex.Message}", ex);
            }

            return games;
        }

        /// <summary>
        /// Scan Xbox Game Pass / Microsoft Store games.
        /// </summary>
        public async Task<List<DetectedGame>> ScanXboxAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // Xbox games are UWP apps - stored in WindowsApps
                // Use Get-AppxPackage in PowerShell or registry
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR\FSEBehavior");
                if (key == null) return games;

                foreach (var valueName in key.GetValueNames())
                {
                    try
                    {
                        var exePath = valueName;
                        if (File.Exists(exePath))
                        {
                            var name = Path.GetFileNameWithoutExtension(exePath);
                            games.Add(new DetectedGame
                            {
                                Name = name,
                                Platform = GamePlatform.Xbox,
                                ExecutablePath = exePath,
                                ExecutableName = Path.GetFileName(exePath)
                            });
                        }
                    }
                    catch { }
                }

                _logging.Info($"Xbox/MS Store: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"Xbox scan failed: {ex.Message}", ex);
            }

            return await Task.FromResult(games);
        }

        /// <summary>
        /// Scan Ubisoft Connect library.
        /// </summary>
        public async Task<List<DetectedGame>> ScanUbisoftAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // Ubisoft Connect configuration
                var ubisoftPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ubisoft Game Launcher");

                if (!Directory.Exists(ubisoftPath))
                {
                    _logging.Info("Ubisoft Connect not found");
                    return games;
                }

                // Check registry for installed games
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var gameKey = key.OpenSubKey(subKeyName);
                            var installDir = gameKey?.GetValue("InstallDir")?.ToString();

                            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                            {
                                // Try to find main executable
                                var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
                                    .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase))
                                    .Where(f => !Path.GetFileName(f).Contains("crash", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                var mainExe = exeFiles.FirstOrDefault();
                                var name = Path.GetFileName(installDir);

                                games.Add(new DetectedGame
                                {
                                    Name = name,
                                    Platform = GamePlatform.Ubisoft,
                                    InstallPath = installDir,
                                    ExecutablePath = mainExe,
                                    ExecutableName = mainExe != null ? Path.GetFileName(mainExe) : null,
                                    PlatformId = subKeyName
                                });
                            }
                        }
                        catch { }
                    }
                }

                _logging.Info($"Ubisoft: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"Ubisoft scan failed: {ex.Message}", ex);
            }

            return await Task.FromResult(games);
        }

        /// <summary>
        /// Scan EA App (formerly Origin) library.
        /// </summary>
        public async Task<List<DetectedGame>> ScanEaAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                // EA App local content
                var eaPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Electronic Arts", "EA Desktop");

                // Check registry
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Electronic Arts");
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var gameKey = key.OpenSubKey(subKeyName);
                            var installDir = gameKey?.GetValue("Install Dir")?.ToString();

                            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                            {
                                var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
                                    .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                var mainExe = exeFiles.FirstOrDefault();

                                games.Add(new DetectedGame
                                {
                                    Name = subKeyName,
                                    Platform = GamePlatform.EA,
                                    InstallPath = installDir,
                                    ExecutablePath = mainExe,
                                    ExecutableName = mainExe != null ? Path.GetFileName(mainExe) : null
                                });
                            }
                        }
                        catch { }
                    }
                }

                _logging.Info($"EA: Found {games.Count} games");
            }
            catch (Exception ex)
            {
                _logging.Error($"EA scan failed: {ex.Message}", ex);
            }

            return await Task.FromResult(games);
        }

        #region Helper Methods

        private string? GetSteamPath()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return null;
                    
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                return key?.GetValue("InstallPath")?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<string>> GetSteamLibraryFoldersAsync(string steamPath)
        {
            var folders = new List<string> { steamPath };

            try
            {
                var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFoldersPath)) return folders;

                var content = await File.ReadAllTextAsync(libraryFoldersPath);
                
                // Parse VDF format - look for "path" entries
                var pathRegex = new Regex(@"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase);
                var matches = pathRegex.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var path = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            folders.Add(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to parse Steam library folders: {ex.Message}");
            }

            return folders;
        }

        private async Task<DetectedGame?> ParseSteamManifestAsync(string manifestPath, string steamAppsPath)
        {
            var content = await File.ReadAllTextAsync(manifestPath);

            // Parse VDF format
            var appIdMatch = Regex.Match(content, @"""appid""\s*""(\d+)""", RegexOptions.IgnoreCase);
            var nameMatch = Regex.Match(content, @"""name""\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var installDirMatch = Regex.Match(content, @"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase);

            if (!nameMatch.Success || !installDirMatch.Success) return null;

            var appId = appIdMatch.Success ? appIdMatch.Groups[1].Value : null;
            var name = nameMatch.Groups[1].Value;
            var installDir = installDirMatch.Groups[1].Value;
            var fullInstallPath = Path.Combine(steamAppsPath, "common", installDir);

            if (!Directory.Exists(fullInstallPath)) return null;

            // Try to find main executable
            string? mainExe = null;
            try
            {
                var exeFiles = Directory.GetFiles(fullInstallPath, "*.exe", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).Contains("crash", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).Contains("ue4prereq", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("_CommonRedist", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Prefer exe in root or with game name
                mainExe = exeFiles.FirstOrDefault(f => 
                    Path.GetFileName(f).Contains(installDir.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) ??
                    exeFiles.FirstOrDefault(f => Path.GetDirectoryName(f) == fullInstallPath) ??
                    exeFiles.FirstOrDefault();
            }
            catch { }

            return new DetectedGame
            {
                Name = name,
                Platform = GamePlatform.Steam,
                InstallPath = fullInstallPath,
                ExecutablePath = mainExe,
                ExecutableName = mainExe != null ? Path.GetFileName(mainExe) : null,
                PlatformId = appId,
                SteamAppId = appId
            };
        }

        private async Task<List<DetectedGame>> ScanGogRegistryAsync()
        {
            var games = new List<DetectedGame>();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
                if (key == null) return games;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = key.OpenSubKey(subKeyName);
                        var gameName = gameKey?.GetValue("gameName")?.ToString();
                        var exePath = gameKey?.GetValue("exe")?.ToString();
                        var installPath = gameKey?.GetValue("path")?.ToString();

                        if (!string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(exePath))
                        {
                            games.Add(new DetectedGame
                            {
                                Name = gameName,
                                Platform = GamePlatform.GOG,
                                InstallPath = installPath,
                                ExecutablePath = exePath,
                                ExecutableName = Path.GetFileName(exePath),
                                PlatformId = subKeyName
                            });
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"GOG registry scan failed: {ex.Message}");
            }

            return await Task.FromResult(games);
        }

        #endregion
    }

    /// <summary>
    /// Represents a detected game from a platform library.
    /// </summary>
    public class DetectedGame
    {
        public string Name { get; set; } = string.Empty;
        public GamePlatform Platform { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? ExecutableName { get; set; }
        public string? PlatformId { get; set; }
        public string? SteamAppId { get; set; }
        public string? IconPath { get; set; }
        public DateTime? LastPlayed { get; set; }
        public long? PlaytimeMinutes { get; set; }

        /// <summary>
        /// Platform-specific launch command.
        /// </summary>
        public string GetLaunchCommand()
        {
            return Platform switch
            {
                GamePlatform.Steam when !string.IsNullOrEmpty(SteamAppId) => $"steam://rungameid/{SteamAppId}",
                GamePlatform.Epic when !string.IsNullOrEmpty(PlatformId) => $"com.epicgames.launcher://apps/{PlatformId}?action=launch&silent=true",
                GamePlatform.GOG when !string.IsNullOrEmpty(PlatformId) => $"goggalaxy://openGameView/{PlatformId}",
                _ when !string.IsNullOrEmpty(ExecutablePath) => ExecutablePath,
                _ => string.Empty
            };
        }
    }

    public enum GamePlatform
    {
        Unknown,
        Steam,
        Epic,
        GOG,
        Xbox,
        Ubisoft,
        EA,
        BattleNet,
        Manual
    }

    public class GameLibraryScanEventArgs : EventArgs
    {
        public int GamesFound { get; }
        public GameLibraryScanEventArgs(int gamesFound) => GamesFound = gamesFound;
    }
}
