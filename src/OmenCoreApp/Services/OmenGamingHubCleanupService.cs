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
            "HPSystemOptimizer"
        };

        private readonly string[] _residualDirectories =
        {
            @"C:\\Program Files\\HP\\SystemOptimizer",
            @"C:\\Program Files\\HP\\Overlay",
            @"C:\\Program Files\\HP\\OmenInstallMonitor",
            @"C:\\Program Files (x86)\\HP\\HP OMEN Gaming Hub",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\\Packages"
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
            "OmenInstallMonitor"
        };

        public OmenGamingHubCleanupService(LoggingService logging)
        {
            _logging = logging;
        }

        public async Task<OmenCleanupResult> CleanupAsync(OmenCleanupOptions options, CancellationToken token = default)
        {
            var result = new OmenCleanupResult();
            try
            {
                if (options.KillRunningProcesses)
                {
                    KillProcesses();
                    result.Steps.Add("Killed running OMEN processes");
                }

                if (options.RemoveStorePackage)
                {
                    result.StorePackageRemoved = await RemoveStorePackagesAsync(options.DryRun, token);
                    result.Steps.Add(result.StorePackageRemoved ? "Store packages removed" : "Store packages still present");
                }

                if (options.RemoveLegacyInstallers)
                {
                    result.UninstallTriggered = await RunLegacyUninstallersAsync(options.DryRun, token);
                    result.Steps.Add(result.UninstallTriggered ? "Legacy uninstallers triggered" : "Legacy uninstallers unavailable");
                }

                if (options.RemoveRegistryTraces)
                {
                    result.RegistryCleaned = RemoveRegistryTraces(options.DryRun);
                    result.Steps.Add(result.RegistryCleaned ? "Registry entries removed" : "No registry entries removed");
                }

                if (options.RemoveResidualFiles)
                {
                    result.FilesRemoved = RemoveResidualFiles(options.DryRun);
                    result.Steps.Add(result.FilesRemoved ? "Residual files deleted" : "Residual files could not be fully deleted");
                }

                if (options.RemoveServicesAndTasks)
                {
                    result.ServicesCleaned = await CleanupServicesAndTasksAsync(options.DryRun, token);
                    result.Steps.Add(result.ServicesCleaned ? "Services/tasks cleaned" : "Services/tasks cleanup incomplete");
                }

                if (options.PreserveFirewallRules)
                {
                    result.Steps.Add("Firewall rules preserved per user preference");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("OMEN Gaming Hub cleanup failed", ex);
                result.Errors.Add(ex.Message);
            }

            return result;
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
            var commands = new List<(string file, string args)>
            {
                ("winget", "uninstall --id 9N1NRK41F8S3 -e --silent"),
                ("winget", "uninstall --name \"OMEN Gaming Hub\" --silent"),
                ("powershell", "-NoLogo -NoProfile -Command \"Get-AppxPackage -Name 'HPInc.HPGamingHub' | Remove-AppxPackage\"")
            };

            foreach (var cmd in commands)
            {
                if (await RunProcessAsync(cmd.file, cmd.args, dryRun, token))
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
                any |= await RunProcessAsync("schtasks", args, dryRun, token);
            }

            foreach (var service in _serviceNames)
            {
                var stopArgs = $"stop {service}";
                var deleteArgs = $"delete {service}";
                var stopped = await RunProcessAsync("sc", stopArgs, dryRun, token);
                var deleted = await RunProcessAsync("sc", deleteArgs, dryRun, token);
                any |= stopped || deleted;
            }
            return any;
        }

        private async Task<bool> RunProcessAsync(string fileName, string arguments, bool dryRun, CancellationToken token)
        {
            if (dryRun)
            {
                _logging.Info($"[DryRun] Would execute {fileName} {arguments}");
                return true;
            }

            try
            {
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

                using (token.Register(() =>
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
                        // ignored
                    }
                    tcs.TrySetCanceled(token);
                }))
                {
                    var exitCode = await tcs.Task.ConfigureAwait(false);
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        _logging.Info(output.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        _logging.Warn(error.Trim());
                    }
                    return exitCode == 0;
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
    }
}
