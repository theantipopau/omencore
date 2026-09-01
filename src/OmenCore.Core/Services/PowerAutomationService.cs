using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenCore.Models;
using OmenCore.Utils;
using Windows.Devices.Power;

namespace OmenCore.Services
{
    /// <summary>
    /// Automated profile switching based on power source (AC/Battery).
    /// Features:
    /// - Auto-switch performance mode on power change
    /// - Auto-switch GPU mode on power change (if supported)
    /// - Configurable per-state settings
    /// </summary>
    public class PowerAutomationService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly FanService _fanService;
        private readonly PerformanceModeService _performanceModeService;
        private readonly ConfigurationService _configService;
        private readonly GpuSwitchService? _gpuSwitchService;
        private bool _isEnabled;
        private bool _lastKnownAcState;
        private bool? _priorSessionAcState;
        private bool _disposed;
        private CancellationTokenSource? _stateChangeCts;
        private readonly object _stateChangeLock = new();

        public event EventHandler<PowerStateChangedEventArgs>? PowerStateChanged;
        public event EventHandler? SystemSuspending;
        public event EventHandler? SystemResuming;

        public bool IsEnabled 
        { 
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    _logging.Info($"Power automation {(value ? "enabled" : "disabled")}");
                    SaveSettings();
                }
            }
        }

        // Settings for AC power
        public string AcFanPreset { get; set; } = "Auto";
        public string AcPerformanceMode { get; set; } = "Balanced";
        public string AcGpuMode { get; set; } = "Hybrid";

        // Settings for Battery
        public string BatteryFanPreset { get; set; } = "Quiet";
        public string BatteryPerformanceMode { get; set; } = "Silent";
        public string BatteryGpuMode { get; set; } = "Eco";

        public bool IsOnAcPower => _lastKnownAcState;

        /// <summary>
        /// True when the AC/Battery power source differs from what this service last persisted as
        /// known-true (a real transition happened while the app was closed, or crashed, or this is
        /// the very first session automation has ever run) - the "ownership" signal
        /// <see cref="ApplyCurrentProfile"/> uses to decide whether a startup profile apply is a
        /// genuine automation reaction or would just be silently overriding the user's last manual
        /// selection for no real-world reason. See that method's own doc comment.
        /// </summary>
        public bool TransitionOccurredSincePriorSession { get; private set; }

        public PowerAutomationService(
            LoggingService logging,
            FanService fanService,
            PerformanceModeService performanceModeService,
            ConfigurationService configService,
            GpuSwitchService? gpuSwitchService = null)
        {
            _logging = logging;
            _fanService = fanService;
            _performanceModeService = performanceModeService;
            _configService = configService;
            _gpuSwitchService = gpuSwitchService;

            // Load settings from config
            LoadSettings();

            // Detect initial power state
            _lastKnownAcState = GetCurrentAcState();

            // Compare against what was persisted at the end of the prior session, before
            // overwriting it below - this is the one-shot "did the power source actually change
            // while the app wasn't running" signal ApplyCurrentProfile() needs. Unknown (no prior
            // session ever persisted a value) counts as "yes, apply" - same as this service's
            // original always-apply behavior - since there's no established baseline to protect.
            TransitionOccurredSincePriorSession = !_priorSessionAcState.HasValue
                || _priorSessionAcState.Value != _lastKnownAcState;
            PersistLastKnownAcState(_lastKnownAcState);

            // Subscribe to power events
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            _logging.Info($"PowerAutomationService initialized. AC Power: {_lastKnownAcState}, Enabled: {_isEnabled}, TransitionSincePriorSession: {TransitionOccurredSincePriorSession}");
        }

        private void LoadSettings()
        {
            try
            {
                var config = _configService.Load();
                
                _isEnabled = config.PowerAutomation?.Enabled ?? false;
                AcFanPreset = config.PowerAutomation?.AcFanPreset ?? "Auto";
                AcPerformanceMode = config.PowerAutomation?.AcPerformanceMode ?? "Balanced";
                AcGpuMode = config.PowerAutomation?.AcGpuMode ?? "Hybrid";
                BatteryFanPreset = config.PowerAutomation?.BatteryFanPreset ?? "Quiet";
                BatteryPerformanceMode = config.PowerAutomation?.BatteryPerformanceMode ?? "Silent";
                BatteryGpuMode = config.PowerAutomation?.BatteryGpuMode ?? "Eco";
                _priorSessionAcState = config.PowerAutomation?.LastKnownAcState;

                _logging.Info($"Power automation settings loaded: Enabled={_isEnabled}, AC={AcPerformanceMode}, Battery={BatteryPerformanceMode}");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to load power automation settings", ex);
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = _configService.Load();
                
                config.PowerAutomation ??= new PowerAutomationSettings();
                config.PowerAutomation.Enabled = _isEnabled;
                config.PowerAutomation.AcFanPreset = AcFanPreset;
                config.PowerAutomation.AcPerformanceMode = AcPerformanceMode;
                config.PowerAutomation.AcGpuMode = AcGpuMode;
                config.PowerAutomation.BatteryFanPreset = BatteryFanPreset;
                config.PowerAutomation.BatteryPerformanceMode = BatteryPerformanceMode;
                config.PowerAutomation.BatteryGpuMode = BatteryGpuMode;
                
                _configService.Save(config);
                _logging.Info("Power automation settings saved");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to save power automation settings", ex);
            }
        }

        /// <summary>
        /// Persists the AC/Battery state this service currently believes is true, independent of
        /// <see cref="SaveSettings"/> (which only runs on an explicit user settings change) - this
        /// needs to happen every session at startup and on every verified transition, so the next
        /// session's <see cref="TransitionOccurredSincePriorSession"/> check has an up-to-date
        /// baseline to compare against rather than a stale one from whenever settings last saved.
        /// </summary>
        private void PersistLastKnownAcState(bool isOnAc)
        {
            try
            {
                var config = _configService.Load();
                config.PowerAutomation ??= new PowerAutomationSettings();
                config.PowerAutomation.LastKnownAcState = isOnAc;
                _configService.Save(config);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to persist last-known AC state: {ex.Message}");
            }
        }

        private bool GetCurrentAcState()
        {
            try
            {
                // Method 1: GetSystemPowerStatus for reliable AC detection
                var isOnAc = PowerStatusHelper.IsAcPowerOnline();
                _logging.Debug($"AC detection (GetSystemPowerStatus): IsOnAc={isOnAc}");
                return isOnAc;
            }
            catch (Exception ex)
            {
                _logging.Warn($"SystemInformation power detection failed: {ex.Message}");
                
                // Method 2: Try WinRT Battery API
                try
                {
                    var report = Battery.AggregateBattery.GetReport();
                    var status = report.Status;
                    var isOnAc = status == Windows.System.Power.BatteryStatus.Charging ||
                                 status == Windows.System.Power.BatteryStatus.Idle ||
                                 status == Windows.System.Power.BatteryStatus.NotPresent;
                    _logging.Debug($"AC detection (WinRT): BatteryStatus={status}, IsOnAc={isOnAc}");
                    return isOnAc;
                }
                catch (Exception ex2)
                {
                    _logging.Warn($"WinRT battery detection failed: {ex2.Message}");
                }
                
                // Method 3: Fallback to WMI
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT BatteryStatus FROM Win32_Battery");
                    
                    foreach (var obj in searcher.Get())
                    {
                        var batteryStatus = (ushort)obj["BatteryStatus"];
                        // 2 = On AC, 1 = Discharging
                        var isOnAc = batteryStatus == 2;
                        _logging.Debug($"AC detection (WMI): BatteryStatus={batteryStatus}, IsOnAc={isOnAc}");
                        return isOnAc;
                    }
                }
                catch (Exception ex3)
                {
                    _logging.Warn($"WMI battery detection failed: {ex3.Message}");
                }
            }
            
            // If all methods fail, assume AC (desktop or detection failure)
            _logging.Warn("All AC detection methods failed, assuming AC power");
            return true;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            try
            {
                _logging.Debug($"PowerModeChanged event received: Mode={e.Mode}");
                
                // Handle suspend (S0 Modern Standby) - pause hardware monitoring to prevent fan revving
                if (e.Mode == PowerModes.Suspend)
                {
                    _logging.Info("System entering suspend/standby mode");
                    try
                    {
                        SystemSuspending?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Error in SystemSuspending handler: {ex.Message}");
                    }
                    return;
                }

                // Handle resume from suspend - resume hardware monitoring
                if (e.Mode == PowerModes.Resume)
                {
                    _logging.Info("System resuming from suspend/standby");
                    try
                    {
                        SystemResuming?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Error in SystemResuming handler: {ex.Message}");
                    }
                    
                    // After resume, check if AC state changed while asleep
                    var postResumeAcState = GetCurrentAcState();
                    if (postResumeAcState != _lastKnownAcState)
                    {
#pragma warning disable CS4014
                        _ = QueueVerifiedPowerStateChangeAsync(postResumeAcState, "resume");
#pragma warning restore CS4014
                    }
                    return;
                }

                // Only respond to actual power line changes
                if (e.Mode != PowerModes.StatusChange)
                {
                    _logging.Debug($"Ignoring non-StatusChange event: {e.Mode}");
                    return;
                }

                var currentAcState = GetCurrentAcState();
                _logging.Debug($"Power status check: AC={currentAcState} (was: {_lastKnownAcState})");
                
                // Only act if state actually changed
                if (currentAcState == _lastKnownAcState)
                {
                    _logging.Debug("Power state unchanged");
                    return;
                }

#pragma warning disable CS4014
                _ = QueueVerifiedPowerStateChangeAsync(currentAcState, "status-change");
#pragma warning restore CS4014
            }
            catch (Exception ex)
            {
                _logging.Error($"Error handling power mode change: {ex.Message}", ex);
            }
        }

        private async Task QueueVerifiedPowerStateChangeAsync(bool targetAcState, string reason)
        {
            CancellationTokenSource cts;
            lock (_stateChangeLock)
            {
                _stateChangeCts?.Cancel();
                _stateChangeCts?.Dispose();
                _stateChangeCts = new CancellationTokenSource();
                cts = _stateChangeCts;
            }

            try
            {
                // Debounce transient line-state flaps (dock wobble, battery telemetry jitter).
                await Task.Delay(2500, cts.Token);

                var confirm1 = GetCurrentAcState();
                await Task.Delay(1000, cts.Token);
                var confirm2 = GetCurrentAcState();
                await Task.Delay(1000, cts.Token);
                var confirm3 = GetCurrentAcState();

                var onAcVotes = (confirm1 ? 1 : 0) + (confirm2 ? 1 : 0) + (confirm3 ? 1 : 0);
                var stableState = onAcVotes >= 2;

                if (stableState != targetAcState)
                {
                    _logging.Warn($"Ignoring transient power transition ({reason}): target={targetAcState}, sampled={confirm1}/{confirm2}/{confirm3}");
                    return;
                }

                if (stableState == _lastKnownAcState)
                {
                    _logging.Debug($"Verified power state unchanged after debounce ({reason})");
                    return;
                }

                _lastKnownAcState = stableState;
                _logging.Info($"Power state verified ({reason}): {(stableState ? "AC Connected" : "On Battery")}");
                PersistLastKnownAcState(stableState);

                // Raise event for UI updates with guarded callback execution.
                RaisePowerStateChangedSafe(stableState, reason);

                if (_isEnabled)
                {
                    _logging.Info("Power automation is enabled, applying verified profile...");
                    ApplyPowerProfile(stableState, reason);
                }
                else
                {
                    _logging.Info("Power automation is disabled, skipping profile application");
                }
            }
            catch (OperationCanceledException)
            {
                // Newer power transition superseded this pending change.
            }
            catch (Exception ex)
            {
                _logging.Warn($"Verified power-state transition failed: {ex.Message}");
            }
            finally
            {
                lock (_stateChangeLock)
                {
                    if (ReferenceEquals(_stateChangeCts, cts))
                    {
                        _stateChangeCts.Dispose();
                        _stateChangeCts = null;
                    }
                }
            }
        }

        private void RaisePowerStateChangedSafe(bool isOnAc, string source)
        {
            try
            {
                UiThreadMarshaller.BeginInvoke(() =>
                {
                    try
                    {
                        PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(isOnAc));
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"PowerStateChanged subscriber threw during {source}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to raise PowerStateChanged event ({source}): {ex.Message}");
            }
        }

        /// <summary>
        /// Apply the appropriate power profile based on AC/Battery state.
        /// </summary>
        public void ApplyPowerProfile(bool isOnAc, string transitionContext = "manual")
        {
            var targetLabel = isOnAc ? "AC" : "Battery";
            var transitionId = Guid.NewGuid().ToString("N")[..8];
            var previousFanPreset = _fanService.ActivePresetName;
            var previousPerformanceMode = _performanceModeService.GetCurrentMode();
            var failures = new List<string>();

            _logging.Info($"Applying {targetLabel} power profile [{transitionId}] (source={transitionContext})...");

            try
            {
                // Apply fan preset - look up from saved presets first to preserve curves
                var fanPresetName = isOnAc ? AcFanPreset : BatteryFanPreset;
                try
                {
                    if (string.Equals(previousFanPreset, fanPresetName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logging.Info($"  [{transitionId}] Fan preset already active: {fanPresetName} (skipped)");
                    }
                    else
                    {
                        var preset = LookupFanPreset(fanPresetName);
                        _fanService.ApplyPreset(preset);
                        _logging.Info($"  [{transitionId}] Fan preset: {fanPresetName}" + (preset.Curve?.Any() == true ? $" ({preset.Curve.Count} curve points)" : ""));
                    }
                }
                catch (Exception fanEx)
                {
                    failures.Add($"fan:{fanEx.Message}");
                    _logging.Warn($"  [{transitionId}] Fan preset apply failed ({fanPresetName}): {fanEx.Message}");
                    TryRollbackFanPreset(previousFanPreset, transitionId);
                }

                // Apply performance mode
                var perfMode = isOnAc ? AcPerformanceMode : BatteryPerformanceMode;
                try
                {
                    if (PerformanceModeNameResolver.AreEquivalent(previousPerformanceMode, perfMode))
                    {
                        _logging.Info($"  [{transitionId}] Performance mode already active: {perfMode} (skipped)");
                    }
                    else
                    {
                        // Route through SetPerformanceMode(string) rather than building a bare
                        // `new PerformanceMode { Name = perfMode }` and calling Apply() directly.
                        // A bare Name-only object carries 0W CPU / 0W GPU, which Apply()'s "both
                        // limits non-positive" guard then skips outright - so on every board that
                        // doesn't define a per-model override for this mode (57 of 59 in the
                        // database), this step silently changed nothing but the fan preset and
                        // Windows power plan. SetPerformanceMode normalizes aliases (e.g. "Silent")
                        // and supplies real generic wattage, matching what manual mode selection in
                        // the UI already does via the same method.
                        _performanceModeService.SetPerformanceMode(perfMode);
                        _logging.Info($"  [{transitionId}] Performance mode: {perfMode}");
                    }
                }
                catch (Exception perfEx)
                {
                    failures.Add($"performance:{perfEx.Message}");
                    _logging.Warn($"  [{transitionId}] Performance mode apply failed ({perfMode}): {perfEx.Message}");
                    TryRollbackPerformanceMode(previousPerformanceMode, transitionId);
                }

                // Apply GPU mode (if service available and supported)
                if (_gpuSwitchService != null && _gpuSwitchService.IsSupported)
                {
                    var gpuMode = isOnAc ? AcGpuMode : BatteryGpuMode;
                    // Note: GPU mode switching typically requires restart
                    // This just queues the change for next boot
                    _logging.Info($"  [{transitionId}] GPU mode (next boot): {gpuMode}");
                }

                if (failures.Count == 0)
                {
                    _logging.Info($"✓ {targetLabel} power profile applied [{transitionId}]");
                }
                else
                {
                    _logging.Warn($"{targetLabel} power profile applied with recoverable failures [{transitionId}]: {string.Join(" | ", failures)}");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply power profile [{transitionId}]: {ex.Message}", ex);
            }
        }

        private void TryRollbackFanPreset(string? previousPresetName, string transitionId)
        {
            if (string.IsNullOrWhiteSpace(previousPresetName))
            {
                _logging.Warn($"  [{transitionId}] Fan rollback skipped (no previous preset snapshot)");
                return;
            }

            try
            {
                var rollbackPreset = LookupFanPreset(previousPresetName);
                _fanService.ApplyPreset(rollbackPreset);
                _logging.Info($"  [{transitionId}] Fan rollback restored preset: {previousPresetName}");
            }
            catch (Exception rollbackEx)
            {
                _logging.Warn($"  [{transitionId}] Fan rollback failed ({previousPresetName}): {rollbackEx.Message}");
            }
        }

        private void TryRollbackPerformanceMode(string? previousModeName, string transitionId)
        {
            if (string.IsNullOrWhiteSpace(previousModeName))
            {
                _logging.Warn($"  [{transitionId}] Performance rollback skipped (no previous mode snapshot)");
                return;
            }

            try
            {
                // Same reasoning as the primary apply above: route through SetPerformanceMode
                // so the restored mode carries real wattage instead of a bare Name-only object.
                _performanceModeService.SetPerformanceMode(previousModeName);
                _logging.Info($"  [{transitionId}] Performance rollback restored mode: {previousModeName}");
            }
            catch (Exception rollbackEx)
            {
                _logging.Warn($"  [{transitionId}] Performance rollback failed ({previousModeName}): {rollbackEx.Message}");
            }
        }

        /// <summary>
        /// Look up a fan preset by name from saved config, falling back to a built-in definition.
        /// This preserves user-defined fan curves when switching power profiles.
        /// </summary>
        private FanPreset LookupFanPreset(string presetName)
        {
            try
            {
                // First try saved custom presets from config (these have user's curves)
                var config = _configService.Load();
                var saved = config.FanPresets?.FirstOrDefault(p => 
                    p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
                
                if (saved != null)
                {
                    _logging.Debug($"Power automation: Found saved preset '{presetName}' with {saved.Curve?.Count ?? 0} curve points");
                    return saved;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to look up preset '{presetName}' from config: {ex.Message}");
            }
            
            // Fall back to built-in preset definitions
            var mode = MapPresetNameToFanMode(presetName);
            var preset = new FanPreset 
            { 
                Name = presetName,
                Mode = mode,
                IsBuiltIn = true,
                Curve = FanModeNameResolver.BuildBuiltInCurve(presetName, mode)
            };
            
            _logging.Debug($"Power automation: Using built-in preset definition for '{presetName}'");
            return preset;
        }

        private FanMode MapPresetNameToFanMode(string presetName)
        {
            return FanModeNameResolver.ResolveBuiltInFanMode(presetName);
        }

        /// <summary>
        /// Apply the configured profile for the current power source, but only if it's actually
        /// warranted - called once at startup (see <c>MainViewModel.RestoreSavedSettingsAsync</c>).
        ///
        /// This is the resolution of the "who owns the currently-active profile, user selection or
        /// automation" question flagged in the v4.3.0 roadmap. Originally this force-applied
        /// unconditionally whenever automation was enabled, on every single startup - which meant
        /// a user who manually picked a different fan/performance preset mid-session, then closed
        /// and reopened the app on the *same* power source, had that manual choice silently
        /// discarded and replaced with automation's configured preset for a transition that never
        /// actually happened. That's the root cause GitHub #177 diagnosed as "custom fan curve not
        /// restored on restart" - not a restore bug, automation overwriting the restore a few lines
        /// later, every single time, by design.
        ///
        /// The fix: automation should own the profile at the moment of a real AC/Battery
        /// transition (including one that happened while the app was closed - see
        /// <see cref="TransitionOccurredSincePriorSession"/>), and the user's last selection should
        /// own everything else, including "the app just restarted, nothing about the power source
        /// changed." So this now only force-applies when a transition is actually known or
        /// suspected to have happened since the app last had control; otherwise it's a no-op and
        /// leaves whatever the generic last-manual-state restore (which runs immediately before
        /// this, in the same startup sequence) already put in place.
        /// </summary>
        public void ApplyCurrentProfile()
        {
            if (!_isEnabled)
            {
                return;
            }

            if (!TransitionOccurredSincePriorSession)
            {
                _logging.Info("Power automation: power source unchanged since last known state - skipping startup profile apply, keeping the user's last manual selection");
                return;
            }

            ApplyPowerProfile(_lastKnownAcState, "manual-sync");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                lock (_stateChangeLock)
                {
                    _stateChangeCts?.Cancel();
                    _stateChangeCts?.Dispose();
                    _stateChangeCts = null;
                }
                _disposed = true;
            }
        }
    }

    public class PowerStateChangedEventArgs : EventArgs
    {
        public bool IsOnAcPower { get; }

        public PowerStateChangedEventArgs(bool isOnAcPower)
        {
            IsOnAcPower = isOnAcPower;
        }
    }
}
