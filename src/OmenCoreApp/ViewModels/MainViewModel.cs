using OmenCore.Corsair;
using OmenCore.Hardware;
using OmenCore.Logitech;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;
using OmenCore.Views;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace OmenCore.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly LoggingService _logging = App.Logging;
        private readonly ConfigurationService _configService = App.Configuration;
        private readonly AppConfig _config;
        private readonly FanService _fanService;
        private readonly PerformanceModeService _performanceModeService;
        private readonly KeyboardLightingService _keyboardLightingService;
        private readonly SystemOptimizationService _systemOptimizationService;
        private readonly GpuSwitchService _gpuSwitchService;
        private CorsairDeviceService? _corsairDeviceService;
        private LogitechDeviceService? _logitechDeviceService;
        private readonly MacroService _macroService = new();
        private readonly UndervoltService _undervoltService;
        private readonly HardwareMonitoringService _hardwareMonitoringService;
        private readonly SystemRestoreService _systemRestoreService;
        private readonly OmenGamingHubCleanupService _hubCleanupService;
        private readonly SystemInfoService _systemInfoService;
        private readonly AutoUpdateService _autoUpdateService;
        private readonly ProcessMonitoringService _processMonitoringService;
        private readonly GameProfileService _gameProfileService;
        private readonly FanCleaningService _fanCleaningService;
        private readonly HotkeyService _hotkeyService;
        private readonly NotificationService _notificationService;
        
        // Sub-ViewModels for modular UI
        public FanControlViewModel? FanControl { get; private set; }
        public LightingViewModel? Lighting { get; private set; }
        public SystemControlViewModel? SystemControl { get; private set; }
        public DashboardViewModel? Dashboard { get; private set; }
        public SettingsViewModel? Settings { get; private set; }
        
        private readonly AsyncRelayCommand _applyUndervoltCommand;
        private readonly AsyncRelayCommand _resetUndervoltCommand;
        private readonly AsyncRelayCommand _refreshUndervoltCommand;
        private readonly AsyncRelayCommand _createRestorePointCommand;
        private readonly AsyncRelayCommand _cleanupOmenHubCommand;
        private readonly AsyncRelayCommand _installUpdateCommand;
        private readonly AsyncRelayCommand _checkForUpdatesCommand;
        private readonly RelayCommand _takeUndervoltControlCommand;
        private readonly RelayCommand _respectExternalUndervoltCommand;
        private readonly RelayCommand _stopMacroRecordingInternalCommand;
        private readonly RelayCommand _saveRecordedMacroInternalCommand;
        private readonly AsyncRelayCommand _applyLogitechColorInternalCommand;
        private readonly AsyncRelayCommand _syncCorsairThemeInternalCommand;
        private readonly RelayCommand _openReleaseNotesCommand;
        private readonly RelayCommand _openGameProfileManagerCommand;
        private readonly INotifyCollectionChanged? _macroBufferNotifier;
        private readonly StringBuilder _logBuffer = new();

        private FanPreset? _selectedPreset;
        private PerformanceMode? _selectedPerformanceMode;
        private LightingProfile? _selectedLightingProfile;
        private CorsairDevice? _selectedCorsairDevice;
        private CorsairLightingPreset? _selectedCorsairPreset;
        private MacroProfile? _selectedMacroProfile;
        private LogitechDevice? _selectedLogitechDevice;
        private string _customPresetName = "Custom";
        private bool _gamingModeActive;
        private UndervoltStatus _undervoltStatus = UndervoltStatus.CreateUnknown();
        private double _requestedCoreOffset;
        private double _requestedCacheOffset;
        private bool _respectExternalUndervolt = true;
        private MonitoringSample? _latestMonitoringSample;
        private bool _monitoringLowOverhead;
        private bool _monitoringInitialized;
        private string _logitechColorHex = "#E6002E";
        private int _logitechBrightness = 80;
        private bool _isMacroRecording;
        private string _newMacroName = "Recorded Macro";
        private bool _restorePointInProgress;
        private string _restorePointStatus = "No restore point created";
        private bool _cleanupInProgress;
        private string _cleanupStatus = "Status: Not checked";
        private bool _cleanupRemoveStorePackage = true;
        private bool _cleanupRemoveLegacyInstallers = true;
        private bool _cleanupRemoveRegistry = true;
        private bool _cleanupRemoveFiles = true;
        private bool _cleanupRemoveServices = true;
        private bool _cleanupKillProcesses = true;
        private bool _cleanupPreserveFirewall = true;
        private bool _cleanupDryRun;
        private VersionInfo? _availableUpdate;
        private bool _updateBannerVisible;
        private string _updateBannerMessage = string.Empty;
        private bool _updateDownloadInProgress;
        private double _updateDownloadProgress;
        private string _updateDownloadStatus = string.Empty;
        private bool _updateInstallBlocked;
        private string _appVersionLabel = "v0.0.0";
        private string _currentFanMode = "Auto";
        private string _currentPerformanceMode = "Balanced";

        public ObservableCollection<FanPreset> FanPresets { get; } = new();
        public ObservableCollection<FanCurvePoint> CustomFanCurve { get; } = new();
        public ObservableCollection<PerformanceMode> PerformanceModes { get; } = new();
        public ObservableCollection<LightingProfile> LightingProfiles { get; } = new();
        public ObservableCollection<ServiceToggle> SystemToggles { get; } = new();
        public ObservableCollection<CorsairDevice> CorsairDevices { get; } = new();
        public ObservableCollection<CorsairLightingPreset> CorsairLightingPresets { get; } = new();
        public ObservableCollection<CorsairDpiStage> EditableDpiStages { get; } = new();
        public ObservableCollection<MacroProfile> MacroProfiles { get; } = new();
        public ObservableCollection<LogitechDevice> LogitechDevices { get; } = new();
        public ReadOnlyObservableCollection<MacroAction> RecordingBuffer => _macroService.Buffer;
        public ObservableCollection<string> RecentEvents { get; } = new();
        public ObservableCollection<string> OmenCleanupSteps { get; } = new();
        public ReadOnlyObservableCollection<GameProfile> GameProfiles => _gameProfileService.Profiles;
        
        public SystemInfo SystemInfo { get; private set; }
        public string AppVersionLabel
        {
            get => _appVersionLabel;
            private set
            {
                if (_appVersionLabel != value)
                {
                    _appVersionLabel = value;
                    OnPropertyChanged(nameof(AppVersionLabel));
                }
            }
        }

        public string CurrentFanMode
        {
            get => _currentFanMode;
            set
            {
                if (_currentFanMode != value)
                {
                    _currentFanMode = value;
                    OnPropertyChanged(nameof(CurrentFanMode));
                }
            }
        }

        public string CurrentPerformanceMode
        {
            get => _currentPerformanceMode;
            set
            {
                if (_currentPerformanceMode != value)
                {
                    _currentPerformanceMode = value;
                    OnPropertyChanged(nameof(CurrentPerformanceMode));
                }
            }
        }

        public bool UpdateBannerVisible
        {
            get => _updateBannerVisible;
            private set
            {
                if (_updateBannerVisible != value)
                {
                    _updateBannerVisible = value;
                    OnPropertyChanged(nameof(UpdateBannerVisible));
                }
            }
        }
        public string UpdateBannerMessage
        {
            get => _updateBannerMessage;
            private set
            {
                if (_updateBannerMessage != value)
                {
                    _updateBannerMessage = value;
                    OnPropertyChanged(nameof(UpdateBannerMessage));
                }
            }
        }
        
        public bool UpdateDownloadInProgress
        {
            get => _updateDownloadInProgress;
            private set
            {
                if (_updateDownloadInProgress != value)
                {
                    _updateDownloadInProgress = value;
                    OnPropertyChanged(nameof(UpdateDownloadInProgress));
                }
            }
        }
        
        public double UpdateDownloadProgress
        {
            get => _updateDownloadProgress;
            private set
            {
                if (Math.Abs(_updateDownloadProgress - value) > 0.01)
                {
                    _updateDownloadProgress = value;
                    OnPropertyChanged(nameof(UpdateDownloadProgress));
                }
            }
        }
        
        public string UpdateDownloadStatus
        {
            get => _updateDownloadStatus;
            private set
            {
                if (_updateDownloadStatus != value)
                {
                    _updateDownloadStatus = value;
                    OnPropertyChanged(nameof(UpdateDownloadStatus));
                }
            }
        }

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }
        public ReadOnlyObservableCollection<MonitoringSample> MonitoringSamples { get; } = null!;
        public MonitoringSample? LatestMonitoringSample
        {
            get => _latestMonitoringSample;
            private set
            {
                if (_latestMonitoringSample == value) return;
                
                _latestMonitoringSample = value;
                
                // Batch property notifications to reduce overhead
                OnPropertyChanged(string.Empty); // Notifies all properties changed
            }
        }
        public bool MonitoringLowOverheadMode
        {
            get => _monitoringLowOverhead;
            set
            {
                if (_monitoringLowOverhead != value)
                {
                    _monitoringLowOverhead = value;
                    _hardwareMonitoringService.SetLowOverheadMode(value);
                    OnPropertyChanged(nameof(MonitoringLowOverheadMode));
                    OnPropertyChanged(nameof(MonitoringGraphsVisible));
                    if (_monitoringInitialized)
                    {
                        PushEvent(value ? "Low overhead monitoring enabled" : "Full monitoring telemetry enabled");
                    }
                }
            }
        }
        public bool MonitoringGraphsVisible => !MonitoringLowOverheadMode;
        public string CpuSummary => LatestMonitoringSample == null ? "CPU telemetry unavailable" : $"{LatestMonitoringSample.CpuTemperatureC:F0}°C • {LatestMonitoringSample.CpuLoadPercent:F0}% load";
        public string GpuSummary => LatestMonitoringSample == null ? "GPU telemetry unavailable" : $"{LatestMonitoringSample.GpuTemperatureC:F0}°C • {LatestMonitoringSample.GpuLoadPercent:F0}% load • {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM";
        public string MemorySummary => LatestMonitoringSample == null ? "Memory telemetry unavailable" : $"{LatestMonitoringSample.RamUsageGb:F1} / {LatestMonitoringSample.RamTotalGb:F0} GB";
        public string StorageSummary => LatestMonitoringSample == null ? "Storage telemetry unavailable" : $"SSD {LatestMonitoringSample.SsdTemperatureC:F0}°C • {LatestMonitoringSample.DiskUsagePercent:F0}% active";
        public string CpuClockSummary => LatestMonitoringSample == null || LatestMonitoringSample.CpuCoreClocksMhz.Count == 0
            ? "Per-core clocks unavailable"
            : string.Join(", ", LatestMonitoringSample.CpuCoreClocksMhz.Select((c, i) => $"C{i + 1}:{c:F0}MHz"));
        public UndervoltStatus UndervoltStatus
        {
            get => _undervoltStatus;
            private set
            {
                _undervoltStatus = value;
                OnPropertyChanged(nameof(UndervoltStatus));
                OnPropertyChanged(nameof(UndervoltStatusSummary));
                OnPropertyChanged(nameof(ExternalUndervoltSummary));
                OnPropertyChanged(nameof(UndervoltWarning));
                OnPropertyChanged(nameof(ShowUndervoltWarning));
                OnPropertyChanged(nameof(HasExternalUndervolt));
                _applyUndervoltCommand?.RaiseCanExecuteChanged();
            }
        }

        public double RequestedCoreOffset
        {
            get => _requestedCoreOffset;
            set
            {
                if (Math.Abs(_requestedCoreOffset - value) > double.Epsilon)
                {
                    _requestedCoreOffset = value;
                    OnPropertyChanged(nameof(RequestedCoreOffset));
                }
            }
        }

        public double RequestedCacheOffset
        {
            get => _requestedCacheOffset;
            set
            {
                if (Math.Abs(_requestedCacheOffset - value) > double.Epsilon)
                {
                    _requestedCacheOffset = value;
                    OnPropertyChanged(nameof(RequestedCacheOffset));
                }
            }
        }

        public bool RespectExternalUndervolt
        {
            get => _respectExternalUndervolt;
            set
            {
                if (_respectExternalUndervolt != value)
                {
                    _respectExternalUndervolt = value;
                    OnPropertyChanged(nameof(RespectExternalUndervolt));
                    _applyUndervoltCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasExternalUndervolt => UndervoltStatus?.HasExternalController ?? false;
        public string UndervoltStatusSummary => UndervoltStatus == null ? "n/a" : $"Core {UndervoltStatus.CurrentCoreOffsetMv:+0;-0;0} mV | Cache {UndervoltStatus.CurrentCacheOffsetMv:+0;-0;0} mV";
        public string ExternalUndervoltSummary => HasExternalUndervolt
            ? $"{UndervoltStatus.ExternalController}: Core {UndervoltStatus.ExternalCoreOffsetMv:+0;-0;0} mV / Cache {UndervoltStatus.ExternalCacheOffsetMv:+0;-0;0} mV"
            : "None detected";
        public string UndervoltWarning => UndervoltStatus?.Warning ?? UndervoltStatus?.Error ?? string.Empty;
        public bool ShowUndervoltWarning => !string.IsNullOrWhiteSpace(UndervoltWarning);

        public FanPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged(nameof(SelectedPreset));
                    if (value != null)
                    {
                        LoadCurve(value);
                    }
                }
            }
        }

        public PerformanceMode? SelectedPerformanceMode
        {
            get => _selectedPerformanceMode;
            set
            {
                if (_selectedPerformanceMode != value)
                {
                    _selectedPerformanceMode = value;
                    OnPropertyChanged(nameof(SelectedPerformanceMode));
                }
            }
        }

        public LightingProfile? SelectedLightingProfile
        {
            get => _selectedLightingProfile;
            set
            {
                if (_selectedLightingProfile != value)
                {
                    _selectedLightingProfile = value;
                    OnPropertyChanged(nameof(SelectedLightingProfile));
                    _syncCorsairThemeInternalCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public CorsairDevice? SelectedCorsairDevice
        {
            get => _selectedCorsairDevice;
            set
            {
                if (_selectedCorsairDevice != value)
                {
                    _selectedCorsairDevice = value;
                    EditableDpiStages.Clear();
                    if (value != null)
                    {
                        foreach (var stage in value.DpiStages)
                        {
                            EditableDpiStages.Add(new CorsairDpiStage { Name = stage.Name, Dpi = stage.Dpi, IsDefault = stage.IsDefault, AngleSnapping = stage.AngleSnapping, LiftOffDistanceMm = stage.LiftOffDistanceMm });
                        }
                    }
                    OnPropertyChanged(nameof(SelectedCorsairDevice));
                }
            }
        }

        public CorsairLightingPreset? SelectedCorsairPreset
        {
            get => _selectedCorsairPreset;
            set
            {
                if (_selectedCorsairPreset != value)
                {
                    _selectedCorsairPreset = value;
                    OnPropertyChanged(nameof(SelectedCorsairPreset));
                }
            }
        }

        public MacroProfile? SelectedMacroProfile
        {
            get => _selectedMacroProfile;
            set
            {
                if (_selectedMacroProfile != value)
                {
                    _selectedMacroProfile = value;
                    OnPropertyChanged(nameof(SelectedMacroProfile));
                }
            }
        }

        public bool IsMacroRecording
        {
            get => _isMacroRecording;
            private set
            {
                if (_isMacroRecording != value)
                {
                    _isMacroRecording = value;
                    OnPropertyChanged(nameof(IsMacroRecording));
                    _stopMacroRecordingInternalCommand?.RaiseCanExecuteChanged();
                    _saveRecordedMacroInternalCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewMacroName
        {
            get => _newMacroName;
            set
            {
                if (_newMacroName != value)
                {
                    _newMacroName = value;
                    OnPropertyChanged(nameof(NewMacroName));
                    _saveRecordedMacroInternalCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public LogitechDevice? SelectedLogitechDevice
        {
            get => _selectedLogitechDevice;
            set
            {
                if (_selectedLogitechDevice != value)
                {
                    _selectedLogitechDevice = value;
                    if (value != null)
                    {
                        LogitechColorHex = value.CurrentColorHex;
                        LogitechBrightness = value.Status.BrightnessPercent;
                    }
                    OnPropertyChanged(nameof(SelectedLogitechDevice));
                    OnPropertyChanged(nameof(LogitechStatusSummary));
                    _applyLogitechColorInternalCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string LogitechColorHex
        {
            get => _logitechColorHex;
            set
            {
                if (_logitechColorHex != value)
                {
                    _logitechColorHex = value;
                    OnPropertyChanged(nameof(LogitechColorHex));
                }
            }
        }

        public int LogitechBrightness
        {
            get => _logitechBrightness;
            set
            {
                if (_logitechBrightness != value)
                {
                    _logitechBrightness = value;
                    OnPropertyChanged(nameof(LogitechBrightness));
                }
            }
        }

        public string LogitechStatusSummary => SelectedLogitechDevice == null
            ? "No device selected"
            : $"Battery {SelectedLogitechDevice.Status.BatteryPercent}% • DPI {SelectedLogitechDevice.Status.Dpi}/{SelectedLogitechDevice.Status.MaxDpi} • Firmware {SelectedLogitechDevice.Status.FirmwareVersion}";

        public string CustomPresetName
        {
            get => _customPresetName;
            set
            {
                if (_customPresetName != value)
                {
                    _customPresetName = value;
                    OnPropertyChanged(nameof(CustomPresetName));
                }
            }
        }

        public bool GamingModeActive
        {
            get => _gamingModeActive;
            set
            {
                if (_gamingModeActive != value)
                {
                    _gamingModeActive = value;
                    OnPropertyChanged(nameof(GamingModeActive));
                    if (value)
                    {
                        _systemOptimizationService.ApplyGamingMode(SystemToggles);
                    }
                    else
                    {
                        _systemOptimizationService.RestoreDefaults();
                    }
                }
            }
        }

        public string LogBuffer => _logBuffer.ToString();

        public bool RestorePointInProgress
        {
            get => _restorePointInProgress;
            private set
            {
                if (_restorePointInProgress != value)
                {
                    _restorePointInProgress = value;
                    OnPropertyChanged(nameof(RestorePointInProgress));
                    _createRestorePointCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string RestorePointStatus
        {
            get => _restorePointStatus;
            private set
            {
                if (_restorePointStatus != value)
                {
                    _restorePointStatus = value;
                    OnPropertyChanged(nameof(RestorePointStatus));
                }
            }
        }

        public bool CleanupInProgress
        {
            get => _cleanupInProgress;
            private set
            {
                if (_cleanupInProgress != value)
                {
                    _cleanupInProgress = value;
                    OnPropertyChanged(nameof(CleanupInProgress));
                    _cleanupOmenHubCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string CleanupStatus
        {
            get => _cleanupStatus;
            private set
            {
                if (_cleanupStatus != value)
                {
                    _cleanupStatus = value;
                    OnPropertyChanged(nameof(CleanupStatus));
                }
            }
        }

        public bool CleanupRemoveStorePackage
        {
            get => _cleanupRemoveStorePackage;
            set
            {
                if (_cleanupRemoveStorePackage != value)
                {
                    _cleanupRemoveStorePackage = value;
                    OnPropertyChanged(nameof(CleanupRemoveStorePackage));
                }
            }
        }

        public bool CleanupRemoveLegacyInstallers
        {
            get => _cleanupRemoveLegacyInstallers;
            set
            {
                if (_cleanupRemoveLegacyInstallers != value)
                {
                    _cleanupRemoveLegacyInstallers = value;
                    OnPropertyChanged(nameof(CleanupRemoveLegacyInstallers));
                }
            }
        }

        public bool CleanupRemoveRegistry
        {
            get => _cleanupRemoveRegistry;
            set
            {
                if (_cleanupRemoveRegistry != value)
                {
                    _cleanupRemoveRegistry = value;
                    OnPropertyChanged(nameof(CleanupRemoveRegistry));
                }
            }
        }

        public bool CleanupRemoveFiles
        {
            get => _cleanupRemoveFiles;
            set
            {
                if (_cleanupRemoveFiles != value)
                {
                    _cleanupRemoveFiles = value;
                    OnPropertyChanged(nameof(CleanupRemoveFiles));
                }
            }
        }

        public bool CleanupRemoveServices
        {
            get => _cleanupRemoveServices;
            set
            {
                if (_cleanupRemoveServices != value)
                {
                    _cleanupRemoveServices = value;
                    OnPropertyChanged(nameof(CleanupRemoveServices));
                }
            }
        }

        public bool CleanupKillProcesses
        {
            get => _cleanupKillProcesses;
            set
            {
                if (_cleanupKillProcesses != value)
                {
                    _cleanupKillProcesses = value;
                    OnPropertyChanged(nameof(CleanupKillProcesses));
                }
            }
        }

        public bool CleanupPreserveFirewall
        {
            get => _cleanupPreserveFirewall;
            set
            {
                if (_cleanupPreserveFirewall != value)
                {
                    _cleanupPreserveFirewall = value;
                    OnPropertyChanged(nameof(CleanupPreserveFirewall));
                }
            }
        }

        public bool CleanupDryRun
        {
            get => _cleanupDryRun;
            set
            {
                if (_cleanupDryRun != value)
                {
                    _cleanupDryRun = value;
                    OnPropertyChanged(nameof(CleanupDryRun));
                }
            }
        }

        public ICommand ApplyFanPresetCommand { get; }
        public ICommand SaveCustomPresetCommand { get; }
        public ICommand ApplyFanCurveCommand { get; }
        public ICommand ApplyPerformanceModeCommand { get; }
        public ICommand ApplyLightingProfileCommand { get; }
        public ICommand ToggleAnimationsCommand { get; }
        public ICommand GamingModeCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand SwitchGpuCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand DiscoverCorsairCommand { get; }
        public ICommand ApplyCorsairLightingCommand { get; }
        public ICommand SaveCorsairDpiCommand { get; }
        public ICommand ApplyMacroCommand { get; }
        public ICommand SyncCorsairThemeCommand { get; }
        public ICommand StartMacroRecordingCommand { get; }
        public ICommand StopMacroRecordingCommand { get; }
        public ICommand SaveRecordedMacroCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand ToggleServiceCommand { get; }
        public ICommand ToggleLowOverheadModeCommand { get; }
        public ICommand ApplyUndervoltCommand { get; }
        public ICommand ResetUndervoltCommand { get; }
        public ICommand RefreshUndervoltCommand { get; }
        public ICommand TakeUndervoltControlCommand { get; }
        public ICommand RespectExternalUndervoltCommand { get; }
        public ICommand DiscoverLogitechCommand { get; }
        public ICommand ApplyLogitechColorCommand { get; }
        public ICommand CreateRestorePointCommand { get; }
        public ICommand CleanupOmenHubCommand { get; }
        public ICommand InstallUpdateCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand OpenReleaseNotesCommand { get; }
        public ICommand OpenGameProfileManagerCommand { get; }
        public ICommand ExportConfigurationCommand { get; }
        public ICommand ImportConfigurationCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _config = _configService.Load();
            var ec = new WinRing0EcAccess();
            try
            {
                if (!ec.Initialize(_config.EcDevicePath))
                {
                    _logging.Warn("EC bridge not available; fan writes disabled");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to initialize EC bridge", ex);
            }

            // Initialize hardware monitor bridge first (needed by ThermalSensorProvider and FanController)
            LibreHardwareMonitorImpl monitorBridge = new LibreHardwareMonitorImpl(msg => _logging.Info($"[Monitor] {msg}"));
            
            var fanController = new FanController(ec, _config.EcFanRegisterMap, monitorBridge);
            _fanService = new FanService(fanController, new ThermalSensorProvider(monitorBridge), _logging, _config.MonitoringIntervalMs);
            ThermalSamples = _fanService.ThermalSamples;
            FanTelemetry = _fanService.FanTelemetry;
            var powerPlanService = new PowerPlanService(_logging);
            
            // Power limit controller (EC-based CPU/GPU power control)
            PowerLimitController? powerLimitController = null;
            try
            {
                powerLimitController = new PowerLimitController(ec, useSimplifiedMode: true);
                _logging.Info("✓ Power limit controller initialized (simplified mode)");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Power limit controller unavailable: {ex.Message}");
            }
            
            _performanceModeService = new PerformanceModeService(fanController, powerPlanService, powerLimitController, _logging);
            _keyboardLightingService = new KeyboardLightingService(_logging);
            _systemOptimizationService = new SystemOptimizationService(_logging);
            _gpuSwitchService = new GpuSwitchService(_logging);
            
            // Services initialized asynchronously
            InitializeServicesAsync();

            var undervoltProvider = new IntelUndervoltProvider();
            _undervoltService = new UndervoltService(undervoltProvider, _logging, _config.Undervolt?.ProbeIntervalMs ?? 4000);
            _undervoltService.StatusChanged += UndervoltServiceOnStatusChanged;
            RespectExternalUndervolt = _config.Undervolt?.RespectExternalControllers ?? true;
            RequestedCoreOffset = _config.Undervolt?.DefaultOffset.CoreMv ?? -75;
            RequestedCacheOffset = _config.Undervolt?.DefaultOffset.CacheMv ?? -50;
            _hardwareMonitoringService = new HardwareMonitoringService(monitorBridge, _logging, _config.Monitoring ?? new MonitoringPreferences());
            MonitoringSamples = _hardwareMonitoringService.Samples;
            _hardwareMonitoringService.SampleUpdated += HardwareMonitoringServiceOnSampleUpdated;
            _monitoringLowOverhead = _config.Monitoring?.LowOverheadMode ?? false;
            _hardwareMonitoringService.SetLowOverheadMode(_monitoringLowOverhead);
            _systemRestoreService = new SystemRestoreService(_logging);
            _hubCleanupService = new OmenGamingHubCleanupService(_logging);
            _systemInfoService = new SystemInfoService(_logging);
            SystemInfo = _systemInfoService.GetSystemInfo();
            _autoUpdateService = new AutoUpdateService(_logging);
            _processMonitoringService = new ProcessMonitoringService(_logging);
            _gameProfileService = new GameProfileService(_logging, _processMonitoringService, _configService);
            _fanCleaningService = new FanCleaningService(_logging, ec, _systemInfoService);
            _hotkeyService = new HotkeyService(_logging);
            _notificationService = new NotificationService(_logging);
            _autoUpdateService.DownloadProgressChanged += OnUpdateDownloadProgressChanged;
            _autoUpdateService.UpdateCheckCompleted += OnBackgroundUpdateCheckCompleted;
            
            // Wire up game profile notifications
            _gameProfileService.ProfileApplyRequested += OnGameProfileApplyRequested;
            
            // Wire up hotkey events
            _hotkeyService.ToggleFanModeRequested += OnHotkeyToggleFanMode;
            _hotkeyService.TogglePerformanceModeRequested += OnHotkeyTogglePerformanceMode;
            _hotkeyService.ToggleBoostModeRequested += OnHotkeyToggleBoostMode;
            _hotkeyService.ToggleQuietModeRequested += OnHotkeyToggleQuietMode;
            _hotkeyService.ToggleWindowRequested += OnHotkeyToggleWindow;

            // Initialize sub-ViewModels that don't depend on async services
            InitializeSubViewModels();
            AppVersionLabel = $"v{_autoUpdateService.GetCurrentVersion()}";

            ApplyFanPresetCommand = new RelayCommand(_ => 
            {
                if (FanControl?.SelectedPreset != null)
                    FanControl.SelectedPreset = FanControl.SelectedPreset; // Trigger setter to apply
            }, _ => FanControl?.SelectedPreset != null);
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset());
            ApplyFanCurveCommand = new RelayCommand(_ => _fanService.ApplyCustomCurve(CustomFanCurve));
            ApplyPerformanceModeCommand = new RelayCommand(_ => 
            {
                if (SystemControl?.SelectedPerformanceMode != null)
                    SystemControl.ApplyPerformanceModeCommand?.Execute(null);
            }, _ => SystemControl?.SelectedPerformanceMode != null);
            ApplyLightingProfileCommand = new AsyncRelayCommand(async _ => 
            {
                if (Lighting?.ApplyCorsairLightingCommand?.CanExecute(null) == true)
                    Lighting.ApplyCorsairLightingCommand.Execute(null);
                await Task.CompletedTask;
            }, _ => Lighting?.SelectedCorsairDevice != null && Lighting?.SelectedCorsairPreset != null);
            ToggleAnimationsCommand = new RelayCommand(param => _systemOptimizationService.ApplyWindowsAnimations(param as string == "Enable"));
            GamingModeCommand = new RelayCommand(_ => GamingModeActive = !GamingModeActive);
            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
            SwitchGpuCommand = new RelayCommand(mode =>
            {
                if (mode is GpuSwitchMode gpuMode)
                {
                    _gpuSwitchService.Switch(gpuMode);
                }
            });
            ReloadConfigCommand = new RelayCommand(_ => ReloadConfiguration());
            OpenConfigFolderCommand = new RelayCommand(_ => OpenConfigFolder());
            DiscoverCorsairCommand = new AsyncRelayCommand(_ => DiscoverCorsairDevices());
            ApplyCorsairLightingCommand = new AsyncRelayCommand(_ => ApplyCorsairLighting(), _ => SelectedCorsairDevice != null && SelectedCorsairPreset != null);
            SaveCorsairDpiCommand = new AsyncRelayCommand(_ => SaveCorsairDpi(), _ => SelectedCorsairDevice != null);
            ApplyMacroCommand = new AsyncRelayCommand(_ => ApplyMacroToDevice(), _ => SelectedCorsairDevice != null && SelectedMacroProfile != null);
            _syncCorsairThemeInternalCommand = new AsyncRelayCommand(_ => SyncCorsairWithTheme(), _ => SelectedLightingProfile != null);
            SyncCorsairThemeCommand = _syncCorsairThemeInternalCommand;
            StartMacroRecordingCommand = new RelayCommand(_ => StartMacroRecording());
            _stopMacroRecordingInternalCommand = new RelayCommand(_ => StopMacroRecording(), _ => IsMacroRecording);
            StopMacroRecordingCommand = _stopMacroRecordingInternalCommand;
            _saveRecordedMacroInternalCommand = new RelayCommand(_ => SaveRecordedMacro(), _ => RecordingBuffer.Count > 0 && !string.IsNullOrWhiteSpace(NewMacroName) && !IsMacroRecording);
            SaveRecordedMacroCommand = _saveRecordedMacroInternalCommand;
            OpenAboutCommand = new RelayCommand(_ => ShowAbout());
            ToggleServiceCommand = new RelayCommand(param =>
            {
                if (param is ServiceToggle toggle)
                {
                    _systemOptimizationService.ApplyToggle(toggle, toggle.EnabledByDefault);
                }
            });
            ToggleLowOverheadModeCommand = new RelayCommand(_ => MonitoringLowOverheadMode = !MonitoringLowOverheadMode);
            _applyUndervoltCommand = new AsyncRelayCommand(_ => ApplyUndervoltAsync(), _ => CanApplyUndervolt());
            ApplyUndervoltCommand = _applyUndervoltCommand;
            _resetUndervoltCommand = new AsyncRelayCommand(_ => ResetUndervoltAsync());
            ResetUndervoltCommand = _resetUndervoltCommand;
            _refreshUndervoltCommand = new AsyncRelayCommand(_ => _undervoltService.RefreshAsync());
            RefreshUndervoltCommand = _refreshUndervoltCommand;
            _takeUndervoltControlCommand = new RelayCommand(_ => TakeUndervoltControl());
            TakeUndervoltControlCommand = _takeUndervoltControlCommand;
            _respectExternalUndervoltCommand = new RelayCommand(_ => RespectExternalUndervoltController());
            RespectExternalUndervoltCommand = _respectExternalUndervoltCommand;
            DiscoverLogitechCommand = new AsyncRelayCommand(_ => DiscoverLogitechDevices());
            _applyLogitechColorInternalCommand = new AsyncRelayCommand(_ => ApplyLogitechColor(), _ => SelectedLogitechDevice != null);
            ApplyLogitechColorCommand = _applyLogitechColorInternalCommand;
            _createRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync(), _ => !RestorePointInProgress);
            CreateRestorePointCommand = _createRestorePointCommand;
            _cleanupOmenHubCommand = new AsyncRelayCommand(_ => RunOmenCleanupAsync(), _ => !CleanupInProgress);
            CleanupOmenHubCommand = _cleanupOmenHubCommand;
            _checkForUpdatesCommand = new AsyncRelayCommand(_ => CheckForUpdatesBannerAsync(true), _ => !_updateDownloadInProgress);
            CheckForUpdatesCommand = _checkForUpdatesCommand;
            _installUpdateCommand = new AsyncRelayCommand(_ => InstallUpdateAsync(), _ => CanInstallUpdate());
            InstallUpdateCommand = _installUpdateCommand;
            _openReleaseNotesCommand = new RelayCommand(_ => OpenReleaseNotes(), _ => CanOpenReleaseNotes());
            OpenReleaseNotesCommand = _openReleaseNotesCommand;
            _openGameProfileManagerCommand = new RelayCommand(_ => OpenGameProfileManager());
            OpenGameProfileManagerCommand = _openGameProfileManagerCommand;
            ExportConfigurationCommand = new AsyncRelayCommand(_ => ExportConfigurationAsync());
            ImportConfigurationCommand = new AsyncRelayCommand(_ => ImportConfigurationAsync());

            _logging.LogEmitted += HandleLogLine;

            HydrateCollections();
            _fanService.Start();
            _undervoltService.Start();
            _ = _undervoltService.RefreshAsync();
            _hardwareMonitoringService.Start();
            OnPropertyChanged(nameof(MonitoringLowOverheadMode));
            OnPropertyChanged(nameof(MonitoringGraphsVisible));
            _monitoringInitialized = true;
            _macroBufferNotifier = RecordingBuffer as INotifyCollectionChanged;
            if (_macroBufferNotifier != null)
            {
                _macroBufferNotifier.CollectionChanged += RecordingBufferOnCollectionChanged;
            }
            
            // Configure background update checks
            var updatePrefs = _config.Updates ?? new UpdatePreferences();
            _autoUpdateService.ConfigureBackgroundChecks(updatePrefs);
            
            // Check for updates on startup if enabled
            if (updatePrefs.CheckOnStartup)
            {
                _ = CheckForUpdatesBannerAsync();
            }
            
            // Initialize game profile system
            InitializeGameProfilesAsync();
        }

        private async void InitializeGameProfilesAsync()
        {
            try
            {
                // Hook into profile apply events
                _gameProfileService.ProfileApplyRequested += OnProfileApplyRequested;
                _gameProfileService.ActiveProfileChanged += OnActiveProfileChanged;
                
                // Initialize and start monitoring
                await _gameProfileService.InitializeAsync();
                _logging.Info("Game profile system initialized");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to initialize game profile system", ex);
            }
        }

        private void OnActiveProfileChanged(object? sender, EventArgs e)
        {
            var profile = _gameProfileService.ActiveProfile;
            if (profile != null)
            {
                _logging.Info($"Active profile: {profile.Name}");
                // Update dashboard if needed
                if (Dashboard != null)
                {
                    // Dashboard.CurrentGameProfile = profile.Name;
                }
            }
        }

        private async void OnProfileApplyRequested(object? sender, ProfileApplyEventArgs e)
        {
            try
            {
                if (e.Trigger == ProfileTrigger.GameExit)
                {
                    _logging.Info("Game exited - restoring default settings");
                    await RestoreDefaultSettingsAsync();
                }
                else if (e.Profile != null)
                {
                    _logging.Info($"Applying profile '{e.Profile.Name}' (trigger: {e.Trigger})");
                    await ApplyGameProfileAsync(e.Profile);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply game profile: {ex.Message}", ex);
            }
        }

        private async Task ApplyGameProfileAsync(GameProfile profile)
        {
            // Apply fan preset
            if (!string.IsNullOrEmpty(profile.FanPresetName) && FanControl != null)
            {
                var preset = FanControl.FanPresets.FirstOrDefault(p => p.Name == profile.FanPresetName);
                if (preset != null)
                {
                    FanControl.SelectedPreset = preset;
                    _logging.Info($"Applied fan preset: {preset.Name}");
                }
            }

            // Apply performance mode
            if (!string.IsNullOrEmpty(profile.PerformanceModeName) && SystemControl != null)
            {
                var mode = SystemControl.PerformanceModes.FirstOrDefault(m => m.Name == profile.PerformanceModeName);
                if (mode != null)
                {
                    SystemControl.SelectedPerformanceMode = mode;
                    _logging.Info($"Applied performance mode: {mode.Name}");
                }
            }

            // Apply CPU undervolt
            if (profile.CpuCoreOffsetMv.HasValue && SystemControl != null)
            {
                SystemControl.RequestedCoreOffset = profile.CpuCoreOffsetMv.Value;
                SystemControl.RequestedCacheOffset = profile.CpuCacheOffsetMv ?? profile.CpuCoreOffsetMv.Value;
                // Trigger apply command
                if (SystemControl.ApplyUndervoltCommand?.CanExecute(null) == true)
                {
                    SystemControl.ApplyUndervoltCommand.Execute(null);
                }
                _logging.Info($"Applied undervolt: Core={profile.CpuCoreOffsetMv}mV, Cache={profile.CpuCacheOffsetMv ?? profile.CpuCoreOffsetMv}mV");
            }

            // Apply GPU mode
            if (profile.GpuMode.HasValue && SystemControl != null)
            {
                SystemControl.SelectedGpuMode = profile.GpuMode.Value;
                // Trigger GPU switch via command
                if (SystemControl.SwitchGpuModeCommand?.CanExecute(null) == true)
                {
                    SystemControl.SwitchGpuModeCommand.Execute(null);
                }
                _logging.Info($"Applied GPU mode: {profile.GpuMode}");
            }

            // Apply Corsair lighting
            if (!string.IsNullOrEmpty(profile.PeripheralLightingProfileName) && Lighting != null)
            {
                var preset = Lighting.CorsairLightingPresets.FirstOrDefault(p => p.Name == profile.PeripheralLightingProfileName);
                if (preset != null && Lighting.CorsairDevices.Any())
                {
                    Lighting.SelectedCorsairPreset = preset;
                    // Apply to all Corsair devices
                    foreach (var device in Lighting.CorsairDevices)
                    {
                        Lighting.SelectedCorsairDevice = device;
                        if (Lighting.ApplyCorsairLightingCommand?.CanExecute(null) == true)
                        {
                            Lighting.ApplyCorsairLightingCommand.Execute(null);
                        }
                    }
                    _logging.Info($"Applied Corsair lighting: {preset.Name}");
                }
            }

            _logging.Info($"✓ Profile '{profile.Name}' applied successfully");
        }

        private async Task RestoreDefaultSettingsAsync()
        {
            // Restore to balanced defaults
            if (FanControl != null)
            {
                var balanced = FanControl.FanPresets.FirstOrDefault(p => p.Name == "Balanced");
                if (balanced != null)
                {
                    FanControl.SelectedPreset = balanced;
                }
            }

            if (SystemControl != null)
            {
                var balanced = SystemControl.PerformanceModes.FirstOrDefault(m => m.Name == "Balanced");
                if (balanced != null)
                {
                    SystemControl.SelectedPerformanceMode = balanced;
                }
            }

            _logging.Info("✓ Restored default settings");
        }

        private void OpenGameProfileManager()
        {
            try
            {
                var window = new GameProfileManagerView
                {
                    Owner = Application.Current.MainWindow,
                    DataContext = new GameProfileManagerViewModel(_gameProfileService, _logging)
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to open game profile manager", ex);
                MessageBox.Show($"Failed to open profile manager: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckForUpdatesBannerAsync(bool showStatus = false)
        {
            try
            {
                if (showStatus)
                {
                    UpdateBannerVisible = true;
                    UpdateBannerMessage = "Checking for updates...";
                }

                var result = await _autoUpdateService.CheckForUpdatesAsync();
                
                // Update last check time
                if (_config.Updates != null)
                {
                    _config.Updates.LastCheckTime = DateTime.Now;
                    _configService.Save(_config);
                }
                
                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    // Check if version is skipped
                    if (_config.Updates?.SkippedVersion == result.LatestVersion.VersionString)
                    {
                        _logging.Info($"Update v{result.LatestVersion.VersionString} available but skipped by user");
                        return;
                    }
                    
                    _availableUpdate = result.LatestVersion;
                    _updateInstallBlocked = false;
                    UpdateBannerMessage = $"Update available: v{_availableUpdate.VersionString} (Current {AppVersionLabel})";
                    UpdateBannerVisible = true;
                }
                else
                {
                    _availableUpdate = null;
                    _updateInstallBlocked = false;
                    if (showStatus)
                    {
                        UpdateBannerMessage = "You are running the latest version.";
                        UpdateBannerVisible = true;
                        // Auto-hide after 3 seconds
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(3000);
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                if (UpdateBannerMessage == "You are running the latest version.")
                                {
                                    UpdateBannerVisible = false;
                                    UpdateBannerMessage = string.Empty;
                                }
                            });
                        });
                    }
                    else
                    {
                        UpdateBannerVisible = false;
                        UpdateBannerMessage = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Update check failed: {ex.Message}");
            }
            finally
            {
                RefreshUpdateCommands();
            }
        }

        private async Task InstallUpdateAsync()
        {
            if (_availableUpdate == null)
            {
                return;
            }

            try
            {
                UpdateDownloadInProgress = true;
                UpdateDownloadProgress = 0;
                UpdateBannerMessage = $"Downloading v{_availableUpdate.VersionString} ({_availableUpdate.FileSizeFormatted})";
                UpdateDownloadStatus = "Initializing download...";
                
                _logging.Info($"Starting update download: v{_availableUpdate.VersionString}");
                
                var installerPath = await _autoUpdateService.DownloadUpdateAsync(_availableUpdate);
                
                if (installerPath == null)
                {
                    var hashMissing = string.IsNullOrWhiteSpace(_availableUpdate?.Sha256Hash);
                    UpdateBannerMessage = hashMissing
                        ? "Update requires manual download (missing SHA256 in release notes)."
                        : "Download failed. Check Release Notes for manual download.";
                    UpdateDownloadStatus = hashMissing ? "Install blocked until SHA256 is provided" : "Download failed";
                    _updateInstallBlocked = hashMissing;
                    _logging.Warn("Update download unavailable; missing hash or download error");
                    RefreshUpdateCommands();
                    return;
                }

                UpdateBannerMessage = "Installing update...";
                UpdateDownloadStatus = "Launching installer...";
                
                _logging.Info($"Installing update from {Path.GetFileName(installerPath)}");
                
                var installResult = await _autoUpdateService.InstallUpdateAsync(installerPath);
                
                if (!installResult.Success)
                {
                    UpdateBannerMessage = installResult.Message;
                    UpdateDownloadStatus = "Installation failed";
                    _logging.Error($"Update installation failed: {installResult.Message}");
                }
                else
                {
                    _logging.Info("Update installer launched - Application will restart");
                }
            }
            catch (System.Security.SecurityException ex)
            {
                _logging.Error("Update security verification failed", ex);
                UpdateBannerMessage = "Security verification failed";
                UpdateDownloadStatus = "Hash verification failed - update rejected for security";
            }
            catch (Exception ex)
            {
                _logging.Error("Update installation failed", ex);
                UpdateBannerMessage = $"Update failed: {ex.Message}";
                UpdateDownloadStatus = "Error occurred";
            }
            finally
            {
                UpdateDownloadInProgress = false;
                UpdateDownloadProgress = 0;
                RefreshUpdateCommands();
            }
        }
        
        private void OnUpdateDownloadProgressChanged(object? sender, UpdateDownloadProgress progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateDownloadProgress = progress.ProgressPercent;
                UpdateDownloadStatus = $"{progress.ProgressPercent:F1}% • {progress.DownloadSpeedMbps:F2} MB/s • {FormatTimeSpan(progress.EstimatedTimeRemaining)} remaining";
            });
        }
        
        private void OnBackgroundUpdateCheckCompleted(object? sender, UpdateCheckResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    _availableUpdate = result.LatestVersion;
                    _updateInstallBlocked = false;
                    UpdateBannerVisible = true;
                    UpdateBannerMessage = $"v{result.LatestVersion.VersionString} is now available";
                    _logging.Info($"Background check found update: v{result.LatestVersion.VersionString}");
                    RefreshUpdateCommands();
                }
            });
        }
        
        private static string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{span.Hours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1)
                return $"{span.Minutes}m {span.Seconds}s";
            return $"{span.Seconds}s";
        }

        private bool CanOpenReleaseNotes() => _availableUpdate != null && !string.IsNullOrWhiteSpace(_availableUpdate.ChangelogUrl);

        private bool CanInstallUpdate() => _availableUpdate != null && !_updateDownloadInProgress && !_updateInstallBlocked;

        private void OpenReleaseNotes()
        {
            if (!CanOpenReleaseNotes())
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _availableUpdate!.ChangelogUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to open release notes", ex);
            }
        }

        private void RefreshUpdateCommands()
        {
            _installUpdateCommand.RaiseCanExecuteChanged();
            _openReleaseNotesCommand.RaiseCanExecuteChanged();
            _checkForUpdatesCommand.RaiseCanExecuteChanged();
        }

        private void HydrateCollections()
        {
            FanPresets.Clear();
            foreach (var preset in _config.FanPresets)
            {
                FanPresets.Add(preset);
            }
            SelectedPreset = FanPresets.FirstOrDefault();

            PerformanceModes.Clear();
            foreach (var mode in _config.PerformanceModes)
            {
                PerformanceModes.Add(mode);
            }
            SelectedPerformanceMode = PerformanceModes.FirstOrDefault();

            LightingProfiles.Clear();
            foreach (var profile in _config.LightingProfiles)
            {
                LightingProfiles.Add(profile);
            }
            SelectedLightingProfile = LightingProfiles.FirstOrDefault();

            SystemToggles.Clear();
            foreach (var toggle in _config.SystemToggles)
            {
                SystemToggles.Add(toggle);
            }

            CorsairDevices.Clear();
            if (_corsairDeviceService != null)
            {
                foreach (var device in _corsairDeviceService.Devices)
                {
                    CorsairDevices.Add(device);
                }
            }

            CorsairLightingPresets.Clear();
            foreach (var preset in _config.CorsairLightingPresets)
            {
                CorsairLightingPresets.Add(preset);
            }
            SelectedCorsairPreset = CorsairLightingPresets.FirstOrDefault();

            LogitechDevices.Clear();
            if (_logitechDeviceService != null)
            {
                foreach (var device in _logitechDeviceService.Devices)
                {
                    LogitechDevices.Add(device);
                }
            }
            SelectedLogitechDevice = LogitechDevices.FirstOrDefault();

            MacroProfiles.Clear();
            foreach (var macro in _config.MacroProfiles)
            {
                MacroProfiles.Add(macro);
            }
            SelectedMacroProfile = MacroProfiles.FirstOrDefault();
        }

        private void LoadCurve(FanPreset preset)
        {
            CustomFanCurve.Clear();
            foreach (var point in preset.Curve)
            {
                CustomFanCurve.Add(new FanCurvePoint { TemperatureC = point.TemperatureC, FanPercent = point.FanPercent });
            }
        }

        private void ApplySelectedPreset()
        {
            if (SelectedPreset == null)
            {
                return;
            }
            _fanService.ApplyPreset(SelectedPreset);
            PushEvent($"Preset '{SelectedPreset.Name}' applied");
        }

        private void SaveCustomPreset()
        {
            var preset = new FanPreset
            {
                Name = string.IsNullOrWhiteSpace(CustomPresetName) ? "Custom" : CustomPresetName,
                Curve = CustomFanCurve.Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent }).ToList(),
                IsBuiltIn = false
            };
            FanPresets.Add(preset);
            _config.FanPresets.Add(preset);
            _configService.Save(_config);
            PushEvent($"Custom preset '{preset.Name}' saved");
        }

        private void ApplyPerformanceMode()
        {
            if (SelectedPerformanceMode == null)
            {
                return;
            }
            _performanceModeService.Apply(SelectedPerformanceMode);
            PushEvent($"Performance mode '{SelectedPerformanceMode.Name}' applied");
        }

        private async Task ApplyLightingProfile()
        {
            if (SelectedLightingProfile == null)
            {
                return;
            }
            _keyboardLightingService.ApplyProfile(SelectedLightingProfile);
            if (_corsairDeviceService != null)
            {
                await _corsairDeviceService.SyncWithThemeAsync(SelectedLightingProfile);
            }
            PushEvent($"Lighting profile '{SelectedLightingProfile.Name}' pushed");
        }

        private void RestoreDefaults()
        {
            _systemOptimizationService.RestoreDefaults();
            _keyboardLightingService.RestoreDefaults();
            _fanService.ApplyPreset(FanPresets.First());
            GamingModeActive = false;
            PushEvent("Defaults restored");
        }

        private void ReloadConfiguration()
        {
            var cfg = _configService.Load();
            _config.FanPresets = cfg.FanPresets;
            _config.PerformanceModes = cfg.PerformanceModes;
            _config.SystemToggles = cfg.SystemToggles;
            _config.LightingProfiles = cfg.LightingProfiles;
            _config.CorsairLightingPresets = cfg.CorsairLightingPresets;
            _config.MacroProfiles = cfg.MacroProfiles;
            HydrateCollections();
            PushEvent("Configuration reloaded");
        }

        private void OpenConfigFolder()
        {
            var folder = _configService.GetConfigFolder();
            if (Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }

        private async Task DiscoverCorsairDevices()
        {
            if (_corsairDeviceService == null) return;
            await _corsairDeviceService.DiscoverAsync();
            CorsairDevices.Clear();
            foreach (var device in _corsairDeviceService.Devices)
            {
                CorsairDevices.Add(device);
            }
            SelectedCorsairDevice = CorsairDevices.FirstOrDefault();
        }

        private async Task ApplyCorsairLighting()
        {
            if (_corsairDeviceService == null || SelectedCorsairDevice == null || SelectedCorsairPreset == null)
            {
                return;
            }
            await _corsairDeviceService.ApplyLightingPresetAsync(SelectedCorsairDevice, SelectedCorsairPreset);
            PushEvent($"Corsair preset '{SelectedCorsairPreset.Name}' applied to {SelectedCorsairDevice.Name}");
        }

        private async Task SaveCorsairDpi()
        {
            if (_corsairDeviceService == null || SelectedCorsairDevice == null)
            {
                return;
            }
            await _corsairDeviceService.ApplyDpiStagesAsync(SelectedCorsairDevice, EditableDpiStages);
            PushEvent($"DPI stages updated for {SelectedCorsairDevice.Name}");
        }

        private async Task ApplyMacroToDevice()
        {
            if (_corsairDeviceService == null || SelectedCorsairDevice == null || SelectedMacroProfile == null)
            {
                return;
            }
            await _corsairDeviceService.ApplyMacroProfileAsync(SelectedCorsairDevice, SelectedMacroProfile);
            PushEvent($"Macro '{SelectedMacroProfile.Name}' applied to {SelectedCorsairDevice.Name}");
        }

        private async Task SyncCorsairWithTheme()
        {
            if (_corsairDeviceService == null) return;
            var profile = SelectedLightingProfile ?? LightingProfiles.FirstOrDefault();
            if (profile == null)
            {
                return;
            }
            await _corsairDeviceService.SyncWithThemeAsync(profile);
            PushEvent($"Corsair devices synced with '{profile.Name}' theme");
        }

        private void StartMacroRecording()
        {
            _macroService.StartRecording();
            IsMacroRecording = true;
            PushEvent("Macro recording started");
        }

        private void StopMacroRecording()
        {
            _macroService.StopRecording();
            IsMacroRecording = false;
            PushEvent("Macro recording stopped");
        }

        private void SaveRecordedMacro()
        {
            if (RecordingBuffer.Count == 0)
            {
                return;
            }
            var profile = _macroService.BuildProfile(string.IsNullOrWhiteSpace(NewMacroName) ? "Recorded Macro" : NewMacroName);
            MacroProfiles.Add(profile);
            _config.MacroProfiles.Add(profile);
            _configService.Save(_config);
            SelectedMacroProfile = profile;
            PushEvent($"Macro '{profile.Name}' saved");
        }

        private async Task DiscoverLogitechDevices()
        {
            if (_logitechDeviceService == null) return;
            await _logitechDeviceService.DiscoverAsync();
            LogitechDevices.Clear();
            foreach (var device in _logitechDeviceService.Devices)
            {
                LogitechDevices.Add(device);
            }
            SelectedLogitechDevice = LogitechDevices.FirstOrDefault();
            PushEvent($"Discovered {LogitechDevices.Count} Logitech device(s)");
        }

        private async Task ApplyLogitechColor()
        {
            if (_logitechDeviceService == null || SelectedLogitechDevice == null)
            {
                return;
            }
            await _logitechDeviceService.ApplyStaticColorAsync(SelectedLogitechDevice, LogitechColorHex, LogitechBrightness);
            PushEvent($"Logitech {SelectedLogitechDevice.Name} color updated");
        }

        private OmenCleanupOptions BuildCleanupOptions() => new()
        {
            RemoveStorePackage = CleanupRemoveStorePackage,
            RemoveLegacyInstallers = CleanupRemoveLegacyInstallers,
            RemoveRegistryTraces = CleanupRemoveRegistry,
            RemoveResidualFiles = CleanupRemoveFiles,
            RemoveServicesAndTasks = CleanupRemoveServices,
            KillRunningProcesses = CleanupKillProcesses,
            PreserveFirewallRules = CleanupPreserveFirewall,
            DryRun = CleanupDryRun
        };

        private async Task CreateRestorePointAsync()
        {
            RestorePointInProgress = true;
            var label = $"OmenCore safeguard {DateTime.Now:yyyy-MM-dd HH:mm}";
            RestorePointStatus = "Creating restore point...";
            try
            {
                var result = await _systemRestoreService.CreateRestorePointAsync(label);
                if (result.Success)
                {
                    RestorePointStatus = $"Restore point #{result.SequenceNumber} created";
                    PushEvent("System restore point created");
                }
                else
                {
                    RestorePointStatus = $"Restore point failed: {result.Message}";
                    PushEvent("Restore point creation failed");
                }
            }
            catch (Exception ex)
            {
                RestorePointStatus = $"Restore point failed: {ex.Message}";
                _logging.Error("Unhandled restore point failure", ex);
                PushEvent("Restore point creation failed");
            }
            finally
            {
                RestorePointInProgress = false;
            }
        }

        private async Task RunOmenCleanupAsync()
        {
            CleanupInProgress = true;
            CleanupStatus = CleanupDryRun ? "Running cleanup dry run..." : "Removing OMEN Gaming Hub...";
            OmenCleanupSteps.Clear();
            try
            {
                var options = BuildCleanupOptions();
                var result = await _hubCleanupService.CleanupAsync(options);
                foreach (var step in result.Steps)
                {
                    OmenCleanupSteps.Add(step);
                }
                foreach (var warning in result.Warnings)
                {
                    OmenCleanupSteps.Add($"Warning: {warning}");
                }
                foreach (var error in result.Errors)
                {
                    OmenCleanupSteps.Add($"Error: {error}");
                }

                if (result.Success)
                {
                    CleanupStatus = CleanupDryRun ? "Dry run completed" : "OMEN Gaming Hub removed";
                    PushEvent(CleanupDryRun ? "OMEN cleanup dry run completed" : "OMEN Gaming Hub cleanup complete");
                }
                else
                {
                    CleanupStatus = "Cleanup finished with errors";
                    PushEvent("OMEN cleanup completed with errors");
                }
            }
            catch (Exception ex)
            {
                CleanupStatus = $"Cleanup failed: {ex.Message}";
                _logging.Error("OMEN cleanup failed", ex);
                PushEvent("OMEN cleanup failed");
            }
            finally
            {
                CleanupInProgress = false;
            }
        }

        private void HardwareMonitoringServiceOnSampleUpdated(object? sender, MonitoringSample sample)
        {
            Application.Current.Dispatcher.Invoke(() => LatestMonitoringSample = sample);
        }

        private void RecordingBufferOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _saveRecordedMacroInternalCommand?.RaiseCanExecuteChanged();
        }

        private async Task ApplyUndervoltAsync()
        {
            var offset = new UndervoltOffset
            {
                CoreMv = RequestedCoreOffset,
                CacheMv = RequestedCacheOffset
            };
            await _undervoltService.ApplyAsync(offset);
            PushEvent($"CPU undervolt core {offset.CoreMv:+0;-0;0} mV / cache {offset.CacheMv:+0;-0;0} mV");
        }

        private async Task ResetUndervoltAsync()
        {
            await _undervoltService.ResetAsync();
            PushEvent("CPU undervolt reset to defaults");
        }

        private bool CanApplyUndervolt() => !HasExternalUndervolt || !RespectExternalUndervolt;

        private void TakeUndervoltControl()
        {
            RespectExternalUndervolt = false;
            PushEvent("OmenCore now controls CPU undervolt planes");
        }

        private void RespectExternalUndervoltController()
        {
            RespectExternalUndervolt = true;
            PushEvent("Respecting external undervolt controller");
        }

        private void UndervoltServiceOnStatusChanged(object? sender, UndervoltStatus status)
        {
            Application.Current.Dispatcher.Invoke(() => UndervoltStatus = status);
        }

        private void ShowAbout()
        {
            var about = new AboutWindow
            {
                Owner = Application.Current.MainWindow
            };
            about.ShowDialog();
        }

        private void PushEvent(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentEvents.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
                while (RecentEvents.Count > 30)
                {
                    RecentEvents.RemoveAt(RecentEvents.Count - 1);
                }
            });
        }

        private void HandleLogLine(string entry)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _logBuffer.AppendLine(entry);
                var lines = _logBuffer.ToString().Split('\n');
                if (lines.Length > 200)
                {
                    _logBuffer.Clear();
                    foreach (var line in lines.Skip(lines.Length - 200))
                    {
                        _logBuffer.AppendLine(line);
                    }
                }
                OnPropertyChanged(nameof(LogBuffer));
            });
        }

        private async void InitializeServicesAsync()
        {
            try
            {
                _corsairDeviceService = await CorsairDeviceService.CreateAsync(_logging);
                _logitechDeviceService = await LogitechDeviceService.CreateAsync(_logging);
                
                await DiscoverCorsairDevices();
                await DiscoverLogitechDevices();
                
                // Initialize Lighting sub-ViewModel after async services are ready
                if (_corsairDeviceService != null && _logitechDeviceService != null)
                {
                    Lighting = new LightingViewModel(_corsairDeviceService, _logitechDeviceService, _logging);
                    OnPropertyChanged(nameof(Lighting));
                    _logging.Info("Lighting sub-ViewModel initialized");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to initialize peripheral services", ex);
            }
        }
        
        private void InitializeSubViewModels()
        {
            // Initialize FanControl sub-ViewModel
            FanControl = new FanControlViewModel(_fanService, _configService, _logging);
            OnPropertyChanged(nameof(FanControl));
            
            // Initialize SystemControl sub-ViewModel
            SystemControl = new SystemControlViewModel(
                _undervoltService,
                _performanceModeService,
                _hubCleanupService,
                _systemRestoreService,
                _gpuSwitchService,
                _logging
            );
            SystemControl.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemControl.CurrentPerformanceModeName) && Dashboard != null)
                {
                    Dashboard.CurrentPerformanceMode = SystemControl.CurrentPerformanceModeName;
                }
            };
            OnPropertyChanged(nameof(SystemControl));
            
            // Initialize Dashboard sub-ViewModel
            Dashboard = new DashboardViewModel(_hardwareMonitoringService);
            Dashboard.CurrentPerformanceMode = SystemControl.CurrentPerformanceModeName;
            OnPropertyChanged(nameof(Dashboard));
            
            // Wire up fan mode changes
            FanControl.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FanControl.CurrentFanModeName) && Dashboard != null)
                {
                    Dashboard.CurrentFanMode = FanControl.CurrentFanModeName;
                }
            };
            Dashboard.CurrentFanMode = FanControl.CurrentFanModeName;
            
            // Initialize Settings sub-ViewModel
            Settings = new SettingsViewModel(_logging, _configService, _systemInfoService, _fanCleaningService);
            OnPropertyChanged(nameof(Settings));
            
            _logging.Info("Sub-ViewModels initialized successfully");
        }

        private void ReloadRecentBuffer()
        {
            _logBuffer.Clear();
            OnPropertyChanged(nameof(LogBuffer));
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private async Task ExportConfigurationAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"OmenCore_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _configService.ExportConfiguration(dialog.FileName, _config);
                    _logging.Info($"Configuration exported to: {dialog.FileName}");
                    PushEvent($"✓ Configuration exported successfully");
                }
                catch (Exception ex)
                {
                    _logging.Error("Failed to export configuration", ex);
                    PushEvent($"✗ Export failed: {ex.Message}");
                }
            }
            await Task.CompletedTask;
        }

        private async Task ImportConfigurationAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Validate before importing
                    if (!_configService.ValidateConfiguration(dialog.FileName))
                    {
                        _logging.Warn("Invalid configuration file format");
                        PushEvent($"✗ Import failed: Invalid file format");
                        return;
                    }

                    var importedConfig = _configService.ImportConfiguration(dialog.FileName);
                    if (importedConfig != null)
                    {
                        _configService.Save(importedConfig);
                        _logging.Info($"Configuration imported from: {dialog.FileName}");
                        PushEvent($"✓ Configuration imported - Restart recommended");
                        
                        // Notify user to restart
                        MessageBox.Show(
                            "Configuration imported successfully!\n\nPlease restart OmenCore for all changes to take effect.",
                            "Import Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error("Failed to import configuration", ex);
                    PushEvent($"✗ Import failed: {ex.Message}");
                }
            }
            await Task.CompletedTask;
        }

        #region Tray Quick Actions

        public void SetFanModeFromTray(string mode)
        {
            _logging.Info($"Fan mode change requested from tray: {mode}");
            try
            {
                FanPreset? targetPreset = mode switch
                {
                    "Max" => FanPresets.FirstOrDefault(p => p.Name.Contains("Max", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Performance", StringComparison.OrdinalIgnoreCase)),
                    "Quiet" => FanPresets.FirstOrDefault(p => p.Name.Contains("Quiet", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Silent", StringComparison.OrdinalIgnoreCase)),
                    _ => FanPresets.FirstOrDefault(p => p.Name.Contains("Auto", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
                };

                if (targetPreset != null)
                {
                    SelectedPreset = targetPreset;
                    _fanService.ApplyPreset(targetPreset);
                    CurrentFanMode = mode;
                    PushEvent($"🌀 Fan mode: {mode}");
                }
                else
                {
                    // No matching preset found, just update state
                    CurrentFanMode = mode;
                    PushEvent($"🌀 Fan mode: {mode} (preset not found)");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set fan mode from tray: {ex.Message}");
            }
        }

        public void SetPerformanceModeFromTray(string mode)
        {
            _logging.Info($"Performance mode change requested from tray: {mode}");
            try
            {
                if (SystemControl != null)
                {
                    var targetMode = SystemControl.PerformanceModes.FirstOrDefault(m => 
                        m.Name.Equals(mode, StringComparison.OrdinalIgnoreCase));

                    if (targetMode != null)
                    {
                        SystemControl.SelectedPerformanceMode = targetMode;
                        SystemControl.ApplyPerformanceModeCommand?.Execute(null);
                        CurrentPerformanceMode = mode;
                        PushEvent($"⚡ Performance: {mode}");
                    }
                }
                else
                {
                    // Direct service call as fallback
                    _performanceModeService.Apply(new PerformanceMode { Name = mode });
                    CurrentPerformanceMode = mode;
                    PushEvent($"⚡ Performance: {mode}");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set performance mode from tray: {ex.Message}");
            }
        }

        #endregion

        #region Hotkey & Notification Handlers

        private void OnGameProfileApplyRequested(object? sender, ProfileApplyEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (e.Profile != null)
                {
                    // Game profile activated
                    _notificationService.ShowGameProfileActivated(e.Profile.ExecutableName, e.Profile.Name);
                    PushEvent($"🎮 Profile: {e.Profile.Name} for {e.Profile.ExecutableName}");
                }
                else if (e.Trigger == ProfileTrigger.GameExit)
                {
                    // Game exited, defaults restored
                    _notificationService.ShowGameProfileDeactivated("Game");
                    PushEvent("🎮 Restored default settings");
                }
            });
        }

        private void OnHotkeyToggleFanMode(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (FanControl == null) return;
                
                // Cycle through fan modes
                var modes = new[] { "Balanced", "Performance", "Quiet" };
                var currentIndex = Array.IndexOf(modes, CurrentFanMode);
                var nextIndex = (currentIndex + 1) % modes.Length;
                var nextMode = modes[nextIndex];
                
                try
                {
                    FanControl.ApplyFanMode(nextMode);
                    CurrentFanMode = nextMode;
                    _notificationService.ShowFanModeChanged(nextMode, "Hotkey");
                    PushEvent($"🌀 Fan: {nextMode} (hotkey)");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Hotkey fan mode change failed: {ex.Message}");
                }
            });
        }

        private void OnHotkeyTogglePerformanceMode(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (SystemControl == null) return;
                
                // Cycle through performance modes
                var modes = new[] { "Balanced", "Performance", "Quiet" };
                var currentIndex = Array.IndexOf(modes, CurrentPerformanceMode);
                var nextIndex = (currentIndex + 1) % modes.Length;
                var nextMode = modes[nextIndex];
                
                try
                {
                    _performanceModeService.Apply(new PerformanceMode { Name = nextMode });
                    CurrentPerformanceMode = nextMode;
                    _notificationService.ShowPerformanceModeChanged(nextMode, "Hotkey");
                    PushEvent($"⚡ Performance: {nextMode} (hotkey)");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Hotkey performance mode change failed: {ex.Message}");
                }
            });
        }

        private void OnHotkeyToggleBoostMode(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    FanControl?.ApplyFanMode("Performance");
                    _performanceModeService.Apply(new PerformanceMode { Name = "Performance" });
                    CurrentFanMode = "Performance";
                    CurrentPerformanceMode = "Performance";
                    _notificationService.ShowFanModeChanged("Boost (Performance)", "Hotkey");
                    PushEvent("🔥 Boost mode activated (hotkey)");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Hotkey boost mode failed: {ex.Message}");
                }
            });
        }

        private void OnHotkeyToggleQuietMode(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    FanControl?.ApplyFanMode("Quiet");
                    _performanceModeService.Apply(new PerformanceMode { Name = "Quiet" });
                    CurrentFanMode = "Quiet";
                    CurrentPerformanceMode = "Quiet";
                    _notificationService.ShowFanModeChanged("Quiet", "Hotkey");
                    PushEvent("🤫 Quiet mode activated (hotkey)");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Hotkey quiet mode failed: {ex.Message}");
                }
            });
        }

        private void OnHotkeyToggleWindow(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null) return;
                
                if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
                {
                    mainWindow.Hide();
                }
                else
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                }
            });
        }

        /// <summary>
        /// Initialize hotkeys after window handle is available
        /// </summary>
        public void InitializeHotkeys(IntPtr windowHandle)
        {
            try
            {
                _hotkeyService.Initialize(windowHandle);
                _hotkeyService.RegisterDefaultHotkeys();
                _logging.Info("Global hotkeys registered");
                PushEvent("⌨️ Global hotkeys enabled");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to register hotkeys: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the notification service for external use
        /// </summary>
        public NotificationService Notifications => _notificationService;

        /// <summary>
        /// Get the hotkey service for external use
        /// </summary>
        public HotkeyService Hotkeys => _hotkeyService;

        #endregion

        public void Dispose()
        {
            _fanService.Dispose();
            _undervoltService.StatusChanged -= UndervoltServiceOnStatusChanged;
            _undervoltService.Dispose();
            _hardwareMonitoringService.SampleUpdated -= HardwareMonitoringServiceOnSampleUpdated;
            _hardwareMonitoringService.Dispose();
            if (_macroBufferNotifier != null)
            {
                _macroBufferNotifier.CollectionChanged -= RecordingBufferOnCollectionChanged;
            }
            _logging.LogEmitted -= HandleLogLine;
            _autoUpdateService.Dispose();

            // Dispose process monitoring and game profile services
            _processMonitoringService?.Dispose();
            _gameProfileService?.Dispose();

            // Dispose hotkey and notification services
            _hotkeyService?.Dispose();
            _notificationService?.Dispose();

            // Dispose device services
            _corsairDeviceService?.Dispose();
            _logitechDeviceService?.Dispose();
        }
    }
}
