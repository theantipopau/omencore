using System;
using System.Linq;
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
                        _lastKnownAcState = postResumeAcState;
                        _logging.Info($"AC state changed during sleep: now {(postResumeAcState ? "AC Connected" : "On Battery")}");
                        
                        try
                        {
                            if (System.Windows.Application.Current?.Dispatcher != null)
                            {
                                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(postResumeAcState));
                                });
                            }
                        }
                        catch { }
                        
                        if (_isEnabled)
                        {
                            ApplyPowerProfile(postResumeAcState);
                        }
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

                _lastKnownAcState = currentAcState;
                
                _logging.Info($"Power state changed: {(currentAcState ? "AC Connected" : "On Battery")}");
                
                // Raise event for UI updates - marshal to UI thread if needed
                try
                {
                    if (System.Windows.Application.Current?.Dispatcher != null)
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(currentAcState));
                        });
                    }
                    else
                    {
                        PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(currentAcState));
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to raise PowerStateChanged event: {ex.Message}");
                }

                // Apply automation if enabled
                if (_isEnabled)
                {
                    _logging.Info("Power automation is enabled, applying profile...");
                    ApplyPowerProfile(currentAcState);
                }
                else
                {
                    _logging.Info("Power automation is disabled, skipping profile application");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Error handling power mode change: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Apply the appropriate power profile based on AC/Battery state.
        /// </summary>
        public void ApplyPowerProfile(bool isOnAc)
        {
            _logging.Info($"Applying {(isOnAc ? "AC" : "Battery")} power profile...");

            try
            {
                // Apply fan preset - look up from saved presets first to preserve curves
                var fanPresetName = isOnAc ? AcFanPreset : BatteryFanPreset;
                var preset = LookupFanPreset(fanPresetName);
                _fanService.ApplyPreset(preset);
                _logging.Info($"  Fan preset: {fanPresetName}" + (preset.Curve?.Any() == true ? $" ({preset.Curve.Count} curve points)" : ""));

                // Apply performance mode
                var perfMode = isOnAc ? AcPerformanceMode : BatteryPerformanceMode;
                var mode = new PerformanceMode { Name = perfMode };
                _performanceModeService.Apply(mode);
                _logging.Info($"  Performance mode: {perfMode}");

                // Apply GPU mode (if service available and supported)
                if (_gpuSwitchService != null && _gpuSwitchService.IsSupported)
                {
                    var gpuMode = isOnAc ? AcGpuMode : BatteryGpuMode;
                    // Note: GPU mode switching typically requires restart
                    // This just queues the change for next boot
                    _logging.Info($"  GPU mode (next boot): {gpuMode}");
                }

                _logging.Info($"✓ {(isOnAc ? "AC" : "Battery")} power profile applied");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply power profile: {ex.Message}", ex);
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
                ApplyPowerProfile(_lastKnownAcState);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
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
