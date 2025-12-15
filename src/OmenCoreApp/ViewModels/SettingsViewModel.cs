using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        
        private bool _startWithWindows;
        private bool _startMinimized;
        private bool _minimizeToTrayOnClose = true;
        private int _pollingIntervalMs = 1500;
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

        public SettingsViewModel(LoggingService logging, ConfigurationService configService, 
            SystemInfoService systemInfoService, FanCleaningService fanCleaningService,
            BiosUpdateService biosUpdateService)
        {
            _logging = logging;
            _configService = configService;
            _config = configService.Config;
            _systemInfoService = systemInfoService;
            _fanCleaningService = fanCleaningService;
            _biosUpdateService = biosUpdateService;

            // Load saved settings
            LoadSettings();

            // Initialize commands
            OpenConfigFolderCommand = new RelayCommand(_ => OpenConfigFolder());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
            OpenGitHubCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore"));
            OpenReleaseNotesCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore/releases"));
            OpenIssuesCommand = new RelayCommand(_ => OpenUrl("https://github.com/theantipopau/omencore/issues"));
            StartFanCleaningCommand = new AsyncRelayCommand(async _ => await StartFanCleaningAsync(), _ => CanStartFanCleaning && !IsFanCleaningActive);
            ResetToDefaultsCommand = new RelayCommand(_ => ResetToDefaults());
            InstallDriverCommand = new RelayCommand(_ => InstallDriver());
            RefreshDriverStatusCommand = new RelayCommand(_ => CheckDriverStatus());
            CheckBiosUpdatesCommand = new AsyncRelayCommand(async _ => await CheckBiosUpdatesAsync(), _ => !IsBiosCheckInProgress);
            DownloadBiosUpdateCommand = new RelayCommand(_ => DownloadBiosUpdate(), _ => BiosUpdateAvailable && !string.IsNullOrEmpty(BiosDownloadUrl));

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
                    SaveSettings();
                }
            }
        }

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
                }
            }
        }

        public string[] FanPresetOptions => new[] { "Auto", "Quiet", "Performance", "Max" };
        public string[] PerformanceModeOptions => new[] { "Silent", "Balanced", "Performance", "Turbo" };

        public string CurrentPowerStatus
        {
            get
            {
                try
                {
                    var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                    return powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online
                        ? "⚡ AC Power Connected"
                        : "🔋 On Battery";
                }
                catch
                {
                    return "Unknown";
                }
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
                }
            }
        }

        public string[] OsdPositionOptions => new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

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

        public bool OsdShowCpuTemp
        {
            get => _config.Osd?.ShowCpuTemp ?? true;
            set
            {
                if (_config.Osd == null) _config.Osd = new OsdSettings();
                _config.Osd.ShowCpuTemp = value;
                OnPropertyChanged();
                SaveSettings();
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
                    ApplyBatteryChargeLimit(value);
                }
            }
        }

        private void ApplyBatteryChargeLimit(bool enabled)
        {
            try
            {
                // This will be wired up to HpWmiBios in App.xaml.cs
                _logging.Info($"Battery charge limit: {(enabled ? "Enabling 80% limit" : "Disabling (full charge)")}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply battery charge limit: {ex.Message}", ex);
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
        public ICommand StartFanCleaningCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand InstallDriverCommand { get; }
        public ICommand RefreshDriverStatusCommand { get; }
        public ICommand CheckBiosUpdatesCommand { get; }
        public ICommand DownloadBiosUpdateCommand { get; }

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
            // Note: IncludePreReleases not yet in UpdatePreferences, using default false
            
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

            // Check startup registry
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                _startWithWindows = key?.GetValue("OmenCore") != null;
            }
            catch { }
            
            _logging.Info($"Settings loaded: Hotkeys={_hotkeysEnabled}, Notifications={_notificationsEnabled}, PowerAutomation={_powerAutomationEnabled}");
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
            // Note: IncludePreReleases not yet in UpdatePreferences

            _configService.Save(_config);
        }

        private void SetStartWithWindows(bool enable)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                
                // Use Task Scheduler for elevated startup (required for hardware access)
                // This method works better than registry Run key which doesn't elevate
                var taskName = "OmenCore";
                
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
                    var createProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks",
                            Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f",
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
                        
                        // Also add to registry as fallback (non-elevated, but ensures app at least tries to start)
                        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.SetValue("OmenCore", $"\"{exePath}\"");
                    }
                    else
                    {
                        _logging.Warn($"Task Scheduler creation returned exit code {createProcess.ExitCode}: {error}");
                        // Fall back to registry only
                        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.SetValue("OmenCore", $"\"{exePath}\"");
                        _logging.Info("Added OmenCore to Windows startup (registry fallback - may not have admin rights)");
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
                    
                    // Also remove from registry
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.DeleteValue("OmenCore", false);
                    _logging.Info("Removed OmenCore from Windows startup");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to modify startup settings", ex);
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
                PollingIntervalMs = 1500;
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
                
                // Check for XTU service conflict
                bool xtuRunning = false;
                string? xtuServiceName = null;
                try
                {
                    var xtuServices = new[] { "XTU3SERVICE", "XtuService", "IntelXtuService" };
                    foreach (var svc in xtuServices)
                    {
                        var processes = Process.GetProcessesByName(svc);
                        if (processes.Any())
                        {
                            xtuRunning = true;
                            xtuServiceName = svc;
                            foreach (var p in processes) p.Dispose();
                            break;
                        }
                    }
                }
                catch { }

                if (pawnIoAvailable)
                {
                    DriverStatusText = "PawnIO Installed";
                    DriverStatusDetail = "Secure Boot compatible driver backend available (recommended)";
                    DriverStatusColor = new SolidColorBrush(Color.FromRgb(102, 187, 106)); // Green
                }
                else if (winRing0Available)
                {
                    if (xtuRunning)
                    {
                        DriverStatusText = "WinRing0 Installed (XTU Conflict)";
                        DriverStatusDetail = $"Intel XTU service ({xtuServiceName}) may block undervolting. Stop XTU to use OmenCore undervolting.";
                        DriverStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
                    }
                    else
                    {
                        DriverStatusText = "WinRing0 Installed (Legacy)";
                        DriverStatusDetail = "Legacy driver backend detected. On Secure Boot/HVCI systems, PawnIO is recommended.";
                        DriverStatusColor = new SolidColorBrush(Color.FromRgb(102, 187, 106)); // Green
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
                            $"WinRing0 may be blocked ({string.Join(", ", reasons)}). " +
                            "Install PawnIO (pawnio.eu) for Secure Boot compatible EC access.";
                    }
                    else
                    {
                        DriverStatusDetail =
                            "Install PawnIO (recommended) or LibreHardwareMonitor (WinRing0) to enable driver-dependent features.";
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
        
        private void InstallDriver()
        {
            // Show info about what will be installed
            var result = MessageBox.Show(
                "OmenCore requires LibreHardwareMonitor for hardware monitoring.\n\n" +
                "LibreHardwareMonitor includes the WinRing0 driver needed for:\n" +
                "• CPU temperature monitoring\n" +
                "• CPU undervolting (MSR access)\n" +
                "• TCC offset control\n\n" +
                "Click OK to download and install LibreHardwareMonitor.",
                "Install Hardware Monitor", 
                MessageBoxButton.OKCancel, 
                MessageBoxImage.Information);
                
            if (result != MessageBoxResult.OK)
                return;
            
            // Delegate to App's download method
            if (Application.Current is App app)
            {
                // Call the download method via reflection or make it public/static
                var method = typeof(App).GetMethod("DownloadAndInstallLibreHardwareMonitor", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(app, null);
                
                // Refresh status after install attempt
                Task.Delay(3000).ContinueWith(_ => 
                    Application.Current.Dispatcher.Invoke(CheckDriverStatus));
            }
            else
            {
                    // Prefer PawnIO on Secure Boot / Memory Integrity systems.
                    if (IsSecureBootEnabled() || IsMemoryIntegrityEnabled())
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://pawnio.eu/",
                            UseShellExecute = true
                        });
                        return;
                    }

                    // Legacy / optional: LibreHardwareMonitor (WinRing0-based) backend.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor",
                        UseShellExecute = true
                    });
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
                
                // Check OGH installation
                try
                {
                    var processes = new[] { "OmenCommandCenterBackground", "OmenCap", "omenmqtt" };
                    OghInstalled = false;
                    foreach (var proc in processes)
                    {
                        try
                        {
                            var procs = System.Diagnostics.Process.GetProcessesByName(proc);
                            if (procs.Length > 0)
                            {
                                OghInstalled = true;
                                foreach (var p in procs) p.Dispose();
                                break;
                            }
                        }
                        catch { }
                    }
                    
                    // Also check service
                    if (!OghInstalled)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\HPOmenCap");
                        OghInstalled = key != null;
                    }
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
                    FanBackend = "WMI BIOS + WinRing0";
                else
                    FanBackend = "WMI BIOS";
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
