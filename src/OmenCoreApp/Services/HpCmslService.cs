using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// HP Client Management Script Library (CMSL) integration service.
    /// Provides BIOS update checking and safe update orchestration using HP's official tools.
    /// 
    /// This acts as an orchestrator - it doesn't flash BIOS directly but:
    /// 1. Detects current BIOS version
    /// 2. Queries HP for available updates
    /// 3. Downloads the appropriate SoftPaq
    /// 4. Launches HP's official updater
    /// 
    /// This approach is safe because HP's updater handles all the dangerous parts.
    /// </summary>
    public class HpCmslService : IDisposable
    {
        private readonly LoggingService? _logging;
        private bool _disposed;
        private bool _cmslAvailable;
        private string _cmslStatus = "Not checked";
        
        private const string CMSL_DOWNLOAD_URL = "https://www.hp.com/us-en/solutions/client-management-solutions/download.html";
        private const string HP_FTP_BASE = "https://ftp.hp.com/pub/softpaq/";
        
        public bool IsCmslAvailable => _cmslAvailable;
        public string CmslStatus => _cmslStatus;

        /// <summary>
        /// BIOS update information.
        /// </summary>
        public class BiosUpdateInfo
        {
            public string CurrentVersion { get; set; } = "";
            public string LatestVersion { get; set; } = "";
            public bool UpdateAvailable { get; set; }
            public string SoftPaqId { get; set; } = "";
            public string SoftPaqName { get; set; } = "";
            public string SoftPaqUrl { get; set; } = "";
            public string ReleaseDate { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
            public long SizeBytes { get; set; }
            public string Category { get; set; } = "BIOS";
        }

        /// <summary>
        /// Device information from HP CMSL.
        /// </summary>
        public class HpDeviceInfo
        {
            public string ProductId { get; set; } = "";
            public string SerialNumber { get; set; } = "";
            public string Model { get; set; } = "";
            public string BiosVersion { get; set; } = "";
            public string Manufacturer { get; set; } = "";
        }

        public HpCmslService(LoggingService? logging = null)
        {
            _logging = logging;
            CheckCmslAvailability();
        }

        /// <summary>
        /// Run a PowerShell command and return the output.
        /// </summary>
        private string? RunPowerShellCommand(string command, int timeoutMs = 30000)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                process.WaitForExit(timeoutMs);
                
                if (!string.IsNullOrEmpty(error))
                {
                    _logging?.Warn($"PowerShell error: {error}");
                }
                
                return output?.Trim();
            }
            catch (Exception ex)
            {
                _logging?.Error($"PowerShell command failed: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Check if HP CMSL PowerShell modules are available.
        /// </summary>
        private void CheckCmslAvailability()
        {
            try
            {
                var result = RunPowerShellCommand("Get-Module -ListAvailable HPCMSL | Select-Object -First 1 -ExpandProperty Name");
                
                _cmslAvailable = !string.IsNullOrEmpty(result) && result.Contains("HPCMSL");
                if (_cmslAvailable)
                {
                    _cmslStatus = "HP CMSL available";
                    _logging?.Info("✓ HP Client Management Script Library available");
                }
                else
                {
                    _cmslStatus = "HP CMSL not installed";
                    _logging?.Info("HP CMSL not installed - BIOS updates will use fallback method");
                }
            }
            catch (Exception ex)
            {
                _cmslAvailable = false;
                _cmslStatus = $"CMSL check failed: {ex.Message}";
                _logging?.Warn($"Could not check HP CMSL: {ex.Message}");
            }
        }

        /// <summary>
        /// Get device information using HP CMSL or WMI fallback.
        /// </summary>
        public async Task<HpDeviceInfo> GetDeviceInfoAsync()
        {
            var info = new HpDeviceInfo();
            
            if (_cmslAvailable)
            {
                info = await GetDeviceInfoCmslAsync();
            }
            
            // Fallback to WMI if CMSL not available or failed
            if (string.IsNullOrEmpty(info.ProductId))
            {
                info = GetDeviceInfoWmi();
            }
            
            return info;
        }

        private async Task<HpDeviceInfo> GetDeviceInfoCmslAsync()
        {
            return await Task.Run(() =>
            {
                var info = new HpDeviceInfo();
                
                try
                {
                    // Batch all CMSL queries into single PowerShell process to reduce startup time
                    // (previously 5 separate processes = 5-10s, now single process = 1-2s)
                    var batchScript = @"
Import-Module HPCMSL -ErrorAction SilentlyContinue
$result = @{
    ProductId = try { Get-HPDeviceProductID } catch { '' }
    SerialNumber = try { Get-HPDeviceSerialNumber } catch { '' }
    Model = try { Get-HPDeviceModel } catch { '' }
    BiosVersion = try { Get-HPBIOSVersion } catch { '' }
    Manufacturer = try { Get-HPDeviceManufacturer } catch { '' }
}
$result | ConvertTo-Json -Compress
";
                    
                    var jsonResult = RunPowerShellCommand(batchScript);
                    
                    if (!string.IsNullOrEmpty(jsonResult) && jsonResult.StartsWith("{"))
                    {
                        // Parse JSON result
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonResult);
                        if (parsed != null)
                        {
                            info.ProductId = parsed.GetValueOrDefault("ProductId") ?? "";
                            info.SerialNumber = parsed.GetValueOrDefault("SerialNumber") ?? "";
                            info.Model = parsed.GetValueOrDefault("Model") ?? "";
                            info.BiosVersion = parsed.GetValueOrDefault("BiosVersion") ?? "";
                            info.Manufacturer = parsed.GetValueOrDefault("Manufacturer") ?? "";
                            _logging?.Info("Got device info via batched CMSL query");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"Failed to get device info via CMSL: {ex.Message}", ex);
                }
                
                return info;
            });
        }

        private HpDeviceInfo GetDeviceInfoWmi()
        {
            var info = new HpDeviceInfo();
            
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                foreach (var obj in searcher.Get())
                {
                    info.ProductId = obj["Product"]?.ToString() ?? "";
                    info.Manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                }
                
                using var sysSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (var obj in sysSearcher.Get())
                {
                    info.Model = obj["Model"]?.ToString() ?? "";
                }
                
                using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
                foreach (var obj in biosSearcher.Get())
                {
                    info.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";
                    info.SerialNumber = obj["SerialNumber"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to get device info via WMI: {ex.Message}", ex);
            }
            
            return info;
        }

        /// <summary>
        /// Check for available BIOS updates.
        /// </summary>
        public async Task<BiosUpdateInfo?> CheckForBiosUpdatesAsync()
        {
            var deviceInfo = await GetDeviceInfoAsync();
            if (string.IsNullOrEmpty(deviceInfo.ProductId))
            {
                _logging?.Warn("Cannot check for updates: Device Product ID not found");
                return null;
            }
            
            _logging?.Info($"Checking for BIOS updates for {deviceInfo.Model} ({deviceInfo.ProductId})...");
            _logging?.Info($"Current BIOS version: {deviceInfo.BiosVersion}");
            
            BiosUpdateInfo? update = null;
            
            if (_cmslAvailable)
            {
                update = await CheckBiosUpdatesCmslAsync(deviceInfo);
            }
            
            // Fallback to HTTP API if CMSL not available
            if (update == null)
            {
                update = await CheckBiosUpdatesHttpAsync(deviceInfo);
            }
            
            if (update != null)
            {
                update.CurrentVersion = deviceInfo.BiosVersion;
                
                if (update.UpdateAvailable)
                {
                    _logging?.Info($"✓ BIOS update available: {update.CurrentVersion} → {update.LatestVersion}");
                    _logging?.Info($"  SoftPaq: {update.SoftPaqId} - {update.SoftPaqName}");
                }
                else
                {
                    _logging?.Info("BIOS is up to date");
                }
            }
            
            return update;
        }

        private async Task<BiosUpdateInfo?> CheckBiosUpdatesCmslAsync(HpDeviceInfo deviceInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Get BIOS updates list from CMSL
                    var script = $@"
Import-Module HPCMSL -ErrorAction SilentlyContinue
$updates = Get-HPBIOSUpdates -Platform '{deviceInfo.ProductId}' -ErrorAction SilentlyContinue
if ($updates) {{
    $latest = $updates | Sort-Object -Property Ver -Descending | Select-Object -First 1
    $latest | Select-Object id, Name, Ver, url, ReleaseDate, Size | ConvertTo-Json
}}
";
                    var result = RunPowerShellCommand(script, 60000);
                    
                    if (!string.IsNullOrEmpty(result) && result.StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(result);
                        var root = doc.RootElement;
                        
                        var latestVersion = root.GetProperty("Ver").GetString() ?? "";
                        return new BiosUpdateInfo
                        {
                            LatestVersion = latestVersion,
                            SoftPaqId = root.GetProperty("id").GetString() ?? "",
                            SoftPaqName = root.GetProperty("Name").GetString() ?? "",
                            SoftPaqUrl = root.GetProperty("url").GetString() ?? "",
                            ReleaseDate = root.GetProperty("ReleaseDate").GetString() ?? "",
                            SizeBytes = root.TryGetProperty("Size", out var size) ? size.GetInt64() : 0,
                            UpdateAvailable = !string.IsNullOrEmpty(latestVersion) && 
                                             latestVersion != deviceInfo.BiosVersion
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"CMSL BIOS check failed: {ex.Message}", ex);
                }
                
                return null;
            });
        }

        private Task<BiosUpdateInfo?> CheckBiosUpdatesHttpAsync(HpDeviceInfo deviceInfo)
        {
            try
            {
                // Construct a support URL the user can visit
                var supportUrl = $"https://support.hp.com/drivers?serialnumber={deviceInfo.SerialNumber}";
                
                _logging?.Info($"HP Support URL: {supportUrl}");
                
                return Task.FromResult<BiosUpdateInfo?>(new BiosUpdateInfo
                {
                    CurrentVersion = deviceInfo.BiosVersion,
                    LatestVersion = "Check HP Support",
                    UpdateAvailable = false, // Can't determine without API access
                    SoftPaqUrl = supportUrl,
                    SoftPaqName = "Check HP Support for updates"
                });
            }
            catch (Exception ex)
            {
                _logging?.Error($"HTTP BIOS check failed: {ex.Message}", ex);
                return Task.FromResult<BiosUpdateInfo?>(null);
            }
        }

        /// <summary>
        /// Download a SoftPaq update package.
        /// </summary>
        public async Task<string?> DownloadSoftPaqAsync(BiosUpdateInfo update, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(update.SoftPaqUrl))
            {
                _logging?.Warn("No SoftPaq URL available");
                return null;
            }
            
            var downloadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmenCore", "Downloads"
            );
            Directory.CreateDirectory(downloadPath);
            
            var fileName = $"{update.SoftPaqId}.exe";
            var filePath = Path.Combine(downloadPath, fileName);
            
            // Check if already downloaded
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == update.SizeBytes || update.SizeBytes == 0)
                {
                    _logging?.Info($"SoftPaq already downloaded: {filePath}");
                    return filePath;
                }
            }
            
            if (_cmslAvailable)
            {
                return await DownloadSoftPaqCmslAsync(update, downloadPath, progress);
            }
            else
            {
                return await DownloadSoftPaqHttpAsync(update, filePath, progress);
            }
        }

        private async Task<string?> DownloadSoftPaqCmslAsync(BiosUpdateInfo update, string downloadPath, IProgress<int>? progress)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var script = $"Import-Module HPCMSL -ErrorAction SilentlyContinue; Get-HPSoftpaq -Number '{update.SoftPaqId}' -SaveAs '{downloadPath}\\{update.SoftPaqId}.exe' -Overwrite";
                    var result = RunPowerShellCommand(script, 300000); // 5 minute timeout for download
                    
                    var filePath = Path.Combine(downloadPath, $"{update.SoftPaqId}.exe");
                    if (File.Exists(filePath))
                    {
                        _logging?.Info($"SoftPaq downloaded: {filePath}");
                        progress?.Report(100);
                        return filePath;
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"CMSL download failed: {ex.Message}", ex);
                }
                
                return null;
            });
        }

        private async Task<string?> DownloadSoftPaqHttpAsync(BiosUpdateInfo update, string filePath, IProgress<int>? progress)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "OmenCore/1.1.1");
                client.Timeout = TimeSpan.FromMinutes(30); // BIOS files can be large
                
                using var response = await client.GetAsync(update.SoftPaqUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? update.SizeBytes;
                
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(filePath);
                
                var buffer = new byte[81920];
                var bytesRead = 0L;
                int read;
                
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                    
                    if (totalBytes > 0)
                    {
                        var percent = (int)((bytesRead * 100) / totalBytes);
                        progress?.Report(percent);
                    }
                }
                
                _logging?.Info($"SoftPaq downloaded: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                _logging?.Error($"HTTP download failed: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Launch the downloaded SoftPaq installer.
        /// The HP installer handles all the actual BIOS flashing safely.
        /// </summary>
        public bool LaunchSoftPaqInstaller(string softPaqPath, bool silent = false)
        {
            if (!File.Exists(softPaqPath))
            {
                _logging?.Error($"SoftPaq file not found: {softPaqPath}");
                return false;
            }
            
            try
            {
                _logging?.Info($"Launching HP BIOS updater: {softPaqPath}");
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = softPaqPath,
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                };
                
                if (silent)
                {
                    // HP SoftPaq silent install flags
                    startInfo.Arguments = "/s /e /f \"" + Path.GetDirectoryName(softPaqPath) + "\"";
                }
                
                Process.Start(startInfo);
                
                _logging?.Info("BIOS updater launched successfully");
                _logging?.Warn("⚠️ IMPORTANT: Do not interrupt the BIOS update process!");
                _logging?.Warn("⚠️ Ensure laptop is connected to power and do not shut down until complete.");
                
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to launch SoftPaq installer: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Install HP CMSL from PowerShell Gallery.
        /// </summary>
        public async Task<bool> InstallCmslAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logging?.Info("Installing HP Client Management Script Library...");
                    
                    var script = @"
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
Install-Module -Name HPCMSL -Force -Scope CurrentUser -AcceptLicense
";
                    var result = RunPowerShellCommand(script, 120000); // 2 minute timeout
                    
                    // Re-check availability
                    CheckCmslAvailability();
                    
                    if (_cmslAvailable)
                    {
                        _logging?.Info("✓ HP CMSL installed successfully");
                        return true;
                    }
                    else
                    {
                        _logging?.Warn("HP CMSL installation may have failed");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"Failed to install CMSL: {ex.Message}", ex);
                    return false;
                }
            });
        }

        /// <summary>
        /// Open HP Support page for manual BIOS download.
        /// </summary>
        public void OpenHpSupportPage(string serialNumber)
        {
            var url = $"https://support.hp.com/drivers?serialnumber={serialNumber}";
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                
                _logging?.Info($"Opened HP Support page: {url}");
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to open HP Support: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Open HP CMSL download page.
        /// </summary>
        public void OpenCmslDownloadPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = CMSL_DOWNLOAD_URL,
                    UseShellExecute = true
                });
                
                _logging?.Info($"Opened HP CMSL download page: {CMSL_DOWNLOAD_URL}");
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to open CMSL page: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
