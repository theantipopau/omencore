using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Hardware
{
    /// <summary>
    /// WMI-based fan controller for HP OMEN/Victus laptops.
    /// Uses HP WMI BIOS interface instead of direct EC access.
    /// This avoids direct EC driver access for normal fan control.
    /// 
    /// Features automatic countdown extension to prevent BIOS from reverting
    /// fan settings after 120 seconds (HP BIOS limitation).
    /// 
    /// Note: Some newer models (OMEN Transcend, 2024+ models) may return success
    /// from WMI commands but not actually change fan behavior. The verification
    /// system can detect this and report if a different backend should be used.
    /// </summary>
    public class WmiFanController : IDisposable
    {
        private readonly IHpWmiBios _wmiBios;
        private readonly IEcAccess? _ecAccess;
        private readonly LibreHardwareMonitorImpl? _hwMonitor;
        private readonly LoggingService? _logging;
        private readonly bool _strictFanModeReadback;
        private readonly bool _allowV1AutoModeFloorClear;
        private readonly int _maxModeDropChecksBeforeReapply;
        private bool _disposed;

        // Manual fan control state
        private HpWmiBios.FanMode _lastMode = HpWmiBios.FanMode.Default;
        private bool _isMaxModeActive = false;  // Track if SetFanMax(true) is active
        private int _lastManualFanPercent = -1; // Track last manual fan percentage for re-apply
        
        // Fan level constants - HP WMI uses 0-55 krpm range (0-5500 RPM) on classic models,
        // or 0-100 percentage on newer models. Auto-detected from WMI at startup.
        private readonly int _maxFanLevel;
        
        // Maximum ceiling for "Max" mode operations. When the user requests maximum cooling,
        // we send a high value and let the BIOS clamp to its hardware maximum.
        // This ensures models with max levels > 55 (e.g., OMEN 16-xd0xxx with 6300 RPM = level 63)
        // can actually reach their full speed, rather than being capped at _maxFanLevel (55).
        private const int MaxFanLevelCeiling = 100;
        
        // Countdown extension timer - keeps fan settings from reverting
        // HP BIOS aggressively reverts fan settings, especially on OMEN 16/Max/xd0xxx models with fast reversion
        private Timer? _countdownExtensionTimer;
        private readonly object _countdownTimerLock = new();
        private int _countdownCallbackActive;
        private const int CountdownExtensionIntervalMs = 5000;
        private const int CountdownExtensionInitialDelayMs = 3000;
        private bool _countdownExtensionEnabled = false;

        // Max-mode maintenance state.
        // In max mode we avoid blindly re-issuing SetFanMax(true) every timer tick because
        // some firmware shows a visible RPM dip on each re-apply (sawtooth behavior).
        // Instead, keep max alive with lightweight countdown extension and only re-assert
        // max when readback telemetry indicates a sustained drop.
        private DateTime _lastMaxModeMaintenanceUtc = DateTime.MinValue;
        private int _maxModeLowTelemetryStreak = 0;
        private int _maxModeTelemetryUnavailableStreak = 0;
        private DateTime _lastManualModeReapplyUtc = DateTime.MinValue;
        private DateTime _lastPresetModeReapplyUtc = DateTime.MinValue;
        private DateTime _lastCountdownExtendUtc = DateTime.MinValue;
        private int _keepaliveWriteCount = 0;
        private const int KeepaliveLogSummaryInterval = 50; // log Info summary every 50 successful keepalive writes
        private DateTime? _lastMaxModeExternalResetUtc;
        private string? _lastMaxModeExternalResetDetails;
        private const int MaxModeMaintenanceIntervalMs = 8000;
        private const int DefaultMaxModeDropChecksBeforeReapply = 2;
        private const int MaxModeTelemetryUnavailableReassertCycles = 3;
        private const int ManualModeReapplyIntervalMs = 15000;
        private const int HighDutyManualModeReapplyIntervalMs = 5000;
        private const int HighDutyManualModeThresholdPercent = 70;
        private const int PresetModeReapplyIntervalMs = 30000;
        private const int CountdownExtendMinIntervalMs = 15000;
        private const int MaxModeHealthyRpmFloor = 2000;
        // Max mode should stay near board ceiling while the hold is active.
        // A lax floor lets "stuck-at-mid" states (for example level 55 on a level-63 board)
        // look healthy, so we keep this threshold intentionally high.
        private const double MaxModeHealthyLevelRatio = 0.90;

        // Self-calibrating fallback for boards whose real fan-level ceiling sits *below*
        // MaxFanLevel * MaxModeHealthyLevelRatio, which makes the nominal check above
        // unwinnable and produces an endless "external reset suspected" reassert loop.
        //
        // MaxFanLevel is a firmware-declared nominal value and is already known to be an
        // unreliable proxy for the real hardware ceiling in *both* directions: OMEN 16-xd0xxx
        // holds level 63 against a nominal 55 (see the SetFanLevel ceiling comment in
        // SetMaxFanSpeed), while OMEN 17-ck1xxx (8A18, nominal 55) tops out at a steady 46-48 —
        // permanently below the derived floor of 50. On that board every maintenance cycle
        // read "dropped" and re-asserted max, fighting firmware that was already holding max.
        //
        // Only engage this fallback once the nominal floor has proven unreachable across several
        // consecutive observations. Boards that *do* reach the nominal floor keep the strict
        // check unchanged, so the stuck-at-mid protection above is preserved wherever it is
        // actually testable.
        private const double MaxModeObservedPeakTolerance = 0.94;
        private const int MaxModeObservedPeakMinObservations = 3;
        private const double MaxModeAbsoluteLevelFloorRatio = 0.50;
        private int _maxModeObservedPeakLevel;
        private int _maxModeLevelObservationCount;
        private bool _maxModeNominalFloorEverMet;
        private const ushort EcFanModeRegister = 0x95; // OmenMon HPCM register
        private const int FanModeReadbackAttempts = 3;
        private const int FanModeReadbackDelayMs = 40;
        
        // RPM debounce tracking — filters transient phantom readings during profile transitions.
        // When fans are transitioning (e.g., profile switch), BIOS may return stale/target fan levels
        // that get misinterpreted as actual RPM. Track previous readings and filter sudden spikes.
        private int _lastCpuRpm = 0;
        private int _lastGpuRpm = 0;
        private DateTime _lastProfileSwitch = DateTime.MinValue;
        private DateTime _lastAutoResetUtc = DateTime.MinValue;
        private const int ProfileTransitionDebounceMs = 3000; // Ignore phantom RPM for 3s after profile switch
        private const int AutoResetCooldownSeconds = 5;
        
        // Command verification tracking
        private int _commandVerifyFailCount = 0;
        private int? _lastCommandRpmBefore = null;
        private const int VerifyDelayMs = 3000; // Wait 3 seconds for fans to respond
        private const int VerifyThreshold = 3; // After 3 verified failures, mark as ineffective

        // Command history for diagnostics
        private readonly List<WmiCommandHistoryEntry> _commandHistory = new();
        private const int MaxCommandHistoryEntries = 50; // Keep last 50 commands

        public bool IsAvailable => _wmiBios.IsAvailable;
        public string Status => _wmiBios.Status;
        public int FanCount => _wmiBios.FanCount;
        
        /// <summary>
        /// Indicates if manual fan control is currently active (vs automatic BIOS control).
        /// </summary>
        public bool IsManualControlActive { get; private set; }

        /// <summary>
        /// Last time Max mode appeared to be externally reset while OmenCore still owned the hold.
        /// </summary>
        public DateTime? LastMaxModeExternalResetUtc => _lastMaxModeExternalResetUtc;

        /// <summary>
        /// Human-readable details for the last suspected external Max-mode reset.
        /// </summary>
        public string LastMaxModeExternalResetDetails => _lastMaxModeExternalResetDetails ?? "No external Max-mode reset detected.";
        
        /// <summary>
        /// Indicates if countdown extension is enabled to prevent fan mode reverting.
        /// </summary>
        public bool CountdownExtensionEnabled => _countdownExtensionEnabled;
        
        /// <summary>
        /// Indicates if WMI commands appear to be ineffective (return success but no change).
        /// On some newer OMEN models (Transcend, 2024+), WMI may report success but
        /// not actually change fan speed. In this case, OGH proxy should be used.
        /// </summary>
        public bool CommandsIneffective => _commandVerifyFailCount >= VerifyThreshold;
        
        /// <summary>
        /// Number of times commands returned success but verification showed no change.
        /// </summary>
        public int VerifyFailCount => _commandVerifyFailCount;

        public static byte MapFanPercentToWmiLevel(int percent, int maxFanLevel)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (percent >= 100)
            {
                return MaxFanLevelCeiling;
            }

            return (byte)(percent * Math.Clamp(maxFanLevel, 1, MaxFanLevelCeiling) / 100);
        }

        public WmiFanController(
            LibreHardwareMonitorImpl? hwMonitor,
            LoggingService? logging = null,
            int maxFanLevelOverride = 0,
            int? modelMaxFanLevel = null,
            IHpWmiBios? injectedWmiBios = null,
            IEcAccess? ecAccess = null,
            bool strictFanModeReadback = true,
            bool allowV1AutoModeFloorClear = true,
            int? maxModeDropChecksBeforeReapply = null)
        {
            _hwMonitor = hwMonitor;
            _logging = logging;
            _wmiBios = injectedWmiBios ?? new HpWmiBios(logging);
            _ecAccess = ecAccess;
            _strictFanModeReadback = strictFanModeReadback;
            _allowV1AutoModeFloorClear = allowV1AutoModeFloorClear;
            _maxModeDropChecksBeforeReapply = Math.Clamp(
                maxModeDropChecksBeforeReapply ?? DefaultMaxModeDropChecksBeforeReapply,
                1,
                DefaultMaxModeDropChecksBeforeReapply);
            
            // Apply user/model override if set, then read the (possibly overridden) max level
            if ((maxFanLevelOverride > 0 || modelMaxFanLevel.HasValue) && _wmiBios is HpWmiBios concrete)
            {
                // Only call DetectMaxFanLevel on the real implementation (not on fakes)
                concrete.DetectMaxFanLevel(maxFanLevelOverride, modelMaxFanLevel);
            }
            _maxFanLevel = _wmiBios.MaxFanLevel;
            var maxFanLevelSource = maxFanLevelOverride > 0
                ? $"user override: {maxFanLevelOverride}"
                : modelMaxFanLevel.HasValue
                    ? $"model database: {modelMaxFanLevel.Value}"
                    : "auto-detected";
            _logging?.Info($"WmiFanController: Max fan level = {_maxFanLevel} ({maxFanLevelSource})");
            _logging?.Info($"WmiFanController: Capabilities — MaxModeDropChecksBeforeReapply={_maxModeDropChecksBeforeReapply}, AllowV1AutoModeFloorClear={_allowV1AutoModeFloorClear}, StrictFanModeReadback={_strictFanModeReadback}");
        }
        
        // Helper methods for getting sensor data with WMI BIOS fallback
        private double GetCpuTemperature()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var temp = _hwMonitor.GetCpuTemperature();
                    if (temp > 0) return temp;
                }
                catch (Exception ex)
                {
                    _logging?.Debug($"WmiFanController: CPU temperature monitor read failed: {ex.Message}");
                }
            }
            return _wmiBios.GetTemperature() ?? 0;
        }
        
        private double GetGpuTemperature()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var temp = _hwMonitor.GetGpuTemperature();
                    if (temp > 0) return temp;
                }
                catch (Exception ex)
                {
                    _logging?.Debug($"WmiFanController: GPU temperature monitor read failed: {ex.Message}");
                }
            }
            return _wmiBios.GetGpuTemperature() ?? 0;
        }
        
        private IEnumerable<(string Name, double Rpm)> GetFanSpeeds()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var speeds = _hwMonitor.GetFanSpeeds();
                    if (speeds.Any()) return speeds;
                }
                catch (Exception ex)
                {
                    _logging?.Debug($"WmiFanController: fan speed monitor read failed: {ex.Message}");
                }
            }
            // Fallback to WMI BIOS — only use V2 direct RPM command on V2+ systems
            // CMD 0x38 (GetFanRpmDirect) is only valid on OMEN Max 2025+ with ThermalPolicy V2.
            // On V0/V1 systems, it may return garbage data (e.g., fan levels misread as RPM)
            // causing phantom 4200-4400 RPM readings when fans are actually quiet.
            if (_wmiBios.ThermalPolicy >= HpWmiBios.ThermalPolicyVersion.V2)
            {
                var rpms = _wmiBios.GetFanRpmDirect();
                var result = new List<(string, double)>();
                if (rpms.HasValue)
                {
                    var (cpu, gpu) = rpms.Value;
                    if (HpWmiBios.IsValidRpm(cpu)) result.Add(("CPU Fan", cpu));
                    if (HpWmiBios.IsValidRpm(gpu)) result.Add(("GPU Fan", gpu));
                }
                if (result.Any()) return result;
            }
            return Enumerable.Empty<(string, double)>();
        }

        /// <summary>
        /// Normalize raw LibreHardwareMonitor fan sensor names to user-friendly display labels.
        /// LHM can return raw EC-internal names like "G", "GP", "#1 Fan", "THRM - Fan1" etc.
        /// depending on the hardware platform. On AMD OMEN laptops (Ryzen AI / Strix Point),
        /// sensors may appear as "G" (GPU die fan) and "GP" (GPU Package fan) from ACPI tables.
        /// We map the first fan to "CPU Fan" and the second to "GPU Fan" for consistent display.
        /// </summary>
        private static string NormalizeFanName(string rawName, int index)
        {
            // Already a clean display name — keep as-is
            if (rawName.Equals("CPU Fan", StringComparison.OrdinalIgnoreCase) ||
                rawName.Equals("GPU Fan", StringComparison.OrdinalIgnoreCase))
                return rawName;

            // Use position-based naming: slot 0 = CPU fan, slot 1 = GPU fan
            return index == 0 ? "CPU Fan" : "GPU Fan";
        }

        /// <summary>
        /// Apply a fan preset using WMI BIOS commands.
        /// For Max preset: Sets Performance mode AND enables max fan speed for immediate 100% fan.
        /// For other presets: Sets the appropriate thermal policy via WMI BIOS.
        /// </summary>
        public bool ApplyPreset(FanPreset preset)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot apply preset: WMI BIOS not available");
                return false;
            }
            
            // Mark profile transition for RPM debounce
            _lastProfileSwitch = DateTime.Now;

            try
            {
                // Use centralized alias resolver — avoids divergent Contains() patterns across backends
                bool isMaxPreset = FanModeNameResolver.IsMaxAlias(preset.Name) && !FanModeNameResolver.IsAutoAlias(preset.Name);
                bool isAutoPreset = FanModeNameResolver.IsAutoAlias(preset.Name);
                
                // For Max preset, we need to:
                // 1. First set Performance mode (for aggressive thermal management)
                // 2. Then enable SetFanMax for 100% immediate fan speed
                // 
                // NOTE: We intentionally do NOT change GPU power here.
                // "Max cooling" means maximum fan speed to reduce temps, not increased GPU power.
                // Increasing GPU power (TGP/PPAB) would generate MORE heat, counteracting cooling.
                // User can independently control GPU power via the dedicated GPU Power settings.
                
                if (isMaxPreset)
                {
                    // Set Performance mode first
                    _logging?.Info("Applying Max fan preset: Setting Performance mode...");
                    _wmiBios.SetFanMode(HpWmiBios.FanMode.Performance);
                    _lastMode = HpWmiBios.FanMode.Performance;
                    
                    // Start countdown extension to keep fan settings active
                    StartCountdownExtension();
                    
                    // Now enable max fan speed (forces 100%)
                    double? rpmBefore = GetCurrentFanRpm();
                    if (_wmiBios.SetFanMax(true))
                    {
                        AddCommandToHistory("SetFanMax(true)", true, null, rpmBefore, null);
                        _logging?.Info("✓ Max fan speed command issued - awaiting verification...");

                        // Verify that fans actually reached high RPMs. Some BIOS/EC combinations accept the command
                        // but never actually spin the fans; avoid showing misleading 100% in the UI by readback.
                        if (VerifyMaxAppliedWithRetries())
                        {
                            _logging?.Info("✓ Max fan speed verified - fans confirmed at high RPM");
                            IsManualControlActive = true; // Mark as manual since we're forcing max
                            _isMaxModeActive = true;      // Track max mode for countdown extension
                            _lastManualFanPercent = 100;
                            _lastMaxModeMaintenanceUtc = DateTime.MinValue;
                            ResetMaxModeHealthTracking();
                            return true;
                        }

                        _logging?.Warn("SetFanMax succeeded but verification failed - attempting fallback methods");
                    }
                    else
                    {
                        AddCommandToHistory("SetFanMax(true)", false, "Command failed", rpmBefore, null);
                        _logging?.Warn("SetFanMax command failed - trying alternative method");
                    }

                    // Alternative: Try setting fan level directly to max
                    // Use MaxFanLevelCeiling (100) instead of _maxFanLevel — let the BIOS clamp
                    // to its actual hardware maximum. Models like OMEN 16-xd0xxx have max level 63
                    // (6300 RPM), which _maxFanLevel (55) would miss.
                    if (_wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling))
                    {
                        _logging?.Info($"✓ Fan level set to ceiling ({MaxFanLevelCeiling}, {MaxFanLevelCeiling}) — BIOS will clamp to hardware max. Awaiting verification...");

                        if (VerifyMaxAppliedWithRetries())
                        {
                            _logging?.Info("✓ Fan level verified - fans confirmed at high RPM");
                            IsManualControlActive = true;
                            _isMaxModeActive = true;
                            _lastManualFanPercent = 100;
                            _lastMaxModeMaintenanceUtc = DateTime.MinValue;
                            ResetMaxModeHealthTracking();
                            return true;
                        }

                        _logging?.Warn($"Fan level set to {MaxFanLevelCeiling} but verification failed");
                    }

                    // Rollback: if SetFanMax was accepted by BIOS but verification failed, make sure to clear the hardware override
                    try
                    {
                        // Reset FanMax flag on BIOS to avoid leaving hardware stuck in Max mode
                        _wmiBios.SetFanMax(false);
                        _commandVerifyFailCount++; // track a failed verification attempt
                        _logging?.Warn("SetFanMax verification failed — rolled back hardware override and incremented failure counter");
                    }
                    catch (Exception ex)
                    {
                        _logging?.Warn($"Failed to rollback SetFanMax after verification failure: {ex.Message}");
                    }

                    _logging?.Error("Failed to enable Max fan speed: verification failed");
                    // Ensure we don't leave UI thinking Max is active
                    IsManualControlActive = false;
                    _isMaxModeActive = false;
                    _lastManualFanPercent = -1;
                    return false;
                }
                
                // For non-Max presets, only run reset sequence if we were in max mode
                // This avoids unnecessary delays when switching between non-max presets
                if (IsManualControlActive)
                {
                    ResetFromMaxMode();
                }

                // Map preset to fan mode.
                // Hotfix v3.5.0: when a preset carries an explicit fan curve payload,
                // keep the current thermal policy mode so fan tuning does not silently
                // toggle GPU power behavior (e.g. 80W/175W swings on some OMEN models).
                // Max remains a special case and intentionally forces Performance + SetFanMax.
                bool hasCurvePayload = preset.Curve != null && preset.Curve.Count > 0;
                var mode = (!isMaxPreset && hasCurvePayload)
                    ? _lastMode
                    : MapPresetToFanMode(preset);

                if (!isMaxPreset && hasCurvePayload)
                {
                    _logging?.Info($"Preset '{preset.Name}' carries a fan curve; preserving thermal policy mode: {_lastMode}");
                }
                
                if (_wmiBios.SetFanMode(mode))
                {
                    // Capture previous mode before overwriting — used below for the V1 kick.
                    var previousMode = _lastMode;
                    if (!ConfirmFanModeReadback(mode, $"preset '{preset.Name}'"))
                    {
                        _logging?.Warn($"Preset '{preset.Name}' rejected: WMI command returned success but EC fan-mode readback did not match {mode}");
                        return false;
                    }

                    _lastMode = mode;
                    IsManualControlActive = false;
                    _isMaxModeActive = false;          // Clear max mode flag
                    _lastManualFanPercent = -1;
                    
                    // Start/stop countdown extension based on mode
                    if (mode == HpWmiBios.FanMode.Default || mode == HpWmiBios.FanMode.LegacyDefault)
                    {
                        // Auto/Default mode - stop countdown extension, let BIOS handle it
                        StopCountdownExtension();

                        // V1 transition kick (GitHub #102 — OMEN 17-ck1xxx fans stay at 100% after
                        // switching from Performance to Balanced/Quiet):
                        // On V1 systems (MaxFanLevel=55, krpm scale), some BIOS versions accept
                        // SetFanMode(Default) without actually reducing the fan hardware state from
                        // a prior Performance command. Sending an explicit SetFanLevel(20,20) —
                        // the same transition hint used in ResetFromMaxMode() — gives the EC a
                        // concrete duty-cycle target to ramp from, forcing the state change.
                        // Only applied when we were actually in Performance mode; skipped on V2
                        // systems where SetFanLevel uses percentage scale and 20% is a real target.
                        bool wasPerformanceMode = previousMode == HpWmiBios.FanMode.Performance ||
                                                  previousMode == HpWmiBios.FanMode.LegacyPerformance;
                        if (wasPerformanceMode && _maxFanLevel < 100)
                        {
                            System.Threading.Thread.Sleep(25);
                            if (_wmiBios.SetFanLevel(20, 20))
                            {
                                _logging?.Info("  V1 transition kick: SetFanLevel(20, 20) issued after Performance→Default switch to unblock BIOS fan ramp-down");
                            }
                        }

                        // V1 auto-mode floor clear (GitHub #100 Bug #4 — OMEN 16 xd0xxx fans
                        // remain at 200-300 RPM minimum in auto mode):
                        // After SetFanMode(Default), BIOS auto control is active, but the last
                        // manual fan level may still act as a floor. Sending SetFanLevel(0,0)
                        // explicitly clears this, allowing the BIOS thermal curve to spin down
                        // fans to 0 RPM at idle. Safe to send after SetFanMode(Default) since
                        // BIOS auto mode is already active — this is not a hard-stop command.
                        ClearV1AutoModeFloor("preset auto handoff");
                    }
                    else
                    {
                        // Non-default mode - start countdown extension to keep it active
                        StartCountdownExtension();
                    }
                    
                    // NOTE: GPU Power Boost is now controlled independently via System tab
                    // We no longer override the user's GPU power setting when applying fan presets
                    // This fixes the bug where Maximum TGP was being reset to Medium
                    
                    _logging?.Info($"✓ Applied preset: {preset.Name} (Mode: {mode})");
                    return true;
                }
                else
                {
                    _logging?.Warn($"SetFanMode failed for preset: {preset.Name} (Mode: {mode})");
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply preset: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Apply a custom fan curve by setting direct fan levels.
        /// </summary>
        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot apply custom curve: WMI BIOS not available");
                return false;
            }

            try
            {
                var curveList = curve.OrderBy(p => p.TemperatureC).ToList();
                if (!curveList.Any())
                {
                    _logging?.Warn("Empty curve provided");
                    return false;
                }

                // Get current temperature to determine fan level
                var cpuTemp = (int)GetCpuTemperature();
                var gpuTemp = (int)GetGpuTemperature();
                var maxTemp = Math.Max(cpuTemp, gpuTemp);

                // Find appropriate curve point
                // SAFETY: If temp exceeds all curve points, use LAST point (highest fan%)
                // This prevents fans from dropping to low speed at high temps
                var targetPoint = curveList.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                  ?? curveList.Last(); // Use highest, not lowest!

                // On many Victus/OMEN systems, manual 0% via WMI does not mean "silent auto";
                // it can leave fans pinned in an unresponsive manual-zero state. Treat 0% as
                // a request to return control to BIOS auto mode.
                if (targetPoint.FanPercent <= 0)
                {
                    _logging?.Info($"Custom curve requested 0% at {maxTemp}°C - restoring BIOS auto control instead of manual 0%.");
                    return RestoreAutoControl();
                }

                byte fanLevel = MapFanPercentToWmiLevel(targetPoint.FanPercent, _maxFanLevel);

                if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                {
                    IsManualControlActive = true;
                    _logging?.Info($"✓ Custom curve applied: {targetPoint.FanPercent}% @ {maxTemp}°C (Level: {fanLevel})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply custom curve: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Set fan speed as a percentage (0-100).
        /// For 100%, uses SetFanMax(true) to achieve maximum RPM beyond SetFanLevel limits.
        /// </summary>
        public bool SetFanSpeed(int percent)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set fan speed: WMI BIOS not available");
                AddCommandToHistory($"SetFanSpeed({percent})", false, "WMI BIOS not available");
                return false;
            }

            percent = Math.Clamp(percent, 0, 100);

            // Manual 0% can wedge some firmware states (fans stop and fail to recover).
            // For safety and model compatibility, map 0% to BIOS auto mode.
            if (percent == 0)
            {
                _logging?.Info("SetFanSpeed(0) mapped to RestoreAutoControl for firmware-safe silent behavior.");
                return RestoreAutoControl();
            }

            // Retry logic for fan control hardening
            const int maxRetries = 3;
            const int retryDelayMs = 500;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    bool success;
                    var captureRpmBefore = percent >= 100 || _isMaxModeActive || attempt > 1;
                    double? rpmBefore = captureRpmBefore ? GetCurrentFanRpm() : null;
                    
                    // For 100%, use SetFanMax which bypasses BIOS power limits.
                    // If it fails, send the protocol ceiling (100) and let BIOS clamp to
                    // model-specific max fan level (for example 63 on OMEN 16-xd0xxx).
                    if (percent >= 100)
                    {
                        success = _wmiBios.SetFanMax(true);
                        if (success)
                        {
                            AddCommandToHistory("SetFanMax(true)", true, null, rpmBefore, null);
                            _logging?.Info($"✓ Fan speed set to MAX (100%) via SetFanMax (attempt {attempt}/{maxRetries})");
                        }
                        else
                        {
                            // Fallback to SetFanLevel if SetFanMax fails
                            success = _wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling);
                            if (success)
                            {
                                AddCommandToHistory($"SetFanLevel({MaxFanLevelCeiling}, {MaxFanLevelCeiling})", true, null, rpmBefore, null);
                                _logging?.Info($"✓ Fan speed set to 100% via SetFanLevel ceiling fallback (attempt {attempt}/{maxRetries})");
                            }
                            else
                            {
                                AddCommandToHistory($"SetFanLevel({MaxFanLevelCeiling}, {MaxFanLevelCeiling})", false, "Command failed", rpmBefore, null);
                            }
                        }
                    }
                    else
                    {
                        // For <100%, disable max mode only if we were actually in max mode.
                        // Avoid sending redundant SetFanMax(false) on every curve tick.
                        if (_isMaxModeActive)
                        {
                            _wmiBios.SetFanMax(false);
                        }
                        
                        byte fanLevel = MapFanPercentToWmiLevel(percent, _maxFanLevel);
                        success = _wmiBios.SetFanLevel(fanLevel, fanLevel);
                        
                        if (success)
                        {
                            AddCommandToHistory($"SetFanLevel({fanLevel}, {fanLevel})", true, null, rpmBefore, null);
                            _logging?.Info($"✓ Fan speed set to {percent}% (Level: {fanLevel}) (attempt {attempt}/{maxRetries})");
                        }
                        else
                        {
                            AddCommandToHistory($"SetFanLevel({fanLevel}, {fanLevel})", false, "Command failed", rpmBefore, null);
                        }
                    }
                    
                    if (success)
                    {
                        IsManualControlActive = true;
                        var enteringMaxMode = percent >= 100 && !_isMaxModeActive;
                        _isMaxModeActive = percent >= 100;  // Track if we're at max
                        if (enteringMaxMode)
                        {
                            // Fresh Max hold: discard any peak learned during a previous hold.
                            ResetMaxModeHealthTracking();
                        }
                        _lastManualFanPercent = percent;     // Track for re-apply
                        _lastManualModeReapplyUtc = DateTime.UtcNow;
                        
                        // Start countdown extension to prevent BIOS from reverting
                        // HP BIOS has a 120-second timeout that resets fan control to auto
                        StartCountdownExtension();
                        
                        // Verify the fan speed was actually applied (basic verification)
                        if (VerifyFanSpeed(percent))
                        {
                            return true;
                        }
                        else
                        {
                            _logging?.Warn($"Fan speed verification failed for {percent}%, retrying...");
                            if (attempt < maxRetries)
                            {
                                Thread.Sleep(retryDelayMs);
                                continue;
                            }
                        }
                    }
                    
                    // If we get here, the command failed
                    if (attempt < maxRetries)
                    {
                        _logging?.Warn($"Fan speed command failed (attempt {attempt}/{maxRetries}), retrying in {retryDelayMs}ms...");
                        Thread.Sleep(retryDelayMs);
                    }
                    else
                    {
                        _logging?.Error($"Fan speed command failed after {maxRetries} attempts");
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"Failed to set fan speed (attempt {attempt}/{maxRetries}): {ex.Message}", ex);
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Set fan speeds independently for CPU and GPU fans.
        /// Allows different curves for each component for optimized cooling.
        /// </summary>
        /// <param name="cpuPercent">CPU fan speed percentage (0-100)</param>
        /// <param name="gpuPercent">GPU fan speed percentage (0-100)</param>
        /// <returns>True if both fans were set successfully</returns>
        public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set fan speeds: WMI BIOS not available");
                return false;
            }

            cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            gpuPercent = Math.Clamp(gpuPercent, 0, 100);

            // Safety mapping: dual 0% means return to BIOS auto. On V2 firmware,
            // explicit manual 0% can produce "fans dead" behavior on some Victus units.
            if (cpuPercent == 0 && gpuPercent == 0)
            {
                _logging?.Info("SetFanSpeeds(0,0) mapped to RestoreAutoControl for firmware-safe silent behavior.");
                return RestoreAutoControl();
            }

            try
            {
                bool success;
                
                // If both are 100%, use SetFanMax for true maximum
                if (cpuPercent >= 100 && gpuPercent >= 100)
                {
                    success = _wmiBios.SetFanMax(true);
                    if (success)
                    {
                        _logging?.Info($"✓ Both fans set to MAX (100%) via SetFanMax");
                    }
                    else
                    {
                        success = _wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling);
                        if (success)
                        {
                            _logging?.Info($"✓ Both fans set to 100% via SetFanLevel ceiling fallback");
                        }
                    }
                }
                else
                {
                    // Disable max mode only if we were actually in max mode.
                    // Avoid redundant SetFanMax(false) writes during steady-state curves.
                    if (_isMaxModeActive)
                    {
                        _wmiBios.SetFanMax(false);
                    }

                    // Single-fan 0% requests on percentage-scale firmware can leave the EC in an
                    // unstable manual-zero state. Keep a minimal manual duty for that channel.
                    if (_maxFanLevel >= 100)
                    {
                        if (cpuPercent == 0)
                        {
                            cpuPercent = 1;
                            _logging?.Debug("Adjusted CPU 0% request to 1% on V2 firmware safety path.");
                        }

                        if (gpuPercent == 0)
                        {
                            gpuPercent = 1;
                            _logging?.Debug("Adjusted GPU 0% request to 1% on V2 firmware safety path.");
                        }
                    }
                    
                    byte cpuLevel = MapFanPercentToWmiLevel(cpuPercent, _maxFanLevel);
                    byte gpuLevel = MapFanPercentToWmiLevel(gpuPercent, _maxFanLevel);
                    
                    success = _wmiBios.SetFanLevel(cpuLevel, gpuLevel);
                    
                    if (success)
                    {
                        _logging?.Info($"✓ Fan speeds set: CPU={cpuPercent}% (L{cpuLevel}), GPU={gpuPercent}% (L{gpuLevel})");
                    }
                }
                
                if (success)
                {
                    IsManualControlActive = true;
                    var enteringMaxMode = cpuPercent >= 100 && gpuPercent >= 100 && !_isMaxModeActive;
                    _isMaxModeActive = cpuPercent >= 100 && gpuPercent >= 100;
                    if (enteringMaxMode)
                    {
                        // Fresh Max hold: discard any peak learned during a previous hold.
                        ResetMaxModeHealthTracking();
                    }
                    _lastManualFanPercent = Math.Max(cpuPercent, gpuPercent);
                    _lastManualModeReapplyUtc = DateTime.UtcNow;
                    StartCountdownExtension();
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan speeds: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Enable maximum fan speed mode.
        /// </summary>
        public bool SetMaxFanSpeed(bool enabled)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set max fan: WMI BIOS not available");
                return false;
            }

            var ok = _wmiBios.SetFanMax(enabled);
            if (!ok)
            {
                return false;
            }

            if (enabled)
            {
                IsManualControlActive = true;
                _isMaxModeActive = true;
                _lastManualFanPercent = 100;
                _lastMaxModeMaintenanceUtc = DateTime.MinValue;
                ResetMaxModeHealthTracking();
                StartCountdownExtension();
            }
            else
            {
                _isMaxModeActive = false;
                _lastManualFanPercent = -1;
                ResetMaxModeHealthTracking();

                // If no other non-default mode is active, stop keepalive timer.
                if (_lastMode == HpWmiBios.FanMode.Default || _lastMode == HpWmiBios.FanMode.LegacyDefault)
                {
                    StopCountdownExtension();
                }
            }

            return true;
        }

        /// <summary>
        /// Set performance mode (Default/Performance/Cool).
        /// </summary>
        public bool SetPerformanceMode(string modeName)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set performance mode: WMI BIOS not available");
                return false;
            }

            HpWmiBios.FanMode fanMode;
            if (FanModeNameResolver.IsPerformanceAlias(modeName) || FanModeNameResolver.IsMaxAlias(modeName))
            {
                fanMode = HpWmiBios.FanMode.Performance;
            }
            else if (FanModeNameResolver.IsQuietAlias(modeName))
            {
                fanMode = HpWmiBios.FanMode.Cool;
            }
            else
            {
                fanMode = HpWmiBios.FanMode.Default;
            }

            if (_wmiBios.SetFanMode(fanMode))
            {
                if (!ConfirmFanModeReadback(fanMode, $"performance mode '{modeName}'"))
                {
                    return false;
                }

                // If Max mode was active, SetFanMode(...) alone does not release the BIOS
                // SetFanMax latch - ResetFromMaxMode() treats them as two separate, both-
                // required commands (see its Step 1/Step 2). This method previously cleared
                // _isMaxModeActive below without ever sending SetFanMax(false), so the
                // hardware stayed latched at max while the in-memory state claimed
                // otherwise. The next RestoreAutoControl() call would then see
                // _isMaxModeActive == false and skip its own reset entirely, since
                // ResetFromMaxMode() has its own identical "only if _isMaxModeActive" gate
                // (a deliberate optimization to skip a 4-5s WMI stall when genuinely not in
                // max mode - here that optimization was firing on stale state instead).
                // Real field report (board 8DCD): fans stuck at max after switching
                // Performance Mode away while Max fans was active, only closing the app
                // (which unconditionally resets EC/WMI on shutdown) released them.
                if (_isMaxModeActive)
                {
                    if (_wmiBios.SetFanMax(false))
                    {
                        _logging?.Info("  Released Max fan latch before switching performance mode");
                    }
                    else
                    {
                        _logging?.Warn("  Failed to release Max fan latch while switching performance mode");
                    }
                }

                _lastMode = fanMode;
                IsManualControlActive = false;
                _isMaxModeActive = false;
                _lastManualFanPercent = -1;
                _lastPresetModeReapplyUtc = DateTime.UtcNow;
                
                // Start/stop countdown extension to prevent BIOS from reverting
                // Performance and Cool modes need extension; Default lets BIOS take over
                if (fanMode == HpWmiBios.FanMode.Default || fanMode == HpWmiBios.FanMode.LegacyDefault)
                {
                    StopCountdownExtension();
                }
                else
                {
                    // Performance/Cool modes need countdown extension to maintain TDP settings
                    StartCountdownExtension();
                }
                
                _logging?.Info($"✓ Performance mode set: {modeName} → {fanMode}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Set performance mode using PerformanceMode object.
        /// </summary>
        public bool SetPerformanceMode(PerformanceMode mode)
        {
            return SetPerformanceMode(mode?.Name ?? "Default");
        }

        /// <summary>
        /// Restore automatic fan control.
        /// </summary>
        public bool RestoreAutoControl()
        {
            // Stop the Max-mode keepalive/reassertion timer unconditionally and first, before
            // anything below can short-circuit or throw. This timer is a pure in-memory
            // System.Threading.Timer with no WMI/hardware I/O of its own, so stopping it here
            // is always safe regardless of IsAvailable or what happens later in this method.
            // Previously this only happened as a side effect reached partway through a
            // successful reset sequence below — so the early "!IsAvailable" return just above
            // (transient WMI unavailability is plausible, not just a hard failure) skipped it
            // entirely, and any exception from ResetFromMaxMode() would have too. That gap is
            // the suspected root cause of GitHub #146: fans observed stuck at max speed during
            // ordinary use (not just suspend), independent of actual temperature.
            try
            {
                StopCountdownExtension();
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to stop fan countdown extension while restoring auto control: {ex.Message}");
            }

            if (!IsAvailable)
            {
                return false;
            }

            try
            {
                // Only run full reset when we were actually in a manual/max state,
                // and throttle repeated resets to avoid hammering firmware with writes.
                var shouldRunReset = _isMaxModeActive || IsManualControlActive;
                if (shouldRunReset)
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _lastAutoResetUtc).TotalSeconds >= AutoResetCooldownSeconds)
                    {
                        ResetFromMaxMode();
                        _lastAutoResetUtc = nowUtc;
                    }
                    else
                    {
                        _logging?.Debug($"Skipping reset sequence (cooldown active: {AutoResetCooldownSeconds}s)");
                    }
                }
                else
                {
                    _logging?.Debug("Skipping reset sequence (already in automatic mode)");
                }

                // Countdown extension was already stopped unconditionally at the top of this
                // method (see comment above); no need to repeat it here.

                // Clear debounce window — we do NOT want to filter RPM reads during this
                // transition; the reset sequence already ensures a clean state.
                _lastProfileSwitch = DateTime.MinValue;

                // Set default mode to restore automatic control
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Default))
                {
                    if (!ConfirmFanModeReadback(HpWmiBios.FanMode.Default, "restore auto control"))
                    {
                        return false;
                    }

                    IsManualControlActive = false;
                    _isMaxModeActive = false;
                    _lastManualFanPercent = -1;
                    _lastMode = HpWmiBios.FanMode.Default;

                    // V1 auto-mode floor clear (GitHub #100 Bug #4):
                    // Explicitly clear the manual fan level register so BIOS thermal
                    // management can drop fans to 0 RPM at idle. Safe here since
                    // SetFanMode(Default) already put BIOS in auto-control mode.
                    ClearV1AutoModeFloor("restore auto control");

                    _logging?.Info("✓ Restored automatic fan control");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to restore auto control: {ex.Message}", ex);
            }

            return false;
        }
        
        /// <summary>
        /// Robust reset sequence to exit MAX fan mode.
        /// Some HP BIOS versions require multiple steps to properly exit max mode.
        /// Based on OmenMon's approach and user feedback from Issue #7.
        /// </summary>
        private void ResetFromMaxMode()
        {
            _logging?.Info("Executing MAX mode reset sequence...");
            
            try
            {
                // Step 1: Disable max fan speed.
                // Only issue SetFanMax(false) when we actually enabled max mode — the WMI call
                // can take 4–5 seconds on some V1 firmware (e.g. 8E41) even for a no-op disable,
                // and skipping it when _isMaxModeActive is false eliminates that stall on every
                // non-max preset transition.
                if (_isMaxModeActive)
                {
                    if (_wmiBios.SetFanMax(false))
                    {
                        _logging?.Info("  Step 1: SetFanMax(false) succeeded");
                    }
                    else
                    {
                        _logging?.Warn("  Step 1: SetFanMax(false) failed");
                    }

                    // Small delay between commands (reduced from 50ms to 25ms)
                    System.Threading.Thread.Sleep(25);
                }
                else
                {
                    _logging?.Info("  Step 1: Skipped SetFanMax(false) — not in max mode (avoids firmware stall)");
                }
                
                // Step 2: Reset thermal policy to Default
                // This forces BIOS to reconsider fan control
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Default))
                {
                    _logging?.Info("  Step 2: SetFanMode(Default) succeeded");
                }
                
                System.Threading.Thread.Sleep(25);
                
                // V2 systems (MaxFanLevel=100) use percentage scale where SetFanLevel(0,0) means
                // "0% duty cycle", which puts the EC into manual-0% mode and prevents BIOS auto 
                // control from working. On V2, we skip SetFanLevel entirely and rely on
                // SetFanMode(Default) to restore BIOS control.
                // V1 systems (MaxFanLevel=55) use krpm scale where 0 means "BIOS takes over".
                if (_maxFanLevel < 100)
                {
                    // V1: Use krpm hint to help BIOS transition
                    // Step 3: Set fan levels to minimum (20 krpm = ~2000 RPM) as a transition hint
                    if (_wmiBios.SetFanLevel(20, 20))
                    {
                        _logging?.Info("  Step 3: SetFanLevel(20, 20) succeeded (V1 krpm hint)");
                    }

                    ClearV1AutoModeFloor("max-mode reset");
                }
                else
                {
                    _logging?.Info("  Steps 3-4: Skipped SetFanLevel on V2 system (percentage scale — would override BIOS auto control)");
                }
                
                // Final delay to let BIOS process (reduced from 100ms to 50ms)
                System.Threading.Thread.Sleep(50);
                
                _logging?.Info("MAX mode reset sequence completed");
            }
            catch (Exception ex)
            {
                _logging?.Warn($"MAX mode reset sequence error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset EC to factory defaults.
        /// Performs a comprehensive reset sequence to restore BIOS control and clear all manual overrides.
        /// This fixes stuck fan readings, incorrect BIOS display values, and other EC-related issues.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot reset EC: WMI BIOS not available");
                return false;
            }
            
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info("Starting EC Reset to Defaults...");
            _logging?.Info("═══════════════════════════════════════════════════");
            
            try
            {
                // Step 1: Disable max fan speed
                _logging?.Info("Step 1: Disabling max fan mode...");
                _wmiBios.SetFanMax(false);
                System.Threading.Thread.Sleep(50);
                
                // Step 2: Set fan mode to Default (restores BIOS thermal policy)
                _logging?.Info("Step 2: Setting fan mode to Default...");
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                System.Threading.Thread.Sleep(50);
                
                // Step 3: Hand fan levels back to BIOS without sending a hard 0%/0 krpm write.
                // V1 firmware has treated SetFanLevel(0,0) as a transient fan stop on some
                // models, while V2 percentage-scale firmware treats it as manual 0% duty.
                if (_maxFanLevel < 100)
                {
                    _logging?.Info("Step 3: Sending V1 fan transition hint...");
                    _wmiBios.SetFanLevel(20, 20);
                }
                else
                {
                    _logging?.Info("Step 3: Skipping SetFanLevel during V2 auto handoff; SetFanMode(Default) restores BIOS control");
                }
                System.Threading.Thread.Sleep(50);
                
                // Step 4: Extend countdown to prevent immediate timeout
                _logging?.Info("Step 4: Extending BIOS countdown timer...");
                _wmiBios.ExtendFanCountdown();
                System.Threading.Thread.Sleep(50);
                
                // Step 5: Set performance mode to Balanced
                _logging?.Info("Step 5: Setting performance mode to Balanced...");
                SetPerformanceMode("Balanced");
                System.Threading.Thread.Sleep(100);
                
                // Step 6: Final fan mode reset to Default
                _logging?.Info("Step 6: Final fan mode reset to Default...");
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                
                // Clear internal state
                IsManualControlActive = false;
                _isMaxModeActive = false;
                _lastMode = HpWmiBios.FanMode.Default;
                
                _logging?.Info("═══════════════════════════════════════════════════");
                _logging?.Info("✓ EC Reset to Defaults completed successfully");
                _logging?.Info("  BIOS should now have full control of fans");
                _logging?.Info("═══════════════════════════════════════════════════");
                
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"EC Reset failed: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Apply performance throttling mitigation via EC register 0x95.
        /// Discovered from omen-fan Linux utility - writing 0x31 (performance mode) to register 0x95
        /// can help mitigate thermal throttling on some OMEN models.
        /// 
        /// This is a fallback for cases where WMI SetFanMode doesn't fully enable performance mode.
        /// Register 0x95 is documented in the omen-fan project as the performance mode register.
        /// 
        /// Note: For WMI-based controller, this primarily uses WMI SetFanMode. Direct EC access
        /// requires the EC-based controller (EcFanControllerWrapper).
        /// </summary>
        public bool ApplyThrottlingMitigation()
        {
            _logging?.Info("Attempting throttling mitigation via WMI Performance mode...");
            
            try
            {
                // Use WMI SetFanMode to Performance as the primary method
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Performance))
                {
                    if (!ConfirmFanModeReadback(HpWmiBios.FanMode.Performance, "throttling mitigation"))
                    {
                        return false;
                    }

                    _logging?.Info("✓ Set WMI FanMode to Performance for throttling mitigation");
                    _lastMode = HpWmiBios.FanMode.Performance;
                    return true;
                }
                else
                {
                    _logging?.Warn("WMI SetFanMode to Performance failed - throttling mitigation not applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Throttling mitigation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read current fan telemetry data.
        /// v2.6.0: Uses GetFanRpmDirect() for V2 systems with proper sanity validation.
        /// </summary>
        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Get fan speeds from hardware monitor
            var fanSpeeds = GetFanSpeeds();
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                // v2.6.0: Validate RPM from LibreHardwareMonitor
                // v2.8.1: Add profile transition debounce — during profile switches,
                // BIOS may return stale/target fan levels that aren't actual RPM.
                int validatedRpm = (rpm > 0 && rpm <= 8000) ? (int)rpm : 0;
                
                // Debounce: during profile transition window, filter sudden large jumps
                bool inTransition = (DateTime.Now - _lastProfileSwitch).TotalMilliseconds < ProfileTransitionDebounceMs;
                if (inTransition && validatedRpm > 0)
                {
                    int prevRpm = index == 0 ? _lastCpuRpm : _lastGpuRpm;
                    // If previous reading was 0 and now we suddenly see a high value,
                    // it's likely a phantom reading during transition
                    if (prevRpm == 0 && validatedRpm > 1000)
                    {
                        _logging?.Debug($"[FanRPM] Debounce: Filtering phantom {validatedRpm} RPM during profile transition (previous was 0)");
                        validatedRpm = 0;
                    }
                }
                
                var fanLevel = _wmiBios.GetFanLevel();
                int levelPercent = 0;
                
                if (fanLevel.HasValue)
                {
                    // Use _maxFanLevel (auto-detected per model) instead of hardcoded 55.
                    // MaxFanLevel=55 for classic krpm models, 100 for percentage-based models.
                    levelPercent = index == 0 
                        ? fanLevel.Value.fan1 * 100 / _maxFanLevel 
                        : fanLevel.Value.fan2 * 100 / _maxFanLevel;
                }
                else if (validatedRpm > 0)
                {
                    // Estimate from RPM
                    levelPercent = EstimateDutyFromRpm(validatedRpm);
                }

                fans.Add(new FanTelemetry
                {
                    // Normalize LibreHardwareMonitor's raw sensor names (which can appear as
                    // "G", "GP", "#1 Fan", or other EC-internal abbreviations on AMD OMEN laptops)
                    // to display-friendly labels. Index 0 = CPU/first fan, index 1 = GPU/second fan.
                    Name = NormalizeFanName(name, index),
                    SpeedRpm = validatedRpm,
                    DutyCyclePercent = Math.Clamp(levelPercent, 0, 100),
                    Temperature = index == 0 ? GetCpuTemperature() : GetGpuTemperature()
                });
                
                // Track last RPM for debounce
                if (index == 0) _lastCpuRpm = validatedRpm;
                else _lastGpuRpm = validatedRpm;
                
                index++;
            }

            // Fallback if no fans detected by LibreHardwareMonitor (common on HP OMEN)
            // HP OMEN laptops expose fan data via HP WMI BIOS, not standard EC Super I/O
            if (fans.Count == 0)
            {
                var biosTemp = _wmiBios.GetTemperature();
                var gpuBiosTemp = _wmiBios.GetGpuTemperature();
                var cpuTemp = GetCpuTemperature();
                var gpuTemp = GetGpuTemperature();
                
                // v2.6.0: Use WMI BIOS temp if LibreHardwareMonitor fails
                if (cpuTemp <= 0 && biosTemp.HasValue) cpuTemp = biosTemp.Value;
                if (gpuTemp <= 0 && gpuBiosTemp.HasValue) gpuTemp = gpuBiosTemp.Value;
                
                // v2.6.0: Try direct RPM command first for V2 systems
                _logging?.Debug("[FanRPM] LibreHardwareMonitor found 0 fans, using WMI BIOS fallback");
                
                int fan1Rpm = 0;
                int fan2Rpm = 0;
                int fan1Percent = 0;
                int fan2Percent = 0;
                bool gotValidData = false;
                var rpmSource = RpmSource.Unknown;
                
                // Try direct RPM reading first — BUT only on V2+ systems.
                // CMD 0x38 (GetFanRpmDirect) is a V2-only command; on V1 systems it returns
                // garbage data (e.g., fan level values misinterpreted as RPM).
                bool inTransition = (DateTime.Now - _lastProfileSwitch).TotalMilliseconds < ProfileTransitionDebounceMs;
                
                if (_wmiBios.ThermalPolicy >= HpWmiBios.ThermalPolicyVersion.V2)
                {
                    var directRpm = _wmiBios.GetFanRpmDirect();
                    if (directRpm.HasValue && (directRpm.Value.fan1Rpm > 0 || directRpm.Value.fan2Rpm > 0))
                    {
                        fan1Rpm = directRpm.Value.fan1Rpm;
                        fan2Rpm = directRpm.Value.fan2Rpm;
                        
                        // Debounce: during profile transitions, filter sudden phantom readings
                        if (inTransition)
                        {
                            if (_lastCpuRpm == 0 && fan1Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom CPU {fan1Rpm} RPM during transition");
                                fan1Rpm = 0;
                            }
                            if (_lastGpuRpm == 0 && fan2Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom GPU {fan2Rpm} RPM during transition");
                                fan2Rpm = 0;
                            }
                        }
                        
                        fan1Percent = Math.Clamp((fan1Rpm * 100) / 5500, 0, 100);
                        fan2Percent = Math.Clamp((fan2Rpm * 100) / 5500, 0, 100);
                        gotValidData = true;
                        rpmSource = RpmSource.WmiBios;
                        _logging?.Debug($"[FanRPM] Direct RPM: CPU={fan1Rpm} ({fan1Percent}%), GPU={fan2Rpm} ({fan2Percent}%)");
                    }
                }
                
                // Try fan level if direct RPM unavailable
                if (!gotValidData)
                {
                    var fanLevel = _wmiBios.GetFanLevel();
                    _logging?.Debug($"[FanRPM] GetFanLevel returned: {(fanLevel.HasValue ? $"fan1={fanLevel.Value.fan1}, fan2={fanLevel.Value.fan2}" : "NULL")}");
                    
                    if (fanLevel.HasValue && (fanLevel.Value.fan1 > 0 || fanLevel.Value.fan2 > 0))
                    {
                        // HP WMI BIOS returns fan level in krpm units:
                        // 0 = 0 RPM (off)
                        // 55 = 5500 RPM (max)
                        // Convert krpm to RPM: multiply by 100
                        fan1Rpm = fanLevel.Value.fan1 * 100;
                        fan2Rpm = fanLevel.Value.fan2 * 100;
                        
                        // Debounce: during profile transitions, BIOS may still report
                        // old target levels even though fans are physically stopped
                        if (inTransition)
                        {
                            if (_lastCpuRpm == 0 && fan1Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom CPU level {fanLevel.Value.fan1} ({fan1Rpm} RPM) during transition");
                                fan1Rpm = 0;
                            }
                            if (_lastGpuRpm == 0 && fan2Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom GPU level {fanLevel.Value.fan2} ({fan2Rpm} RPM) during transition");
                                fan2Rpm = 0;
                            }
                        }
                        
                        // Sanity check the calculated RPM
                        if (fan1Rpm > 8000 || fan2Rpm > 8000)
                        {
                            _logging?.Warn($"[FanRPM] Invalid calculated RPM from level: {fan1Rpm}, {fan2Rpm} - using as level directly");
                            // The value might be actual RPM, not level - use as-is if in valid range
                            // fanLevel.Value.fan1/fan2 are bytes (0-255), but treat them as raw RPM if calculated RPM is invalid
                            int rawFan1 = fanLevel.Value.fan1;
                            int rawFan2 = fanLevel.Value.fan2;
                            if (rawFan1 > 0 && rawFan1 <= 8000) fan1Rpm = rawFan1;
                            if (rawFan2 > 0 && rawFan2 <= 8000) fan2Rpm = rawFan2;
                        }
                        
                        // Calculate percent based on max RPM range (5500 RPM = 100%)
                        fan1Percent = Math.Clamp((fan1Rpm * 100) / 5500, 0, 100);
                        fan2Percent = Math.Clamp((fan2Rpm * 100) / 5500, 0, 100);
                        gotValidData = true;
                        // GetFanLevel is a command/readback level. The x100 conversion is useful
                        // for a rough display value on classic V1 systems, but it is not an
                        // independent tachometer measurement and must never verify physical RPM.
                        rpmSource = RpmSource.Estimated;
                        
                        _logging?.Debug($"HP WMI fan-level estimate: Fan1={fanLevel.Value.fan1} -> ~{fan1Rpm} RPM ({fan1Percent}%), Fan2={fanLevel.Value.fan2} -> ~{fan2Rpm} RPM ({fan2Percent}%)");
                    }
                }
                
                // Do not fabricate RPM values when WMI readback is unavailable.
                // Surface a clear unavailable state (0 RPM + Unknown source) instead.
                if (!gotValidData)
                {
                    fan1Rpm = 0;
                    fan2Rpm = 0;
                    fan1Percent = 0;
                    fan2Percent = 0;
                    rpmSource = RpmSource.Unknown;
                    _logging?.Debug("[FanRPM] WMI fan readback unavailable - reporting 0 RPM with unknown source");
                }

                fans.Add(new FanTelemetry 
                { 
                    Name = "CPU Fan", 
                    SpeedRpm = fan1Rpm, 
                    DutyCyclePercent = fan1Percent, 
                    Temperature = cpuTemp > 0 ? cpuTemp : biosTemp ?? 0,
                    RpmSource = rpmSource
                });
                
                fans.Add(new FanTelemetry 
                { 
                    Name = "GPU Fan", 
                    SpeedRpm = fan2Rpm, 
                    DutyCyclePercent = fan2Percent, 
                    Temperature = gpuTemp,
                    RpmSource = rpmSource
                });

                // Track last known RPM for phantom RPM debounce
                _lastCpuRpm = fan1Rpm;
                _lastGpuRpm = fan2Rpm;
            }

            return fans;
        }

        /// <summary>
        /// Get current GPU power settings.
        /// </summary>
        public (bool customTgp, bool ppab, int dState)? GetGpuPowerSettings()
        {
            return _wmiBios.GetGpuPower();
        }

        /// <summary>
        /// Set GPU power level.
        /// </summary>
        public bool SetGpuPower(HpWmiBios.GpuPowerLevel level)
        {
            return _wmiBios.SetGpuPower(level);
        }

        /// <summary>
        /// Get current GPU mode.
        /// </summary>
        public HpWmiBios.GpuMode? GetGpuMode()
        {
            return _wmiBios.GetGpuMode();
        }

        /// <summary>
        /// Small verification helper: after attempting to enable max, checks fan RPM/duty a few times
        /// to confirm the hardware actually ramped. Returns true if verification passes.
        /// v2.6.0: Uses raw WMI reads to avoid circular dependency with estimated values.
        /// </summary>
        private bool VerifyMaxAppliedWithRetries()
        {
            const int attempts = 5;
            const int delayMs = 1000; // 1 second between checks — fans need 3-5 seconds to spin up

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // v2.6.0: Use raw WMI BIOS read instead of ReadFanSpeeds() which may return estimates
                    var rawRpm = _wmiBios.GetFanRpmDirect();
                    if (rawRpm.HasValue)
                    {
                        int maxRpm = Math.Max(rawRpm.Value.fan1Rpm, rawRpm.Value.fan2Rpm);
                        _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: Raw RPM - CPU={rawRpm.Value.fan1Rpm}, GPU={rawRpm.Value.fan2Rpm}");
                        
                        // For max mode, fans should be spinning fast (at least 3000 RPM)
                        // Lowered from 4000 to give faster verification on models that ramp slower
                        if (maxRpm >= 3000)
                        {
                            _logging?.Info($"[VerifyMax] ✓ Verified: {maxRpm} RPM");
                            return true;
                        }
                        
                        // Check if increasing from baseline
                        if (_lastCommandRpmBefore.HasValue && maxRpm > _lastCommandRpmBefore.Value + 500)
                        {
                            _logging?.Info($"[VerifyMax] ✓ RPM increased: {_lastCommandRpmBefore.Value} -> {maxRpm} (+{maxRpm - _lastCommandRpmBefore.Value})");
                            return true;
                        }
                    }
                    else
                    {
                        // Fallback to fan level if RPM command not available
                        var fanLevel = _wmiBios.GetFanLevel();
                        if (fanLevel.HasValue)
                        {
                            int maxLevel = Math.Max(fanLevel.Value.fan1, fanLevel.Value.fan2);
                            _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: Fan level - CPU={fanLevel.Value.fan1}, GPU={fanLevel.Value.fan2}");
                            
                            // Level 40+ indicates significant spin-up (works for both 0-55 and 0-100 ranges)
                            if (maxLevel >= 40)
                            {
                                _logging?.Info($"[VerifyMax] ✓ Verified via level: {maxLevel}");
                                return true;
                            }
                        }
                        else
                        {
                            _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: No fan data available from WMI");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"[VerifyMax] Error: {ex.Message}");
                }

                System.Threading.Thread.Sleep(delayMs);
            }
            
            _logging?.Warn($"[VerifyMax] Failed after {attempts} attempts - fan command may not be effective on this model");
            return false;
        }

        private HpWmiBios.FanMode MapPresetToFanMode(FanPreset preset)
        {
            // Determine the base mode from preset
            HpWmiBios.FanMode baseMode;
            
            // Check preset's FanMode enum first (if specified)
            switch (preset.Mode)
            {
                case Models.FanMode.Max:
                    baseMode = HpWmiBios.FanMode.Performance;
                    break;
                case Models.FanMode.Performance:
                    baseMode = HpWmiBios.FanMode.Performance;
                    break;
                case Models.FanMode.Quiet:
                    baseMode = HpWmiBios.FanMode.Cool;
                    break;
                case Models.FanMode.Manual:
                    // Custom curve preset: use neutral Default BIOS fan mode.
                    // The fan levels are driven by the curve monitoring engine, not the BIOS thermal
                    // policy. Using Cool or Performance here would impose an unintended TDP cap
                    // (Cool → ~25 W) or performance boost that the user did not ask for.
                    baseMode = HpWmiBios.FanMode.Default;
                    break;
                default:
                    // Map by canonical alias — resolver handles tokenized names uniformly
                    if (FanModeNameResolver.IsMaxAlias(preset.Name) && !FanModeNameResolver.IsAutoAlias(preset.Name))
                    {
                        baseMode = HpWmiBios.FanMode.Performance;
                    }
                    else if (FanModeNameResolver.IsQuietAlias(preset.Name))
                    {
                        baseMode = HpWmiBios.FanMode.Cool;
                    }
                    else if (FanModeNameResolver.IsPerformanceAlias(preset.Name))
                    {
                        baseMode = HpWmiBios.FanMode.Performance;
                    }
                    else
                    {
                        // Map based on preset curve characteristics
                        var maxFan = preset.Curve.Any() ? preset.Curve.Max(p => p.FanPercent) : 50;
                        var avgFan = preset.Curve.Any() ? preset.Curve.Average(p => p.FanPercent) : 50;
                        
                        if (avgFan < 40)
                            baseMode = HpWmiBios.FanMode.Cool;
                        else if (avgFan > 70 || maxFan > 90)
                            baseMode = HpWmiBios.FanMode.Performance;
                        else
                            baseMode = HpWmiBios.FanMode.Default;
                    }
                    break;
            }
            
            // Map to the correct command bytes based on ThermalPolicyVersion.
            // V0/Legacy systems use different command bytes (0x00-0x03) than V1+ systems (0x30/0x31/0x50).
            // Sending V1 bytes to V0 BIOS causes undefined behavior — e.g. Quiet (0x50) being 
            // interpreted as max performance, Transcend 14 2025 "Quiet = max fans" bug.
            if (_wmiBios.ThermalPolicy < HpWmiBios.ThermalPolicyVersion.V1)
            {
                // V0/Legacy mapping
                switch (baseMode)
                {
                    case HpWmiBios.FanMode.Performance:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyPerformance (0x01)");
                        return HpWmiBios.FanMode.LegacyPerformance;
                    case HpWmiBios.FanMode.Cool:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyCool (0x02)");
                        return HpWmiBios.FanMode.LegacyCool;
                    case HpWmiBios.FanMode.Default:
                    default:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyDefault (0x00)");
                        return HpWmiBios.FanMode.LegacyDefault;
                }
            }
            
            // V1+: use standard mode bytes (0x30, 0x31, 0x50)
            return baseMode;
        }

        // NOTE: ApplyGpuPowerFromPreset() was removed in v1.5.0-beta3
        // GPU Power Boost is now controlled independently via the System tab
        // Fan presets no longer override the user's GPU power setting

        private int EstimateDutyFromRpm(int rpm)
        {
            if (rpm == 0) return 0;
            
            const int minRpm = 1500;
            const int maxRpm = 6000;
            
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        /// <summary>
        /// Start the countdown extension timer to prevent BIOS from reverting fan settings.
        /// HP BIOS can revert fan settings on some models. Keepalive writes are bounded
        /// because WMI fan-level commands are expensive and can dominate CPU while curves
        /// hold the same target.
        /// </summary>
        public void StartCountdownExtension()
        {
            lock (_countdownTimerLock)
            {
                if (_countdownExtensionEnabled) return;

                _countdownExtensionTimer = new Timer(CountdownExtensionCallback, null,
                    CountdownExtensionInitialDelayMs, CountdownExtensionIntervalMs);
                _countdownExtensionEnabled = true;
                BackgroundTimerRegistry.Register(
                    "FanCountdownExtension",
                    "WmiFanController",
                    $"Reasserts fan settings to prevent BIOS reversion (every {CountdownExtensionIntervalMs / 1000}s)",
                    CountdownExtensionIntervalMs,
                    BackgroundTimerTier.Critical);
                _logging?.Info("✓ Fan countdown extension enabled (prevents settings reverting)");
            }
        }
        
        /// <summary>
        /// Stop the countdown extension timer.
        /// </summary>
        public void StopCountdownExtension()
        {
            lock (_countdownTimerLock)
            {
                if (!_countdownExtensionEnabled) return;

                _countdownExtensionTimer?.Dispose();
                _countdownExtensionTimer = null;
                _countdownExtensionEnabled = false;
                BackgroundTimerRegistry.Unregister("FanCountdownExtension");
                _logging?.Info("Fan countdown extension stopped");
            }
        }
        
        /// <summary>
        /// Countdown extension callback - periodically re-applies fan settings.
        /// Re-applies settings on a bounded cadence to prevent BIOS reversion without
        /// hammering WMI while a curve holds a stable fan percentage.
        /// </summary>
        private void CountdownExtensionCallback(object? state)
        {
            if (!IsAvailable || _disposed) return;

            // Timer callbacks can overlap under heavy system load; guard re-entry to avoid
            // concurrent firmware writes and unstable fan state transitions.
            if (Interlocked.Exchange(ref _countdownCallbackActive, 1) == 1)
            {
                return;
            }
            
            try
            {
                // Re-apply current fan settings to prevent BIOS reversion (OmenMon approach)
                if (IsManualControlActive || (_lastMode != HpWmiBios.FanMode.Default && _lastMode != HpWmiBios.FanMode.LegacyDefault))
                {
                    // For Max mode, re-apply SetFanMax(true) to ensure it stays active
                    if (_isMaxModeActive)
                    {
                        var nowUtc = DateTime.UtcNow;
                        if ((nowUtc - _lastMaxModeMaintenanceUtc).TotalMilliseconds < MaxModeMaintenanceIntervalMs)
                        {
                            return;
                        }

                        _lastMaxModeMaintenanceUtc = nowUtc;

                        var telemetryHealthy = IsMaxModeTelemetryHealthy(out var healthDetails);
                        var telemetryUnavailable = healthDetails.Contains("telemetry unavailable", StringComparison.OrdinalIgnoreCase);

                        if (telemetryUnavailable)
                        {
                            _maxModeTelemetryUnavailableStreak++;
                            if (_maxModeTelemetryUnavailableStreak < MaxModeTelemetryUnavailableReassertCycles)
                            {
                                _maxModeLowTelemetryStreak = 0;
                                TryExtendFanCountdown(nowUtc);
                                _logging?.Debug($"Max mode telemetry unavailable - keepalive only ({_maxModeTelemetryUnavailableStreak}/{MaxModeTelemetryUnavailableReassertCycles})");
                                return;
                            }

                            _maxModeTelemetryUnavailableStreak = 0;
                            var legacyDetails = "Max telemetry unavailable across multiple maintenance cycles; applying bounded legacy SetFanMax reassert to retain ownership.";
                            _lastMaxModeExternalResetUtc = DateTime.UtcNow;
                            _lastMaxModeExternalResetDetails = legacyDetails;

                            if (_wmiBios.SetFanMax(true))
                            {
                                _logging?.Warn("Max mode telemetry unavailable - periodic compatibility reassert applied");
                                AddCommandToHistory("SetFanMax(true)", true, legacyDetails, null, GetCurrentFanRpm());
                            }
                            else
                            {
                                _logging?.Warn("Max mode telemetry unavailable - periodic compatibility reassert failed");
                                AddCommandToHistory("SetFanMax(true)", false, legacyDetails, null, GetCurrentFanRpm());
                            }
                            return;
                        }

                        _maxModeTelemetryUnavailableStreak = 0;
                        if (telemetryHealthy)
                        {
                            _maxModeLowTelemetryStreak = 0;
                            TryExtendFanCountdown(nowUtc);
                            _logging?.Debug($"Max mode maintained via countdown keepalive ({healthDetails})");
                            return;
                        }

                        _maxModeLowTelemetryStreak++;
                        if (_maxModeLowTelemetryStreak < _maxModeDropChecksBeforeReapply)
                        {
                            _logging?.Debug($"Max mode telemetry low ({healthDetails}) - waiting for sustained confirmation ({_maxModeLowTelemetryStreak}/{_maxModeDropChecksBeforeReapply})");
                            return;
                        }

                        _maxModeLowTelemetryStreak = 0;
                        var resetDetails = $"Max telemetry dropped while Max mode was active ({healthDetails}); OGH, firmware timeout, or another controller may have reset fan state.";
                        _lastMaxModeExternalResetUtc = DateTime.UtcNow;
                        _lastMaxModeExternalResetDetails = resetDetails;

                        if (_wmiBios.SetFanMax(true))
                        {
                            _logging?.Warn($"External fan reset suspected - Max mode re-applied after sustained drop ({healthDetails})");
                            AddCommandToHistory("SetFanMax(true)", true, resetDetails, null, GetCurrentFanRpm());
                        }
                        else
                        {
                            _logging?.Warn("External fan reset suspected - failed to re-apply Max mode, trying fallback");
                            AddCommandToHistory("SetFanMax(true)", false, resetDetails, null, GetCurrentFanRpm());
                            // Fallback: send ceiling value (100) — let BIOS clamp to hardware max
                            var fallbackSucceeded = _wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling);
                            AddCommandToHistory(
                                $"SetFanLevel({MaxFanLevelCeiling}, {MaxFanLevelCeiling})",
                                fallbackSucceeded,
                                $"{resetDetails}; SetFanMax(true) failed, fallback {(fallbackSucceeded ? "sent" : "failed")}.",
                                null,
                                GetCurrentFanRpm());
                        }
                    }
                    else if (IsManualControlActive && _lastManualFanPercent >= 0)
                    {
                        var nowUtc = DateTime.UtcNow;
                        var reapplyIntervalMs = _lastManualFanPercent >= HighDutyManualModeThresholdPercent
                            ? HighDutyManualModeReapplyIntervalMs
                            : ManualModeReapplyIntervalMs;
                        if ((nowUtc - _lastManualModeReapplyUtc).TotalMilliseconds < reapplyIntervalMs)
                        {
                            // Even when a manual-level write is throttled, keep the BIOS ownership
                            // countdown alive so firmware does not reclaim fan policy between writes.
                            TryExtendFanCountdown(nowUtc);
                            return;
                        }

                        // For custom fan curves, re-apply the last set percentage.
                        byte fanLevel = MapFanPercentToWmiLevel(_lastManualFanPercent, _maxFanLevel);
                        if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                        {
                            _lastManualModeReapplyUtc = nowUtc;
                            TryExtendFanCountdown(nowUtc);
                            var count = Interlocked.Increment(ref _keepaliveWriteCount);
                            if (count % KeepaliveLogSummaryInterval == 1)
                                _logging?.Info($"[FanKeepalive] Fan level keepalive active: {_lastManualFanPercent}% (write #{count})");
                        }
                    }
                    else
                    {
                        var nowUtc = DateTime.UtcNow;
                        if (FanService.IsAnyDiagnosticModeActive)
                        {
                            TryExtendFanCountdown(nowUtc);
                            return;
                        }

                        if ((nowUtc - _lastPresetModeReapplyUtc).TotalMilliseconds < PresetModeReapplyIntervalMs)
                        {
                            return;
                        }

                        // For preset modes, re-apply the fan mode
                        if (_wmiBios.SetFanMode(_lastMode))
                        {
                            _lastPresetModeReapplyUtc = nowUtc;
                            var count = Interlocked.Increment(ref _keepaliveWriteCount);
                            if (count % KeepaliveLogSummaryInterval == 1)
                                _logging?.Info($"[FanKeepalive] Fan mode keepalive active: {_lastMode} (write #{count})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan settings: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _countdownCallbackActive, 0);
            }
        }

        private void TryExtendFanCountdown(DateTime nowUtc)
        {
            if ((nowUtc - _lastCountdownExtendUtc).TotalMilliseconds < CountdownExtendMinIntervalMs)
            {
                return;
            }

            _lastCountdownExtendUtc = nowUtc;
            _wmiBios.ExtendFanCountdown();
        }

        private bool ConfirmFanModeReadback(HpWmiBios.FanMode expectedMode, string operation)
        {
            if (_ecAccess?.IsAvailable != true)
            {
                _logging?.Debug($"Fan mode readback unavailable for {operation}; accepting WMI command result");
                return true;
            }

            byte expected = (byte)expectedMode;
            byte? lastRead = null;

            for (int attempt = 1; attempt <= FanModeReadbackAttempts; attempt++)
            {
                try
                {
                    var actual = _ecAccess.ReadByte(EcFanModeRegister);
                    lastRead = actual;
                    if (actual == expected)
                    {
                        _logging?.Debug($"Fan mode readback confirmed for {operation}: EC[0x{EcFanModeRegister:X2}]=0x{actual:X2}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Debug($"Fan mode readback unavailable for {operation}: {ex.Message}");
                    return true;
                }

                if (attempt < FanModeReadbackAttempts)
                {
                    System.Threading.Thread.Sleep(FanModeReadbackDelayMs);
                }
            }

            var actualText = lastRead.HasValue ? $"0x{lastRead.Value:X2}" : "n/a";
            _logging?.Warn($"Fan mode readback mismatch for {operation}: expected EC[0x{EcFanModeRegister:X2}]=0x{expected:X2}, got {actualText}");
            if (!_strictFanModeReadback)
            {
                _logging?.Warn($"Fan mode readback mismatch ignored for {operation}; model profile does not allow direct EC fan-mode verification");
                return true;
            }

            return false;
        }

        private void ClearV1AutoModeFloor(string context)
        {
            if (_maxFanLevel >= 100)
            {
                _logging?.Info("  V2 auto-mode handoff: skipped SetFanLevel(0, 0); percentage-scale firmware uses SetFanMode(Default) only");
                return;
            }

            if (!_allowV1AutoModeFloorClear)
            {
                _logging?.Info($"  V1 auto-mode handoff: skipped SetFanLevel(0, 0) during {context}; conservative WMI profile avoids manual-zero fan stops");
                return;
            }

            System.Threading.Thread.Sleep(50);
            if (_wmiBios.SetFanLevel(0, 0))
            {
                _logging?.Info($"  V1 auto-mode floor clear: SetFanLevel(0, 0) succeeded during {context}");
            }
            else
            {
                _logging?.Warn($"  V1 auto-mode floor clear failed during {context}");
            }
        }

        /// <summary>
        /// Clears the per-session Max-mode health tracking state. Must run on every Max-mode
        /// entry and exit so an observed peak learned during one hold can never leak into the
        /// next one (a stale peak from a previous session would lower the drop-detection bar).
        /// </summary>
        private void ResetMaxModeHealthTracking()
        {
            _maxModeLowTelemetryStreak = 0;
            _maxModeTelemetryUnavailableStreak = 0;
            _maxModeObservedPeakLevel = 0;
            _maxModeLevelObservationCount = 0;
            _maxModeNominalFloorEverMet = false;
        }

        private bool IsMaxModeTelemetryHealthy(out string details)
        {
            bool hasTelemetry = false;
            bool healthy = false;
            bool hasLevelTelemetry = false;
            bool hasRpmTelemetry = false;
            bool levelHealthy = false;
            bool rpmHealthy = false;
            string levelInfo = "levels=n/a";
            string rpmInfo = "rpm=n/a";

            var fanLevels = _wmiBios.GetFanLevel();
            if (fanLevels.HasValue)
            {
                hasTelemetry = true;
                hasLevelTelemetry = true;
                int nominalFloor = Math.Max(1, (int)Math.Round(_maxFanLevel * MaxModeHealthyLevelRatio));
                int levelMax = Math.Max(fanLevels.Value.fan1, fanLevels.Value.fan2);

                if (levelMax > _maxModeObservedPeakLevel)
                {
                    _maxModeObservedPeakLevel = levelMax;
                }
                if (levelMax >= nominalFloor)
                {
                    _maxModeNominalFloorEverMet = true;
                }
                if (_maxModeLevelObservationCount < int.MaxValue)
                {
                    _maxModeLevelObservationCount++;
                }

                // Default to the strict nominal floor. Only fall back to the observed peak once
                // this board has demonstrated, across several consecutive reads, that it never
                // reaches the nominal floor at all — otherwise a board that legitimately dropped
                // from max would get a lowered bar and the drop would go undetected.
                int effectiveFloor = nominalFloor;
                bool usingObservedPeak = false;
                if (!_maxModeNominalFloorEverMet &&
                    _maxModeLevelObservationCount >= MaxModeObservedPeakMinObservations &&
                    _maxModeObservedPeakLevel > 0 &&
                    _maxModeObservedPeakLevel < nominalFloor)
                {
                    // Keep an absolute backstop so a genuine collapse toward idle/auto is still
                    // caught even when the observed peak becomes the reference point.
                    effectiveFloor = Math.Max(
                        (int)Math.Round(_maxModeObservedPeakLevel * MaxModeObservedPeakTolerance),
                        (int)Math.Round(_maxFanLevel * MaxModeAbsoluteLevelFloorRatio));
                    usingObservedPeak = true;
                }

                levelHealthy = levelMax >= effectiveFloor;
                levelInfo = usingObservedPeak
                    ? $"levels={fanLevels.Value.fan1}/{fanLevels.Value.fan2} floor={effectiveFloor} (observed peak {_maxModeObservedPeakLevel}; nominal floor {nominalFloor} unreachable on this board)"
                    : $"levels={fanLevels.Value.fan1}/{fanLevels.Value.fan2} floor={effectiveFloor}";
            }

            var rpms = _wmiBios.GetFanRpmDirect();
            if (rpms.HasValue)
            {
                hasTelemetry = true;
                hasRpmTelemetry = true;
                int rpmMax = Math.Max(rpms.Value.fan1Rpm, rpms.Value.fan2Rpm);
                rpmHealthy = rpmMax >= MaxModeHealthyRpmFloor;
                rpmInfo = $"rpm={rpms.Value.fan1Rpm}/{rpms.Value.fan2Rpm} floor={MaxModeHealthyRpmFloor}";
            }

            // If telemetry is unavailable, prefer non-invasive keepalive over forced reapply.
            if (!hasTelemetry)
            {
                details = "telemetry unavailable";
                return true;
            }

            // Prefer fan-level telemetry when available: it maps directly to firmware hold state.
            // RPM is used as fallback when level telemetry is missing.
            if (hasLevelTelemetry)
            {
                healthy = levelHealthy;
            }
            else if (hasRpmTelemetry)
            {
                healthy = rpmHealthy;
            }

            details = $"{levelInfo}, {rpmInfo}";
            return healthy;
        }
        
        /// <summary>
        /// Test if WMI commands actually affect fan speed.
        /// Some newer OMEN models (Transcend, 2024+) return success but don't change speed.
        /// This helps detect if OGH proxy should be used instead.
        /// </summary>
        /// <returns>True if commands appear effective, false if they seem to have no effect</returns>
        public bool TestCommandEffectiveness()
        {
            if (!IsAvailable)
                return false;
                
            _logging?.Info("Testing WMI command effectiveness...");
            
            try
            {
                // Get baseline RPM
                var initialSpeeds = ReadFanSpeeds().ToList();
                var initialRpm = initialSpeeds.FirstOrDefault()?.SpeedRpm ?? 0;
                _logging?.Info($"  Initial fan RPM: {initialRpm}");
                
                // Try setting max fan mode
                bool setResult = _wmiBios.SetFanMax(true);
                _logging?.Info($"  SetFanMax(true) returned: {setResult}");
                
                if (!setResult)
                {
                    _logging?.Warn("  WMI command returned failure - commands may not work on this model");
                    return false;
                }
                
                // Wait for fans to respond (they have inertia)
                System.Threading.Thread.Sleep(VerifyDelayMs);
                
                // Check if RPM increased
                var newSpeeds = ReadFanSpeeds().ToList();
                var newRpm = newSpeeds.FirstOrDefault()?.SpeedRpm ?? 0;
                _logging?.Info($"  New fan RPM after max command: {newRpm}");
                
                // Restore default mode
                _wmiBios.SetFanMax(false);
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                
                // If initial RPM was already high (> 4000), command might not show increase
                // In that case, we consider it likely working
                if (initialRpm >= 4000)
                {
                    _logging?.Info("  Initial RPM already high - assuming commands work");
                    return true;
                }
                
                // If RPM increased by at least 500, commands are working
                int rpmIncrease = newRpm - initialRpm;
                if (rpmIncrease >= 500)
                {
                    _logging?.Info($"  ✓ Fan RPM increased by {rpmIncrease} - commands are effective");
                    return true;
                }
                
                // Commands returned success but no RPM change
                _commandVerifyFailCount++;
                _logging?.Warn($"  ⚠️ WMI returned success but fan RPM didn't change significantly (+{rpmIncrease})");
                _logging?.Warn($"  This model may require OGH proxy for fan control (fail count: {_commandVerifyFailCount})");
                
                if (CommandsIneffective)
                {
                    _logging?.Error("  ❌ WMI commands confirmed ineffective on this model");
                    _logging?.Info("  → Consider using OGH proxy or reinstalling OMEN Gaming Hub");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Command effectiveness test failed: {ex.Message}");
                return true; // Assume working on exception to avoid false negatives
            }
        }
        
        /// <summary>
        /// Record the current fan RPM before sending a command.
        /// Call VerifyCommandEffect() after the command to check if it worked.
        /// </summary>
        public void RecordPreCommandRpm()
        {
            try
            {
                var speeds = ReadFanSpeeds().ToList();
                _lastCommandRpmBefore = speeds.FirstOrDefault()?.SpeedRpm ?? 0;
            }
            catch
            {
                _lastCommandRpmBefore = null;
            }
        }
        
        /// <summary>
        /// Verify if the last command had an effect on fan speed.
        /// Should be called ~3 seconds after a fan command.
        /// </summary>
        /// <param name="wasIncreaseExpected">True if we expected fans to speed up, false if slow down</param>
        /// <returns>True if command appeared effective</returns>
        public bool VerifyCommandEffect(bool wasIncreaseExpected)
        {
            if (!_lastCommandRpmBefore.HasValue)
                return true; // No baseline, assume success
                
            try
            {
                var speeds = ReadFanSpeeds().ToList();
                var currentRpm = speeds.FirstOrDefault()?.SpeedRpm ?? 0;
                var rpmChange = currentRpm - _lastCommandRpmBefore.Value;
                
                bool effectDetected;
                if (wasIncreaseExpected)
                {
                    // For increase commands, we expect at least 300 RPM increase
                    effectDetected = rpmChange >= 300 || currentRpm >= 4000;
                }
                else
                {
                    // For decrease commands, we expect at least 300 RPM decrease
                    effectDetected = rpmChange <= -300 || currentRpm <= 2500;
                }
                
                if (!effectDetected)
                {
                    _commandVerifyFailCount++;
                    _logging?.Warn($"Command verification failed: RPM change was {rpmChange} (expected {(wasIncreaseExpected ? "increase" : "decrease")})");
                }
                
                _lastCommandRpmBefore = null;
                return effectDetected;
            }
            catch
            {
                _lastCommandRpmBefore = null;
                return true; // Assume success on error
            }
        }

        /// <summary>
        /// Estimate fan speed based on temperature using a realistic OMEN BIOS fan curve.
        /// This provides more accurate RPM readings when direct fan level reading is unavailable.
        /// </summary>
        private (int percent, int rpm) EstimateFanFromTemperature(double temperature, string fanType)
        {
            int percent;

            // OMEN BIOS fan curves are typically more aggressive than linear
            // CPU fans tend to run slightly higher than GPU fans at same temp
            if (temperature <= 30)
            {
                // Idle/cool: 20-30%
                percent = fanType == "CPU" ? 25 : 20;
            }
            else if (temperature <= 40)
            {
                // Light load: 30-45%
                percent = (int)(25 + (temperature - 30) * 2.5); // 25-50% over 10°C
            }
            else if (temperature <= 60)
            {
                // Moderate load: 45-70%
                percent = (int)(40 + (temperature - 40) * 1.5); // 40-70% over 20°C
            }
            else if (temperature <= 80)
            {
                // Heavy load: 70-90%
                percent = (int)(70 + (temperature - 60) * 1.0); // 70-90% over 20°C
            }
            else
            {
                // Extreme load: 90-100%
                percent = Math.Min(100, (int)(90 + (temperature - 80) * 2.5)); // 90-100%+
            }

            // CPU fans typically run 5-10% higher than GPU fans
            if (fanType == "CPU")
            {
                percent = Math.Min(100, percent + 5);
            }

            percent = Math.Clamp(percent, 0, 100);
            int rpm = (percent * 5500) / 100;

            return (percent, rpm);
        }

        /// <summary>
        /// Basic verification that fan speed was applied (checks if command succeeded and no immediate error).
        /// More advanced verification would require reading back fan levels, but WMI BIOS doesn't always provide this.
        /// </summary>
        private bool VerifyFanSpeed(int targetPercent)
        {
            // For now, just do basic verification - in future could read back fan levels if available
            // This is mainly to catch immediate failures
            try
            {
                // If we have fan telemetry, we could verify RPM changed in expected direction
                // For now, assume success if no exception was thrown
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Fan speed verification error: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopCountdownExtension();
                // Don't restore auto control on exit - preserve user's fan settings
                // This prevents fans ramping up when user closes app while in Quiet mode
                _wmiBios.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Get command history for diagnostics
        /// </summary>
        public IReadOnlyList<WmiCommandHistoryEntry> GetCommandHistory()
        {
            lock (_commandHistory)
            {
                return _commandHistory.ToList();
            }
        }

        /// <summary>
        /// Get current fan RPM for command history tracking
        /// </summary>
        private double? GetCurrentFanRpm()
        {
            try
            {
                var fanSpeeds = GetFanSpeeds();
                var firstFan = fanSpeeds.FirstOrDefault();
                return firstFan.Rpm; // Access the Rpm property of the tuple
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Add a command to the history
        /// </summary>
        private void AddCommandToHistory(string command, bool success, string? error = null, double? fanRpmBefore = null, double? fanRpmAfter = null)
        {
            lock (_commandHistory)
            {
                _commandHistory.Add(new WmiCommandHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Command = command,
                    Success = success,
                    Error = error,
                    FanRpmBefore = fanRpmBefore,
                    FanRpmAfter = fanRpmAfter
                });

                // Keep only the most recent entries
                if (_commandHistory.Count > MaxCommandHistoryEntries)
                {
                    _commandHistory.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// Entry in WMI command history for diagnostics
    /// </summary>
    public class WmiCommandHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Command { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
        public double? FanRpmBefore { get; set; }
        public double? FanRpmAfter { get; set; }
    }
}
