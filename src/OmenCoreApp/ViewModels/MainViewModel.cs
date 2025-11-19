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
        private readonly CorsairDeviceService _corsairDeviceService;
        private readonly LogitechDeviceService _logitechDeviceService;
        private readonly MacroService _macroService = new();
        private readonly UndervoltService _undervoltService;
        private readonly HardwareMonitoringService _hardwareMonitoringService;
        private readonly SystemRestoreService _systemRestoreService;
        private readonly OmenGamingHubCleanupService _hubCleanupService;
        private readonly SystemInfoService _systemInfoService;
        private readonly AsyncRelayCommand _applyUndervoltCommand;
        private readonly AsyncRelayCommand _resetUndervoltCommand;
        private readonly AsyncRelayCommand _refreshUndervoltCommand;
        private readonly AsyncRelayCommand _createRestorePointCommand;
        private readonly AsyncRelayCommand _cleanupOmenHubCommand;
        private readonly RelayCommand _takeUndervoltControlCommand;
        private readonly RelayCommand _respectExternalUndervoltCommand;
        private readonly RelayCommand _stopMacroRecordingInternalCommand;
        private readonly RelayCommand _saveRecordedMacroInternalCommand;
        private readonly RelayCommand _applyLogitechColorInternalCommand;
        private readonly RelayCommand _syncCorsairThemeInternalCommand;
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
        
        public SystemInfo SystemInfo { get; private set; }

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }
        public ReadOnlyObservableCollection<MonitoringSample> MonitoringSamples { get; } = null!;
        public MonitoringSample? LatestMonitoringSample
        {
            get => _latestMonitoringSample;
            private set
            {
                _latestMonitoringSample = value;
                OnPropertyChanged(nameof(LatestMonitoringSample));
                OnPropertyChanged(nameof(CpuSummary));
                OnPropertyChanged(nameof(GpuSummary));
                OnPropertyChanged(nameof(MemorySummary));
                OnPropertyChanged(nameof(StorageSummary));
                OnPropertyChanged(nameof(CpuClockSummary));
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

            var fanController = new FanController(ec, _config.EcFanRegisterMap);
            _fanService = new FanService(fanController, new ThermalSensorProvider(), _logging, _config.MonitoringIntervalMs);
            ThermalSamples = _fanService.ThermalSamples;
            FanTelemetry = _fanService.FanTelemetry;
            var powerPlanService = new PowerPlanService(_logging);
            _performanceModeService = new PerformanceModeService(fanController, powerPlanService, _logging);
            _keyboardLightingService = new KeyboardLightingService(_logging);
            _systemOptimizationService = new SystemOptimizationService(_logging);
            _gpuSwitchService = new GpuSwitchService(_logging);
            _corsairDeviceService = new CorsairDeviceService(_logging);
            _logitechDeviceService = new LogitechDeviceService(_logging);
            var undervoltProvider = new IntelUndervoltProvider();
            _undervoltService = new UndervoltService(undervoltProvider, _logging, _config.Undervolt?.ProbeIntervalMs ?? 4000);
            _undervoltService.StatusChanged += UndervoltServiceOnStatusChanged;
            RespectExternalUndervolt = _config.Undervolt?.RespectExternalControllers ?? true;
            RequestedCoreOffset = _config.Undervolt?.DefaultOffset.CoreMv ?? -75;
            RequestedCacheOffset = _config.Undervolt?.DefaultOffset.CacheMv ?? -50;
            var monitorBridge = new LibreHardwareMonitorBridge();
            _hardwareMonitoringService = new HardwareMonitoringService(monitorBridge, _logging, _config.Monitoring ?? new MonitoringPreferences());
            MonitoringSamples = _hardwareMonitoringService.Samples;
            _hardwareMonitoringService.SampleUpdated += HardwareMonitoringServiceOnSampleUpdated;
            _monitoringLowOverhead = _config.Monitoring?.LowOverheadMode ?? false;
            _hardwareMonitoringService.SetLowOverheadMode(_monitoringLowOverhead);
            _systemRestoreService = new SystemRestoreService(_logging);
            _hubCleanupService = new OmenGamingHubCleanupService(_logging);
            _systemInfoService = new SystemInfoService(_logging);
            SystemInfo = _systemInfoService.GetSystemInfo();
            _corsairDeviceService.Discover();
            _logitechDeviceService.Discover();

            ApplyFanPresetCommand = new RelayCommand(_ => ApplySelectedPreset(), _ => SelectedPreset != null);
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset());
            ApplyFanCurveCommand = new RelayCommand(_ => _fanService.ApplyCustomCurve(CustomFanCurve));
            ApplyPerformanceModeCommand = new RelayCommand(_ => ApplyPerformanceMode(), _ => SelectedPerformanceMode != null);
            ApplyLightingProfileCommand = new RelayCommand(_ => ApplyLightingProfile(), _ => SelectedLightingProfile != null);
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
            DiscoverCorsairCommand = new RelayCommand(_ => DiscoverCorsairDevices());
            ApplyCorsairLightingCommand = new RelayCommand(_ => ApplyCorsairLighting(), _ => SelectedCorsairDevice != null && SelectedCorsairPreset != null);
            SaveCorsairDpiCommand = new RelayCommand(_ => SaveCorsairDpi(), _ => SelectedCorsairDevice != null);
            ApplyMacroCommand = new RelayCommand(_ => ApplyMacroToDevice(), _ => SelectedCorsairDevice != null && SelectedMacroProfile != null);
            _syncCorsairThemeInternalCommand = new RelayCommand(_ => SyncCorsairWithTheme(), _ => SelectedLightingProfile != null);
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
            DiscoverLogitechCommand = new RelayCommand(_ => DiscoverLogitechDevices());
            _applyLogitechColorInternalCommand = new RelayCommand(_ => ApplyLogitechColor(), _ => SelectedLogitechDevice != null);
            ApplyLogitechColorCommand = _applyLogitechColorInternalCommand;
            _createRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync(), _ => !RestorePointInProgress);
            CreateRestorePointCommand = _createRestorePointCommand;
            _cleanupOmenHubCommand = new AsyncRelayCommand(_ => RunOmenCleanupAsync(), _ => !CleanupInProgress);
            CleanupOmenHubCommand = _cleanupOmenHubCommand;

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
            foreach (var device in _corsairDeviceService.Devices)
            {
                CorsairDevices.Add(device);
            }

            CorsairLightingPresets.Clear();
            foreach (var preset in _config.CorsairLightingPresets)
            {
                CorsairLightingPresets.Add(preset);
            }
            SelectedCorsairPreset = CorsairLightingPresets.FirstOrDefault();

            LogitechDevices.Clear();
            foreach (var device in _logitechDeviceService.Devices)
            {
                LogitechDevices.Add(device);
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

        private void ApplyLightingProfile()
        {
            if (SelectedLightingProfile == null)
            {
                return;
            }
            _keyboardLightingService.ApplyProfile(SelectedLightingProfile);
            _corsairDeviceService.SyncWithTheme(SelectedLightingProfile);
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

        private void DiscoverCorsairDevices()
        {
            _corsairDeviceService.Discover();
            CorsairDevices.Clear();
            foreach (var device in _corsairDeviceService.Devices)
            {
                CorsairDevices.Add(device);
            }
            SelectedCorsairDevice = CorsairDevices.FirstOrDefault();
        }

        private void ApplyCorsairLighting()
        {
            if (SelectedCorsairDevice == null || SelectedCorsairPreset == null)
            {
                return;
            }
            _corsairDeviceService.ApplyLightingPreset(SelectedCorsairDevice, SelectedCorsairPreset);
            PushEvent($"Corsair preset '{SelectedCorsairPreset.Name}' applied to {SelectedCorsairDevice.Name}");
        }

        private void SaveCorsairDpi()
        {
            if (SelectedCorsairDevice == null)
            {
                return;
            }
            _corsairDeviceService.ApplyDpiStages(SelectedCorsairDevice, EditableDpiStages);
            PushEvent($"DPI stages updated for {SelectedCorsairDevice.Name}");
        }

        private void ApplyMacroToDevice()
        {
            if (SelectedCorsairDevice == null || SelectedMacroProfile == null)
            {
                return;
            }
            _corsairDeviceService.ApplyMacroProfile(SelectedCorsairDevice, SelectedMacroProfile);
            PushEvent($"Macro '{SelectedMacroProfile.Name}' applied to {SelectedCorsairDevice.Name}");
        }

        private void SyncCorsairWithTheme()
        {
            var profile = SelectedLightingProfile ?? LightingProfiles.FirstOrDefault();
            if (profile == null)
            {
                return;
            }
            _corsairDeviceService.SyncWithTheme(profile);
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

        private void DiscoverLogitechDevices()
        {
            _logitechDeviceService.Discover();
            LogitechDevices.Clear();
            foreach (var device in _logitechDeviceService.Devices)
            {
                LogitechDevices.Add(device);
            }
            SelectedLogitechDevice = LogitechDevices.FirstOrDefault();
            PushEvent($"Discovered {LogitechDevices.Count} Logitech device(s)");
        }

        private void ApplyLogitechColor()
        {
            if (SelectedLogitechDevice == null)
            {
                return;
            }
            _logitechDeviceService.ApplyColor(SelectedLogitechDevice, LogitechColorHex, LogitechBrightness);
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

        private void ReloadRecentBuffer()
        {
            _logBuffer.Clear();
            OnPropertyChanged(nameof(LogBuffer));
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
        }
    }
}
