using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
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
        private string _fanCleaningStatusText = "Checking hardware...";
        private string _fanCleaningStatusIcon = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2Z";
        private Brush _fanCleaningStatusColor = Brushes.Gray;
        private bool _canStartFanCleaning;
        private bool _isFanCleaningActive;
        private string _fanCleaningProgress = "";
        private int _fanCleaningProgressPercent;
        private CancellationTokenSource? _fanCleaningCts;

        public SettingsViewModel(LoggingService logging, ConfigurationService configService, 
            SystemInfoService systemInfoService, FanCleaningService fanCleaningService)
        {
            _logging = logging;
            _configService = configService;
            _config = configService.Config;
            _systemInfoService = systemInfoService;
            _fanCleaningService = fanCleaningService;

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

            // Check fan cleaning availability
            CheckFanCleaningAvailability();
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

            // Check startup registry
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                _startWithWindows = key?.GetValue("OmenCore") != null;
            }
            catch { }
        }

        private void SaveSettings()
        {
            _config.Monitoring.PollIntervalMs = _pollingIntervalMs;
            _config.Monitoring.HistoryCount = _historyCount;
            _config.Monitoring.LowOverheadMode = _lowOverheadMode;
            
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
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("OmenCore", $"\"{exePath}\"");
                        _logging.Info("Added OmenCore to Windows startup");
                    }
                }
                else
                {
                    key.DeleteValue("OmenCore", false);
                    _logging.Info("Removed OmenCore from Windows startup");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to modify startup registry", ex);
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
                var path = ConfigFolderPath;
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
            
            if (systemInfo.IsHpOmen)
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
                    FanCleaningStatusText = $"⚠️ HP OMEN detected but EC access unavailable. Install WinRing0 driver.";
                    FanCleaningStatusIcon = "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z"; // Warning
                    FanCleaningStatusColor = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Orange
                    CanStartFanCleaning = false;
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
                        Application.Current.Dispatcher.Invoke(() =>
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

        #endregion
    }
}
