using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Handles application auto-update functionality
    /// </summary>
    public class AutoUpdateService
    {
        private readonly LoggingService _logging;
        private readonly HttpClient _httpClient;
        private readonly string _updateCheckUrl;
        private readonly string _downloadDirectory;
        private readonly Version _currentVersion;
        private System.Timers.Timer? _checkTimer;
        private UpdatePreferences? _preferences;
        
        public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;
        public event EventHandler<UpdateDownloadProgress>? DownloadProgressChanged;
        public event EventHandler<UpdateInstallResult>? InstallCompleted;
        
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
            _downloadDirectory = Path.Combine(Path.GetTempPath(), "OmenCore", "Updates");
            Directory.CreateDirectory(_downloadDirectory);
            _currentVersion = LoadCurrentVersion();
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
            
            if (preferences.AutoCheckEnabled && preferences.CheckIntervalHours > 0)
            {
                var intervalMs = TimeSpan.FromHours(preferences.CheckIntervalHours).TotalMilliseconds;
                _checkTimer = new System.Timers.Timer(intervalMs);
                _checkTimer.Elapsed += async (s, e) => await OnTimerCheckAsync();
                _checkTimer.AutoReset = true;
                _checkTimer.Start();
                
                _logging.Info($"Background update checks enabled (every {preferences.CheckIntervalHours}h)");
            }
            else
            {
                _logging.Info("Background update checks disabled");
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
                _logging.Error("Scheduled update check failed", ex);
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
                
                // TODO: Replace with actual update server API call
                // For now, this is a stub implementation
                var response = await _httpClient.GetAsync(_updateCheckUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    result.Status = UpdateStatus.CheckFailed;
                    result.Message = $"Update check failed: HTTP {response.StatusCode}";
                    _logging.Warn(result.Message);
                    return result;
                }
                
                var jsonContent = await response.Content.ReadAsStringAsync();
                
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
                
                // Parse version from tag (e.g., "v1.0.1" -> "1.0.1")
                var versionString = tagName.TrimStart('v');
                if (!Version.TryParse(versionString, out var latestVersion))
                {
                    result.Status = UpdateStatus.CheckFailed;
                    result.Message = $"Invalid version format: {tagName}";
                    return result;
                }
                
                // Check if newer version available
                if (latestVersion > _currentVersion)
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

                    // Try to extract SHA256 hash from release notes
                    result.LatestVersion.Sha256Hash = ExtractHashFromBody(result.LatestVersion.ReleaseNotes) ?? string.Empty;
                    
                    // Get download URL from assets
                    if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        JsonElement selectedAsset = default;
                        var foundAsset = false;
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString() ?? string.Empty;
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedAsset = asset;
                                foundAsset = true;
                                break;
                            }

                            if (!foundAsset && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                selectedAsset = asset;
                                foundAsset = true;
                            }
                        }

                        if (foundAsset)
                        {
                            result.LatestVersion.DownloadUrl = selectedAsset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                            var size = selectedAsset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
                            result.LatestVersion.FileSize = size;
                            result.LatestVersion.FileSizeFormatted = FormatFileSize(size);
                        }
                    }
                    
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
                _logging.Error("Update check failed", ex);
            }
            catch (Exception ex)
            {
                result.Status = UpdateStatus.CheckFailed;
                result.Message = $"Update check failed: {ex.Message}";
                _logging.Error("Update check failed", ex);
            }
            
            UpdateCheckCompleted?.Invoke(this, result);
            return result;
        }
        
        /// <summary>
        /// Download an update package
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(VersionInfo versionInfo, CancellationToken cancellationToken = default)
        {
            try
            {
                _logging.Info($"Downloading update {versionInfo.VersionString}...");
                if (string.IsNullOrWhiteSpace(versionInfo.DownloadUrl))
                {
                    _logging.Warn("No download URL provided for the requested update.");
                    return null;
                }
                
                var fileName = $"OmenCore-{versionInfo.VersionString}-Setup.exe";
                var downloadPath = Path.Combine(_downloadDirectory, fileName);
                
                // Delete existing file if present
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                
                using var response = await _httpClient.GetAsync(versionInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                var buffer = new byte[8192];
                var stopwatch = Stopwatch.StartNew();
                
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
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
                
                // Verify file hash (preferred for security)
                if (string.IsNullOrWhiteSpace(versionInfo.Sha256Hash))
                {
                    _logging.Warn("Update skipped: Release missing SHA256 hash. Require manual download/validation.");
                    if (File.Exists(downloadPath))
                    {
                        File.Delete(downloadPath);
                    }
                    return null;
                }
                
                var computedHash = ComputeSha256Hash(downloadPath);
                if (!computedHash.Equals(versionInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    _logging.Error($"SHA256 verification failed! Expected: {versionInfo.Sha256Hash}, Computed: {computedHash}");
                    File.Delete(downloadPath);
                    throw new System.Security.SecurityException($"Update package failed SHA256 verification. File may be corrupted or tampered with.");
                }
                
                _logging.Info($"✅ Update verified successfully (SHA256: {computedHash.Substring(0, 16)}...)");
                _logging.Info($"Update downloaded successfully: {downloadPath}");
                return downloadPath;
            }
            catch (Exception ex)
            {
                _logging.Error("Update download failed", ex);
                return null;
            }
        }
        
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
                
                _logging.Info($"Installing update from {installerPath}...");
                
                // Launch installer with silent/quiet flags
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
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
                _logging.Error("Update installation failed", ex);
            }
            
            InstallCompleted?.Invoke(this, result);
            return result;
        }
        
        /// <summary>
        /// Enable or disable automatic update checking on startup
        /// </summary>
        public void SetAutoUpdateEnabled(bool enabled)
        {
            // TODO: Store preference in config
            _logging.Info($"Auto-update {(enabled ? "enabled" : "disabled")}");
        }
        
        /// <summary>
        /// Get the current version of the application
        /// </summary>
        public Version GetCurrentVersion() => _currentVersion;
        
        private string ComputeSha256Hash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        
        public void Dispose()
        {
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            _httpClient?.Dispose();
        }

        private Version LoadCurrentVersion()
        {
            try
            {
                var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");
                if (File.Exists(versionFile))
                {
                    var text = File.ReadAllLines(versionFile);
                    foreach (var line in text)
                    {
                        var candidate = line.Trim();
                        if (Version.TryParse(candidate, out var fileVersion))
                        {
                            return fileVersion;
                        }
                    }
                }

                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                return assemblyVersion ?? new Version(1, 0, 0);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Falling back to default version: {ex.Message}");
                return new Version(1, 0, 0);
            }
        }

        private string? ExtractHashFromBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            
            // Look for SHA256: [hex] or SHA-256: [hex]
            // Matches 64-character hex string
            var match = System.Text.RegularExpressions.Regex.Match(body, @"SHA-?256:\s*([a-fA-F0-9]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
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
    }
}
