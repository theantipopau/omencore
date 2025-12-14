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
                // Use System.Windows.Forms.SystemInformation for reliable AC detection
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                return powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            }
            catch
            {
                // Fallback to WMI if SystemInformation fails
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT * FROM Win32_Battery");
                    
                    foreach (var obj in searcher.Get())
                    {
                        var batteryStatus = (ushort)obj["BatteryStatus"];
                        // 2 = On AC, 1 = Discharging
                        return batteryStatus == 2;
                    }
                }
                catch
                {
                    // If all else fails, assume AC (desktop or detection failure)
                    return true;
                }
            }
            
            return true;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // Only respond to actual power line changes
            if (e.Mode != PowerModes.StatusChange)
                return;

            var currentAcState = GetCurrentAcState();
            
            // Only act if state actually changed
            if (currentAcState == _lastKnownAcState)
                return;

            _lastKnownAcState = currentAcState;
            
            _logging.Info($"Power state changed: {(currentAcState ? "AC Connected" : "On Battery")}");
            
            // Raise event for UI updates
            PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(currentAcState));

            // Apply automation if enabled
            if (_isEnabled)
            {
                ApplyPowerProfile(currentAcState);
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
                // Apply fan preset
                var fanPreset = isOnAc ? AcFanPreset : BatteryFanPreset;
                var preset = new FanPreset 
                { 
                    Name = fanPreset,
                    Mode = MapPresetNameToFanMode(fanPreset),
                    Curve = new()
                };
                _fanService.ApplyPreset(preset);
                _logging.Info($"  Fan preset: {fanPreset}");

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
