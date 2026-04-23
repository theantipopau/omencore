using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Services.KeyboardLighting;
using OmenCore.Utils;

namespace OmenCore.Services.Diagnostics
{
    /// <summary>
    /// Privacy-first diagnostic data collection and export.
    /// Collects logs, EC state, system info for bug reports without sensitive data.
    /// </summary>
    public class DiagnosticExportService
    {
        private readonly LoggingService _logging;
        private readonly string _logsDirectory;
        private readonly ResumeRecoveryDiagnosticsService? _resumeDiagnostics;

        public DiagnosticExportService(LoggingService logging, string logsDirectory, ResumeRecoveryDiagnosticsService? resumeDiagnostics = null)
        {
            _logging = logging;
            _logsDirectory = logsDirectory;
            _resumeDiagnostics = resumeDiagnostics;
        }

        /// <summary>
        /// Collect diagnostics: logs, system info, EC state, etc.
        /// Returns path to diagnostic bundle ZIP file.
        /// </summary>
        public async Task<string> CollectAndExportAsync(
            IEcAccess? ecAccess = null,
            LibreHardwareMonitorImpl? hwMonitor = null,
            object? wmiController = null)
        {
            try
            {
                var exportPath = Path.Combine(
                    Path.GetTempPath(),
                    $"OmenCore-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(exportPath);

                _logging.Info($"Collecting diagnostics to {exportPath}");

                // Collect components in parallel
                var tasks = new List<Task>
                {
                    CollectLogsAsync(exportPath),
                    CollectSystemInfoAsync(exportPath),
                    CollectRuntimePerformanceSnapshotAsync(exportPath),
                    CollectBackgroundTimerSnapshotAsync(exportPath),
                    CollectModelIdentityTraceAsync(exportPath),
                    CollectEcStateAsync(exportPath, ecAccess),
                    CollectHardwareInfoAsync(exportPath, hwMonitor),
                    CollectWmiCommandHistoryAsync(exportPath, wmiController),
                    CollectResumeRecoveryDiagnosticsAsync(exportPath)
                };

                await Task.WhenAll(tasks);

                // Create ZIP archive
                string zipPath = ZipDiagnostics(exportPath);

                _logging.Info($"✓ Diagnostics exported to {zipPath}");
                return zipPath;
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "DiagnosticExportService",
                    operation: "CollectAndExportAsync",
                    message: "Failed to export diagnostics",
                    ex: ex);
                throw;
            }
        }

        private async Task CollectLogsAsync(string exportPath)
        {
            try
            {
                // Copy recent log files from current and legacy locations.
                var candidateDirs = new List<string> { _logsDirectory };
                var legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore");
                if (!candidateDirs.Contains(legacyDir, StringComparer.OrdinalIgnoreCase))
                {
                    candidateDirs.Add(legacyDir);
                }

                var logFiles = candidateDirs
                    .Where(Directory.Exists)
                    .SelectMany(dir => Directory.GetFiles(dir, "*.log"))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(5)
                    .ToList();

                if (logFiles.Count > 0)
                {
                    var logsExportPath = Path.Combine(exportPath, "logs");
                    Directory.CreateDirectory(logsExportPath);

                    foreach (var logFile in logFiles)
                    {
                        File.Copy(logFile, Path.Combine(logsExportPath, Path.GetFileName(logFile)), overwrite: true);
                    }

                    _logging.Info($"Collected {logFiles.Count} log files");
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.WarnWithContext(
                    component: "DiagnosticExportService",
                    operation: "CollectLogsAsync",
                    message: "Failed to collect logs",
                    ex: ex);
            }
        }

        private async Task CollectSystemInfoAsync(string exportPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== SYSTEM INFORMATION ===");
                sb.AppendLine($"Timestamp: {DateTime.Now:O}");
                sb.AppendLine($"OmenCore Version: {GetOmenCoreVersion()}");
                sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
                sb.AppendLine($"Processor: {Environment.ProcessorCount} cores");
                sb.AppendLine($"RAM: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                sb.AppendLine();

                // Check security features
                sb.AppendLine("=== SECURITY FEATURES ===");
                sb.AppendLine($"SecureBoot: {GetSecureBootStatus()}");
                sb.AppendLine($"HVCI: {GetHvciStatus()}");
                sb.AppendLine();

                // Driver status
                sb.AppendLine("=== DRIVER STATUS ===");
                sb.AppendLine($"Legacy WinRing0: {GetWinRing0Status()}");
                sb.AppendLine($"PawnIO: {GetPawnIOStatus()}");
                sb.AppendLine();

                // Services
                sb.AppendLine("=== SERVICES ===");
                sb.AppendLine($"XTU Service: {GetXtuServiceStatus()}");
                sb.AppendLine($"Afterburner: {GetAfterburnerStatus()}");

                File.WriteAllText(Path.Combine(exportPath, "system-info.txt"), sb.ToString());
                _logging.Info("Collected system information");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.WarnWithContext(
                    component: "DiagnosticExportService",
                    operation: "CollectSystemInfoAsync",
                    message: "Failed to collect system info",
                    ex: ex);
            }
        }

        private async Task CollectResumeRecoveryDiagnosticsAsync(string exportPath)
        {
            try
            {
                var report = _resumeDiagnostics?.BuildExportReport() ?? "Resume recovery diagnostics service not available";
                File.WriteAllText(Path.Combine(exportPath, "resume-recovery.txt"), report);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect resume recovery diagnostics: {ex.Message}");
            }
        }

        private async Task CollectRuntimePerformanceSnapshotAsync(string exportPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== RUNTIME PERFORMANCE SNAPSHOT ===");
                sb.AppendLine($"Captured: {DateTime.Now:O}");
                sb.AppendLine();

                var current = Process.GetCurrentProcess();
                using (current)
                {
                    sb.AppendLine("[Current Process]");
                    sb.AppendLine($"Name: {current.ProcessName}");
                    sb.AppendLine($"PID: {current.Id}");
                    sb.AppendLine($"StartTime: {SafeGet(() => current.StartTime.ToString("O"), "Unavailable")}");
                    sb.AppendLine($"TotalProcessorTime: {SafeGet(() => current.TotalProcessorTime.ToString(), "Unavailable")}");
                    sb.AppendLine($"Threads: {SafeGet(() => current.Threads.Count.ToString(), "Unavailable")}");
                    sb.AppendLine($"Handles: {SafeGet(() => current.HandleCount.ToString(), "Unavailable")}");
                    sb.AppendLine($"WorkingSetMB: {SafeGet(() => (current.WorkingSet64 / 1024d / 1024d).ToString("F1"), "Unavailable")}");
                    sb.AppendLine($"PrivateMemoryMB: {SafeGet(() => (current.PrivateMemorySize64 / 1024d / 1024d).ToString("F1"), "Unavailable")}");
                    sb.AppendLine();
                }

                sb.AppendLine("[GC]");
                sb.AppendLine($"TotalManagedMemoryMB: {GC.GetTotalMemory(false) / 1024d / 1024d:F1}");
                sb.AppendLine($"Gen0Collections: {GC.CollectionCount(0)}");
                sb.AppendLine($"Gen1Collections: {GC.CollectionCount(1)}");
                sb.AppendLine($"Gen2Collections: {GC.CollectionCount(2)}");
                var gcInfo = GC.GetGCMemoryInfo();
                sb.AppendLine($"HeapSizeBytes: {gcInfo.HeapSizeBytes}");
                sb.AppendLine($"MemoryLoadBytes: {gcInfo.MemoryLoadBytes}");
                sb.AppendLine($"HighMemoryLoadThresholdBytes: {gcInfo.HighMemoryLoadThresholdBytes}");
                sb.AppendLine();

                sb.AppendLine("[ThreadPool]");
                ThreadPool.GetMinThreads(out var workerMin, out var ioMin);
                ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);
                ThreadPool.GetAvailableThreads(out var workerAvail, out var ioAvail);
                sb.AppendLine($"WorkerThreads: available={workerAvail}, min={workerMin}, max={workerMax}");
                sb.AppendLine($"IOThreads: available={ioAvail}, min={ioMin}, max={ioMax}");
                sb.AppendLine();

                sb.AppendLine("[Omen-Related Processes]");
                var omenProcesses = Process.GetProcesses()
                    .Where(p => p.ProcessName.Contains("omen", StringComparison.OrdinalIgnoreCase)
                             || p.ProcessName.Contains("hardwareworker", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!omenProcesses.Any())
                {
                    sb.AppendLine("No Omen-related processes detected.");
                }
                else
                {
                    foreach (var proc in omenProcesses)
                    {
                        using (proc)
                        {
                            var cpuTime = SafeGet(() => proc.TotalProcessorTime.ToString(), "Unavailable");
                            var wsMb = SafeGet(() => (proc.WorkingSet64 / 1024d / 1024d).ToString("F1"), "Unavailable");
                            sb.AppendLine($"{proc.ProcessName} (PID {proc.Id}) | CPU {cpuTime} | WS {wsMb} MB");
                        }
                    }
                }

                File.WriteAllText(Path.Combine(exportPath, "runtime-performance.txt"), sb.ToString());
                _logging.Info("Collected runtime performance snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect runtime performance snapshot: {ex.Message}");
            }
        }

        private async Task CollectBackgroundTimerSnapshotAsync(string exportPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== BACKGROUND TIMER REGISTRY SNAPSHOT ===");
                sb.AppendLine($"Captured: {DateTime.UtcNow:O}");
                sb.AppendLine();

                var timers = BackgroundTimerRegistry.GetAll();

                if (!timers.Any())
                {
                    sb.AppendLine("No timers currently registered.");
                }
                else
                {
                    sb.AppendLine($"Active background loops: {timers.Count}");
                    sb.AppendLine();

                    foreach (var t in timers)
                    {
                        var runningFor = DateTime.UtcNow - t.RegisteredUtc;
                        sb.AppendLine($"[{t.Name}]");
                        sb.AppendLine($"  Owner:       {t.OwnerService}");
                        sb.AppendLine($"  Tier:        {t.Tier}");
                        sb.AppendLine($"  Description: {t.Description}");
                        sb.AppendLine($"  Interval:    {FormatTimerInterval(t.IntervalMs)}");
                        sb.AppendLine($"  RunningFor:  {FormatTimerAge(runningFor)}");
                        sb.AppendLine();
                    }
                }

                File.WriteAllText(Path.Combine(exportPath, "background-timers.txt"), sb.ToString());
                _logging.Info($"Collected background timer snapshot ({timers.Count} registered)");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect background timer snapshot: {ex.Message}");
            }
        }

        private static string FormatTimerInterval(int ms) =>
            ms >= 3_600_000 ? $"{ms / 3_600_000.0:F1}h" :
            ms >= 60_000    ? $"{ms / 60_000.0:F1}min" :
            ms >= 1_000     ? $"{ms / 1_000.0:F1}s" :
            $"{ms}ms";

        private static string FormatTimerAge(TimeSpan age) =>
            age.TotalHours >= 1   ? $"{age.TotalHours:F1}h" :
            age.TotalMinutes >= 1 ? $"{age.TotalMinutes:F1}min" :
            $"{age.TotalSeconds:F0}s";

        private static string SafeGet(Func<string> getter, string fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private async Task CollectModelIdentityTraceAsync(string exportPath)
        {
            try
            {
                var systemInfo = new SystemInfoService(_logging).GetSystemInfo();
                var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities: null, logging: _logging);
                File.WriteAllText(Path.Combine(exportPath, "identity-resolution-trace.txt"), summary.TraceText);
                _logging.Info("Collected model identity resolution trace");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect model identity resolution trace: {ex.Message}");
            }
        }

        private async Task CollectEcStateAsync(string exportPath, IEcAccess? ecAccess)
        {
            try
            {
                if (ecAccess == null || !ecAccess.IsAvailable)
                {
                    File.WriteAllText(Path.Combine(exportPath, "ec-state.txt"), "EC access not available");
                    await Task.CompletedTask;
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== EC STATE DUMP ===");
                sb.AppendLine($"Captured: {DateTime.Now:O}");
                sb.AppendLine();

                // Read comprehensive EC registers (safe addresses for diagnostics)
                var safeRegisters = new[]
                {
                    // Fan control registers
                    0x2E, 0x2F, 0x34, 0x35, 0xCE, 0xCF,
                    // Temperature registers
                    0x60, 0x61, 0x62, 0x63, 0x68, 0x69,
                    // Power management
                    0x70, 0x71, 0x72, 0x73,
                    // System status
                    0x80, 0x81, 0x82, 0x83
                };

                sb.AppendLine("EC Register Dump (Safe Addresses):");
                sb.AppendLine("Address\tValue\tBinary");
                sb.AppendLine("--------------------------------");

                foreach (var reg in safeRegisters)
                {
                    try
                    {
                        byte value = ecAccess.ReadByte((ushort)reg);
                        string binary = Convert.ToString(value, 2).PadLeft(8, '0');
                        sb.AppendLine($"0x{reg:X2}\t0x{value:X2}\t{binary}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"0x{reg:X2}\tERROR\t{ex.Message}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("=== FAN CONTROL REGISTERS ===");
                sb.AppendLine("These registers may control fan speed and mode:");
                var fanRegisters = new[] { 0x2E, 0x2F, 0x34, 0x35 };
                foreach (var reg in fanRegisters)
                {
                    try
                    {
                        byte value = ecAccess.ReadByte((ushort)reg);
                        sb.AppendLine($"EC[0x{reg:X2}] = 0x{value:X2} ({value})");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"[DiagnosticExportService] {nameof(CollectEcStateAsync)} fan register 0x{reg:X2} read failed: {ex.Message}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("=== TEMPERATURE REGISTERS ===");
                sb.AppendLine("These registers may contain temperature readings:");
                var tempRegisters = new[] { 0x60, 0x61, 0x62, 0x63, 0x68, 0x69 };
                foreach (var reg in tempRegisters)
                {
                    try
                    {
                        byte value = ecAccess.ReadByte((ushort)reg);
                        sb.AppendLine($"EC[0x{reg:X2}] = 0x{value:X2} ({value}°C raw)");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"[DiagnosticExportService] {nameof(CollectEcStateAsync)} temp register 0x{reg:X2} read failed: {ex.Message}");
                    }
                }

                File.WriteAllText(Path.Combine(exportPath, "ec-state.txt"), sb.ToString());
                _logging.Info("Collected comprehensive EC state");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect EC state: {ex.Message}");
            }
        }

        private async Task CollectHardwareInfoAsync(string exportPath, LibreHardwareMonitorImpl? hwMonitor)
        {
            try
            {
                if (hwMonitor == null)
                {
                    File.WriteAllText(Path.Combine(exportPath, "hardware-info.txt"), "Hardware monitoring not available");
                    await Task.CompletedTask;
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== HARDWARE TELEMETRY ===");
                sb.AppendLine($"Captured: {DateTime.Now:O}");
                sb.AppendLine();

                sb.AppendLine($"CPU Temp: {hwMonitor.GetCpuTemperature()}°C");
                sb.AppendLine($"GPU Temp: {hwMonitor.GetGpuTemperature()}°C");

                File.WriteAllText(Path.Combine(exportPath, "hardware-info.txt"), sb.ToString());
                _logging.Info("Collected hardware information");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect hardware info: {ex.Message}");
            }
        }

        private async Task CollectWmiCommandHistoryAsync(string exportPath, object? wmiController)
        {
            try
            {
                if (wmiController == null)
                {
                    File.WriteAllText(Path.Combine(exportPath, "wmi-command-history.txt"), "WMI fan controller not available");
                    await Task.CompletedTask;
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== WMI COMMAND HISTORY ===");
                sb.AppendLine($"Captured: {DateTime.Now:O}");
                sb.AppendLine();

                // Try to get command history using reflection (since we don't have the interface)
                try
                {
                    var getHistoryMethod = wmiController.GetType().GetMethod("GetCommandHistory");
                    if (getHistoryMethod != null)
                    {
                        var history = getHistoryMethod.Invoke(wmiController, null) as System.Collections.IEnumerable;
                        if (history != null)
                        {
                            sb.AppendLine("Recent WMI Commands:");
                            sb.AppendLine("Timestamp\t\t\tCommand\t\t\tSuccess\tError\t\tRPM Before\tRPM After");
                            sb.AppendLine("----------------------------------------------------------------------------------------------------------------");

                            foreach (var entry in history)
                            {
                                var timestamp = entry.GetType().GetProperty("Timestamp")?.GetValue(entry)?.ToString() ?? "N/A";
                                var command = entry.GetType().GetProperty("Command")?.GetValue(entry)?.ToString() ?? "N/A";
                                var success = entry.GetType().GetProperty("Success")?.GetValue(entry)?.ToString() ?? "N/A";
                                var error = entry.GetType().GetProperty("Error")?.GetValue(entry)?.ToString() ?? "N/A";
                                var rpmBefore = entry.GetType().GetProperty("FanRpmBefore")?.GetValue(entry)?.ToString() ?? "N/A";
                                var rpmAfter = entry.GetType().GetProperty("FanRpmAfter")?.GetValue(entry)?.ToString() ?? "N/A";

                                sb.AppendLine($"{timestamp}\t{command,-20}\t{success,-7}\t{error,-10}\t{rpmBefore,-10}\t{rpmAfter}");
                            }
                        }
                        else
                        {
                            sb.AppendLine("No WMI commands recorded yet.");
                        }
                    }
                    else
                    {
                        sb.AppendLine("Command history not available in this controller version.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Error reading command history: {ex.Message}");
                }

                sb.AppendLine();
                sb.AppendLine("=== WMI CONTROLLER STATUS ===");
                // Add basic status info using reflection
                try
                {
                    var isAvailable = wmiController.GetType().GetProperty("IsAvailable")?.GetValue(wmiController)?.ToString() ?? "Unknown";
                    var status = wmiController.GetType().GetProperty("Status")?.GetValue(wmiController)?.ToString() ?? "Unknown";
                    var fanCount = wmiController.GetType().GetProperty("FanCount")?.GetValue(wmiController)?.ToString() ?? "Unknown";

                    sb.AppendLine($"Available: {isAvailable}");
                    sb.AppendLine($"Status: {status}");
                    sb.AppendLine($"Fan Count: {fanCount}");
                }
                catch
                {
                    sb.AppendLine("Controller status not available.");
                }

                File.WriteAllText(Path.Combine(exportPath, "wmi-command-history.txt"), sb.ToString());
                _logging.Info("Collected WMI command history");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect WMI command history: {ex.Message}");
            }
        }

        private string ZipDiagnostics(string exportPath)
        {
            try
            {
                var zipPath = Path.ChangeExtension(exportPath, ".zip");
                if (File.Exists(zipPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(zipPath);
                    var dir = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
                    zipPath = Path.Combine(dir, $"{baseName}-{Guid.NewGuid():N}.zip");
                }
                
                // Use .NET built-in ZipFile
                if (Directory.Exists(exportPath))
                {
                    System.IO.Compression.ZipFile.CreateFromDirectory(exportPath, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
                    Directory.Delete(exportPath, recursive: true);
                }

                _logging.Info($"Created diagnostic archive: {zipPath}");
                return zipPath;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to create ZIP archive: {ex.Message}");
                return exportPath; // Return directory if ZIP fails
            }
        }

        private string GetOmenCoreVersion()
        {
            return AppVersionProvider.GetVersionString();
        }

        [SupportedOSPlatform("windows")]
        private string GetSecureBootStatus()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                if (key != null)
                {
                    var value = key.GetValue("UEFISecureBootEnabled");
                    return value is int i ? (i == 1 ? "Enabled" : "Disabled") : "Unknown";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetSecureBootStatus)} failed: {ex.Message}");
            }
            return "Unknown";
        }

        [SupportedOSPlatform("windows")]
        private string GetHvciStatus()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                if (key != null)
                {
                    var value = key.GetValue("Enabled");
                    return value is int i ? (i == 1 ? "Enabled" : "Disabled") : "Unknown";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetHvciStatus)} failed: {ex.Message}");
            }
            return "Not Configured";
        }

        private string GetWinRing0Status()
        {
            try
            {
                // Check if legacy WinRing0 artifacts are present
                var driverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "WinRing0x64.sys");
                if (File.Exists(driverPath))
                    return "Installed";
                
                // Also check temp directory (some apps drop it there)
                var tempPath = Path.Combine(Path.GetTempPath(), "WinRing0x64.sys");
                if (File.Exists(tempPath))
                    return "Installed (temp)";
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetWinRing0Status)} failed: {ex.Message}");
            }
            return "Not Found";
        }

        [SupportedOSPlatform("windows")]
        private string GetPawnIOStatus()
        {
            try
            {
                // Check if PawnIO driver service exists
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                if (key != null)
                {
                    var start = key.GetValue("Start");
                    return start is int s ? s switch
                    {
                        0 or 1 or 2 => "Installed & Active",
                        3 => "Installed (Manual Start)",
                        4 => "Installed (Disabled)",
                        _ => "Installed"
                    } : "Installed";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetPawnIOStatus)} failed: {ex.Message}");
            }
            return "Not Installed";
        }

        [SupportedOSPlatform("windows")]
        private string GetXtuServiceStatus()
        {
            try
            {
                // Check if Intel XTU service is running
                var xtuProcesses = System.Diagnostics.Process.GetProcessesByName("XTU3Service");
                if (xtuProcesses.Length > 0)
                {
                    foreach (var p in xtuProcesses) p.Dispose();
                    return "Running";
                }
                
                // Check service registry
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\XTU3SERVICE");
                if (key != null)
                    return "Installed (Not Running)";
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetXtuServiceStatus)} failed: {ex.Message}");
            }
            return "Not Installed";
        }

        private string GetAfterburnerStatus()
        {
            try
            {
                var abProcesses = System.Diagnostics.Process.GetProcessesByName("MSIAfterburner");
                if (abProcesses.Length > 0)
                {
                    foreach (var p in abProcesses) p.Dispose();
                    return "Running";
                }
                
                // Check common install path
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (File.Exists(Path.Combine(programFiles, "MSI Afterburner", "MSIAfterburner.exe")))
                    return "Installed (Not Running)";
            }
            catch (Exception ex)
            {
                _logging.Warn($"[DiagnosticExportService] {nameof(GetAfterburnerStatus)} failed: {ex.Message}");
            }
            return "Not Installed";
        }
    }

    /// <summary>
    /// GitHub issue template generator for bug reports.
    /// Creates pre-filled issue text with diagnostic context.
    /// </summary>
    public class GitHubIssueTemplate
    {
        public static string GenerateBugReportTemplate(string issueTitle, string issueDescription, string diagnosticsZipPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## {issueTitle}");
            sb.AppendLine();
            sb.AppendLine("### Description");
            sb.AppendLine(issueDescription);
            sb.AppendLine();
            sb.AppendLine("### Environment");
            sb.AppendLine($"- **OmenCore Version**: {GetVersionFromAssembly()}");
            sb.AppendLine($"- **OS**: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"- **Time**: {DateTime.Now:O}");
            sb.AppendLine();
            sb.AppendLine("### Diagnostics");
            sb.AppendLine($"Diagnostic package attached: `{Path.GetFileName(diagnosticsZipPath)}`");
            sb.AppendLine();
            sb.AppendLine("### Steps to Reproduce");
            sb.AppendLine("1. ...");
            sb.AppendLine("2. ...");
            sb.AppendLine("3. ...");
            sb.AppendLine();
            sb.AppendLine("### Expected Behavior");
            sb.AppendLine("- ...");
            sb.AppendLine();
            sb.AppendLine("### Actual Behavior");
            sb.AppendLine("- ...");

            return sb.ToString();
        }

        private static string GetVersionFromAssembly()
        {
            return AppVersionProvider.GetVersionString();
        }
    }
}
