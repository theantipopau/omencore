using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.KeyboardLighting;
using OmenCore.Services.Rgb;
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
        private readonly HardwareMonitoringService? _hardwareMonitoringService;
        private readonly FanService? _fanService;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly Func<RgbManager?>? _rgbManagerProvider;

        public DiagnosticExportService(
            LoggingService logging,
            string logsDirectory,
            ResumeRecoveryDiagnosticsService? resumeDiagnostics = null,
            HardwareMonitoringService? hardwareMonitoringService = null,
            FanService? fanService = null,
            KeyboardLightingService? keyboardLightingService = null,
            Func<RgbManager?>? rgbManagerProvider = null)
        {
            _logging = logging;
            _logsDirectory = logsDirectory;
            _resumeDiagnostics = resumeDiagnostics;
            _hardwareMonitoringService = hardwareMonitoringService;
            _fanService = fanService;
            _keyboardLightingService = keyboardLightingService;
            _rgbManagerProvider = rgbManagerProvider;
        }

        /// <summary>
        /// Collect diagnostics: logs, system info, EC state, etc.
        /// Returns path to diagnostic bundle ZIP file.
        /// </summary>
        public async Task<string> CollectAndExportAsync(
            IEcAccess? ecAccess = null,
            LibreHardwareMonitorImpl? hwMonitor = null,
            object? wmiController = null,
            HardwareMonitoringService? monitoringService = null,
            FanService? fanService = null)
        {
            try
            {
                var effectiveMonitoringService = monitoringService ?? _hardwareMonitoringService;
                var effectiveFanService = fanService ?? _fanService;

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
                    CollectResourceFootprintSnapshotAsync(exportPath, effectiveMonitoringService, effectiveFanService),
                    CollectRuntimePerformanceSnapshotAsync(exportPath),
                    CollectBackgroundTimerSnapshotAsync(exportPath),
                    CollectRgbControlPathAsync(exportPath),
                    CollectModelIdentityTraceAsync(exportPath),
                    CollectTuningSafetySnapshotAsync(exportPath),
                    CollectEcStateAsync(exportPath, ecAccess),
                    CollectHardwareInfoAsync(exportPath, hwMonitor),
                    CollectWmiCommandHistoryAsync(exportPath, wmiController),
                    CollectTuningAndFanFocusAsync(exportPath, wmiController),
                    CollectMonitoringCadenceAndFanHoldAsync(exportPath, effectiveMonitoringService, effectiveFanService, wmiController),
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

        private async Task CollectResourceFootprintSnapshotAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== RESOURCE FOOTPRINT SNAPSHOT ===");
                sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
                sb.AppendLine("Purpose: 3.6 lightweight baseline for idle, tray, OSD, fan-hold, and page-activation measurements.");
                sb.AppendLine();

                AppendProcessFootprint(sb, Process.GetCurrentProcess(), "OmenCore App");

                sb.AppendLine("[Hardware Worker Processes]");
                var workerProcesses = GetHardwareWorkerProcesses();

                if (workerProcesses.Count == 0)
                {
                    sb.AppendLine("No hardware worker process detected.");
                }
                else
                {
                    foreach (var worker in workerProcesses)
                    {
                        AppendProcessFootprint(sb, worker, worker.ProcessName, disposeProcess: true);
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[Monitoring Cadence]");
                if (monitoringService == null)
                {
                    sb.AppendLine("Monitoring service unavailable.");
                }
                else
                {
                    sb.AppendLine($"Health: {monitoringService.HealthStatus}");
                    sb.AppendLine($"Source: {monitoringService.MonitoringSource}");
                    sb.AppendLine($"LastSampleAgeSeconds: {FormatMaybeInfiniteSeconds(monitoringService.LastSampleAge)}");
                    sb.AppendLine($"LowOverheadMode: {monitoringService.LowOverheadMode}");
                    sb.AppendLine($"CurrentCadenceReason: {monitoringService.CurrentCadenceReason}");
                    var transitions = monitoringService.GetCadenceTransitionsSnapshot();
                    sb.AppendLine($"CadenceTransitionCount: {transitions.Count}");
                    foreach (var transition in transitions.TakeLast(5))
                    {
                        sb.AppendLine($"  {transition.TimestampUtc:O} | {transition.CadenceMs}ms | {transition.Reason}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[Fan Activity Blockers]");
                if (fanService == null)
                {
                    sb.AppendLine("Fan service unavailable.");
                }
                else
                {
                    sb.AppendLine($"CurveActive: {fanService.IsCurveActive}");
                    sb.AppendLine($"HoldActive: {fanService.IsHoldActive}");
                    sb.AppendLine($"CurveOrHoldActive: {fanService.IsCurveOrHoldActive}");
                    sb.AppendLine($"CommandHistoryCount: {fanService.GetCommandHistorySnapshot().Count}");
                }

                sb.AppendLine();
                sb.AppendLine("[Background Timers]");
                var timers = BackgroundTimerRegistry.GetAll();
                sb.AppendLine($"ActiveTimerCount: {timers.Count}");
                foreach (var timer in timers)
                {
                    sb.AppendLine($"  {timer.Name} | owner={timer.OwnerService} | tier={timer.Tier} | interval={timer.IntervalMs}ms | {timer.Description}");
                }

                sb.AppendLine();
                sb.AppendLine("[Managed Runtime]");
                sb.AppendLine($"ManagedMemoryMB: {GC.GetTotalMemory(false) / 1024d / 1024d:F1}");
                sb.AppendLine($"Gen0Collections: {GC.CollectionCount(0)}");
                sb.AppendLine($"Gen1Collections: {GC.CollectionCount(1)}");
                sb.AppendLine($"Gen2Collections: {GC.CollectionCount(2)}");
                var gcInfo = GC.GetGCMemoryInfo();
                sb.AppendLine($"HeapSizeMB: {gcInfo.HeapSizeBytes / 1024d / 1024d:F1}");
                sb.AppendLine($"MemoryLoadMB: {gcInfo.MemoryLoadBytes / 1024d / 1024d:F1}");

                sb.AppendLine();
                sb.AppendLine("[Optional Subsystem Load Hints]");
                AppendAssemblyLoadHint(sb, "LibreHardwareMonitor");
                AppendAssemblyLoadHint(sb, "NvAPI");
                AppendAssemblyLoadHint(sb, "Afterburner");
                AppendAssemblyLoadHint(sb, "Corsair");
                AppendAssemblyLoadHint(sb, "Logitech");
                AppendAssemblyLoadHint(sb, "Razer");
                AppendAssemblyLoadHint(sb, "OpenRGB");
                AppendAssemblyLoadHint(sb, "RGB");

                File.WriteAllText(Path.Combine(exportPath, "resource-footprint.txt"), sb.ToString());
                _logging.Info("Collected resource footprint snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect resource footprint snapshot: {ex.Message}");
            }
        }

        private async Task CollectMonitoringCadenceAndFanHoldAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService,
            object? wmiController)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== MONITORING CADENCE + FAN HOLD SNAPSHOT ===");
                sb.AppendLine($"Captured: {DateTime.UtcNow:O}");
                sb.AppendLine();

                sb.AppendLine("[Monitoring Cadence]");
                if (monitoringService == null)
                {
                    sb.AppendLine("Monitoring service unavailable.");
                }
                else
                {
                    sb.AppendLine($"Health: {monitoringService.HealthStatus}");
                    sb.AppendLine($"Source: {monitoringService.MonitoringSource}");
                    sb.AppendLine($"LastSampleAgeSeconds: {monitoringService.LastSampleAge.TotalSeconds:F1}");
                    sb.AppendLine($"CurrentCadenceReason: {monitoringService.CurrentCadenceReason}");

                    var transitions = monitoringService.GetCadenceTransitionsSnapshot();
                    sb.AppendLine($"Cadence transitions recorded: {transitions.Count}");
                    foreach (var transition in transitions.TakeLast(12))
                    {
                        sb.AppendLine($"  {transition.TimestampUtc:O} | {transition.CadenceMs}ms | {transition.Reason}");
                        sb.AppendLine($"    flags: uiActive={transition.UiWindowActive}, trayOnly={transition.TrayOnlyMode}, overlayRealtime={transition.OverlayRealtimeMode}, lowOverhead={transition.LowOverheadMode}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[Fan Hold State]");
                if (fanService == null)
                {
                    sb.AppendLine("Fan service unavailable.");
                }
                else
                {
                    sb.AppendLine($"CurveActive: {fanService.IsCurveActive}");
                    sb.AppendLine($"HoldActive: {fanService.IsHoldActive}");
                    sb.AppendLine($"CurveOrHoldActive: {fanService.IsCurveOrHoldActive}");
                    var history = fanService.GetCommandHistorySnapshot();
                    var holdTransitions = history.Where(entry => entry.Command == "HoldStateTransition").ToList();
                    sb.AppendLine($"Fan command history entries: {history.Count}");
                    sb.AppendLine($"Hold transitions in history: {holdTransitions.Count}");
                    foreach (var transition in holdTransitions.TakeLast(12))
                    {
                        sb.AppendLine($"  {transition.TimestampUtc:O} | {transition.Target} | {transition.Details}");
                    }
                }

                if (wmiController != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("[WMI Keepalive Ownership]");
                    AppendReflectedProperty(sb, wmiController, "CountdownExtensionEnabled");
                    AppendReflectedProperty(sb, wmiController, "IsManualControlActive");
                    AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetUtc");
                    AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetDetails");
                }

                File.WriteAllText(Path.Combine(exportPath, "monitoring-cadence-hold.txt"), sb.ToString());
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect monitoring cadence + hold snapshot: {ex.Message}");
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

        private async Task CollectRgbControlPathAsync(string exportPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== RGB CONTROL PATH SNAPSHOT ===");
                sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
                sb.AppendLine("Purpose: show which RGB backends are detected, initialized, available, or likely overwritten by HP/OMEN software.");
                sb.AppendLine();

                AppendKeyboardLightingControlPath(sb);
                AppendRgbProviderControlPath(sb);
                AppendRgbConflictProcesses(sb);

                File.WriteAllText(Path.Combine(exportPath, "rgb-control-path.txt"), sb.ToString());
                _logging.Info("Collected RGB control path snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect RGB control path snapshot: {ex.Message}");
            }
        }

        private void AppendKeyboardLightingControlPath(StringBuilder sb)
        {
            sb.AppendLine("[HP Keyboard]");
            if (_keyboardLightingService == null)
            {
                sb.AppendLine("Keyboard lighting service unavailable.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"Available: {_keyboardLightingService.IsAvailable}");
            sb.AppendLine($"ActiveBackend: {_keyboardLightingService.BackendType}");
            sb.AppendLine($"PerKeyActive: {_keyboardLightingService.IsPerKey}");
            sb.AppendLine($"PerKeyCapableHardware: {_keyboardLightingService.IsPerKeyCapableHardware}");
            sb.AppendLine();
        }

        private void AppendRgbProviderControlPath(StringBuilder sb)
        {
            sb.AppendLine("[External RGB Providers]");
            var rgbManager = SafeGetRgbManager();
            if (rgbManager == null)
            {
                sb.AppendLine("RGB manager unavailable or not initialized yet.");
                sb.AppendLine("Provider startup remains lazy until the Lighting page or an explicit RGB action requests it.");
                sb.AppendLine();
                return;
            }

            var status = rgbManager.GetStatus();
            sb.AppendLine($"TotalProviders: {status.TotalProviders}");
            sb.AppendLine($"AvailableProviders: {status.AvailableProviders}");
            sb.AppendLine($"TotalDevices: {status.TotalDevices}");

            if (status.ProviderStatuses.Count == 0)
            {
                sb.AppendLine("No providers registered.");
                sb.AppendLine();
                return;
            }

            foreach (var provider in status.ProviderStatuses.OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {provider.ProviderName} ({provider.ProviderId})");
                sb.AppendLine($"  ConnectionStatus: {provider.ConnectionStatus}");
                sb.AppendLine($"  Available: {provider.IsAvailable}");
                sb.AppendLine($"  Connected: {provider.IsConnected}");
                sb.AppendLine($"  DeviceCount: {provider.DeviceCount}");
                sb.AppendLine($"  Detail: {provider.StatusDetail}");
            }

            sb.AppendLine();
        }

        private RgbManager? SafeGetRgbManager()
        {
            try
            {
                return _rgbManagerProvider?.Invoke();
            }
            catch (Exception ex)
            {
                _logging.Warn($"RGB manager provider failed during diagnostics: {ex.Message}");
                return null;
            }
        }

        private static void AppendRgbConflictProcesses(StringBuilder sb)
        {
            sb.AppendLine("[Known HP/OMEN RGB Conflict Processes]");
            var conflicts = GetRunningRgbConflictProcesses();
            if (conflicts.Count == 0)
            {
                sb.AppendLine("No known HP/OMEN RGB conflict processes detected.");
            }
            else
            {
                sb.AppendLine("Detected processes that may overwrite OmenCore keyboard lighting:");
                foreach (var conflict in conflicts)
                {
                    sb.AppendLine($"- {conflict}");
                }
            }

            sb.AppendLine();
        }

        private static List<string> GetRunningRgbConflictProcesses()
        {
            var conflicts = new List<string>();
            var candidates = new[]
            {
                ("OmenLightStudio", "OMEN Light Studio"),
                ("HPOmenLightStudio", "OMEN Light Studio"),
                ("OmenCommandCenterBackground", "OMEN Gaming Hub"),
                ("HPOmenCommandCenter", "OMEN Gaming Hub"),
            };

            foreach (var (processName, displayName) in candidates)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    if (processes.Length > 0 && !conflicts.Contains(displayName, StringComparer.OrdinalIgnoreCase))
                    {
                        conflicts.Add(displayName);
                    }
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return conflicts;
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

        private static void AppendProcessFootprint(
            StringBuilder sb,
            Process process,
            string label,
            bool disposeProcess = false)
        {
            try
            {
                sb.AppendLine($"[{label}]");
                sb.AppendLine($"Name: {SafeGet(() => process.ProcessName, "Unavailable")}");
                sb.AppendLine($"PID: {SafeGet(() => process.Id.ToString(), "Unavailable")}");
                sb.AppendLine($"StartTime: {SafeGet(() => process.StartTime.ToString("O"), "Unavailable")}");
                sb.AppendLine($"UptimeSeconds: {SafeGet(() => (DateTime.Now - process.StartTime).TotalSeconds.ToString("F0"), "Unavailable")}");
                sb.AppendLine($"TotalProcessorTime: {SafeGet(() => process.TotalProcessorTime.ToString(), "Unavailable")}");
                sb.AppendLine($"AverageCpuPercentSinceStart: {SafeGet(() => CalculateAverageCpuPercentSinceStart(process).ToString("F2"), "Unavailable")}");
                sb.AppendLine($"Threads: {SafeGet(() => process.Threads.Count.ToString(), "Unavailable")}");
                sb.AppendLine($"Handles: {SafeGet(() => process.HandleCount.ToString(), "Unavailable")}");
                sb.AppendLine($"WorkingSetMB: {SafeGet(() => (process.WorkingSet64 / 1024d / 1024d).ToString("F1"), "Unavailable")}");
                sb.AppendLine($"PrivateMemoryMB: {SafeGet(() => (process.PrivateMemorySize64 / 1024d / 1024d).ToString("F1"), "Unavailable")}");
                sb.AppendLine();
            }
            finally
            {
                if (disposeProcess)
                {
                    process.Dispose();
                }
            }
        }

        private static List<Process> GetHardwareWorkerProcesses()
        {
            var matches = new List<Process>();

            foreach (var process in Process.GetProcesses())
            {
                var keep = false;
                try
                {
                    keep = process.ProcessName.Contains("HardwareWorker", StringComparison.OrdinalIgnoreCase)
                        || process.ProcessName.Equals("OmenCore.HardwareWorker", StringComparison.OrdinalIgnoreCase);

                    if (keep)
                    {
                        matches.Add(process);
                    }
                }
                catch
                {
                    keep = false;
                }
                finally
                {
                    if (!keep)
                    {
                        process.Dispose();
                    }
                }
            }

            return matches
                .OrderBy(p => SafeGet(() => p.ProcessName, string.Empty), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double CalculateAverageCpuPercentSinceStart(Process process)
        {
            var elapsedSeconds = Math.Max((DateTime.Now - process.StartTime).TotalSeconds, 0.001);
            return process.TotalProcessorTime.TotalSeconds / elapsedSeconds / Math.Max(Environment.ProcessorCount, 1) * 100d;
        }

        private static string FormatMaybeInfiniteSeconds(TimeSpan value)
        {
            return value == TimeSpan.MaxValue ? "No successful sample yet" : value.TotalSeconds.ToString("F1");
        }

        private static void AppendAssemblyLoadHint(StringBuilder sb, string token)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => (a.GetName().Name ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"{token}: {(loaded ? "loaded" : "not loaded")}");
        }

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

        private async Task CollectTuningSafetySnapshotAsync(string exportPath)
        {
            try
            {
                var (config, source) = LoadConfigForDiagnostics();
                var undervolt = config.Undervolt ?? new UndervoltPreferences();
                var gpuOc = config.GpuOc ?? new GpuOcSettings();

                var sb = new StringBuilder();
                sb.AppendLine("=== TUNING SAFETY SNAPSHOT ===");
                sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
                sb.AppendLine("Purpose: Export saved tuning safety state without waking System Control, EC, MSR, NVAPI, or OGH paths.");
                sb.AppendLine($"Config source: {source}");
                sb.AppendLine();

                sb.AppendLine("[Startup Hardware Restore]");
                sb.AppendLine($"EnableStartupHardwareRestore: {FormatBool(config.EnableStartupHardwareRestore)}");
                sb.AppendLine($"AllowStartupRestoreOnOmen16OrVictus: {FormatBool(config.AllowStartupRestoreOnOmen16OrVictus)}");
                sb.AppendLine($"LastPerformanceModeName: {FormatValue(config.LastPerformanceModeName)}");
                sb.AppendLine($"LastGpuPowerBoostLevel: {FormatValue(config.LastGpuPowerBoostLevel)}");
                sb.AppendLine($"LastCpuPl1Watts: {FormatNullableWatts(config.LastCpuPl1Watts)}");
                sb.AppendLine($"LastCpuPl2Watts: {FormatNullableWatts(config.LastCpuPl2Watts)}");
                sb.AppendLine($"LastTccOffset: {FormatNullableDegrees(config.LastTccOffset)}");
                sb.AppendLine();

                sb.AppendLine("[CPU Undervolt / Curve Optimizer]");
                sb.AppendLine($"ApplyOnStartup: {FormatBool(undervolt.ApplyOnStartup)}");
                sb.AppendLine($"PendingTestApply: {FormatBool(undervolt.PendingTestApply)}");
                sb.AppendLine($"StartupPendingConfirmation: {FormatBool(undervolt.StartupPendingConfirmation)}");
                sb.AppendLine($"RecoveryRequiredOnNextStartup: {FormatBool(TuningStartupRecoveryGuard.ShouldSafeReset(undervolt))}");
                sb.AppendLine($"LastStartupHadUnconfirmedState: {FormatBool(undervolt.LastStartupHadUnconfirmedState)}");
                sb.AppendLine($"RespectExternalControllers: {FormatBool(undervolt.RespectExternalControllers)}");
                sb.AppendLine($"SavedDefaultOffset: {FormatUndervoltOffset(undervolt.DefaultOffset)}");
                sb.AppendLine($"PerCoreEnabled: {FormatBool(undervolt.EnablePerCoreUndervolt)}");
                sb.AppendLine($"SavedPerCoreOffsets: {FormatPerCoreOffsets(undervolt.PerCoreOffsetsMv)}");
                sb.AppendLine($"LastConfirmedOffset: {FormatUndervoltOffset(undervolt.LastConfirmedOffset)}");
                sb.AppendLine($"LastConfirmedAtUtc: {FormatDate(undervolt.LastConfirmedAtUtc)}");
                sb.AppendLine();

                sb.AppendLine("[GPU Overclock]");
                sb.AppendLine($"ApplyOnStartup: {FormatBool(gpuOc.ApplyOnStartup)}");
                sb.AppendLine($"PendingTestApply: {FormatBool(gpuOc.PendingTestApply)}");
                sb.AppendLine($"StartupPendingConfirmation: {FormatBool(gpuOc.StartupPendingConfirmation)}");
                sb.AppendLine($"RecoveryRequiredOnNextStartup: {FormatBool(TuningStartupRecoveryGuard.ShouldSafeReset(gpuOc))}");
                sb.AppendLine($"LastStartupHadUnconfirmedState: {FormatBool(gpuOc.LastStartupHadUnconfirmedState)}");
                sb.AppendLine($"SavedCoreClockOffsetMHz: {gpuOc.CoreClockOffsetMHz:+0;-0;0}");
                sb.AppendLine($"SavedMemoryClockOffsetMHz: {gpuOc.MemoryClockOffsetMHz:+0;-0;0}");
                sb.AppendLine($"SavedPowerLimitPercent: {gpuOc.PowerLimitPercent}%");
                sb.AppendLine($"SavedVoltageOffsetMv: {FormatNullableMillivolts(gpuOc.VoltageOffsetMv)}");
                sb.AppendLine($"LastConfirmedCoreClockOffsetMHz: {gpuOc.LastConfirmedCoreClockOffsetMHz:+0;-0;0}");
                sb.AppendLine($"LastConfirmedMemoryClockOffsetMHz: {gpuOc.LastConfirmedMemoryClockOffsetMHz:+0;-0;0}");
                sb.AppendLine($"LastConfirmedPowerLimitPercent: {gpuOc.LastConfirmedPowerLimitPercent}%");
                sb.AppendLine($"LastConfirmedVoltageOffsetMv: {gpuOc.LastConfirmedVoltageOffsetMv:+0;-0;0} mV");
                sb.AppendLine($"LastConfirmedAtUtc: {FormatDate(gpuOc.LastConfirmedAtUtc)}");
                sb.AppendLine($"SelectedGpuOcProfile: {FormatValue(config.LastGpuOcProfileName)}");
                sb.AppendLine($"CustomGpuOcProfileCount: {config.GpuOcProfiles?.Count ?? 0}");
                sb.AppendLine();

                sb.AppendLine("[AMD Power Limits]");
                if (config.AmdPowerLimits == null)
                {
                    sb.AppendLine("SavedAmdPowerLimits: none");
                }
                else
                {
                    sb.AppendLine($"StapmLimitWatts: {config.AmdPowerLimits.StapmLimitWatts} W");
                    sb.AppendLine($"TempLimitC: {config.AmdPowerLimits.TempLimitC} C");
                }

                File.WriteAllText(Path.Combine(exportPath, "tuning-safety.txt"), sb.ToString());
                _logging.Info("Collected tuning safety snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.WarnWithContext(
                    component: "DiagnosticExportService",
                    operation: "CollectTuningSafetySnapshotAsync",
                    message: "Failed to collect tuning safety snapshot",
                    ex: ex);
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
                    var lastMaxResetUtc = wmiController.GetType().GetProperty("LastMaxModeExternalResetUtc")?.GetValue(wmiController);
                    var lastMaxResetDetails = wmiController.GetType().GetProperty("LastMaxModeExternalResetDetails")?.GetValue(wmiController)?.ToString();

                    sb.AppendLine($"Available: {isAvailable}");
                    sb.AppendLine($"Status: {status}");
                    sb.AppendLine($"Fan Count: {fanCount}");
                    if (lastMaxResetUtc is DateTime timestampUtc)
                    {
                        sb.AppendLine($"Last Max External Reset: {timestampUtc:O}");
                        sb.AppendLine($"Last Max External Reset Detail: {lastMaxResetDetails ?? "Unknown"}");
                    }
                    else
                    {
                        sb.AppendLine("Last Max External Reset: <none recorded>");
                    }
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

        private async Task CollectTuningAndFanFocusAsync(string exportPath, object? wmiController)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== TUNING + FAN FOCUS SNAPSHOT ===");
                sb.AppendLine($"Captured: {DateTime.UtcNow:O}");
                sb.AppendLine();

                ICpuUndervoltProvider? provider = null;
                string providerBackend = "Unavailable";

                try
                {
                    provider = CpuUndervoltProviderFactory.Create(out providerBackend);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[CPU Undervolt Provider]");
                    sb.AppendLine($"Provider init failed: {ex.Message}");
                    sb.AppendLine();
                }

                try
                {
                    sb.AppendLine("[CPU Detection]");
                    sb.AppendLine($"Detected vendor: {CpuUndervoltProviderFactory.DetectedVendor}");
                    sb.AppendLine($"CPU name: {CpuUndervoltProviderFactory.CpuName}");
                    sb.AppendLine($"RyzenControl CPU name: {RyzenControl.CpuName}");
                    sb.AppendLine($"RyzenControl CPU model: {RyzenControl.CpuModel}");
                    sb.AppendLine($"Ryzen family: {RyzenControl.Family}");
                    sb.AppendLine($"Ryzen AI 9 guarded path flag: {RyzenControl.IsRyzenAi9CurveOptimizerUnsupported()}");
                    sb.AppendLine();

                    sb.AppendLine("[CPU Undervolt Provider]");
                    sb.AppendLine($"Backend info: {providerBackend}");
                    sb.AppendLine($"Provider type: {provider?.GetType().Name ?? "<none>"}");

                    if (provider is AmdUndervoltProvider amd)
                    {
                        sb.AppendLine($"AMD backend: {amd.ActiveBackend}");
                        sb.AppendLine($"AMD family: {amd.Family}");
                        sb.AppendLine($"AMD CPU: {amd.CpuName}");
                        sb.AppendLine($"AMD IsSupported: {amd.IsSupported}");
                        sb.AppendLine($"AMD SupportsIgpuCO: {amd.SupportsIgpu}");
                    }
                    else if (provider is IntelUndervoltProvider intel)
                    {
                        sb.AppendLine($"Intel backend: {intel.ActiveBackend}");
                    }

                    if (provider != null)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            var status = await provider.ProbeAsync(cts.Token);
                            sb.AppendLine("Probe status:");
                            sb.AppendLine($"  Timestamp: {status.Timestamp:O}");
                            sb.AppendLine($"  ControlledByOmenCore: {status.ControlledByOmenCore}");
                            sb.AppendLine($"  CoreOffsetMv: {status.CurrentCoreOffsetMv:+0;-0;0}");
                            sb.AppendLine($"  CacheOffsetMv: {status.CurrentCacheOffsetMv:+0;-0;0}");
                            sb.AppendLine($"  ExternalController: {status.ExternalController ?? "<none>"}");
                            sb.AppendLine($"  Warning: {status.Warning ?? "<none>"}");
                            sb.AppendLine($"  Error: {status.Error ?? "<none>"}");
                        }
                        catch (Exception probeEx)
                        {
                            sb.AppendLine($"Probe failed: {probeEx.Message}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("Probe unavailable: provider not initialized.");
                    }

                    sb.AppendLine();
                }
                finally
                {
                    if (provider is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }

                if (wmiController != null)
                {
                    sb.AppendLine("[WMI Fan Ownership]");
                    AppendReflectedProperty(sb, wmiController, "IsAvailable");
                    AppendReflectedProperty(sb, wmiController, "Status");
                    AppendReflectedProperty(sb, wmiController, "IsManualControlActive");
                    AppendReflectedProperty(sb, wmiController, "CountdownExtensionEnabled");
                    AppendReflectedProperty(sb, wmiController, "CommandsIneffective");
                    AppendReflectedProperty(sb, wmiController, "VerifyFailCount");
                    AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetUtc");
                    AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetDetails");

                    try
                    {
                        var getHistoryMethod = wmiController.GetType().GetMethod("GetCommandHistory");
                        if (getHistoryMethod?.Invoke(wmiController, null) is IEnumerable history)
                        {
                            var entries = history.Cast<object>().ToList();
                            sb.AppendLine($"Recent WMI history entries: {entries.Count}");
                            foreach (var entry in entries.Skip(Math.Max(0, entries.Count - 10)))
                            {
                                var timestamp = entry.GetType().GetProperty("Timestamp")?.GetValue(entry)?.ToString() ?? "N/A";
                                var command = entry.GetType().GetProperty("Command")?.GetValue(entry)?.ToString() ?? "N/A";
                                var success = entry.GetType().GetProperty("Success")?.GetValue(entry)?.ToString() ?? "N/A";
                                var error = entry.GetType().GetProperty("Error")?.GetValue(entry)?.ToString() ?? "";
                                sb.AppendLine($"  {timestamp} | {command} | success={success} | error={error}");
                            }
                        }
                    }
                    catch (Exception historyEx)
                    {
                        sb.AppendLine($"WMI history read failed: {historyEx.Message}");
                    }
                }
                else
                {
                    sb.AppendLine("[WMI Fan Ownership]");
                    sb.AppendLine("WMI controller unavailable.");
                }

                File.WriteAllText(Path.Combine(exportPath, "tuning-fan-focus.txt"), sb.ToString());
                _logging.Info("Collected tuning + fan focus snapshot");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect tuning + fan focus snapshot: {ex.Message}");
            }
        }

        private static void AppendReflectedProperty(StringBuilder sb, object source, string propertyName)
        {
            var property = source.GetType().GetProperty(propertyName);
            var value = property?.GetValue(source);
            sb.AppendLine($"{propertyName}: {value ?? "<unavailable>"}");
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

        private static (AppConfig Config, string Source) LoadConfigForDiagnostics()
        {
            var configDirectory = Environment.GetEnvironmentVariable("OMENCORE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                configDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OmenCore");
            }

            var configPath = Path.Combine(configDirectory, "config.json");
            if (!File.Exists(configPath))
                return (DefaultConfiguration.Create(), $"defaults (config not found: {configPath})");

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (config ?? DefaultConfiguration.Create(), configPath);
        }

        private static string FormatBool(bool value) => value ? "yes" : "no";

        private static string FormatValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "not set" : value.Trim();

        private static string FormatDate(DateTime? value) =>
            value.HasValue ? value.Value.ToUniversalTime().ToString("O") : "never";

        private static string FormatNullableWatts(int? value) =>
            value.HasValue ? $"{value.Value} W" : "not set";

        private static string FormatNullableDegrees(int? value) =>
            value.HasValue ? $"{value.Value} C" : "not set";

        private static string FormatNullableMillivolts(int? value) =>
            value.HasValue ? $"{value.Value:+0;-0;0} mV" : "not set";

        private static string FormatUndervoltOffset(UndervoltOffset? offset)
        {
            return offset == null
                ? "not set"
                : $"Core {offset.CoreMv:+0;-0;0} mV, Cache {offset.CacheMv:+0;-0;0} mV";
        }

        private static string FormatPerCoreOffsets(int?[]? offsets)
        {
            if (offsets == null || offsets.Length == 0)
                return "none";

            var active = offsets.Where(offset => offset.HasValue).ToArray();
            if (active.Length == 0)
                return $"configured ({offsets.Length} slots), all disabled";

            var min = active.Min(offset => offset!.Value);
            var max = active.Max(offset => offset!.Value);
            return $"{active.Length}/{offsets.Length} active, range {min:+0;-0;0} to {max:+0;-0;0} mV";
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
