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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OmenCore.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        // P/Invoke for reliable window focus
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        
        private const int SW_RESTORE = 9;
        
        private readonly LoggingService _logging = App.Logging;
        private readonly ConfigurationService _configService = App.Configuration;
        private readonly AppConfig _config;
        private readonly FanService _fanService = null!;
        private readonly IFanVerificationService _fanVerificationService = null!;
        private readonly PerformanceModeService _performanceModeService = null!;
        private readonly KeyboardLightingService _keyboardLightingService;
        private readonly SystemOptimizationService _systemOptimizationService;
        private readonly GpuSwitchService _gpuSwitchService;
        private CorsairDeviceService? _corsairDeviceService;
        private LogitechDeviceService? _logitechDeviceService;
        private OmenCore.Razer.RazerService? _razerService;
        private readonly MacroService _macroService = new();
        private readonly UndervoltService _undervoltService;
        private readonly HardwareMonitoringService _hardwareMonitoringService;
        public HardwareMonitoringService HardwareMonitoringService => _hardwareMonitoringService;
        private readonly SystemRestoreService _systemRestoreService;
        private readonly OmenGamingHubCleanupService _hubCleanupService;
        private readonly SystemInfoService _systemInfoService;
        private readonly AutoUpdateService _autoUpdateService;
        private readonly ProcessMonitoringService _processMonitoringService;
        private readonly GameProfileService _gameProfileService;
        private readonly FanCleaningService _fanCleaningService;
        private readonly HotkeyService _hotkeyService;
        private readonly NotificationService _notificationService;
        private readonly BiosUpdateService _biosUpdateService;
        private readonly PowerAutomationService _powerAutomationService;
        private readonly OmenKeyService _omenKeyService;
        private readonly NvapiService? _nvapiService;
        private AmdGpuService? _amdGpuService;
        private OsdService? _osdService;
        private ConflictDetectionService? _conflictDetectionService;
        private HpWmiBios? _wmiBios;
        private OghServiceProxy? _oghProxy;
        private HotkeyOsdWindow? _hotkeyOsd;
        
        // Sub-ViewModels for modular UI (Lazy Loaded)
        private FanControlViewModel? _fanControl;
        public FanControlViewModel? FanControl
        {
            get
            {
                if (_fanControl == null)
                {
                    _fanControl = new FanControlViewModel(_fanService, _configService, _logging);
                    _fanControl.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(FanControlViewModel.CurrentFanModeName))
                        {
                            if (_dashboard != null)
                            {
                                _dashboard.CurrentFanMode = _fanControl.CurrentFanModeName;
                            }
                            // Also update MainViewModel.CurrentFanMode to sync with tray
                            CurrentFanMode = _fanControl.CurrentFanModeName;
                        }
                    };
                    OnPropertyChanged(nameof(FanControl));
                }
                return _fanControl;
            }
        }

        public LightingViewModel? Lighting { get; private set; }

        private SystemControlViewModel? _systemControl;
        public SystemControlViewModel? SystemControl
        {
            get
            {
                if (_systemControl == null)
                {
                    _systemControl = new SystemControlViewModel(
                        _undervoltService,
                        _performanceModeService,
                        _hubCleanupService,
                        _systemRestoreService,
                        _gpuSwitchService,
                        _logging,
                        _configService,
                        _wmiBios,
                        _oghProxy,
                        _systemInfoService,
                        _nvapiService,
                        amdGpuService: _amdGpuService
                    );
                    _systemControl.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SystemControlViewModel.CurrentPerformanceModeName) && _dashboard != null)
                        {
                            _dashboard.CurrentPerformanceMode = _systemControl.CurrentPerformanceModeName;
                            // Also sync to MainViewModel's CurrentPerformanceMode for tray menu
                            CurrentPerformanceMode = _systemControl.CurrentPerformanceModeName;
                        }
                    };
                    OnPropertyChanged(nameof(SystemControl));
                }
                return _systemControl;
            }
        }

        private DashboardViewModel? _dashboard;
        public DashboardViewModel? Dashboard
        {
            get
            {
                if (_dashboard == null)
                {
                    _dashboard = new DashboardViewModel(_hardwareMonitoringService);
                    // Initialize with current values (triggers creation of dependencies if needed)
                    if (SystemControl != null)
                    {
                        _dashboard.CurrentPerformanceMode = SystemControl.CurrentPerformanceModeName;
                        // Sync to tray menu as well
                        CurrentPerformanceMode = SystemControl.CurrentPerformanceModeName;
                    }
                    if (FanControl != null) _dashboard.CurrentFanMode = FanControl.CurrentFanModeName;
                    OnPropertyChanged(nameof(Dashboard));
                }
                return _dashboard;
            }
        }

        private SettingsViewModel? _settings;
        public SettingsViewModel? Settings
        {
            get
            {
                if (_settings == null)
                {
                    // Create services required for SettingsViewModel
                    var profileExportService = new ProfileExportService(_logging, _configService);
                    var diagnosticsExportService = new DiagnosticsExportService(_logging, _configService);
                    
                    _settings = new SettingsViewModel(_logging, _configService, _systemInfoService, 
                        _fanCleaningService, _biosUpdateService, profileExportService, diagnosticsExportService,
                        _wmiBios, _omenKeyService, _osdService, _hardwareMonitoringService, 
                        _powerAutomationService, _fanService);
                    
                    // Subscribe to low overhead mode changes from Settings
                    _settings.LowOverheadModeChanged += (s, enabled) =>
                    {
                        _monitoringLowOverhead = enabled;
                        OnPropertyChanged(nameof(MonitoringLowOverheadMode));
                        OnPropertyChanged(nameof(MonitoringGraphsVisible));
                    };
                    
                    // Subscribe to power state changes to update Settings status
                    _powerAutomationService.PowerStateChanged += (s, e) =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            _settings?.RefreshPowerStatus();
                        });
                    };
                    
                    // Subscribe to suspend/resume events for S0 Modern Standby support
                    _powerAutomationService.SystemSuspending += (s, e) =>
                    {
                        _hardwareMonitoringService?.Pause();
                    };
                    
                    _powerAutomationService.SystemResuming += (s, e) =>
                    {
                        _hardwareMonitoringService?.Resume();
                    };
                    
                    OnPropertyChanged(nameof(Settings));
                }
                return _settings;
            }
        }

        private GeneralViewModel? _general;
        public GeneralViewModel? General
        {
            get
            {
                if (_general == null)
                {
                    _general = new GeneralViewModel(_fanService, _performanceModeService, _configService, _logging, _systemInfoService);
                    // Wire up the FanControlViewModel reference for preset sync
                    _general.SetFanControlViewModel(FanControl);
                    OnPropertyChanged(nameof(General));
                }
                return _general;
            }
        }

        private SystemOptimizerViewModel? _systemOptimizer;
        public SystemOptimizerViewModel? SystemOptimizer
        {
            get
            {
                if (_systemOptimizer == null)
                {
                    _systemOptimizer = new SystemOptimizerViewModel(_logging);
                    OnPropertyChanged(nameof(SystemOptimizer));
                }
                return _systemOptimizer;
            }
        }
        
        private BloatwareManagerViewModel? _bloatwareManager;
        public BloatwareManagerViewModel? BloatwareManager
        {
            get
            {
                if (_bloatwareManager == null)
                {
                    _bloatwareManager = new BloatwareManagerViewModel(_logging);
                    OnPropertyChanged(nameof(BloatwareManager));
                }
                return _bloatwareManager;
            }
        }
        
        private GameLibraryViewModel? _gameLibrary;
        public GameLibraryViewModel? GameLibrary
        {
            get
            {
                if (_gameLibrary == null)
                {
                    var libraryService = new GameLibraryService(_logging);
                    _gameLibrary = new GameLibraryViewModel(_logging, libraryService, _gameProfileService);
                    OnPropertyChanged(nameof(GameLibrary));
                }
                return _gameLibrary;
            }
        }
        
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
        
        /// <summary>
        /// Device capabilities detected at startup.
        /// </summary>
        public Hardware.DeviceCapabilities? DetectedCapabilities { get; private set; }
        
        /// <summary>
        /// The active fan control backend (for UI display).
        /// </summary>
        public string FanBackend { get; private set; } = "Detecting...";
        
        /// <summary>
        /// The active EC access backend (PawnIO or WinRing0).
        /// </summary>
        public string EcBackend { get; private set; } = "None";
        
        /// <summary>
        /// True if Secure Boot is enabled (blocks WinRing0).
        /// </summary>
        public bool SecureBootEnabled => DetectedCapabilities?.SecureBootEnabled ?? false;
        
        /// <summary>
        /// Warning message for limited functionality.
        /// </summary>
        public string? CapabilityWarning { get; private set; }

        /// <summary>
        /// Power automation service for AC/Battery profile switching.
        /// Exposed for Settings UI binding.
        /// </summary>
        public PowerAutomationService PowerAutomation => _powerAutomationService;
        
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
        
        // v2.7.0: GPU Power and Keyboard state for tray sync
        private string _currentGpuPowerLevel = "Medium";
        private int _currentKeyboardBrightness = 3;
        
        public string CurrentGpuPowerLevel
        {
            get => _currentGpuPowerLevel;
            set
            {
                if (_currentGpuPowerLevel != value)
                {
                    _currentGpuPowerLevel = value;
                    OnPropertyChanged(nameof(CurrentGpuPowerLevel));
                }
            }
        }
        
        public int CurrentKeyboardBrightness
        {
            get => _currentKeyboardBrightness;
            set
            {
                if (_currentKeyboardBrightness != value)
                {
                    _currentKeyboardBrightness = value;
                    OnPropertyChanged(nameof(CurrentKeyboardBrightness));
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
                
                // v2.6.1: Push update to GeneralViewModel for enhanced General tab
                _general?.UpdateFromMonitoringSample(value);
                
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

        // Expose Fan Diagnostics VM
        public FanDiagnosticsViewModel FanDiagnostics { get; private set; }

        // Expose Keyboard Diagnostics VM
        public KeyboardDiagnosticsViewModel KeyboardDiagnostics { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _config = _configService.Load();
            
            // Initialize hardware monitor bridge
            // Priority: WmiBiosMonitor (no dependencies) -> LibreHardwareMonitor (if available for enhanced metrics)
            IHardwareMonitorBridge monitorBridge;
            LibreHardwareMonitorImpl? libreHwMonitor = null;
            
            // Try WmiBiosMonitor first - it's self-sufficient with no external dependencies
            var wmiBiosMonitor = new WmiBiosMonitor(_logging);
            
            // If battery monitoring is disabled in config, prevent all battery WMI queries
            // This prevents EC timeout errors on systems with dead/removed batteries
            if (_config.Battery?.DisableMonitoring == true)
            {
                wmiBiosMonitor.DisableBatteryMonitoring();
                _logging.Info("⚡ Battery monitoring disabled by config (Battery.DisableMonitoring=true)");
            }
            
            if (wmiBiosMonitor.IsAvailable)
            {
                _logging.Info("✓ WMI BIOS monitoring available (self-sufficient mode)");
                
                // Optionally try LibreHardwareMonitor for enhanced metrics (GPU clocks, power, VRAM, etc.)
                try
                {
                    libreHwMonitor = new LibreHardwareMonitorImpl(
                        msg => _logging.Debug($"[LibreHW] {msg}"),
                        useWorker: true);
                    _logging.Info("✓ LibreHardwareMonitor available for enhanced metrics");
                    monitorBridge = libreHwMonitor; // Use LibreHW if available (has more metrics)
                    
                    // Disable battery monitoring in worker if config says so
                    if (_config.Battery?.DisableMonitoring == true)
                    {
                        _ = libreHwMonitor.DisableBatteryMonitoringAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logging.Info($"LibreHardwareMonitor unavailable ({ex.Message}) - using WMI BIOS only");
                    monitorBridge = wmiBiosMonitor;
                }
            }
            else
            {
                _logging.Warn("WMI BIOS monitoring not available - trying LibreHardwareMonitor");
                
                // Fall back to LibreHardwareMonitor
                try
                {
                    libreHwMonitor = new LibreHardwareMonitorImpl(
                        msg => _logging.Info($"[Monitor] {msg}"),
                        useWorker: true);
                    monitorBridge = libreHwMonitor;
                    
                    if (_config.Battery?.DisableMonitoring == true)
                    {
                        _ = libreHwMonitor.DisableBatteryMonitoringAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"No monitoring available: {ex.Message}");
                    // Use WmiBiosMonitor even if not fully available - it will return zeros
                    monitorBridge = wmiBiosMonitor;
                }
            }
            
            // Run capability detection to identify available backends
            var capabilityService = new CapabilityDetectionService(_logging);
            var capabilities = capabilityService.DetectCapabilities();
            DetectedCapabilities = capabilities;
            
            // Set capability warning if functionality is limited
            if (capabilities.IsDesktop)
            {
                CapabilityWarning = $"Desktop PC detected ({capabilities.Chassis}). Fan control uses WMI — EC-based curves are not available on desktops.";
                _logging.Info("Desktop OMEN PC — WMI fan control active. Desktop RGB available via USB HID.");
            }
            else if (capabilities.SecureBootEnabled && !capabilities.OghRunning)
            {
                CapabilityWarning = "Secure Boot enabled - some features may be limited. Install OMEN Gaming Hub for full control.";
            }
            else if (capabilities.FanControl == Hardware.FanControlMethod.MonitoringOnly)
            {
                CapabilityWarning = "Fan control unavailable - monitoring only mode.";
            }
            
            // Initialize EC access with automatic backend selection
            // Tries PawnIO first (Secure Boot compatible), then WinRing0
            IEcAccess? ec = null;
            try
            {
                ec = EcAccessFactory.GetEcAccess();
                if (ec != null && ec.IsAvailable)
                {
                    _logging.Info($"EC access initialized: {EcAccessFactory.GetStatusMessage()}");
                    EcBackend = EcAccessFactory.ActiveBackend.ToString();
                }
                else
                {
                    _logging.Info("EC access not available; will try WMI BIOS for fan control");
                    EcBackend = "None";
                }
            }
            catch (Exception ex)
            {
                _logging.Info($"EC access initialization skipped: {ex.Message}");
                EcBackend = "Error";
            }

            // Create fan controller with intelligent backend selection using pre-detected capabilities
            // Priority: OGH Proxy > WMI BIOS (no driver) > EC (requires WinRing0) > Fallback (monitoring only)
            var fanControllerFactory = new FanControllerFactory(monitorBridge, ec, _config.EcFanRegisterMap, _logging, capabilities, _config.MaxFanLevelOverride);
            var fanController = fanControllerFactory.Create();
            FanBackend = fanControllerFactory.ActiveBackend;
            
            // Create HP WMI BIOS instance for GPU Power Boost control
            _wmiBios = new HpWmiBios(_logging);
            
            // Create OGH Proxy for systems where WMI BIOS commands don't work
            // This is common on 2023+ OMEN laptops with Secure Boot enabled
            _oghProxy = new OghServiceProxy(_logging);
            
            // Run OGH diagnostics on startup to help debug command issues
            if (_oghProxy.Status.WmiAvailable && _config.EnableDiagnostics)
            {
                _logging.Info("Running OGH command diagnostics...");
                _oghProxy.RunDiagnostics();
            }
            
            // Create notification service early (before FanService which needs it)
            _notificationService = new NotificationService(_logging);
            
            _fanService = new FanService(fanController, new ThermalSensorProvider(monitorBridge), _logging, _notificationService, _config.MonitoringIntervalMs);
            _fanService.SetHysteresis(_config.FanHysteresis);
            _fanService.ThermalProtectionEnabled = _config.FanHysteresis?.ThermalProtectionEnabled ?? true;
            // Configure smoothing/transition settings for fan ramping
            _fanService.SetSmoothingSettings(_config.FanTransition);
            ThermalSamples = _fanService.ThermalSamples;
            FanTelemetry = _fanService.FanTelemetry;
            var powerPlanService = new PowerPlanService(_logging);

            // Fan verification service (closed-loop verification)
            _fanVerificationService = new FanVerificationService(_wmiBios, _fanService, _logging);
            FanDiagnostics = new FanDiagnosticsViewModel(_fanVerificationService, _fanService, _logging);

            // Keyboard diagnostics
            KeyboardDiagnostics = new KeyboardDiagnosticsViewModel(_corsairDeviceService, _logitechDeviceService, _keyboardLightingService, _razerService, _logging);
            
            // Power limit controller (EC-based CPU/GPU power control)
            PowerLimitController? powerLimitController = null;
            if (ec != null && ec.IsAvailable)
            {
                try
                {
                    powerLimitController = new PowerLimitController(ec, useSimplifiedMode: true);
                    _logging.Info("✓ Power limit controller initialized (simplified mode)");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Power limit controller unavailable: {ex.Message}");
                }
            }
            else
            {
                _logging.Info("Power limit controller skipped (EC not available)");
            }
            
            // Enable experimental EC keyboard writes if user has opted in
            if (_config.ExperimentalEcKeyboardEnabled)
            {
                Hardware.PawnIOEcAccess.EnableExperimentalKeyboardWrites = true;
                _logging.Info("⚠️ Experimental EC keyboard writes ENABLED (user opted in)");
            }
            
            _performanceModeService = new PerformanceModeService(fanController, powerPlanService, powerLimitController, _logging);
            _keyboardLightingService = new KeyboardLightingService(_logging, ec, _wmiBios, _configService);
            _systemOptimizationService = new SystemOptimizationService(_logging);
            _gpuSwitchService = new GpuSwitchService(_logging);
            
            // Initialize NVAPI for GPU overclocking/tuning
            try
            {
                _nvapiService = new NvapiService(_logging);
            }
            catch (Exception ex)
            {
                _logging.Warn($"NVAPI service initialization failed: {ex.Message}");
                _nvapiService = null;
            }
            
            // Initialize AMD GPU service (ADL2) for AMD GPU overclocking
            try
            {
                _amdGpuService = new AmdGpuService(_logging);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var success = await _amdGpuService.InitializeAsync();
                        if (!success)
                        {
                            _amdGpuService = null;
                            _logging.Info("AMD GPU service: No AMD discrete GPU found or ADL not available");
                        }
                    }
                    catch (Exception ex)
                    {
                        _amdGpuService = null;
                        _logging.Warn($"AMD GPU service async init failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"AMD GPU service initialization failed: {ex.Message}");
                _amdGpuService = null;
            }
            
            // Services initialized asynchronously
            InitializeServicesAsync();

            // Auto-detect CPU vendor (Intel/AMD) and create appropriate undervolt provider
            var undervoltProvider = CpuUndervoltProviderFactory.Create(out string undervoltBackend);
            _logging.Info($"CPU undervolt provider: {undervoltBackend}");
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
            
            // Enable WMI BIOS fallback for temperature freeze recovery (v2.7.0)
            _hardwareMonitoringService.SetWmiBiosService(_wmiBios);
            
            _systemRestoreService = new SystemRestoreService(_logging);
            _hubCleanupService = new OmenGamingHubCleanupService(_logging);
            _systemInfoService = new SystemInfoService(_logging);
            SystemInfo = _systemInfoService.GetSystemInfo();
            _autoUpdateService = new AutoUpdateService(_logging);
            _processMonitoringService = new ProcessMonitoringService(_logging);
            _gameProfileService = new GameProfileService(_logging, _processMonitoringService, _configService);
            _fanCleaningService = new FanCleaningService(_logging, ec, _systemInfoService, _wmiBios, _oghProxy);
            _biosUpdateService = new BiosUpdateService(_logging);
            _hotkeyService = new HotkeyService(_logging);
            // _notificationService created earlier (before FanService)
            _powerAutomationService = new PowerAutomationService(_logging, _fanService, _performanceModeService, _configService, _gpuSwitchService);
            _omenKeyService = new OmenKeyService(_logging, _configService);
            
            // Initialize OSD service (will only activate if enabled in settings)
            // Pass ThermalProvider from FanService for temperature data
            _osdService = new OsdService(_configService, _logging, _fanService?.ThermalProvider, _fanService);
            
            // Initialize conflict detection service for detecting conflicting software
            _conflictDetectionService = new ConflictDetectionService(_logging);
            _conflictDetectionService.OnConflictsDetected += (conflicts) =>
            {
                if (conflicts.Count > 0)
                {
                    _logging.Warn($"Detected {conflicts.Count} conflicting application(s): {_conflictDetectionService.GetConflictSummary()}");
                }
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    await _conflictDetectionService.ScanForConflictsAsync();
                    // Monitor every 60 seconds in the background
                    await _conflictDetectionService.MonitorConflictsAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
                }
                catch (Exception ex) { _logging.Warn($"Conflict detection failed: {ex.Message}"); }
            });
            
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
            
            // Wire up OMEN key events
            _omenKeyService.ToggleOmenCoreRequested += OnOmenKeyToggleWindow;
            _omenKeyService.CyclePerformanceRequested += OnHotkeyTogglePerformanceMode;
            _omenKeyService.CycleFanModeRequested += OnHotkeyToggleFanMode;
            _omenKeyService.ToggleMaxCoolingRequested += OnOmenKeyToggleMaxCooling;
            
            // Subscribe to service events for UI synchronization (e.g., power automation changes)
            _fanService!.PresetApplied += OnFanPresetApplied;
            _performanceModeService!.ModeApplied += OnPerformanceModeApplied;

            // Initialize sub-ViewModels that don't depend on async services
            // InitializeSubViewModels(); // Removed in favor of lazy loading
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
            
            // Restore saved settings (GPU Power Boost, TCC Offset, Fan Preset) on startup
            _ = RestoreSettingsOnStartupAsync();
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

        /// <summary>
        /// Restore saved settings (GPU Power Boost, fan preset) on startup.
        /// Runs after hardware initialization with retry logic for WMI readiness.
        /// </summary>
        private async Task RestoreSettingsOnStartupAsync()
        {
            try
            {
                // Brief delay to let hardware stabilize after boot
                await Task.Delay(2000);
                
                // Restore GPU Power Boost level
                var savedGpuPb = _config.LastGpuPowerBoostLevel;
                if (!string.IsNullOrEmpty(savedGpuPb) && _wmiBios != null)
                {
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            var level = savedGpuPb switch
                            {
                                "Minimum" => HpWmiBios.GpuPowerLevel.Minimum,
                                "Medium" => HpWmiBios.GpuPowerLevel.Medium,
                                "Maximum" => HpWmiBios.GpuPowerLevel.Maximum,
                                "Extended" => HpWmiBios.GpuPowerLevel.Extended3,
                                _ => HpWmiBios.GpuPowerLevel.Medium
                            };
                            
                            if (_wmiBios.SetGpuPower(level))
                            {
                                _logging.Info($"✓ GPU Power Boost restored on startup: {savedGpuPb}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logging.Warn($"GPU Power Boost restore attempt {attempt}/3 failed: {ex.Message}");
                            if (attempt < 3) await Task.Delay(1500);
                        }
                    }
                }
                
                // Restore fan preset
                var savedFanPreset = _config.LastFanPresetName;
                if (!string.IsNullOrEmpty(savedFanPreset) && _fanService != null)
                {
                    try
                    {
                        // Look up the saved preset from config
                        var preset = _config.FanPresets?.FirstOrDefault(p => 
                            p.Name.Equals(savedFanPreset, StringComparison.OrdinalIgnoreCase));
                        
                        if (preset != null)
                        {
                            _fanService.ApplyPreset(preset);
                            _logging.Info($"✓ Fan preset restored on startup: {savedFanPreset} ({preset.Curve?.Count ?? 0} curve points)");
                            
                            // Sync UI with restored preset
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                FanControl?.SelectPresetByNameNoApply(savedFanPreset);
                                CurrentFanMode = savedFanPreset;
                                if (_dashboard != null) _dashboard.CurrentFanMode = savedFanPreset;
                            });
                        }
                        else
                        {
                            // Try built-in preset
                            var builtIn = new FanPreset
                            {
                                Name = savedFanPreset,
                                Mode = savedFanPreset.ToLowerInvariant() switch
                                {
                                    "max" or "maximum" => FanMode.Max,
                                    "performance" or "turbo" => FanMode.Performance,
                                    "quiet" or "silent" => FanMode.Quiet,
                                    _ => FanMode.Auto
                                },
                                IsBuiltIn = true
                            };
                            _fanService.ApplyPreset(builtIn);
                            _logging.Info($"✓ Fan preset restored on startup: {savedFanPreset} (built-in)");
                            
                            // Sync UI with restored preset
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                FanControl?.SelectPresetByNameNoApply(savedFanPreset);
                                CurrentFanMode = savedFanPreset;
                                if (_dashboard != null) _dashboard.CurrentFanMode = savedFanPreset;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Fan preset restore failed: {ex.Message}");
                    }
                }
                
                // Restore TCC offset
                var savedTcc = _config.LastTccOffset;
                if (savedTcc.HasValue && savedTcc.Value > 0 && _wmiBios != null)
                {
                    try
                    {
                        // TCC offset via WMI BIOS if available
                        _logging.Info($"TCC Offset restore: {savedTcc.Value}°C (requires reapply via System Control)");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"TCC offset restore failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Settings restoration failed: {ex.Message}", ex);
            }
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                UpdateDownloadProgress = progress.ProgressPercent;
                UpdateDownloadStatus = $"{progress.ProgressPercent:F1}% • {progress.DownloadSpeedMbps:F2} MB/s • {FormatTimeSpan(progress.EstimatedTimeRemaining)} remaining";
            });
        }
        
        private void OnBackgroundUpdateCheckCompleted(object? sender, UpdateCheckResult result)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
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
            
            // Skip restore point for dry run
            if (!CleanupDryRun)
            {
                // Automatically attempt to create restore point before cleanup
                _logging.Info("Attempting to create restore point before OGH cleanup...");
                CleanupStatus = "Creating restore point...";
                OmenCleanupSteps.Add("Creating system restore point...");
                
                var restoreResult = await _systemRestoreService.CreateRestorePointAsync("OmenCore - Before OGH Cleanup");
                
                if (!restoreResult.Success)
                {
                    _logging.Warn($"Restore point creation failed: {restoreResult.Message}");
                    OmenCleanupSteps.Add($"⚠ Restore point failed: {restoreResult.Message}");
                    
                    // Ask user if they want to continue anyway
                    var continueAnyway = System.Windows.MessageBox.Show(
                        $"System Restore point creation failed:\n{restoreResult.Message}\n\n" +
                        "This is often because System Restore is disabled on your system.\n\n" +
                        "Do you want to continue with the cleanup anyway?\n\n" +
                        "Note: Without a restore point, you cannot easily undo the cleanup.",
                        "Restore Point Failed - OmenCore",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    
                    if (continueAnyway != System.Windows.MessageBoxResult.Yes)
                    {
                        CleanupStatus = "⚠ Cleanup cancelled - no restore point";
                        OmenCleanupSteps.Add("Cleanup cancelled by user");
                        _logging.Info("OGH cleanup cancelled by user due to restore point failure");
                        PushEvent("OGH cleanup cancelled");
                        CleanupInProgress = false;
                        return;
                    }
                    
                    _logging.Info("User chose to continue cleanup without restore point");
                    OmenCleanupSteps.Add("Continuing without restore point (user confirmed)");
                }
                else
                {
                    _logging.Info($"✓ Restore point created (sequence: {restoreResult.SequenceNumber})");
                    OmenCleanupSteps.Add($"✓ Restore point created (#{restoreResult.SequenceNumber})");
                }
                
                CleanupStatus = "Removing OMEN Gaming Hub...";
            }
            
            // Subscribe to real-time progress updates
            void OnStepCompleted(string step)
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    OmenCleanupSteps.Add(step);
                    CleanupStatus = step;
                });
            }
            
            _hubCleanupService.StepCompleted += OnStepCompleted;
            
            try
            {
                var options = BuildCleanupOptions();
                var result = await _hubCleanupService.CleanupAsync(options);
                
                // Add any warnings/errors that weren't reported via events
                foreach (var warning in result.Warnings)
                {
                    if (!OmenCleanupSteps.Contains($"Warning: {warning}"))
                        OmenCleanupSteps.Add($"Warning: {warning}");
                }
                foreach (var error in result.Errors)
                {
                    if (!OmenCleanupSteps.Contains($"Error: {error}"))
                        OmenCleanupSteps.Add($"Error: {error}");
                }

                if (result.Success)
                {
                    CleanupStatus = CleanupDryRun ? "✓ Dry run completed" : "✓ OMEN Gaming Hub removed";
                    PushEvent(CleanupDryRun ? "OMEN cleanup dry run completed" : "OMEN Gaming Hub cleanup complete");
                }
                else
                {
                    CleanupStatus = "⚠ Cleanup finished with errors";
                    PushEvent("OMEN cleanup completed with errors");
                }
            }
            catch (Exception ex)
            {
                CleanupStatus = $"✗ Cleanup failed: {ex.Message}";
                _logging.Error("OMEN cleanup failed", ex);
                PushEvent("OMEN cleanup failed");
            }
            finally
            {
                _hubCleanupService.StepCompleted -= OnStepCompleted;
                CleanupInProgress = false;
            }
        }

        private void HardwareMonitoringServiceOnSampleUpdated(object? sender, MonitoringSample sample)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() => LatestMonitoringSample = sample);
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
            Application.Current?.Dispatcher?.BeginInvoke(() => UndervoltStatus = status);
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
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
            // Skip empty entries to reduce log clutter
            if (string.IsNullOrWhiteSpace(entry)) return;
            
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                // Append without extra newline padding
                if (_logBuffer.Length > 0)
                    _logBuffer.Append('\n');
                _logBuffer.Append(entry.TrimEnd());
                
                // Keep only last 200 lines to prevent memory growth
                var content = _logBuffer.ToString();
                var lines = content.Split('\n');
                if (lines.Length > 200)
                {
                    _logBuffer.Clear();
                    _logBuffer.Append(string.Join("\n", lines.Skip(lines.Length - 200)));
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
                _razerService = new OmenCore.Razer.RazerService(_logging);
                
                await DiscoverCorsairDevices();
                await DiscoverLogitechDevices();
                
                // Initialize Lighting sub-ViewModel after async services are ready
                if (_corsairDeviceService != null && _logitechDeviceService != null)
                {
                    // Show lighting tab if:
                    // 1. Corsair or Logitech peripheral devices are found, OR
                    // 2. HP OMEN keyboard lighting is available, OR
                    // 3. Razer Synapse is detected, OR
                    // 4. OpenRGB server is running (for desktop/generic RGB support)
                    bool hasPeripherals = _corsairDeviceService.Devices.Any() || _logitechDeviceService.Devices.Any();
                    bool hasKeyboardLighting = _keyboardLightingService?.IsAvailable ?? false;
                    bool hasRazer = _razerService?.IsAvailable ?? false;
                    
                    // Try to detect OpenRGB server (runs on port 6742 by default)
                    var openRgbProvider = new OmenCore.Services.Rgb.OpenRgbProvider(_logging);
                    await openRgbProvider.InitializeAsync();
                    bool hasOpenRgb = openRgbProvider.IsAvailable;
                    
                    if (hasPeripherals || hasKeyboardLighting || hasRazer || hasOpenRgb)
                    {
                        // Initialize RGB manager and providers with priority: Corsair -> Logitech -> Razer -> OpenRGB -> SystemGeneric
                        var rgbManager = new OmenCore.Services.Rgb.RgbManager();
                        var corsairProvider = new OmenCore.Services.Rgb.CorsairRgbProvider(_logging, _configService);
                        var logitechProvider = new OmenCore.Services.Rgb.LogitechRgbProvider(_logging);
                        rgbManager.RegisterProvider(corsairProvider);
                        rgbManager.RegisterProvider(logitechProvider);

                        if (_razerService != null)
                        {
                            var razerProvider = new OmenCore.Services.Rgb.RazerRgbProvider(_logging, _razerService);
                            rgbManager.RegisterProvider(razerProvider);
                        }
                        
                        // Register OpenRGB provider if available
                        if (hasOpenRgb)
                        {
                            rgbManager.RegisterProvider(openRgbProvider);
                            _logging.Info($"OpenRGB integration enabled with {openRgbProvider.DeviceCount} device(s)");
                        }

                        var systemProvider = new OmenCore.Services.Rgb.SystemRgbProvider(rgbManager, _logging);
                        rgbManager.RegisterProvider(systemProvider);

                        await rgbManager.InitializeAllAsync();

                        Lighting = new LightingViewModel(_corsairDeviceService, _logitechDeviceService, _logging, _keyboardLightingService, _configService, _razerService, rgbManager);
                        OnPropertyChanged(nameof(Lighting));

                        // Apply saved keyboard colors on startup
                        if (hasKeyboardLighting)
                        {
                            _ = Lighting.ApplySavedKeyboardColorsAsync();
                        }
                        
                        if (hasKeyboardLighting && !hasPeripherals)
                        {
                            _logging.Info("Lighting sub-ViewModel initialized (keyboard lighting available)");
                        }
                        else
                        {
                            _logging.Info("Lighting sub-ViewModel initialized (devices found)");
                        }
                    }
                    else
                    {
                        _logging.Info("Lighting sub-ViewModel skipped (no devices or keyboard lighting found)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to initialize peripheral services", ex);
            }
        }
        
        /* InitializeSubViewModels removed in favor of lazy loading */

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
                // BUG FIX v2.6.1: Prioritize exact match for "Max" to avoid picking "Performance" first
                FanPreset? targetPreset = mode switch
                {
                    "Max" => FanPresets.FirstOrDefault(p => p.Name.Equals("Max", StringComparison.OrdinalIgnoreCase)) 
                             ?? FanPresets.FirstOrDefault(p => p.Name.Contains("Max", StringComparison.OrdinalIgnoreCase)),
                    "Quiet" => FanPresets.FirstOrDefault(p => p.Name.Contains("Quiet", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Silent", StringComparison.OrdinalIgnoreCase)),
                    _ => FanPresets.FirstOrDefault(p => p.Name.Contains("Auto", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
                };

                if (targetPreset != null)
                {
                    SelectedPreset = targetPreset;
                    // BUG FIX v2.6.1: Use immediate=true so max fan is applied right away
                    _fanService.ApplyPreset(targetPreset, immediate: true);
                    CurrentFanMode = mode;
                    PushEvent($"🌀 Fan mode: {mode}");
                    
                    // v2.6.1: Show notification for fan mode changes from tray
                    _notificationService.ShowFanModeChanged(mode, "Quick Access");
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
        
        /// <summary>
        /// Apply a combined quick profile (Performance + Fan) from the system tray
        /// </summary>
        public void ApplyQuickProfileFromTray(string profile)
        {
            _logging.Info($"Quick profile change requested from tray: {profile}");
            try
            {
                // Use GeneralViewModel if available for proper syncing
                if (General != null)
                {
                    switch (profile.ToLower())
                    {
                        case "performance":
                            General.ApplyPerformanceProfile();
                            break;
                        case "balanced":
                            General.ApplyBalancedProfile();
                            break;
                        case "quiet":
                            General.ApplyQuietProfile();
                            break;
                        default:
                            _logging.Warn($"Unknown profile: {profile}");
                            return;
                    }
                }
                else
                {
                    // Fallback: apply both modes separately
                    switch (profile.ToLower())
                    {
                        case "performance":
                            _performanceModeService.SetPerformanceMode("Performance");
                            _fanService.ApplyMaxCooling();
                            CurrentPerformanceMode = "Performance";
                            CurrentFanMode = "Max";
                            break;
                        case "balanced":
                            _performanceModeService.SetPerformanceMode("Default");
                            _fanService.ApplyAutoMode();
                            CurrentPerformanceMode = "Balanced";
                            CurrentFanMode = "Auto";
                            break;
                        case "quiet":
                            _performanceModeService.SetPerformanceMode("Quiet");
                            _fanService.ApplyQuietMode();
                            CurrentPerformanceMode = "Quiet";
                            CurrentFanMode = "Quiet";
                            break;
                    }
                }
                
                ShowHotkeyOsd("Profile", profile, "Tray");
                PushEvent($"🎮 Profile: {profile}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to apply quick profile from tray: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set GPU power level from system tray (v2.7.0)
        /// </summary>
        public void SetGpuPowerFromTray(string level)
        {
            _logging.Info($"GPU power level change requested from tray: {level}");
            try
            {
                if (SystemControl != null)
                {
                    SystemControl.GpuPowerBoostLevel = level;
                    SystemControl.ApplyGpuPowerBoostCommand?.Execute(null);
                    CurrentGpuPowerLevel = level;
                    PushEvent($"⚡ GPU Power: {level}");
                    _notificationService.ShowInfo("GPU Power", $"Set to {level}");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set GPU power from tray: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set keyboard backlight level from system tray (v2.7.0)
        /// </summary>
        public void SetKeyboardBacklightFromTray(int level)
        {
            _logging.Info($"Keyboard backlight change requested from tray: level {level}");
            try
            {
                if (_keyboardLightingService?.IsAvailable == true)
                {
                    // Convert level 0-3 to brightness byte (0, 85, 170, 255)
                    int brightness = level switch
                    {
                        0 => 0,
                        1 => 85,
                        2 => 170,
                        _ => 255
                    };
                    _keyboardLightingService.SetBrightness(brightness);
                    CurrentKeyboardBrightness = level;
                    string levelName = level switch { 0 => "Off", 1 => "Low", 2 => "Medium", _ => "High" };
                    PushEvent($"💡 Keyboard: {levelName}");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set keyboard backlight from tray: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Toggle keyboard backlight from system tray (v2.7.0)
        /// </summary>
        public void ToggleKeyboardBacklightFromTray()
        {
            _logging.Info("Keyboard backlight toggle requested from tray");
            try
            {
                // Cycle through levels: Off -> Low -> Medium -> High -> Off
                int newLevel = (CurrentKeyboardBrightness + 1) % 4;
                SetKeyboardBacklightFromTray(newLevel);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to toggle keyboard backlight from tray: {ex.Message}");
            }
        }

        #endregion

        #region Hotkey & Notification Handlers

        private void OnGameProfileApplyRequested(object? sender, ProfileApplyEventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
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
                    ShowHotkeyOsd("Fan Mode", nextMode, "Ctrl+Shift+F");
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
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
                    ShowHotkeyOsd("Performance", nextMode, "Ctrl+Shift+P");
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    FanControl?.ApplyFanMode("Performance");
                    _performanceModeService.Apply(new PerformanceMode { Name = "Performance" });
                    CurrentFanMode = "Performance";
                    CurrentPerformanceMode = "Performance";
                    ShowHotkeyOsd("Boost", "Performance", "Ctrl+Shift+B");
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    FanControl?.ApplyFanMode("Quiet");
                    _performanceModeService.Apply(new PerformanceMode { Name = "Quiet" });
                    CurrentFanMode = "Quiet";
                    CurrentPerformanceMode = "Quiet";
                    ShowHotkeyOsd("Quiet Mode", "Quiet", "Ctrl+Shift+Q");
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
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null)
                {
                    _logging.Warn("Toggle window: MainWindow is null");
                    return;
                }
                
                _logging.Info($"Toggle window: IsVisible={mainWindow.IsVisible}, WindowState={mainWindow.WindowState}, ShowInTaskbar={mainWindow.ShowInTaskbar}");
                
                // Check if window is currently shown and not minimized
                bool isWindowShown = mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized;
                
                if (isWindowShown)
                {
                    // Hide the window
                    mainWindow.Hide();
                    mainWindow.ShowInTaskbar = false;
                    _logging.Info("Window hidden to tray");
                }
                else
                {
                    // Show the window - ensure it's properly restored
                    mainWindow.Show();
                    mainWindow.ShowInTaskbar = true;
                    mainWindow.WindowState = WindowState.Normal;
                    
                    // Use Windows API for reliable foreground focus
                    // This bypasses Windows' focus-stealing prevention for background apps
                    var handle = new WindowInteropHelper(mainWindow).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        // Get the current foreground window's thread
                        var foregroundWindow = GetForegroundWindow();
                        GetWindowThreadProcessId(foregroundWindow, out _);
                        var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
                        var currentThread = GetCurrentThreadId();
                        
                        // Attach threads to share input queue (allows SetForegroundWindow to work)
                        if (foregroundThread != currentThread)
                        {
                            AttachThreadInput(foregroundThread, currentThread, true);
                        }
                        
                        ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        
                        // Detach threads
                        if (foregroundThread != currentThread)
                        {
                            AttachThreadInput(foregroundThread, currentThread, false);
                        }
                    }
                    
                    // WPF activation as fallback
                    mainWindow.Activate();
                    mainWindow.Focus();
                    
                    _logging.Info("Window shown and activated");
                }
            });
        }
        
        private void OnOmenKeyToggleWindow(object? sender, EventArgs e)
        {
            // Same as hotkey toggle but may include OSD feedback
            OnHotkeyToggleWindow(sender, e);
        }
        
        private void OnOmenKeyToggleMaxCooling(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(async () =>
            {
                try
                {
                    // Toggle between Max and Auto
                    var currentPreset = FanControl?.SelectedPreset;
                    if (currentPreset?.Name == "Max")
                    {
                        _fanService.ApplyAutoMode();
                        FanControl?.SelectPresetByNameNoApply("Auto");
                        ShowHotkeyOsd("Fan Mode", "Auto (BIOS Control)", "OMEN Key");
                    }
                    else
                    {
                        _fanService.ApplyMaxCooling();
                        FanControl?.SelectPresetByNameNoApply("Max");
                        ShowHotkeyOsd("Fan Mode", "Maximum Cooling", "OMEN Key");
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OMEN key max cooling toggle failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handle fan preset changes from FanService (e.g., power automation).
        /// Updates all UI indicators: sidebar, tray, dashboard.
        /// </summary>
        private void OnFanPresetApplied(object? sender, string presetName)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                // Update MainViewModel's CurrentFanMode for tray/sidebar sync
                CurrentFanMode = presetName;
                
                // Update Dashboard if loaded
                if (_dashboard != null)
                {
                    _dashboard.CurrentFanMode = presetName;
                }
                
                // Update FanControlViewModel's selected preset if loaded
                FanControl?.SelectPresetByNameNoApply(presetName);
                
                _logging.Info($"UI synced: Fan preset '{presetName}' applied");
            });
        }

        /// <summary>
        /// Handle performance mode changes from PerformanceModeService (e.g., power automation).
        /// Updates all UI indicators: sidebar, tray, dashboard, OSD.
        /// </summary>
        private void OnPerformanceModeApplied(object? sender, string modeName)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                // Update MainViewModel's CurrentPerformanceMode for tray/sidebar sync
                CurrentPerformanceMode = modeName;
                
                // Update Dashboard if loaded
                if (_dashboard != null)
                {
                    _dashboard.CurrentPerformanceMode = modeName;
                }
                
                // Update OSD overlay with new performance mode
                _osdService?.SetPerformanceMode(modeName);
                
                // Update SystemControlViewModel's selected mode (without re-applying)
                SystemControl?.SelectModeByNameNoApply(modeName);
                
                _logging.Info($"UI synced: Performance mode '{modeName}' applied");
            });
        }

        /// <summary>
        /// Initialize hotkeys after window handle is available
        /// </summary>
        public void InitializeHotkeys(IntPtr windowHandle)
        {
            try
            {
                // Only register hotkeys if enabled in settings
                var hotkeysEnabled = _config.Monitoring?.HotkeysEnabled ?? true;
                
                _hotkeyService.Initialize(windowHandle);
                
                if (hotkeysEnabled)
                {
                    _hotkeyService.RegisterDefaultHotkeys();
                    _logging.Info("Global hotkeys registered");
                    PushEvent("⌨️ Global hotkeys enabled");
                }
                else
                {
                    _logging.Info("Global hotkeys disabled by user setting");
                }
                
                // Start OMEN key interception if enabled
                var omenKeyEnabled = _config.Features?.OmenKeyInterceptionEnabled ?? _config.OmenKeyEnabled;
                if (omenKeyEnabled)
                {
                    _omenKeyService.StartInterception();
                    _logging.Info("OMEN key interception started");
                }
                
                // Initialize OSD overlay (if enabled in settings)
                // Pass monitoring sample source for accurate CPU/GPU load data
                _osdService?.SetMonitoringSampleSource(() => LatestMonitoringSample);
                _osdService?.Initialize();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to register hotkeys: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get the OMEN key service for settings UI
        /// </summary>
        public OmenKeyService OmenKey => _omenKeyService;

        /// <summary>
        /// Get the notification service for external use
        /// </summary>
        public NotificationService Notifications => _notificationService;

        /// <summary>
        /// Get the hotkey service for external use
        /// </summary>
        public HotkeyService Hotkeys => _hotkeyService;

        /// <summary>
        /// Show the hotkey OSD popup with mode information
        /// </summary>
        private void ShowHotkeyOsd(string category, string modeName, string hotkeyText)
        {
            try
            {
                // Create OSD window if it doesn't exist
                if (_hotkeyOsd == null)
                {
                    _hotkeyOsd = new HotkeyOsdWindow();
                    _logging.Info("HotkeyOsdWindow created");
                }
                
                _hotkeyOsd.ShowMode(category, modeName, $"via Hotkey ({hotkeyText})");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to show hotkey OSD: {ex.Message}");
            }
        }

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
            
            // Unsubscribe auto-update events
            _autoUpdateService.DownloadProgressChanged -= OnUpdateDownloadProgressChanged;
            _autoUpdateService.UpdateCheckCompleted -= OnBackgroundUpdateCheckCompleted;
            _autoUpdateService.Dispose();

            // Unsubscribe game profile events
            if (_gameProfileService != null)
            {
                _gameProfileService.ProfileApplyRequested -= OnGameProfileApplyRequested;
                _gameProfileService.ProfileApplyRequested -= OnProfileApplyRequested;
                _gameProfileService.ActiveProfileChanged -= OnActiveProfileChanged;
            }
            
            // Unsubscribe hotkey events
            if (_hotkeyService != null)
            {
                _hotkeyService.ToggleFanModeRequested -= OnHotkeyToggleFanMode;
                _hotkeyService.TogglePerformanceModeRequested -= OnHotkeyTogglePerformanceMode;
                _hotkeyService.ToggleBoostModeRequested -= OnHotkeyToggleBoostMode;
                _hotkeyService.ToggleQuietModeRequested -= OnHotkeyToggleQuietMode;
                _hotkeyService.ToggleWindowRequested -= OnHotkeyToggleWindow;
            }

            // Dispose process monitoring and game profile services
            _processMonitoringService?.Dispose();
            _gameProfileService?.Dispose();

            // Dispose hotkey and notification services
            _hotkeyService?.Dispose();
            _notificationService?.Dispose();
            
            // Dispose OMEN key service
            _omenKeyService?.Dispose();
            
            // Dispose power automation service
            _powerAutomationService?.Dispose();

            // Dispose OSD services
            _osdService?.Dispose();
            _hotkeyOsd?.Close();

            // Dispose device services
            _corsairDeviceService?.Dispose();
            _logitechDeviceService?.Dispose();
        }
    }
}
