using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Fan service that implements continuous fan curve monitoring like OmenMon.
    /// 
    /// Key differences from v1.2.x:
    /// 1. Continuous curve application: Every CurveUpdateIntervalMs, reads temps and applies curve
    /// 2. Adaptive polling: Longer intervals when temps are stable, shorter when changing
    /// 3. Separate monitoring vs control loops to reduce DPC latency
    /// 4. Hysteresis support to prevent fan oscillation
    /// </summary>
    public class FanService : IDisposable
    {
        private readonly IFanController _fanController;
        private readonly ThermalSensorProvider _thermalProvider;
        private readonly LoggingService _logging;
        private readonly NotificationService _notificationService;
        private readonly ResumeRecoveryDiagnosticsService _resumeDiagnostics; // Always non-null; STEP-12 Option A
        private readonly RuntimeEcOperationCoordinator _ecOperationCoordinator;
        private readonly DeviceCapabilities? _capabilities;
        private TimeSpan _monitorPollPeriod;
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private readonly ObservableCollection<FanTelemetry> _fanTelemetry = new();
        private CancellationTokenSource? _cts;

        // Active fan curves for continuous application (OmenMon-style)
        // Now supports separate CPU and GPU curves for independent fan control
        private List<FanCurvePoint>? _activeCurve;      // Legacy: unified curve (maps to both)
        private List<FanCurvePoint>? _cpuCurve;         // CPU-specific curve
        private List<FanCurvePoint>? _gpuCurve;         // GPU-specific curve
        private bool _independentCurvesEnabled = false; // When true, use separate curves
        private FanPreset? _activePreset;
        private bool _curveEnabled = false;
        private volatile bool _systemSuspendActive;
        private readonly object _curveLock = new();
        
        /// <summary>
        /// Expose ThermalSensorProvider for OSD and other services that need temperature data.
        /// </summary>
        public ThermalSensorProvider ThermalProvider => _thermalProvider;
        
        // Curve update timing - more aggressive for responsive fan control
        // v2.7.0: Reduced intervals to combat BIOS fan reversion on OMEN 16/Max models
        private const int CurveUpdateIntervalMs = 5000;  // 5 seconds between curve updates (reduced from 10)
        private const int CurveForceRefreshMs = 30000;   // Force re-apply every 30 seconds (reduced from 60)
        private const int ConservativeCurveForceRefreshMs = 60000;
        private const int ConservativeCurveMinWriteIntervalMs = 10000;
        private const int ConservativeCurveMinDeltaPercent = 5;
        private const int MonitorMinIntervalMs = 1000;   // 1 second minimum for UI updates
        private const int MonitorMaxIntervalMs = 5000;   // 5 seconds when temps stable
        private DateTime _lastCurveUpdate = DateTime.MinValue;
        private DateTime _lastCurveForceRefresh = DateTime.MinValue;
        private DateTime _lastCurveWriteUtc = DateTime.MinValue;
        private int _lastAppliedFanPercent = -1;
        private int _lastAppliedCpuFanPercent = -1;      // For independent curves
        private int _lastAppliedGpuFanPercent = -1;      // For independent curves

        // Smoothing / transition settings (configurable)
        private bool _smoothingEnabled = true;
        private int _smoothingDurationMs = 1000;
        private int _smoothingStepMs = 200;
        private double _smoothedCpuCurveTemp = double.NaN;
        private double _smoothedGpuCurveTemp = double.NaN;
        private const double CurveTempSmoothingBypassTempC = 75.0;
        private const double CurveTempMaxRisePerEvaluationC = 6.0;
        private const double CurveTempMaxDropPerEvaluationC = 4.0;

        // Expose for tests and read-only inspection
        public bool SmoothingEnabled => _smoothingEnabled;
        public int SmoothingDurationMs => _smoothingDurationMs;
        public int SmoothingStepMs => _smoothingStepMs;
        
        // Adaptive polling - reduce DPC latency by polling less when stable
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private int _stableReadings = 0;
        private const int StableThreshold = 3; // Number of stable readings before slowing down
        private const double TempChangeThreshold = 3.0; // °C change to trigger faster polling
        // When set (tests only), the monitor loop uses this fixed delay instead of the adaptive one.
        private int _fixedPollOverrideMs = 0;
        
        // Fan telemetry change detection - reduce UI churn by only updating on meaningful change
        private List<int> _lastFanSpeeds = new();
        private List<int> _fanChangeConfirmCounters = new();
        // Tracks which RPM value each fan's confirmation counter is currently counting toward.
        // When the candidate value changes (e.g. from 0-w-duty to a real RPM spike) the counter
        // must be reset so that we don't accidentally carry over counts from a different value.
        private List<int> _fanChangePendingRpms = new();
        private List<DateTime> _zeroRpmDutySinceByFan = new();
        private List<TelemetryDataState> _lastFanRpmStates = new();
        private const int FanSpeedChangeThreshold = 50; // RPM change to trigger UI update
        private const int RpmReadbackUnavailableThresholdSeconds = 10;
        private const int CurveZeroRpmWakeKickThresholdSeconds = RpmReadbackUnavailableThresholdSeconds + 2;
        private const int CurveZeroRpmWakeKickCooldownSeconds = 60;
        private const int CurveZeroRpmWakeKickMinPercent = 35;
        private const int CurveZeroRpmWakeKickMaxPercent = 60;
        private const double CurveZeroRpmWakeKickMinTempC = 55.0;
        private DateTime _zeroRpmCurveCommandSince = DateTime.MinValue;
        private DateTime _lastCurveZeroRpmWakeKick = DateTime.MinValue;
        private int _lastRawPrimaryFanRpm = -1;
        private int _lastReportedPrimaryFanDutyPercent = -1;
        // Require two consecutive reads to accept a large non-zero RPM change to avoid
        // showing spurious transient readings (single-sample noise). Zero RPM is
        // accepted immediately so stopped-fan state is visible to users.
        private const int FanChangeConfirmRequiredCycles = 2;

        // Fan-mode transition window: when a preset is being applied the BIOS briefly
        // resets WMI fan registers, causing both RPM and duty-cycle to momentarily read 0.
        // We hold the previous RPM during a short post-apply grace period instead of
        // surfacing 0 RPM to the UI (which looks like a fan failure to the user).
        private volatile bool _fanModeTransitioning;
        private DateTime _fanTransitionUntil = DateTime.MinValue;
        private const int FanTransitionHoldMs = 5000; // hold for up to 5 s after preset apply
        private DateTime _lastDeferredMaxVerifyAt = DateTime.MinValue;
        private const int DeferredMaxVerifyMinIntervalMs = 10000;
        private DateTime _lastMaxPersistenceRecoveryAt = DateTime.MinValue;
        private const int MaxPersistenceRecoveryCooldownMs = 30000;

        // Bounded command history for diagnostics exports and field reports. This captures
        // requested writes even when hardware readback is delayed or unavailable.
        private const int MaxFanCommandHistoryEntries = 80;
        private readonly object _fanCommandHistoryLock = new();
        private readonly Queue<FanCommandHistoryEntry> _fanCommandHistory = new();
        private bool? _lastRecordedHoldActive;
        private bool? _lastNotifiedCurveOrHoldActive;
        
        // Thermal protection - override Auto mode when temps get too high
        // v2.8.0: Raised thresholds — 80°C/85°C was too aggressive for gaming laptops
        // that routinely hit 85°C under heavy load. Users reported constant fan ramp-ups
        // on Silent mode. Modern laptop CPUs throttle at 95-100°C; 85°C is normal.
        // v2.8.0: Added time-based debounce to prevent fan yo-yo on CPUs that briefly spike
        private double _thermalProtectionThreshold = 90.0;      // °C - start ramping fans (configurable)
        private double _thermalEmergencyThreshold = 95.0;       // °C - Emergency 100% (configurable, see SetHysteresis)
        private const double ThermalSafeReleaseTemp = 65.0;     // Temps below this are truly safe for release
        private const int ThermalReleaseMinFanPercent = 40;     // Min fan on thermal release to prevent yo-yo
        private const double ThermalReleaseHysteresis = 10.0;   // °C below threshold to release (was 5°C, too tight)
        private volatile bool _thermalProtectionActive = false;
        
        // Debounce timers — prevent rapid activate/deactivate cycling
        private DateTime _thermalAboveThresholdSince = DateTime.MinValue;  // When temp first exceeded threshold
        private DateTime _thermalBelowReleaseSince = DateTime.MinValue;    // When temp first dropped below release
        private const double ThermalActivateDebounceSeconds = 5.0;  // Must stay above threshold for 5s to activate
        private const double ThermalReleaseDebounceSeconds = 15.0;  // Must stay below release for 15s to deactivate
        
        // EC write rate-limiting for thermal protection
        // When thermal protection is active, avoid re-issuing the same fan command every poll cycle.
        // Each SetFanSpeed call generates 7+ EC writes. At 1s polling, that's 7+ EC ops/second which
        // overwhelms the EC → ACPI Event 13 → EC stops responding to OS → false battery critical shutdown.
        private DateTime _lastThermalFanWriteTime = DateTime.MinValue;
        private int _lastThermalFanPercent = -1;
        private const double ThermalWriteMinIntervalSeconds = 15.0;  // Re-apply thermal fan speed at most every 15s
        
        // Diagnostic mode - suspends curve engine to allow manual fan testing
        private volatile bool _diagnosticModeActive = false;
        private static int _globalDiagnosticModeCount = 0;
        
        // Fan level range note: HP WMI uses 0-55 (krpm) on classic models or 0-100 (percentage) on newer.
        // Actual conversion is handled by WmiFanController which auto-detects the max level.
        private bool _thermalProtectionEnabled = true; // Can be disabled in settings
        
        // Hysteresis state
        private FanHysteresisSettings _hysteresis = new();
        private double _lastHysteresisTemp = 0;
        private DateTime _lastFanChangeRequest = DateTime.MinValue;
        private int _pendingFanPercent = -1;
        private bool _pendingIncrease = false;

        // GPU power boost integration - adjust fan curves based on GPU power level
        private string _gpuPowerBoostLevel = "Medium";
        public string GpuPowerBoostLevel
        {
            get => _gpuPowerBoostLevel;
            set
            {
                if (_gpuPowerBoostLevel != value)
                {
                    _gpuPowerBoostLevel = value;
                    _logging.Info($"GPU Power Boost level updated to: {_gpuPowerBoostLevel} - adjusting fan curves accordingly");
                    // Force a curve re-evaluation on next cycle
                    _lastCurveUpdate = DateTime.MinValue;
                }
            }
        }

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }
        
        /// <summary>
        /// The backend being used for fan control (WMI BIOS, EC, or None).
        /// </summary>
        public string Backend => _fanController.Backend;

        private bool IsEcBackend => Backend.Contains("EC", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Whether a custom fan curve is actively being applied.
        /// Thread-safe read of curve state.
        /// </summary>
        public bool IsCurveActive
        {
            get
            {
                lock (_curveLock)
                {
                    return _curveEnabled && (_activeCurve != null || (_cpuCurve != null && _gpuCurve != null));
                }
            }
        }

        /// <summary>
        /// Whether the active backend is currently maintaining fan ownership (keepalive/hold)
        /// independent of curve mode.
        /// </summary>
        public bool IsHoldActive
        {
            get
            {
                try
                {
                    return _fanController.IsHoldActive;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// True when either a custom curve is active or backend hold/keepalive is active.
        /// </summary>
        public bool IsCurveOrHoldActive => IsCurveActive || IsHoldActive;
        
        /// <summary>
        /// Whether independent CPU/GPU curves are being used.
        /// </summary>
        public bool IndependentCurvesEnabled => _independentCurvesEnabled;
        
        /// <summary>
        /// The currently active preset name, if any.
        /// </summary>
        public string? ActivePresetName => _activePreset?.Name;
        
        /// <summary>
        /// The currently active preset (for diagnostic mode restoration).
        /// </summary>
        public FanPreset? ActivePreset => _activePreset;
        
        /// <summary>
        /// Whether thermal protection is currently overriding fan control.
        /// </summary>
        public bool IsThermalProtectionActive => _thermalProtectionActive;
        
        /// <summary>
        /// Whether diagnostic mode is active (suspends curve engine for manual testing).
        /// </summary>
        public bool IsDiagnosticModeActive => _diagnosticModeActive;

        /// <summary>
        /// Global guard for lower-level fan backends that must yield while a diagnostic session
        /// is manually driving fan state.
        /// </summary>
        public static bool IsAnyDiagnosticModeActive => System.Threading.Volatile.Read(ref _globalDiagnosticModeCount) > 0;

        public IReadOnlyList<FanCommandHistoryEntry> GetCommandHistorySnapshot()
        {
            lock (_fanCommandHistoryLock)
            {
                return _fanCommandHistory.ToList();
            }
        }

        public string GetFanCommandHistoryReport()
        {
            var entries = GetCommandHistorySnapshot();
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("Fan Command History");
            lines.AppendLine($"Backend: {Backend}");
            lines.AppendLine($"Current mode: {_currentFanMode}");
            lines.AppendLine($"Active preset: {_activePreset?.Name ?? "<none>"}");
            lines.AppendLine($"Curve active: {IsCurveActive}");
            lines.AppendLine($"Diagnostic mode: {_diagnosticModeActive}");
            lines.AppendLine($"Thermal protection active: {_thermalProtectionActive}");
            AppendOptionalMaxExternalResetStatus(lines);
            lines.AppendLine($"Entries: {entries.Count}");
            lines.AppendLine(new string('-', 80));

            foreach (var entry in entries)
            {
                lines.AppendLine(
                    $"{entry.TimestampUtc:O} | {(entry.Success ? "OK" : "FAIL"),-4} | {entry.Command,-22} | {entry.Target}");
                lines.AppendLine(
                    $"  backend={entry.Backend}; mode={entry.FanMode}; preset={entry.ActivePresetName ?? "<none>"}; " +
                    $"curve={entry.CurveActive}; hold={entry.HoldActive}; curveOrHold={entry.CurveOrHoldActive}; diagnostic={entry.DiagnosticModeActive}; thermal={entry.ThermalProtectionActive}");
                lines.AppendLine(
                    $"  model={FormatCommandValue(entry.ModelName)} ({FormatCommandValue(entry.ProductId)}); " +
                    $"writes={entry.FanWritesAvailable}; curves={entry.FanCurvesAvailable}; manual={entry.ManualFanControlAvailable}; desktopBlocked={entry.DesktopFanWritesBlocked}");
                lines.AppendLine(
                    $"  readback={FormatCommandValue(entry.TelemetrySummary)}; " +
                    $"rawPrimaryRpm={FormatNullableCommandValue(entry.RawPrimaryFanRpm)}; reportedPrimaryDuty={FormatNullableCommandValue(entry.ReportedPrimaryFanDutyPercent)}");
                if (!string.IsNullOrWhiteSpace(entry.Details))
                {
                    lines.AppendLine($"  detail={entry.Details}");
                }
            }

            return lines.ToString();
        }

        private void AppendOptionalMaxExternalResetStatus(System.Text.StringBuilder lines)
        {
            try
            {
                var controllerType = _fanController.GetType();
                var resetUtc = controllerType.GetProperty("LastMaxModeExternalResetUtc")?.GetValue(_fanController);
                var details = controllerType.GetProperty("LastMaxModeExternalResetDetails")?.GetValue(_fanController)?.ToString();

                if (resetUtc is DateTime timestampUtc)
                {
                    lines.AppendLine($"Last Max external reset: {timestampUtc:O}");
                    if (!string.IsNullOrWhiteSpace(details))
                    {
                        lines.AppendLine($"Last Max external reset detail: {details}");
                    }
                }
                else
                {
                    lines.AppendLine("Last Max external reset: <none recorded>");
                }
            }
            catch
            {
                lines.AppendLine("Last Max external reset: <unavailable>");
            }
        }

        private void RecordFanCommand(string command, string target, bool success, string details = "")
        {
            var holdActive = IsHoldActive;
            var productId = ResolveFanCommandProductId();
            var modelName = ResolveFanCommandModelName();
            var fanWritesAvailable = FanWritesAvailable;
            var fanCurvesAvailable = FanCurvesAvailable;
            var manualFanControlAvailable = ManualFanControlAvailable;
            var desktopFanWritesBlocked = DesktopFanWritesBlocked;
            var telemetrySummary = BuildFanCommandTelemetrySummary();
            var rawPrimaryFanRpm = _lastRawPrimaryFanRpm >= 0 ? _lastRawPrimaryFanRpm : (int?)null;
            var reportedPrimaryFanDutyPercent = _lastReportedPrimaryFanDutyPercent >= 0 ? _lastReportedPrimaryFanDutyPercent : (int?)null;

            if (_lastRecordedHoldActive.HasValue && _lastRecordedHoldActive.Value != holdActive)
            {
                EnqueueFanCommandEntry(new FanCommandHistoryEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Command = "HoldStateTransition",
                    Target = holdActive ? "Active" : "Inactive",
                    Success = true,
                    Details = $"Hold state changed: {(_lastRecordedHoldActive.Value ? "Active" : "Inactive")} -> {(holdActive ? "Active" : "Inactive")}",
                    Backend = Backend,
                    FanMode = _currentFanMode,
                    ActivePresetName = _activePreset?.Name,
                    CurveActive = IsCurveActive,
                    HoldActive = holdActive,
                    CurveOrHoldActive = IsCurveOrHoldActive,
                    DiagnosticModeActive = _diagnosticModeActive,
                    ThermalProtectionActive = _thermalProtectionActive,
                    ProductId = productId,
                    ModelName = modelName,
                    FanWritesAvailable = fanWritesAvailable,
                    FanCurvesAvailable = fanCurvesAvailable,
                    ManualFanControlAvailable = manualFanControlAvailable,
                    DesktopFanWritesBlocked = desktopFanWritesBlocked,
                    TelemetrySummary = telemetrySummary,
                    RawPrimaryFanRpm = rawPrimaryFanRpm,
                    ReportedPrimaryFanDutyPercent = reportedPrimaryFanDutyPercent
                });
            }
            _lastRecordedHoldActive = holdActive;

            var entry = new FanCommandHistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Command = command,
                Target = target,
                Success = success,
                Details = details,
                Backend = Backend,
                FanMode = _currentFanMode,
                ActivePresetName = _activePreset?.Name,
                CurveActive = IsCurveActive,
                HoldActive = holdActive,
                CurveOrHoldActive = IsCurveOrHoldActive,
                DiagnosticModeActive = _diagnosticModeActive,
                ThermalProtectionActive = _thermalProtectionActive,
                ProductId = productId,
                ModelName = modelName,
                FanWritesAvailable = fanWritesAvailable,
                FanCurvesAvailable = fanCurvesAvailable,
                ManualFanControlAvailable = manualFanControlAvailable,
                DesktopFanWritesBlocked = desktopFanWritesBlocked,
                TelemetrySummary = telemetrySummary,
                RawPrimaryFanRpm = rawPrimaryFanRpm,
                ReportedPrimaryFanDutyPercent = reportedPrimaryFanDutyPercent
            };

            EnqueueFanCommandEntry(entry);
            NotifyFanActivityStateChangedIfNeeded();
        }

        private string ResolveFanCommandProductId()
        {
            if (!string.IsNullOrWhiteSpace(_capabilities?.ProductId))
            {
                return _capabilities!.ProductId;
            }

            return _capabilities?.ModelConfig?.ProductId ?? "unknown";
        }

        private string ResolveFanCommandModelName()
        {
            if (!string.IsNullOrWhiteSpace(_capabilities?.ModelName))
            {
                return _capabilities!.ModelName;
            }

            return _capabilities?.ModelConfig?.ModelName ?? "unknown";
        }

        private string BuildFanCommandTelemetrySummary()
        {
            try
            {
                var snapshot = _fanTelemetry
                    .Select((fan, index) =>
                    {
                        var name = string.IsNullOrWhiteSpace(fan.Name) ? $"Fan {index + 1}" : fan.Name;
                        var rpmText = fan.DisplayRpmText;
                        return $"{name}: {rpmText}, duty {fan.DutyCyclePercent}%, state {fan.RpmState}, source {fan.RpmSourceDisplay}";
                    })
                    .ToList();

                if (snapshot.Count == 0)
                {
                    return "no fan telemetry snapshot";
                }

                return string.Join("; ", snapshot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return $"fan telemetry snapshot unavailable ({ex.GetType().Name})";
            }
        }

        private static string FormatCommandValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "<none>" : value;

        private static string FormatNullableCommandValue(int? value) =>
            value.HasValue ? value.Value.ToString() : "<none>";

        private void EnqueueFanCommandEntry(FanCommandHistoryEntry entry)
        {
            lock (_fanCommandHistoryLock)
            {
                _fanCommandHistory.Enqueue(entry);
                while (_fanCommandHistory.Count > MaxFanCommandHistoryEntries)
                {
                    _fanCommandHistory.Dequeue();
                }
            }
        }

        private void NotifyFanActivityStateChangedIfNeeded()
        {
            var active = IsCurveOrHoldActive;
            if (_lastNotifiedCurveOrHoldActive.HasValue && _lastNotifiedCurveOrHoldActive.Value == active)
            {
                return;
            }

            _lastNotifiedCurveOrHoldActive = active;
            FanActivityStateChanged?.Invoke(this, active);
        }

        /// <summary>
        /// Human-readable description of the current fan control state for diagnostics/UI.
        /// Mirrors the implicit state machine: thermal > diagnostic > curve > preset > auto.
        /// </summary>
        public string FanControlStateDescription
        {
            get
            {
                if (_thermalProtectionActive) return "Thermal protection (overriding)";
                if (_diagnosticModeActive)    return "Diagnostic mode (curve suspended)";
                if (IsCurveActive)            return $"Fan curve active — {_activePreset?.Name ?? "custom"}";
                if (_activePreset != null)    return $"Preset: {_activePreset.Name}";
                return "BIOS auto control";
            }
        }
        
        /// <summary>
        /// Enter diagnostic mode - suspends curve engine to allow manual fan testing.
        /// Call ExitDiagnosticMode() when done to resume normal operation.
        /// </summary>
        public void EnterDiagnosticMode()
        {
            if (_diagnosticModeActive)
            {
                return;
            }

            _diagnosticModeActive = true;
            System.Threading.Interlocked.Increment(ref _globalDiagnosticModeCount);
            _logging.Info("🔧 Entered fan diagnostic mode - curve engine suspended");
        }
        
        /// <summary>
        /// Exit diagnostic mode - resumes normal curve engine operation.
        /// </summary>
        public void ExitDiagnosticMode()
        {
            if (!_diagnosticModeActive)
            {
                return;
            }

            _diagnosticModeActive = false;
            if (System.Threading.Interlocked.Decrement(ref _globalDiagnosticModeCount) < 0)
            {
                System.Threading.Volatile.Write(ref _globalDiagnosticModeCount, 0);
            }
            _logging.Info("✓ Exited fan diagnostic mode - curve engine resumed");
        }

        /// <summary>
        /// Restore BIOS automatic fan control. Call this when no user preset was active
        /// and the fan controller needs to be returned to default BIOS management.
        /// </summary>
        public void RestoreAutoControl()
        {
            try
            {
                DisableCurve();
                var restored = RestoreAutoControlSerialized();
                _activePreset = null;
                _currentFanMode = "Auto";
                RecordFanCommand("RestoreAutoControl", "BIOS auto", restored, restored ? "BIOS auto fan control restored" : "Controller returned false");
                _logging.Info("✓ BIOS auto fan control restored");
            }
            catch (Exception ex)
            {
                RecordFanCommand("RestoreAutoControl", "BIOS auto", false, ex.Message);
                _logging.Error("Failed to restore auto control", ex);
                throw;
            }
        }

        public bool RestoreOemAutoControl()
        {
            if (!FanWritesAvailable)
            {
                var reason = DesktopFanWritesBlocked
                    ? DesktopFanWriteBlockedMessage
                    : $"Fan control unavailable: {_fanController.Status}";
                RecordFanCommand("RestoreOemAutoControl", "OEM auto", false, reason);
                _logging.Warn($"OEM auto restore skipped; {reason}");
                return false;
            }

            try
            {
                DisableCurve();
                _activePreset = null;
                _currentFanMode = "Auto";

                var restored = RestoreAutoControlSerialized();
                var reset = false;
                try
                {
                    reset = _fanController.ResetEcToDefaults();
                }
                catch (Exception resetEx)
                {
                    _logging.Warn($"OEM auto restore reset step failed: {resetEx.Message}");
                }

                var success = restored || reset;
                RecordFanCommand(
                    "RestoreOemAutoControl",
                    "OEM auto",
                    success,
                    $"RestoreAutoControl={restored}; ResetEcToDefaults={reset}");
                _logging.Info(success
                    ? "OEM auto fan control restored"
                    : "OEM auto restore did not report success");
                PublishPresetApplied(_currentFanMode);
                return success;
            }
            catch (Exception ex)
            {
                RecordFanCommand("RestoreOemAutoControl", "OEM auto", false, ex.Message);
                _logging.Error("Failed to restore OEM auto fan control", ex);
                return false;
            }
        }

        /// <summary>
        /// Prepare fan control for system suspend by pausing active fan-engine writes
        /// and restoring BIOS auto policy to avoid fan spikes while sleeping.
        /// </summary>
        public void HandleSystemSuspend()
        {
            _systemSuspendActive = true;

            // Reset thermal protection state so stale readings cannot pin max fans while suspended.
            _thermalProtectionActive = false;
            _thermalAboveThresholdSince = DateTime.MinValue;
            _thermalBelowReleaseSince = DateTime.MinValue;

            // Stop any backend-owned Max-mode keepalive/reassertion timer unconditionally and
            // first, before attempting the BIOS auto-control restore below. That timer (when
            // present) runs on its own independent schedule inside the fan controller with no
            // suspend awareness of its own — it previously only stopped as a side effect of
            // RestoreAutoControlSerialized() succeeding. If that call threw or its underlying
            // WMI write failed (both plausible while the system is mid-suspend), the timer kept
            // firing every few seconds and reasserting Max fan mode for as long as the process
            // had threads running during the suspend transition (GitHub #146: fans observed
            // stuck at max through lid-close, followed by a BIOS thermal shutdown while the
            // laptop sat in a closed bag).
            try
            {
                _fanController.StopCountdownExtension();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to stop fan countdown extension during suspend: {ex.Message}");
            }

            try
            {
                var restored = FanWritesAvailable && RestoreAutoControlSerialized();
                _logging.Info(restored
                    ? "System suspend detected — fan engine paused and BIOS auto fan control restored"
                    : "System suspend detected — fan engine paused (BIOS auto fan control restore was not available or did not succeed; Max-mode keepalive has been stopped regardless)");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore BIOS auto control during suspend: {ex.Message}");
            }
        }

        /// <summary>
        /// Reapply active fan mode after system resume.
        /// Helps recover from BIOS/firmware fan policy resets during sleep.
        /// </summary>
        public void HandleSystemResume()
        {
            _systemSuspendActive = false;

            try
            {
                if (_activePreset != null)
                {
                    _logging.Info($"Re-applying fan preset after resume: {_activePreset.Name}");
                    ApplyPreset(_activePreset);
                    return;
                }

                if (_curveEnabled)
                {
                    _logging.Info("Fan curve mode active on resume — forcing immediate curve refresh");
                    _lastCurveUpdate = DateTime.MinValue;
                    _lastCurveForceRefresh = DateTime.MinValue;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to re-apply fan settings after resume: {ex.Message}");
            }
        }

        /// <summary>
        /// Run a Max verification using the underlying fan controller and return the result and details.
        /// </summary>
        public (bool success, string details) VerifyMaxApplied()
        {
            try
            {
                if (_fanController == null)
                    return (false, "No fan controller available");

                var ok = _fanController.VerifyMaxApplied(out string details);
                return (ok, details);
            }
            catch (Exception ex)
            {
                return (false, $"VerifyMaxApplied exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Check RPM sanity for two mismatch classes:
        /// 1) duty > 0% but RPM = 0 for >30s (possible sensor/hardware failure)
        /// 2) duty = 0% but RPM stays high for >30s (likely firmware/ownership override)
        /// Called periodically from the monitoring loop after preset applies.
        /// </summary>
        public void CheckRpmSanity(int dutyPercent, int rpmReading)
        {
            // Only track when duty > 0% (fans commanded to run)
            if (dutyPercent > 0 && rpmReading == 0)
            {
                // Start the zero-RPM timer if not already started
                if (_zeroRpmWithDutyStartTime == DateTime.MinValue)
                {
                    _zeroRpmWithDutyStartTime = DateTime.UtcNow;
                    _logging.Warn($"RPM sanity check: zero RPM detected with {dutyPercent}% duty - monitoring for hardware issue");
                }

                // Check if we've exceeded the threshold
                var elapsedSeconds = (DateTime.UtcNow - _zeroRpmWithDutyStartTime).TotalSeconds;
                if (elapsedSeconds >= RpmSanityCheckThresholdSeconds && !_rpmSanityWarningRaised)
                {
                    _rpmSanityWarningRaised = true;
                    var message = $"Fan hardware may be failing: duty cycle at {dutyPercent}% but RPM reads 0 for {(int)elapsedSeconds}+ seconds. " +
                                  "This suggests either a fan hardware failure or broken RPM sensor. Run hardware diagnostics to verify fan operation.";
                    _logging.Error(message);

                    // Raise event for UI banner
                    RpmSanityCheckWarning?.Invoke(this, new RpmSanityCheckEventArgs
                    {
                        DutyPercent = dutyPercent,
                        RpmReading = rpmReading,
                        DurationAtZero = TimeSpan.FromSeconds(elapsedSeconds),
                        Message = message
                    });
                }
            }
            else if (dutyPercent == 0 && rpmReading >= HighRpmSanityThreshold)
            {
                if (_highRpmWithZeroDutyStartTime == DateTime.MinValue)
                {
                    _highRpmWithZeroDutyStartTime = DateTime.UtcNow;
                    _logging.Warn($"RPM sanity check: high RPM detected with 0% duty ({rpmReading} RPM) - monitoring for firmware override/mismatch");
                }

                var elapsedSeconds = (DateTime.UtcNow - _highRpmWithZeroDutyStartTime).TotalSeconds;
                if (elapsedSeconds >= RpmSanityCheckThresholdSeconds && !_highRpmZeroDutyWarningRaised)
                {
                    _highRpmZeroDutyWarningRaised = true;
                    var message = $"Fan control mismatch detected: requested duty is 0% but RPM remains around {rpmReading} for {(int)elapsedSeconds}+ seconds. " +
                                  "This usually means firmware or another app still owns fan control. Try toggling Max then reapply your curve, and close OMEN Gaming Hub/Light Studio if running.";
                    _logging.Warn(message);

                    RpmSanityCheckWarning?.Invoke(this, new RpmSanityCheckEventArgs
                    {
                        DutyPercent = dutyPercent,
                        RpmReading = rpmReading,
                        DurationAtZero = TimeSpan.FromSeconds(elapsedSeconds),
                        Message = message
                    });
                }
            }
            else if (dutyPercent > 0 && rpmReading > 0)
            {
                // RPM is healthy - clear the warning state
                _zeroRpmWithDutyStartTime = DateTime.MinValue;
                _rpmSanityWarningRaised = false;
                _highRpmWithZeroDutyStartTime = DateTime.MinValue;
                _highRpmZeroDutyWarningRaised = false;
                if (_lastFanRpm == 0)
                {
                    _logging.Info($"RPM sanity check: recovered - RPM now reading {rpmReading} at {dutyPercent}% duty");
                }
            }
            else if (dutyPercent == 0)
            {
                // Duty is off, reset the timer
                _zeroRpmWithDutyStartTime = DateTime.MinValue;
                _rpmSanityWarningRaised = false;
                if (rpmReading < HighRpmSanityThreshold)
                {
                    _highRpmWithZeroDutyStartTime = DateTime.MinValue;
                    _highRpmZeroDutyWarningRaised = false;
                }
            }

            _lastFanDutyPercent = dutyPercent;
            _lastFanRpm = rpmReading;
        }
        
        /// <summary>
        /// Public method to reset the RPM sanity warning (called when user dismisses the warning).
        /// </summary>
        public void DismissRpmSanityWarning()
        {
            _zeroRpmWithDutyStartTime = DateTime.MinValue;
            _rpmSanityWarningRaised = false;
            _highRpmWithZeroDutyStartTime = DateTime.MinValue;
            _highRpmZeroDutyWarningRaised = false;
            _logging.Info("RPM sanity warning dismissed by user");
        }
        
        /// <summary>
        /// Event raised when a preset is applied (for UI synchronization).
        /// </summary>
        public event EventHandler<string>? PresetApplied;

        /// <summary>
        /// Event raised when curve or backend hold activity changes.
        /// Used by monitoring cadence wiring to keep tray-only mode lightweight
        /// without dropping telemetry cadence while OmenCore owns fan state.
        /// </summary>
        public event EventHandler<bool>? FanActivityStateChanged;
        
        /// <summary>
        /// Event raised when RPM sanity check detects zero RPM for >30s with active duty cycle.
        /// </summary>
        public event EventHandler<RpmSanityCheckEventArgs>? RpmSanityCheckWarning;
        
        // RPM sanity check state - monitor for broken RPM readback after preset applies
        // If duty > 0% but RPM reads 0 for >30 seconds, likely indicates hardware issue
        private DateTime _zeroRpmWithDutyStartTime = DateTime.MinValue;
        private DateTime _highRpmWithZeroDutyStartTime = DateTime.MinValue;
        private int _lastFanDutyPercent = -1;
        private int _lastFanRpm = -1;
        private bool _rpmSanityWarningRaised = false;
        private bool _highRpmZeroDutyWarningRaised = false;
        private const int RpmSanityCheckThresholdSeconds = 30;
        private const int HighRpmSanityThreshold = 1800;
        
        /// <summary>
        /// Enable/disable thermal protection override.
        /// When enabled, fans will ramp to max if temps exceed 90°C, even in Auto mode.
        /// </summary>
        public bool ThermalProtectionEnabled
        {
            get => _thermalProtectionEnabled;
            set
            {
                _thermalProtectionEnabled = value;
                _logging.Info($"Thermal protection: {(value ? "Enabled" : "Disabled")}");
            }
        }
        
        /// <summary>
        /// Configure hysteresis settings to prevent fan oscillation.
        /// Also loads thermal protection threshold from settings.
        /// </summary>
        public void SetHysteresis(FanHysteresisSettings settings)
        {
            _hysteresis = settings ?? new FanHysteresisSettings();
            _thermalProtectionEnabled = _hysteresis.ThermalProtectionEnabled;
            
            // Load configurable thermal protection threshold, clamp to safe range
            // v2.8.0: Widened range — previous max of 90°C was too restrictive for
            // high-power laptops where 85°C is normal gaming temp
            _thermalProtectionThreshold = Math.Clamp(settings?.ThermalProtectionThreshold ?? 90.0, 75.0, 95.0);

            // Load configurable emergency (hard 100%) threshold, clamp to a safe range, and keep
            // it meaningfully above the ramp-start threshold so the two never invert (e.g. a user
            // raising ramp-start to 95°C doesn't leave emergency sitting at or below it).
            var emergencyRaw = Math.Clamp(settings?.ThermalEmergencyThreshold ?? 95.0, 90.0, 99.0);
            _thermalEmergencyThreshold = Math.Max(emergencyRaw, _thermalProtectionThreshold + 2.0);

            _logging.Info($"Fan hysteresis: {(_hysteresis.Enabled ? $"Enabled (deadzone={_hysteresis.DeadZone}°C, ramp↑={_hysteresis.RampUpDelay}s, ramp↓={_hysteresis.RampDownDelay}s)" : "Disabled")}");
            _logging.Info($"Thermal protection: {(_thermalProtectionEnabled ? $"Enabled (ramp={_thermalProtectionThreshold:F0}°C, emergency={_thermalEmergencyThreshold:F0}°C)" : "Disabled — user has taken full responsibility for thermal management")}");
        }

        /// <summary>
        /// Create FanService with the new IFanController interface.
        /// </summary>
        public FanService(IFanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, NotificationService notificationService, int pollMs, ResumeRecoveryDiagnosticsService resumeDiagnostics, RuntimeEcOperationCoordinator? ecOperationCoordinator = null, DeviceCapabilities? capabilities = null)
        {
            _fanController = controller;
            _thermalProvider = thermalProvider;
            _logging = logging;
            _notificationService = notificationService;
            _resumeDiagnostics = resumeDiagnostics;
            _ecOperationCoordinator = ecOperationCoordinator ?? new RuntimeEcOperationCoordinator(logging);
            _capabilities = capabilities;
            _monitorPollPeriod = TimeSpan.FromMilliseconds(Math.Max(MonitorMinIntervalMs, pollMs));
            ThermalSamples = new ReadOnlyObservableCollection<ThermalSample>(_thermalSamples);
            FanTelemetry = new ReadOnlyObservableCollection<FanTelemetry>(_fanTelemetry);
            
            _logging.Info($"FanService initialized with backend: {Backend}, curve interval: {CurveUpdateIntervalMs}ms");
        }

        /// <summary>
        /// Legacy constructor for compatibility with existing FanController.
        /// </summary>
        public FanService(FanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, NotificationService notificationService, int pollMs, ResumeRecoveryDiagnosticsService resumeDiagnostics, RuntimeEcOperationCoordinator? ecOperationCoordinator = null, DeviceCapabilities? capabilities = null)
            : this(new EcFanControllerWrapper(controller, null!, logging), thermalProvider, logging, notificationService, pollMs, resumeDiagnostics, ecOperationCoordinator, capabilities)
        {
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            
            // Immediately populate fan telemetry so UI shows RPM right away
            try
            {
                var fanSpeeds = _fanController.ReadFanSpeeds().ToList();

                // Populate internal state (required for headless/unit-test scenarios).
                _lastFanSpeeds = fanSpeeds.Select(f => f.Rpm).ToList();
                _fanChangeConfirmCounters = Enumerable.Repeat(0, _lastFanSpeeds.Count).ToList();
                _fanChangePendingRpms = new List<int>(_lastFanSpeeds);

                // Update UI-bound collection only when a WPF dispatcher is available.
                // Use BeginInvoke (fire-and-forget) to avoid a blocking cross-thread call
                // during service startup, which could deadlock if Start() is called from the UI thread.
                if (App.Current?.Dispatcher != null)
                {
                    App.Current.Dispatcher.BeginInvoke(() =>
                    {
                        SyncFanTelemetryCollection(fanSpeeds);
                    });
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not read initial fan speeds: {ex.Message}");
            }
            
            _ = Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        /// <summary>
        /// Apply a preset and start continuous curve monitoring if it has a curve.
        /// </summary>
        public bool ApplyPreset(FanPreset preset, bool immediate = false)
        {
            if (!FanCurvesAvailable && HasCurvePayload(preset) && preset.Mode == FanMode.Manual)
            {
                _logging.Warn($"Fan preset '{preset.Name}' skipped; custom fan curves are disabled for this model.");
                RecordFanCommand("ApplyPreset", preset.Name, false, "Fan curves disabled by model capability database");
                return false;
            }

            preset = PreparePresetForCapability(preset);

            if (!FanWritesAvailable)
            {
                _logging.Warn($"Fan preset '{preset.Name}' skipped; fan control unavailable ({_fanController.Status})");
                RecordFanCommand("ApplyPreset", preset.Name, false, $"Fan control unavailable: {_fanController.Status}");
                return false;
            }

            // Do not allow external preset changes while in diagnostic mode — this prevents
            // the UI or other code from overriding a manual diagnostics test (user-reported bug).
            if (_diagnosticModeActive)
            {
                _logging.Warn($"Skipping preset '{preset.Name}' while in diagnostic mode");
                RecordFanCommand("ApplyPreset", preset.Name, false, "Diagnostic mode active");
                return false;
            }

            // Mark a transition window so the monitor loop holds the last-known-good RPM
            // instead of surfacing the transient 0-RPM that the BIOS emits during mode handoff.
            _fanModeTransitioning = true;
            _fanTransitionUntil = DateTime.UtcNow.AddMilliseconds(FanTransitionHoldMs);

            // Apply the preset's thermal policy first
            var controllerAcceptedPreset = ApplyPresetSerialized(preset);
            RecordFanCommand("ApplyPreset.Controller", preset.Name, controllerAcceptedPreset, controllerAcceptedPreset ? "Controller returned success" : "Controller returned false");
            if (controllerAcceptedPreset)
            {
                _logging.Info($"Fan preset '{preset.Name}' applied via {Backend} (controller returned success) - verifying state...");

                // Record previous state so we can rollback if verification fails
                var previousPreset = _activePreset;
                var previousCurveEnabled = _curveEnabled;
                var previousActiveCurve = _activeCurve != null ? new List<FanCurvePoint>(_activeCurve) : null;
                var previousFanMode = _currentFanMode;

                bool isMaxPreset = IsMaxPreset(preset);
                bool isAutoPreset = IsAutoPreset(preset);
                bool hasCurvePayload = HasCurvePayload(preset);

                // Verification helper
                bool VerificationPasses()
                {
                    try
                    {
                        // 1) Max preset: verify via controller-specific verification (if available)
                        if (isMaxPreset)
                        {
                            var (ok, details) = VerifyMaxApplied();
                            if (ok) return true;

                            _logging.Warn($"VerifyMaxApplied did not confirm Max immediately: {details}. Keeping Max requested because some firmware reports stale RPM/level for 10-15s after SetFanMax.");
                            return true;
                        }

                        // 2) For curve-based presets: the controller returning true is sufficient.
                        // The continuous curve engine maintains correct fan speeds each poll cycle.
                        // Checking for RPM change here is unreliable — fans may already be near the
                        // target speed (e.g. at idle, 40% curve target ≈ current 40% idle speed),
                        // causing a false "no change detected" rollback that permanently disables
                        // the curve even though the preset was applied successfully.
                        if (hasCurvePayload)
                            return true;

                        // 3) For policy-only presets (Auto/Performance/Quiet/etc.) where there is no
                        // explicit curve payload, a successful controller apply is sufficient.
                        // This prevents false rollback for startup-restored built-in presets that are
                        // represented by Mode with an empty curve.
                        if (IsModeOnlyPreset(preset))
                            return true;

                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Preset verification threw: {ex.Message}");
                        return false;
                    }
                }

                // If preset involves immediate fan changes (Max or performance curve), verify the device state.
                var verified = VerificationPasses();

                if (!verified)
                {
                    RecordFanCommand("ApplyPreset.Verify", preset.Name, false, "Verification failed; attempting rollback");
                    // Attempt rollback to previous state
                    _logging.Error($"Preset '{preset.Name}' verification failed — attempting rollback to previous preset: {(previousPreset?.Name ?? "<none>")}");
                    try
                    {
                        if (previousPreset != null)
                        {
                            // Reapply previous preset on controller
                            ApplyPresetSerialized(previousPreset);
                            RecordFanCommand("ApplyPreset.Rollback", previousPreset.Name, true, $"Rollback after failed preset '{preset.Name}'");

                            // Restore previous curve state as necessary
                            if (previousCurveEnabled && previousActiveCurve != null)
                            {
                                EnableCurve(previousActiveCurve, previousPreset);
                            }
                            else
                            {
                                DisableCurve();
                            }

                            _activePreset = previousPreset;
                            _currentFanMode = previousFanMode;
                            _logging.Info($"Rollback to preset '{previousPreset.Name}' completed");
                        }
                        else
                        {
                            // No previous preset — restore BIOS auto control
                            DisableCurve();
                            RestoreAutoControlSerialized();
                            RecordFanCommand("ApplyPreset.Rollback", "BIOS auto", true, $"Rollback after failed preset '{preset.Name}'");
                            _activePreset = null;
                            _currentFanMode = "Auto";
                            _logging.Info("Rollback: restored BIOS auto control");
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordFanCommand("ApplyPreset.Rollback", previousPreset?.Name ?? "BIOS auto", false, ex.Message);
                        _logging.Error($"Rollback failed: {ex.Message}", ex);
                    }

                    // Do not raise PresetApplied on verification failure
                    return false;
                }

                // Verification succeeded — update UI-visible state and enable curve/mode as before
                _logging.Info($"Preset '{preset.Name}' verified successfully");
                RecordFanCommand("ApplyPreset.Verify", preset.Name, true, "Verification passed");

                _currentFanMode = ResolvePresetModeLabel(preset, isMaxPreset, isAutoPreset);

                if (isMaxPreset)
                {
                    ApplyMaxCooling(forceApply: true);
                    _activePreset = preset;
                }
                else if (isAutoPreset)
                {
                    if (hasCurvePayload)
                    {
                        EnableCurve(preset.Curve.ToList(), preset);
                        _logging.Info($"Preset '{preset.Name}' preserved controller-applied Auto policy and enabled explicit curve payload");

                        if (immediate)
                        {
                            var temps = _thermalProvider.ReadTemperatures().ToList();
                            var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                            var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                            _ = ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);
                        }
                    }
                    else
                    {
                        DisableCurve();
                        _activePreset = preset;

                        RestoreAutoControlSerialized();
                        _logging.Info($"✓ Preset '{preset.Name}' using BIOS auto control (fans can stop at idle)");
                    }
                }
                else if (hasCurvePayload)
                {
                    EnableCurve(preset.Curve.ToList(), preset);
                    _logging.Info($"✓ Preset '{preset.Name}' curve enabled with {preset.Curve.Count} points");

                    if (immediate)
                    {
                        var temps = _thermalProvider.ReadTemperatures().ToList();
                        var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                        var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                        _ = ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);
                    }
                }
                else
                {
                    DisableCurve();
                    _activePreset = preset;
                    _logging.Info($"Preset '{preset.Name}' using BIOS control (no curve defined)");
                }

                // Raise event for UI synchronization (sidebar, tray, etc.)
                PublishPresetApplied(preset.Name);
                return true;
            }
            else
            {
                _logging.Warn($"Fan preset '{preset.Name}' failed to apply via {Backend}");
                return false;
            }
        }

        private static bool IsMaxPreset(FanPreset preset)
        {
            return (preset.Mode == FanMode.Max || FanModeNameResolver.IsMaxAlias(preset.Name)) &&
                   !FanModeNameResolver.IsAutoAlias(preset.Name);
        }

        private static bool IsAutoPreset(FanPreset preset)
        {
            return preset.Mode == FanMode.Auto ||
                   FanModeNameResolver.IsAutoAlias(preset.Name);
        }

        private static bool IsModeOnlyPreset(FanPreset preset)
        {
            if (preset.Mode == FanMode.Auto || preset.Mode == FanMode.Performance || preset.Mode == FanMode.Quiet)
            {
                return true;
            }

            return FanModeNameResolver.IsAutoAlias(preset.Name) ||
                   FanModeNameResolver.IsPerformanceAlias(preset.Name) ||
                   FanModeNameResolver.IsQuietAlias(preset.Name);
        }

        private static bool HasCurvePayload(FanPreset preset)
        {
            return preset.Curve != null && preset.Curve.Count > 0;
        }

        private static string ResolvePresetModeLabel(FanPreset preset, bool isMaxPreset, bool isAutoPreset)
        {
            var isExtremeAlias = HasAliasToken(preset.Name, "extreme");
            if (isMaxPreset)
            {
                return isExtremeAlias ? "Extreme" : "Max";
            }

            if (isAutoPreset)
            {
                return "Auto";
            }

            var isGamingAlias = HasAliasToken(preset.Name, "gaming");
            return preset.Mode switch
            {
                FanMode.Manual => "Custom",
                FanMode.Quiet => "Quiet",
                FanMode.Performance when isExtremeAlias => "Extreme",
                FanMode.Performance when isGamingAlias => "Gaming",
                FanMode.Performance => "Performance",
                _ => preset.Name
            };
        }

        private static bool HasAliasToken(string? value, string alias)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(alias))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized == alias)
            {
                return true;
            }

            var tokens = normalized
                .Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Any(token => token == alias);
        }

        /// <summary>
        /// Apply a custom curve and start continuous monitoring.
        /// </summary>
        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve, bool immediate = false)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn($"Custom fan curve skipped; fan control unavailable ({_fanController.Status})");
                RecordFanCommand("ApplyCustomCurve", "custom curve", false, $"Fan control unavailable: {_fanController.Status}");
                return;
            }

            if (!FanCurvesAvailable)
            {
                _logging.Warn("Custom fan curve skipped; this model is limited to OEM fan profiles.");
                RecordFanCommand("ApplyCustomCurve", "custom curve", false, "Fan curves disabled by model capability database");
                return;
            }
            
            var curveList = curve.ToList();
            if (!ValidateCurve(curveList, out var validationError))
            {
                _logging.Warn($"Custom fan curve rejected: {validationError}");
                RecordFanCommand("ApplyCustomCurve", $"{curveList.Count} point(s)", false, validationError);
                return;
            }
            
            // Apply once immediately, then enable continuous monitoring
            if (ApplyCustomCurveSerialized(curveList))
            {
                EnableCurve(curveList, null);
                _currentFanMode = "Custom";
                RecordFanCommand("ApplyCustomCurve", $"{curveList.Count} point(s)", true, "Controller returned success");
                _logging.Info($"Custom fan curve applied and enabled with {curveList.Count} points");

                if (immediate)
                {
                    // Apply curve immediately based on current temps
                    var temps = _thermalProvider.ReadTemperatures().ToList();
                    var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                    var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                    _ = ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);
                }
            }
            else
            {
                RecordFanCommand("ApplyCustomCurve", $"{curveList.Count} point(s)", false, "Controller returned false");
                _logging.Warn($"Custom fan curve failed to apply via {Backend}");
            }
        }
        
        /// <summary>
        /// Enable continuous curve application.
        /// </summary>
        private void EnableCurve(List<FanCurvePoint> curve, FanPreset? preset)
        {
            lock (_curveLock)
            {
                _activeCurve = curve.OrderBy(p => p.TemperatureC).ToList();
                _activePreset = preset;
                _curveEnabled = true;
                _lastCurveUpdate = DateTime.MinValue; // Force immediate update
                _lastAppliedFanPercent = -1;
                _zeroRpmCurveCommandSince = DateTime.MinValue;
                _lastCurveZeroRpmWakeKick = DateTime.MinValue;
                ResetCurveTemperatureSmoothing();
            }

            NotifyFanActivityStateChangedIfNeeded();
        }
        
        /// <summary>
        /// Disable continuous curve application.
        /// </summary>
        public void DisableCurve()
        {
            lock (_curveLock)
            {
                _curveEnabled = false;
                _activeCurve = null;
                _cpuCurve = null;
                _gpuCurve = null;
                _independentCurvesEnabled = false;
                _lastAppliedFanPercent = -1;
                _lastAppliedCpuFanPercent = -1;
                _lastAppliedGpuFanPercent = -1;
                _zeroRpmCurveCommandSince = DateTime.MinValue;
                _lastCurveZeroRpmWakeKick = DateTime.MinValue;
                ResetCurveTemperatureSmoothing();
            }

            NotifyFanActivityStateChangedIfNeeded();
        }
        
        /// <summary>
        /// Enable independent CPU and GPU fan curves.
        /// </summary>
        /// <param name="cpuCurve">The fan curve to use for CPU temperature</param>
        /// <param name="gpuCurve">The fan curve to use for GPU temperature</param>
        public void EnableIndependentCurves(List<FanCurvePoint> cpuCurve, List<FanCurvePoint> gpuCurve)
        {
            if (!ValidateCurve(cpuCurve, out var cpuError))
            {
                _logging.Warn($"CPU curve rejected: {cpuError}");
                return;
            }
            if (!ValidateCurve(gpuCurve, out var gpuError))
            {
                _logging.Warn($"GPU curve rejected: {gpuError}");
                return;
            }

            lock (_curveLock)
            {
                _cpuCurve = cpuCurve.OrderBy(p => p.TemperatureC).ToList();
                _gpuCurve = gpuCurve.OrderBy(p => p.TemperatureC).ToList();
                _activeCurve = null; // Clear single curve mode
                _activePreset = null;
                _curveEnabled = true;
                _independentCurvesEnabled = true;
                _lastCurveUpdate = DateTime.MinValue; // Force immediate update
                _lastAppliedCpuFanPercent = -1;
                _lastAppliedGpuFanPercent = -1;
                ResetCurveTemperatureSmoothing();
            }

            NotifyFanActivityStateChangedIfNeeded();

            _logging.Info($"Independent fan curves enabled - CPU: {_cpuCurve.Count} points, GPU: {_gpuCurve.Count} points");
        }

        /// <summary>
        /// Validate that a fan curve is monotonic in temperature and within safe percentage bounds.
        /// </summary>
        private bool ValidateCurve(IReadOnlyList<FanCurvePoint> curve, out string error)
        {
            error = string.Empty;

            if (curve.Count < 2)
            {
                error = "Curve needs at least 2 points";
                return false;
            }

            int lastTemp = int.MinValue;
            foreach (var point in curve)
            {
                if (point.TemperatureC < lastTemp)
                {
                    error = "Temperatures must be non-decreasing";
                    return false;
                }
                if (point.FanPercent < 0 || point.FanPercent > 100)
                {
                    error = "Fan % must stay between 0 and 100";
                    return false;
                }
                lastTemp = point.TemperatureC;
            }

            return true;
        }
        
        /// <summary>
        /// Apply safety bounds clamping to prevent dangerous fan curves.
        /// v2.8.0: Relaxed thresholds — previous values (60°C→40%, 70°C→70%) were
        /// far too aggressive for gaming laptops that routinely idle at 55-65°C and
        /// run 75-90°C under load. Users reported fans ramping up on Silent.
        /// Only intervene at genuinely dangerous temperatures.
        /// </summary>
        private double ApplySafetyBoundsClamping(double fanPercent, double temperatureC)
        {
            // Respect the same opt-out CheckThermalProtection already honors — the toggle's own
            // doc comment promises "fans will NEVER be automatically overridden by thermal
            // protection" when disabled, but this method used to apply its emergency/critical
            // floor unconditionally, which silently broke that promise for anyone on a custom
            // fan curve. Field report: users on 8D87/88F7 asked for a way to turn off the
            // "thermal emergency forces max fan" behavior entirely — this was the gap.
            if (!_thermalProtectionEnabled)
                return fanPercent;

            double clamped = fanPercent;

            // Emergency thermal protection (configurable, default 95°C) - force 100%
            if (temperatureC >= _thermalEmergencyThreshold)
            {
                if (fanPercent < 100.0)
                {
                    _logging.Warn($"EMERGENCY: Temperature {temperatureC:F1}°C >= {_thermalEmergencyThreshold:F0}°C, forcing fans to 100% (curve wanted {fanPercent:F0}%)");
                    return 100.0;
                }
            }

            // Critical: 90°C+ — minimum 80% (genuine danger zone)
            if (temperatureC >= 90.0)
            {
                clamped = Math.Max(fanPercent, 80.0);
            }
            // High: 85°C+ — minimum 60% (approaching throttle temp)
            else if (temperatureC >= 85.0)
            {
                clamped = Math.Max(fanPercent, 60.0);
            }
            // Moderate: 80°C+ — minimum 40% (warm but typical under load)
            else if (temperatureC >= 80.0)
            {
                clamped = Math.Max(fanPercent, 40.0);
            }
            // Light: below 80°C — trust the user's curve entirely
            // Gaming laptops idle at 50-65°C; there's no danger here
            
            if (clamped > fanPercent)
            {
                _logging.Info($"Safety clamp: {fanPercent:F0}% → {clamped:F0}% (temp {temperatureC:F1}°C)");
            }
            
            return clamped;
        }

        private void ResetCurveTemperatureSmoothing()
        {
            _smoothedCpuCurveTemp = double.NaN;
            _smoothedGpuCurveTemp = double.NaN;
        }

        private (double cpuTemp, double gpuTemp) SmoothCurveTemperatures(double cpuTemp, double gpuTemp)
        {
            _smoothedCpuCurveTemp = SmoothCurveTemperature("CPU", _smoothedCpuCurveTemp, cpuTemp);
            _smoothedGpuCurveTemp = SmoothCurveTemperature("GPU", _smoothedGpuCurveTemp, gpuTemp);
            return (_smoothedCpuCurveTemp, _smoothedGpuCurveTemp);
        }

        private double SmoothCurveTemperature(string sensorName, double previous, double current)
        {
            if (current <= 0 || double.IsNaN(current) || double.IsInfinity(current))
            {
                return current;
            }

            if (double.IsNaN(previous) || previous <= 0 || current >= CurveTempSmoothingBypassTempC)
            {
                return current;
            }

            var delta = current - previous;
            var limitedDelta = delta > 0
                ? Math.Min(delta, CurveTempMaxRisePerEvaluationC)
                : Math.Max(delta, -CurveTempMaxDropPerEvaluationC);

            if (Math.Abs(limitedDelta - delta) > 0.01)
            {
                var smoothed = previous + limitedDelta;
                _logging.Debug($"Curve temp smoothing: {sensorName} {previous:F1}C -> {current:F1}C limited to {smoothed:F1}C");
                return smoothed;
            }

            return current;
        }
        
        /// <summary>
        /// Interpolate fan speed using slope-based linear interpolation (omen-fan style).
        /// This provides smoother fan speed transitions between curve points by
        /// calculating the exact speed based on where the temperature falls between points.
        /// 
        /// Based on the omen-fan Linux utility algorithm:
        /// slope[i] = (speed[i] - speed[i-1]) / (temp[i] - temp[i-1])
        /// speed = speed[i-1] + slope[i-1] * (current_temp - temp[i-1])
        /// </summary>
        /// <param name="curve">The fan curve points (must be sorted by temperature)</param>
        /// <param name="temperature">Current temperature in degrees Celsius</param>
        /// <returns>Interpolated fan speed percentage (0-100)</returns>
        private static double InterpolateFanSpeed(List<FanCurvePoint> curve, double temperature)
        {
            if (curve == null || curve.Count == 0)
                return 50; // Default fallback
                
            // Below first point - use minimum speed
            if (temperature <= curve[0].TemperatureC)
                return curve[0].FanPercent;
                
            // Above last point - use maximum speed (safety: always go to max, never drop)
            if (temperature >= curve[^1].TemperatureC)
                return curve[^1].FanPercent;
                
            // Find surrounding points and interpolate using slope calculation
            for (int i = 0; i < curve.Count - 1; i++)
            {
                var lower = curve[i];
                var upper = curve[i + 1];
                
                if (temperature >= lower.TemperatureC && temperature <= upper.TemperatureC)
                {
                    // Calculate slope: (speed_delta) / (temp_delta)
                    double tempDelta = upper.TemperatureC - lower.TemperatureC;
                    
                    // Avoid division by zero (curve points at same temperature)
                    if (tempDelta <= 0)
                        return lower.FanPercent;
                    
                    double slope = (upper.FanPercent - lower.FanPercent) / tempDelta;
                    
                    // Calculate interpolated speed
                    double interpolatedSpeed = lower.FanPercent + slope * (temperature - lower.TemperatureC);
                    
                    // Clamp to valid range (0-100%)
                    return Math.Clamp(interpolatedSpeed, 0, 100);
                }
            }
            
            // Fallback: use last curve point (shouldn't reach here if curve is sorted)
            return curve[^1].FanPercent;
        }
        
        /// <summary>
        /// Configure smoothing settings programmatically.
        /// </summary>
        public void SetSmoothingSettings(FanTransitionSettings settings)
        {
            if (settings == null) return;
            _smoothingEnabled = settings.EnableSmoothing;
            _smoothingDurationMs = Math.Max(0, settings.SmoothingDurationMs);
            _smoothingStepMs = Math.Max(50, settings.SmoothingStepMs);
            _logging.Info($"Fan smoothing: {(_smoothingEnabled ? "Enabled" : "Disabled")}, duration={_smoothingDurationMs}ms, step={_smoothingStepMs}ms");
        }

        /// <summary>
        /// Force apply the active curve immediately (or once) for the given temps.
        /// Useful for tests and immediate apply operations.
        /// </summary>
        public async Task ForceApplyCurveNowAsync(double cpuTemp, double gpuTemp, bool immediate = false, CancellationToken ct = default)
        {
            await Task.Run(async () =>
            {
                await ApplyCurveIfNeededAsync(cpuTemp, gpuTemp, immediate, ct);
            }, ct);
        }
        public bool FanWritesAvailable => _fanController.IsAvailable && !DesktopFanWritesBlocked;

        public bool FanCurvesAvailable => FanWritesAvailable && (_capabilities?.ModelConfig?.SupportsFanCurves ?? true);

        public bool ManualFanControlAvailable => FanCurvesAvailable;

        private bool DesktopFanWritesBlocked => _capabilities?.FanWritesBlockedForSafety == true;

        private const string DesktopFanWriteBlockedMessage = "Desktop fan writes disabled by v3.6.3 safety gate; telemetry only";

        private bool ConservativeLegacyFanPolicy =>
            IsModelProduct("88D2") ||
            (_capabilities?.ModelConfig?.Family == OmenModelFamily.Legacy &&
             _capabilities.ModelConfig.UserVerified == false &&
             _capabilities.ModelConfig.SupportsFanControlEc == false);

        private bool IsModelProduct(string productId) =>
            string.Equals(_capabilities?.ProductId, productId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_capabilities?.ModelConfig?.ProductId, productId, StringComparison.OrdinalIgnoreCase);

        private bool ShouldSkipConservativeCurveWrite(int targetFanPercent, DateTime now, bool forceRefresh, bool immediate)
        {
            if (!ConservativeLegacyFanPolicy || forceRefresh || immediate || _lastAppliedFanPercent < 0)
            {
                return false;
            }

            var delta = Math.Abs(targetFanPercent - _lastAppliedFanPercent);
            if (delta > 0 && delta < ConservativeCurveMinDeltaPercent)
            {
                _logging.Debug($"Conservative fan policy: skipped {delta}% curve delta below {ConservativeCurveMinDeltaPercent}% minimum");
                return true;
            }

            if (_lastCurveWriteUtc != DateTime.MinValue &&
                (now - _lastCurveWriteUtc).TotalMilliseconds < ConservativeCurveMinWriteIntervalMs)
            {
                _logging.Debug($"Conservative fan policy: skipped curve write inside {ConservativeCurveMinWriteIntervalMs}ms dwell window");
                return true;
            }

            return false;
        }

        private FanPreset PreparePresetForCapability(FanPreset preset)
        {
            if (FanCurvesAvailable || !HasCurvePayload(preset) || IsMaxPreset(preset))
            {
                return preset;
            }

            var mode = preset.Mode;
            if (mode == FanMode.Manual)
            {
                mode = FanMode.Auto;
            }

            _logging.Warn($"Fan preset '{preset.Name}' contains a curve, but custom fan curves are disabled for this model; applying OEM fan profile only.");
            return new FanPreset
            {
                Name = preset.Name,
                Mode = mode,
                Curve = new List<FanCurvePoint>(),
                IsBuiltIn = preset.IsBuiltIn
            };
        }

        private void MarkCurveWrite(DateTime now)
        {
            _lastCurveWriteUtc = now;
        }

        // Serialize controller write operations so concurrent triggers (monitor loop, tray,
        // hotkeys, automation) do not interleave EC/WMI fan writes unpredictably.
        private readonly object _fanWriteLock = new();

        private bool SetFanSpeedSerialized(int percent)
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("SetFanSpeed", $"{percent}%", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }

            lock (_fanWriteLock)
            {
                return _ecOperationCoordinator.Execute("FanService", "SetFanSpeed", () => _fanController.SetFanSpeed(percent));
            }
        }

        private bool SetFanSpeedsSerialized(int cpuPercent, int gpuPercent)
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("SetFanSpeeds", $"CPU {cpuPercent}% / GPU {gpuPercent}%", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }

            lock (_fanWriteLock)
            {
                return _ecOperationCoordinator.Execute("FanService", "SetFanSpeeds", () => _fanController.SetFanSpeeds(cpuPercent, gpuPercent));
            }
        }

        private bool ApplyPresetSerialized(FanPreset preset)
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ApplyPreset", preset.Name, false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }

            lock (_fanWriteLock)
            {
                return _ecOperationCoordinator.Execute("FanService", "ApplyPreset", () => _fanController.ApplyPreset(preset));
            }
        }

        private bool ApplyCustomCurveSerialized(List<FanCurvePoint> curve)
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ApplyCustomCurve", $"{curve.Count} point(s)", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }

            lock (_fanWriteLock)
            {
                return _ecOperationCoordinator.Execute("FanService", "ApplyCustomCurve", () => _fanController.ApplyCustomCurve(curve));
            }
        }

        private void ApplyMaxCoolingSerialized()
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ApplyMaxCooling", "Max", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return;
            }

            lock (_fanWriteLock)
            {
                _ecOperationCoordinator.Execute("FanService", "ApplyMaxCooling", () => _fanController.ApplyMaxCooling());
            }
        }

        private void ApplyAutoModeSerialized()
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ApplyAutoMode", "Auto", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return;
            }

            lock (_fanWriteLock)
            {
                _ecOperationCoordinator.Execute("FanService", "ApplyAutoMode", () => _fanController.ApplyAutoMode());
            }
        }

        private void ApplyQuietModeSerialized()
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ApplyQuietMode", "Quiet", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return;
            }

            lock (_fanWriteLock)
            {
                _ecOperationCoordinator.Execute("FanService", "ApplyQuietMode", () => _fanController.ApplyQuietMode());
            }
        }

        private bool RestoreAutoControlSerialized()
        {
            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("RestoreAutoControl", "BIOS auto", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }

            lock (_fanWriteLock)
            {
                return _ecOperationCoordinator.Execute("FanService", "RestoreAutoControl", () => _fanController.RestoreAutoControl());
            }
        }

        /// <summary>
        /// Forces a specific poll interval, bypassing the 1-second minimum floor and adaptive
        /// slowdown.  Intended for unit tests only — do not call in production code.
        /// </summary>
        public void ForceFixedPollInterval(int ms)
        {
            _monitorPollPeriod = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            _fixedPollOverrideMs = Math.Max(1, ms);
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            _logging.Info("Fan monitor loop started (with continuous curve support)");

            // Start() already seeds an initial fan read for immediate UI state.
            // Delay the first monitor iteration to the configured cadence so
            // smoothing confirmation windows and startup readback behavior remain predictable.
            var initialDelay = _fixedPollOverrideMs > 0
                ? _fixedPollOverrideMs
                : (int)_monitorPollPeriod.TotalMilliseconds;
            if (initialDelay > 0)
            {
                try
                {
                    await Task.Delay(initialDelay, token);
                }
                catch (OperationCanceledException)
                {
                    _logging.Info("Fan monitor loop stopped");
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // Stop()/Dispose() cancels then immediately disposes the CancellationTokenSource
                    // with no wait for this loop to observe it; Task.Delay can race and throw this
                    // instead of OperationCanceledException. Same graceful-exit outcome either way.
                    _logging.Info("Fan monitor loop stopped");
                    return;
                }
            }
            
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_systemSuspendActive)
                    {
                        await Task.Delay(1000, token);
                        continue;
                    }

                    // Read temperatures. Avoid LINQ here; this loop runs continuously.
                    double cpuTemp = 0;
                    double gpuTemp = 0;
                    double firstTemp = 0;
                    double secondTemp = 0;
                    int tempIndex = 0;
                    foreach (var reading in _thermalProvider.ReadTemperatures())
                    {
                        if (tempIndex == 0) firstTemp = reading.Celsius;
                        if (tempIndex == 1) secondTemp = reading.Celsius;

                        if (cpuTemp <= 0 && reading.Sensor.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                        {
                            cpuTemp = reading.Celsius;
                        }
                        else if (gpuTemp <= 0 && reading.Sensor.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuTemp = reading.Celsius;
                        }

                        tempIndex++;
                    }

                    if (cpuTemp <= 0) cpuTemp = firstTemp;
                    if (gpuTemp <= 0) gpuTemp = secondTemp;
                    
                    var sample = new ThermalSample
                    {
                        Timestamp = DateTime.Now,
                        CpuCelsius = cpuTemp,
                        GpuCelsius = gpuTemp
                    };
                    
                    // Check if temps are stable for adaptive polling
                    bool tempsStable = Math.Abs(cpuTemp - _lastCpuTemp) < TempChangeThreshold 
                                       && Math.Abs(gpuTemp - _lastGpuTemp) < TempChangeThreshold;
                    _lastCpuTemp = cpuTemp;
                    _lastGpuTemp = gpuTemp;
                    
                    if (tempsStable)
                    {
                        _stableReadings = Math.Min(_stableReadings + 1, StableThreshold + 1);
                    }
                    else
                    {
                        _stableReadings = 0;
                    }
                    
                    // Check thermal protection FIRST (overrides Auto mode when temps critical)
                    CheckThermalProtection(cpuTemp, gpuTemp);
                    
                    // Apply fan curve if enabled and enough time has passed
                    await ApplyCurveIfNeededAsync(cpuTemp, gpuTemp, immediate: false);
                    
                    // Read fan speeds (less frequently to reduce ACPI overhead)
                    var fanSpeeds = _fanController.ReadFanSpeeds().ToList();

                    // Prepare a stable "display" RPM list using confirmation counters to
                    // ignore single-sample spikes. Zero RPM is accepted immediately.
                    var fanCount = fanSpeeds.Count;
                    var primaryRawRpm = fanCount > 0 ? fanSpeeds[0].Rpm : 0;
                    _lastRawPrimaryFanRpm = fanCount > 0 ? primaryRawRpm : -1;
                    _lastReportedPrimaryFanDutyPercent = fanCount > 0 ? fanSpeeds[0].DutyCyclePercent : -1;

                    // Resize/initialize confirmation counters when fan count changes
                    if (_fanChangeConfirmCounters == null || _fanChangeConfirmCounters.Count != fanCount)
                    {
                        _fanChangeConfirmCounters = Enumerable.Repeat(0, fanCount).ToList();
                        _fanChangePendingRpms = Enumerable.Repeat(0, fanCount).ToList();
                        _zeroRpmDutySinceByFan = Enumerable.Repeat(DateTime.MinValue, fanCount).ToList();
                        _lastFanRpmStates = Enumerable.Repeat(TelemetryDataState.Unknown, fanCount).ToList();
                    }

                    var displayRpms = new List<int>(fanCount);
                    var rpmStates = new List<TelemetryDataState>(fanCount);
                    for (int i = 0; i < fanCount; i++)
                    {
                        var newRpm = fanSpeeds[i].Rpm;
                        var lastRpm = (i < _lastFanSpeeds.Count) ? _lastFanSpeeds[i] : 0;

                        // Accept zero immediately **only** when duty cycle also indicates stopped fans.
                        // If RPM==0 but duty-cycle > 0 we treat it as a readback glitch and DO NOT
                        // accept the zero (hold the previous value until duty indicates stopped).
                        var duty = (i < fanSpeeds.Count) ? fanSpeeds[i].DutyCyclePercent : 0;
                        var rpmReadbackUnavailable = UpdateRpmReadbackUnavailableState(i, duty, newRpm);
                        rpmStates.Add(rpmReadbackUnavailable
                            ? TelemetryDataState.Unavailable
                            : (newRpm > 0 ? TelemetryDataState.Valid : TelemetryDataState.Zero));

                        if (newRpm == 0)
                        {
                            // During a fan-mode transition the BIOS momentarily drives both RPM
                            // and duty to 0 while the new WMI command is being processed.  Hold
                            // the previous non-zero RPM for the duration of the transition window
                            // so the UI never shows an alarming "0 RPM" flash on a mode switch.
                            bool inTransitionWindow = _fanModeTransitioning && DateTime.UtcNow < _fanTransitionUntil;
                            if (inTransitionWindow && lastRpm > 0)
                            {
                                displayRpms.Add(lastRpm);
                                continue;
                            }

                            // Clear the transition flag once the window has expired.
                            if (_fanModeTransitioning && !inTransitionWindow)
                                _fanModeTransitioning = false;

                            if (duty == 0)
                            {
                                displayRpms.Add(0);
                                _fanChangeConfirmCounters[i] = 0;
                                if (i < _fanChangePendingRpms.Count) _fanChangePendingRpms[i] = 0;
                                continue;
                            }

                            // Inconsistent zero (rpm==0 but duty>0) — treat as transient noise and
                            // hold previous value (do not accept even after confirmation cycles).
                            // Reset counter if we were previously tracking a different pending value
                            // (e.g. switching from a large-RPM confirmation run to a zero-run).
                            if (i < _fanChangePendingRpms.Count && _fanChangePendingRpms[i] != 0)
                            {
                                _fanChangePendingRpms[i] = 0;
                                _fanChangeConfirmCounters[i] = 0;
                            }
                            _fanChangeConfirmCounters[i] = Math.Min(_fanChangeConfirmCounters[i] + 1, FanChangeConfirmRequiredCycles);
                            displayRpms.Add(lastRpm);
                            continue;
                        }

                        // Small differences are accepted immediately
                        if (Math.Abs(newRpm - lastRpm) <= FanSpeedChangeThreshold)
                        {
                            displayRpms.Add(newRpm);
                            _fanChangeConfirmCounters[i] = 0;
                            if (i < _fanChangePendingRpms.Count) _fanChangePendingRpms[i] = newRpm;
                            continue;
                        }

                        // Large differences require multiple consecutive confirmations.
                        // Reset counter when the candidate value itself changes (e.g. after an
                        // inconsistent-zero run, which counts towards 0, not towards this rpm).
                        if (i < _fanChangePendingRpms.Count && _fanChangePendingRpms[i] != newRpm)
                        {
                            _fanChangePendingRpms[i] = newRpm;
                            _fanChangeConfirmCounters[i] = 0;
                        }
                        _fanChangeConfirmCounters[i] = Math.Min(_fanChangeConfirmCounters[i] + 1, FanChangeConfirmRequiredCycles);
                        if (_fanChangeConfirmCounters[i] >= FanChangeConfirmRequiredCycles)
                        {
                            displayRpms.Add(newRpm);
                            _fanChangeConfirmCounters[i] = 0;
                        }
                        else
                        {
                            // Hold previous value until change is confirmed
                            displayRpms.Add(lastRpm);
                        }
                    }

                    // Determine whether the displayed RPMs differ from the last UI values
                    bool fanSpeedsChanged = !IntListsEqual(_lastFanSpeeds, displayRpms);
                    bool fanRpmStateChanged = !RpmStateListsEqual(_lastFanRpmStates, rpmStates);

                    // Update internal last-seen RPMs even when there's no UI dispatcher
                    // (keeps headless/unit-test scenarios deterministic)
                    _lastFanSpeeds = displayRpms;
                    _lastFanRpmStates = rpmStates;

                    // Use BeginInvoke to avoid potential deadlocks
                    App.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        _thermalSamples.Add(sample);
                        const int window = 120;
                        while (_thermalSamples.Count > window)
                        {
                            _thermalSamples.RemoveAt(0);
                        }

                        // Only update fan telemetry if values changed meaningfully
                        if (fanSpeedsChanged || fanRpmStateChanged)
                        {
                            SyncFanTelemetryCollection(fanSpeeds, displayRpms, rpmStates);
                        }
                        
                        // Check RPM sanity: if duty > 0% but RPM = 0, monitor for hardware failure
                        if (fanSpeeds.Count > 0)
                        {
                            var primaryFan = fanSpeeds[0];
                            CheckRpmSanity(primaryFan.DutyCyclePercent, primaryRawRpm);
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Gracefully exit on app shutdown
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error("Fan monitor loop error", ex);
                }

                // Adaptive polling delay - slower when temps stable to reduce DPC latency.
                // _fixedPollOverrideMs, when set, bypasses both the minimum floor and the
                // adaptive slowdown (used by unit tests to keep timing predictable).
                var pollDelay = _fixedPollOverrideMs > 0
                    ? _fixedPollOverrideMs
                    : (_stableReadings >= StableThreshold
                        ? MonitorMaxIntervalMs
                        : (int)_monitorPollPeriod.TotalMilliseconds);
                    
                try
                {
                    await Task.Delay(pollDelay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // See matching catch above the initial delay: Stop()/Dispose() can race
                    // Task.Delay's cancellation-token registration into throwing this instead
                    // of OperationCanceledException. Same graceful-exit outcome either way.
                    break;
                }
            }

            _logging.Info("Fan monitor loop stopped");
        }

        private void SyncFanTelemetryCollection(
            IReadOnlyList<FanTelemetry> fanSpeeds,
            IReadOnlyList<int>? displayRpms = null,
            IReadOnlyList<TelemetryDataState>? rpmStates = null)
        {
            var collectionResized = false;
            var itemsUpdated = 0;

            while (_fanTelemetry.Count > fanSpeeds.Count)
            {
                _fanTelemetry.RemoveAt(_fanTelemetry.Count - 1);
                collectionResized = true;
            }

            for (int i = 0; i < fanSpeeds.Count; i++)
            {
                var source = fanSpeeds[i];
                if (i >= _fanTelemetry.Count)
                {
                    _fanTelemetry.Add(CreateFanTelemetrySnapshot(source, i, displayRpms, rpmStates));
                    collectionResized = true;
                    itemsUpdated++;
                    continue;
                }

                if (UpdateFanTelemetrySnapshot(_fanTelemetry[i], source, i, displayRpms, rpmStates))
                {
                    itemsUpdated++;
                }
            }

            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized, itemsUpdated);
        }

        private static FanTelemetry CreateFanTelemetrySnapshot(
            FanTelemetry source,
            int index,
            IReadOnlyList<int>? displayRpms,
            IReadOnlyList<TelemetryDataState>? rpmStates)
        {
            var snapshot = new FanTelemetry();
            UpdateFanTelemetrySnapshot(snapshot, source, index, displayRpms, rpmStates);
            return snapshot;
        }

        private static bool UpdateFanTelemetrySnapshot(
            FanTelemetry target,
            FanTelemetry source,
            int index,
            IReadOnlyList<int>? displayRpms,
            IReadOnlyList<TelemetryDataState>? rpmStates)
        {
            var speedRpm = displayRpms != null && index < displayRpms.Count
                ? displayRpms[index]
                : (source.SpeedRpm != 0 ? source.SpeedRpm : source.Rpm);
            var rpmState = rpmStates != null && index < rpmStates.Count
                ? rpmStates[index]
                : source.RpmState;

            if (rpmState == TelemetryDataState.Unknown)
            {
                rpmState = speedRpm > 0 ? TelemetryDataState.Valid : TelemetryDataState.Zero;
            }

            var changed = target.Name != source.Name
                || target.DutyCyclePercent != source.DutyCyclePercent
                || Math.Abs(target.Temperature - source.Temperature) > 0.1
                || target.RpmSource != source.RpmSource
                || target.SpeedRpm != speedRpm
                || target.RpmState != rpmState;

            target.Name = source.Name;
            target.DutyCyclePercent = source.DutyCyclePercent;
            target.Temperature = source.Temperature;
            target.RpmSource = source.RpmSource;
            target.SpeedRpm = speedRpm;
            target.RpmState = rpmState;
            return changed;
        }

        private static bool IntListsEqual(List<int> left, List<int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RpmStateListsEqual(List<TelemetryDataState> left, List<TelemetryDataState> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool UpdateRpmReadbackUnavailableState(int fanIndex, int dutyPercent, int rawRpm)
        {
            if (fanIndex < 0)
            {
                return false;
            }

            if (_zeroRpmDutySinceByFan.Count <= fanIndex)
            {
                while (_zeroRpmDutySinceByFan.Count <= fanIndex)
                {
                    _zeroRpmDutySinceByFan.Add(DateTime.MinValue);
                }
            }

            if (dutyPercent > 0 && rawRpm == 0)
            {
                if (_zeroRpmDutySinceByFan[fanIndex] == DateTime.MinValue)
                {
                    _zeroRpmDutySinceByFan[fanIndex] = DateTime.UtcNow;
                    return false;
                }

                return (DateTime.UtcNow - _zeroRpmDutySinceByFan[fanIndex]).TotalSeconds >= RpmReadbackUnavailableThresholdSeconds;
            }

            _zeroRpmDutySinceByFan[fanIndex] = DateTime.MinValue;
            return false;
        }
        
        /// <summary>
        /// Thermal protection override - kicks fans to max when temps hit critical levels.
        /// This works even in Auto mode to prevent thermal throttling/damage.
        /// v2.8.0: Raised thresholds based on community feedback:
        /// - 90°C (configurable, see ThermalProtectionThreshold): Start ramping fans aggressively
        /// - 95°C (configurable, see ThermalEmergencyThreshold): Emergency max fans
        /// Entirely disableable via ThermalProtectionEnabled = false.
        /// </summary>
        // Remember the fan mode/preset BEFORE thermal protection kicks in
        private string? _preThermalFanMode;
        private FanPreset? _preThermalPreset;
        private int _preThermalFanPercent;
        
        private void CheckThermalProtection(double cpuTemp, double gpuTemp)
        {
            if (!_thermalProtectionEnabled || !FanWritesAvailable)
                return;

            // Sanity check: reject readings that are clearly hardware errors or garbage values
            // (e.g. uninitialized Afterburner shared-memory floats, WMI unit mis-conversion).
            // Real CPU/GPU temps are physically bounded to -10 °C … 150 °C.
            const double MaxSaneTemp = 150.0;
            const double MinSaneTemp = -10.0;
            if (cpuTemp > MaxSaneTemp || cpuTemp < MinSaneTemp)
            {
                _logging.Warn($"[ThermalProtection] Ignoring invalid CPU temp {cpuTemp:F0}°C (hardware read error — outside {MinSaneTemp}–{MaxSaneTemp}°C range)");
                cpuTemp = 0;
            }
            if (gpuTemp > MaxSaneTemp || gpuTemp < MinSaneTemp)
            {
                _logging.Warn($"[ThermalProtection] Ignoring invalid GPU temp {gpuTemp:F0}°C (hardware read error — outside {MinSaneTemp}–{MaxSaneTemp}°C range)");
                gpuTemp = 0;
            }
            // If both readings are invalid/zero, no reliable data — skip protection logic
            if (cpuTemp <= 0 && gpuTemp <= 0)
                return;

            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            var now = DateTime.UtcNow;
            
            // Emergency: temps >= configurable threshold (default 95°C) - immediate max fans (no debounce, safety critical)
            if (maxTemp >= _thermalEmergencyThreshold)
            {
                // Reset release timer
                _thermalBelowReleaseSince = DateTime.MinValue;
                
                if (!_thermalProtectionActive)
                {
                    // Store current fan state BEFORE thermal protection
                    _preThermalFanMode = _currentFanMode;
                    _preThermalPreset = _activePreset;
                    _preThermalFanPercent = _lastAppliedFanPercent;
                    
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL EMERGENCY: {maxTemp:F0}°C - forcing fans to 100%!");

                    // Record both individual readings (not just maxTemp) so a field report can
                    // tell whether this was a sustained real event or a single-sample sensor
                    // glitch — this tier intentionally has no debounce (safety critical), so it
                    // is the most exposed to a transient bad reading triggering a visible spike.
                    RecordFanCommand("ThermalProtection.Emergency", "100%", true,
                        $"cpu={cpuTemp:F1}C gpu={gpuTemp:F1}C maxTemp={maxTemp:F1}C (no debounce - safety critical)");

                    // Notify user of thermal protection activation
                    _notificationService?.ShowThermalProtectionActivated(maxTemp, "Emergency - Max Fans");

                    // FIRST activation — always write immediately
                    SetFanSpeedSerialized(100);
                    _lastThermalFanWriteTime = now;
                    _lastThermalFanPercent = 100;
                }
                else
                {
                    // Already in emergency mode — only re-apply periodically as keepalive
                    // Avoids hammering EC with 7+ writes every poll cycle (1-5s)
                    // EC overwhelm causes ACPI Event 13 → false battery critical → system shutdown
                    if ((now - _lastThermalFanWriteTime).TotalSeconds >= ThermalWriteMinIntervalSeconds)
                    {
                        SetFanSpeedSerialized(100);
                        _lastThermalFanWriteTime = now;
                        _logging.Debug($"Thermal emergency keepalive: re-applied 100% fans (every {ThermalWriteMinIntervalSeconds}s)");
                    }
                }
                return;
            }
            
            // Warning: temps >= configurable threshold (default 90°C) - boost fans
            // v2.8.0: Requires sustained temperature above threshold for debounce period
            if (maxTemp >= _thermalProtectionThreshold)
            {
                // Reset release timer since we're above threshold
                _thermalBelowReleaseSince = DateTime.MinValue;
                
                // Start tracking when temp first exceeded threshold
                if (_thermalAboveThresholdSince == DateTime.MinValue)
                {
                    _thermalAboveThresholdSince = now;
                }
                
                if (!_thermalProtectionActive)
                {
                    // Check debounce — temp must stay above threshold for N seconds
                    var aboveDuration = (now - _thermalAboveThresholdSince).TotalSeconds;
                    if (aboveDuration < ThermalActivateDebounceSeconds)
                    {
                        // Not yet sustained — don't activate yet
                        return;
                    }
                    
                    // Store current fan state BEFORE thermal protection
                    _preThermalFanMode = _currentFanMode;
                    _preThermalPreset = _activePreset;
                    _preThermalFanPercent = _lastAppliedFanPercent;
                    
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL WARNING: {maxTemp:F0}°C sustained for {aboveDuration:F0}s - boosting fan speed");
                    
                    // Notify user of thermal protection activation
                    _notificationService?.ShowThermalProtectionActivated(maxTemp, "Warning - Boosted Fans");
                }
                
                // Calculate thermal protection target: threshold = 85%, scaling to 100% at emergency
                var tempRange = _thermalEmergencyThreshold - _thermalProtectionThreshold;
                var thermalTargetPercent = (int)(85 + (maxTemp - _thermalProtectionThreshold) * (15.0 / tempRange));
                thermalTargetPercent = Math.Min(100, thermalTargetPercent);
                
                // BUG FIX #32: Don't REDUCE fan speed if already at higher speed!
                // If user is in Max mode at 100%, don't drop to 85%
                if (_preThermalFanPercent >= thermalTargetPercent)
                {
                    _logging.Info($"Thermal protection: keeping existing fan speed ({_preThermalFanPercent}%) >= thermal target ({thermalTargetPercent}%)");
                    return;
                }
                
                // Rate-limit EC writes: only re-apply if target changed or enough time passed
                // Avoids hammering EC with identical commands every poll cycle
                if (thermalTargetPercent == _lastThermalFanPercent && 
                    (now - _lastThermalFanWriteTime).TotalSeconds < ThermalWriteMinIntervalSeconds)
                {
                    return;
                }
                
                RecordFanCommand("ThermalProtection.Warning", $"{thermalTargetPercent}%", true,
                    $"cpu={cpuTemp:F1}C gpu={gpuTemp:F1}C maxTemp={maxTemp:F1}C sustainedFor={(now - _thermalAboveThresholdSince).TotalSeconds:F0}s");
                SetFanSpeedSerialized(thermalTargetPercent);
                _lastThermalFanWriteTime = now;
                _lastThermalFanPercent = thermalTargetPercent;
                return;
            }
            
            // Temps dropped below threshold — reset activate timer
            _thermalAboveThresholdSince = DateTime.MinValue;
            
            // Temps back to safe range - release thermal protection
            // v2.8.0: Increased hysteresis from 5°C to 10°C and added debounce timer
            var releaseThreshold = _thermalProtectionThreshold - ThermalReleaseHysteresis;
            if (_thermalProtectionActive && maxTemp < releaseThreshold)
            {
                // Start tracking when temp first dropped below release threshold
                if (_thermalBelowReleaseSince == DateTime.MinValue)
                {
                    _thermalBelowReleaseSince = now;
                }
                
                // Check debounce — temp must stay below release threshold for N seconds
                var belowDuration = (now - _thermalBelowReleaseSince).TotalSeconds;
                if (belowDuration < ThermalReleaseDebounceSeconds)
                {
                    // Not yet sustained — keep thermal protection active
                    return;
                }
                _thermalProtectionActive = false;
                _logging.Info($"✓ Temps normalized ({maxTemp:F0}°C) - thermal protection released");
                RecordFanCommand("ThermalProtection.Release", "restoring previous state", true,
                    $"cpu={cpuTemp:F1}C gpu={gpuTemp:F1}C maxTemp={maxTemp:F1}C belowReleaseFor={belowDuration:F0}s");
                
                // BUG FIX v2.3.1: SAFE RELEASE - Don't let BIOS drop fans to 0 RPM at warm temps!
                // If temps are still "gaming warm" (above ThermalSafeReleaseTemp), keep fans
                // spinning at minimum floor to prevent 0 RPM bug on Victus/OMEN laptops.
                bool stillWarm = maxTemp >= ThermalSafeReleaseTemp;
                
                // BUG FIX #32: Restore the ORIGINAL fan state from BEFORE thermal protection
                // Not necessarily _activePreset, which may have been changed during thermal event
                if (_preThermalFanMode == "Max")
                {
                    _logging.Info($"Restoring Max fan mode after thermal protection");
                    ApplyMaxCoolingSerialized();
                    SetFanSpeedSerialized(100);
                    _currentFanMode = "Max";
                    _lastAppliedFanPercent = 100;
                }
                else if (_preThermalPreset != null)
                {
                    _logging.Info($"Restoring preset '{_preThermalPreset.Name}' after thermal protection");
                    ApplyPresetSerialized(_preThermalPreset);
                    _activePreset = _preThermalPreset;
                    
                    // If still warm and restoring to Auto/Default preset, set minimum fan floor
                    if (stillWarm && FanModeNameResolver.IsAutoAlias(_preThermalPreset.Name))
                    {
                        _logging.Info($"Setting minimum {ThermalReleaseMinFanPercent}% fan floor (temps still {maxTemp:F0}°C)");
                        SetFanSpeedSerialized(ThermalReleaseMinFanPercent);
                        _lastAppliedFanPercent = ThermalReleaseMinFanPercent;
                    }
                }
                else if (_preThermalFanPercent > 0)
                {
                    // v2.6.1: Don't restore to low fan speeds if temps are still warm!
                    // This was causing temp yo-yo on high-power laptops (i9/4090)
                    int restorePercent = _preThermalFanPercent;
                    if (stillWarm && restorePercent < ThermalReleaseMinFanPercent)
                    {
                        _logging.Info($"Temps still warm ({maxTemp:F0}°C) - using minimum {ThermalReleaseMinFanPercent}% instead of {restorePercent}%");
                        restorePercent = ThermalReleaseMinFanPercent;
                    }
                    _logging.Info($"Restoring fan speed {restorePercent}% after thermal protection");
                    SetFanSpeedSerialized(restorePercent);
                    _lastAppliedFanPercent = restorePercent;
                }
                else
                {
                    // No pre-thermal state - restore to BIOS auto mode
                    // BUG FIX v2.3.1: If still warm, set minimum fan floor to prevent 0 RPM
                    if (stillWarm)
                    {
                        _logging.Info($"Setting minimum {ThermalReleaseMinFanPercent}% fan floor (temps still {maxTemp:F0}°C)");
                        SetFanSpeedSerialized(ThermalReleaseMinFanPercent);
                        _lastAppliedFanPercent = ThermalReleaseMinFanPercent;
                    }
                    else
                    {
                        // Truly cool (<55°C) - safe to let BIOS control
                        _logging.Info("Restoring fan control to BIOS auto mode (temps low enough)");
                        RestoreAutoControlSerialized();
                    }
                }
                
                // Clear pre-thermal state
                _preThermalFanMode = null;
                _preThermalPreset = null;
                _preThermalFanPercent = 0;
            }
        }
        
        /// <summary>
        /// Apply fan curve based on current temperature if curve is enabled.
        /// This is the core OmenMon-style continuous fan control with hysteresis support.
        /// </summary>
        private bool TryApplyCurveZeroRpmWakeKick(int targetFanPercent, double temperatureC, DateTime now)
        {
            if (!_curveEnabled || _thermalProtectionActive || _diagnosticModeActive || !FanWritesAvailable ||
                temperatureC < CurveZeroRpmWakeKickMinTempC)
            {
                _zeroRpmCurveCommandSince = DateTime.MinValue;
                return false;
            }

            var commandedPercent = Math.Max(targetFanPercent, _lastAppliedFanPercent);
            if (commandedPercent <= 0 || _lastRawPrimaryFanRpm < 0)
            {
                _zeroRpmCurveCommandSince = DateTime.MinValue;
                return false;
            }

            if (_lastRawPrimaryFanRpm > 0)
            {
                if (_zeroRpmCurveCommandSince != DateTime.MinValue)
                {
                    _logging.Info($"Curve zero-RPM watch recovered: primary fan now reads {_lastRawPrimaryFanRpm} RPM after {commandedPercent}% request");
                }

                _zeroRpmCurveCommandSince = DateTime.MinValue;
                return false;
            }

            if (_zeroRpmCurveCommandSince == DateTime.MinValue)
            {
                _zeroRpmCurveCommandSince = now;
                _logging.Warn($"Curve zero-RPM watch: primary fan reports 0 RPM after {commandedPercent}% request (reported duty {_lastReportedPrimaryFanDutyPercent}%)");
                return false;
            }

            var zeroSeconds = (now - _zeroRpmCurveCommandSince).TotalSeconds;
            if (zeroSeconds < CurveZeroRpmWakeKickThresholdSeconds)
            {
                return false;
            }

            if (_lastCurveZeroRpmWakeKick != DateTime.MinValue &&
                (now - _lastCurveZeroRpmWakeKick).TotalSeconds < CurveZeroRpmWakeKickCooldownSeconds)
            {
                return false;
            }

            var wakePercent = Math.Clamp(
                Math.Max(commandedPercent, CurveZeroRpmWakeKickMinPercent),
                CurveZeroRpmWakeKickMinPercent,
                CurveZeroRpmWakeKickMaxPercent);

            _lastCurveZeroRpmWakeKick = now;
            _logging.Warn($"Curve zero-RPM recovery: requested {commandedPercent}% but primary fan stayed at 0 RPM for {(int)zeroSeconds}s; sending one-shot {wakePercent}% wake kick at {temperatureC:F1}C");

            var success = SetFanSpeedSerialized(wakePercent);
            RecordFanCommand(
                "CurveZeroRpmWakeKick",
                $"{wakePercent}%",
                success,
                success
                    ? $"0 RPM after {commandedPercent}% curve request; reported duty {_lastReportedPrimaryFanDutyPercent}%"
                    : "Controller returned false");

            if (success)
            {
                _lastAppliedFanPercent = wakePercent;
                _lastAppliedCpuFanPercent = Math.Max(_lastAppliedCpuFanPercent, wakePercent);
                _lastAppliedGpuFanPercent = Math.Max(_lastAppliedGpuFanPercent, wakePercent);
                _lastHysteresisTemp = temperatureC;
                _pendingFanPercent = -1;
                _lastCurveUpdate = now;
                _lastCurveForceRefresh = now;
                MarkCurveWrite(now);
            }

            return success;
        }

        private Task ApplyCurveIfNeededAsync(double cpuTemp, double gpuTemp, bool immediate = false, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // Skip curve application if thermal protection is active
            if (_thermalProtectionActive)
                return Task.CompletedTask;
                
            // Skip curve application if diagnostic mode is active (manual testing)
            if (_diagnosticModeActive)
                return Task.CompletedTask;
            
            // Check if curves are available
            bool hasSingleCurve = _activeCurve != null;
            bool hasIndependentCurves = _independentCurvesEnabled && _cpuCurve != null && _gpuCurve != null;
            
            if (!_curveEnabled || (!hasSingleCurve && !hasIndependentCurves) || !FanWritesAvailable)
                return Task.CompletedTask;

            // Only update curve every CurveUpdateIntervalMs
            var now = DateTime.Now;
            var timeSinceLastUpdate = (now - _lastCurveUpdate).TotalMilliseconds;
            var timeSinceForceRefresh = (now - _lastCurveForceRefresh).TotalMilliseconds;
            var forceRefreshIntervalMs = ConservativeLegacyFanPolicy ? ConservativeCurveForceRefreshMs : CurveForceRefreshMs;
            
            // Check if we need to force a refresh (re-apply even if unchanged)
            // This combats BIOS countdown timer that may reset fan control
            bool forceRefresh = timeSinceForceRefresh >= forceRefreshIntervalMs;
            
            if (timeSinceLastUpdate < CurveUpdateIntervalMs && !forceRefresh && !immediate)
                return Task.CompletedTask;
                
            // Route to appropriate curve handler
            if (hasIndependentCurves)
            {
                return ApplyIndependentCurvesAsync(cpuTemp, gpuTemp, immediate, forceRefresh, now);
            }
            
            lock (_curveLock)
            {
                if (_activeCurve == null)
                {
                    return Task.CompletedTask;
                }

                try
                {
                    if (cpuTemp <= 0 && gpuTemp <= 0)
                    {
                        TryReuseLatestThermalSample(ref cpuTemp, ref gpuTemp);
                    }
                    var (controlCpuTemp, controlGpuTemp) = SmoothCurveTemperatures(cpuTemp, gpuTemp);
                    // Use smoothed control temps for curve interpolation, but keep raw
                    // temperatures for safety clamps and hysteresis state.
                    var maxTemp = Math.Max(controlCpuTemp, controlGpuTemp);
                    var rawMaxTemp = Math.Max(cpuTemp, gpuTemp);
                    
                    // Calculate fan speed using slope-based interpolation (omen-fan style)
                    // This provides smoother transitions between curve points
                    double targetFanPercent = InterpolateFanSpeed(_activeCurve, maxTemp);
                    
                    // Adjust fan speed based on GPU power boost level
                    // Higher power boost levels generate more heat, so slightly increase fan speed
                    targetFanPercent = AdjustFanPercentForGpuPowerBoost((int)targetFanPercent, gpuTemp);
                    
                    // Apply safety bounds clamping based on temperature
                    targetFanPercent = ApplySafetyBoundsClamping(targetFanPercent, rawMaxTemp);

                    // If immediate flag passed, bypass hysteresis and smoothing and apply now
                    if (immediate)
                    {
                        if (SetFanSpeedSerialized((int)targetFanPercent))
                        {
                            _lastAppliedFanPercent = (int)targetFanPercent;
                            _lastHysteresisTemp = rawMaxTemp;
                            _pendingFanPercent = -1;
                            _lastCurveUpdate = now;
                            MarkCurveWrite(now);
                            _logging.Info($"Immediate curve applied: {targetFanPercent}% @ {rawMaxTemp:F1}C (control {maxTemp:F1}C, GPU boost: {_gpuPowerBoostLevel})");
                        }

                        return Task.CompletedTask;
                    }

                    if (TryApplyCurveZeroRpmWakeKick((int)Math.Round(targetFanPercent), rawMaxTemp, now))
                    {
                        return Task.CompletedTask;
                    }

                    var roundedTargetFanPercent = (int)Math.Round(targetFanPercent);
                    if (ShouldSkipConservativeCurveWrite(roundedTargetFanPercent, now, forceRefresh, immediate))
                    {
                        _lastCurveUpdate = now;
                        return Task.CompletedTask;
                    }

                    // Apply hysteresis if enabled
                    if (_hysteresis.Enabled && _lastAppliedFanPercent >= 0)
                    {
                        var tempDelta = Math.Abs(rawMaxTemp - _lastHysteresisTemp);
                        
                        // Check if temperature change is within dead-zone
                        if (tempDelta < _hysteresis.DeadZone && targetFanPercent != _lastAppliedFanPercent)
                        {
                            // Within dead-zone, don't change fan speed
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                        
                        // Apply ramp delay for speed changes
                        bool isIncrease = targetFanPercent > _lastAppliedFanPercent;
                        double requiredDelay = isIncrease ? _hysteresis.RampUpDelay : _hysteresis.RampDownDelay;
                        
                        if (_pendingFanPercent != (int)targetFanPercent)
                        {
                            // New target, start delay timer
                            _pendingFanPercent = (int)targetFanPercent;
                            _pendingIncrease = isIncrease;
                            _lastFanChangeRequest = now;
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                        
                        // Check if delay has elapsed
                        var timeSinceRequest = (now - _lastFanChangeRequest).TotalSeconds;
                        if (timeSinceRequest < requiredDelay)
                        {
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                    }                    
                    // Apply if fan percent changed OR if we're forcing a refresh
                    // Force refresh combats BIOS countdown timer that may have reset fan control
                    if (targetFanPercent != _lastAppliedFanPercent || forceRefresh)
                    {
                        bool allowSmoothing = _smoothingEnabled && !IsEcBackend;

                        // If smoothing disabled or this is a force refresh or we have no previous applied value, just set directly
                        if (!allowSmoothing || _lastAppliedFanPercent < 0 || forceRefresh)
                        {
                            // Single attempt — no retries to reduce EC load
                            // WriteDuty already has deduplication; retrying just adds more EC writes
                            bool success = SetFanSpeedSerialized((int)targetFanPercent);
                            
                            if (success)
                            {
                                _lastAppliedFanPercent = (int)targetFanPercent;
                                _lastHysteresisTemp = rawMaxTemp;
                                _pendingFanPercent = -1;
                                MarkCurveWrite(now);
                                
                                if (forceRefresh)
                                {
                                    _lastCurveForceRefresh = now;
                                    _logging.Info($"Curve force-refreshed: {targetFanPercent}% @ {rawMaxTemp:F1}C (control {maxTemp:F1}C)");
                                }
                                else
                                {
                                    _logging.Info($"Curve applied: {targetFanPercent}% @ {rawMaxTemp:F1}C (control {maxTemp:F1}C)");
                                }
                            }
                            else
                            {
                                _logging.Warn($"Failed to set fan speed to {targetFanPercent}%");
                            }
                        }
                        else
                        {
                            // Ramp to the new target asynchronously so we don't block the monitor loop
                            var cancellationToken = CancellationToken.None;
                            _ = RampFanToPercentAsync((int)targetFanPercent, cancellationToken);
                            MarkCurveWrite(now);
                            _lastHysteresisTemp = rawMaxTemp;
                        }
                    }
                    
                    _lastCurveUpdate = now;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to apply fan curve: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Apply independent CPU and GPU fan curves based on their respective temperatures.
        /// Uses slope-based interpolation for smooth fan speed transitions.
        /// </summary>
        private Task ApplyIndependentCurvesAsync(double cpuTemp, double gpuTemp, bool immediate, bool forceRefresh, DateTime now)
        {
            lock (_curveLock)
            {
                if (_cpuCurve == null || _gpuCurve == null)
                    return Task.CompletedTask;
                    
                try
                {
                    var rawCpuTemp = cpuTemp;
                    var rawGpuTemp = gpuTemp;
                    (cpuTemp, gpuTemp) = SmoothCurveTemperatures(cpuTemp, gpuTemp);

                    // Evaluate CPU curve using slope-based interpolation
                    int cpuFanPercent = (int)Math.Round(InterpolateFanSpeed(_cpuCurve, cpuTemp));
                    
                    // Evaluate GPU curve using slope-based interpolation
                    int gpuFanPercent = (int)Math.Round(InterpolateFanSpeed(_gpuCurve, gpuTemp));
                    
                    // Apply safety bounds clamping to both CPU and GPU fan speeds
                    cpuFanPercent = (int)Math.Round(ApplySafetyBoundsClamping(cpuFanPercent, rawCpuTemp));
                    gpuFanPercent = (int)Math.Round(ApplySafetyBoundsClamping(gpuFanPercent, rawGpuTemp));

                    if (TryApplyCurveZeroRpmWakeKick(Math.Max(cpuFanPercent, gpuFanPercent), Math.Max(rawCpuTemp, rawGpuTemp), now))
                    {
                        return Task.CompletedTask;
                    }

                    // Check if either fan needs updating
                    bool cpuChanged = cpuFanPercent != _lastAppliedCpuFanPercent;
                    bool gpuChanged = gpuFanPercent != _lastAppliedGpuFanPercent;
                    
                    if (!cpuChanged && !gpuChanged && !forceRefresh && !immediate)
                    {
                        _lastCurveUpdate = now;
                        return Task.CompletedTask;
                    }
                    
                    // Apply using the dual fan speed method
                    if (_fanController is WmiFanController)
                    {
                        if (SetFanSpeedsSerialized(cpuFanPercent, gpuFanPercent))
                        {
                            _lastAppliedCpuFanPercent = cpuFanPercent;
                            _lastAppliedGpuFanPercent = gpuFanPercent;
                            _lastCurveUpdate = now;
                            MarkCurveWrite(now);
                            
                            if (forceRefresh)
                            {
                                _lastCurveForceRefresh = now;
                                _logging.Info($"Independent curves force-refreshed - CPU: {cpuFanPercent}% @ {cpuTemp:F1}°C, GPU: {gpuFanPercent}% @ {gpuTemp:F1}°C");
                            }
                            else
                            {
                                _logging.Info($"Independent curves applied - CPU: {cpuFanPercent}% @ {cpuTemp:F1}°C, GPU: {gpuFanPercent}% @ {gpuTemp:F1}°C");
                            }
                        }
                    }
                    else
                    {
                        // Fallback for non-WMI controllers: use max of both targets
                        int maxPercent = Math.Max(cpuFanPercent, gpuFanPercent);
                        if (SetFanSpeedSerialized(maxPercent))
                        {
                            _lastAppliedFanPercent = maxPercent;
                            _lastAppliedCpuFanPercent = cpuFanPercent;
                            _lastAppliedGpuFanPercent = gpuFanPercent;
                            _lastCurveUpdate = now;
                            _logging.Info($"Independent curves (fallback mode): {maxPercent}% - CPU target: {cpuFanPercent}%, GPU target: {gpuFanPercent}%");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to apply independent fan curves: {ex.Message}");
                }
            }
            
            return Task.CompletedTask;
        }

        private async Task RampFanToPercentAsync(int targetPercent, CancellationToken cancellationToken)
        {
            if (IsEcBackend)
            {
                SetFanSpeedSerialized(targetPercent);
                _lastAppliedFanPercent = targetPercent;
                return;
            }

            // Determine start point for ramp. If no previous value, start from 0% to provide a ramp-up
            int from = _lastAppliedFanPercent < 0 ? 0 : _lastAppliedFanPercent;

            if (targetPercent == from)
                return;

            int to = targetPercent;
            int diff = to - from;
            int steps = Math.Max(1, _smoothingDurationMs / Math.Max(1, _smoothingStepMs));
            double stepSize = diff / (double)steps;

            for (int i = 1; i <= steps; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                int interim = (int)Math.Round(from + stepSize * i);
                interim = Math.Clamp(interim, 0, 100);
                try
                {
                    SetFanSpeedSerialized(interim);
                    _lastAppliedFanPercent = interim;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Error during fan ramp step: {ex.Message}");
                }

                try { await Task.Delay(_smoothingStepMs, cancellationToken); } catch { break; }
            }

            // Ensure final target applied
            if (_lastAppliedFanPercent != targetPercent)
            {
                SetFanSpeedSerialized(targetPercent);
                _lastAppliedFanPercent = targetPercent;
            }
        }

        #region Quick Profile Methods (for GeneralView)

        /// <summary>
        /// Apply max cooling mode (100% fans).
        /// </summary>
        public void ApplyMaxCooling(bool forceApply = false)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Max cooling skipped; fan control unavailable");
                RecordFanCommand("ApplyMaxCooling", "Max", false, $"Fan control unavailable: {_fanController.Status}");
                return;
            }

            // OmenMon-Reborn parity: reduce unnecessary hardware writes by checking current state first.
            if (!forceApply && string.Equals(_currentFanMode, "Max", StringComparison.OrdinalIgnoreCase) && !_curveEnabled)
            {
                RecordFanCommand("ApplyMaxCooling", "Max", true, "Already in Max mode - write skipped");
                ScheduleDeferredMaxVerification();
                PublishPresetApplied(_currentFanMode);
                return;
            }
            
            DisableCurve();
            ApplyMaxCoolingSerialized();
            RecordFanCommand("ApplyMaxCooling.Controller", "Max", true, "ApplyMaxCooling sent");

            // WMI max mode is maintained by controller-level keepalive logic. Re-sending an
            // immediate SetFanSpeed(100) here can create an unnecessary re-apply pulse on
            // some firmware. Keep the defensive write for non-WMI backends.
            if (!Backend.Contains("WMI", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (SetFanSpeedSerialized(100))
                    {
                        _lastAppliedFanPercent = 100;
                        RecordFanCommand("SetFanSpeed", "100%", true, "Defensive non-WMI Max write");
                    }
                }
                catch (Exception ex)
                {
                    RecordFanCommand("SetFanSpeed", "100%", false, ex.Message);
                    _logging.Warn($"SetFanSpeed(100) during ApplyMaxCooling threw: {ex.Message}");
                }
            }
            else
            {
                _lastAppliedFanPercent = 100;
            }

            _currentFanMode = "Max";
            RecordFanCommand("ApplyMaxCooling", "Max", true, "Max cooling mode active");
            ScheduleDeferredMaxVerification();
            _logging.Info("Max cooling mode applied");
            PublishPresetApplied(_currentFanMode);
        }

        private void ScheduleDeferredMaxVerification()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDeferredMaxVerifyAt).TotalMilliseconds < DeferredMaxVerifyMinIntervalMs)
            {
                return;
            }

            _lastDeferredMaxVerifyAt = now;
            var cts = _cts;
            var token = cts?.Token ?? CancellationToken.None;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Allow BIOS/WMI handoff and fan spin-up to settle before checking.
                    await Task.Delay(FanTransitionHoldMs + 1500, token);
                    var (ok, details) = VerifyMaxApplied();
                    if (!ok)
                    {
                        _logging.Warn($"Deferred Max verification did not confirm Max state: {details}");
                        AttemptMaxPersistenceRecovery(details);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Service stopped/disposed.
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Deferred Max verification failed: {ex.Message}");
                }
            }, token);
        }

        private void AttemptMaxPersistenceRecovery(string verifyDetails)
        {
            // Recovery is only valid if Max is still the requested state and curve mode is off.
            if (!string.Equals(_currentFanMode, "Max", StringComparison.OrdinalIgnoreCase) || _curveEnabled)
            {
                return;
            }

            if (!FanWritesAvailable)
            {
                RecordFanCommand("ApplyMaxCooling.Recover", "Max", false, $"Recovery skipped; fan control unavailable. Verify details: {verifyDetails}");
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastMaxPersistenceRecoveryAt).TotalMilliseconds < MaxPersistenceRecoveryCooldownMs)
            {
                return;
            }

            _lastMaxPersistenceRecoveryAt = now;
            _logging.Warn($"Max persistence recovery: deferred verify failed ({verifyDetails}); forcing one recovery apply.");

            try
            {
                ApplyMaxCooling(forceApply: true);
                RecordFanCommand("ApplyMaxCooling.Recover", "Max", true, "Forced recovery apply triggered after failed deferred verification");
            }
            catch (Exception ex)
            {
                RecordFanCommand("ApplyMaxCooling.Recover", "Max", false, ex.Message);
                _logging.Warn($"Max persistence recovery failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply auto fan mode (BIOS control).
        /// </summary>
        public void ApplyAutoMode()
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Auto mode skipped; fan control unavailable");
                RecordFanCommand("ApplyAutoMode", "Auto", false, $"Fan control unavailable: {_fanController.Status}");
                return;
            }
            
            DisableCurve();
            ApplyAutoModeSerialized();
            _currentFanMode = "Auto";
            RecordFanCommand("ApplyAutoMode", "Auto", true, "Auto fan mode applied");
            _logging.Info("Auto fan mode applied (BIOS control)");
            PublishPresetApplied(_currentFanMode);
        }

        /// <summary>
        /// Apply quiet fan mode (low speeds).
        /// </summary>
        public void ApplyQuietMode()
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Quiet mode skipped; fan control unavailable");
                RecordFanCommand("ApplyQuietMode", "Quiet", false, $"Fan control unavailable: {_fanController.Status}");
                return;
            }
            
            DisableCurve();
            ApplyQuietModeSerialized();
            _currentFanMode = "Quiet";
            RecordFanCommand("ApplyQuietMode", "Quiet", true, "Quiet fan mode applied");
            _logging.Info("Quiet fan mode applied");
            PublishPresetApplied(_currentFanMode);
        }

        private string _currentFanMode = "Auto";

        private void PublishPresetApplied(string presetName)
        {
            var handlers = PresetApplied;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, presetName);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Fan preset subscriber failed for '{presetName}': {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Get the current fan mode name.
        /// </summary>
        public string? GetCurrentFanMode() => _currentFanMode;
        
        /// <summary>
        /// Reset EC (Embedded Controller) to factory defaults.
        /// Restores BIOS control of fans and clears all manual overrides.
        /// Use this to fix stuck fan readings, incorrect BIOS display values, and other EC-related issues.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            _logging.Info("═══════════════════════════════════════════════════");
            _logging.Info("FanService: Initiating EC Reset to Defaults...");
            _logging.Info("═══════════════════════════════════════════════════");

            if (DesktopFanWritesBlocked)
            {
                RecordFanCommand("ResetEcToDefaults", "EC factory defaults", false, DesktopFanWriteBlockedMessage);
                _logging.Warn(DesktopFanWriteBlockedMessage);
                return false;
            }
            
            // First, disable any active fan curve
            DisableCurve();
            
            // Clear our internal state
            _currentFanMode = "Auto";
            _lastAppliedFanPercent = 0;
            
            // Delegate to the fan controller
            var result = _fanController.ResetEcToDefaults();
            RecordFanCommand("ResetEcToDefaults", "EC factory defaults", result, result ? "Controller returned success" : "Controller returned false");
            
            if (result)
            {
                _logging.Info("✓ EC Reset completed successfully via FanService");
            }
            else
            {
                _logging.Warn("EC Reset returned false - may have partially succeeded");
            }
            
            return result;
        }

        private void TryReuseLatestThermalSample(ref double cpuTemp, ref double gpuTemp)
        {
            try
            {
                if (_thermalSamples.Count == 0)
                {
                    return;
                }

                var latest = _thermalSamples[^1];
                if (cpuTemp <= 0 && latest.CpuCelsius > 0)
                {
                    cpuTemp = latest.CpuCelsius;
                }

                if (gpuTemp <= 0 && latest.GpuCelsius > 0)
                {
                    gpuTemp = latest.GpuCelsius;
                }
            }
            catch
            {
                // Best-effort fallback only.
            }
        }

        /// <summary>
        /// Adjust fan percentage based on GPU power boost level to account for increased heat generation.
        /// Higher power boost levels require slightly higher fan speeds for optimal cooling.
        /// </summary>
        private int AdjustFanPercentForGpuPowerBoost(int baseFanPercent, double gpuTemp)
        {
            if (gpuTemp < 50) // Only adjust when GPU is under load
                return baseFanPercent;

            int adjustment = _gpuPowerBoostLevel switch
            {
                "Minimum" => 0,    // Base TGP - no adjustment needed
                "Medium" => 2,     // Custom TGP - slight increase
                "Maximum" => 5,    // Custom TGP + Dynamic Boost - moderate increase
                "Extended" => 8,   // Extended boost - significant increase for higher wattage
                _ => 0
            };

            // Scale adjustment based on GPU temperature - more adjustment at higher temps
            double tempFactor = Math.Min(1.0, (gpuTemp - 50) / 30); // 0-1 scale over 50-80°C
            int scaledAdjustment = (int)(adjustment * tempFactor);

            int adjustedPercent = Math.Min(100, baseFanPercent + scaledAdjustment);

            if (scaledAdjustment > 0)
            {
                _logging.Debug($"GPU power boost adjustment: {baseFanPercent}% + {scaledAdjustment}% = {adjustedPercent}% (boost: {_gpuPowerBoostLevel}, GPU: {gpuTemp:F1}°C)");
            }

            return adjustedPercent;
        }

        #endregion

        /// <summary>
        /// Force-set fan speed directly on controller (used for restoration/diagnostics).
        /// </summary>
        public void ForceSetFanSpeed(int percent)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("ForceSetFanSpeed skipped; fan control unavailable");
                RecordFanCommand("ForceSetFanSpeed", $"{percent}%", false, $"Fan control unavailable: {_fanController.Status}");
                return;
            }

            if (!ManualFanControlAvailable)
            {
                _logging.Warn("ForceSetFanSpeed skipped; manual fan levels are disabled for this model.");
                RecordFanCommand("ForceSetFanSpeed", $"{percent}%", false, "Manual fan levels disabled by model capability database");
                return;
            }

            try
            {
                var success = SetFanSpeedSerialized(percent);
                if (success)
                {
                    _lastAppliedFanPercent = percent;
                }
                RecordFanCommand("ForceSetFanSpeed", $"{percent}%", success, success ? "Controller returned success" : "Controller returned false");
            }
            catch (Exception ex)
            {
                RecordFanCommand("ForceSetFanSpeed", $"{percent}%", false, ex.Message);
                _logging.Warn($"ForceSetFanSpeed({percent}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Force-set individual fan speeds directly on controller (used for diagnostics).
        /// </summary>
        public bool ForceSetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("ForceSetFanSpeeds skipped; fan control unavailable");
                RecordFanCommand("ForceSetFanSpeeds", $"CPU {cpuPercent}% / GPU {gpuPercent}%", false, $"Fan control unavailable: {_fanController.Status}");
                return false;
            }

            if (!ManualFanControlAvailable)
            {
                _logging.Warn("ForceSetFanSpeeds skipped; manual fan levels are disabled for this model.");
                RecordFanCommand("ForceSetFanSpeeds", $"CPU {cpuPercent}% / GPU {gpuPercent}%", false, "Manual fan levels disabled by model capability database");
                return false;
            }

            try
            {
                var success = SetFanSpeedsSerialized(cpuPercent, gpuPercent);
                RecordFanCommand("ForceSetFanSpeeds", $"CPU {cpuPercent}% / GPU {gpuPercent}%", success, success ? "Controller returned success" : "Controller returned false");
                return success;
            }
            catch (Exception ex)
            {
                RecordFanCommand("ForceSetFanSpeeds", $"CPU {cpuPercent}% / GPU {gpuPercent}%", false, ex.Message);
                _logging.Warn($"ForceSetFanSpeeds({cpuPercent}, {gpuPercent}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test helper: try to read a LastSetPercent-like property from the underlying fan controller if present.
        /// Returns null if the property does not exist.
        /// </summary>
        public int? GetControllerReportedSetPercent()
        {
            try
            {
                var prop = _fanController.GetType().GetProperty("LastSetPercent");
                if (prop != null && prop.PropertyType == typeof(int))
                {
                    return (int?)prop.GetValue(_fanController);
                }
            }
            catch (Exception ex)
            {
                _logging.Debug($"Controller-reported fan setpoint unavailable: {ex.Message}");
            }
            return null;
        }

        public void Dispose()
        {
            // Restore system auto-control before shutting down
            // This returns fans to BIOS/Windows default control instead of staying at last manual setting
            try
            {
                if (FanWritesAvailable)
                {
                    // Use full EC reset which is more thorough than RestoreAutoControl
                    // This resets fan state, timer, and BIOS control registers
                    _logging.Info("Resetting EC to restore BIOS fan control on shutdown...");
                    
                    // First, try RestoreAutoControl for immediate effect
                    RestoreAutoControlSerialized();
                    
                    // Wait briefly for EC to process
                    System.Threading.Thread.Sleep(100);
                    
                    // Then do full reset to ensure BIOS takes over
                    if (_fanController.ResetEcToDefaults())
                    {
                        _logging.Info("FanService disposed (EC reset complete, BIOS should now control fans)");
                    }
                    else
                    {
                        _logging.Warn("FanService disposed (EC reset returned false, fans may not restore properly)");
                    }
                }
                else
                {
                    _logging.Info(DesktopFanWritesBlocked
                        ? "FanService disposed (desktop fan writes blocked, no auto-control restoration)"
                        : "FanService disposed (fan controller not available, no auto-control restoration)");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore auto-control on dispose: {ex.Message}");
            }
            
            DisableCurve();
            Stop();
        }
    }

    public sealed class FanCommandHistoryEntry
    {
        public DateTime TimestampUtc { get; init; }
        public string Command { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string Details { get; init; } = string.Empty;
        public string Backend { get; init; } = string.Empty;
        public string FanMode { get; init; } = string.Empty;
        public string? ActivePresetName { get; init; }
        public bool CurveActive { get; init; }
        public bool HoldActive { get; init; }
        public bool CurveOrHoldActive { get; init; }
        public bool DiagnosticModeActive { get; init; }
        public bool ThermalProtectionActive { get; init; }
        public string ProductId { get; init; } = "unknown";
        public string ModelName { get; init; } = "unknown";
        public bool FanWritesAvailable { get; init; }
        public bool FanCurvesAvailable { get; init; }
        public bool ManualFanControlAvailable { get; init; }
        public bool DesktopFanWritesBlocked { get; init; }
        public string TelemetrySummary { get; init; } = string.Empty;
        public int? RawPrimaryFanRpm { get; init; }
        public int? ReportedPrimaryFanDutyPercent { get; init; }
    }

    /// <summary>
    /// Event arguments for RPM sanity check warnings.
    /// Indicates that RPM reading is stuck at 0 while fans are commanded to run.
    /// </summary>
    public class RpmSanityCheckEventArgs : EventArgs
    {
        public int DutyPercent { get; set; }
        public int RpmReading { get; set; }
        public TimeSpan DurationAtZero { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
