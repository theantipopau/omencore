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
        private readonly object? _wmiController;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly Func<RgbManager?>? _rgbManagerProvider;
        private readonly RuntimeEcOperationCoordinator _ecOperationCoordinator;
        private readonly PerformanceModeService? _performanceModeService;
        private readonly HotkeyService? _hotkeyService;
        private readonly OmenKeyService? _omenKeyService;

        public DiagnosticExportService(
            LoggingService logging,
            string logsDirectory,
            ResumeRecoveryDiagnosticsService? resumeDiagnostics = null,
            HardwareMonitoringService? hardwareMonitoringService = null,
            FanService? fanService = null,
            KeyboardLightingService? keyboardLightingService = null,
            Func<RgbManager?>? rgbManagerProvider = null,
            RuntimeEcOperationCoordinator? ecOperationCoordinator = null,
            PerformanceModeService? performanceModeService = null,
            HotkeyService? hotkeyService = null,
            OmenKeyService? omenKeyService = null,
            object? wmiController = null)
        {
            _logging = logging;
            _logsDirectory = logsDirectory;
            _resumeDiagnostics = resumeDiagnostics;
            _hardwareMonitoringService = hardwareMonitoringService;
            _fanService = fanService;
            _wmiController = wmiController;
            _keyboardLightingService = keyboardLightingService;
            _rgbManagerProvider = rgbManagerProvider;
            _ecOperationCoordinator = ecOperationCoordinator ?? new RuntimeEcOperationCoordinator(logging);
            _performanceModeService = performanceModeService;
            _hotkeyService = hotkeyService;
            _omenKeyService = omenKeyService;
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
                var effectiveWmiController = wmiController ?? _wmiController;

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
                    CollectBoundedPerformanceSnapshotsAsync(exportPath),
                    CollectBackgroundTimerSnapshotAsync(exportPath),
                    CollectLaunchReadinessSnapshotAsync(exportPath, effectiveMonitoringService, effectiveFanService),
                    CollectCoreControlReadinessAsync(exportPath, effectiveMonitoringService, effectiveFanService, effectiveWmiController),
                    CollectOmenMonRebornParityAsync(exportPath, effectiveMonitoringService, effectiveFanService),
                    CollectFieldValidationScriptAsync(exportPath, effectiveMonitoringService, effectiveFanService),
                    CollectPriorityModelValidationCardsAsync(exportPath, effectiveMonitoringService, effectiveFanService),
                    CollectRcValidationMatrixAsync(exportPath),
                    CollectRgbControlPathAsync(exportPath),
                    CollectModelIdentityTraceAsync(exportPath),
                    CollectTuningSafetySnapshotAsync(exportPath),
                    CollectEcStateAsync(exportPath, ecAccess),
                    CollectHardwareInfoAsync(exportPath, hwMonitor),
                    CollectWmiCommandHistoryAsync(exportPath, effectiveWmiController),
                    CollectTuningAndFanFocusAsync(exportPath, effectiveWmiController),
                    CollectMonitoringCadenceAndFanHoldAsync(exportPath, effectiveMonitoringService, effectiveFanService, effectiveWmiController),
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

        private async Task CollectLaunchReadinessSnapshotAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== 3.8.1 LAUNCH READINESS SNAPSHOT ===");
                sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
                sb.AppendLine("Purpose: summarize the 3.8.1 field-validation state for fan recovery, performance-mode routing, CPU authority, RGB support, and hardware-worker containment.");
                sb.AppendLine();

                sb.AppendLine("[Fan Recovery]");
                if (fanService == null)
                {
                    sb.AppendLine("Fan service unavailable.");
                }
                else
                {
                    sb.AppendLine($"Backend: {fanService.Backend}");
                    sb.AppendLine($"FanWritesAvailable: {fanService.FanWritesAvailable}");
                    sb.AppendLine($"FanCurvesAvailable: {fanService.FanCurvesAvailable}");
                    sb.AppendLine($"ManualFanControlAvailable: {fanService.ManualFanControlAvailable}");
                    sb.AppendLine($"CurrentMode: {fanService.GetCurrentFanMode() ?? "<unknown>"}");
                    sb.AppendLine($"ActivePreset: {fanService.ActivePresetName ?? "<none>"}");
                    sb.AppendLine($"CurveActive: {fanService.IsCurveActive}");
                    sb.AppendLine($"HoldActive: {fanService.IsHoldActive}");
                    sb.AppendLine($"RecentFanCommandCount: {fanService.GetCommandHistorySnapshot().Count}");
                    sb.AppendLine("RecoveryAction: RestoreOemAutoControl clears OmenCore fan ownership and returns control to firmware auto mode.");
                }

                sb.AppendLine();
                sb.AppendLine("[Performance Mode Apply Trace]");
                if (_performanceModeService == null)
                {
                    sb.AppendLine("Performance mode service unavailable.");
                }
                else
                {
                    sb.Append(_performanceModeService.GetApplyTraceReport());
                }

                sb.AppendLine();
                sb.AppendLine("[CPU Temperature Authority]");
                if (monitoringService == null)
                {
                    sb.AppendLine("Monitoring service unavailable.");
                }
                else
                {
                    sb.AppendLine($"MonitoringSource: {monitoringService.MonitoringSource}");
                    sb.AppendLine($"Health: {monitoringService.HealthStatus}");
                    sb.AppendLine($"LastSampleAgeSeconds: {FormatMaybeInfiniteSeconds(monitoringService.LastSampleAge)}");
                }

                sb.AppendLine();
                sb.AppendLine("[HP Keyboard RGB]");
                if (_keyboardLightingService == null)
                {
                    sb.AppendLine("Keyboard lighting service unavailable.");
                }
                else
                {
                    sb.AppendLine($"Available: {_keyboardLightingService.IsAvailable}");
                    sb.AppendLine($"ActiveBackend: {_keyboardLightingService.BackendType}");
                    sb.AppendLine($"PerKeyActive: {_keyboardLightingService.IsPerKey}");
                    sb.AppendLine($"PerKeyCapableHardware: {_keyboardLightingService.IsPerKeyCapableHardware}");
                    if (_keyboardLightingService.IsPerKeyCapableHardware && !_keyboardLightingService.IsPerKey)
                    {
                        sb.AppendLine("PerKeyLaunchStatus: HID per-key backend/editor pending; keep zone/light-bar fallback available where supported.");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[Hardware Worker Containment]");
                sb.AppendLine("AMD ADL quarantine status is reported by HardwareWorker.log when active.");
                sb.AppendLine("Expected hybrid behavior: unstable AMD ADL-backed iGPU telemetry can be quarantined while NVIDIA, CPU, fan, memory, battery, and storage telemetry remain active.");

                File.WriteAllText(Path.Combine(exportPath, "launch-readiness.txt"), sb.ToString());
                _logging.Info("Collected 3.8.1 launch readiness snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect launch readiness snapshot: {ex.Message}");
            }
        }

        private async Task CollectCoreControlReadinessAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService,
            object? wmiController)
        {
            try
            {
                var report = BuildCoreControlReadinessReport(monitoringService, fanService, wmiController);
                File.WriteAllText(Path.Combine(exportPath, "core-control-readiness.txt"), report);
                _logging.Info("Collected core control readiness snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect core control readiness snapshot: {ex.Message}");
            }
        }

        public string BuildCoreControlReadinessReport(
            HardwareMonitoringService? monitoringService = null,
            FanService? fanService = null,
            object? wmiController = null)
        {
            var (config, source) = LoadConfigForDiagnostics();
            var sb = new StringBuilder();

            sb.AppendLine("=== CORE CONTROL READINESS ===");
            sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("Purpose: summarize whether OmenCore can safely and truthfully control fans, RGB, overclocking, and undervolting on this session.");
            sb.AppendLine($"Config source: {source}");
            sb.AppendLine();

            AppendFanReadiness(sb, fanService, wmiController, config);
            AppendRgbReadiness(sb);
            AppendTuningReadiness(sb, config);
            AppendMonitoringReadiness(sb, monitoringService);
            AppendHotkeyReadiness(sb, config);
            AppendCoreControlNextActions(sb, fanService);

            return sb.ToString();
        }

        private void AppendFanReadiness(StringBuilder sb, FanService? fanService, object? wmiController, AppConfig config)
        {
            sb.AppendLine("[Fans]");
            if (fanService == null)
            {
                sb.AppendLine("Status: service unavailable");
                sb.AppendLine("Readiness: blocked until fan service initializes");
            }
            else
            {
                var history = fanService.GetCommandHistorySnapshot();
                var lastCommand = history.LastOrDefault();

                sb.AppendLine($"Backend: {fanService.Backend}");
                sb.AppendLine($"WritesAvailable: {FormatBool(fanService.FanWritesAvailable)}");
                sb.AppendLine($"ManualDirectAvailable: {FormatBool(fanService.ManualFanControlAvailable)}");
                sb.AppendLine($"CurvesAvailable: {FormatBool(fanService.FanCurvesAvailable)}");
                sb.AppendLine($"CurrentMode: {fanService.GetCurrentFanMode() ?? "<unknown>"}");
                sb.AppendLine($"ActivePreset: {FormatValue(fanService.ActivePresetName)}");
                sb.AppendLine($"CurveActive: {FormatBool(fanService.IsCurveActive)}");
                sb.AppendLine($"HoldActive: {FormatBool(fanService.IsHoldActive)}");
                sb.AppendLine($"ThermalProtectionActive: {FormatBool(fanService.IsThermalProtectionActive)}");
                sb.AppendLine($"DiagnosticModeActive: {FormatBool(fanService.IsDiagnosticModeActive)}");
                sb.AppendLine($"SavedStartupFanPreset: {FormatValue(config.LastFanPresetName)}");
                sb.AppendLine($"SavedIndependentCurvesEnabled: {FormatBool(config.IndependentFanCurvesEnabled)}");
                sb.AppendLine($"FanCommandHistoryCount: {history.Count}");

                if (lastCommand == null)
                {
                    sb.AppendLine("LastCommand: none recorded this session");
                }
                else
                {
                    sb.AppendLine($"LastCommand: {lastCommand.TimestampUtc:O} | {(lastCommand.Success ? "OK" : "FAIL")} | {lastCommand.Command} -> {lastCommand.Target}");
                    sb.AppendLine($"LastCommandDetail: {FormatValue(lastCommand.Details)}");
                    sb.AppendLine($"LastCommandState: backend={lastCommand.Backend}; mode={lastCommand.FanMode}; preset={lastCommand.ActivePresetName ?? "<none>"}; curve={lastCommand.CurveActive}; hold={lastCommand.HoldActive}; diagnostic={lastCommand.DiagnosticModeActive}; thermal={lastCommand.ThermalProtectionActive}");
                    sb.AppendLine($"LastCommandModel: {FormatValue(lastCommand.ModelName)} ({FormatValue(lastCommand.ProductId)})");
                    sb.AppendLine($"LastCommandGates: writes={FormatBool(lastCommand.FanWritesAvailable)}; curves={FormatBool(lastCommand.FanCurvesAvailable)}; manual={FormatBool(lastCommand.ManualFanControlAvailable)}; desktopBlocked={FormatBool(lastCommand.DesktopFanWritesBlocked)}");
                    sb.AppendLine($"LastCommandReadback: {FormatValue(lastCommand.TelemetrySummary)}; rawPrimaryRpm={FormatNullableInt(lastCommand.RawPrimaryFanRpm)}; reportedPrimaryDuty={FormatNullableInt(lastCommand.ReportedPrimaryFanDutyPercent)}");
                }
            }

            if (wmiController != null)
            {
                sb.AppendLine("WmiController:");
                AppendReflectedProperty(sb, wmiController, "IsAvailable");
                AppendReflectedProperty(sb, wmiController, "Status");
                AppendReflectedProperty(sb, wmiController, "FanCount");
                AppendReflectedProperty(sb, wmiController, "IsManualControlActive");
                AppendReflectedProperty(sb, wmiController, "CommandsIneffective");
                AppendReflectedProperty(sb, wmiController, "VerifyFailCount");
                AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetUtc");
                AppendReflectedProperty(sb, wmiController, "LastMaxModeExternalResetDetails");
            }

            sb.AppendLine("RecoveryAction: use Restore OEM Auto before retesting Max/Direct/Curve if ownership or RPM readback looks wrong.");
            sb.AppendLine();
        }

        private void AppendRgbReadiness(StringBuilder sb)
        {
            sb.AppendLine("[RGB]");
            var (config, _) = LoadConfigForDiagnostics();
            var observedRgb = config.KeyboardLighting ?? new KeyboardLightingSettings();
            if (_keyboardLightingService == null)
            {
                sb.AppendLine("HpKeyboardService: unavailable");
            }
            else
            {
                sb.AppendLine($"HpKeyboardAvailable: {FormatBool(_keyboardLightingService.IsAvailable)}");
                sb.AppendLine($"HpKeyboardBackend: {_keyboardLightingService.BackendType}");
                sb.AppendLine($"HpKeyboardPerKeyActive: {FormatBool(_keyboardLightingService.IsPerKey)}");
                sb.AppendLine($"HpKeyboardPerKeyCapableHardware: {FormatBool(_keyboardLightingService.IsPerKeyCapableHardware)}");
                sb.AppendLine($"LastApplySurface: {_keyboardLightingService.LastApplySurface}");
                sb.AppendLine($"LastApplyStatus: {_keyboardLightingService.LastApplyStatus}");
                sb.AppendLine("ProbeGuidance: apply a safe obvious static color and record whether keyboard zones, per-key keys, light bar, or only backlight changed.");
            }
            AppendRgbObservedSurface(sb, observedRgb);

            var rgbManager = SafeGetRgbManager();
            if (rgbManager == null)
            {
                sb.AppendLine("ExternalProviders: RGB manager unavailable or lazy-not-initialized");
            }
            else
            {
                var status = rgbManager.GetStatus();
                sb.AppendLine($"ExternalProviderCount: {status.TotalProviders}");
                sb.AppendLine($"ExternalProvidersAvailable: {status.AvailableProviders}");
                sb.AppendLine($"ExternalRgbDeviceCount: {status.TotalDevices}");
                foreach (var provider in status.ProviderStatuses.OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"  {provider.ProviderName}: available={provider.IsAvailable}; connected={provider.IsConnected}; devices={provider.DeviceCount}; status={provider.ConnectionStatus}; detail={provider.StatusDetail}");
                }
            }

            var conflicts = GetRunningRgbConflictProcesses();
            sb.AppendLine($"KnownHpRgbConflictProcesses: {(conflicts.Count == 0 ? "none" : string.Join(", ", conflicts))}");
            sb.AppendLine();
        }

        private void AppendTuningReadiness(StringBuilder sb, AppConfig config)
        {
            var undervolt = config.Undervolt ?? new UndervoltPreferences();
            var gpuOc = config.GpuOc ?? new GpuOcSettings();

            sb.AppendLine("[Tuning / OC / Undervolt]");
            sb.AppendLine($"StartupHardwareRestoreEnabled: {FormatBool(config.EnableStartupHardwareRestore)}");
            sb.AppendLine($"StartupRestoreCategories: {StartupRestorePolicy.BuildSummary(config)}");
            sb.AppendLine($"StartupRestoreExtraGuardAllowed: {FormatBool(config.AllowStartupRestoreOnOmen16OrVictus)}");
            sb.AppendLine($"SavedPerformanceMode: {FormatValue(config.LastPerformanceModeName)}");
            sb.AppendLine($"SavedGpuPowerBoostLevel: {FormatValue(config.LastGpuPowerBoostLevel)}");
            sb.AppendLine($"SavedCpuPL1: {FormatNullableWatts(config.LastCpuPl1Watts)}");
            sb.AppendLine($"SavedCpuPL2: {FormatNullableWatts(config.LastCpuPl2Watts)}");
            sb.AppendLine($"SavedTccOffset: {FormatNullableDegrees(config.LastTccOffset)}");
            sb.AppendLine($"SavedGpuOcProfile: {FormatValue(config.LastGpuOcProfileName)}");
            sb.AppendLine($"UndervoltApplyOnStartup: {FormatBool(undervolt.ApplyOnStartup)}");
            sb.AppendLine($"UndervoltPendingConfirmation: {FormatBool(undervolt.StartupPendingConfirmation)}");
            sb.AppendLine($"UndervoltRecoveryRequired: {FormatBool(TuningStartupRecoveryGuard.ShouldSafeReset(undervolt))}");
            sb.AppendLine($"GpuOcApplyOnStartup: {FormatBool(gpuOc.ApplyOnStartup)}");
            sb.AppendLine($"GpuOcPendingConfirmation: {FormatBool(gpuOc.StartupPendingConfirmation)}");
            sb.AppendLine($"GpuOcRecoveryRequired: {FormatBool(TuningStartupRecoveryGuard.ShouldSafeReset(gpuOc))}");
            sb.AppendLine($"RollbackBundleAvailable: yes");
            sb.AppendLine($"RollbackTargets: performance={TuningRollbackCoordinator.SafePerformanceMode}; fan={TuningRollbackCoordinator.SafeFanPresetName}; gpuPower={TuningRollbackCoordinator.SafeGpuPowerBoostLevel}; tcc={TuningRollbackCoordinator.SafeTccOffset} C; undervolt=0 mV; gpuOc=0 MHz/100%; amdStapm={TuningRollbackCoordinator.SafeAmdStapmWatts} W; amdTemp={TuningRollbackCoordinator.SafeAmdTempLimitC} C");
            sb.AppendLine($"RollbackCpuPowerTarget: PL1={FormatNullableWatts(config.LastCpuPl1Watts)}; PL2={FormatNullableWatts(config.LastCpuPl2Watts)}; if startup-read values are unavailable, saved PL restore is cleared");

            sb.AppendLine("PerformanceApplyTrace:");
            if (_performanceModeService == null)
            {
                sb.AppendLine("  Performance mode service unavailable.");
            }
            else
            {
                var trace = _performanceModeService.GetApplyTraceSnapshot();
                sb.AppendLine($"  TraceCount: {trace.Count}");
                foreach (var entry in trace.TakeLast(5))
                {
                    sb.AppendLine($"  {entry.TimestampUtc:O} | requested={entry.RequestedModeName} | effective={entry.EffectiveModeName} | ecPowerApplied={entry.EcPowerLimitApplied} | wmiFallbackApplied={entry.WmiPolicyFallbackApplied} | fanAction={entry.FanPolicyAction}");
                }
            }

            sb.AppendLine("ReadbackRule: every tuning apply should show requested value, readback value, and locked/unsupported reason before being considered verified.");
            sb.AppendLine();
        }

        private static void AppendMonitoringReadiness(StringBuilder sb, HardwareMonitoringService? monitoringService)
        {
            sb.AppendLine("[Monitoring / Readback]");
            if (monitoringService == null)
            {
                sb.AppendLine("MonitoringService: unavailable");
            }
            else
            {
                sb.AppendLine($"Source: {monitoringService.MonitoringSource}");
                sb.AppendLine($"Health: {monitoringService.HealthStatus}");
                sb.AppendLine($"LastSampleAgeSeconds: {FormatMaybeInfiniteSeconds(monitoringService.LastSampleAge)}");
                sb.AppendLine($"CadenceReason: {monitoringService.CurrentCadenceReason}");
                sb.AppendLine($"LowOverheadMode: {FormatBool(monitoringService.LowOverheadMode)}");
            }
            sb.AppendLine();
        }

        private void AppendHotkeyReadiness(StringBuilder sb, AppConfig config)
        {
            sb.AppendLine("[Hotkeys / OMEN Key]");
            sb.AppendLine($"ConfigHotkeysEnabled: {FormatBool(config.Monitoring?.HotkeysEnabled ?? true)}");
            sb.AppendLine($"ConfigWindowFocusedHotkeys: {FormatBool(config.Monitoring?.WindowFocusedHotkeys ?? true)}");
            sb.AppendLine($"ConfigOmenKeyInterceptionEnabled: {FormatBool(config.Features?.OmenKeyInterceptionEnabled ?? config.OmenKeyEnabled)}");
            sb.AppendLine($"ConfigOmenKeyAction: {FormatValue(config.Features?.OmenKeyAction ?? config.OmenKeyAction)}");
            sb.AppendLine($"ConfigStrictOmenKeyMode: {FormatBool(config.StrictOmenKeyMode)}");
            sb.AppendLine($"ConfigFirmwareFnPProfileCycle: {FormatBool(config.Features?.EnableFirmwareFnPProfileCycle == true)}");
            sb.AppendLine($"ConfigSuppressHotkeysInRdp: {FormatBool(config.Features?.SuppressHotkeysInRdp == true)}");

            if (_hotkeyService == null)
            {
                sb.AppendLine("HotkeyService: unavailable");
            }
            else
            {
                var hotkeys = _hotkeyService.GetDiagnosticSnapshot();
                sb.AppendLine($"HotkeyServiceEnabled: {FormatBool(hotkeys.Enabled)}");
                sb.AppendLine($"HotkeyWindowHandleReady: {FormatBool(hotkeys.WindowHandleReady)}");
                sb.AppendLine($"RegisteredHotkeyCount: {hotkeys.RegisteredCount}");
                sb.AppendLine($"PendingHotkeyCount: {hotkeys.PendingCount}");
                AppendHotkeyBindings(sb, "RegisteredHotkeys", hotkeys.RegisteredBindings);
                AppendHotkeyBindings(sb, "PendingHotkeys", hotkeys.PendingBindings);
            }

            if (_omenKeyService == null)
            {
                sb.AppendLine("OmenKeyService: unavailable");
            }
            else
            {
                var omenKey = _omenKeyService.GetDiagnosticSnapshot();
                sb.AppendLine($"OmenKeyEnabled: {FormatBool(omenKey.Enabled)}");
                sb.AppendLine($"OmenKeyAction: {omenKey.Action}");
                sb.AppendLine($"OmenKeyExternalAppConfigured: {FormatBool(omenKey.ExternalAppConfigured)}");
                sb.AppendLine($"OmenKeyHookActive: {FormatBool(omenKey.HookActive)}");
                sb.AppendLine($"OmenKeyWmiWatcherActive: {FormatBool(omenKey.WmiWatcherActive)}");
                sb.AppendLine($"OmenKeyStrictMode: {FormatBool(omenKey.StrictMode)}");
                sb.AppendLine($"OmenKeyFirmwareFnPProfileCycleEnabled: {FormatBool(omenKey.FirmwareFnPProfileCycleEnabled)}");
                sb.AppendLine($"OmenKeySuppressInRdp: {FormatBool(omenKey.SuppressInRdp)}");
                if (omenKey.LastNeverInterceptAgeMs.HasValue)
                {
                    sb.AppendLine($"LastNeverInterceptKey: vk=0x{omenKey.LastNeverInterceptVkCode:X2}; scan=0x{omenKey.LastNeverInterceptScanCode:X4}; ageMs={omenKey.LastNeverInterceptAgeMs.Value:F0}");
                }
                else
                {
                    sb.AppendLine("LastNeverInterceptKey: none recorded");
                }

                if (omenKey.LastCandidateAccepted.HasValue)
                {
                    sb.AppendLine($"LastOmenKeyCandidate: source={omenKey.LastCandidateSource}; vk=0x{omenKey.LastCandidateVkCode:X2}; scan=0x{omenKey.LastCandidateScanCode:X4}; accepted={FormatBool(omenKey.LastCandidateAccepted.Value)}; reason={omenKey.LastCandidateReason}; ageMs={omenKey.LastCandidateAgeMs:F0}");
                }
                else
                {
                    sb.AppendLine("LastOmenKeyCandidate: none recorded");
                }
            }

            sb.AppendLine("ValidationRule: press the physical OMEN key and profile-cycle hotkey once, then verify logs show the expected hook/WMI source and no never-intercept suppression.");
            sb.AppendLine();
        }

        private static void AppendHotkeyBindings(StringBuilder sb, string label, HotkeyDiagnosticBinding[] bindings)
        {
            if (bindings.Length == 0)
            {
                sb.AppendLine($"{label}: none");
                return;
            }

            sb.AppendLine($"{label}:");
            foreach (var binding in bindings)
            {
                var id = binding.Id > 0 ? binding.Id.ToString() : "pending";
                sb.AppendLine($"  {binding.Action}: {binding.Chord}; id={id}; enabled={FormatBool(binding.IsEnabled)}");
            }
        }

        private static void AppendCoreControlNextActions(StringBuilder sb, FanService? fanService)
        {
            sb.AppendLine("[Suggested Next Validation Actions]");
            if (fanService?.FanWritesAvailable == true)
            {
                sb.AppendLine("- Fan: Restore OEM Auto, then test Max, Direct 40/60/80%, Auto, and a curve ramp while watching RPM/level readback.");
            }
            else
            {
                sb.AppendLine("- Fan: export model identity and backend status before enabling any manual/curve controls.");
            }
            sb.AppendLine("- RGB: apply a safe obvious static color and record which physical surface changed.");
            sb.AppendLine("- Tuning: apply one setting at a time and capture requested/readback/locked state after each apply.");
            sb.AppendLine("- Hotkeys: test Ctrl+Shift profile/fan hotkeys plus the physical OMEN key, then confirm hook/WMI source in diagnostics.");
            sb.AppendLine("- Startup restore: keep disabled until manual readback passes for fan/performance/RGB/tuning on this model.");
        }

        private async Task CollectOmenMonRebornParityAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService)
        {
            try
            {
                var report = BuildOmenMonRebornParityReport(monitoringService, fanService);
                File.WriteAllText(Path.Combine(exportPath, "omenmon-reborn-parity.txt"), report);
                _logging.Info("Collected OmenMon-Reborn parity snapshot");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect OmenMon-Reborn parity snapshot: {ex.Message}");
            }
        }

        public string BuildOmenMonRebornParityReport(
            HardwareMonitoringService? monitoringService = null,
            FanService? fanService = null)
        {
            var (config, source) = LoadConfigForDiagnostics();
            var systemInfo = SafeGetSystemInfo();
            var identity = ModelIdentityResolutionService.Build(systemInfo, capabilities: null, logging: _logging);
            var calibration = new FanCalibrationStorageService(_logging);
            var normalizedModelId = FanCalibrationStorageService.NormalizeModelId(
                identity.CapabilityProductId != "Unknown"
                    ? identity.CapabilityProductId
                    : identity.RawWmiModel);
            var hasCalibration = calibration.HasCalibration(normalizedModelId);

            var sb = new StringBuilder();
            sb.AppendLine("=== OMENMON-REBORN PARITY SNAPSHOT ===");
            sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("Purpose: map user expectations from OmenMon-Reborn onto OmenCore's current safe-control surface without importing GPL implementation code.");
            sb.AppendLine($"Config source: {source}");
            sb.AppendLine();

            sb.AppendLine("[Source Expectations]");
            sb.AppendLine("- Lightweight background operation with command/probe-style diagnostics.");
            sb.AppendLine("- Dynamic model truthfulness instead of unsafe hardcoded EC layouts.");
            sb.AppendLine("- Read-only unknown-model probing before any writes.");
            sb.AppendLine("- Fan calibration evidence for RPM/register/layout promotion.");
            sb.AppendLine("- EC contention hardening and clean failed-read handling.");
            sb.AppendLine("- Fan, RGB/backlight, OMEN key, and compact tray workflows.");
            sb.AppendLine();

            sb.AppendLine("[OmenCore Current Equivalents]");
            sb.AppendLine($"ModelIdentity: {identity.Summary}");
            sb.AppendLine($"IdentityConfidence: {identity.Confidence}; source={identity.ResolutionSource}; userVerified={FormatBool(identity.IsUserVerified)}");
            sb.AppendLine($"CapabilityProductId: {identity.CapabilityProductId}");
            sb.AppendLine($"ModelWarning: {FormatValue(identity.WarningText)}");
            sb.AppendLine($"FanBackend: {fanService?.Backend ?? "unavailable"}");
            sb.AppendLine($"FanWritesAvailable: {FormatBool(fanService?.FanWritesAvailable == true)}");
            sb.AppendLine($"ManualDirectAvailable: {FormatBool(fanService?.ManualFanControlAvailable == true)}");
            sb.AppendLine($"FanCurvesAvailable: {FormatBool(fanService?.FanCurvesAvailable == true)}");
            sb.AppendLine($"MonitoringSource: {monitoringService?.MonitoringSource ?? "unavailable"}");
            sb.AppendLine($"MonitoringHealth: {monitoringService?.HealthStatus.ToString() ?? "unavailable"}");
            sb.AppendLine($"LowOverheadMode: {FormatBool(monitoringService?.LowOverheadMode == true)}");
            sb.AppendLine($"StartupHardwareRestoreEnabled: {FormatBool(config.EnableStartupHardwareRestore)}");
            sb.AppendLine($"FanCalibrationModelKey: {normalizedModelId}");
            sb.AppendLine($"FanCalibrationAvailable: {FormatBool(hasCalibration)}");
            sb.AppendLine("EcCoordination: runtime EC operation coordinator serializes OmenCore EC sections; PawnIO backend uses Global\\Access_EC for cross-process EC access.");
            sb.AppendLine();

            sb.AppendLine("[Parity Matrix]");
            AppendParityRow(sb, "Probe report", "Implemented",
                "Diagnostic export includes identity-resolution-trace.txt, core-control-readiness.txt, launch-readiness.txt, rgb-control-path.txt, tuning-safety.txt, and this parity file.");
            AppendParityRow(sb, "Lightweight background mode", "Partial",
                "Low-overhead monitoring/tray cadence exists; a dedicated fan-only profile is still planned.");
            AppendParityRow(sb, "Dynamic model database", identity.IsKnownModel ? "Implemented" : "Partial",
                identity.IsKnownModel
                    ? "Exact or inferred model capability resolution exists for this session."
                    : "Unknown/fallback identity is visible, but OmenCore will not promote unsafe write paths without evidence.");
            AppendParityRow(sb, "Unknown-model read-only fallback", "Partial",
                "OmenCore exports identity and readiness data; full EC layout auto-detection remains evidence-gated.");
            AppendParityRow(sb, "Auto-calibration wizard", hasCalibration ? "Implemented" : "Partial",
                hasCalibration
                    ? "A stored fan calibration profile exists for the normalized model key."
                    : "Fan calibration storage exists, but this session has no stored calibration profile for the normalized model key.");
            AppendParityRow(sb, "EC contention hardening", "Implemented",
                "Shared runtime EC coordinator and PawnIO Global\\Access_EC mutex are present; direct read-path garbage handling remains backend-specific.");
            AppendParityRow(sb, "Fan direct/profile control", fanService?.FanWritesAvailable == true ? "Implemented" : "Degraded",
                fanService?.FanWritesAvailable == true
                    ? "Fan writes are available through the active backend."
                    : "Fan service or write path is unavailable in this diagnostic context.");
            AppendParityRow(sb, "RGB/backlight surface clarity", _keyboardLightingService != null ? "Partial" : "Degraded",
                _keyboardLightingService != null
                    ? $"HP keyboard backend={_keyboardLightingService.BackendType}; lastSurface={_keyboardLightingService.LastApplySurface}; lastStatus={_keyboardLightingService.LastApplyStatus}"
                    : "Keyboard lighting service unavailable in this diagnostic context.");
            AppendParityRow(sb, "OMEN key and profile cycling", _hotkeyService != null || _omenKeyService != null ? "Implemented" : "Partial",
                "Core-control readiness exports registered/pending hotkeys and OMEN-key hook/WMI watcher state when services are available.");
            sb.AppendLine();

            sb.AppendLine("[Safe Emulation Policy]");
            sb.AppendLine("- Emulate behavior and diagnostics, not GPL source code.");
            sb.AppendLine("- Keep unknown EC layouts read-only until a monotonic RPM/readback/calibration report proves the mapping.");
            sb.AppendLine("- Prefer WMI/firmware APIs for MAX-series and other safety-gated boards.");
            sb.AppendLine("- Do not promote a model from degraded to verified without ProductId, backend, requested value, readback value, and recovery result.");
            sb.AppendLine();

            sb.AppendLine("[Next Evidence To Collect]");
            sb.AppendLine("- Export this bundle after testing Auto, Max, Direct 40/60/80%, curve ramp, and Restore OEM Auto.");
            sb.AppendLine("- Attach core-control-readiness.txt plus fan command history for fan issues.");
            sb.AppendLine("- Attach rgb-control-path.txt after applying one obvious static color for RGB/backlight issues.");
            sb.AppendLine("- For unknown/fallback models, include identity-resolution-trace.txt and any fan calibration output before requesting write-path promotion.");

            return sb.ToString();
        }

        private static void AppendParityRow(StringBuilder sb, string feature, string status, string notes)
        {
            sb.AppendLine($"- {feature}: {status} - {notes}");
        }

        private SystemInfo SafeGetSystemInfo()
        {
            try
            {
                return new SystemInfoService(_logging).GetSystemInfo();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect system info for OmenMon-Reborn parity report: {ex.Message}");
                return new SystemInfo();
            }
        }

        private async Task CollectFieldValidationScriptAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService)
        {
            try
            {
                var report = BuildFieldValidationScriptReport(monitoringService, fanService);
                File.WriteAllText(Path.Combine(exportPath, "field-validation-script.txt"), report);
                _logging.Info("Collected field validation script");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect field validation script: {ex.Message}");
            }
        }

        public string BuildFieldValidationScriptReport(
            HardwareMonitoringService? monitoringService = null,
            FanService? fanService = null)
        {
            var (config, source) = LoadConfigForDiagnostics();
            var systemInfo = SafeGetSystemInfo();
            var identity = ModelIdentityResolutionService.Build(systemInfo, capabilities: null, logging: _logging);
            var priorityBoard = ClassifyPriorityBoard(identity.CapabilityProductId, identity.RawBaseboardProduct, identity.RawWmiModel);

            var sb = new StringBuilder();
            sb.AppendLine("=== FIELD VALIDATION SCRIPT ===");
            sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("Purpose: repeatable v3.8.1 release-gate smoke script for fan control, RGB, performance mode, profile cycling, hotkeys, and startup restore.");
            sb.AppendLine($"Config source: {source}");
            sb.AppendLine();

            sb.AppendLine("[System Under Test]");
            sb.AppendLine($"ResolvedModel: {identity.Summary}");
            sb.AppendLine($"ProductId: {identity.CapabilityProductId}");
            sb.AppendLine($"PriorityBoard: {priorityBoard}");
            sb.AppendLine($"IdentityConfidence: {identity.Confidence}; source={identity.ResolutionSource}; userVerified={FormatBool(identity.IsUserVerified)}");
            sb.AppendLine($"FanBackend: {fanService?.Backend ?? "unavailable"}");
            sb.AppendLine($"FanWritesAvailable: {FormatBool(fanService?.FanWritesAvailable == true)}");
            sb.AppendLine($"DirectFanAvailable: {FormatBool(fanService?.ManualFanControlAvailable == true)}");
            sb.AppendLine($"FanCurvesAvailable: {FormatBool(fanService?.FanCurvesAvailable == true)}");
            sb.AppendLine($"MonitoringSource: {monitoringService?.MonitoringSource ?? "unavailable"}");
            sb.AppendLine($"MonitoringHealth: {monitoringService?.HealthStatus.ToString() ?? "unavailable"}");
            sb.AppendLine($"StartupRestoreEnabled: {FormatBool(config.EnableStartupHardwareRestore)}");
            sb.AppendLine();

            sb.AppendLine("[Before You Start]");
            sb.AppendLine("1. Keep AC power connected and close HP OMEN Gaming Hub, OMEN Light Studio, OpenRGB, and other RGB/fan tools.");
            sb.AppendLine("2. Open OmenCore and wait 30 seconds on Dashboard so monitoring source/health can settle.");
            sb.AppendLine("3. Export diagnostics once before changing controls; keep that as the baseline bundle.");
            sb.AppendLine("4. If any fan command behaves strangely, click Restore OEM Auto before the next test.");
            sb.AppendLine();

            sb.AppendLine("[Fan Validation]");
            sb.AppendLine("1. Restore OEM Auto; record current mode, fan RPM, and fan level readback after 30 seconds.");
            sb.AppendLine("2. Apply Max; hold for 10 minutes or the longest safe practical window, recording whether RPM/level drops or firmware reclaims control.");
            sb.AppendLine("3. If Direct is available, test 40%, 60%, and 80%; wait 30 seconds after each Apply and record requested %, RPM, fan level, and any verification warning.");
            sb.AppendLine("4. If custom curves are available, apply a simple ramp and confirm the UI reports curve ownership without hiding stale/unavailable telemetry.");
            sb.AppendLine("5. Restore OEM Auto again and confirm fans return to BIOS/OEM behavior.");
            sb.AppendLine();

            sb.AppendLine("[RGB / Surface Validation]");
            sb.AppendLine("1. Apply one obvious static color such as red to HP keyboard lighting.");
            sb.AppendLine("2. Record which physical surface changed: per-key keyboard, four-zone keyboard, single backlight, light bar, external device, or nothing.");
            sb.AppendLine("3. Apply a second obvious color such as blue and confirm whether the same surface changes.");
            sb.AppendLine("4. Export diagnostics and include rgb-control-path.txt plus core-control-readiness.txt.");
            sb.AppendLine();

            sb.AppendLine("[Performance / Tuning Validation]");
            sb.AppendLine("1. Record current mode, CPU package power, PL1/PL2 where available, GPU power boost state, and any locked/unsupported text.");
            sb.AppendLine("2. Apply Quiet/Balanced/Performance/Max modes that are exposed for this model; after each, record requested mode, effective mode, readback, and WMI fallback state.");
            sb.AppendLine("3. Do not enable startup restore for fan/performance/RGB/tuning until manual readback passes on this model.");
            sb.AppendLine();

            sb.AppendLine("[Profile Cycling And Hotkeys]");
            sb.AppendLine("1. Test Ctrl+Shift+F for fan mode cycling and Ctrl+Shift+E for General profile cycling.");
            sb.AppendLine("2. Test Ctrl+Shift+P for performance mode cycling and Win+F12 as the window-open fallback.");
            sb.AppendLine("3. Press the physical OMEN key once; record whether OmenCore handles it, HP software handles it, or nothing happens.");
            sb.AppendLine("4. Export diagnostics and include the Hotkeys / OMEN Key section from core-control-readiness.txt.");
            sb.AppendLine();

            sb.AppendLine("[Startup Restore Validation]");
            sb.AppendLine("1. Leave startup restore disabled until fan, performance, RGB, and tuning readbacks are manually verified.");
            sb.AppendLine("2. Enable only one startup-restore category at a time where the UI allows category-specific restore.");
            sb.AppendLine("3. Restart Windows, wait 60 seconds after login, then record applied state, readback state, and any recovery warning.");
            sb.AppendLine("4. Use the safe restore/Restore OEM Auto path immediately if fans, power, RGB, or tuning state looks wrong.");
            sb.AppendLine();

            sb.AppendLine("[Evidence To Attach]");
            sb.AppendLine("- core-control-readiness.txt");
            sb.AppendLine("- field-validation-script.txt");
            sb.AppendLine("- omenmon-reborn-parity.txt");
            sb.AppendLine("- rgb-control-path.txt for lighting reports");
            sb.AppendLine("- wmi-command-history.txt and tuning-fan-focus.txt for fan/performance reports");
            sb.AppendLine("- identity-resolution-trace.txt for unknown, fallback, or unverified models");

            return sb.ToString();
        }

        private static string ClassifyPriorityBoard(string productId, string baseboardProduct, string model)
        {
            var raw = $"{productId} {baseboardProduct} {model}";
            var priority = new[] { "8D41", "8D87", "8BD4", "8C30", "8DCD", "878C", "8600", "8BCD" };
            var match = priority.FirstOrDefault(id => raw.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }

            if (raw.Contains("OMEN 17", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("db-1000", StringComparison.OrdinalIgnoreCase))
            {
                return "OMEN 17 db-1000";
            }

            if (raw.Contains("Victus", StringComparison.OrdinalIgnoreCase))
            {
                return "Victus 15/16 field cohort";
            }

            return "Not in v3.8.1 priority board list";
        }

        private async Task CollectPriorityModelValidationCardsAsync(
            string exportPath,
            HardwareMonitoringService? monitoringService,
            FanService? fanService)
        {
            try
            {
                var report = BuildPriorityModelValidationCardsReport(monitoringService, fanService);
                File.WriteAllText(Path.Combine(exportPath, "priority-model-validation-cards.txt"), report);
                _logging.Info("Collected priority model validation cards");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect priority model validation cards: {ex.Message}");
            }
        }

        public string BuildPriorityModelValidationCardsReport(
            HardwareMonitoringService? monitoringService = null,
            FanService? fanService = null)
        {
            var (config, source) = LoadConfigForDiagnostics();
            var systemInfo = SafeGetSystemInfo();
            var identity = ModelIdentityResolutionService.Build(systemInfo, capabilities: null, logging: _logging);
            var priorityBoard = ClassifyPriorityBoard(identity.CapabilityProductId, identity.RawBaseboardProduct, identity.RawWmiModel);

            var sb = new StringBuilder();
            sb.AppendLine("=== PRIORITY MODEL VALIDATION CARDS ===");
            sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("Purpose: model-family validation cards for the v3.8.1 RC gate. Use the matching card first, then the generic field-validation-script.txt.");
            sb.AppendLine($"Config source: {source}");
            sb.AppendLine();

            sb.AppendLine("[Detected Context]");
            sb.AppendLine($"ResolvedModel: {identity.Summary}");
            sb.AppendLine($"ProductId: {identity.CapabilityProductId}");
            sb.AppendLine($"PriorityBoard: {priorityBoard}");
            sb.AppendLine($"FanBackend: {fanService?.Backend ?? "unavailable"}");
            sb.AppendLine($"FanWritesAvailable: {FormatBool(fanService?.FanWritesAvailable == true)}");
            sb.AppendLine($"DirectFanAvailable: {FormatBool(fanService?.ManualFanControlAvailable == true)}");
            sb.AppendLine($"FanCurvesAvailable: {FormatBool(fanService?.FanCurvesAvailable == true)}");
            sb.AppendLine($"MonitoringSource: {monitoringService?.MonitoringSource ?? "unavailable"}");
            sb.AppendLine($"StartupRestoreEnabled: {FormatBool(config.EnableStartupHardwareRestore)}");
            sb.AppendLine();

            AppendModelValidationCard(
                sb,
                "8D41 / OMEN Max 16-ah0xxx",
                priorityBoard == "8D41",
                "Validate WMI-only Max fan hold after first-low-sample reassertion.",
                new[]
                {
                    "Confirm Direct EC/legacy fan writes remain disabled; backend should be WMI-only.",
                    "Run Max for 10 minutes under load and record every visible RPM/level drop.",
                    "Restore OEM Auto and confirm fan ownership releases cleanly.",
                    "Attach wmi-command-history.txt, core-control-readiness.txt, and field-validation-script.txt."
                });

            AppendModelValidationCard(
                sb,
                "8D87 / OMEN Max follow-up",
                priorityBoard == "8D87",
                "Validate long-session Max/Direct obedience and RGB surface routing.",
                new[]
                {
                    "Run Max for at least 10 minutes and note whether fans become less obedient after initial success.",
                    "If Direct is visible, test 40/60/80% and compare requested level to RPM/level readback.",
                    "Apply one obvious RGB color and record keyboard vs light-bar behavior.",
                    "Attach rgb-control-path.txt, wmi-command-history.txt, and core-control-readiness.txt."
                });

            AppendModelValidationCard(
                sb,
                "8BD4 / Victus 16-s0xxx",
                priorityBoard == "8BD4",
                "Validate conservative WMI V1 handoff without manual-zero floor clear.",
                new[]
                {
                    "Test Auto -> Max -> Auto and confirm fans do not stick at 0/200 RPM or high RPM.",
                    "Run a custom curve or high Direct request if available, then Restore OEM Auto.",
                    "Capture a long-session gaming report if fans previously stuck at max or stopped reacting.",
                    "Attach fan command history, fan verification output, and core-control-readiness.txt."
                });

            AppendModelValidationCard(
                sb,
                "8DCD / Victus 15",
                priorityBoard == "8DCD",
                "Validate WMI thermal-policy fallback for Performance mode before adding watt overrides.",
                new[]
                {
                    "Record Balanced and Performance CPU package power under the same load.",
                    "Capture before/after PL1/PL2 readback where available.",
                    "Confirm whether Performance still EC-limits around 40W.",
                    "Attach tuning-fan-focus.txt, core-control-readiness.txt, and any performance apply trace."
                });

            AppendModelValidationCard(
                sb,
                "8C30 / Victus 15-fb1xxx",
                priorityBoard == "8C30",
                "Validate Performance/Balanced/Quiet mode separation before adding any 8C30 watt overrides.",
                new[]
                {
                    "Record Quiet, Balanced, and Performance CPU package power under the same repeatable load.",
                    "Confirm performance apply trace shows direct EC power writes skipped and WMI thermal-policy fallback attempted/applied.",
                    "Capture fan RPM/level response for each mode; this board is single-fan and WMI-policy-first.",
                    "Attach tuning-fan-focus.txt, core-control-readiness.txt, wmi-command-history.txt, and identity-resolution-trace.txt."
                });

            AppendModelValidationCard(
                sb,
                "878C / OMEN 15-ek0xxx",
                priorityBoard == "878C",
                "Validate exact WMI profile routing for Quick Profiles that previously left fans low at 99C.",
                new[]
                {
                    "Record RPM and CPU/GPU temperatures before and after Performance, Balanced, Quiet, Auto, Gaming/Extreme, and Custom Max.",
                    "Confirm Performance-mode apply trace shows WMI thermal-policy fallback applied and direct EC power writes skipped.",
                    "Capture PL1/PL2/GPU power readback under the same load before adding any wattage override.",
                    "Attach core-control-readiness.txt, wmi-command-history.txt, tuning-fan-focus.txt, and identity-resolution-trace.txt."
                });

            AppendModelValidationCard(
                sb,
                "8600 / OMEN 15-dh0xxx",
                priorityBoard == "8600",
                "Validate exact legacy identity, WMI policy routing, and telemetry recovery after missing-PawnIO reports.",
                new[]
                {
                    "Install PawnIO from the v3.8.0+ installer path, reboot, then record whether CPU temperature unsticks from ~28C and CPU power/fan RPM leave 0.",
                    "Test Quiet, Balanced, Performance, Auto, and Max under the same load; record fan noise/RPM/readback and temperatures before/after each mode.",
                    "Confirm performance apply trace shows direct EC power writes skipped and WMI thermal-policy fallback attempted/applied.",
                    "Attach core-control-readiness.txt, launch-readiness.txt, wmi-command-history.txt, tuning-fan-focus.txt, and identity-resolution-trace.txt from both Windows and Linux if available."
                });

            AppendModelValidationCard(
                sb,
                "8BCD / Linux OMEN 16-xd0xxx",
                priorityBoard == "8BCD",
                "Validate degraded WMI-control reporting and preserve working telemetry/EC paths.",
                new[]
                {
                    "Run Linux diagnose with kernel logs showing or disproving WMAA/WHCM aborts.",
                    "Test fan profile writes and record whether RPM changes or only status text changes.",
                    "Record battery power_supply discovery and keyboard RGB behavior separately.",
                    "Attach journalctl -k -b, hp-wmi/hwmon listings, diagnose output, and sysfs battery paths."
                });

            AppendModelValidationCard(
                sb,
                "OMEN 17 db-1000",
                priorityBoard == "OMEN 17 db-1000",
                "Validate battery-charge wording, Direct fan usability, and visual readability.",
                new[]
                {
                    "Confirm battery warning no longer treats 68% charge as battery health.",
                    "Test Direct 40/60/80% if visible and record requested percent versus RPM.",
                    "Review dashboard warning badge readability in light/dark contexts.",
                    "Attach core-control-readiness.txt and screenshots only if UI wording still misleads."
                });

            AppendModelValidationCard(
                sb,
                "Victus 15/16 field cohort",
                priorityBoard == "Victus 15/16 field cohort",
                "Validate OMEN key interception, profile cycling, fan profile truthfulness, and RGB surface clarity.",
                new[]
                {
                    "Test Ctrl+Shift profile hotkeys, Win+F12 fallback, and the physical OMEN key.",
                    "Apply fan Auto/Performance/Max and record whether profile status matches actual RPM changes.",
                    "Apply one obvious RGB color and record whether keyboard, backlight, light bar, or nothing changes.",
                    "Attach Hotkeys / OMEN Key readiness, rgb-control-path.txt, and fan command history."
                });

            sb.AppendLine("[Promotion Rule]");
            sb.AppendLine("A model path can move from experimental/degraded to verified only when the matching card has a clean pass with ProductId, backend, requested value, readback value, and recovery/Restore Auto result.");

            return sb.ToString();
        }

        private async Task CollectRcValidationMatrixAsync(string exportPath)
        {
            try
            {
                var report = BuildRcValidationMatrixReport();
                File.WriteAllText(Path.Combine(exportPath, "rc-validation-matrix.txt"), report);
                _logging.Info("Collected RC validation matrix");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect RC validation matrix: {ex.Message}");
            }
        }

        public string BuildRcValidationMatrixReport()
        {
            var systemInfo = SafeGetSystemInfo();
            var identity = ModelIdentityResolutionService.Build(systemInfo, capabilities: null, logging: _logging);
            var priorityBoard = ClassifyPriorityBoard(identity.CapabilityProductId, identity.RawBaseboardProduct, identity.RawWmiModel);

            var sb = new StringBuilder();
            sb.AppendLine("=== RC VALIDATION MATRIX ===");
            sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
            sb.AppendLine("Purpose: track v3.8.1 release-gate evidence by priority model cohort. Rows marked field-pending must not be advertised as verified until a clean tester pass is attached.");
            sb.AppendLine();

            sb.AppendLine("[Detected Context]");
            sb.AppendLine($"ResolvedModel: {identity.Summary}");
            sb.AppendLine($"ProductId: {identity.CapabilityProductId}");
            sb.AppendLine($"PriorityBoard: {priorityBoard}");
            sb.AppendLine();

            sb.AppendLine("[Matrix]");
            sb.AppendLine("selected | cohort | local status | field status | promotion evidence");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8D41",
                "8D41 / OMEN Max 16-ah0xxx",
                "Fix implemented: WMI-only one-sample Max reassertion and readiness diagnostics.",
                "Field validation pending",
                "10 minute Max hold under load, Restore OEM Auto recovery, wmi-command-history.txt, core-control-readiness.txt.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8D87",
                "8D87 / OMEN Max follow-up",
                "Fix implemented: WMI-only one-sample Max reassertion and OMEN MAX RGB diagnostics.",
                "Field validation pending",
                "Long-session Max/Direct obedience, RGB surface observation, HID PID evidence if per-key fails.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8BD4",
                "8BD4 / Victus 16-s0xxx",
                "Fix implemented: conservative WMI V1 handoff, zero-floor clear disabled, RGB ColorTable path enabled.",
                "Field validation pending",
                "Auto -> Max -> Auto, custom curve/Direct where visible, long-session report, RGB surface observation.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8DCD",
                "8DCD / Victus 15",
                "Fix implemented: exact conservative profile with WMI thermal-policy fallback.",
                "Field validation pending",
                "Balanced vs Performance CPU package power, PL1/PL2 readback, performance apply trace.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8C30",
                "8C30 / Victus 15-fb1xxx",
                "Fix implemented: exact WMI-policy-first profile with direct EC/CPU power-limit UI disabled and explicit Quiet/Balanced/Performance modes.",
                "Field validation pending",
                "Quiet vs Balanced vs Performance package power, fan RPM/level response, WMI policy fallback trace.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "878C",
                "878C / OMEN 15-ek0xxx",
                "Fix implemented: exact legacy WMI profile with direct EC writes disabled and WMI thermal-policy fallback.",
                "Field validation pending",
                "Performance/Balanced/Quiet/Auto/Gaming/Extreme RPM response, PL readback, fan command history.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8600",
                "8600 / OMEN 15-dh0xxx",
                "Fix implemented: exact conservative legacy profile, Unknown keyboard fallback removed, WMI thermal-policy fallback enabled, direct EC/RPM/PL readback held back.",
                "Field validation pending",
                "PawnIO install + reboot telemetry recovery, Quiet/Balanced/Performance/Auto/Max response, Windows/Linux diagnostics.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "8BCD",
                "8BCD / Linux OMEN 16-xd0xxx",
                "Partial: degraded-control detection and battery/sysfs fallback added; broken WMI writes are not claimed fixed.",
                "Field validation pending",
                "Kernel WMAA/WHCM evidence, effective fan/RGB write readback, battery power_supply discovery.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "OMEN 17 db-1000",
                "OMEN 17 db-1000",
                "Fix implemented: battery charge/health wording and Direct fan UI first pass.",
                "Field validation pending",
                "No false battery-health alert at partial charge, Direct 40/60/80 RPM response, readability check.");
            AppendRcValidationMatrixRow(
                sb,
                priorityBoard,
                "Victus 15/16 field cohort",
                "Victus 15/16 field cohort",
                "Diagnostics implemented: OMEN-key/hotkey readiness, RGB surface clarity, fan truthfulness guidance.",
                "Field validation pending",
                "Profile hotkeys, physical OMEN key source, fan profile truthfulness, RGB surface behavior.");

            sb.AppendLine();
            sb.AppendLine("[Release Rule]");
            sb.AppendLine("Keep v3.8.1 as RC/pre-release until priority fan/performance/hotkey/RGB rows either have clean field passes or release notes explicitly mark the path experimental/degraded.");

            return sb.ToString();
        }

        private static void AppendRcValidationMatrixRow(
            StringBuilder sb,
            string selectedBoard,
            string boardKey,
            string cohort,
            string localStatus,
            string fieldStatus,
            string promotionEvidence)
        {
            var selected = string.Equals(selectedBoard, boardKey, StringComparison.OrdinalIgnoreCase) ? "*" : "-";
            sb.AppendLine($"{selected} | {cohort} | {localStatus} | {fieldStatus} | {promotionEvidence}");
        }

        private static void AppendModelValidationCard(
            StringBuilder sb,
            string title,
            bool selected,
            string goal,
            string[] steps)
        {
            sb.AppendLine($"[{(selected ? "SELECTED" : "REFERENCE")} - {title}]");
            sb.AppendLine($"Goal: {goal}");
            foreach (var step in steps)
            {
                sb.AppendLine($"- {step}");
            }
            sb.AppendLine();
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
            var (config, source) = LoadConfigForDiagnostics();
            var observedRgb = config.KeyboardLighting ?? new KeyboardLightingSettings();
            sb.AppendLine($"ConfigSource: {source}");
            if (_keyboardLightingService == null)
            {
                sb.AppendLine("Keyboard lighting service unavailable.");
                AppendRgbObservedSurface(sb, observedRgb);
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"Available: {_keyboardLightingService.IsAvailable}");
            sb.AppendLine($"ActiveBackend: {_keyboardLightingService.BackendType}");
            sb.AppendLine($"PerKeyActive: {_keyboardLightingService.IsPerKey}");
            sb.AppendLine($"PerKeyCapableHardware: {_keyboardLightingService.IsPerKeyCapableHardware}");
            if (_keyboardLightingService.IsPerKeyCapableHardware && !_keyboardLightingService.IsPerKey)
            {
                sb.AppendLine("PerKeyLaunchStatus: HID per-key backend/editor pending; zone/light-bar fallback remains the supported path in this build.");
            }
            sb.AppendLine($"LastApplySurface: {_keyboardLightingService.LastApplySurface}");
            sb.AppendLine($"LastApplyStatus: {_keyboardLightingService.LastApplyStatus}");
            AppendRgbObservedSurface(sb, observedRgb);
            sb.AppendLine();
        }

        private static void AppendRgbObservedSurface(StringBuilder sb, KeyboardLightingSettings settings)
        {
            sb.AppendLine("[HP Keyboard Observed Surface]");
            sb.AppendLine($"ObservedSurface: {FormatValue(settings.ObservedSurface)}");
            sb.AppendLine($"ObservedAtUtc: {FormatDate(settings.ObservedAtUtc)}");
            sb.AppendLine($"ObservedProbeColor: {FormatValue(settings.ObservedProbeColorHex)}");
            sb.AppendLine($"ObservedBackend: {FormatValue(settings.ObservedBackend)}");
            sb.AppendLine($"ObservedApplyStatus: {FormatValue(settings.ObservedApplyStatus)}");
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

                var uiCounters = RuntimeUiPerformanceCounters.GetSnapshot();
                sb.AppendLine("[UI Dispatcher + Projection Counters]");
                sb.AppendLine($"CounterUptimeSeconds: {uiCounters.UptimeSeconds:F1}");
                sb.AppendLine($"DispatcherBeginInvokePosts: {uiCounters.DispatcherBeginInvokePosts}");
                sb.AppendLine($"DispatcherInvokes: {uiCounters.DispatcherInvokes}");
                sb.AppendLine($"MainMonitoringSamplesQueued: {uiCounters.MainMonitoringSamplesQueued}");
                sb.AppendLine($"MainMonitoringSamplesCoalesced: {uiCounters.MainMonitoringSamplesCoalesced}");
                sb.AppendLine($"MainMonitoringSamplesProjected: {uiCounters.MainMonitoringSamplesProjected}");
                sb.AppendLine($"DashboardSamplesReceived: {uiCounters.DashboardSamplesReceived}");
                sb.AppendLine($"DashboardSamplesProjected: {uiCounters.DashboardSamplesProjected}");
                sb.AppendLine($"DashboardSamplesSkipped: {uiCounters.DashboardSamplesSkipped}");
                sb.AppendLine($"DashboardDispatcherPosts: {uiCounters.DashboardDispatcherPosts}");
                sb.AppendLine($"DashboardProjectionRequeues: {uiCounters.DashboardProjectionRequeues}");
                sb.AppendLine($"GeneralSamplesReceived: {uiCounters.GeneralSamplesReceived}");
                sb.AppendLine($"GeneralSamplesProjected: {uiCounters.GeneralSamplesProjected}");
                sb.AppendLine($"GeneralSamplesSkipped: {uiCounters.GeneralSamplesSkipped}");
                sb.AppendLine($"TotalProjectedSamples: {uiCounters.TotalProjectedSamples}");
                sb.AppendLine($"DispatcherBeginInvokePostsPerSecond: {uiCounters.DispatcherBeginInvokePostsPerSecond:F2}");
                sb.AppendLine($"DispatcherInvokesPerSecond: {uiCounters.DispatcherInvokesPerSecond:F2}");
                sb.AppendLine($"MainProjectedSamplesPerSecond: {uiCounters.MainProjectedSamplesPerSecond:F2}");
                sb.AppendLine($"DashboardProjectedSamplesPerSecond: {uiCounters.DashboardProjectedSamplesPerSecond:F2}");
                sb.AppendLine($"GeneralProjectedSamplesPerSecond: {uiCounters.GeneralProjectedSamplesPerSecond:F2}");
                sb.AppendLine($"ProjectionAmplificationRatio: {uiCounters.ProjectionAmplificationRatio:F2}");
                sb.AppendLine($"DispatcherAmplificationRatio: {uiCounters.DispatcherAmplificationRatio:F2}");
                sb.AppendLine($"MainProjectionAcceptanceRatio: {uiCounters.MainProjectionAcceptanceRatio:F2}");
                sb.AppendLine($"DashboardProjectionAcceptanceRatio: {uiCounters.DashboardProjectionAcceptanceRatio:F2}");
                sb.AppendLine($"GeneralProjectionAcceptanceRatio: {uiCounters.GeneralProjectionAcceptanceRatio:F2}");
                sb.AppendLine();

                // v3.6.2 Field Validation Counters
                sb.AppendLine("[v3.6.2 Field Validation Counters]");
                sb.AppendLine($"DashboardDormancyActivations: {uiCounters.DashboardDormancyActivations}");
                sb.AppendLine($"DashboardDormancySamplesProjected: {uiCounters.DashboardDormancySamplesProjected}");
                sb.AppendLine($"HiddenSurfaceSamplesSkipped: {uiCounters.HiddenSurfaceSamplesSkipped}");
                sb.AppendLine($"TrayRenderCacheHits: {uiCounters.TrayRenderCacheHits}");
                sb.AppendLine($"TrayRenderCacheMisses: {uiCounters.TrayRenderCacheMisses}");
                sb.AppendLine($"TrayRenderCacheHitRatio: {uiCounters.TrayRenderCacheHitRatio:F2}");
                sb.AppendLine($"PopupRenderCacheHits: {uiCounters.PopupRenderCacheHits}");
                sb.AppendLine($"PopupRenderCacheMisses: {uiCounters.PopupRenderCacheMisses}");
                sb.AppendLine($"PopupRenderCacheHitRatio: {uiCounters.PopupRenderCacheHitRatio:F2}");
                sb.AppendLine($"LatestSampleReplacements: {uiCounters.LatestSampleReplacements}");
                sb.AppendLine($"FanTelemetrySyncs: {uiCounters.FanTelemetrySyncs}");
                sb.AppendLine($"FanTelemetryCollectionResizes: {uiCounters.FanTelemetryCollectionResizes}");
                sb.AppendLine($"FanTelemetryItemsUpdated: {uiCounters.FanTelemetryItemsUpdated}");
                sb.AppendLine($"FanTelemetryPropertyOnlySyncs: {uiCounters.FanTelemetryPropertyOnlySyncs}");
                sb.AppendLine($"FanTelemetryCollectionResizeRatio: {uiCounters.FanTelemetryCollectionResizeRatio:F2}");
                sb.AppendLine($"FanTelemetryPropertyOnlySyncRatio: {uiCounters.FanTelemetryPropertyOnlySyncRatio:F2}");
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

        private async Task CollectBoundedPerformanceSnapshotsAsync(string exportPath)
        {
            try
            {
                var scenario = NormalizeDiagnosticScenarioLabel(Environment.GetEnvironmentVariable("OMENCORE_DIAGNOSTIC_SCENARIO"));
                var sampleCount = 3;
                var interval = TimeSpan.FromSeconds(5);
                var snapshots = new List<(DateTime Utc, TimeSpan Cpu, long WorkingSetBytes, long PrivateBytes, int Threads, int Handles, RuntimeUiPerformanceCounterSnapshot Ui)>();

                for (var i = 0; i < sampleCount; i++)
                {
                    using var process = Process.GetCurrentProcess();
                    snapshots.Add((
                        DateTime.UtcNow,
                        process.TotalProcessorTime,
                        process.WorkingSet64,
                        process.PrivateMemorySize64,
                        process.Threads.Count,
                        process.HandleCount,
                        RuntimeUiPerformanceCounters.GetSnapshot()));

                    if (i < sampleCount - 1)
                    {
                        await Task.Delay(interval);
                    }
                }

                var first = snapshots[0];
                var last = snapshots[^1];
                var totalWindow = last.Utc - first.Utc;
                var cpuDelta = last.Cpu - first.Cpu;
                var estimatedCpuPercent = totalWindow.TotalMilliseconds <= 0
                    ? 0
                    : Math.Max(0, cpuDelta.TotalMilliseconds / (totalWindow.TotalMilliseconds * Environment.ProcessorCount) * 100.0);

                var sb = new StringBuilder();
                sb.AppendLine("=== BOUNDED PERFORMANCE SNAPSHOT ===");
                sb.AppendLine($"CapturedUtc: {DateTime.UtcNow:O}");
                sb.AppendLine($"Scenario: {scenario}");
                sb.AppendLine($"SampleCount: {sampleCount}");
                sb.AppendLine($"SampleIntervalSeconds: {interval.TotalSeconds:F0}");
                sb.AppendLine($"WindowSeconds: {totalWindow.TotalSeconds:F1}");
                sb.AppendLine("Purpose: cheap, bounded evidence for focused/minimized/popup/OSD/tray states without continuous logging.");
                sb.AppendLine();

                sb.AppendLine("[Runtime State Summary]");
                sb.AppendLine($"MonitoringHealth: {_hardwareMonitoringService?.HealthStatus.ToString() ?? "Unavailable"}");
                sb.AppendLine($"MonitoringSource: {_hardwareMonitoringService?.MonitoringSource ?? "Unavailable"}");
                sb.AppendLine($"CurrentCadenceReason: {_hardwareMonitoringService?.CurrentCadenceReason ?? "Unavailable"}");
                sb.AppendLine($"LowOverheadMode: {_hardwareMonitoringService?.LowOverheadMode.ToString() ?? "Unavailable"}");
                sb.AppendLine($"FanControlState: {_fanService?.FanControlStateDescription ?? "Unavailable"}");
                sb.AppendLine($"CurveActive: {_fanService?.IsCurveActive.ToString() ?? "Unavailable"}");
                sb.AppendLine($"HoldActive: {_fanService?.IsHoldActive.ToString() ?? "Unavailable"}");
                sb.AppendLine();

                for (var i = 0; i < snapshots.Count; i++)
                {
                    var s = snapshots[i];
                    sb.AppendLine($"[Sample {i + 1}]");
                    sb.AppendLine($"Utc: {s.Utc:O}");
                    sb.AppendLine($"WorkingSetMB: {s.WorkingSetBytes / 1024d / 1024d:F1}");
                    sb.AppendLine($"PrivateMemoryMB: {s.PrivateBytes / 1024d / 1024d:F1}");
                    sb.AppendLine($"Threads: {s.Threads}");
                    sb.AppendLine($"Handles: {s.Handles}");
                    sb.AppendLine($"DispatcherAmplificationRatio: {s.Ui.DispatcherAmplificationRatio:F2}");
                    sb.AppendLine($"ProjectionAmplificationRatio: {s.Ui.ProjectionAmplificationRatio:F2}");
                    sb.AppendLine($"MainProjectionAcceptanceRatio: {s.Ui.MainProjectionAcceptanceRatio:F2}");
                    sb.AppendLine($"DashboardProjectionAcceptanceRatio: {s.Ui.DashboardProjectionAcceptanceRatio:F2}");
                    sb.AppendLine($"GeneralProjectionAcceptanceRatio: {s.Ui.GeneralProjectionAcceptanceRatio:F2}");
                    sb.AppendLine($"DispatcherAmplificationClass: {ClassifyAmplificationRatio(s.Ui.DispatcherAmplificationRatio)}");
                    sb.AppendLine($"ProjectionAmplificationClass: {ClassifyAmplificationRatio(s.Ui.ProjectionAmplificationRatio)}");
                    sb.AppendLine($"MainProjectionAcceptanceClass: {ClassifyAcceptanceRatio(s.Ui.MainProjectionAcceptanceRatio)}");
                    sb.AppendLine($"DashboardAcceptanceClass: {ClassifyAcceptanceRatio(s.Ui.DashboardProjectionAcceptanceRatio)}");
                    sb.AppendLine($"GeneralAcceptanceClass: {ClassifyAcceptanceRatio(s.Ui.GeneralProjectionAcceptanceRatio)}");
                    sb.AppendLine($"DashboardDormancyActivations: {s.Ui.DashboardDormancyActivations}");
                    sb.AppendLine($"HiddenSurfaceSamplesSkipped: {s.Ui.HiddenSurfaceSamplesSkipped}");
                    sb.AppendLine($"TrayRenderCacheHitRatio: {s.Ui.TrayRenderCacheHitRatio:F2}");
                    sb.AppendLine($"PopupRenderCacheHitRatio: {s.Ui.PopupRenderCacheHitRatio:F2}");
                    sb.AppendLine($"FanTelemetryCollectionResizeRatio: {s.Ui.FanTelemetryCollectionResizeRatio:F2}");
                    sb.AppendLine($"FanTelemetryPropertyOnlySyncRatio: {s.Ui.FanTelemetryPropertyOnlySyncRatio:F2}");
                    sb.AppendLine($"TrayRenderCacheClass: {ClassifyCacheHitRatio(s.Ui.TrayRenderCacheHitRatio)}");
                    sb.AppendLine($"PopupRenderCacheClass: {ClassifyCacheHitRatio(s.Ui.PopupRenderCacheHitRatio)}");
                    sb.AppendLine();
                }

                sb.AppendLine("[Window Delta]");
                sb.AppendLine($"EstimatedCpuPercentOverWindow: {estimatedCpuPercent:F2}");
                sb.AppendLine($"EstimatedCpuClass: {ClassifyEstimatedCpuPercent(estimatedCpuPercent)}");
                sb.AppendLine($"WorkingSetDeltaMB: {(last.WorkingSetBytes - first.WorkingSetBytes) / 1024d / 1024d:F1}");
                sb.AppendLine($"PrivateMemoryDeltaMB: {(last.PrivateBytes - first.PrivateBytes) / 1024d / 1024d:F1}");
                sb.AppendLine($"DispatcherBeginInvokePostsDelta: {last.Ui.DispatcherBeginInvokePosts - first.Ui.DispatcherBeginInvokePosts}");
                sb.AppendLine($"DispatcherInvokesDelta: {last.Ui.DispatcherInvokes - first.Ui.DispatcherInvokes}");
                sb.AppendLine($"MainMonitoringSamplesQueuedDelta: {last.Ui.MainMonitoringSamplesQueued - first.Ui.MainMonitoringSamplesQueued}");
                sb.AppendLine($"MainMonitoringSamplesProjectedDelta: {last.Ui.MainMonitoringSamplesProjected - first.Ui.MainMonitoringSamplesProjected}");
                sb.AppendLine($"DashboardSamplesProjectedDelta: {last.Ui.DashboardSamplesProjected - first.Ui.DashboardSamplesProjected}");
                sb.AppendLine($"GeneralSamplesProjectedDelta: {last.Ui.GeneralSamplesProjected - first.Ui.GeneralSamplesProjected}");
                sb.AppendLine($"HiddenSurfaceSamplesSkippedDelta: {last.Ui.HiddenSurfaceSamplesSkipped - first.Ui.HiddenSurfaceSamplesSkipped}");
                sb.AppendLine($"LatestSampleReplacementsDelta: {last.Ui.LatestSampleReplacements - first.Ui.LatestSampleReplacements}");
                sb.AppendLine($"FanTelemetrySyncsDelta: {last.Ui.FanTelemetrySyncs - first.Ui.FanTelemetrySyncs}");
                sb.AppendLine($"FanTelemetryCollectionResizesDelta: {last.Ui.FanTelemetryCollectionResizes - first.Ui.FanTelemetryCollectionResizes}");
                sb.AppendLine($"FanTelemetryItemsUpdatedDelta: {last.Ui.FanTelemetryItemsUpdated - first.Ui.FanTelemetryItemsUpdated}");
                sb.AppendLine($"FanTelemetryPropertyOnlySyncsDelta: {last.Ui.FanTelemetryPropertyOnlySyncs - first.Ui.FanTelemetryPropertyOnlySyncs}");
                sb.AppendLine($"ScenarioAssessment: {BuildBoundedScenarioAssessment(last, first, estimatedCpuPercent)}");

                File.WriteAllText(Path.Combine(exportPath, "runtime-performance-bounded.txt"), sb.ToString());
                _logging.Info("Collected bounded performance snapshots");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect bounded performance snapshots: {ex.Message}");
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

        private static string NormalizeDiagnosticScenarioLabel(string? raw)
        {
            var trimmed = raw?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? "unspecified" : trimmed;
        }

        private static string ClassifyAmplificationRatio(double ratio)
        {
            if (ratio <= 1.1)
            {
                return "Excellent";
            }

            if (ratio <= 2.0)
            {
                return "Expected";
            }

            if (ratio <= 3.0)
            {
                return "Elevated";
            }

            return "Critical";
        }

        private static string ClassifyAcceptanceRatio(double ratio)
        {
            if (ratio <= 0.10)
            {
                return "Strong suppression";
            }

            if (ratio <= 0.50)
            {
                return "Moderate suppression";
            }

            if (ratio <= 0.80)
            {
                return "Moderate projection";
            }

            return "High projection";
        }

        private static string ClassifyCacheHitRatio(double ratio)
        {
            if (ratio >= 0.90)
            {
                return "Excellent";
            }

            if (ratio >= 0.70)
            {
                return "Good";
            }

            if (ratio >= 0.50)
            {
                return "Watch";
            }

            return "Low";
        }

        private static string ClassifyEstimatedCpuPercent(double estimatedCpuPercent)
        {
            if (estimatedCpuPercent <= 1.0)
            {
                return "Idle-like";
            }

            if (estimatedCpuPercent <= 2.5)
            {
                return "Expected active background";
            }

            if (estimatedCpuPercent <= 5.0)
            {
                return "Elevated";
            }

            return "Critical";
        }

        private static string BuildBoundedScenarioAssessment(
            (DateTime Utc, TimeSpan Cpu, long WorkingSetBytes, long PrivateBytes, int Threads, int Handles, RuntimeUiPerformanceCounterSnapshot Ui) last,
            (DateTime Utc, TimeSpan Cpu, long WorkingSetBytes, long PrivateBytes, int Threads, int Handles, RuntimeUiPerformanceCounterSnapshot Ui) first,
            double estimatedCpuPercent)
        {
            var findings = new List<string>();

            if (estimatedCpuPercent > 5.0)
            {
                findings.Add("High process CPU across bounded window");
            }

            if (last.Ui.DispatcherAmplificationRatio > 3.0)
            {
                findings.Add("Dispatcher amplification above triage threshold");
            }

            if (last.Ui.ProjectionAmplificationRatio > 3.0)
            {
                findings.Add("Projection amplification above triage threshold");
            }

            if ((last.Ui.HiddenSurfaceSamplesSkipped - first.Ui.HiddenSurfaceSamplesSkipped) <= 0)
            {
                findings.Add("No hidden-surface suppression observed in this window");
            }

            if (findings.Count == 0)
            {
                return "No immediate anomalies detected in bounded window";
            }

            return string.Join("; ", findings);
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
                sb.AppendLine($"StartupRestoreCategories: {StartupRestorePolicy.BuildSummary(config)}");
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
                        byte value = ReadDiagnosticEcByte(ecAccess, (ushort)reg, "CollectEcState.SafeRegister");
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
                        byte value = ReadDiagnosticEcByte(ecAccess, (ushort)reg, "CollectEcState.FanRegister");
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
                        byte value = ReadDiagnosticEcByte(ecAccess, (ushort)reg, "CollectEcState.TempRegister");
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

        private byte ReadDiagnosticEcByte(IEcAccess ecAccess, ushort register, string operationName)
        {
            return _ecOperationCoordinator.Execute(
                "DiagnosticExportService",
                $"{operationName}.0x{register:X2}",
                () => ecAccess.ReadByte(register));
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

        private static string FormatNullableInt(int? value) =>
            value.HasValue ? value.Value.ToString() : "not set";

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
