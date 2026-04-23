using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
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
        private const int ExternalOperationTimeoutMs = 45000;
        private readonly LoggingService _logger;
        private readonly SystemRestoreService _systemRestoreService;
        private readonly string _backupPath;
        private readonly string _historyFilePath;
        private readonly SemaphoreSlim _restorePointLock = new(1, 1);
        private List<BloatwareApp> _detectedApps = new();
        private Dictionary<string, BloatwareBackup> _backups = new();
        private List<BloatwareSignature> _dynamicSignatures = new();
        private List<BloatwareHistoryEntry> _historyEntries = new();
        private DateTime? _lastRemovalRestorePointCreatedAt;
        private string? _lastRemovalRestorePointDescription;
        private string? _lastRemovalRestorePointMessage;
        private uint? _lastRemovalRestorePointSequence;

        public event Action<string>? StatusChanged;
        public event Action<BloatwareApp>? AppRemoved;
        public event Action<BloatwareApp>? AppRestored;

        public IReadOnlyList<BloatwareApp> DetectedApps => _detectedApps.AsReadOnly();

        /// <summary>
        /// True when OmenCore is running with Administrator privileges.
        /// Bloatware removal requires admin rights.
        /// </summary>
        public static bool IsRunningAsAdmin
        {
            get
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public BloatwareManagerService(LoggingService logger)
        {
            _logger = logger;
            _systemRestoreService = new SystemRestoreService(logger);
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmenCore", "BloatwareBackups");
            _historyFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmenCore", "Logs", "bloatware-history.json");
            
            Directory.CreateDirectory(_backupPath);
            LoadBackups();
            LoadBloatwareSignatures();
            LoadHistory();
        }

        /// <summary>
        /// Creates a system restore point before destructive bloatware-removal operations.
        /// Reuses a recent restore point created by this service in the same session.
        /// </summary>
        public async Task<PreRemovalRestorePointResult> EnsurePreRemovalRestorePointAsync(
            int plannedItemCount,
            CancellationToken cancellationToken = default)
        {
            if (!IsRunningAsAdmin)
            {
                return PreRemovalRestorePointResult.Failed("Administrator privileges are required to create a restore point.");
            }

            await _restorePointLock.WaitAsync(cancellationToken);
            try
            {
                if (_lastRemovalRestorePointCreatedAt.HasValue &&
                    DateTime.Now - _lastRemovalRestorePointCreatedAt.Value <= TimeSpan.FromMinutes(10))
                {
                    return PreRemovalRestorePointResult.Reused(
                        _lastRemovalRestorePointCreatedAt.Value,
                        _lastRemovalRestorePointDescription,
                        _lastRemovalRestorePointSequence,
                        _lastRemovalRestorePointMessage);
                }

                var safeCount = Math.Max(1, plannedItemCount);
                var description = $"OmenCore: Before bloatware removal ({safeCount} item(s))";
                StatusChanged?.Invoke("Creating pre-removal system restore point...");

                var restore = await _systemRestoreService.CreateRestorePointAsync(description, cancellationToken);
                if (!restore.Success)
                {
                    var failure = string.IsNullOrWhiteSpace(restore.Message)
                        ? "Unknown restore point failure"
                        : restore.Message;
                    _logger.Warn($"Pre-removal restore point creation failed: {failure}");
                    return PreRemovalRestorePointResult.Failed(failure);
                }

                _lastRemovalRestorePointCreatedAt = DateTime.Now;
                _lastRemovalRestorePointDescription = description;
                _lastRemovalRestorePointSequence = restore.SequenceNumber;
                _lastRemovalRestorePointMessage = restore.Message;

                _logger.Info($"Pre-removal restore point created for {safeCount} app(s): {description}");
                return PreRemovalRestorePointResult.Created(
                    _lastRemovalRestorePointCreatedAt.Value,
                    description,
                    restore.SequenceNumber,
                    restore.Message);
            }
            catch (OperationCanceledException)
            {
                return PreRemovalRestorePointResult.Failed("Restore point creation was canceled.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create pre-removal restore point: {ex.Message}");
                return PreRemovalRestorePointResult.Failed(ex.Message);
            }
            finally
            {
                _restorePointLock.Release();
            }
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
                    var matchedDynamic = TryMatchConfiguredBloatware(pkg.Name, out var category, out var description, out var risk, out var friendlyName);
                    if (matchedDynamic || IsKnownBloatware(pkg.Name, out category, out description, out risk))
                    {
                        _detectedApps.Add(new BloatwareApp
                        {
                            Name = friendlyName ?? GetFriendlyName(pkg.Name),
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

                        var matchedDynamic = TryMatchConfiguredBloatware(displayName, out var category, out var description, out var risk, out var friendlyName);
                        if (matchedDynamic || IsKnownBloatware(displayName, out category, out description, out risk))
                        {
                            _detectedApps.Add(new BloatwareApp
                            {
                                Name = friendlyName ?? displayName,
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

                ScanStartupFolders();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to scan startup items: {ex.Message}");
            }
        }

        private void ScanStartupFolders()
        {
            var startupFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            foreach (var folder in startupFolders.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
            {
                foreach (var filePath in Directory.EnumerateFiles(folder))
                {
                    var startupName = Path.GetFileNameWithoutExtension(filePath);
                    if (!IsKnownBloatwareStartup(startupName, filePath, out var category, out var description, out var risk))
                    {
                        continue;
                    }

                    _detectedApps.Add(new BloatwareApp
                    {
                        Name = startupName,
                        PackageId = filePath,
                        Publisher = "Startup Folder",
                        Type = BloatwareType.StartupItem,
                        Category = category,
                        Description = description,
                        RemovalRisk = risk,
                        CanRestore = true,
                        IsRemoved = false,
                        StartupFilePath = filePath
                    });
                }
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
                            Name = GetFriendlyTaskName(taskName),
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
        public async Task<bool> RemoveAppAsync(BloatwareApp app, CancellationToken cancellationToken = default)
        {
            if (app.IsRemoved)
            {
                const string alreadyRemoved = "Item was already removed in this session.";
                SetRemovalOutcome(app, RemovalStatus.Skipped, alreadyRemoved, alreadyRemoved);
                StatusChanged?.Invoke($"Skipped {app.Name}: {alreadyRemoved}");
                RecordHistory(app, "remove", true, alreadyRemoved);
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsRunningAsAdmin)
            {
                const string adminRequired = "Administrator privileges are required to remove applications.";
                SetRemovalOutcome(app, RemovalStatus.Failed, adminRequired, adminRequired);
                StatusChanged?.Invoke("Administrator privileges required. Please restart OmenCore as Administrator to remove applications.");
                _logger.Warn($"Bloatware removal attempted without Administrator rights: {app.Name}");
                RecordHistory(app, "remove", false, adminRequired);
                return false;
            }

            StatusChanged?.Invoke($"Removing {app.Name}...");
            _logger.Info($"Attempting to remove bloatware: {app.Name} ({app.Type})");

            SetRemovalOutcome(app, RemovalStatus.Pending, $"Removal started for {app.Name}.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Backup before removal if possible
                if (app.CanRestore)
                {
                    await BackupAppAsync(app);
                }

                cancellationToken.ThrowIfCancellationRequested();

                bool success = app.Type switch
                {
                    BloatwareType.AppxPackage => await RemoveAppxPackageAsync(app, cancellationToken),
                    BloatwareType.Win32App => await RemoveWin32AppAsync(app, cancellationToken),
                    BloatwareType.StartupItem => await RemoveStartupItemAsync(app, cancellationToken),
                    BloatwareType.ScheduledTask => await DisableScheduledTaskAsync(app, cancellationToken),
                    _ => false
                };

                if (success)
                {
                    if (app.LastRemovalStatus == RemovalStatus.Skipped)
                    {
                        var skipDetail = string.IsNullOrWhiteSpace(app.LastRemovalDetail)
                            ? "Removal was skipped (no state change required)."
                            : app.LastRemovalDetail!;
                        SetRemovalOutcome(app, RemovalStatus.Skipped, skipDetail, skipDetail);
                        app.IsRemoved = true;
                        AppRemoved?.Invoke(app);
                        StatusChanged?.Invoke($"Skipped {app.Name}: {skipDetail}");
                        RecordHistory(app, "remove", true, skipDetail);
                        _logger.Info($"Skipped removal for {app.Name}: {skipDetail}");
                        return true;
                    }

                    var successDetail = string.IsNullOrWhiteSpace(app.LastRemovalDetail)
                        ? "Removal completed and verification confirmed the item is no longer detected."
                        : app.LastRemovalDetail!;
                    SetRemovalOutcome(app, RemovalStatus.VerifiedSuccess, successDetail);
                    app.IsRemoved = true;
                    AppRemoved?.Invoke(app);
                    StatusChanged?.Invoke($"Removed {app.Name}: {successDetail}");
                    RecordHistory(app, "remove", true, successDetail);
                    _logger.Info($"Successfully removed: {app.Name}");
                }
                else
                {
                    var failureDetail = string.IsNullOrWhiteSpace(app.LastRemovalDetail)
                        ? (!string.IsNullOrWhiteSpace(app.LastFailureReason)
                            ? app.LastFailureReason!
                            : "Removal command completed but post-state verification detected the item is still present.")
                        : app.LastRemovalDetail!;
                    var failureReason = string.IsNullOrWhiteSpace(app.LastFailureReason)
                        ? failureDetail
                        : app.LastFailureReason!;
                    SetRemovalOutcome(app, RemovalStatus.Failed, failureDetail, failureReason);
                    StatusChanged?.Invoke($"Failed to remove {app.Name}: {failureDetail}");
                    RecordHistory(app, "remove", false, failureDetail);
                    _logger.Warn($"Failed to remove {app.Name}: {failureDetail}");
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                const string canceled = "Canceled before removal completed.";
                SetRemovalOutcome(app, RemovalStatus.Skipped, canceled, canceled);
                StatusChanged?.Invoke($"Skipped {app.Name}: {canceled}");
                RecordHistory(app, "remove", false, canceled);
                _logger.Warn($"Removal canceled for {app.Name}");
                throw;
            }
            catch (Exception ex)
            {
                SetRemovalOutcome(app, RemovalStatus.Failed, ex.Message, ex.Message);
                _logger.Error($"Error removing {app.Name}: {ex.Message}");
                StatusChanged?.Invoke($"Error removing {app.Name}: {ex.Message}");
                RecordHistory(app, "remove", false, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Removes apps one-by-one and rolls back previously removed restorable apps when a failure occurs.
        /// </summary>
        public async Task<BulkRemovalResult> RemoveAppsWithRollbackAsync(
            IReadOnlyList<BloatwareApp> apps,
            Action<int, int, BloatwareApp>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new BulkRemovalResult
            {
                RequestedTotal = apps?.Count ?? 0
            };

            if (apps == null || apps.Count == 0)
            {
                result.Completed = true;
                return result;
            }

            var removedBeforeFailure = new List<BloatwareApp>();

            for (var i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                if (cancellationToken.IsCancellationRequested)
                {
                    MarkBulkOperationCanceled(result, apps, i, null);
                    StatusChanged?.Invoke($"Bulk removal canceled. {result.Succeeded.Count} completed, {result.Skipped.Count} skipped.");
                    return result;
                }

                progress?.Invoke(i + 1, apps.Count, app);

                bool removed;
                try
                {
                    removed = await RemoveAppAsync(app, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    MarkBulkOperationCanceled(result, apps, i + 1, app);
                    StatusChanged?.Invoke($"Bulk removal canceled at {app.Name}. {result.Succeeded.Count} completed, {result.Skipped.Count} skipped.");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Bulk removal exception for {app.Name}: {ex.Message}");
                    app.LastRemovalStatus = RemovalStatus.Failed;
                    app.LastFailureReason = ex.Message;
                    removed = false;
                }

                if (removed)
                {
                    if (app.LastRemovalStatus == RemovalStatus.Skipped)
                    {
                        result.Skipped.Add(app);
                        continue;
                    }

                    result.Succeeded.Add(app);
                    removedBeforeFailure.Add(app);
                    continue;
                }

                result.FailedAt = app;
                result.Failed.Add(app);
                result.Completed = false;
                StatusChanged?.Invoke($"Bulk removal failed at {app.Name}. Rolling back previous removals...");

                foreach (var previous in Enumerable.Reverse(removedBeforeFailure))
                {
                    if (!previous.CanRestore || !previous.IsRemoved)
                    {
                        result.RollbackSkipped.Add(previous);
                        continue;
                    }

                    var restored = await RestoreAppAsync(previous);
                    if (restored)
                    {
                        result.RollbackSucceeded.Add(previous);
                    }
                    else
                    {
                        result.RollbackFailed.Add(previous);
                    }
                }

                var rollbackSummary = result.RollbackFailed.Count == 0
                    ? $"Rollback complete ({result.RollbackSucceeded.Count} restored, {result.RollbackSkipped.Count} skipped)."
                    : $"Rollback partial ({result.RollbackSucceeded.Count} restored, {result.RollbackFailed.Count} failed, {result.RollbackSkipped.Count} skipped).";
                StatusChanged?.Invoke($"Bulk remove stopped at {app.Name}. {rollbackSummary}");
                return result;
            }

            result.Completed = true;
            return result;
        }

        public async Task<BulkRestoreResult> RestoreAppsAsync(
            IReadOnlyList<BloatwareApp> apps,
            Action<int, int, BloatwareApp>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new BulkRestoreResult
            {
                RequestedTotal = apps?.Count ?? 0
            };

            if (apps == null || apps.Count == 0)
            {
                result.Completed = true;
                return result;
            }

            for (var i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                if (cancellationToken.IsCancellationRequested)
                {
                    MarkBulkRestoreCanceled(result, apps, i, null);
                    StatusChanged?.Invoke($"Bulk restore canceled. {result.Succeeded.Count} completed, {result.Skipped.Count} skipped.");
                    return result;
                }

                progress?.Invoke(i + 1, apps.Count, app);

                try
                {
                    var restored = await RestoreAppAsync(app, cancellationToken);
                    if (restored)
                    {
                        result.Succeeded.Add(app);
                    }
                    else
                    {
                        result.Failed.Add(app);
                    }
                }
                catch (OperationCanceledException)
                {
                    MarkBulkRestoreCanceled(result, apps, i + 1, app);
                    StatusChanged?.Invoke($"Bulk restore canceled at {app.Name}. {result.Succeeded.Count} completed, {result.Skipped.Count} skipped.");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Bulk restore exception for {app.Name}: {ex.Message}");
                    app.LastFailureReason = ex.Message;
                    app.LastRemovalDetail = ex.Message;
                    result.Failed.Add(app);
                }
            }

            result.Completed = true;
            return result;
        }

        private async Task<bool> RemoveAppxPackageAsync(BloatwareApp app, CancellationToken cancellationToken)
        {
            // Strategy: try current-user removal first, then -AllUsers, then provisioned package removal
            // This handles both standard and pre-provisioned (OEM-installed) packages
            //
            // IMPORTANT: Get-AppxPackage positional parameter is -Name (e.g. "Microsoft.BingWeather"),
            // NOT PackageFullName (e.g. "Microsoft.BingWeather_4.53.52220.0_x64__8wekyb3d8bbwe").
            // Extract the Name portion from PackageFullName by splitting on '_'.
            var appxName = app.PackageId.Contains('_') ? app.PackageId.Split('_')[0] : app.PackageId;
            var escapedAppxName = EscapePowerShellSingleQuotedString(appxName);

            var beforeState = await GetAppxPresenceSnapshotAsync(appxName, cancellationToken);
            if (beforeState != null && !beforeState.IsPresent)
            {
                var detail = "Package was already absent before removal attempt.";
                SetRemovalOutcome(app, RemovalStatus.Skipped, detail, detail);
                return true;
            }
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"" +
                    $"try {{ Get-AppxPackage '{escapedAppxName}' | Remove-AppxPackage -ErrorAction Stop; exit 0 }} " +
                    $"catch {{ " +
                    $"  try {{ Get-AppxPackage -AllUsers '{escapedAppxName}' | Remove-AppxPackage -AllUsers -ErrorAction Stop; exit 0 }} " +
                    $"  catch {{ " +
                    $"    try {{ Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '{escapedAppxName}*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction Stop; exit 0 }} " +
                    $"    catch {{ Write-Error $_.Exception.Message; exit 1 }} " +
                    $"  }} " +
                    $"}}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var stderrTask = process.StandardError.ReadToEndAsync();
            await WaitForProcessExitAsync(process, app, $"Removing {app.Name}", cancellationToken);
            var stderr = await stderrTask;
            
            if (process.ExitCode != 0)
            {
                var reason = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : "PowerShell AppX removal exited with non-zero code.";
                _logger.Warn($"AppX removal failed for {app.Name}: {reason}");
                
                if (reason.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                    reason.Contains("0x80070005", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn($"  → Run OmenCore as Administrator to remove provisioned packages");
                    reason = "Access denied — run OmenCore as Administrator to remove provisioned packages.";
                }

                app.LastFailureReason = reason;
            }

            var afterState = await GetAppxPresenceSnapshotAsync(appxName, cancellationToken);
            if (afterState == null)
            {
                var verificationFailure = process.ExitCode == 0
                    ? "Removal command completed, but AppX presence verification failed."
                    : app.LastFailureReason ?? "AppX removal failed and verification could not complete.";
                app.LastFailureReason = verificationFailure;
                app.LastRemovalDetail = verificationFailure;
                return false;
            }

            if (afterState.IsPresent)
            {
                var scopeDescription = DescribeAppxPresence(afterState);
                var detail = beforeState != null && AreAppxPresenceSnapshotsEqual(beforeState, afterState)
                    ? $"Removal resulted in no state change; package is still present ({scopeDescription})."
                    : $"Package is still present after removal attempt ({scopeDescription}).";

                if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(app.LastFailureReason))
                {
                    detail = $"{detail} Command error: {app.LastFailureReason}";
                }

                app.LastFailureReason = detail;
                app.LastRemovalDetail = detail;
                return false;
            }

            app.LastFailureReason = null;
            app.LastRemovalDetail = "AppX package removal verified across current user, all users, and provisioned scopes.";
            
            return true;
        }

        private async Task<bool> RemoveWin32AppAsync(BloatwareApp app, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(app.UninstallCommand))
            {
                const string missingCommand = "No uninstall command is registered for this Win32 application.";
                app.LastFailureReason = missingCommand;
                app.LastRemovalDetail = missingCommand;
                return false;
            }

            if (!IsWin32AppStillInstalled(app))
            {
                const string alreadyAbsent = "Win32 uninstall entry was already absent before removal attempt.";
                SetRemovalOutcome(app, RemovalStatus.Skipped, alreadyAbsent, alreadyAbsent);
                return true;
            }

            var (cmd, args) = ParseWin32UninstallCommand(app.UninstallCommand);
            if (string.IsNullOrWhiteSpace(cmd))
            {
                _logger.Warn($"Could not parse Win32 uninstall command for {app.Name}: {app.UninstallCommand}");
                app.LastFailureReason = "Could not parse Win32 uninstall command.";
                app.LastRemovalDetail = app.LastFailureReason;
                return false;
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

                await WaitForProcessExitAsync(process, app, $"Uninstalling {app.Name}", cancellationToken);
                if (process.ExitCode != 0)
                {
                    _logger.Warn($"Win32 uninstaller for {app.Name} exited with code {process.ExitCode}; verifying actual removal state");
                }

                var removed = await VerifyWin32AppRemovedAsync(app, cancellationToken);
                if (!removed)
                {
                    _logger.Warn($"Win32 app still detected after uninstall attempt: {app.Name}");
                    var detail = process.ExitCode != 0
                        ? $"Uninstaller exited with code {process.ExitCode} and app is still present."
                        : "Uninstaller exited successfully but app was still detected after verification.";
                    app.LastFailureReason = detail;
                    app.LastRemovalDetail = detail;
                }
                else
                {
                    app.LastFailureReason = null;
                    app.LastRemovalDetail = "Win32 app uninstall verified; registry no longer reports the app as installed.";
                }

                return removed;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Win32 uninstall failed for {app.Name}: {ex.Message}");
                app.LastFailureReason = ex.Message;
                app.LastRemovalDetail = ex.Message;
                return false;
            }
        }

        private (string fileName, string arguments) ParseWin32UninstallCommand(string uninstallCommand)
        {
            var command = uninstallCommand.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return (string.Empty, string.Empty);
            }

            if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                var args = Regex.Replace(command, @"(?i)^\s*""?msiexec(?:\.exe)?""?", string.Empty).Trim();
                args = Regex.Replace(args, @"(?i)(^|\s)/i(?=\s|\{)", "$1/X");

                if (!Regex.IsMatch(args, @"(?i)(^|\s)/(quiet|qn|passive)(\s|$)"))
                {
                    args += " /qn /norestart";
                }

                return ("msiexec.exe", args.Trim());
            }

            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                var endQuote = command.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    var fileName = command[1..endQuote];
                    var args = command[(endQuote + 1)..].Trim();
                    return (fileName, AppendSilentFlags(args));
                }
            }

            var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                var fileName = command[..(exeIndex + 4)].Trim();
                var args = command[(exeIndex + 4)..].Trim();
                return (fileName, AppendSilentFlags(args));
            }

            return (command, string.Empty);
        }

        private static string AppendSilentFlags(string args)
        {
            if (Regex.IsMatch(args, @"(?i)(^|\s)/(s|silent|quiet|verysilent)(\s|$)"))
            {
                return args;
            }

            return string.IsNullOrWhiteSpace(args)
                ? "/S /silent /quiet"
                : args + " /S /silent /quiet";
        }

        private async Task<bool> VerifyWin32AppRemovedAsync(BloatwareApp app, CancellationToken cancellationToken)
        {
            const int maxChecks = 6;

            for (var attempt = 1; attempt <= maxChecks; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsWin32AppStillInstalled(app))
                {
                    return true;
                }

                await Task.Delay(2000, cancellationToken);
            }

            return false;
        }

        private bool IsWin32AppStillInstalled(BloatwareApp app)
        {
            var uninstallKeys = new[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (hive, keyPath) in uninstallKeys)
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key == null)
                {
                    continue;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null)
                    {
                        continue;
                    }

                    var displayName = subKey.GetValue("DisplayName")?.ToString();
                    if (string.Equals(subKeyName, app.PackageId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(displayName, app.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Task<bool> RemoveStartupItemAsync(BloatwareApp app, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!string.IsNullOrWhiteSpace(app.StartupFilePath))
                {
                    if (!File.Exists(app.StartupFilePath))
                    {
                        const string startupMissing = "Startup file was already absent before removal attempt.";
                        SetRemovalOutcome(app, RemovalStatus.Skipped, startupMissing, startupMissing);
                        return Task.FromResult(true);
                    }

                    var startupBackupDir = Path.Combine(_backupPath, "startup-items");
                    Directory.CreateDirectory(startupBackupDir);
                    var backupFilePath = Path.Combine(
                        startupBackupDir,
                        $"{Path.GetFileNameWithoutExtension(app.StartupFilePath)}-{Guid.NewGuid():N}{Path.GetExtension(app.StartupFilePath)}");

                    File.Move(app.StartupFilePath, backupFilePath);
                    if (_backups.TryGetValue(app.PackageId, out var backup))
                    {
                        backup.StartupFilePath = app.StartupFilePath;
                        backup.StartupBackupPath = backupFilePath;
                        SaveBackups();
                    }

                    app.LastRemovalDetail = "Startup file moved out of the startup folder successfully.";
                    app.LastFailureReason = null;

                    return Task.FromResult(true);
                }

                var hive = app.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = hive.OpenSubKey(app.RegistryPath!, true);
                if (key == null)
                {
                    app.LastFailureReason = "Startup registry key could not be opened for removal.";
                    app.LastRemovalDetail = app.LastFailureReason;
                    return Task.FromResult(false);
                }

                if (key.GetValue(app.Name) == null)
                {
                    const string startupValueMissing = "Startup registry value was already absent before removal attempt.";
                    SetRemovalOutcome(app, RemovalStatus.Skipped, startupValueMissing, startupValueMissing);
                    return Task.FromResult(true);
                }

                key.DeleteValue(app.Name, false);
                app.LastRemovalDetail = "Startup registry entry removed successfully.";
                app.LastFailureReason = null;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                app.LastFailureReason = ex.Message;
                app.LastRemovalDetail = ex.Message;
                return Task.FromResult(false);
            }
        }

        private async Task<bool> DisableScheduledTaskAsync(BloatwareApp app, CancellationToken cancellationToken)
        {
            if (!await IsScheduledTaskPresentAsync(app.PackageId, cancellationToken))
            {
                const string taskMissing = "Scheduled task was already absent before disable attempt.";
                SetRemovalOutcome(app, RemovalStatus.Skipped, taskMissing, taskMissing);
                return true;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/change /tn \"{app.PackageId}\" /disable",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var stderrTask = process.StandardError.ReadToEndAsync();
            await WaitForProcessExitAsync(process, app, $"Disabling scheduled task for {app.Name}", cancellationToken);

            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var reason = !string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : "schtasks.exe returned a non-zero exit code while disabling the task.";
                app.LastFailureReason = reason;
                app.LastRemovalDetail = reason;
                return false;
            }

            app.LastFailureReason = null;
            app.LastRemovalDetail = "Scheduled task disabled successfully.";
            return true;
        }

        /// <summary>
        /// Restores a previously removed application.
        /// </summary>
        public async Task<bool> RestoreAppAsync(BloatwareApp app, CancellationToken cancellationToken = default)
        {
            if (!app.IsRemoved || !app.CanRestore) return false;

            cancellationToken.ThrowIfCancellationRequested();

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
                    BloatwareType.AppxPackage => await RestoreAppxPackageAsync(backup, app, cancellationToken),
                    BloatwareType.StartupItem => await RestoreStartupItemAsync(backup, cancellationToken),
                    BloatwareType.ScheduledTask => await EnableScheduledTaskAsync(app, cancellationToken),
                    _ => false
                };

                if (success)
                {
                    app.IsRemoved = false;
                    AppRestored?.Invoke(app);
                    StatusChanged?.Invoke($"Successfully restored {app.Name}");
                    RecordHistory(app, "restore", true, null);
                    _logger.Info($"Successfully restored: {app.Name}");
                }
                else
                {
                    RecordHistory(app, "restore", false, app.LastFailureReason);
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                app.LastFailureReason = "Restore canceled before completion.";
                StatusChanged?.Invoke($"Canceled restoring {app.Name}");
                RecordHistory(app, "restore", false, app.LastFailureReason);
                _logger.Warn($"Restore canceled for {app.Name}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error restoring {app.Name}: {ex.Message}");
                app.LastFailureReason = ex.Message;
                RecordHistory(app, "restore", false, app.LastFailureReason);
                return false;
            }
        }

        private async Task<bool> RestoreAppxPackageAsync(BloatwareBackup backup, BloatwareApp app, CancellationToken cancellationToken)
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

            await WaitForProcessExitAsync(process, app, $"Restoring {app.Name}", cancellationToken);
            return process.ExitCode == 0;
        }

        private Task<bool> RestoreStartupItemAsync(BloatwareBackup backup, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!string.IsNullOrWhiteSpace(backup.StartupFilePath) &&
                    !string.IsNullOrWhiteSpace(backup.StartupBackupPath))
                {
                    if (!File.Exists(backup.StartupBackupPath))
                    {
                        return Task.FromResult(File.Exists(backup.StartupFilePath));
                    }

                    var destinationDirectory = Path.GetDirectoryName(backup.StartupFilePath);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (File.Exists(backup.StartupFilePath))
                    {
                        File.Delete(backup.StartupFilePath);
                    }

                    File.Move(backup.StartupBackupPath, backup.StartupFilePath);
                    return Task.FromResult(true);
                }

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

        private async Task<bool> EnableScheduledTaskAsync(BloatwareApp app, CancellationToken cancellationToken)
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

            await WaitForProcessExitAsync(process, app, $"Re-enabling scheduled task for {app.Name}", cancellationToken);
            return process.ExitCode == 0;
        }

        private async Task WaitForProcessExitAsync(Process process, BloatwareApp app, string operationDescription, CancellationToken cancellationToken)
        {
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(ExternalOperationTimeoutMs);
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                TryTerminateProcess(process);
                var timeoutMessage = $"{operationDescription} timed out after {ExternalOperationTimeoutMs / 1000} seconds and was terminated.";
                app.LastFailureReason = timeoutMessage;
                throw new TimeoutException(timeoutMessage);
            }

            try
            {
                await exitTask;
            }
            catch (OperationCanceledException)
            {
                TryTerminateProcess(process);
                throw;
            }
        }

        private static void TryTerminateProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        private static void MarkBulkOperationCanceled(BulkRemovalResult result, IReadOnlyList<BloatwareApp> apps, int remainingStartIndex, BloatwareApp? canceledAt)
        {
            result.Canceled = true;
            result.Completed = false;
            result.CanceledAt = canceledAt;

            for (var index = remainingStartIndex; index < apps.Count; index++)
            {
                var pendingApp = apps[index];
                const string skippedReason = "Skipped because bulk removal was canceled before this item was processed.";
                pendingApp.LastRemovalStatus = RemovalStatus.Skipped;
                pendingApp.LastFailureReason = skippedReason;
                pendingApp.LastRemovalDetail = skippedReason;
                result.Skipped.Add(pendingApp);
            }
        }

        private static void MarkBulkRestoreCanceled(BulkRestoreResult result, IReadOnlyList<BloatwareApp> apps, int remainingStartIndex, BloatwareApp? canceledAt)
        {
            result.Canceled = true;
            result.Completed = false;
            result.CanceledAt = canceledAt;

            for (var index = remainingStartIndex; index < apps.Count; index++)
            {
                var pendingApp = apps[index];
                pendingApp.LastFailureReason = "Skipped because bulk restore was canceled before this item was processed.";
                pendingApp.LastRemovalDetail = pendingApp.LastFailureReason;
                result.Skipped.Add(pendingApp);
            }
        }

        private static void SetRemovalOutcome(BloatwareApp app, RemovalStatus status, string detail, string? failureReason = null)
        {
            app.LastRemovalStatus = status;
            app.LastRemovalDetail = detail;
            app.LastFailureReason = failureReason;
        }

        private static string EscapePowerShellSingleQuotedString(string input)
        {
            return input.Replace("'", "''", StringComparison.Ordinal);
        }

        private async Task<AppxPresenceSnapshot?> GetAppxPresenceSnapshotAsync(string appxName, CancellationToken cancellationToken)
        {
            var escaped = EscapePowerShellSingleQuotedString(appxName);
            var queryScript =
                $"$name='{escaped}';" +
                "$current=@(Get-AppxPackage -Name $name).Count -gt 0;" +
                "$all=@(Get-AppxPackage -AllUsers -Name $name).Count -gt 0;" +
                "$prov=@(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like ($name + '*') }).Count -gt 0;" +
                "[pscustomobject]@{CurrentUserInstalled=$current;AnyUserInstalled=$all;ProvisionedInstalled=$prov}|ConvertTo-Json -Compress";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{queryScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var waitForExit = process.WaitForExitAsync(cancellationToken);
            var timeout = Task.Delay(10000, cancellationToken);
            var completed = await Task.WhenAny(waitForExit, timeout);
            if (completed != waitForExit)
            {
                TryTerminateProcess(process);
                return null;
            }

            await waitForExit;

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.Warn($"Failed to query AppX presence for {appxName}: {stderr}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<AppxPresenceSnapshot>(stdout, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.Warn($"Failed to parse AppX presence JSON for {appxName}: {ex.Message}");
                return null;
            }
        }

        private static bool AreAppxPresenceSnapshotsEqual(AppxPresenceSnapshot left, AppxPresenceSnapshot right)
        {
            return left.CurrentUserInstalled == right.CurrentUserInstalled
                && left.AnyUserInstalled == right.AnyUserInstalled
                && left.ProvisionedInstalled == right.ProvisionedInstalled;
        }

        private static string DescribeAppxPresence(AppxPresenceSnapshot snapshot)
        {
            var scopes = new List<string>();
            if (snapshot.CurrentUserInstalled)
            {
                scopes.Add("current user");
            }

            if (snapshot.AnyUserInstalled)
            {
                scopes.Add("all users");
            }

            if (snapshot.ProvisionedInstalled)
            {
                scopes.Add("provisioned image");
            }

            return scopes.Count == 0 ? "none" : string.Join(", ", scopes);
        }

        private async Task<bool> IsScheduledTaskPresentAsync(string taskName, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{taskName}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            await WaitForProcessExitAsync(process, new BloatwareApp { Name = taskName }, $"Checking scheduled task {taskName}", cancellationToken);

            if (process.ExitCode == 0)
            {
                return true;
            }

            var stderr = await stderrTask;
            if (string.IsNullOrWhiteSpace(stderr))
            {
                return false;
            }

            return !stderr.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase)
                && !stderr.Contains("cannot find the task", StringComparison.OrdinalIgnoreCase);
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
                    backup.StartupFilePath = app.StartupFilePath;

                    if (!string.IsNullOrWhiteSpace(app.RegistryPath) && !string.IsNullOrWhiteSpace(app.RegistryHive))
                    {
                        var hive = app.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                        using var key = hive.OpenSubKey(app.RegistryPath!);
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

        private void LoadBloatwareSignatures()
        {
            try
            {
                var databasePath = FindBloatwareDatabasePath();
                if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
                {
                    _dynamicSignatures = new List<BloatwareSignature>();
                    return;
                }

                var json = File.ReadAllText(databasePath);
                var database = JsonSerializer.Deserialize<BloatwareSignatureDatabase>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _dynamicSignatures = database?.Signatures ?? new List<BloatwareSignature>();
                _logger.Info($"Loaded {_dynamicSignatures.Count} dynamic bloatware signatures from {databasePath}");
            }
            catch (Exception ex)
            {
                _dynamicSignatures = new List<BloatwareSignature>();
                _logger.Warn($"Failed to load dynamic bloatware signatures: {ex.Message}");
            }
        }

        private static string? FindBloatwareDatabasePath()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "config", "bloatware_database.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            return null;
        }

        private bool TryMatchConfiguredBloatware(
            string name,
            out BloatwareCategory category,
            out string description,
            out RemovalRisk risk,
            out string? friendlyName)
        {
            foreach (var signature in _dynamicSignatures)
            {
                if (string.IsNullOrWhiteSpace(signature.Pattern))
                {
                    continue;
                }

                var isMatch = string.Equals(signature.Match, "Exact", StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(name, signature.Pattern, StringComparison.OrdinalIgnoreCase)
                    : name.Contains(signature.Pattern, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                {
                    continue;
                }

                category = Enum.TryParse<BloatwareCategory>(signature.Category, true, out var parsedCategory)
                    ? parsedCategory
                    : BloatwareCategory.Unknown;
                risk = Enum.TryParse<RemovalRisk>(signature.Risk, true, out var parsedRisk)
                    ? parsedRisk
                    : RemovalRisk.Unknown;
                description = signature.Description ?? string.Empty;
                friendlyName = string.IsNullOrWhiteSpace(signature.FriendlyName) ? null : signature.FriendlyName;
                return true;
            }

            category = BloatwareCategory.Unknown;
            description = string.Empty;
            risk = RemovalRisk.Unknown;
            friendlyName = null;
            return false;
        }

        #region Known Bloatware Database

        private bool IsKnownBloatware(string name, out BloatwareCategory category, out string description, out RemovalRisk risk)
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

            if (TryMatchConfiguredBloatware(name, out category, out description, out risk, out _))
            {
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
            var normalizedTaskName = NormalizeTaskIdentifier(taskName);

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

            if ((taskName.Contains("HP", StringComparison.OrdinalIgnoreCase) || normalizedTaskName.Contains("hp", StringComparison.OrdinalIgnoreCase)) &&
                !taskName.Contains("OmenCore", StringComparison.OrdinalIgnoreCase))
            {
                category = BloatwareCategory.OemSoftware;
                description = "HP scheduled task";
                risk = RemovalRisk.Low;
                return true;
            }
            if (taskName.Contains("Customer Experience", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("CEIP", StringComparison.OrdinalIgnoreCase) ||
                taskName.Contains("Telemetry", StringComparison.OrdinalIgnoreCase) ||
                normalizedTaskName.Contains("touchpoint analytics", StringComparison.OrdinalIgnoreCase))
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

        private static string NormalizeTaskIdentifier(string taskName)
        {
            return taskName
                .Replace('\\', ' ')
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Trim();
        }

        private static string GetFriendlyTaskName(string taskName)
        {
            var trimmed = taskName.Trim('\\');
            var leafName = trimmed.Contains('\\') ? trimmed.Split('\\').Last() : trimmed;
            return Regex.Replace(leafName.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
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

        /// <summary>
        /// Exports a plain-text removal result log for all apps that had a removal attempted in this session.
        /// Returns the full path of the written file, or null on failure.
        /// </summary>
        public string? ExportRemovalLog(IEnumerable<BloatwareApp> apps)
        {
            try
            {
                var attempted = apps.Where(a => a.LastRemovalStatus != RemovalStatus.NotAttempted).ToList();
                if (!attempted.Any()) return null;

                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OmenCore", "Logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, $"bloatware-removal-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                var lines = new System.Text.StringBuilder();
                lines.AppendLine($"OmenCore Bloatware Removal Log");
                lines.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                lines.AppendLine($"Machine: {Environment.MachineName}");
                lines.AppendLine($"User: {Environment.UserName}");
                lines.AppendLine($"Administrator: {(IsRunningAsAdmin ? "Yes" : "No")}");
                lines.AppendLine($"OS: {Environment.OSVersion}");
                if (_lastRemovalRestorePointCreatedAt.HasValue)
                {
                    lines.AppendLine($"Pre-removal restore point: {_lastRemovalRestorePointCreatedAt.Value:yyyy-MM-dd HH:mm:ss}");
                    if (!string.IsNullOrWhiteSpace(_lastRemovalRestorePointDescription))
                    {
                        lines.AppendLine($"Restore point description: {_lastRemovalRestorePointDescription}");
                    }

                    if (_lastRemovalRestorePointSequence.HasValue)
                    {
                        lines.AppendLine($"Restore point sequence: {_lastRemovalRestorePointSequence.Value}");
                    }

                    if (!string.IsNullOrWhiteSpace(_lastRemovalRestorePointMessage))
                    {
                        lines.AppendLine($"Restore point status: {_lastRemovalRestorePointMessage}");
                    }
                }
                else
                {
                    lines.AppendLine("Pre-removal restore point: Not created in this session");
                }
                lines.AppendLine(new string('─', 70));
                lines.AppendLine();

                var succeeded = attempted.Where(a => a.LastRemovalStatus == RemovalStatus.VerifiedSuccess || a.LastRemovalStatus == RemovalStatus.Succeeded).ToList();
                var failed = attempted.Where(a => a.LastRemovalStatus == RemovalStatus.Failed).ToList();
                var skipped = attempted.Where(a => a.LastRemovalStatus == RemovalStatus.Skipped).ToList();

                lines.AppendLine($"Summary: {succeeded.Count} succeeded, {failed.Count} failed, {skipped.Count} skipped out of {attempted.Count} tracked.");
                lines.AppendLine();

                if (succeeded.Any())
                {
                    lines.AppendLine("SUCCEEDED:");
                    foreach (var a in succeeded)
                    {
                        lines.AppendLine($"  ✓  [{a.Type,-13}] {a.Name} ({a.Category})");
                        if (!string.IsNullOrWhiteSpace(a.LastRemovalDetail))
                            lines.AppendLine($"       Detail: {a.LastRemovalDetail}");
                    }
                    lines.AppendLine();
                }

                if (failed.Any())
                {
                    lines.AppendLine("FAILED:");
                    foreach (var a in failed)
                    {
                        lines.AppendLine($"  ✗  [{a.Type,-13}] {a.Name} ({a.Category})");
                        var detail = !string.IsNullOrWhiteSpace(a.LastRemovalDetail)
                            ? a.LastRemovalDetail
                            : a.LastFailureReason;
                        if (!string.IsNullOrWhiteSpace(detail))
                            lines.AppendLine($"       Reason: {detail}");
                    }
                    lines.AppendLine();
                }

                if (skipped.Any())
                {
                    lines.AppendLine("SKIPPED:");
                    foreach (var a in skipped)
                    {
                        lines.AppendLine($"  •  [{a.Type,-13}] {a.Name} ({a.Category})");
                        var detail = !string.IsNullOrWhiteSpace(a.LastRemovalDetail)
                            ? a.LastRemovalDetail
                            : a.LastFailureReason;
                        if (!string.IsNullOrWhiteSpace(detail))
                            lines.AppendLine($"       Reason: {detail}");
                    }
                    lines.AppendLine();
                }

                var appNames = attempted.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var historical = _historyEntries
                    .Where(entry => appNames.Contains(entry.Name))
                    .OrderByDescending(entry => entry.Timestamp)
                    .Take(50)
                    .ToList();

                if (historical.Any())
                {
                    lines.AppendLine("RECENT HISTORY:");
                    foreach (var entry in historical)
                    {
                        lines.AppendLine($"  {entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Action.ToUpperInvariant(),-7} | {(entry.Success ? "OK" : "FAIL"),-4} | {entry.Name} ({entry.Type})");
                        if (!string.IsNullOrWhiteSpace(entry.Details))
                        {
                            lines.AppendLine($"       Details: {entry.Details}");
                        }
                    }
                    lines.AppendLine();
                }

                File.WriteAllText(logPath, lines.ToString(), System.Text.Encoding.UTF8);
                _logger.Info($"Bloatware removal log exported: {logPath}");
                return logPath;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to export bloatware removal log: {ex.Message}");
                return null;
            }
        }

        private void RecordHistory(BloatwareApp app, string action, bool success, string? details)
        {
            var entry = new BloatwareHistoryEntry
            {
                Timestamp = DateTime.Now,
                Action = action,
                Name = app.Name,
                PackageId = app.PackageId,
                Type = app.Type.ToString(),
                Success = success,
                Details = details,
                IsAdministrator = IsRunningAsAdmin,
                DeviceName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString()
            };

            _historyEntries.Add(entry);
            if (_historyEntries.Count > 5000)
            {
                _historyEntries = _historyEntries
                    .OrderByDescending(item => item.Timestamp)
                    .Take(5000)
                    .OrderBy(item => item.Timestamp)
                    .ToList();
            }

            SaveHistory();
        }

        private void LoadHistory()
        {
            try
            {
                var historyDir = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrWhiteSpace(historyDir))
                {
                    Directory.CreateDirectory(historyDir);
                }

                if (!File.Exists(_historyFilePath))
                {
                    _historyEntries = new List<BloatwareHistoryEntry>();
                    return;
                }

                var json = File.ReadAllText(_historyFilePath);
                _historyEntries = JsonSerializer.Deserialize<List<BloatwareHistoryEntry>>(json) ?? new List<BloatwareHistoryEntry>();
            }
            catch (Exception ex)
            {
                _historyEntries = new List<BloatwareHistoryEntry>();
                _logger.Warn($"Failed to load bloatware history: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                var historyDir = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrWhiteSpace(historyDir))
                {
                    Directory.CreateDirectory(historyDir);
                }

                var json = JsonSerializer.Serialize(_historyEntries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to save bloatware history: {ex.Message}");
            }
        }
    }

    #region Supporting Types

    public class BloatwareApp : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string Publisher { get; set; } = "";
        public BloatwareType Type { get; set; }
        public BloatwareCategory Category { get; set; }
        public string Description { get; set; } = "";
        public RemovalRisk RemovalRisk { get; set; }
        public bool CanRestore { get; set; }
        private bool _isRemoved;
        public bool IsRemoved
        {
            get => _isRemoved;
            set => SetField(ref _isRemoved, value);
        }
        public string? UninstallCommand { get; set; }
        public string? RegistryPath { get; set; }
        public string? RegistryHive { get; set; }
        public string? StartupFilePath { get; set; }
        /// <summary>Result of the most recent removal attempt.</summary>
        private RemovalStatus _lastRemovalStatus = RemovalStatus.NotAttempted;
        public RemovalStatus LastRemovalStatus
        {
            get => _lastRemovalStatus;
            set => SetField(ref _lastRemovalStatus, value);
        }
        /// <summary>Human-readable detail from the most recent removal attempt.</summary>
        private string? _lastRemovalDetail;
        public string? LastRemovalDetail
        {
            get => _lastRemovalDetail;
            set => SetField(ref _lastRemovalDetail, value);
        }
        /// <summary>Human-readable failure reason from the most recent removal attempt.</summary>
        private string? _lastFailureReason;
        public string? LastFailureReason
        {
            get => _lastFailureReason;
            set => SetField(ref _lastFailureReason, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class AppxPresenceSnapshot
    {
        public bool CurrentUserInstalled { get; set; }
        public bool AnyUserInstalled { get; set; }
        public bool ProvisionedInstalled { get; set; }
        public bool IsPresent => CurrentUserInstalled || AnyUserInstalled || ProvisionedInstalled;
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
        public string? StartupFilePath { get; set; }
        public string? StartupBackupPath { get; set; }
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

    public enum RemovalStatus
    {
        NotAttempted,
        Pending,
        Succeeded,
        VerifiedSuccess,
        Skipped,
        Failed
    }

    public sealed class BulkRemovalResult
    {
        public int RequestedTotal { get; set; }
        public bool Completed { get; set; }
        public bool Canceled { get; set; }
        public BloatwareApp? FailedAt { get; set; }
        public BloatwareApp? CanceledAt { get; set; }
        public List<BloatwareApp> Succeeded { get; } = new();
        public List<BloatwareApp> Failed { get; } = new();
        public List<BloatwareApp> Skipped { get; } = new();
        public List<BloatwareApp> RollbackSucceeded { get; } = new();
        public List<BloatwareApp> RollbackFailed { get; } = new();
        public List<BloatwareApp> RollbackSkipped { get; } = new();
    }

    public sealed class PreRemovalRestorePointResult
    {
        public bool Success { get; private set; }
        public bool ReusedExisting { get; private set; }
        public DateTime? CreatedAt { get; private set; }
        public string? Description { get; private set; }
        public uint? SequenceNumber { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public static PreRemovalRestorePointResult Created(DateTime createdAt, string? description, uint? sequenceNumber, string? message)
        {
            return new PreRemovalRestorePointResult
            {
                Success = true,
                ReusedExisting = false,
                CreatedAt = createdAt,
                Description = description,
                SequenceNumber = sequenceNumber,
                Message = string.IsNullOrWhiteSpace(message) ? "Restore point created successfully." : message
            };
        }

        public static PreRemovalRestorePointResult Reused(DateTime createdAt, string? description, uint? sequenceNumber, string? message)
        {
            return new PreRemovalRestorePointResult
            {
                Success = true,
                ReusedExisting = true,
                CreatedAt = createdAt,
                Description = description,
                SequenceNumber = sequenceNumber,
                Message = string.IsNullOrWhiteSpace(message)
                    ? "Reusing a recent pre-removal restore point from this session."
                    : message
            };
        }

        public static PreRemovalRestorePointResult Failed(string message)
        {
            return new PreRemovalRestorePointResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message)
                    ? "Restore point creation failed."
                    : message
            };
        }
    }

    public sealed class BulkRestoreResult
    {
        public int RequestedTotal { get; set; }
        public bool Completed { get; set; }
        public bool Canceled { get; set; }
        public BloatwareApp? CanceledAt { get; set; }
        public List<BloatwareApp> Succeeded { get; } = new();
        public List<BloatwareApp> Failed { get; } = new();
        public List<BloatwareApp> Skipped { get; } = new();
    }

    public sealed class BloatwareSignatureDatabase
    {
        public List<BloatwareSignature> Signatures { get; set; } = new();
    }

    public sealed class BloatwareSignature
    {
        public string Pattern { get; set; } = string.Empty;
        public string Match { get; set; } = "Contains";
        public string? FriendlyName { get; set; }
        public string Category { get; set; } = nameof(BloatwareCategory.Unknown);
        public string Description { get; set; } = string.Empty;
        public string Risk { get; set; } = nameof(RemovalRisk.Unknown);
    }

    public sealed class BloatwareHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Details { get; set; }
        public bool IsAdministrator { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
    }

    #endregion
}
