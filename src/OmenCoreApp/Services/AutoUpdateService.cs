using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
        
        // Current version of the application
        private static readonly Version CurrentVersion = new Version(1, 0, 0);
        
        public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;
        public event EventHandler<UpdateDownloadProgress>? DownloadProgressChanged;
        public event EventHandler<UpdateInstallResult>? InstallCompleted;
        
        public AutoUpdateService(LoggingService logging, string updateCheckUrl = "https://api.github.com/repos/yourusername/omencore/releases/latest")
        {
            _logging = logging;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"OmenCore/{CurrentVersion}");
            
            _updateCheckUrl = updateCheckUrl;
            _downloadDirectory = Path.Combine(Path.GetTempPath(), "OmenCore", "Updates");
            Directory.CreateDirectory(_downloadDirectory);
        }
        
        /// <summary>
        /// Check for available updates from the update server
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = new VersionInfo { Version = CurrentVersion }
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
                if (latestVersion > CurrentVersion)
                {
                    result.UpdateAvailable = true;
                    result.Status = UpdateStatus.UpdateAvailable;
                    result.LatestVersion = new VersionInfo
                    {
                        Version = latestVersion,
                        ReleaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""
                    };
                    
                    // Get download URL from assets
                    if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var firstAsset = assets[0];
                        result.LatestVersion.DownloadUrl = firstAsset.GetProperty("browser_download_url").GetString() ?? "";
                        result.LatestVersion.FileSize = firstAsset.TryGetProperty("size", out var size) ? size.GetInt64() : 0;
                    }
                    
                    result.Message = $"Update available: v{latestVersion}";
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
                
                // Verify file hash if provided
                if (!string.IsNullOrEmpty(versionInfo.Sha256Hash))
                {
                    var computedHash = ComputeSha256Hash(downloadPath);
                    if (!computedHash.Equals(versionInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        _logging.Error("Downloaded file hash mismatch!");
                        File.Delete(downloadPath);
                        return null;
                    }
                }
                
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
        public Version GetCurrentVersion() => CurrentVersion;
        
        private string ComputeSha256Hash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
