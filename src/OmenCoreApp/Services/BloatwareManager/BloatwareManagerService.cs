using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenCore.Services.BloatwareManager
{
    /// <summary>
    /// Service for detecting and managing bloatware/pre-installed applications.
    /// Provides safe removal and restoration capabilities for gaming laptops.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class BloatwareManagerService : IDisposable
    {
        private readonly LoggingService _logger;
        private readonly string _backupPath;
        private List<BloatwareApp> _detectedApps = new();
        private Dictionary<string, BloatwareBackup> _backups = new();

        public event Action<string>? StatusChanged;
        public event Action<BloatwareApp>? AppRemoved;
        public event Action<BloatwareApp>? AppRestored;

        public IReadOnlyList<BloatwareApp> DetectedApps => _detectedApps.AsReadOnly();

        public BloatwareManagerService(LoggingService logger)
        {
            _logger = logger;
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmenCore", "BloatwareBackups");
            
            Directory.CreateDirectory(_backupPath);
            LoadBackups();
        }

        /// <summary>
        /// Scans the system for known bloatware and pre-installed applications.
        /// </summary>
        public async Task<List<BloatwareApp>> ScanForBloatwareAsync()
        {
            _detectedApps.Clear();
            StatusChanged?.Invoke("Scanning for bloatware...");

            await Task.Run(() =>
            {
                // Scan UWP/AppX packages
                ScanAppxPackages();
                
                // Scan traditional Win32 apps
                ScanWin32Apps();
                
                // Scan startup items
                ScanStartupItems();
                
                // Scan scheduled tasks
                ScanScheduledTasks();
            });

            // Sort by category then name
            _detectedApps = _detectedApps
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToList();

            StatusChanged?.Invoke($"Found {_detectedApps.Count} bloatware items");
            _logger.Info($"Bloatware scan complete: {_detectedApps.Count} items detected");
            
            return _detectedApps;
        }

        private void ScanAppxPackages()
        {
            try
            {
                // Use PowerShell to get AppX packages
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-AppxPackage | Select-Object Name, PackageFullName, Publisher | ConvertTo-Json\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);

                if (string.IsNullOrEmpty(output)) return;

                var packages = JsonSerializer.Deserialize<List<AppxPackageInfo>>(output);
                if (packages == null) return;

                foreach (var pkg in packages)
                {
                    if (pkg.Name == null) continue;
                    if (IsKnownBloatware(pkg.Name, out var category, out var description, out var risk))
                    {
                        _detectedApps.Add(new BloatwareApp
                        {
                            Name = GetFriendlyName(pkg.Name),
                            PackageId = pkg.PackageFullName ?? pkg.Name,
                            Publisher = pkg.Publisher ?? "Unknown",
                            Type = BloatwareType.AppxPackage,
                            Category = category,
                            Description = description,
                            RemovalRisk = risk,
                            CanRestore = true,
                            IsRemoved = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to scan AppX packages: {ex.Message}");
            }
        }

        private void ScanWin32Apps()
        {
            try
            {
                var uninstallKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var keyPath in uninstallKeys)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = subKey.GetValue("DisplayName")?.ToString();
                        var publisher = subKey.GetValue("Publisher")?.ToString();
                        var uninstallString = subKey.GetValue("UninstallString")?.ToString();

                        if (string.IsNullOrEmpty(displayName)) continue;

                        if (IsKnownBloatware(displayName, out var category, out var description, out var risk))
                        {
                            _detectedApps.Add(new BloatwareApp
                            {
                                Name = displayName,
                                PackageId = subKeyName,
                                Publisher = publisher ?? "Unknown",
                                Type = BloatwareType.Win32App,
                                Category = category,
                                Description = description,
                                RemovalRisk = risk,
                                UninstallCommand = uninstallString,
                                CanRestore = false, // Win32 apps generally can't be restored
                                IsRemoved = false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to scan Win32 apps: {ex.Message}");
            }
        }

        private void ScanStartupItems()
        {
            try
            {
                var startupKeys = new[]
                {
                    (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                    (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                    (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")
                };

                foreach (var (hive, keyPath) in startupKeys)
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key == null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        var value = key.GetValue(valueName)?.ToString();
                        if (string.IsNullOrEmpty(value)) continue;

                        if (IsKnownBloatwareStartup(valueName, value, out var category, out var description, out var risk))
                        {
                            _detectedApps.Add(new BloatwareApp
                            {
                                Name = valueName,
                                PackageId = $"{keyPath}\\{valueName}",
                                Publisher = "Startup Item",
                                Type = BloatwareType.StartupItem,
                                Category = category,
                                Description = description,
                                RemovalRisk = risk,
                                CanRestore = true,
                                IsRemoved = false,
                                RegistryPath = keyPath,
                                RegistryHive = hive == Registry.CurrentUser ? "HKCU" : "HKLM"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to scan startup items: {ex.Message}");
            }
        }

        private void ScanScheduledTasks()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo CSV /v",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);

                // Parse CSV output for known bloatware tasks
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var parts = line.Split(',');
                    if (parts.Length < 2) continue;

                    var taskName = parts[1].Trim('"');
                    if (IsKnownBloatwareTask(taskName, out var category, out var description, out var risk))
                    {
                        _detectedApps.Add(new BloatwareApp
                        {
                            Name = Path.GetFileName(taskName),
                            PackageId = taskName,
                            Publisher = "Scheduled Task",
                            Type = BloatwareType.ScheduledTask,
                            Category = category,
                            Description = description,
                            RemovalRisk = risk,
                            CanRestore = true,
                            IsRemoved = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to scan scheduled tasks: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a bloatware application safely.
        /// </summary>
        public async Task<bool> RemoveAppAsync(BloatwareApp app)
        {
            if (app.IsRemoved) return true;

            StatusChanged?.Invoke($"Removing {app.Name}...");
            _logger.Info($"Attempting to remove bloatware: {app.Name} ({app.Type})");

            try
            {
                // Backup before removal if possible
                if (app.CanRestore)
                {
                    await BackupAppAsync(app);
                }

                bool success = app.Type switch
                {
                    BloatwareType.AppxPackage => await RemoveAppxPackageAsync(app),
                    BloatwareType.Win32App => await RemoveWin32AppAsync(app),
                    BloatwareType.StartupItem => await RemoveStartupItemAsync(app),
                    BloatwareType.ScheduledTask => await DisableScheduledTaskAsync(app),
                    _ => false
                };

                if (success)
                {
                    app.IsRemoved = true;
                    AppRemoved?.Invoke(app);
                    StatusChanged?.Invoke($"Successfully removed {app.Name}");
                    _logger.Info($"Successfully removed: {app.Name}");
                }
                else
                {
                    StatusChanged?.Invoke($"Failed to remove {app.Name}");
                    _logger.Warn($"Failed to remove: {app.Name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error removing {app.Name}: {ex.Message}");
                StatusChanged?.Invoke($"Error removing {app.Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RemoveAppxPackageAsync(BloatwareApp app)
        {
            // Strategy: try current-user removal first, then -AllUsers, then provisioned package removal
            // This handles both standard and pre-provisioned (OEM-installed) packages
            //
            // IMPORTANT: Get-AppxPackage positional parameter is -Name (e.g. "Microsoft.BingWeather"),
            // NOT PackageFullName (e.g. "Microsoft.BingWeather_4.53.52220.0_x64__8wekyb3d8bbwe").
            // Extract the Name portion from PackageFullName by splitting on '_'.
            var appxName = app.PackageId.Contains('_') ? app.PackageId.Split('_')[0] : app.PackageId;
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"" +
                    $"try {{ Get-AppxPackage '{appxName}' | Remove-AppxPackage -ErrorAction Stop; exit 0 }} " +
                    $"catch {{ " +
                    $"  try {{ Get-AppxPackage -AllUsers '{appxName}' | Remove-AppxPackage -AllUsers -ErrorAction Stop; exit 0 }} " +
                    $"  catch {{ " +
                    $"    try {{ Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '{appxName}*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction Stop; exit 0 }} " +
                    $"    catch {{ Write-Error $_.Exception.Message; exit 1 }} " +
                    $"  }} " +
                    $"}}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            {
                _logger.Warn($"AppX removal failed for {app.Name}: {stderr.Trim()}");
                
                if (stderr.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("0x80070005", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn($"  → Run OmenCore as Administrator to remove provisioned packages");
                }
            }
            
            return process.ExitCode == 0;
        }

        private async Task<bool> RemoveWin32AppAsync(BloatwareApp app)
        {
            if (string.IsNullOrEmpty(app.UninstallCommand)) return false;

            // Parse uninstall command
            var cmd = app.UninstallCommand;
            var args = "";

            if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                // Add silent flags for MSI
                args = cmd.Replace("msiexec.exe", "").Trim() + " /qn /norestart";
                cmd = "msiexec.exe";
            }
            else if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    args = cmd[(endQuote + 1)..].Trim() + " /S /silent /quiet";
                    cmd = cmd[1..endQuote];
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return true; // Win32 uninstallers have inconsistent exit codes
            }
            catch
            {
                return false;
            }
        }

        private Task<bool> RemoveStartupItemAsync(BloatwareApp app)
        {
            try
            {
                var hive = app.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = hive.OpenSubKey(app.RegistryPath!, true);
                if (key == null) return Task.FromResult(false);

                key.DeleteValue(app.Name, false);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        private async Task<bool> DisableScheduledTaskAsync(BloatwareApp app)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/change /tn \"{app.PackageId}\" /disable",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        /// <summary>
        /// Restores a previously removed application.
        /// </summary>
        public async Task<bool> RestoreAppAsync(BloatwareApp app)
        {
            if (!app.IsRemoved || !app.CanRestore) return false;

            StatusChanged?.Invoke($"Restoring {app.Name}...");
            _logger.Info($"Attempting to restore: {app.Name}");

            try
            {
                if (!_backups.TryGetValue(app.PackageId, out var backup))
                {
                    _logger.Warn($"No backup found for {app.Name}");
                    return false;
                }

                bool success = app.Type switch
                {
                    BloatwareType.AppxPackage => await RestoreAppxPackageAsync(backup),
                    BloatwareType.StartupItem => await RestoreStartupItemAsync(backup),
                    BloatwareType.ScheduledTask => await EnableScheduledTaskAsync(app),
                    _ => false
                };

                if (success)
                {
                    app.IsRemoved = false;
                    AppRestored?.Invoke(app);
                    StatusChanged?.Invoke($"Successfully restored {app.Name}");
                    _logger.Info($"Successfully restored: {app.Name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error restoring {app.Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RestoreAppxPackageAsync(BloatwareBackup backup)
        {
            if (string.IsNullOrEmpty(backup.ManifestPath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Add-AppxPackage -Register '{backup.ManifestPath}' -DisableDevelopmentMode\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        private Task<bool> RestoreStartupItemAsync(BloatwareBackup backup)
        {
            try
            {
                var hive = backup.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = hive.OpenSubKey(backup.RegistryPath!, true);
                if (key == null) return Task.FromResult(false);

                key.SetValue(backup.Name, backup.RegistryValue ?? "");
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        private async Task<bool> EnableScheduledTaskAsync(BloatwareApp app)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/change /tn \"{app.PackageId}\" /enable",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }

        private async Task BackupAppAsync(BloatwareApp app)
        {
            var backup = new BloatwareBackup
            {
                PackageId = app.PackageId,
                Name = app.Name,
                Type = app.Type,
                BackupDate = DateTime.Now
            };

            switch (app.Type)
            {
                case BloatwareType.AppxPackage:
                    // Store manifest location for potential reinstall
                    backup.ManifestPath = await GetAppxManifestPathAsync(app.PackageId);
                    break;
                    
                case BloatwareType.StartupItem:
                    backup.RegistryHive = app.RegistryHive;
                    backup.RegistryPath = app.RegistryPath;
                    var hive = app.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using (var key = hive.OpenSubKey(app.RegistryPath!))
                    {
                        backup.RegistryValue = key?.GetValue(app.Name)?.ToString();
                    }
                    break;
            }

            _backups[app.PackageId] = backup;
            SaveBackups();
        }

        private async Task<string?> GetAppxManifestPathAsync(string packageFullName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-AppxPackage '{packageFullName}').InstallLocation\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var installLocation = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (string.IsNullOrWhiteSpace(installLocation)) return null;

            var manifestPath = Path.Combine(installLocation.Trim(), "AppxManifest.xml");
            return File.Exists(manifestPath) ? manifestPath : null;
        }

        private void LoadBackups()
        {
            try
            {
                var backupFile = Path.Combine(_backupPath, "backups.json");
                if (File.Exists(backupFile))
                {
                    var json = File.ReadAllText(backupFile);
                    _backups = JsonSerializer.Deserialize<Dictionary<string, BloatwareBackup>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load bloatware backups: {ex.Message}");
            }
        }

        private void SaveBackups()
        {
            try
            {
                var backupFile = Path.Combine(_backupPath, "backups.json");
                var json = JsonSerializer.Serialize(_backups, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(backupFile, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save bloatware backups: {ex.Message}");
            }
        }

        #region Known Bloatware Database

        private static bool IsKnownBloatware(string name, out BloatwareCategory category, out string description, out RemovalRisk risk)
        {
            // ═══════════════════════════════════════════════════════════════════════════════════
            // PROTECTED APPS - NEVER flag these as bloatware
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // Exclude OmenCore from bloatware detection - we're not bloatware!
            if (name.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // HP Support Assistant - needed for driver updates and BIOS updates
            if (name.Contains("HPSupportAssistant", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HP Support Assistant", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AD2F1837.HPSupportAssistant", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // NVIDIA drivers and essential components - NEVER remove
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Driver", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("PhysX", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("FrameView", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Control Panel", StringComparison.OrdinalIgnoreCase)))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Intel drivers and essential components - NEVER remove
            if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Driver", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Management Engine", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Rapid Storage", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Network", StringComparison.OrdinalIgnoreCase)))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // AMD drivers - NEVER remove
            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Driver", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Chipset", StringComparison.OrdinalIgnoreCase)))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Realtek audio/network drivers - NEVER remove
            if (name.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Microsoft Visual C++ Redistributables - NEVER remove (games need these)
            if (name.Contains("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("VC_Redist", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("vcredist", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // .NET Framework/Runtime - NEVER remove (apps need these)
            if (name.Contains(".NET", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Runtime", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Framework", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Desktop", StringComparison.OrdinalIgnoreCase)))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // DirectX - NEVER remove
            if (name.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Windows Store and critical Windows components - NEVER remove
            if (name.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Microsoft.StorePurchaseApp", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.NET", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.Windows.Photos", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WindowsCalculator", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WindowsCamera", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WindowsNotepad", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.Paint", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.ScreenSketch", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WebpImageExtension", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.HEIFImageExtension", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.HEVCVideoExtension", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.DesktopAppInstaller", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Xbox Gaming Overlay - NEVER remove (needed for Game Bar, FPS counter, screenshots)
            if (name.Contains("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("GamingOverlay", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("GameOverlay", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("GameBar", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Identity", StringComparison.OrdinalIgnoreCase)))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }

            // ═══════════════════════════════════════════════════════════════════════════════════
            // BLOATWARE DETECTION - Safe to flag/remove
            // ═══════════════════════════════════════════════════════════════════════════════════

            // HP Bloatware
            if (name.Contains("HP Sure", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP security software - can conflict with third-party AV";
                risk = RemovalRisk.Medium;
                return true;
            }
            // Note: HP Support Assistant is explicitly excluded above - don't flag it
            if (name.Contains("HP Customer", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HPPrivacy", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("HPSystemEvent", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP diagnostics/telemetry tool - safe to remove";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("OMEN Gaming Hub", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP OMEN software - OmenCore replaces this";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("HP Audio", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP audio enhancement software";
                risk = RemovalRisk.Medium;
                return true;
            }
            if (name.Contains("HP Command Center", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP system control software - OmenCore replaces this";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("McAfee", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Antivirus;
                description = "Trial antivirus - can impact gaming performance";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Norton", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Antivirus;
                description = "Trial antivirus - can impact gaming performance";
                risk = RemovalRisk.Low;
                return true;
            }

            // Windows Bloatware
            if (name.Contains("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase) && 
                !name.Contains("GamingOverlay") && !name.Contains("GameOverlay"))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Xbox app - keep if you use Xbox Game Pass";
                risk = RemovalRisk.Medium;
                return true;
            }
            if (name.Contains("Clipchamp", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Microsoft video editor";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.BingNews", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.BingWeather", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.BingFinance", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Microsoft Bing news/weather app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.GetHelp", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.Getstarted", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Windows tips and help app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.MicrosoftSolitaireCollection", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Games;
                description = "Microsoft Solitaire with ads";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.ZuneMusic", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.ZuneVideo", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Groove Music / Movies & TV app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.People", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Windows People app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.SkypeApp", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Communication;
                description = "Skype messaging app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.YourPhone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.WindowsPhone", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Phone Link app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Microsoft.MixedReality", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.3DViewer", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Microsoft.Print3D", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "3D/Mixed Reality app - rarely used";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Disney", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Netflix", StringComparison.OrdinalIgnoreCase) && name.Contains("Microsoft"))
            {
                category = BloatwareCategory.Promotional;
                description = "Promotional app shortcut";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("CandyCrush", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("BubbleWitch", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("FarmVille", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("king.com", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Games;
                description = "Pre-installed casual game";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Facebook", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Instagram", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Twitter", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Social;
                description = "Social media app";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Dolby", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "Dolby audio enhancement - may affect audio quality";
                risk = RemovalRisk.Medium;
                return true;
            }

            category = BloatwareCategory.Unknown;
            description = "";
            risk = RemovalRisk.Unknown;
            return false;
        }

        private static bool IsKnownBloatwareStartup(string name, string value, out BloatwareCategory category, out string description, out RemovalRisk risk)
        {
            // ═══════════════════════════════════════════════════════════════════════════════════
            // PROTECTED STARTUP ITEMS - NEVER flag these
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // Exclude OmenCore from bloatware detection
            if (name.Contains("OmenCore", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // NVIDIA - essential for GPU functionality
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Intel - essential for Intel hardware
            if ((name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("Intel", StringComparison.OrdinalIgnoreCase)) &&
                !name.Contains("Intel Driver", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // AMD - essential for AMD hardware
            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("AMD", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Realtek - essential for audio/network
            if (name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Windows Security/Defender - NEVER disable
            if (name.Contains("Windows Security", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }

            // ═══════════════════════════════════════════════════════════════════════════════════
            // BLOATWARE STARTUP DETECTION
            // ═══════════════════════════════════════════════════════════════════════════════════

            if (name.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "OneDrive startup - disable if not using cloud sync";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Cortana", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "Cortana assistant startup";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Teams", StringComparison.OrdinalIgnoreCase) && name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Communication;
                description = "Microsoft Teams startup - disable if not using";
                risk = RemovalRisk.Low;
                return true;
            }
            if ((name.Contains("HP", StringComparison.OrdinalIgnoreCase) || 
                value.Contains("HP", StringComparison.OrdinalIgnoreCase)) &&
                !name.Contains("OmenCore", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP software startup item";
                risk = RemovalRisk.Low;
                return true;
            }
            if (name.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Steam", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Startup;
                description = "App startup - disable if not needed immediately";
                risk = RemovalRisk.Low;
                return true;
            }

            category = BloatwareCategory.Unknown;
            description = "";
            risk = RemovalRisk.Unknown;
            return false;
        }

        private static bool IsKnownBloatwareTask(string taskName, out BloatwareCategory category, out string description, out RemovalRisk risk)
        {
            // ═══════════════════════════════════════════════════════════════════════════════════
            // PROTECTED SCHEDULED TASKS - NEVER flag these
            // ═══════════════════════════════════════════════════════════════════════════════════
            
            // Exclude OmenCore from bloatware detection
            if (taskName.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Windows Defender/Security tasks - NEVER disable
            if (taskName.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("Antimalware", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("MpSigStub", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Windows Update tasks - NEVER disable
            if (taskName.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("UpdateOrchestrator", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("SoftwareProtection", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // Disk cleanup/maintenance - NEVER disable
            if (taskName.Contains("DiskCleanup", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("Defrag", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("SystemRestore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }
            
            // NVIDIA tasks - NEVER disable
            if (taskName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Unknown;
                description = "";
                risk = RemovalRisk.Unknown;
                return false;
            }

            // ═══════════════════════════════════════════════════════════════════════════════════
            // BLOATWARE TASK DETECTION
            // ═══════════════════════════════════════════════════════════════════════════════════

            if (taskName.Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                !taskName.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP scheduled task";
                risk = RemovalRisk.Low;
                return true;
            }
            if (taskName.Contains("Customer Experience", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("CEIP", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("Telemetry", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.Telemetry;
                description = "Telemetry data collection task";
                risk = RemovalRisk.Low;
                return true;
            }
            if (taskName.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.WindowsApps;
                description = "OneDrive scheduled sync task";
                risk = RemovalRisk.Low;
                return true;
            }

            category = BloatwareCategory.Unknown;
            description = "";
            risk = RemovalRisk.Unknown;
            return false;
        }

        private static string GetFriendlyName(string packageName)
        {
            // Convert package names to friendly names
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Microsoft.BingNews", "Bing News" },
                { "Microsoft.BingWeather", "Bing Weather" },
                { "Microsoft.GetHelp", "Get Help" },
                { "Microsoft.Getstarted", "Tips" },
                { "Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection" },
                { "Microsoft.ZuneMusic", "Groove Music" },
                { "Microsoft.ZuneVideo", "Movies & TV" },
                { "Microsoft.People", "People" },
                { "Microsoft.SkypeApp", "Skype" },
                { "Microsoft.YourPhone", "Phone Link" },
                { "Microsoft.3DViewer", "3D Viewer" },
                { "Microsoft.MixedReality.Portal", "Mixed Reality Portal" },
                { "Clipchamp.Clipchamp", "Clipchamp" },
            };

            foreach (var (key, value) in mappings)
            {
                if (packageName.Contains(key, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            // Extract name from package format (e.g., "Microsoft.WindowsCalculator" -> "Windows Calculator")
            var parts = packageName.Split('.');
            if (parts.Length >= 2)
            {
                var name = parts[^1];
                // Add spaces before capital letters
                return System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            }

            return packageName;
        }

        #endregion

        public void Dispose()
        {
            SaveBackups();
            GC.SuppressFinalize(this);
        }
    }

    #region Supporting Types

    public class BloatwareApp
    {
        public string Name { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string Publisher { get; set; } = "";
        public BloatwareType Type { get; set; }
        public BloatwareCategory Category { get; set; }
        public string Description { get; set; } = "";
        public RemovalRisk RemovalRisk { get; set; }
        public bool CanRestore { get; set; }
        public bool IsRemoved { get; set; }
        public string? UninstallCommand { get; set; }
        public string? RegistryPath { get; set; }
        public string? RegistryHive { get; set; }
    }

    public class BloatwareBackup
    {
        public string PackageId { get; set; } = "";
        public string Name { get; set; } = "";
        public BloatwareType Type { get; set; }
        public DateTime BackupDate { get; set; }
        public string? ManifestPath { get; set; }
        public string? RegistryHive { get; set; }
        public string? RegistryPath { get; set; }
        public string? RegistryValue { get; set; }
    }

    public class AppxPackageInfo
    {
        public string? Name { get; set; }
        public string? PackageFullName { get; set; }
        public string? Publisher { get; set; }
    }

    public enum BloatwareType
    {
        AppxPackage,
        Win32App,
        StartupItem,
        ScheduledTask
    }

    public enum BloatwareCategory
    {
        Unknown,
        OemSoftware,
        WindowsApps,
        Antivirus,
        Games,
        Social,
        Communication,
        Promotional,
        Telemetry,
        Startup
    }

    public enum RemovalRisk
    {
        Unknown,
        Low,
        Medium,
        High
    }

    #endregion
}
