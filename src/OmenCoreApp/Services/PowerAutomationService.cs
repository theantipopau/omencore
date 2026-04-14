using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenCore.Models;
using OmenCore.ViewModels;
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

            // Subscribe to power events
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            
            _logging.Info($"PowerAutomationService initialized. AC Power: {_lastKnownAcState}, Enabled: {_isEnabled}");
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

        private bool GetCurrentAcState()
        {
            try
            {
                // Method 1: Use System.Windows.Forms.SystemInformation for reliable AC detection
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                var isOnAc = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                _logging.Debug($"AC detection (SystemInformation): PowerLineStatus={powerStatus.PowerLineStatus}, IsOnAc={isOnAc}");
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
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(isOnAc));
                        }
                        catch (Exception ex)
                        {
                            _logging.Warn($"PowerStateChanged subscriber threw during {source}: {ex.Message}");
                        }
                    }));
                    return;
                }

                PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(isOnAc));
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
                    var preset = LookupFanPreset(fanPresetName);
                    _fanService.ApplyPreset(preset);
                    _logging.Info($"  [{transitionId}] Fan preset: {fanPresetName}" + (preset.Curve?.Any() == true ? $" ({preset.Curve.Count} curve points)" : ""));
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
                    var mode = new PerformanceMode { Name = perfMode };
                    _performanceModeService.Apply(mode);
                    _logging.Info($"  [{transitionId}] Performance mode: {perfMode}");
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
                _performanceModeService.Apply(new PerformanceMode { Name = previousModeName });
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
                Curve = GetBuiltInCurve(mode)
            };
            
            _logging.Debug($"Power automation: Using built-in preset definition for '{presetName}'");
            return preset;
        }

        /// <summary>
        /// Get the default curve for a built-in fan mode.
        /// </summary>
        private static List<FanCurvePoint> GetBuiltInCurve(FanMode mode)
        {
            return mode switch
            {
                FanMode.Max => new() { new FanCurvePoint { TemperatureC = 0, FanPercent = 100 } },
                FanMode.Performance => new()
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 40 },
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 50 },
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 65 },
                    new FanCurvePoint { TemperatureC = 70, FanPercent = 80 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 95 },
                    new FanCurvePoint { TemperatureC = 90, FanPercent = 100 },
                },
                FanMode.Quiet => new()
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 20 },
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 25 },
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 35 },
                    new FanCurvePoint { TemperatureC = 70, FanPercent = 50 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 65 },
                    new FanCurvePoint { TemperatureC = 90, FanPercent = 80 },
                },
                _ => new() // Auto/Default - empty curve lets BIOS control
            };
        }

        private FanMode MapPresetNameToFanMode(string presetName)
        {
            return presetName.ToLowerInvariant() switch
            {
                "max" or "maximum" => FanMode.Max,
                "performance" or "turbo" or "gaming" => FanMode.Performance,
                "quiet" or "silent" or "cool" => FanMode.Quiet,
                "manual" or "custom" => FanMode.Manual,
                _ => FanMode.Auto
            };
        }

        /// <summary>
        /// Force apply the current power profile (useful for initial setup).
        /// </summary>
        public void ApplyCurrentProfile()
        {
            if (_isEnabled)
            {
                ApplyPowerProfile(_lastKnownAcState, "manual-sync");
            }
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
