using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly AppConfig _config;
        private readonly SystemInfoService _systemInfoService;
        private readonly FanCleaningService _fanCleaningService;
        private readonly BiosUpdateService _biosUpdateService;
        private readonly OmenKeyService? _omenKeyService;
        private readonly OsdService? _osdService;
        private readonly HardwareMonitoringService? _hardwareMonitoringService;
        private readonly Hardware.HpWmiBios? _wmiBios;
        private readonly PowerAutomationService? _powerAutomationService;
        private readonly ProfileExportService _profileExportService;
        private readonly DiagnosticsExportService _diagnosticsExportService;
        
        private bool _startWithWindows;
        private bool _startMinimized;
        private bool _minimizeToTrayOnClose = true;
        private int _pollingIntervalMs = 2000;
        private int _historyCount = 120;
        private bool _lowOverheadMode;
        private bool _autoCheckUpdates = true;
        private int _updateCheckIntervalIndex = 2; // Daily
        private bool _includePreReleases;
        private bool _hotkeysEnabled = true;
        private bool _notificationsEnabled = true;
        private bool _gameNotificationsEnabled = true;
        private bool _modeChangeNotificationsEnabled = true;
        private bool _temperatureWarningsEnabled = true;
        
        // Power automation fields
        private bool _powerAutomationEnabled;
        private string _acFanPreset = "Auto";
        private string _acPerformanceMode = "Balanced";
        private string _batteryFanPreset = "Quiet";
        private string _batteryPerformanceMode = "Silent";
        
        private string _fanCleaningStatusText = "Checking hardware...";
        private string _fanCleaningStatusIcon = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2Z";
        private Brush _fanCleaningStatusColor = Brushes.Gray;
        private bool _canStartFanCleaning;
        private bool _isFanCleaningActive;
        private string _fanCleaningProgress = "";
        private int _fanCleaningProgressPercent;
        private CancellationTokenSource? _fanCleaningCts;
        
        // Driver status fields
        private string _driverStatusText = "Checking...";
        private string _driverStatusDetail = "";
        private Brush _driverStatusColor = Brushes.Gray;
        
        // System status fields for Settings view
        private string _fanBackend = "Detecting...";
        private bool _secureBootEnabled;
        private bool _pawnIOAvailable;
        private bool _oghInstalled;
        
        // BIOS update fields
        private string _systemModel = "";
        private string _currentBiosVersion = "";
        private string _latestBiosVersion = "";
        private bool _biosUpdateAvailable;
        private string _biosDownloadUrl = "";
        private string _biosCheckStatus = "Not checked";
        private bool _isBiosCheckInProgress;
        private Brush _biosStatusColor = Brushes.Gray;
        
        // Fan service for EC reset
        private readonly FanService? _fanService;

        public SettingsViewModel(LoggingService logging, ConfigurationService configService, 
            SystemInfoService systemInfoService, FanCleaningService fanCleaningService,
            BiosUpdateService biosUpdateService,
            ProfileExportService profileExportService,
            DiagnosticsExportService diagnosticsExportService,
            Hardware.HpWmiBios? wmiBios = null,
            OmenKeyService? omenKeyService = null,
            OsdService? osdService = null,
            HardwareMonitoringService? hardwareMonitoringService = null,
            PowerAutomationService? powerAutomationService = null,
            FanService? fanService = null)
        {
            _logging = logging;
            _configService = configService;
            _config = configService.Config;
            _systemInfoService = systemInfoService;
            _fanCleaningService = fanCleaningService;
            _biosUpdateService = biosUpdateService;
            _fanService = fanService;
            _wmiBios = wmiBios;
            _omenKeyService = omenKeyService;
            _osdService = osdService;
            _hardwareMonitoringService = hardwareMonitoringService;
            _powerAutomationService = powerAutomationService;
            _profileExportService = profileExportService;
            _diagnosticsExportService = diagnosticsExportService;

            // Load saved settings
            LoadSettings();
            
            // Initialize power status
            RefreshPowerStatus();

            // Initialize commands
            OpenConfigFolderCommand = new RelayCommand(_ => OpenConfigFolder());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
            OpenGitHubCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore"));
            OpenReleaseNotesCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore/releases"));
            OpenIssuesCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore/issues"));
            OpenDonateCommand = new RelayCommand(_ => OpenUrl("https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD"));
            OpenHpSupportAssistantCommand = new RelayCommand(_ => OpenUrl("https://support.hp.com/drivers"));
            OpenHpDriversPageCommand = new RelayCommand(_ => OpenUrl("https://support.hp.com/drivers"));
            OpenOmenGamingHubCommand = new RelayCommand(_ => OpenUrl("https://apps.microsoft.com/detail/9nqdw009t0t0"));
            StartFanCleaningCommand = new AsyncRelayCommand(async _ => await StartFanCleaningAsync(), _ => CanStartFanCleaning && !IsFanCleaningActive);
            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());
            InstallDriverCommand = new RelayCommand(_ => InstallDriver());
            RefreshDriverStatusCommand = new RelayCommand(_ => CheckDriverStatus());
            CheckBiosUpdatesCommand = new AsyncRelayCommand(async _ => await CheckBiosUpdatesAsync(), _ => !IsBiosCheckInProgress);
            DownloadBiosUpdateCommand = new RelayCommand(_ => DownloadBiosUpdate(), _ => BiosUpdateAvailable && !string.IsNullOrEmpty(BiosDownloadUrl));
            ScanBloatwareCommand = new AsyncRelayCommand(async _ => await ScanBloatwareAsync(), _ => !IsScanningBloatware);
            RemoveBloatwareCommand = new AsyncRelayCommand(async _ => await RemoveBloatwareAsync(), _ => !IsScanningBloatware && BloatwareCount > 0);
            ResetEcToDefaultsCommand = new RelayCommand(_ => ResetEcToDefaults(), _ => _fanService != null);
            ImportProfileCommand = new AsyncRelayCommand(async _ => await ImportProfileAsync());
            ExportProfileCommand = new AsyncRelayCommand(async _ => await ExportProfileAsync());
            ExportDiagnosticsCommand = new AsyncRelayCommand(async _ => await ExportDiagnosticsAsync());
            RefreshBiosReliabilityCommand = new RelayCommand(_ => RefreshBiosReliability());

            // Check fan cleaning availability
            CheckFanCleaningAvailability();
            
            // Check driver status
            CheckDriverStatus();
            
            // Load system status for Settings page
            LoadSystemStatus();
            
            // Load system info for BIOS
            LoadSystemInfo();
        }

        #region General Settings

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (_startWithWindows != value)
                {
                    _startWithWindows = value;
                    OnPropertyChanged();
                    SetStartWithWindows(value);
                }
            }
        }

        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (_startMinimized != value)
                {
                    _startMinimized = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool MinimizeToTrayOnClose
        {
            get => _minimizeToTrayOnClose;
            set
            {
                if (_minimizeToTrayOnClose != value)
                {
                    _minimizeToTrayOnClose = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Monitoring Settings

        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set
            {
                if (_pollingIntervalMs != value)
                {
                    _pollingIntervalMs = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public int HistoryCount
        {
            get => _historyCount;
            set
            {
                if (_historyCount != value)
                {
                    _historyCount = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool LowOverheadMode
        {
            get => _lowOverheadMode;
            set
            {
                if (_lowOverheadMode != value)
                {
                    _lowOverheadMode = value;
                    OnPropertyChanged();
                    
                    // Apply immediately to monitoring service
                    _hardwareMonitoringService?.SetLowOverheadMode(value);
                    _logging.Info($"Low overhead mode {(value ? "enabled" : "disabled")}");
                    
                    // Raise event for MainViewModel to update MonitoringGraphsVisible
                    LowOverheadModeChanged?.Invoke(this, value);
                    
                    SaveSettings();
                }
            }
        }
        
        /// <summary>
        /// Event raised when low overhead mode changes, so MainViewModel can update UI
        /// </summary>
        public event EventHandler<bool>? LowOverheadModeChanged;

        #endregion

        #region Power Automation Settings

        public bool PowerAutomationEnabled
        {
            get => _powerAutomationEnabled;
            set
            {
                if (_powerAutomationEnabled != value)
                {
                    _powerAutomationEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Sync with PowerAutomationService at runtime
                    if (_powerAutomationService != null)
                    {
                        _powerAutomationService.IsEnabled = value;
                        _logging.Info($"PowerAutomationService.IsEnabled synced to {value}");
                    }
                }
            }
        }

        public string AcFanPreset
        {
            get => _acFanPreset;
            set
            {
                if (_acFanPreset != value)
                {
                    _acFanPreset = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Sync with PowerAutomationService at runtime
                    if (_powerAutomationService != null)
                    {
                        _powerAutomationService.AcFanPreset = value;
                    }
                }
            }
        }

        public string AcPerformanceMode
        {
            get => _acPerformanceMode;
            set
            {
                if (_acPerformanceMode != value)
                {
                    _acPerformanceMode = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Sync with PowerAutomationService at runtime
                    if (_powerAutomationService != null)
                    {
                        _powerAutomationService.AcPerformanceMode = value;
                    }
                }
            }
        }

        public string BatteryFanPreset
        {
            get => _batteryFanPreset;
            set
            {
                if (_batteryFanPreset != value)
                {
                    _batteryFanPreset = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Sync with PowerAutomationService at runtime
                    if (_powerAutomationService != null)
                    {
                        _powerAutomationService.BatteryFanPreset = value;
                    }
                }
            }
        }

        public string BatteryPerformanceMode
        {
            get => _batteryPerformanceMode;
            set
            {
                if (_batteryPerformanceMode != value)
                {
                    _batteryPerformanceMode = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Sync with PowerAutomationService at runtime
                    if (_powerAutomationService != null)
                    {
                        _powerAutomationService.BatteryPerformanceMode = value;
                    }
                }
            }
        }

        public string[] FanPresetOptions => new[] { "Auto", "Quiet", "Extreme", "Max" };
        public string[] PerformanceModeOptions => new[] { "Silent", "Balanced", "Performance", "Turbo" };

        private string _currentPowerStatus = "Unknown";
        public string CurrentPowerStatus
        {
            get => _currentPowerStatus;
            private set
            {
                if (_currentPowerStatus != value)
                {
                    _currentPowerStatus = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Refresh the current power status display
        /// </summary>
        public void RefreshPowerStatus()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                CurrentPowerStatus = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online
                    ? "⚡ AC Power Connected"
                    : "🔋 On Battery";
            }
            catch
            {
                CurrentPowerStatus = "Unknown";
            }
        }

        #endregion

        #region Update Settings

        public bool AutoCheckUpdates
        {
            get => _autoCheckUpdates;
            set
            {
                if (_autoCheckUpdates != value)
                {
                    _autoCheckUpdates = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public int UpdateCheckIntervalIndex
        {
            get => _updateCheckIntervalIndex;
            set
            {
                if (_updateCheckIntervalIndex != value)
                {
                    _updateCheckIntervalIndex = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool IncludePreReleases
        {
            get => _includePreReleases;
            set
            {
                if (_includePreReleases != value)
                {
                    _includePreReleases = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Hotkeys Settings

        public bool HotkeysEnabled
        {
            get => _hotkeysEnabled;
            set
            {
                if (_hotkeysEnabled != value)
                {
                    _hotkeysEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Notifications Settings

        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set
            {
                if (_notificationsEnabled != value)
                {
                    _notificationsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool GameNotificationsEnabled
        {
            get => _gameNotificationsEnabled;
            set
            {
                if (_gameNotificationsEnabled != value)
                {
                    _gameNotificationsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ModeChangeNotificationsEnabled
        {
            get => _modeChangeNotificationsEnabled;
            set
            {
                if (_modeChangeNotificationsEnabled != value)
                {
                    _modeChangeNotificationsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool TemperatureWarningsEnabled
        {
            get => _temperatureWarningsEnabled;
            set
            {
                if (_temperatureWarningsEnabled != value)
                {
                    _temperatureWarningsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Feature Toggles

        public bool CorsairIntegrationEnabled
        {
            get => _config.Features?.CorsairIntegrationEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.CorsairIntegrationEnabled != value)
                {
                    _config.Features.CorsairIntegrationEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool LogitechIntegrationEnabled
        {
            get => _config.Features?.LogitechIntegrationEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.LogitechIntegrationEnabled != value)
                {
                    _config.Features.LogitechIntegrationEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool GameProfilesEnabled
        {
            get => _config.Features?.GameProfilesEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.GameProfilesEnabled != value)
                {
                    _config.Features.GameProfilesEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool KeyboardLightingEnabled
        {
            get => _config.Features?.KeyboardLightingEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.KeyboardLightingEnabled != value)
                {
                    _config.Features.KeyboardLightingEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool FanCurvesEnabled
        {
            get => _config.Features?.FanCurvesEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.FanCurvesEnabled != value)
                {
                    _config.Features.FanCurvesEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool FeaturePowerAutomationEnabled
        {
            get => _config.Features?.PowerAutomationEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.PowerAutomationEnabled != value)
                {
                    _config.Features.PowerAutomationEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        public bool AmbientLightingEnabled
        {
            get => _config.AmbientLighting?.Enabled ?? false;
            set
            {
                if (_config.AmbientLighting == null) _config.AmbientLighting = new AmbientLightingSettings();
                if (_config.AmbientLighting.Enabled != value)
                {
                    _config.AmbientLighting.Enabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        public float AmbientLightingSaturation
        {
            get => _config.AmbientLighting?.SaturationBoost ?? 1.2f;
            set
            {
                if (_config.AmbientLighting == null) _config.AmbientLighting = new AmbientLightingSettings();
                if (Math.Abs(_config.AmbientLighting.SaturationBoost - value) > 0.01f)
                {
                    _config.AmbientLighting.SaturationBoost = Math.Clamp(value, 0.5f, 2.0f);
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        public int AmbientLightingFps
        {
            get
            {
                var interval = _config.AmbientLighting?.UpdateIntervalMs ?? 33;
                return interval > 0 ? 1000 / interval : 30;
            }
            set
            {
                if (_config.AmbientLighting == null) _config.AmbientLighting = new AmbientLightingSettings();
                var newInterval = value > 0 ? 1000 / value : 33;
                if (_config.AmbientLighting.UpdateIntervalMs != newInterval)
                {
                    _config.AmbientLighting.UpdateIntervalMs = Math.Clamp(newInterval, 16, 200);
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool GpuSwitchingEnabled
        {
            get => _config.Features?.GpuSwitchingEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.GpuSwitchingEnabled != value)
                {
                    _config.Features.GpuSwitchingEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool UndervoltEnabled
        {
            get => _config.Features?.UndervoltEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.UndervoltEnabled != value)
                {
                    _config.Features.UndervoltEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool TrayTempDisplayEnabled
        {
            get => _config.Features?.TrayTempDisplayEnabled ?? true;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.TrayTempDisplayEnabled != value)
                {
                    _config.Features.TrayTempDisplayEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    _logging.Info($"Tray temperature display {(value ? "enabled" : "disabled")}");
                    
                    // Immediately refresh tray icon to reflect the change
                    App.TrayIcon?.RefreshTrayIcon();
                }
            }
        }

        /// <summary>
        /// When true, OmenCore will attempt direct HID access for Corsair devices
        /// and will *not* fall back to iCUE. This may improve iCUE-free operation
        /// for some devices but can reduce compatibility on others.
        /// </summary>
        public bool CorsairDisableIcueFallback
        {
            get => _config.CorsairDisableIcueFallback;
            set
            {
                if (_config.CorsairDisableIcueFallback != value)
                {
                    _config.CorsairDisableIcueFallback = value;
                    OnPropertyChanged();
                    SaveSettings();
                    _logging.Info($"Corsair iCUE fallback {(value ? "disabled (HID-only mode)" : "enabled (iCUE fallback allowed)")}");
                }
            }
        }

        public bool TelemetryEnabled
        {
            get => _config.TelemetryEnabled;
            set
            {
                if (_config.TelemetryEnabled != value)
                {
                    _config.TelemetryEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    _logging.Info($"Telemetry {(value ? "enabled" : "disabled")} by user");
                }
            }
        }

        public bool OmenKeyInterceptionEnabled
        {
            get => _config.Features?.OmenKeyInterceptionEnabled ?? false;
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.OmenKeyInterceptionEnabled != value)
                {
                    _config.Features.OmenKeyInterceptionEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Start or stop the OMEN key service at runtime
                    if (_omenKeyService != null)
                    {
                        if (value)
                        {
                            _omenKeyService.StartInterception();
                            _logging.Info("OMEN key interception started (user enabled)");
                        }
                        else
                        {
                            _omenKeyService.StopInterception();
                            _logging.Info("OMEN key interception stopped (user disabled)");
                        }
                    }
                }
            }
        }

        public string OmenKeyAction
        {
            get => _config.Features?.OmenKeyAction ?? "ShowQuickPopup";
            set
            {
                if (_config.Features == null) _config.Features = new FeaturePreferences();
                if (_config.Features.OmenKeyAction != value)
                {
                    _config.Features.OmenKeyAction = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public string[] OmenKeyActionOptions => new[] { "ShowQuickPopup", "ShowWindow", "ToggleFanMode", "TogglePerformanceMode" };

        public bool ExperimentalEcKeyboardEnabled
        {
            get => _config.ExperimentalEcKeyboardEnabled;
            set
            {
                if (_config.ExperimentalEcKeyboardEnabled != value)
                {
                    // Show warning dialog before enabling
                    if (value)
                    {
                        var result = MessageBox.Show(
                            "🛑 DANGER: HIGH-RISK EXPERIMENTAL FEATURE 🛑\n\n" +
                            "Direct EC keyboard writes can cause:\n" +
                            "• HARD SYSTEM CRASHES requiring forced restart\n" +
                            "• Screen brightness stuck at minimum (black screen)\n" +
                            "• Keyboard/display controller malfunction\n" +
                            "• Other unpredictable EC-related issues\n\n" +
                            "EC keyboard registers (0xB2-0xBE) vary by laptop model. Writing to wrong addresses " +
                            "has caused system crashes and display issues on various OMEN models.\n\n" +
                            "If your screen goes black after enabling this:\n" +
                            "1. Restart your laptop (hold power 10 seconds)\n" +
                            "2. Delete config: %APPDATA%\\OmenCore\\config.json\n" +
                            "3. Or reinstall OmenCore\n\n" +
                            "Only enable this if WMI keyboard lighting doesn't work on your model.\n\n" +
                            "ENABLE AT YOUR OWN RISK?",
                            "⚠️ Experimental Feature Warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Stop,
                            MessageBoxResult.No);
                        
                        if (result != MessageBoxResult.Yes)
                        {
                            return; // User cancelled
                        }
                        
                        _logging.Warn("⚠️ User enabled experimental EC keyboard writes - system crash/display risk!");
                    }
                    
                    _config.ExperimentalEcKeyboardEnabled = value;
                    
                    // Update the static flag immediately so EC writes work without restart
                    Hardware.PawnIOEcAccess.EnableExperimentalKeyboardWrites = value;
                    Hardware.WinRing0EcAccess.EnableExperimentalKeyboardWrites = value;
                    _logging.Info($"EC keyboard writes flag updated at runtime: {value}");
                    
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        public bool ExclusiveEcAccessDiagnosticsEnabled
        {
            get => _config.ExclusiveEcAccessDiagnosticsEnabled;
            set
            {
                if (_config.ExclusiveEcAccessDiagnosticsEnabled != value)
                {
                    // Show warning dialog before enabling
                    if (value)
                    {
                        var result = MessageBox.Show(
                            "⚠️ EXCLUSIVE EC ACCESS DIAGNOSTICS ⚠️\n\n" +
                            "This diagnostic mode will:\n" +
                            "• Acquire exclusive access to EC registers\n" +
                            "• Block other applications from accessing EC\n" +
                            "• Log detailed contention information\n" +
                            "• May interfere with OMEN Gaming Hub or other fan control apps\n\n" +
                            "Use this only for troubleshooting EC register conflicts.\n" +
                            "It will help identify if other apps are causing fan RPM 0 issues.\n\n" +
                            "Enable exclusive diagnostics mode?",
                            "Exclusive EC Access Diagnostics",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No);
                        
                        if (result != MessageBoxResult.Yes)
                        {
                            return; // User cancelled
                        }
                        
                        _logging.Warn("⚠️ Exclusive EC access diagnostics enabled - may interfere with other apps");
                    }
                    
                    _config.ExclusiveEcAccessDiagnosticsEnabled = value;
                    
                    // Update the static flags immediately
                    Hardware.PawnIOEcAccess.EnableExclusiveEcAccessDiagnostics = value;
                    Hardware.WinRing0EcAccess.EnableExclusiveEcAccessDiagnostics = value;
                    _logging.Info($"Exclusive EC diagnostics flag updated at runtime: {value}");
                    
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        /// <summary>
        /// Available keyboard backend options for the dropdown.
        /// </summary>
        public string[] KeyboardBackendOptions => new[] { "Auto", "WmiBios", "Wmi", "Ec" };
        
        /// <summary>
        /// User's preferred keyboard backend. Forces specific backend if available.
        /// </summary>
        public string PreferredKeyboardBackend
        {
            get => _config.PreferredKeyboardBackend;
            set
            {
                if (_config.PreferredKeyboardBackend != value)
                {
                    // Warn if selecting EC without experimental enabled
                    if (value == "Ec" && !_config.ExperimentalEcKeyboardEnabled)
                    {
                        var result = MessageBox.Show(
                            "⚠️ EC Backend Selected ⚠️\n\n" +
                            "Using EC for keyboard control requires 'Enable EC keyboard' to be checked.\n\n" +
                            "Would you like to enable experimental EC keyboard support?\n" +
                            "(Warning: This may cause system crashes on some models)",
                            "Enable EC Keyboard?",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            // Enable EC first, then set backend
                            _config.ExperimentalEcKeyboardEnabled = true;
                            OnPropertyChanged(nameof(ExperimentalEcKeyboardEnabled));
                        }
                        else
                        {
                            return; // User cancelled, don't change backend
                        }
                    }
                    
                    _config.PreferredKeyboardBackend = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    _logging.Info($"Keyboard backend preference changed to: {value}");
                    
                    // Note: Service restart required for change to take effect
                    MessageBox.Show(
                        $"Keyboard backend set to: {value}\n\n" +
                        "This change will take effect after restarting OmenCore.",
                        "Backend Changed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
        
        /// <summary>
        /// Invert RGB zone order for OMEN Max 16 light bar (zones run right-to-left).
        /// When enabled: Zone 1 = Right, Zone 4 = Left
        /// </summary>
        public bool InvertRgbZoneOrder
        {
            get => _config.InvertRgbZoneOrder;
            set
            {
                if (_config.InvertRgbZoneOrder != value)
                {
                    _config.InvertRgbZoneOrder = value;
                    OnPropertyChanged();
                    SaveSettings();
                    _logging.Info($"RGB zone order inversion: {(value ? "Enabled" : "Disabled")} (for light bar compatibility)");
                }
            }
        }

        #endregion

        #region OSD Settings

        public bool OsdEnabled
        {
            get => _config.Osd?.Enabled ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                if (_config.Osd.Enabled != value)
                {
                    _config.Osd.Enabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    
                    // Enable or disable the OSD service at runtime
                    if (_osdService != null)
                    {
                        if (value)
                        {
                            _osdService.Initialize();
                            _logging.Info("OSD overlay initialized (user enabled)");
                        }
                        else
                        {
                            _osdService.Shutdown();
                            _logging.Info("OSD overlay shutdown (user disabled)");
                        }
                    }
                }
            }
        }

        public string OsdPosition
        {
            get => _config.Osd?.Position ?? "TopLeft";
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                if (_config.Osd.Position != value)
                {
                    _config.Osd.Position = value;
                    OnPropertyChanged();
                    SaveSettings();
                    NotifyOsdSettingsChanged();
                }
            }
        }

        public string[] OsdPositionOptions => new[] { "TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight" };

        public string OsdHotkey
        {
            get => _config.Osd?.ToggleHotkey ?? "F12";
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                if (_config.Osd.ToggleHotkey != value)
                {
                    _config.Osd.ToggleHotkey = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        /// <summary>
        /// Notify OSD service that settings have changed so it can update the overlay live
        /// </summary>
        private void NotifyOsdSettingsChanged()
        {
            _osdService?.UpdateSettings();
        }

        public bool OsdShowCpuTemp
        {
            get => _config.Osd?.ShowCpuTemp ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowCpuTemp = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowGpuTemp
        {
            get => _config.Osd?.ShowGpuTemp ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowGpuTemp = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowCpuLoad
        {
            get => _config.Osd?.ShowCpuLoad ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowCpuLoad = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowGpuLoad
        {
            get => _config.Osd?.ShowGpuLoad ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowGpuLoad = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowFanSpeed
        {
            get => _config.Osd?.ShowFanSpeed ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowFanSpeed = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowRamUsage
        {
            get => _config.Osd?.ShowRamUsage ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowRamUsage = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowFps
        {
            get => _config.Osd?.ShowFps ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowFps = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowFanMode
        {
            get => _config.Osd?.ShowFanMode ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowFanMode = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowPerformanceMode
        {
            get => _config.Osd?.ShowPerformanceMode ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowPerformanceMode = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowFrametime
        {
            get => _config.Osd?.ShowFrametime ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowFrametime = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowTime
        {
            get => _config.Osd?.ShowTime ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowTime = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowGpuPower
        {
            get => _config.Osd?.ShowGpuPower ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowGpuPower = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowCpuPower
        {
            get => _config.Osd?.ShowCpuPower ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowCpuPower = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowNetworkLatency
        {
            get => _config.Osd?.ShowNetworkLatency ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowNetworkLatency = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowVramUsage
        {
            get => _config.Osd?.ShowVramUsage ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowVramUsage = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowNetworkUpload
        {
            get => _config.Osd?.ShowNetworkUpload ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowNetworkUpload = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowNetworkDownload
        {
            get => _config.Osd?.ShowNetworkDownload ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowNetworkDownload = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowBattery
        {
            get => _config.Osd?.ShowBattery ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowBattery = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowCpuClock
        {
            get => _config.Osd?.ShowCpuClock ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowCpuClock = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        public bool OsdShowGpuClock
        {
            get => _config.Osd?.ShowGpuClock ?? false;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowGpuClock = value;
                OnPropertyChanged();
                SaveSettings();
                NotifyOsdSettingsChanged();
            }
        }

        /// <summary>
        /// OSD Layout: "Vertical" (default) or "Horizontal"
        /// </summary>
        public string OsdLayout
        {
            get => _config.Osd?.Layout ?? "Vertical";
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                if (_config.Osd.Layout != value)
                {
                    _config.Osd.Layout = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOsdHorizontalLayout));
                    SaveSettings();
                    NotifyOsdSettingsChanged();
                }
            }
        }

        /// <summary>
        /// Helper for checkbox binding - true when layout is horizontal
        /// </summary>
        public bool IsOsdHorizontalLayout
        {
            get => OsdLayout == "Horizontal";
            set
            {
                OsdLayout = value ? "Horizontal" : "Vertical";
            }
        }

        #endregion

        #region Battery Settings

        public bool BatteryChargeLimitEnabled
        {
            get => _config.Battery?.ChargeLimitEnabled ?? false;
            set
            {
                if (_config.Battery == null) _config.Battery = new BatterySettings();
                if (_config.Battery.ChargeLimitEnabled != value)
                {
                    _config.Battery.ChargeLimitEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                    // Apply battery care mode immediately
                    ApplyBatteryChargeLimit(value, BatteryChargeThresholdPercent);
                }
            }
        }

        public int BatteryChargeThresholdPercent
        {
            get => _config.Battery?.ChargeThresholdPercent ?? 80;
            set
            {
                if (_config.Battery == null) _config.Battery = new BatterySettings();
                var clamped = Math.Clamp(value, 60, 100);
                if (_config.Battery.ChargeThresholdPercent != clamped)
                {
                    _config.Battery.ChargeThresholdPercent = clamped;
                    OnPropertyChanged();
                    SaveSettings();
                    // Apply if enabled
                    if (BatteryChargeLimitEnabled)
                    {
                        ApplyBatteryChargeLimit(true, clamped);
                    }
                }
            }
        }

        private void ApplyBatteryChargeLimit(bool enabled, int thresholdPercent = 80)
        {
            try
            {
                if (_wmiBios == null)
                {
                    _logging.Warn("Cannot apply battery charge limit: WMI BIOS not available");
                    System.Windows.MessageBox.Show(
                        "Battery charge limit cannot be set - HP WMI BIOS interface not available.\n\n" +
                        "This feature requires HP OMEN/Victus laptop with WMI BIOS support.",
                        "Battery Care Unavailable",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                // Set battery care mode via WMI BIOS
                // When enabled: limit charging to 80%
                // When disabled: allow full charge to 100%
                bool success = _wmiBios.SetBatteryCareMode(enabled);
                
                if (success)
                {
                    // Verify by reading back the setting
                    var currentMode = _wmiBios.GetBatteryCareMode();
                    if (currentMode == enabled)
                    {
                        _logging.Info($"✓ Battery charge limit: {(enabled ? $"Enabled {thresholdPercent}% limit" : "Disabled (full charge)")}");
                    }
                    else
                    {
                        _logging.Warn($"⚠ Battery charge limit command succeeded but verification failed. " +
                                      $"Requested: {(enabled ? "80%" : "100%")}, Current: {(currentMode == true ? "80%" : currentMode == false ? "100%" : "unknown")}");
                    }
                }
                else
                {
                    _logging.Warn("Battery charge limit WMI command returned failure");
                    System.Windows.MessageBox.Show(
                        "Battery charge limit command failed.\n\n" +
                        "This may indicate:\n" +
                        "• Your laptop model doesn't support this feature\n" +
                        "• The HP BIOS needs an update\n" +
                        "• AC adapter is not connected\n\n" +
                        "Try toggling the setting in OMEN Gaming Hub to compare behavior.",
                        "Battery Care Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply battery charge limit: {ex.Message}", ex);
                System.Windows.MessageBox.Show(
                    $"Failed to apply battery charge limit: {ex.Message}",
                    "Battery Care Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        #endregion

        #region Fan Hysteresis Settings

        public bool FanHysteresisEnabled
        {
            get => _config.FanHysteresis?.Enabled ?? true;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                if (_config.FanHysteresis.Enabled != value)
                {
                    _config.FanHysteresis.Enabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double FanHysteresisDeadZone
        {
            get => _config.FanHysteresis?.DeadZone ?? 3.0;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                if (Math.Abs(_config.FanHysteresis.DeadZone - value) > 0.01)
                {
                    _config.FanHysteresis.DeadZone = Math.Max(0, Math.Min(10, value));
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double FanHysteresisRampUpDelay
        {
            get => _config.FanHysteresis?.RampUpDelay ?? 0.5;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                if (Math.Abs(_config.FanHysteresis.RampUpDelay - value) > 0.01)
                {
                    _config.FanHysteresis.RampUpDelay = Math.Max(0, Math.Min(10, value));
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double FanHysteresisRampDownDelay
        {
            get => _config.FanHysteresis?.RampDownDelay ?? 3.0;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                if (Math.Abs(_config.FanHysteresis.RampDownDelay - value) > 0.01)
                {
                    _config.FanHysteresis.RampDownDelay = Math.Max(0, Math.Min(30, value));
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Thermal protection threshold in °C. Default 90°C, range 70-95°C.
        /// Fans will ramp up when temps exceed this threshold.
        /// v2.8.0: Raised max from 90 to 95 for high-power laptops that naturally run hot.
        /// </summary>
        public double ThermalProtectionThreshold
        {
            get => _config.FanHysteresis?.ThermalProtectionThreshold ?? 90.0;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                var clamped = Math.Max(70, Math.Min(95, value));
                if (Math.Abs(_config.FanHysteresis.ThermalProtectionThreshold - clamped) > 0.1)
                {
                    _config.FanHysteresis.ThermalProtectionThreshold = clamped;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Enable/disable thermal protection override.
        /// When disabled, fans will NEVER be automatically overridden by thermal protection.
        /// The user takes full responsibility for thermal management.
        /// </summary>
        public bool ThermalProtectionEnabled
        {
            get => _config.FanHysteresis?.ThermalProtectionEnabled ?? true;
            set
            {
                if (_config.FanHysteresis == null) _config.FanHysteresis = new FanHysteresisSettings();
                if (_config.FanHysteresis.ThermalProtectionEnabled != value)
                {
                    _config.FanHysteresis.ThermalProtectionEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }
        
        public int MaxFanLevelOverride
        {
            get => _config.MaxFanLevelOverride;
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_config.MaxFanLevelOverride != clamped)
                {
                    _config.MaxFanLevelOverride = clamped;
                    OnPropertyChanged();
                    SaveSettings();
                    _logging.Info($"Max fan level override set to {clamped} (0 = auto-detect, requires restart)");
                }
            }
        }

        #endregion

        #region Fan Cleaning

        public string FanCleaningStatusText
        {
            get => _fanCleaningStatusText;
            set { _fanCleaningStatusText = value; OnPropertyChanged(); }
        }

        public string FanCleaningStatusIcon
        {
            get => _fanCleaningStatusIcon;
            set { _fanCleaningStatusIcon = value; OnPropertyChanged(); }
        }

        public Brush FanCleaningStatusColor
        {
            get => _fanCleaningStatusColor;
            set { _fanCleaningStatusColor = value; OnPropertyChanged(); }
        }

        public bool CanStartFanCleaning
        {
            get => _canStartFanCleaning;
            set 
            { 
                _canStartFanCleaning = value; 
                OnPropertyChanged();
                (StartFanCleaningCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsFanCleaningActive
        {
            get => _isFanCleaningActive;
            set 
            { 
                _isFanCleaningActive = value; 
                OnPropertyChanged();
                (StartFanCleaningCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string FanCleaningProgress
        {
            get => _fanCleaningProgress;
            set { _fanCleaningProgress = value; OnPropertyChanged(); }
        }

        public int FanCleaningProgressPercent
        {
            get => _fanCleaningProgressPercent;
            set { _fanCleaningProgressPercent = value; OnPropertyChanged(); }
        }

        #endregion

        #region Data & About

        public string ConfigFolderPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenCore");

        public string LogFolderPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore");

        public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>()?.Version ?? "1.0.0.0";

        #endregion

        #region Commands

        public ICommand OpenConfigFolderCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand OpenGitHubCommand { get; }
        public ICommand OpenReleaseNotesCommand { get; }
        public ICommand OpenIssuesCommand { get; }
        public ICommand OpenDonateCommand { get; }
        public ICommand OpenHpSupportAssistantCommand { get; }
        public ICommand OpenHpDriversPageCommand { get; }
        public ICommand OpenOmenGamingHubCommand { get; }
        public ICommand StartFanCleaningCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand InstallDriverCommand { get; }
        public ICommand RefreshDriverStatusCommand { get; }
        public ICommand CheckBiosUpdatesCommand { get; }
        public ICommand DownloadBiosUpdateCommand { get; }
        public ICommand ResetEcToDefaultsCommand { get; }
        public ICommand ImportProfileCommand { get; }
        public ICommand ExportProfileCommand { get; }
        public ICommand ExportDiagnosticsCommand { get; }
        public ICommand RefreshBiosReliabilityCommand { get; }

        #endregion
        
        #region Driver Status Properties
        
        public string DriverStatusText
        {
            get => _driverStatusText;
            set { _driverStatusText = value; OnPropertyChanged(); }
        }
        
        public string DriverStatusDetail
        {
            get => _driverStatusDetail;
            set { _driverStatusDetail = value; OnPropertyChanged(); }
        }
        
        public Brush DriverStatusColor
        {
            get => _driverStatusColor;
            set { _driverStatusColor = value; OnPropertyChanged(); }
        }
        
        #endregion
        
        #region System Status Properties (for Settings page)
        
        public string FanBackend
        {
            get => _fanBackend;
            set { _fanBackend = value; OnPropertyChanged(); }
        }
        
        public bool SecureBootEnabled
        {
            get => _secureBootEnabled;
            set { _secureBootEnabled = value; OnPropertyChanged(); }
        }
        
        public bool PawnIOAvailable
        {
            get => _pawnIOAvailable;
            set { _pawnIOAvailable = value; OnPropertyChanged(); }
        }
        
        public bool OghInstalled
        {
            get => _oghInstalled;
            set { _oghInstalled = value; OnPropertyChanged(); }
        }
        
        private string _oghDetectionDetail = "";
        public string OghDetectionDetail
        {
            get => _oghDetectionDetail;
            set { _oghDetectionDetail = value; OnPropertyChanged(); }
        }
        
        // Standalone status properties (v2.7.0)
        private string _standaloneStatus = "Checking...";
        private string _standaloneStatusColor = "Gray";
        private string _standaloneStatusSummary = "";
        private DependencyAudit? _dependencyAudit;
        
        public string StandaloneStatus
        {
            get => _standaloneStatus;
            set { _standaloneStatus = value; OnPropertyChanged(); }
        }
        
        public string StandaloneStatusColor
        {
            get => _standaloneStatusColor;
            set { _standaloneStatusColor = value; OnPropertyChanged(); }
        }
        
        public string StandaloneStatusSummary
        {
            get => _standaloneStatusSummary;
            set { _standaloneStatusSummary = value; OnPropertyChanged(); }
        }
        
        public DependencyAudit? DependencyAudit
        {
            get => _dependencyAudit;
            set { _dependencyAudit = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Refresh the standalone dependency audit.
        /// </summary>
        public void RefreshStandaloneStatus()
        {
            try
            {
                _systemInfoService.ClearAuditCache();
                var audit = _systemInfoService.PerformDependencyAudit();
                DependencyAudit = audit;
                StandaloneStatus = audit.StatusText;
                StandaloneStatusColor = audit.StatusColor;
                StandaloneStatusSummary = audit.Summary;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to perform dependency audit: {ex.Message}", ex);
                StandaloneStatus = "Error";
                StandaloneStatusColor = "#FF6B6B";
                StandaloneStatusSummary = $"Audit failed: {ex.Message}";
            }
        }
        
        // PawnIO-Only Mode (v2.7.0)
        private bool _pawnIOOnlyMode;
        
        /// <summary>
        /// Force PawnIO-only backend mode. Disables HP service-dependent features.
        /// </summary>
        public bool PawnIOOnlyMode
        {
            get => _pawnIOOnlyMode;
            set 
            { 
                if (_pawnIOOnlyMode != value)
                {
                    _pawnIOOnlyMode = value; 
                    OnPropertyChanged();
                    
                    // Persist to config
                    if (_config?.Features != null)
                    {
                        _config.Features.PawnIOOnlyMode = value;
                        _configService.Save(_config);
                        _logging.Info($"PawnIO-Only Mode: {(value ? "Enabled" : "Disabled")}");
                    }
                }
            }
        }
        
        // BIOS Reliability Stats (v2.7.0)
        private Hardware.BiosReliabilityStats? _biosReliabilityStats;
        
        /// <summary>
        /// BIOS WMI query reliability statistics.
        /// </summary>
        public Hardware.BiosReliabilityStats? BiosReliabilityStats
        {
            get => _biosReliabilityStats;
            private set { _biosReliabilityStats = value; OnPropertyChanged(); OnPropertyChanged(nameof(BiosReliabilityText)); OnPropertyChanged(nameof(BiosReliabilityColor)); }
        }
        
        /// <summary>
        /// Summary text for BIOS reliability.
        /// </summary>
        public string BiosReliabilityText => _biosReliabilityStats?.Summary ?? "Not available";
        
        /// <summary>
        /// Color for BIOS reliability indicator.
        /// </summary>
        public string BiosReliabilityColor => _biosReliabilityStats?.HealthRating switch
        {
            "Excellent" => "#4CAF50",
            "Good" => "#8BC34A",
            "Fair" => "#FFC107",
            "Poor" => "#FF9800",
            "Critical" => "#F44336",
            _ => "#9E9E9E"
        };
        
        /// <summary>
        /// Refresh BIOS reliability statistics.
        /// </summary>
        public void RefreshBiosReliability()
        {
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                BiosReliabilityStats = _wmiBios.GetReliabilityStats();
            }
        }
        
        #endregion
        
        #region BIOS Update Properties
        
        public string SystemModel
        {
            get => _systemModel;
            set { _systemModel = value; OnPropertyChanged(); }
        }
        
        public string CurrentBiosVersion
        {
            get => _currentBiosVersion;
            set { _currentBiosVersion = value; OnPropertyChanged(); }
        }
        
        public string LatestBiosVersion
        {
            get => _latestBiosVersion;
            set { _latestBiosVersion = value; OnPropertyChanged(); }
        }
        
        public bool BiosUpdateAvailable
        {
            get => _biosUpdateAvailable;
            set 
            { 
                _biosUpdateAvailable = value; 
                OnPropertyChanged();
                (DownloadBiosUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        
        public string BiosDownloadUrl
        {
            get => _biosDownloadUrl;
            set 
            { 
                _biosDownloadUrl = value; 
                OnPropertyChanged();
                (DownloadBiosUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        
        public string BiosCheckStatus
        {
            get => _biosCheckStatus;
            set { _biosCheckStatus = value; OnPropertyChanged(); }
        }
        
        public bool IsBiosCheckInProgress
        {
            get => _isBiosCheckInProgress;
            set 
            { 
                _isBiosCheckInProgress = value; 
                OnPropertyChanged();
                (CheckBiosUpdatesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        
        public Brush BiosStatusColor
        {
            get => _biosStatusColor;
            set { _biosStatusColor = value; OnPropertyChanged(); }
        }
        
        #endregion

        #region Private Methods

        private void LoadSettings()
        {
            // Load from config
            _pollingIntervalMs = _config.Monitoring.PollIntervalMs;
            _historyCount = _config.Monitoring.HistoryCount;
            _lowOverheadMode = _config.Monitoring.LowOverheadMode;
            _autoCheckUpdates = _config.Updates?.AutoCheckEnabled ?? true;
            _includePreReleases = _config.Updates?.IncludePreReleases ?? false;
            // Map CheckIntervalHours to dropdown index: 0=Every startup, 1=Every 6 hours, 2=Daily(12h), 3=Weekly
            _updateCheckIntervalIndex = _config.Updates?.CheckIntervalHours switch
            {
                0 => 0,      // Every startup
                6 => 1,      // Every 6 hours
                12 => 2,     // Daily
                168 => 3,    // Weekly
                _ => 2       // Default to daily
            };
            
            // Load hotkey and notification settings
            _hotkeysEnabled = _config.Monitoring.HotkeysEnabled;
            _notificationsEnabled = _config.Monitoring.NotificationsEnabled;
            _gameNotificationsEnabled = _config.Monitoring.GameNotificationsEnabled;
            _modeChangeNotificationsEnabled = _config.Monitoring.ModeChangeNotificationsEnabled;
            _temperatureWarningsEnabled = _config.Monitoring.TemperatureWarningsEnabled;
            
            // Load UI preferences
            _startMinimized = _config.Monitoring.StartMinimized;
            _minimizeToTrayOnClose = _config.Monitoring.MinimizeToTrayOnClose;
            
            // Load power automation settings
            _powerAutomationEnabled = _config.PowerAutomation?.Enabled ?? false;
            _acFanPreset = _config.PowerAutomation?.AcFanPreset ?? "Auto";
            _acPerformanceMode = _config.PowerAutomation?.AcPerformanceMode ?? "Balanced";
            _batteryFanPreset = _config.PowerAutomation?.BatteryFanPreset ?? "Quiet";
            _batteryPerformanceMode = _config.PowerAutomation?.BatteryPerformanceMode ?? "Silent";

            // Check startup status - first check Task Scheduler, then fall back to registry
            _startWithWindows = CheckStartupTaskExists() || CheckStartupRegistryExists();
            
            // Load PawnIO-Only Mode (v2.7.0)
            _pawnIOOnlyMode = _config.Features?.PawnIOOnlyMode ?? false;
            
            _logging.Info($"Settings loaded: Hotkeys={_hotkeysEnabled}, Notifications={_notificationsEnabled}, PowerAutomation={_powerAutomationEnabled}, PawnIOOnly={_pawnIOOnlyMode}");
        }

        private void SaveSettings()
        {
            _config.Monitoring.PollIntervalMs = _pollingIntervalMs;
            _config.Monitoring.HistoryCount = _historyCount;
            _config.Monitoring.LowOverheadMode = _lowOverheadMode;
            
            // Save hotkey and notification settings
            _config.Monitoring.HotkeysEnabled = _hotkeysEnabled;
            _config.Monitoring.NotificationsEnabled = _notificationsEnabled;
            _config.Monitoring.GameNotificationsEnabled = _gameNotificationsEnabled;
            _config.Monitoring.ModeChangeNotificationsEnabled = _modeChangeNotificationsEnabled;
            _config.Monitoring.TemperatureWarningsEnabled = _temperatureWarningsEnabled;
            
            // Save UI preferences
            _config.Monitoring.StartMinimized = _startMinimized;
            _config.Monitoring.MinimizeToTrayOnClose = _minimizeToTrayOnClose;
            
            // Save power automation settings
            _config.PowerAutomation ??= new PowerAutomationSettings();
            _config.PowerAutomation.Enabled = _powerAutomationEnabled;
            _config.PowerAutomation.AcFanPreset = _acFanPreset;
            _config.PowerAutomation.AcPerformanceMode = _acPerformanceMode;
            _config.PowerAutomation.BatteryFanPreset = _batteryFanPreset;
            _config.PowerAutomation.BatteryPerformanceMode = _batteryPerformanceMode;
            
            if (_config.Updates == null)
                _config.Updates = new UpdatePreferences();
            _config.Updates.AutoCheckEnabled = _autoCheckUpdates;
            _config.Updates.IncludePreReleases = _includePreReleases;
            // Map dropdown index to hours: 0=Every startup(0), 1=Every 6 hours(6), 2=Daily(12), 3=Weekly(168)
            _config.Updates.CheckIntervalHours = _updateCheckIntervalIndex switch
            {
                0 => 0,      // Every startup
                1 => 6,      // Every 6 hours  
                2 => 12,     // Daily
                3 => 168,    // Weekly
                _ => 12      // Default to daily
            };

            _configService.Save(_config);
        }
        
        /// <summary>
        /// Check if OmenCore scheduled task exists for startup.
        /// </summary>
        private bool CheckStartupTaskExists()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/query /tn \"OmenCore\" /fo list",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                
                // If exit code is 0 and output contains task name, task exists
                var taskExists = process.ExitCode == 0 && output.Contains("OmenCore");
                if (taskExists)
                {
                    _logging.Info("Startup task 'OmenCore' found in Task Scheduler");
                }
                return taskExists;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not check Task Scheduler for startup task: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if OmenCore registry entry exists for startup (fallback method).
        /// </summary>
        [SupportedOSPlatform("windows")]
        private bool CheckStartupRegistryExists()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                var exists = key?.GetValue("OmenCore") != null;
                if (exists)
                {
                    _logging.Info("Startup registry entry 'OmenCore' found");
                }
                return exists;
            }
            catch
            {
                return false;
            }
        }

        private void SetStartWithWindows(bool enable)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                
                // Use Task Scheduler for elevated startup (required for hardware access)
                // Don't use registry Run key - it causes double startup with installer shortcut
                var taskName = "OmenCore";
                
                // Always clean up old startup methods first to avoid duplicates
                CleanupOldStartupMethods(exePath);
                
                if (enable)
                {
                    // First, try to remove any existing task
                    try
                    {
                        var deleteProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "schtasks",
                                Arguments = $"/delete /tn \"{taskName}\" /f",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };
                        deleteProcess.Start();
                        deleteProcess.WaitForExit(3000);
                    }
                    catch { /* Task may not exist, ignore */ }
                    
                    // Create scheduled task with highest privileges (runs as admin on logon)
                    // Use --minimized flag to start minimized to tray
                    var createProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    createProcess.Start();
                    var output = createProcess.StandardOutput.ReadToEnd();
                    var error = createProcess.StandardError.ReadToEnd();
                    createProcess.WaitForExit(5000);
                    
                    if (createProcess.ExitCode == 0)
                    {
                        _logging.Info($"Created scheduled task '{taskName}' for elevated startup");
                        // NOTE: Don't add registry fallback - it causes double startup issues
                        
                        // Verify the task was created successfully
                        if (CheckStartupTaskExists())
                        {
                            _logging.Info("✓ Startup task verified in Task Scheduler");
                        }
                    }
                    else
                    {
                        _logging.Warn($"Task Scheduler creation returned exit code {createProcess.ExitCode}: {error}");
                        
                        // If task creation failed, it's likely because we're not elevated
                        // Try to show a helpful message
                        var isElevated = System.Security.Principal.WindowsIdentity.GetCurrent()
                            .Owner?.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid) == true;
                        
                        if (!isElevated)
                        {
                            _logging.Warn("OmenCore is not running as administrator. To enable auto-start, please:");
                            _logging.Warn("1. Right-click OmenCore and 'Run as administrator'");
                            _logging.Warn("2. Enable 'Start with Windows' again");
                            _logging.Warn("Alternatively, use the installer with the 'Start with Windows' option checked.");
                            
                            // Reset the checkbox since creation failed
                            _startWithWindows = false;
                            OnPropertyChanged(nameof(StartWithWindows));
                            
                            System.Windows.MessageBox.Show(
                                "Auto-start requires administrator privileges to create a scheduled task.\n\n" +
                                "To enable auto-start:\n" +
                                "1. Right-click OmenCore and select 'Run as administrator'\n" +
                                "2. Go to Settings and enable 'Start with Windows' again\n\n" +
                                "Or, re-run the installer with the 'Start with Windows' option checked.",
                                "Administrator Required",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        }
                        else
                        {
                            _logging.Warn("Auto-start task creation failed despite being elevated. Check Windows Event Log for details.");
                        }
                    }
                }
                else
                {
                    // Remove scheduled task
                    try
                    {
                        var deleteProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "schtasks",
                                Arguments = $"/delete /tn \"{taskName}\" /f",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };
                        deleteProcess.Start();
                        deleteProcess.WaitForExit(3000);
                        _logging.Info($"Removed scheduled task '{taskName}'");
                    }
                    catch (Exception taskEx)
                    {
                        _logging.Warn($"Could not remove scheduled task: {taskEx.Message}");
                    }
                    
                    _logging.Info("Removed OmenCore from Windows startup");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to modify startup settings", ex);
            }
        }
        
        /// <summary>
        /// Remove old startup methods that may cause duplicate launches.
        /// </summary>
        private void CleanupOldStartupMethods(string exePath)
        {
            try
            {
                // Remove registry Run entry if it exists (old method)
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue("OmenCore", false);
                
                // Remove startup folder shortcut if it exists (installer method)
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, "OmenCore.lnk");
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    _logging.Info("Removed old startup folder shortcut");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not clean up old startup methods: {ex.Message}");
            }
        }

        private void OpenConfigFolder()
        {
            try
            {
                var path = ConfigFolderPath;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to open config folder", ex);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var path = LogFolderPath;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to open log folder", ex);
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to open URL: {url}", ex);
            }
        }

        private void CheckFanCleaningAvailability()
        {
            var systemInfo = _systemInfoService.GetSystemInfo();
            
            if (systemInfo.IsHpGaming)
            {
                var canClean = _fanCleaningService.IsSupported;
                if (canClean)
                {
                    FanCleaningStatusText = $"✓ HP OMEN detected: {systemInfo.Model}. Fan cleaning available.";
                    FanCleaningStatusIcon = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"; // Checkmark
                    FanCleaningStatusColor = new SolidColorBrush(Color.FromRgb(129, 199, 132)); // Green
                    CanStartFanCleaning = true;
                }
                else
                {
                    var reason = _fanCleaningService.UnsupportedReason;
                    FanCleaningStatusText = $"⚠️ {reason}";
                    FanCleaningStatusIcon = "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z"; // Warning
                    FanCleaningStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
                    CanStartFanCleaning = false;
                    _logging.Info($"Fan cleaning unavailable: {reason}");
                }
            }
            else
            {
                FanCleaningStatusText = $"✗ Non-HP OMEN system detected: {systemInfo.Manufacturer} {systemInfo.Model}. Fan cleaning unavailable.";
                FanCleaningStatusIcon = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"; // X
                FanCleaningStatusColor = new SolidColorBrush(Color.FromRgb(229, 115, 115)); // Red
                CanStartFanCleaning = false;
            }
        }

        private async Task StartFanCleaningAsync()
        {
            var result = MessageBox.Show(
                "This will run the fan cleaning cycle for 20 seconds.\n\n" +
                "⚠️ IMPORTANT:\n" +
                "• Place your laptop on a flat, stable surface\n" +
                "• Ensure vents are not blocked\n" +
                "• The fans will briefly reverse direction\n" +
                "• You may hear unusual fan sounds - this is normal\n\n" +
                "Continue with fan cleaning?",
                "Fan Dust Cleaning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            IsFanCleaningActive = true;
            _fanCleaningCts = new CancellationTokenSource();

            try
            {
                _logging.Info("Starting fan cleaning cycle");
                
                await _fanCleaningService.StartCleaningAsync(
                    progress =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            FanCleaningProgress = progress.Message;
                            FanCleaningProgressPercent = progress.ProgressPercent;
                        });
                    },
                    _fanCleaningCts.Token);

                _logging.Info("Fan cleaning cycle completed successfully");
                MessageBox.Show("Fan cleaning cycle completed successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                _logging.Info("Fan cleaning was cancelled");
            }
            catch (Exception ex)
            {
                _logging.Error("Fan cleaning failed", ex);
                MessageBox.Show($"Fan cleaning failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsFanCleaningActive = false;
                FanCleaningProgress = "";
                FanCleaningProgressPercent = 0;
                _fanCleaningCts?.Dispose();
                _fanCleaningCts = null;
            }
        }

        private void ResetToDefaults()
        {
            var result = MessageBox.Show(
                "This will reset all settings to their default values.\n\n" +
                "This includes:\n" +
                "• Monitoring settings\n" +
                "• Notification preferences\n" +
                "• Hotkey settings\n" +
                "• Update preferences\n\n" +
                "Continue with reset?",
                "Reset to Defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Reset all settings to defaults
                PollingIntervalMs = 2000;
                HistoryCount = 120;
                LowOverheadMode = false;
                StartMinimized = false;
                MinimizeToTrayOnClose = true;
                AutoCheckUpdates = true;
                UpdateCheckIntervalIndex = 2; // Daily
                IncludePreReleases = false;
                HotkeysEnabled = true;
                NotificationsEnabled = true;
                GameNotificationsEnabled = true;
                ModeChangeNotificationsEnabled = true;
                TemperatureWarningsEnabled = true;

                SaveSettings();
                
                _logging.Info("Settings reset to defaults");
                MessageBox.Show("All settings have been reset to their default values.", 
                    "Settings Reset", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to reset settings", ex);
                MessageBox.Show($"Failed to reset settings: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CheckDriverStatus()
        {
            try
            {
                var secureBootEnabled = IsSecureBootEnabled();
                var memoryIntegrityEnabled = IsMemoryIntegrityEnabled();

                var pawnIoAvailable = IsPawnIOAvailable();

                // Check WinRing0 driver - try multiple device paths
                var devicePaths = new[] { @"\\.\WinRing0_1_2_0", @"\\.\WinRing0_1_2", @"\\.\WinRing0" };
                bool winRing0Available = false;
                
                foreach (var devicePath in devicePaths)
                {
                    try
                    {
                        var handle = NativeMethods.CreateFile(
                            devicePath,
                            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            3, // OPEN_EXISTING
                            0,
                            IntPtr.Zero);

                        if (!handle.IsInvalid && !handle.IsClosed)
                        {
                            winRing0Available = true;
                            handle.Close();
                            break;
                        }
                    }
                    catch { }
                }
                
                // Check for XTU service conflict (check SERVICES not processes)
                bool xtuRunning = false;
                string? xtuServiceName = null;
                try
                {
                    var xtuServices = new[] { "XTU3SERVICE", "XtuService", "IntelXtuService" };
                    foreach (var svc in xtuServices)
                    {
                        try
                        {
                            using var sc = new System.ServiceProcess.ServiceController(svc);
                            // Check if service exists and is running
                            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                            {
                                xtuRunning = true;
                                xtuServiceName = svc;
                                break;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Service doesn't exist - expected if XTU not installed
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Service doesn't exist or access denied
                        }
                    }
                }
                catch { }

                if (pawnIoAvailable)
                {
                    DriverStatusText = "PawnIO Installed";
                    DriverStatusDetail = "✓ Secure Boot compatible driver backend available (recommended). Won't trigger Windows Defender false positives.";
                    DriverStatusColor = new SolidColorBrush(Color.FromRgb(102, 187, 106)); // Green
                }
                else if (winRing0Available)
                {
                    if (xtuRunning)
                    {
                        DriverStatusText = "WinRing0 Detected (XTU Conflict)";
                        DriverStatusDetail = $"Intel XTU service ({xtuServiceName}) may block undervolting. Stop XTU to use OmenCore undervolting. Consider migrating to PawnIO. (Note: WinRing0 may trigger Defender false positives - see FAQ)";
                        DriverStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
                    }
                    else
                    {
                        DriverStatusText = "WinRing0 Detected (Legacy)";
                        DriverStatusDetail = "Legacy driver backend working. Note: May trigger Windows Defender false positives (known issue with hardware monitoring tools). PawnIO recommended for Secure Boot systems.";
                        DriverStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange-yellow (warn about legacy)
                    }
                }
                else
                {
                    DriverStatusText = "No Driver Backend Detected";

                    if (secureBootEnabled || memoryIntegrityEnabled)
                    {
                        var reasons = new List<string>();
                        if (secureBootEnabled) reasons.Add("Secure Boot is enabled");
                        if (memoryIntegrityEnabled) reasons.Add("Memory Integrity (HVCI) is enabled");

                        DriverStatusDetail =
                            $"Legacy drivers blocked ({string.Join(", ", reasons)}). " +
                            "Install PawnIO (pawnio.eu) for Secure Boot compatible MSR/EC access - avoids Defender false positives and works with security features enabled.";
                    }
                    else
                    {
                        DriverStatusDetail =
                            "Install PawnIO (recommended - no Defender false positives) or run OmenCore as Administrator to initialize WinRing0.";
                    }

                    DriverStatusColor = new SolidColorBrush(Color.FromRgb(239, 83, 80)); // Red
                }
            }
            catch (Exception ex)
            {
                DriverStatusText = "Check Failed";
                DriverStatusDetail = ex.Message;
                DriverStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
            }
        }

        private static bool IsPawnIOAvailable()
        {
            try
            {
                // Registry check
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key != null)
                    return true;

                // Default install path
                var defaultPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO", "PawnIOLib.dll");
                if (System.IO.File.Exists(defaultPath))
                    return true;

                // Driver loaded check
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_SystemDriver WHERE Name LIKE '%PawnIO%'");
                return searcher.Get().Count > 0;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsSecureBootEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                var value = key?.GetValue("UEFISecureBootEnabled");
                return value != null && Convert.ToInt32(value) == 1;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsMemoryIntegrityEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                var value = key?.GetValue("Enabled");
                return value != null && Convert.ToInt32(value) == 1;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Reset EC (Embedded Controller) to factory defaults.
        /// Shows a confirmation dialog before proceeding.
        /// </summary>
        private void ResetEcToDefaults()
        {
            var result = MessageBox.Show(
                "This will reset the Embedded Controller (EC) to factory defaults.\n\n" +
                "This action will:\n" +
                "• Restore BIOS control of fans\n" +
                "• Clear all manual fan speed overrides\n" +
                "• Reset fan boost mode\n" +
                "• Reset thermal policy timers\n\n" +
                "Use this if:\n" +
                "• Fan speeds appear stuck in BIOS\n" +
                "• BIOS shows incorrect fan readings\n" +
                "• You want to completely restore factory fan behavior\n\n" +
                "The app will apply Auto mode after reset.\n\n" +
                "Do you want to continue?",
                "Reset EC to Defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes)
                return;
            
            try
            {
                _logging.Info("User initiated EC reset to defaults...");
                
                if (_fanService == null)
                {
                    MessageBox.Show(
                        "Fan service is not available. EC reset cannot be performed.",
                        "EC Reset Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                
                var success = _fanService.ResetEcToDefaults();
                
                if (success)
                {
                    MessageBox.Show(
                        "EC has been reset to factory defaults.\n\n" +
                        "BIOS should now have full control of fans.\n" +
                        "If your BIOS displays still show incorrect values,\n" +
                        "try restarting your laptop.",
                        "EC Reset Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    _logging.Info("EC reset to defaults completed successfully");
                }
                else
                {
                    MessageBox.Show(
                        "EC reset may have partially completed.\n\n" +
                        "Some systems may require a full restart\n" +
                        "for all changes to take effect.",
                        "EC Reset Result",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    
                    _logging.Warn("EC reset returned false - may have partially succeeded");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"EC reset failed: {ex.Message}", ex);
                MessageBox.Show(
                    $"Failed to reset EC: {ex.Message}",
                    "EC Reset Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void InstallDriver()
        {
            // Show info about what will be installed
            var result = MessageBox.Show(
                "OmenCore recommends PawnIO for advanced hardware features.\n\n" +
                "PawnIO provides Secure Boot compatible MSR/EC access for:\n" +
                "• CPU undervolting (Intel/AMD)\n" +
                "• TCC offset control\n" +
                "• EC-based fan control fallback\n\n" +
                "Click OK to open the PawnIO website.",
                "Install PawnIO Driver", 
                MessageBoxButton.OKCancel, 
                MessageBoxImage.Information);
                
            if (result != MessageBoxResult.OK)
                return;
            
            // Open PawnIO website (recommended for all systems)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://pawnio.eu/",
                    UseShellExecute = true
                });
                
                // Refresh status after install attempt
                Task.Delay(5000).ContinueWith(_ => 
                    Application.Current.Dispatcher.Invoke(CheckDriverStatus));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadSystemStatus()
        {
            try
            {
                // Check Secure Boot
                SecureBootEnabled = IsSecureBootEnabled();
                
                // Check PawnIO availability
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                    PawnIOAvailable = key != null;
                }
                catch
                {
                    PawnIOAvailable = false;
                }
                
                // Check OGH installation - use ServiceController to check if services are actually running
                try
                {
                    OghInstalled = false;
                    var detectedItems = new System.Collections.Generic.List<string>();
                    
                    // Check for OGH services (most reliable)
                    var oghServiceNames = new[] { "HPOmenCap", "HPOmenCommandCenter" };
                    foreach (var serviceName in oghServiceNames)
                    {
                        try
                        {
                            using var sc = new System.ServiceProcess.ServiceController(serviceName);
                            // Only count if service is RUNNING, not just installed
                            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                            {
                                OghInstalled = true;
                                detectedItems.Add($"Service: {serviceName} (running)");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Service doesn't exist - expected after uninstall
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Service doesn't exist or access denied
                        }
                    }
                    
                    // Also check for running processes as backup
                    var processes = new[] { "OmenCommandCenterBackground", "OmenCap", "omenmqtt" };
                    foreach (var proc in processes)
                    {
                        try
                        {
                            var procs = System.Diagnostics.Process.GetProcessesByName(proc);
                            if (procs.Length > 0)
                            {
                                OghInstalled = true;
                                // Get the process path for more detail
                                var path = "";
                                try { path = procs[0].MainModule?.FileName ?? ""; } catch { }
                                var detail = string.IsNullOrEmpty(path) ? $"Process: {proc}" : $"Process: {proc} ({path})";
                                detectedItems.Add(detail);
                                foreach (var p in procs) p.Dispose();
                            }
                        }
                        catch { }
                    }
                    
                    OghDetectionDetail = detectedItems.Count > 0 
                        ? string.Join("\n", detectedItems) 
                        : "Not detected";
                }
                catch
                {
                    OghInstalled = false;
                }
                
                // Determine fan backend
                if (PawnIOAvailable)
                    FanBackend = "WMI BIOS + PawnIO";
                else if (OghInstalled)
                    FanBackend = "WMI BIOS (OGH)";
                else if (!SecureBootEnabled)
                    FanBackend = "WMI BIOS + Legacy EC";
                else
                    FanBackend = "WMI BIOS";
                    
                // Perform standalone dependency audit (v2.7.0)
                RefreshStandaloneStatus();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load system status: {ex.Message}");
                FanBackend = "Unknown";
            }
        }
        
        private void LoadSystemInfo()
        {
            try
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                // Use Model for display (full name like "HP OMEN by HP Laptop 17-ck2xxx")
                SystemModel = !string.IsNullOrEmpty(sysInfo.Model) ? sysInfo.Model : sysInfo.ProductName ?? "Unknown HP Product";
                CurrentBiosVersion = sysInfo.BiosVersion ?? "Unknown";
                BiosCheckStatus = "Click 'Check for Updates' to check HP for BIOS updates";
                BiosStatusColor = Brushes.Gray;
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to load system info for BIOS display", ex);
                SystemModel = "Unable to detect";
                CurrentBiosVersion = "Unable to detect";
                BiosCheckStatus = "Could not read system information";
                BiosStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
            }
        }
        
        private async Task CheckBiosUpdatesAsync()
        {
            if (IsBiosCheckInProgress)
                return;
                
            IsBiosCheckInProgress = true;
            BiosCheckStatus = "Checking HP for BIOS updates...";
            BiosStatusColor = Brushes.Gray;
            
            try
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                var result = await _biosUpdateService.CheckForUpdatesAsync(sysInfo);
                
                LatestBiosVersion = result.LatestBiosVersion ?? "Unknown";
                BiosDownloadUrl = result.DownloadUrl ?? "";
                BiosUpdateAvailable = result.UpdateAvailable;
                
                if (result.UpdateAvailable)
                {
                    BiosCheckStatus = $"⬆️ Update available: {result.LatestBiosVersion}";
                    BiosStatusColor = new SolidColorBrush(Color.FromRgb(102, 187, 106)); // Green
                }
                else if (!string.IsNullOrEmpty(result.LatestBiosVersion))
                {
                    BiosCheckStatus = "✓ Your BIOS is up to date";
                    BiosStatusColor = new SolidColorBrush(Color.FromRgb(102, 187, 106)); // Green
                }
                else
                {
                    BiosCheckStatus = result.Message;
                    BiosStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to check for BIOS updates", ex);
                BiosCheckStatus = $"Error: {ex.Message}";
                BiosStatusColor = new SolidColorBrush(Color.FromRgb(239, 83, 80)); // Red
            }
            finally
            {
                IsBiosCheckInProgress = false;
            }
        }
        
        private void DownloadBiosUpdate()
        {
            if (string.IsNullOrEmpty(BiosDownloadUrl))
                return;
                
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = BiosDownloadUrl,
                    UseShellExecute = true
                });
                _logging.Info($"Opened BIOS download URL: {BiosDownloadUrl}");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to open BIOS download URL", ex);
                // Fallback to HP support page
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://support.hp.com/drivers",
                    UseShellExecute = true
                });
            }
        }

        #endregion
        
        #region HP Bloatware Removal
        
        private bool _isScanningBloatware;
        private int _bloatwareCount;
        private string _bloatwareList = "";
        private string _bloatwareProgress = "";
        
        public bool IsScanningBloatware
        {
            get => _isScanningBloatware;
            set { _isScanningBloatware = value; OnPropertyChanged(); }
        }
        
        public int BloatwareCount
        {
            get => _bloatwareCount;
            set { _bloatwareCount = value; OnPropertyChanged(); }
        }
        
        public string BloatwareList
        {
            get => _bloatwareList;
            set { _bloatwareList = value; OnPropertyChanged(); }
        }
        
        public string BloatwareProgress
        {
            get => _bloatwareProgress;
            set { _bloatwareProgress = value; OnPropertyChanged(); }
        }
        
        public ICommand ScanBloatwareCommand { get; }
        public ICommand RemoveBloatwareCommand { get; }
        
        private async Task ScanBloatwareAsync()
        {
            if (IsScanningBloatware) return;
            
            IsScanningBloatware = true;
            BloatwareProgress = "Scanning for HP bloatware packages...";
            BloatwareList = "Scanning for HP bloatware...";
            BloatwareCount = 0;
            
            try
            {
                var bloatware = new System.Collections.Generic.List<string>();
                
                // Common HP bloatware package names — comprehensive list for Omen/Victus/HP systems
                var bloatwarePackages = new[]
                {
                    "AD2F1837.HPSystemEventUtility",
                    "AD2F1837.HPAudioSwitch", 
                    "AD2F1837.HPConnectionOptimizer",
                    "AD2F1837.HPDocumentation",
                    "AD2F1837.HPJumpStarts",
                    "AD2F1837.HPPrivacySettings",
                    "AD2F1837.HPQuickDrop",
                    "AD2F1837.HPSureClick",
                    "AD2F1837.HPSureRun",
                    "AD2F1837.HPSureSense",
                    "AD2F1837.HPTouchpointManager",
                    "AD2F1837.myHP",
                    "AD2F1837.HPEnhancedLighting",
                    "AD2F1837.HPSmart",
                    "AD2F1837.HPPCHardwareDiagnosticsWindows",
                    "AD2F1837.HPDesktopSupportUtilities",
                    "AD2F1837.HPInc.EnergyStar",
                    "AD2F1837.HPWorkWell",
                    "AD2F1837.HPAccessoryCenter",
                    "AD2F1837.HPSystemInformation",
                    "AD2F1837.HPQuickTouch",
                    "AD2F1837.HPPowerManager",
                    "AD2F1837.OMENCommandCenter",
                    "AD2F1837.OMENCommandCenterDev",
                    "AD2F1837.OMENCommandCenter_Beta",
                    "HPInc.HPGamingHub",
                };
                
                await Task.Run(() =>
                {
                    // Scan for all HP AppX packages (AD2F1837.HP*, AD2F1837.OMEN*, HPInc.*)
                    // Do NOT include Realtek - those are audio/network drivers, not bloatware
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage | Where-Object { $_.Name -like 'AD2F1837.HP*' -or $_.Name -like 'AD2F1837.OMEN*' -or $_.Name -like 'AD2F1837.myHP*' -or $_.Name -like 'HPInc.HP*' } | Select-Object -ExpandProperty Name\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var errors = process.StandardError.ReadToEnd();
                        process.WaitForExit(30000);
                        
                        if (!string.IsNullOrWhiteSpace(errors))
                        {
                            _logging.Warn($"Bloatware scan stderr: {errors.Trim()}");
                        }
                        
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !bloatware.Contains(trimmed))
                            {
                                // Never include HP Support Assistant — it's useful
                                if (trimmed.Contains("HPSupportAssistant", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                    
                                bloatware.Add(trimmed);
                            }
                        }
                    }
                });
                
                BloatwareCount = bloatware.Count;
                
                if (bloatware.Count == 0)
                {
                    BloatwareList = "✓ No HP bloatware detected!\n\nYour system is clean.";
                }
                else
                {
                    BloatwareList = $"Found {bloatware.Count} HP bloatware package(s):\n\n" +
                                  string.Join("\n", bloatware.Select(p => $"• {p.Replace("AD2F1837.", "").Replace("HPInc.", "")}"));
                }
                
                _logging.Info($"Bloatware scan complete: {bloatware.Count} package(s) found");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to scan for bloatware", ex);
                BloatwareList = $"❌ Scan failed: {ex.Message}";
            }
            finally
            {
                IsScanningBloatware = false;
            }
        }
        
        private async Task RemoveBloatwareAsync()
        {
            if (BloatwareCount == 0)
            {
                MessageBox.Show("No bloatware detected. Run a scan first.", "OmenCore", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show(
                $"This will remove {BloatwareCount} HP bloatware package(s).\n\n" +
                "⚠️ WARNING:\n" +
                "• This action cannot be undone\n" +
                "• Some HP features may stop working\n" +
                "• HP Support Assistant will NOT be removed\n" +
                "• Realtek audio/network drivers will NOT be touched\n" +
                "• System restart may be required\n\n" +
                "Continue with removal?",
                "Remove HP Bloatware - Confirmation Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            IsScanningBloatware = true;
            BloatwareProgress = "Starting bloatware removal...";
            BloatwareList = "Removing HP bloatware packages...\n\nThis may take a few minutes...";
            
            try
            {
                var removed = 0;
                var failed = 0;
                var failedNames = new System.Collections.Generic.List<string>();
                var packageNames = new System.Collections.Generic.List<string>();
                
                // First get the list of packages to remove — matches scan filter exactly
                BloatwareProgress = "[1/3] Getting package list...";
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage | Where-Object { ($_.Name -like 'AD2F1837.HP*' -or $_.Name -like 'AD2F1837.OMEN*' -or $_.Name -like 'AD2F1837.myHP*' -or $_.Name -like 'HPInc.HP*') -and $_.Name -notlike '*HPSupportAssistant*' } | Select-Object -ExpandProperty Name\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(30000);
                        packageNames.AddRange(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));
                    }
                });
                
                BloatwareProgress = $"[2/3] Removing {packageNames.Count} packages...";
                var total = packageNames.Count;
                var current = 0;
                
                // Remove each package individually with progress
                // Use -ErrorAction SilentlyContinue to handle provisioned packages gracefully
                foreach (var packageName in packageNames)
                {
                    current++;
                    var friendlyName = packageName.Replace("AD2F1837.", "").Replace("HPInc.", "");
                    BloatwareProgress = $"[2/3] Removing {current}/{total}: {friendlyName}...";
                    
                    await Task.Run(() =>
                    {
                        // Try normal removal first, then try with -AllUsers for provisioned packages
                        var psi = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"" +
                                $"try {{ Get-AppxPackage -Name '{packageName}' | Remove-AppxPackage -ErrorAction Stop }} " +
                                $"catch {{ " +
                                $"  try {{ Get-AppxPackage -AllUsers -Name '{packageName}' | Remove-AppxPackage -AllUsers -ErrorAction Stop }} " +
                                $"  catch {{ " +
                                $"    try {{ Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq '{packageName}' }} | Remove-AppxProvisionedPackage -Online -ErrorAction Stop }} " +
                                $"    catch {{ throw $_ }} " +
                                $"  }} " +
                                $"}}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        
                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            var stdout = process.StandardOutput.ReadToEnd();
                            var stderr = process.StandardError.ReadToEnd();
                            process.WaitForExit(60000);
                            
                            if (process.ExitCode == 0 || string.IsNullOrWhiteSpace(stderr))
                            {
                                removed++;
                                _logging.Info($"Removed: {packageName}");
                            }
                            else
                            {
                                // Check if it's an access denied error
                                if (stderr.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                                    stderr.Contains("0x80070005", StringComparison.OrdinalIgnoreCase))
                                {
                                    failed++;
                                    failedNames.Add($"{friendlyName} (needs admin)");
                                    _logging.Warn($"Access denied removing: {packageName} - needs elevation");
                                }
                                else
                                {
                                    failed++;
                                    failedNames.Add(friendlyName);
                                    _logging.Warn($"Failed to remove: {packageName} — {stderr.Trim()}");
                                }
                            }
                        }
                    });
                }
                
                BloatwareProgress = "[3/3] Cleanup complete!";
                
                var resultText = $"✓ Bloatware removal complete!\n\n" +
                              $"• Removed: {removed} package(s)\n" +
                              $"• Failed: {failed} package(s)\n";
                
                if (failedNames.Count > 0)
                {
                    resultText += $"\nFailed packages:\n" + string.Join("\n", failedNames.Select(n => $"  ✗ {n}"));
                    
                    if (failedNames.Any(n => n.Contains("needs admin")))
                    {
                        resultText += "\n\n💡 Tip: Run OmenCore as Administrator to remove provisioned packages.";
                    }
                }
                
                resultText += "\n\nRun a new scan to verify.";
                BloatwareList = resultText;
                BloatwareCount = 0;
                
                _logging.Info($"Bloatware removal complete: {removed} removed, {failed} failed");
                
                var msgText = $"Removed {removed} bloatware package(s).";
                if (failed > 0)
                {
                    msgText += $"\n\n{failed} package(s) could not be removed.";
                    if (failedNames.Any(n => n.Contains("needs admin")))
                    {
                        msgText += "\n\nSome packages require administrator privileges.\nRight-click OmenCore → 'Run as administrator' and try again.";
                    }
                }
                msgText += "\n\nSome changes may require a system restart.";
                
                MessageBox.Show(msgText,
                    "Bloatware Removal Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to remove bloatware", ex);
                BloatwareList = $"❌ Removal failed: {ex.Message}";
                MessageBox.Show($"Failed to remove bloatware: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsScanningBloatware = false;
            }
        }
        
        #endregion
        
        #region Profile Management
        
        private async Task ImportProfileAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import OmenCore Profile",
                    Filter = "OmenCore Profile (*.omencore)|*.omencore|All Files (*.*)|*.*",
                    DefaultExt = ".omencore"
                };
                
                if (dialog.ShowDialog() != true)
                    return;
                
                var fileName = dialog.FileName;
                if (string.IsNullOrEmpty(fileName))
                    return;
                
                var profile = await _profileExportService.ImportProfileAsync(fileName);
                if (profile == null)
                {
                    MessageBox.Show(
                        "Failed to import profile. The file may be corrupted or incompatible.",
                        "Import Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                
                // Show import options dialog
                var result = MessageBox.Show(
                    $"Import profile from {Path.GetFileName(fileName)}?\n\n" +
                    $"This will apply:\n" +
                    $"• {profile.FanPresets?.Count ?? 0} fan preset(s)\n" +
                    $"• {profile.PerformanceModes?.Count ?? 0} performance mode(s)\n" +
                    $"• {profile.GpuOcProfiles?.Count ?? 0} GPU OC profile(s)\n" +
                    $"• Battery & hysteresis settings\n\n" +
                    "Existing settings will be overwritten.",
                    "Import Profile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
                
                _profileExportService.ApplyProfile(profile, _config);
                _configService.Save(_config);
                
                _logging.Info($"Profile imported successfully from {fileName}");
                
                MessageBox.Show(
                    "Profile imported successfully!\n\n" +
                    "Settings have been applied. Some changes may require restarting the app.",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to import profile", ex);
                MessageBox.Show($"Failed to import profile: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task ExportProfileAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export OmenCore Profile",
                    Filter = "OmenCore Profile (*.omencore)|*.omencore|All Files (*.*)|*.*",
                    DefaultExt = ".omencore",
                    FileName = $"omencore-profile-{DateTime.Now:yyyy-MM-dd}.omencore"
                };
                
                if (dialog.ShowDialog() != true)
                    return;
                
                await _profileExportService.ExportProfileAsync(dialog.FileName, _config);
                
                _logging.Info($"Profile exported successfully to {dialog.FileName}");
                
                MessageBox.Show(
                    $"Profile exported successfully!\\n\\n" +
                    $"File: {Path.GetFileName(dialog.FileName)}\\n" +
                    $"Location: {Path.GetDirectoryName(dialog.FileName)}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to export profile", ex);
                MessageBox.Show($"Failed to export profile: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task ExportDiagnosticsAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Diagnostics Bundle",
                    Filter = "ZIP Archive (*.zip)|*.zip|All Files (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = $"omencore-diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip"
                };
                
                if (dialog.ShowDialog() != true)
                    return;
                
                var exportedPath = await _diagnosticsExportService.ExportDiagnosticsAsync();
                
                // Copy to user-selected location if export succeeded
                if (exportedPath != null && File.Exists(exportedPath))
                {
                    File.Copy(exportedPath, dialog.FileName, overwrite: true);
                    try { File.Delete(exportedPath); } catch { }
                }
                
                _logging.Info($"Diagnostics exported successfully to {dialog.FileName}");
                
                MessageBox.Show(
                    $"Diagnostics bundle exported successfully!\\n\\n" +
                    $"File: {Path.GetFileName(dialog.FileName)}\\n" +
                    $"Location: {Path.GetDirectoryName(dialog.FileName)}\\n\\n" +
                    "You can attach this ZIP file to GitHub issues for troubleshooting.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to export diagnostics", ex);
                MessageBox.Show($"Failed to export diagnostics: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
        
        private static class NativeMethods
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint GENERIC_WRITE = 0x40000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);
        }
    }
}
