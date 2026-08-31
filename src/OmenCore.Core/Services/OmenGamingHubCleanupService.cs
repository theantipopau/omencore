using Microsoft.Win32;
using OmenCore.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    public class OmenGamingHubCleanupService
    {
        private readonly LoggingService _logging;
        
        /// <summary>
        /// Default timeout for individual cleanup commands (2 minutes)
        /// </summary>
        private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(2);
        
        /// <summary>
        /// Timeout for winget operations which can be slower (3 minutes)
        /// </summary>
        private static readonly TimeSpan WingetTimeout = TimeSpan.FromMinutes(3);
        
        /// <summary>
        /// Event fired when a cleanup step is completed
        /// </summary>
        public event Action<string>? StepCompleted;
        private readonly string[] _storePackages =
        {
            "AD2F1837.OMENCommandCenter",
            "AD2F1837.OMENCommandCenterDev",
            "AD2F1837.OMENCommandCenter_Beta"
        };

        private readonly string[] _processNames =
        {
            "OmenCommandCenter",
            "OmenCommandCenterBackground",
            "OmenInstallMonitor",
            "HP.Omen.OmenCommandCenter",
            "HPSystemOptimizer",
            "OmenCap"  // HP Omen HSA - runs from DriverStore even after OGH uninstall
        };

        private readonly string[] _residualDirectories =
        {
            @"C:\\Program Files\\HP\\SystemOptimizer",
            @"C:\\Program Files\\HP\\Overlay",
            @"C:\\Program Files\\HP\\OmenInstallMonitor",
            @"C:\\Program Files (x86)\\HP\\HP OMEN Gaming Hub",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\\Packages"
        };
        
        /// <summary>
        /// DriverStore inf packages that contain OmenCap.exe - these persist after OGH uninstall
        /// and can block MSR access for undervolting.
        /// </summary>
        private readonly string[] _driverStorePatterns =
        {
            "hpomencustomcapcomp.inf_*",  // Contains OmenCap.exe - blocks MSR/undervolt
            "hpomenhsacomp.inf_*",        // HP Omen HSA component
            "hpomenwmicap.inf_*"          // HP Omen WMI capability
        };

        private readonly string _windowsAppsRoot = @"C:\\Program Files\\WindowsApps";

        private readonly (RegistryHive hive, string path)[] _registryKeyTargets =
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\\HP\\OMENCommandCenter"),
            (RegistryHive.LocalMachine, @"SOFTWARE\\Hewlett-Packard\\OMEN Command Center"),
            (RegistryHive.LocalMachine, @"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\OMENCommandCenter"),
            (RegistryHive.CurrentUser, @"SOFTWARE\\HP\\OMENCommandCenter"),
            (RegistryHive.CurrentUser, @"SOFTWARE\\Hewlett-Packard\\OMEN Command Center"),
            (RegistryHive.CurrentUser, @"Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\AppModel\\Repository\\Packages\\AD2F1837.OMENCommandCenter")
        };

        private readonly string[] _scheduledTasks =
        {
            "HP\\OMEN Command Center",
            "HP\\OmenHousekeeper",
            "HP\\OmenInstallMonitor"
        };

        private readonly string[] _serviceNames =
        {
            "OmenCommandCenterBackgroundService",
            "HPAppHelperCap",
            "HPOmenCap",              // HP Omen HSA Service (primary OGH service)
            "HPOmenCommandCenter",    // Older OGH service name
            "OmenInstallMonitor",
            "HpTouchpointAnalyticsService", // HP Analytics
            "HPDiagsCap"              // HP Diagnostics
        };

        public OmenGamingHubCleanupService(LoggingService logging)
        {
            _logging = logging;
        }

        public async Task<OmenCleanupResult> CleanupAsync(OmenCleanupOptions options, CancellationToken token = default)
        {
            var result = new OmenCleanupResult();
            var totalSteps = CountSteps(options);
            var currentStep = 0;
            
            void ReportProgress(string message)
            {
                currentStep++;
                var progress = $"[{currentStep}/{totalSteps}] {message}";
                _logging.Info(progress);
                result.Steps.Add(message);
                StepCompleted?.Invoke(progress);
            }
            
            try
            {
                _logging.Info("═══════════════════════════════════════════════════");
                _logging.Info("Starting OMEN Gaming Hub cleanup...");
                _logging.Info("═══════════════════════════════════════════════════");
                
                if (options.KillRunningProcesses)
                {
                    KillProcesses();
                    ReportProgress("Killed running OMEN processes");
                }

                if (options.RemoveStorePackage)
                {
                    ReportProgress("Removing Store packages (this may take a minute)...");
                    result.StorePackageRemoved = await RemoveStorePackagesAsync(options.DryRun, token);
                    ReportProgress(result.StorePackageRemoved ? "✓ Store packages removed" : "⚠ Store packages still present");
                }

                if (options.RemoveLegacyInstallers)
                {
                    ReportProgress("Running legacy uninstallers (this may take a few minutes)...");
                    result.UninstallTriggered = await RunLegacyUninstallersAsync(options.DryRun, token);
                    ReportProgress(result.UninstallTriggered ? "✓ Legacy uninstallers completed" : "⚠ Legacy uninstallers unavailable");
                }

                if (options.RemoveRegistryTraces)
                {
                    result.RegistryCleaned = RemoveRegistryTraces(options.DryRun);
                    ReportProgress(result.RegistryCleaned ? "✓ Registry entries removed" : "⚠ No registry entries removed");
                }

                if (options.RemoveResidualFiles)
                {
                    result.FilesRemoved = RemoveResidualFiles(options.DryRun);
                    ReportProgress(result.FilesRemoved ? "✓ Residual files deleted" : "⚠ Residual files could not be fully deleted");
                }

                if (options.RemoveServicesAndTasks)
                {
                    ReportProgress("Cleaning up services and scheduled tasks...");
                    result.ServicesCleaned = await CleanupServicesAndTasksAsync(options.DryRun, token);
                    ReportProgress(result.ServicesCleaned ? "✓ Services/tasks cleaned" : "⚠ Services/tasks cleanup incomplete");
                }
                
                // Check for OmenCap in DriverStore (persists after OGH uninstall and blocks MSR)
                if (options.RemoveResidualFiles)
                {
                    var driverStoreInfo = DetectDriverStoreOmenCap();
                    if (!string.IsNullOrEmpty(driverStoreInfo))
                    {
                        _logging.Warn("═══════════════════════════════════════════════════");
                        _logging.Warn("⚠️ OmenCap.exe detected in Windows DriverStore!");
                        _logging.Warn("═══════════════════════════════════════════════════");
                        _logging.Warn(driverStoreInfo);
                        _logging.Warn("This component runs automatically and may block:");
                        _logging.Warn("  - CPU undervolting (MSR access)");
                        _logging.Warn("  - Direct EC access");
                        _logging.Warn("═══════════════════════════════════════════════════");
                        _logging.Warn("To remove manually (requires admin):");
                        _logging.Warn("  1. Run: pnputil /enum-drivers | findstr /i omen");
                        _logging.Warn("  2. Find the oem##.inf for hpomencustomcapcomp");
                        _logging.Warn("  3. Run: pnputil /delete-driver oem##.inf /force");
                        _logging.Warn("  4. Reboot your computer");
                        _logging.Warn("═══════════════════════════════════════════════════");
                        result.Warnings.Add($"OmenCap.exe found in DriverStore: {driverStoreInfo}. Manual removal required.");
                        ReportProgress("⚠ OmenCap detected in DriverStore - see log for removal instructions");
                    }
                }

                if (options.PreserveFirewallRules)
                {
                    ReportProgress("Firewall rules preserved per user preference");
                }
                
                _logging.Info("═══════════════════════════════════════════════════");
                _logging.Info("OMEN Gaming Hub cleanup completed successfully");
                _logging.Info("═══════════════════════════════════════════════════");
                ReportProgress("✓ Cleanup completed successfully");
            }
            catch (OperationCanceledException)
            {
                _logging.Warn("OMEN Gaming Hub cleanup was cancelled");
                result.Errors.Add("Cleanup was cancelled by user");
                ReportProgress("⚠ Cleanup cancelled");
            }
            catch (Exception ex)
            {
                _logging.Error("OMEN Gaming Hub cleanup failed", ex);
                result.Errors.Add(ex.Message);
                ReportProgress($"✗ Cleanup failed: {ex.Message}");
            }

            return result;
        }
        
        private int CountSteps(OmenCleanupOptions options)
        {
            var count = 1; // Final completion message
            if (options.KillRunningProcesses) count++;
            if (options.RemoveStorePackage) count += 2; // Start + result
            if (options.RemoveLegacyInstallers) count += 2;
            if (options.RemoveRegistryTraces) count++;
            if (options.RemoveResidualFiles) count++;
            if (options.RemoveServicesAndTasks) count += 2;
            if (options.PreserveFirewallRules) count++;
            return count;
        }

        private void KillProcesses()
        {
            foreach (var name in _processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill(true);
                        _logging.Info($"Terminated {process.ProcessName} (PID {process.Id})");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Unable to kill {process.ProcessName}: {ex.Message}");
                    }
                }
            }
        }

        private async Task<bool> RemoveStorePackagesAsync(bool dryRun, CancellationToken token)
        {
            var removed = false;
            foreach (var package in _storePackages)
            {
                var cmd = $"Get-AppxPackage -Name {package} | Remove-AppxPackage";
                var ok = await RunProcessAsync("powershell", $"-NoLogo -NoProfile -Command \"{cmd}\"", dryRun, token);
                removed = removed || ok;
            }
            return removed;
        }

        private async Task<bool> RunLegacyUninstallersAsync(bool dryRun, CancellationToken token)
        {
            var commands = new List<(string file, string args, TimeSpan timeout)>
            {
                ("winget", "uninstall --id 9N1NRK41F8S3 -e --silent", WingetTimeout),
                ("winget", "uninstall --name \"OMEN Gaming Hub\" --silent", WingetTimeout),
                ("powershell", "-NoLogo -NoProfile -Command \"Get-AppxPackage -Name 'HPInc.HPGamingHub' | Remove-AppxPackage\"", DefaultCommandTimeout)
            };

            foreach (var (file, args, timeout) in commands)
            {
                if (await RunProcessWithTimeoutAsync(file, args, timeout, dryRun, token))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RemoveRegistryTraces(bool dryRun)
        {
            var removedAny = false;
            foreach (var (hive, path) in _registryKeyTargets)
            {
                removedAny |= DeleteRegistryKey(hive, path, RegistryView.Registry64, dryRun);
                removedAny |= DeleteRegistryKey(hive, path, RegistryView.Registry32, dryRun);
            }

            removedAny |= DeleteRunValue("OMEN Command Center", dryRun);
            return removedAny;
        }

        private bool DeleteRegistryKey(RegistryHive hive, string path, RegistryView view, bool dryRun)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.OpenSubKey(path);
                if (subKey == null)
                {
                    return false;
                }

                if (dryRun)
                {
                    _logging.Info($"[DryRun] Would delete registry key {hive} {path} ({view})");
                    return true;
                }

                baseKey.DeleteSubKeyTree(path);
                _logging.Info($"Deleted registry key {hive} {path} ({view})");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Unable to delete registry key {hive} {path} ({view}): {ex.Message}");
                return false;
            }
        }

        private bool DeleteRunValue(string valueName, bool dryRun)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: !dryRun);
                if (key == null)
                {
                    return false;
                }

                if (key.GetValue(valueName) == null)
                {
                    return false;
                }

                if (dryRun)
                {
                    _logging.Info($"[DryRun] Would delete Run value '{valueName}'");
                    return true;
                }

                key.DeleteValue(valueName);
                _logging.Info($"Removed Run value '{valueName}'");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Unable to remove Run value '{valueName}': {ex.Message}");
                return false;
            }
        }

        private bool RemoveResidualFiles(bool dryRun)
        {
            var removed = false;
            foreach (var path in _residualDirectories)
            {
                if (path.EndsWith("Packages", StringComparison.OrdinalIgnoreCase))
                {
                    removed |= RemoveWindowsAppPackages(path, dryRun);
                    continue;
                }

                removed |= DeleteDirectory(path, dryRun);
            }
            removed |= RemoveWindowsStoreInstallations(dryRun);
            return removed;
        }

        private bool RemoveWindowsAppPackages(string packagesRoot, bool dryRun)
        {
            var removed = false;
            if (!Directory.Exists(packagesRoot))
            {
                return false;
            }

            var candidates = Directory.GetDirectories(packagesRoot, "AD2F1837.OMENCommandCenter*", SearchOption.TopDirectoryOnly);
            foreach (var dir in candidates)
            {
                removed |= DeleteDirectory(dir, dryRun);
            }
            return removed;
        }

        private bool RemoveWindowsStoreInstallations(bool dryRun)
        {
            if (!Directory.Exists(_windowsAppsRoot))
            {
                return false;
            }

            var removed = false;
            try
            {
                var candidates = Directory.GetDirectories(_windowsAppsRoot, "AD2F1837.OMENCommandCenter*", SearchOption.TopDirectoryOnly);
                foreach (var dir in candidates)
                {
                    removed |= DeleteDirectory(dir, dryRun);
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Unable to enumerate WindowsApps entries: {ex.Message}");
            }

            return removed;
        }

        private bool DeleteDirectory(string path, bool dryRun)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return false;
                }

                if (dryRun)
                {
                    _logging.Info($"[DryRun] Would remove directory {path}");
                    return true;
                }

                Directory.Delete(path, recursive: true);
                _logging.Info($"Deleted directory {path}");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Unable to delete directory {path}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CleanupServicesAndTasksAsync(bool dryRun, CancellationToken token)
        {
            var any = false;
            foreach (var task in _scheduledTasks)
            {
                var args = $"/Delete /TN \"{task}\" /F";
                any |= await RunProcessWithTimeoutAsync("schtasks", args, DefaultCommandTimeout, dryRun, token);
            }

            foreach (var service in _serviceNames)
            {
                var stopArgs = $"stop {service}";
                var deleteArgs = $"delete {service}";
                var stopped = await RunProcessWithTimeoutAsync("sc", stopArgs, TimeSpan.FromSeconds(30), dryRun, token);
                var deleted = await RunProcessWithTimeoutAsync("sc", deleteArgs, TimeSpan.FromSeconds(30), dryRun, token);
                any |= stopped || deleted;
            }
            return any;
        }

        private async Task<bool> RunProcessWithTimeoutAsync(string fileName, string arguments, TimeSpan timeout, bool dryRun, CancellationToken token)
        {
            if (dryRun)
            {
                _logging.Info($"[DryRun] Would execute {fileName} {arguments}");
                return true;
            }

            try
            {
                _logging.Info($"Executing: {fileName} {arguments} (timeout: {timeout.TotalSeconds}s)");
                
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                var tcs = new TaskCompletionSource<int>();
                process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);
                process.Start();

                // Create a combined cancellation token with timeout
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                
                using (linkedCts.Token.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            _logging.Warn($"Command timed out or cancelled, killing process: {fileName}");
                            process.Kill(true);
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                    
                    if (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        tcs.TrySetException(new TimeoutException($"Command timed out after {timeout.TotalSeconds}s: {fileName} {arguments}"));
                    }
                    else
                    {
                        tcs.TrySetCanceled(token);
                    }
                }))
                {
                    try
                    {
                        var exitCode = await tcs.Task.ConfigureAwait(false);
                        
                        // Read output with a short timeout to avoid hanging on output
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();
                        
                        if (await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(TimeSpan.FromSeconds(5))) == Task.WhenAll(outputTask, errorTask))
                        {
                            var output = await outputTask.ConfigureAwait(false);
                            var error = await errorTask.ConfigureAwait(false);
                            
                            if (!string.IsNullOrWhiteSpace(output))
                            {
                                _logging.Info(output.Trim());
                            }
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                _logging.Warn(error.Trim());
                            }
                        }
                        
                        _logging.Info($"Command completed with exit code {exitCode}: {fileName}");
                        return exitCode == 0;
                    }
                    catch (TimeoutException tex)
                    {
                        _logging.Warn(tex.Message);
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logging.Warn($"Command {fileName} {arguments} cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Unable to execute {fileName} {arguments}: {ex.Message}");
                return false;
            }
        }
        
        // Legacy method for backwards compatibility - uses default timeout
        private Task<bool> RunProcessAsync(string fileName, string arguments, bool dryRun, CancellationToken token)
        {
            return RunProcessWithTimeoutAsync(fileName, arguments, DefaultCommandTimeout, dryRun, token);
        }
        
        /// <summary>
        /// Detects if OmenCap.exe exists in Windows DriverStore.
        /// This component persists after OGH uninstall and blocks MSR access for undervolting.
        /// </summary>
        /// <returns>Path info if found, null otherwise</returns>
        private string? DetectDriverStoreOmenCap()
        {
            try
            {
                var driverStoreRoot = @"C:\Windows\System32\DriverStore\FileRepository";
                if (!Directory.Exists(driverStoreRoot))
                {
                    return null;
                }
                
                foreach (var pattern in _driverStorePatterns)
                {
                    try
                    {
                        var matches = Directory.GetDirectories(driverStoreRoot, pattern, SearchOption.TopDirectoryOnly);
                        foreach (var dir in matches)
                        {
                            // Check if OmenCap.exe exists in this driver package
                            var omenCapPath = Path.Combine(dir, "OmenCap.exe");
                            if (File.Exists(omenCapPath))
                            {
                                return omenCapPath;
                            }
                            
                            // Also check subdirectories
                            try
                            {
                                var subFiles = Directory.GetFiles(dir, "OmenCap.exe", SearchOption.AllDirectories);
                                if (subFiles.Length > 0)
                                {
                                    return subFiles[0];
                                }
                            }
                            catch
                            {
                                // Ignore access denied errors
                            }
                        }
                    }
                    catch
                    {
                        // Ignore pattern match failures
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to check DriverStore for OmenCap: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Checks if OmenCap.exe process is currently running (regardless of source).
        /// </summary>
        public static bool IsOmenCapRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("OmenCap");
                var running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                return running;
            }
            catch
            {
                return false;
            }
        }
    }
}
