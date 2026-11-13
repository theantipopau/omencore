using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Installation type for platform-aware updates
    /// </summary>
    public enum InstallationType
    {
        Installer,  // Installed via Setup.exe
        Portable,   // Running from extracted folder
        Unknown
    }

    /// <summary>
    /// Handles application auto-update functionality
    /// </summary>
    public class AutoUpdateService : IDisposable
    {
        private static readonly TimeSpan DownloadRetention = TimeSpan.FromDays(14);
        private const string PartialDownloadSuffix = ".partial";

        private readonly LoggingService _logging;
        private readonly HttpClient _httpClient;
        private readonly string _updateCheckUrl;
        private readonly string _releaseFeedUrl;
        private readonly string _downloadDirectory;
        private readonly Version _currentVersion;
        private readonly InstallationType _installationType;
        private readonly SemaphoreSlim _scheduledCheckSemaphore = new(1, 1);
        private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);
        private System.Timers.Timer? _checkTimer;
        private UpdatePreferences? _preferences;
        private bool _disposed;
        
        public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;
        public event EventHandler<UpdateDownloadProgress>? DownloadProgressChanged;
        public event EventHandler<UpdateInstallResult>? InstallCompleted;
        
        /// <summary>
        /// Gets the detected installation type (Installer or Portable)
        /// </summary>
        public InstallationType InstallationType => _installationType;
        
        public AutoUpdateService(LoggingService logging, string updateCheckUrl = "https://api.github.com/repos/theantipopau/omencore/releases/latest")
        {
            _logging = logging;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OmenCore-Updater");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            
            _updateCheckUrl = updateCheckUrl;
            _releaseFeedUrl = updateCheckUrl.EndsWith("/latest", StringComparison.OrdinalIgnoreCase)
                ? updateCheckUrl.Substring(0, updateCheckUrl.Length - "/latest".Length) + "?per_page=20"
                : "https://api.github.com/repos/theantipopau/omencore/releases?per_page=20";
            _downloadDirectory = Path.Combine(Path.GetTempPath(), "OmenCore", "Updates");
            Directory.CreateDirectory(_downloadDirectory);
            CleanupStaleDownloads();
            _currentVersion = LoadCurrentVersion();
            _installationType = DetectInstallationType();
            _logging.Info($"Detected installation type: {_installationType}");
        }
        
        /// <summary>
        /// Detect whether running from installer or portable mode
        /// </summary>
        // Inno Setup registers the uninstaller under "<AppId>_is1", where AppId is the GUID in
        // installer/OmenCoreInstaller.iss - not under the product name.
        private const string InnoUninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F5B6F3F-8FAF-4FC8-A5E0-4E2C0E8F2E2B}_is1";
        private const string LegacyUninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OmenCore";

        private InstallationType DetectInstallationType()
        {
            try
            {
                if (InstallerRegistryKeyExists())
                    return InstallationType.Installer;

                return DetectInstallationTypeFromDirectory(
                    GetRunningExeDirectory(),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    File.Exists);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to detect installation type: {ex.Message}");
                return InstallationType.Unknown;
            }
        }

        /// <summary>
        /// The folder the running exe actually lives in. NOT <see cref="AppContext.BaseDirectory"/>:
        /// release builds are published with IncludeAllContentForSelfExtract, which points that at
        /// the %TEMP%\.net\OmenCore\&lt;hash&gt; extraction folder - never Program Files and never next
        /// to the uninstaller. That made every installed release build detect as Portable, download
        /// the portable zip, and then fail to "install" it as an exe.
        /// </summary>
        private static string? GetRunningExeDirectory()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
                return Path.GetDirectoryName(processPath);
            return AppContext.BaseDirectory;
        }

        private static bool InstallerRegistryKeyExists()
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var inno = root.OpenSubKey(InnoUninstallKey);
                    if (inno != null) return true;
                    using var legacy = root.OpenSubKey(LegacyUninstallKey);
                    if (legacy != null) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Installer if the exe sits under Program Files or next to Inno Setup's uninstaller,
        /// otherwise Portable. Pure so it can be tested without a real install.
        /// </summary>
        internal static InstallationType DetectInstallationTypeFromDirectory(
            string? exeDirectory, string? programFiles, string? programFilesX86, Func<string, bool> fileExists)
        {
            if (string.IsNullOrEmpty(exeDirectory))
                return InstallationType.Unknown;

            if ((!string.IsNullOrEmpty(programFiles) && exeDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(programFilesX86) && exeDirectory.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
            {
                return InstallationType.Installer;
            }

            if (fileExists(Path.Combine(exeDirectory, "unins000.exe")) ||
                fileExists(Path.Combine(exeDirectory, "Uninstall.exe")))
            {
                return InstallationType.Installer;
            }

            return InstallationType.Portable;
        }
        
        /// <summary>
        /// Configure background update checking with user preferences
        /// </summary>
        public void ConfigureBackgroundChecks(UpdatePreferences preferences)
        {
            _preferences = preferences;
            
            // Stop existing timer if any
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            BackgroundTimerRegistry.Unregister("AutoUpdateCheck");
            
            if (preferences.AutoCheckEnabled && preferences.CheckIntervalHours > 0)
            {
                var intervalMs = TimeSpan.FromHours(preferences.CheckIntervalHours).TotalMilliseconds;
                _checkTimer = new System.Timers.Timer(intervalMs);
                _checkTimer.Elapsed += CheckTimerElapsed;
                _checkTimer.AutoReset = true;
                _checkTimer.Start();
                BackgroundTimerRegistry.Register(
                    "AutoUpdateCheck",
                    "AutoUpdateService",
                    "Background update availability check",
                    (int)TimeSpan.FromHours(preferences.CheckIntervalHours).TotalMilliseconds,
                    BackgroundTimerTier.Optional);
                
                _logging.Info($"Background update checks enabled (every {preferences.CheckIntervalHours}h)");
            }
            else
            {
                _checkTimer = null;
                _logging.Info("Background update checks disabled");
            }
        }

        private void CheckTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            _ = RunScheduledCheckSafelyAsync();
        }

        private async Task RunScheduledCheckSafelyAsync()
        {
            if (!await _scheduledCheckSemaphore.WaitAsync(0))
            {
                _logging.Debug("Skipping scheduled update check because another check is already running.");
                return;
            }

            try
            {
                await OnTimerCheckAsync();
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "RunScheduledCheckSafelyAsync",
                    message: "Unhandled exception from update timer callback",
                    ex: ex);
            }
            finally
            {
                _scheduledCheckSemaphore.Release();
            }
        }
        
        /// <summary>
        /// Perform scheduled update check (triggered by timer)
        /// </summary>
        private async Task OnTimerCheckAsync()
        {
            try
            {
                _logging.Info("Performing scheduled update check");
                var result = await CheckForUpdatesAsync();
                
                // Update last check time
                if (_preferences != null)
                {
                    _preferences.LastCheckTime = DateTime.Now;
                }
                
                // Check if version should be skipped
                if (result.UpdateAvailable && _preferences?.SkippedVersion == result.LatestVersion?.ToString())
                {
                    _logging.Info($"Update v{result.LatestVersion} available but skipped by user");
                    return;
                }
                
                // Notify if update is available
                if (result.UpdateAvailable && _preferences?.ShowUpdateNotifications == true)
                {
                    _logging.Info($"Update available: v{result.LatestVersion}");
                    UpdateCheckCompleted?.Invoke(this, result);
                }
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "OnTimerCheckAsync",
                    message: "Scheduled update check failed",
                    ex: ex);
            }
        }
        
        /// <summary>
        /// Check for available updates from the update server
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = new VersionInfo { Version = _currentVersion }
            };
            
            try
            {
                _logging.Info("Checking for updates...");
                
                var jsonContent = await GetReleasePayloadAsync();
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    result.Status = UpdateStatus.CheckFailed;
                    result.Message = "No release data returned from GitHub.";
                    _logging.Warn(result.Message);
                    return result;
                }
                
                // Parse GitHub release JSON
                using var jsonDoc = JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;
                
                var tagName = root.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName))
                {
                    result.Status = UpdateStatus.CheckFailed;
                    result.Message = "Invalid release data from server.";
                    return result;
                }
                
                // Parse version from tag (e.g., "v1.4.0-beta2" -> "1.4.0")
                // Strip 'v' prefix and handle semver suffixes like -beta, -alpha, -rc
                var versionString = tagName.TrimStart('v');
                var semverSuffix = string.Empty;
                
                // Extract semver suffix (e.g., "-beta2", "-alpha", "-rc1")
                var dashIndex = versionString.IndexOf('-');
                if (dashIndex > 0)
                {
                    semverSuffix = versionString[dashIndex..]; // e.g., "-beta2"
                    versionString = versionString.Substring(0, dashIndex); // e.g., "1.4.0"
                }
                
                if (!Version.TryParse(versionString, out var latestVersion))
                {
                    result.Status = UpdateStatus.CheckFailed;
                    result.Message = $"Invalid version format: {tagName}";
                    _logging.Warn($"Could not parse version from tag: {tagName}");
                    return result;
                }
                
                // For comparison, we need to handle prerelease versions
                // A prerelease (beta, alpha) is LESS than the release version
                // But beta2 > beta1, and 1.4.0-beta > 1.3.0 release
                var isLatestPrerelease = !string.IsNullOrEmpty(semverSuffix);
                var isCurrentPrerelease = IsCurrentVersionPrerelease();
                
                // Compare versions with prerelease logic
                var shouldUpdate = CompareVersionsWithPrerelease(
                    _currentVersion, GetCurrentPrereleaseTag(),
                    latestVersion, semverSuffix);
                
                // Check if newer version available
                if (shouldUpdate)
                {
                    result.UpdateAvailable = true;
                    result.Status = UpdateStatus.UpdateAvailable;
                    result.LatestVersion = new VersionInfo
                    {
                        Version = latestVersion,
                        ReleaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                        ReleaseDate = root.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String
                            ? DateTime.TryParse(published.GetString(), out var publishedDate) ? publishedDate : DateTime.UtcNow
                            : DateTime.UtcNow,
                        ChangelogUrl = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? string.Empty : string.Empty
                    };

                    // Get download URL from assets - platform-aware selection
                    if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var selectedAsset = SelectPlatformAwareAsset(assets);

                        if (selectedAsset.ValueKind != JsonValueKind.Undefined)
                        {
                            result.LatestVersion.DownloadUrl = selectedAsset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                            result.LatestVersion.AssetFileName = selectedAsset.GetProperty("name").GetString() ?? string.Empty;
                            var size = selectedAsset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
                            result.LatestVersion.FileSize = size;
                            result.LatestVersion.FileSizeFormatted = FormatFileSize(size);

                            var sidecarName = result.LatestVersion.AssetFileName + ".sha256";
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (asset.TryGetProperty("name", out var nameElement) &&
                                    string.Equals(nameElement.GetString(), sidecarName, StringComparison.OrdinalIgnoreCase) &&
                                    asset.TryGetProperty("browser_download_url", out var sidecarUrl))
                                {
                                    result.LatestVersion.Sha256SidecarUrl = sidecarUrl.GetString() ?? string.Empty;
                                    break;
                                }
                            }
                        }
                    }
                    
                    // Extract SHA256 hash from release notes for the selected asset
                    result.LatestVersion.Sha256Hash = ExtractHashForAsset(result.LatestVersion.ReleaseNotes, result.LatestVersion.AssetFileName) ?? string.Empty;
                    
                    if (string.IsNullOrWhiteSpace(result.LatestVersion.DownloadUrl))
                    {
                        result.Message = "Update available on GitHub releases.";
                    }
                    else
                    {
                        result.Message = $"Update available: v{latestVersion}";
                    }
                    
                    _logging.Info(result.Message);
                }
                else
                {
                    result.Status = UpdateStatus.UpToDate;
                    result.Message = "You are running the latest version.";
                    result.UpdateAvailable = false;
                    _logging.Info(result.Message);
                }
            }
            catch (HttpRequestException ex)
            {
                result.Status = UpdateStatus.NetworkError;
                result.Message = $"Network error: {ex.Message}";
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "CheckForUpdatesAsync.Network",
                    message: "Update check failed",
                    ex: ex);
            }
            catch (Exception ex)
            {
                result.Status = UpdateStatus.CheckFailed;
                result.Message = $"Update check failed: {ex.Message}";
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "CheckForUpdatesAsync",
                    message: "Update check failed",
                    ex: ex);
            }
            
            UpdateCheckCompleted?.Invoke(this, result);
            return result;
        }

        private async Task<string?> GetReleasePayloadAsync()
        {
            var includePreReleases = _preferences?.IncludePreReleases == true;

            if (!includePreReleases)
            {
                // Fast path for stable updates.
                var stablePayload = await GetStringIfSuccessAsync(_updateCheckUrl);
                if (!string.IsNullOrWhiteSpace(stablePayload))
                {
                    return stablePayload;
                }
            }

            // Fallback and prerelease path: use releases feed and pick matching candidate.
            var feedPayload = await GetStringIfSuccessAsync(_releaseFeedUrl);
            if (string.IsNullOrWhiteSpace(feedPayload))
            {
                return null;
            }

            using var feedDoc = JsonDocument.Parse(feedPayload);
            if (feedDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var release in feedDoc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean())
                {
                    continue;
                }

                var isPrerelease = release.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
                if (!includePreReleases && isPrerelease)
                {
                    continue;
                }

                return release.GetRawText();
            }

            return null;
        }

        private async Task<string?> GetStringIfSuccessAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logging.Warn($"Update endpoint returned HTTP {(int)response.StatusCode}: {url}");
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }

        private void CleanupStaleDownloads(string? preservePath = null)
        {
            try
            {
                if (!Directory.Exists(_downloadDirectory))
                {
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                var removedCount = 0;
                var preserveFullPath = string.IsNullOrWhiteSpace(preservePath)
                    ? null
                    : Path.GetFullPath(preservePath);

                foreach (var filePath in Directory.EnumerateFiles(_downloadDirectory))
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(filePath);
                        if (!string.IsNullOrWhiteSpace(preserveFullPath) &&
                            string.Equals(fullPath, preserveFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var info = new FileInfo(fullPath);
                        var age = nowUtc - info.LastWriteTimeUtc;
                        var isPartial = info.Name.EndsWith(PartialDownloadSuffix, StringComparison.OrdinalIgnoreCase) ||
                                        info.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase);

                        if (isPartial || age > DownloadRetention)
                        {
                            info.Delete();
                            removedCount++;
                        }
                    }
                    catch (Exception fileEx)
                    {
                        _logging.Debug($"Skipped update-cache cleanup for '{filePath}': {fileEx.Message}");
                    }
                }

                if (removedCount > 0)
                {
                    _logging.Info($"Cleaned up {removedCount} stale update file(s) from {_downloadDirectory}");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Update-cache cleanup failed: {ex.Message}");
            }
        }

        private static void DeleteFileIfExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        
        /// <summary>
        /// Download an update package
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(VersionInfo versionInfo, CancellationToken cancellationToken = default)
        {
            var lockTaken = false;
            string? tempDownloadPath = null;

            try
            {
                lockTaken = await _downloadSemaphore.WaitAsync(0, cancellationToken);
                if (!lockTaken)
                {
                    _logging.Warn("Download request ignored because another update download is already in progress.");
                    return null;
                }

                _logging.Info($"Downloading update {versionInfo.VersionString}...");
                if (string.IsNullOrWhiteSpace(versionInfo.DownloadUrl))
                {
                    _logging.Warn("No download URL provided for the requested update.");
                    return null;
                }

                // Prefer the checksum file the release workflow attached over the hash in the notes text (WP-30).
                if (!string.IsNullOrWhiteSpace(versionInfo.Sha256SidecarUrl))
                {
                    var sidecarHash = await TryReadSidecarHashAsync(versionInfo.Sha256SidecarUrl, versionInfo.AssetFileName);
                    if (sidecarHash != null)
                    {
                        if (!string.IsNullOrWhiteSpace(versionInfo.Sha256Hash) &&
                            !sidecarHash.Equals(versionInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Two hashes published for the same file that disagree: refuse rather than pick one.
                            _logging.Error($"Download blocked: the release's checksum file ({sidecarHash[..16]}...) and its notes ({versionInfo.Sha256Hash[..Math.Min(16, versionInfo.Sha256Hash.Length)]}...) disagree.");
                            return null;
                        }

                        versionInfo.Sha256Hash = sidecarHash;
                        _logging.Info("Using the SHA256 from the release's checksum file");
                    }
                }

                // Block download if no hash — we cannot verify integrity post-download
                if (string.IsNullOrWhiteSpace(versionInfo.Sha256Hash))
                {
                    _logging.Warn("Download blocked: the release has no SHA256 for this asset (no checksum file and none in the notes). Open GitHub releases to download manually.");
                    return null;
                }

                CleanupStaleDownloads();

                // Use actual asset filename from GitHub release, fallback to constructed name
                var fileName = !string.IsNullOrWhiteSpace(versionInfo.AssetFileName) 
                    ? versionInfo.AssetFileName 
                    : $"OmenCoreSetup-{versionInfo.VersionString}.exe";
                var downloadPath = Path.Combine(_downloadDirectory, fileName);
                tempDownloadPath = downloadPath + PartialDownloadSuffix;
                
                // Ensure stale destination artifacts cannot contaminate a new download.
                DeleteFileIfExists(downloadPath);
                DeleteFileIfExists(tempDownloadPath);
                
                using var response = await _httpClient.GetAsync(versionInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                // Log response diagnostics to aid troubleshooting asset-selection and redirect issues
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "none";
                var actualUrl = response.RequestMessage?.RequestUri?.ToString() ?? versionInfo.DownloadUrl;
                _logging.Info($"Download response — Status: {(int)response.StatusCode}, Content-Type: {contentType}, URL: {actualUrl}");
                if (!contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase) &&
                    !contentType.Contains("application/", StringComparison.OrdinalIgnoreCase) &&
                    !contentType.Contains("binary/", StringComparison.OrdinalIgnoreCase))
                {
                    _logging.Warn($"Unexpected Content-Type '{contentType}' — may indicate an error page or redirect loop");
                }
                
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                var buffer = new byte[8192];
                var stopwatch = Stopwatch.StartNew();
                
                // Download to file - use explicit scope to ensure file handle is released
                {
                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        downloadedBytes += bytesRead;
                        
                        // Report progress every 100KB
                        if (downloadedBytes % 102400 == 0 || downloadedBytes == totalBytes)
                        {
                            var elapsed = stopwatch.Elapsed.TotalSeconds;
                            var speedMbps = elapsed > 0 ? (downloadedBytes / elapsed) / (1024 * 1024) : 0;
                            var remaining = speedMbps > 0 ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / (speedMbps * 1024 * 1024)) : TimeSpan.Zero;
                            
                            var progress = new UpdateDownloadProgress
                            {
                                BytesDownloaded = downloadedBytes,
                                TotalBytes = totalBytes,
                                StatusMessage = $"Downloading {fileName}...",
                                DownloadSpeedMbps = speedMbps,
                                EstimatedTimeRemaining = remaining
                            };
                        
                            DownloadProgressChanged?.Invoke(this, progress);
                        }
                    }
                }
                // End of explicit scope - file handle released here
                
                // Small delay to ensure file handle is fully released by OS
                await Task.Delay(100, cancellationToken);
                
                // Verify file hash if available (preferred for security)
                // Hash guaranteed non-empty — pre-validated above before download started
                var computedHash = ComputeSha256Hash(tempDownloadPath);
                if (!computedHash.Equals(versionInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    _logging.Error($"SHA256 verification failed! Expected: {versionInfo.Sha256Hash}, Computed: {computedHash}");
                    DeleteFileIfExists(tempDownloadPath);
                    throw new System.Security.SecurityException($"Update package failed SHA256 verification. File may be corrupted or tampered with.");
                }
                _logging.Info($"✓ SHA256 hash verified successfully ({computedHash[..16]}...)");
                
                // Validate downloaded file before returning
                var stagedFileInfo = new FileInfo(tempDownloadPath);
                if (stagedFileInfo.Length < 100_000) // Less than 100KB is definitely not a valid installer or ZIP
                {
                    _logging.Error($"Downloaded file is suspiciously small ({stagedFileInfo.Length} bytes) — likely an error page or corrupt download");
                    DeleteFileIfExists(tempDownloadPath);
                    return null;
                }
                
                // Validate file header — PE executable (MZ) or ZIP archive (PK)
                using (var headerStream = new FileStream(tempDownloadPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[4];
                    await headerStream.ReadAsync(header, 0, 4, cancellationToken);
                    var isMZ = header[0] == 0x4D && header[1] == 0x5A; // MZ = PE executable
                    var isPK = header[0] == 0x50 && header[1] == 0x4B; // PK = ZIP archive
                    
                    if (!isMZ && !isPK)
                    {
                        _logging.Error($"Downloaded file has invalid header (0x{header[0]:X2}{header[1]:X2}) — not a valid executable or archive. File may be an HTML error page.");
                        DeleteFileIfExists(tempDownloadPath);
                        return null;
                    }
                    
                    // If we expected an installer (.exe) but got a ZIP, or vice versa, warn
                    if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !isMZ)
                    {
                        _logging.Error($"Downloaded file has .exe extension but is not a PE executable (header: 0x{header[0]:X2}{header[1]:X2})");
                        DeleteFileIfExists(tempDownloadPath);
                        return null;
                    }
                }

                DeleteFileIfExists(downloadPath);
                File.Move(tempDownloadPath, downloadPath);
                CleanupStaleDownloads(downloadPath);

                var fileInfo = new FileInfo(downloadPath);
                
                _logging.Info($"Update downloaded and validated successfully: {downloadPath} ({fileInfo.Length:N0} bytes)");
                return downloadPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logging.Warn("Update download cancelled.");
                DeleteFileIfExists(tempDownloadPath);
                return null;
            }
            catch (System.Security.SecurityException)
            {
                // A hash mismatch must reach the caller, which tells the user the package was rejected; the file is already deleted.
                throw;
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "DownloadUpdateAsync",
                    message: "Update download failed",
                    ex: ex);

                DeleteFileIfExists(tempDownloadPath);
                return null;
            }
            finally
            {
                if (lockTaken)
                {
                    _downloadSemaphore.Release();
                }
            }
        }
        
        internal static string PortableUpdateMessage(string zipPath) =>
            "This is the portable version of OmenCore, which can't replace itself while it's running. " +
            $"The update has been downloaded to {zipPath}. Close OmenCore, then extract that zip over " +
            "your current OmenCore folder.";

        /// <summary>
        /// Install a downloaded update
        /// </summary>
        public async Task<UpdateInstallResult> InstallUpdateAsync(string installerPath)
        {
            var result = new UpdateInstallResult();
            
            try
            {
                if (!File.Exists(installerPath))
                {
                    result.Success = false;
                    result.Message = "Installer file not found.";
                    return result;
                }
                
                // Validate PE header before attempting to execute
                try
                {
                    using var headerStream = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var header = new byte[2];
                    headerStream.Read(header, 0, 2);

                    // A portable build downloads the zip, which cannot replace the exe that is
                    // running it. Say so, and show the file, rather than reporting a zip as a
                    // "corrupted" installer.
                    if (header[0] == 0x50 && header[1] == 0x4B) // PK - zip archive
                    {
                        result.Success = false;
                        result.Message = PortableUpdateMessage(installerPath);
                        _logging.Info($"Portable update downloaded to {installerPath}; manual extraction required.");
                        try
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{installerPath}\"") { UseShellExecute = true });
                        }
                        catch (Exception openEx)
                        {
                            _logging.Debug($"Could not open Explorer at the portable update: {openEx.Message}");
                        }
                        return result;
                    }

                    if (header[0] != 0x4D || header[1] != 0x5A) // MZ header
                    {
                        result.Success = false;
                        result.Message = "Downloaded file is not a valid Windows executable. The download may have been corrupted. Please try again or download manually from GitHub.";
                        _logging.Error($"Installer failed PE validation — header bytes: 0x{header[0]:X2}{header[1]:X2} (expected MZ/0x4D5A). File: {installerPath}");
                        return result;
                    }
                }
                catch (Exception headerEx)
                {
                    _logging.Warn($"Could not validate installer header: {headerEx.Message}");
                }
                
                _logging.Info($"Installing update from {installerPath}...");
                
                // Stop HardwareWorker first to release any file locks
                var workerProcesses = Process.GetProcessesByName("OmenCore.HardwareWorker");
                foreach (var proc in workerProcesses)
                {
                    try
                    {
                        _logging.Info($"Stopping HardwareWorker process (PID: {proc.Id})...");
                        if (!proc.HasExited)
                            proc.Kill();
                        var exited = proc.WaitForExit(3000);
                        if (!exited)
                            _logging.Warn($"HardwareWorker (PID: {proc.Id}) did not exit within 3 s; installer may be unable to replace locked files");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Could not stop HardwareWorker (PID: {proc.Id}): {ex.Message}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                
                // Launch installer with silent flags
                // Note: Don't use /CLOSEAPPLICATIONS as it can cause system slowness
                // Instead, we close OmenCore ourselves before launching installer
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /NORESTART /RELAUNCH=1",
                    UseShellExecute = true,
                    Verb = "runas" // Request elevation
                };
                
                Process.Start(startInfo);
                
                result.Success = true;
                result.Message = "Update installer launched. Application will restart after installation.";
                result.InstallerPath = installerPath;
                result.RequiresRestart = true;
                
                _logging.Info("Update installer launched successfully.");
                
                // Close the current application after a short delay
                await Task.Delay(2000);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Installation failed: {ex.Message}";
                _logging.ErrorWithContext(
                    component: "AutoUpdateService",
                    operation: "InstallUpdateAsync",
                    message: "Update installation failed",
                    ex: ex);
            }
            
            InstallCompleted?.Invoke(this, result);
            return result;
        }
        
        /// <summary>
        /// Enable or disable automatic update checking on startup
        /// </summary>
        public void SetAutoUpdateEnabled(bool enabled)
        {
            _logging.Info($"Auto-update {(enabled ? "enabled" : "disabled")}");
            
            if (_preferences != null)
            {
                _preferences.AutoCheckEnabled = enabled;
                _preferences.CheckOnStartup = enabled;
                // Re-apply the updated preferences to start/stop the timer
                ConfigureBackgroundChecks(_preferences);
            }
        }
        
        /// <summary>
        /// Get the current version of the application
        /// </summary>
        public Version GetCurrentVersion() => _currentVersion;
        
        /// <summary>Downloads a small "&lt;asset&gt;.sha256" file; null on any failure, so the caller falls back to the notes.</summary>
        private async Task<string?> TryReadSidecarHashAsync(string url, string assetFileName)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("application/octet-stream");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logging.Warn($"Checksum file download failed: HTTP {(int)response.StatusCode}");
                    return null;
                }

                if (response.Content.Headers.ContentLength is > 4096)
                {
                    _logging.Warn("Checksum file is larger than expected; ignoring it");
                    return null;
                }

                var text = await response.Content.ReadAsStringAsync(cts.Token);
                var hash = ParseSha256Sidecar(text, assetFileName);
                if (hash == null) _logging.Warn("Checksum file did not contain a SHA256 for this asset");
                return hash;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logging.Warn($"Checksum file could not be read: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a checksum file in the `sha256sum` format ("&lt;64 hex&gt;  &lt;name&gt;", optionally "*&lt;name&gt;" for binary mode) or a bare hash.
        /// A line that names a file must name this asset; a bare hash is accepted only when it is the file's only content.
        /// </summary>
        public static string? ParseSha256Sidecar(string? text, string assetFileName)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !IsSha256Hex(parts[0])) continue;

                if (parts.Length == 1)
                {
                    if (lines.Length == 1) return parts[0].ToLowerInvariant();
                    continue;
                }

                var name = parts[1].Trim().TrimStart('*');
                if (string.Equals(name, assetFileName, StringComparison.OrdinalIgnoreCase))
                    return parts[0].ToLowerInvariant();
            }

            return null;
        }

        private static bool IsSha256Hex(string value) =>
            value.Length == 64 && value.All(Uri.IsHexDigit);

        private string ComputeSha256Hash(string filePath)
        {
            // Retry logic in case file handle isn't fully released yet
            const int maxRetries = 3;
            const int retryDelayMs = 200;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var sha256 = SHA256.Create();
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    _logging.Warn($"File locked, retrying hash computation (attempt {attempt}/{maxRetries})...");
                    Thread.Sleep(retryDelayMs * attempt);
                }
            }
            
            // Final attempt - let it throw if still locked
            using var sha256Final = SHA256.Create();
            using var streamFinal = File.OpenRead(filePath);
            var hashFinal = sha256Final.ComputeHash(streamFinal);
            return BitConverter.ToString(hashFinal).Replace("-", "").ToLowerInvariant();
        }

        // Store the prerelease tag from VERSION.txt (e.g., "-beta2")
        private string _currentPrereleaseTag = string.Empty;
        
        private Version LoadCurrentVersion()
        {
            Version? versionFromFile = null;
            string prereleaseFromFile = string.Empty;

            try
            {
                var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");
                if (File.Exists(versionFile))
                {
                    var text = File.ReadAllLines(versionFile);
                    foreach (var line in text)
                    {
                        var candidate = line.Trim();
                        if (string.IsNullOrEmpty(candidate)) continue;
                        
                        // Handle semver suffixes like "1.4.0-beta2"
                        var versionPart = candidate;
                        var dashIndex = candidate.IndexOf('-');
                        if (dashIndex > 0)
                        {
                            prereleaseFromFile = candidate[dashIndex..]; // e.g., "-beta2"
                            versionPart = candidate.Substring(0, dashIndex); // e.g., "1.4.0"
                        }
                        
                        if (Version.TryParse(versionPart, out var fileVersion))
                        {
                            versionFromFile = fileVersion;
                            _logging.Info($"Loaded version from VERSION.txt: {candidate}");
                            break;
                        }
                    }
                }

                var assembly = Assembly.GetExecutingAssembly();
                var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                var fileVersionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                var assemblyNameVersion = assembly.GetName().Version?.ToString();

                string prereleaseFromFileVersion = string.Empty;
                Version? versionFromAssembly =
                    TryParseVersionString(informationalVersion, out var prereleaseFromInfo) ??
                    TryParseVersionString(fileVersionAttribute, out prereleaseFromFileVersion) ??
                    TryParseVersionString(assemblyNameVersion, out _);

                var assemblyPrerelease = !string.IsNullOrEmpty(prereleaseFromInfo)
                    ? prereleaseFromInfo
                    : prereleaseFromFileVersion;

                if (versionFromFile != null && versionFromAssembly != null)
                {
                    if (versionFromAssembly > versionFromFile)
                    {
                        _currentPrereleaseTag = assemblyPrerelease;
                        _logging.Info($"Using assembly version {versionFromAssembly} over VERSION.txt {versionFromFile}");
                        return versionFromAssembly;
                    }

                    _currentPrereleaseTag = prereleaseFromFile;
                    return versionFromFile;
                }

                if (versionFromFile != null)
                {
                    _currentPrereleaseTag = prereleaseFromFile;
                    return versionFromFile;
                }

                if (versionFromAssembly != null)
                {
                    _currentPrereleaseTag = assemblyPrerelease;
                    return versionFromAssembly;
                }

                return new Version(1, 0, 0);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Falling back to default version: {ex.Message}");
                return new Version(1, 0, 0);
            }
        }

        private static Version? TryParseVersionString(string? versionText, out string prereleaseTag)
        {
            prereleaseTag = string.Empty;
            if (string.IsNullOrWhiteSpace(versionText))
            {
                return null;
            }

            var text = versionText.Trim();
            var plus = text.IndexOf('+');
            if (plus > 0)
            {
                text = text.Substring(0, plus);
            }

            var match = Regex.Match(text, @"(?<ver>\d+\.\d+\.\d+(?:\.\d+)?)(?<pre>-[0-9A-Za-z.-]+)?");
            if (!match.Success)
            {
                return null;
            }

            var ver = match.Groups["ver"].Value;
            var pre = match.Groups["pre"].Value;
            if (!string.IsNullOrEmpty(pre))
            {
                prereleaseTag = pre;
            }

            return Version.TryParse(ver, out var parsed) ? parsed : null;
        }
        
        /// <summary>
        /// Get the current version's prerelease tag (e.g., "-beta2")
        /// </summary>
        private string GetCurrentPrereleaseTag() => _currentPrereleaseTag;
        
        /// <summary>
        /// Check if current version is a prerelease
        /// </summary>
        private bool IsCurrentVersionPrerelease() => !string.IsNullOrEmpty(_currentPrereleaseTag);
        
        /// <summary>
        /// Compare versions with prerelease support.
        /// Returns true if latest version is newer than current.
        /// 
        /// Rules:
        /// - 1.4.0 > 1.3.0 (any release)
        /// - 1.4.0-beta2 > 1.3.0 (release)
        /// - 1.4.0-beta2 > 1.4.0-beta1
        /// - 1.4.0 > 1.4.0-beta2 (release > prerelease of same version)
        /// </summary>
        private bool CompareVersionsWithPrerelease(Version current, string currentPrerelease, Version latest, string latestPrerelease)
        {
            // If base versions are different, simple comparison works
            if (latest > current)
            {
                return true;
            }
            
            if (latest < current)
            {
                return false;
            }
            
            // Base versions are equal - compare prerelease tags
            // Examples: 1.4.0-beta2 vs 1.4.0-beta1, or 1.4.0 vs 1.4.0-beta2
            
            var currentIsPrerelease = !string.IsNullOrEmpty(currentPrerelease);
            var latestIsPrerelease = !string.IsNullOrEmpty(latestPrerelease);
            
            if (!currentIsPrerelease && !latestIsPrerelease)
            {
                // Both are stable releases with same version - no update
                return false;
            }
            
            if (currentIsPrerelease && !latestIsPrerelease)
            {
                // Current is prerelease, latest is stable - UPDATE (stable > prerelease)
                return true;
            }
            
            if (!currentIsPrerelease && latestIsPrerelease)
            {
                // Current is stable, latest is prerelease - don't downgrade to prerelease
                return false;
            }
            
            // Both are prereleases - compare tags (e.g., beta2 > beta1)
            return ComparePrereleaseStrings(currentPrerelease, latestPrerelease) < 0;
        }
        
        /// <summary>
        /// Compare prerelease strings like "-beta1" vs "-beta2"
        /// Returns negative if a < b, zero if equal, positive if a > b
        /// </summary>
        private int ComparePrereleaseStrings(string a, string b)
        {
            // Normalize: strip leading dash
            a = a.TrimStart('-').ToLowerInvariant();
            b = b.TrimStart('-').ToLowerInvariant();
            
            // Extract type and number: "beta2" -> ("beta", 2)
            var (typeA, numA) = ParsePrereleaseTag(a);
            var (typeB, numB) = ParsePrereleaseTag(b);
            
            // Compare types first (alpha < beta < rc)
            var typeComparison = GetPrereleaseTypeOrder(typeA).CompareTo(GetPrereleaseTypeOrder(typeB));
            if (typeComparison != 0)
            {
                return typeComparison;
            }
            
            // Same type, compare numbers
            return numA.CompareTo(numB);
        }
        
        private (string type, int number) ParsePrereleaseTag(string tag)
        {
            // Match patterns like "beta2", "alpha1", "rc3"
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"^([a-z]+)(\d*)$");
            if (match.Success)
            {
                var type = match.Groups[1].Value;
                var numStr = match.Groups[2].Value;
                var num = string.IsNullOrEmpty(numStr) ? 1 : int.Parse(numStr);
                return (type, num);
            }
            return (tag, 1);
        }
        
        private int GetPrereleaseTypeOrder(string type)
        {
            return type switch
            {
                "alpha" => 1,
                "beta" => 2,
                "rc" => 3,
                _ => 0
            };
        }

        /// <summary>
        /// Extract SHA256 hash from release notes for a specific asset file.
        /// Supports multiple formats:
        ///   - "filename.exe:  HASH" (changelog format)
        ///   - "SHA256: HASH" or "SHA-256: HASH" (generic)
        ///   - "**SHA256**: `HASH`" (markdown formatted)
        ///   - Bare 64-char hex on same line as filename
        /// </summary>
        private string? ExtractHashForAsset(string body, string assetFileName)
        {
            if (string.IsNullOrEmpty(body)) return null;
            
            // 1. Try per-asset match: "filename: HASH", "filename:    HASH", or a markdown table
            //    row like "| `filename` | `HASH` |" — the separator between the filename and the
            //    hash can include backticks and a pipe as well as whitespace/colon, since every
            //    release since 3.4.1 has actually shipped hashes as a two-column markdown table
            //    (`| Artifact | SHA256 |`), not the plain "name: hash" line this used to assume.
            //    Confirmed via GitHub #192: this previously matched nothing against real release
            //    notes, so the auto-updater always fell back to "missing SHA256" and refused to
            //    install, even when a valid hash was right there in the table.
            if (!string.IsNullOrEmpty(assetFileName))
            {
                var escapedName = System.Text.RegularExpressions.Regex.Escape(assetFileName);
                var assetMatch = System.Text.RegularExpressions.Regex.Match(
                    body,
                    escapedName + @"[`|:\s]+([a-fA-F0-9]{64})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (assetMatch.Success)
                {
                    _logging.Info($"Extracted SHA256 for asset '{assetFileName}' from release notes");
                    return assetMatch.Groups[1].Value;
                }
            }

            // 2. Try "SHA256: HASH" with optional backticks, bold markers, hyphens, or a pipe
            //    (a markdown table's "| SHA256 |" header cell followed by a row separator)
            var shaMatch = System.Text.RegularExpressions.Regex.Match(
                body,
                @"SHA-?256[:\s*`|]+([a-fA-F0-9]{64})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (shaMatch.Success)
            {
                return shaMatch.Groups[1].Value;
            }
            
            // 3. Fallback: find any 64-char hex string that looks like a hash
            //    (on a line containing "SHA" or "hash" or "checksum")
            var lines = body.Split('\n');
            foreach (var line in lines)
            {
                if (line.IndexOf("sha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("checksum", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var hexMatch = System.Text.RegularExpressions.Regex.Match(line, @"([a-fA-F0-9]{64})");
                    if (hexMatch.Success)
                    {
                        return hexMatch.Groups[1].Value;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Legacy single-hash extraction (kept for backward compatibility)
        /// </summary>
        private string? ExtractHashFromBody(string body)
        {
            return ExtractHashForAsset(body, string.Empty);
        }
        
        /// <summary>
        /// Select the appropriate update asset based on installation type
        /// </summary>
        private JsonElement SelectPlatformAwareAsset(JsonElement assets)
        {
            var byName = new System.Collections.Generic.Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.Length > 0) byName[name] = asset;
            }

            var selected = SelectAssetName(byName.Keys, _installationType);
            if (selected == null)
            {
                _logging.Warn($"No suitable update asset found for installation type {_installationType}.");
                return default;
            }

            _logging.Info($"Selected update asset for {_installationType}: {selected}");
            return byName[selected];
        }

        /// <summary>
        /// Pick the release asset for this installation type, by name only.
        ///
        /// Installer builds get the Setup .exe and never a zip. Portable builds get the WINDOWS zip:
        /// releases also carry a Linux zip, and the old "any .zip" rule took whichever zip came last
        /// in the list. Checksum sidecars (".sha256") never match, since both rules test the extension.
        /// </summary>
        internal static string? SelectAssetName(System.Collections.Generic.IEnumerable<string> names, InstallationType installationType)
        {
            string? installer = null, windowsZip = null, fallbackExe = null;

            foreach (var name in names)
            {
                var n = name.ToLowerInvariant();
                if (n.EndsWith(".exe") && (n.Contains("setup") || n.Contains("installer")))
                {
                    installer ??= name;
                }
                else if (n.EndsWith(".zip") && !n.Contains("source") && !n.Contains("linux"))
                {
                    // Prefer an explicit Windows zip over any other non-Linux zip.
                    if (windowsZip == null || (n.Contains("win") && !windowsZip.ToLowerInvariant().Contains("win")))
                        windowsZip = name;
                }
                else if (n.EndsWith(".exe"))
                {
                    fallbackExe ??= name;
                }
            }

            return installationType switch
            {
                InstallationType.Installer => installer ?? fallbackExe,
                InstallationType.Portable => windowsZip,
                _ => installer ?? windowsZip ?? fallbackExe
            };
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            var order = 0;
            double length = bytes;
            while (length >= 1024 && order < units.Length - 1)
            {
                order++;
                length /= 1024;
            }

            return $"{length:0.##} {units[order]}";
        }
        
        /// <summary>
        /// Dispose resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// Protected dispose implementation.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                BackgroundTimerRegistry.Unregister("AutoUpdateCheck");
                if (_checkTimer != null)
                {
                    _checkTimer.Elapsed -= CheckTimerElapsed;
                }
                _checkTimer?.Stop();
                _checkTimer?.Dispose();
                _scheduledCheckSemaphore.Dispose();
                _downloadSemaphore.Dispose();
                _httpClient?.Dispose();
            }
            
            _disposed = true;
        }
    }
}
