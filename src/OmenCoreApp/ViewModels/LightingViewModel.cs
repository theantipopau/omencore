using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OmenCore.Corsair;
using OmenCore.Logitech;
using OmenCore.Models;
using OmenCore.Razer;
using OmenCore.Services;
using OmenCore.Utils;
using OmenCore.Views;

namespace OmenCore.ViewModels
{
    public class LightingViewModel : ViewModelBase
    {
        private readonly CorsairDeviceService? _corsairService;
        private readonly LogitechDeviceService? _logitechService;
        private readonly RazerService? _razerService;
        private readonly OmenCore.Services.Rgb.OpenRgbProvider? _openRgbProvider;
        private readonly KeyboardLightingService? _keyboardLightingService;
        private readonly ConfigurationService? _configService;
        private readonly LoggingService _logging;
        private readonly OmenCore.Services.Rgb.RgbManager? _rgbManager;
        private readonly HardwareMonitoringService? _hardwareMonitoringService;
        private readonly PerformanceModeService? _performanceModeService;
        private readonly RgbSceneService? _sceneService;
        private readonly ScreenSamplingService? _screenSamplingService;
        
        private CorsairDevice? _selectedCorsairDevice;
        private CorsairLightingPreset? _selectedCorsairPreset;
        private LogitechDevice? _selectedLogitechDevice;
        private string _logitechColorHex = "#E6002E";
        private int _logitechBrightness = 80;
        private string _corsairColorHex = "#FF0000";
        private int _logitechRedValue = 230;
        private int _logitechGreenValue = 0;
        private int _logitechBlueValue = 46;
        private MacroProfile? _selectedMacroProfile;
        
        // Scene-related fields
        private RgbScene? _selectedScene;
        private bool _isAmbientModeActive;
        
        // 4-Zone Keyboard colors
        private string _zone1ColorHex = "#E6002E"; // OMEN Red
        private string _zone2ColorHex = "#0096FF"; // Blue
        private string _zone3ColorHex = "#9B30FF"; // Purple
        private string _zone4ColorHex = "#00FFFF"; // Cyan
        private KeyboardPreset? _selectedKeyboardPreset;
        private bool _colorsLoadedFromConfig; // Track if colors were loaded from saved config
        private bool _applyKeyboardColorsOnStartup = true;
        
        // Temperature-responsive lighting
        private bool _temperatureResponsiveLightingEnabled;
        private bool _performanceModeSyncedLightingEnabled;
        private bool _throttlingIndicatorLightingEnabled;
        private double _cpuTempThresholdLow = 40;
        private double _cpuTempThresholdMedium = 70;
        private double _cpuTempThresholdHigh = 85;
        private double _gpuTempThresholdLow = 35;
        private double _gpuTempThresholdMedium = 65;
        private double _gpuTempThresholdHigh = 80;
        private string _tempLowColorHex = "#00FF00"; // Green
        private string _tempMediumColorHex = "#FFFF00"; // Yellow
        private string _tempHighColorHex = "#FF0000"; // Red
        private string _throttlingColorHex = "#FF4500"; // Orange-Red
        
        // Performance mode colors
        private string _balancedModeColorHex = "#0096FF"; // Blue
        private string _performanceModeColorHex = "#FF0000"; // Red
        private string _quietModeColorHex = "#800080"; // Purple
        private string _customModeColorHex = "#00FFFF"; // Cyan
        
        // Empty collections for when services are not available
        private static readonly ReadOnlyObservableCollection<CorsairDevice> _emptyCorsairDevices = 
            new(new ObservableCollection<CorsairDevice>());
        private static readonly ReadOnlyObservableCollection<LogitechDevice> _emptyLogitechDevices = 
            new(new ObservableCollection<LogitechDevice>());

        public ReadOnlyObservableCollection<CorsairDevice> CorsairDevices => _corsairService?.Devices ?? _emptyCorsairDevices;
        public ReadOnlyObservableCollection<LogitechDevice> LogitechDevices => _logitechService?.Devices ?? _emptyLogitechDevices;
        public ObservableCollection<CorsairLightingPreset> CorsairLightingPresets { get; } = new();
        public ObservableCollection<KeyboardPreset> KeyboardPresets { get; } = new();
        public ICommand ApplyCorsairPresetToSystemCommand { get; }
        public ICommand SyncAllRgbCommand { get; }
        
        #region Scene Properties
        
        /// <summary>
        /// All available RGB scenes.
        /// </summary>
        public ObservableCollection<RgbScene> Scenes { get; } = new();
        
        /// <summary>
        /// Currently selected scene.
        /// </summary>
        public RgbScene? SelectedScene
        {
            get => _selectedScene;
            set
            {
                if (_selectedScene != value)
                {
                    _selectedScene = value;
                    OnPropertyChanged();
                    if (value != null && _sceneService != null)
                    {
                        _ = ApplySelectedSceneAsync();
                    }
                }
            }
        }
        
        /// <summary>
        /// Whether ambient/screen sampling mode is active.
        /// </summary>
        public bool IsAmbientModeActive
        {
            get => _isAmbientModeActive;
            set
            {
                if (_isAmbientModeActive != value)
                {
                    _isAmbientModeActive = value;
                    OnPropertyChanged();
                    ToggleAmbientMode(value);
                }
            }
        }
        
        /// <summary>
        /// Whether scene service is available.
        /// </summary>
        public bool IsSceneServiceEnabled => _sceneService != null;
        
        /// <summary>
        /// Current scene name for display.
        /// </summary>
        public string CurrentSceneName => _sceneService?.CurrentScene?.Name ?? "None";
        
        /// <summary>
        /// Current ambient color hex value.
        /// </summary>
        public string AmbientColorHex { get; private set; } = "#000000";
        
        /// <summary>
        /// Command to apply selected scene.
        /// </summary>
        public ICommand ApplySceneCommand { get; }
        
        /// <summary>
        /// Command to save current settings as a new scene.
        /// </summary>
        public ICommand SaveAsSceneCommand { get; }
        
        /// <summary>
        /// Command to toggle ambient mode.
        /// </summary>
        public ICommand ToggleAmbientModeCommand { get; }
        
        #endregion
        
        // Connection status properties for UI badges
        public bool IsCorsairConnected => CorsairDevices.Count > 0;
        public bool IsLogitechConnected => LogitechDevices.Count > 0;
        public bool IsRazerConnected => _razerService?.IsAvailable ?? false;
        
        // Service availability for UI visibility
        public bool IsCorsairEnabled => _corsairService != null;
        public bool IsLogitechEnabled => _logitechService != null;
        public bool IsRazerEnabled => _razerService != null;
        
        // Razer properties
        private readonly ObservableCollection<RazerDevice> _razerDevices = new();
        private string _razerColorHex = "#00FF00"; // Razer Green
        private int _razerRedValue = 0;
        private int _razerGreenValue = 255;
        private int _razerBlueValue = 0;
        
        public ObservableCollection<RazerDevice> RazerDevices => _razerDevices;
        public bool HasRazerDevices => _razerDevices.Count > 0;
        public bool IsRazerAvailable => _razerService?.IsAvailable ?? false;
        public string RazerDeviceStatusText => IsRazerAvailable 
            ? $"{_razerDevices.Count} device(s) detected" 
            : "Razer Synapse not detected";
        
        public string RazerColorHex
        {
            get => _razerColorHex;
            set
            {
                if (_razerColorHex != value)
                {
                    _razerColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public int RazerRedValue
        {
            get => _razerRedValue;
            set
            {
                if (_razerRedValue != value)
                {
                    _razerRedValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateRazerHexFromRgb();
                }
            }
        }
        
        public int RazerGreenValue
        {
            get => _razerGreenValue;
            set
            {
                if (_razerGreenValue != value)
                {
                    _razerGreenValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateRazerHexFromRgb();
                }
            }
        }
        
        public int RazerBlueValue
        {
            get => _razerBlueValue;
            set
            {
                if (_razerBlueValue != value)
                {
                    _razerBlueValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateRazerHexFromRgb();
                }
            }
        }
        
        // OpenRGB properties
        public bool HasOpenRgbDevices => _openRgbProvider?.IsAvailable ?? false;
        public int OpenRgbDeviceCount => _openRgbProvider?.DeviceCount ?? 0;
        public string OpenRgbStatusText => HasOpenRgbDevices 
            ? $"{OpenRgbDeviceCount} device(s) via OpenRGB" 
            : "OpenRGB server not detected";
        public System.Collections.Generic.IReadOnlyList<OmenCore.Services.Rgb.OpenRgbDevice> OpenRgbDevices => 
            _openRgbProvider?.Devices ?? new System.Collections.Generic.List<OmenCore.Services.Rgb.OpenRgbDevice>();
        
        /// <summary>
        /// True if any Corsair devices have been discovered.
        /// Used to hide the Corsair section when no devices are connected.
        /// </summary>
        public bool HasCorsairDevices => CorsairDevices.Count > 0;
        
        /// <summary>
        /// True if any Logitech devices have been discovered.
        /// Used to hide the Logitech section when no devices are connected.
        /// </summary>
        public bool HasLogitechDevices => LogitechDevices.Count > 0;
        
        /// <summary>
        /// True if HP OMEN keyboard lighting is available.
        /// Used to show/hide the keyboard lighting section.
        /// </summary>
        public bool IsKeyboardLightingAvailable => _keyboardLightingService?.IsAvailable ?? false;
        
        /// <summary>
        /// Backend type for keyboard lighting (WMI BIOS, WMI, EC, or None).
        /// </summary>
        public string KeyboardLightingBackend => _keyboardLightingService?.BackendType ?? "None";
        
        /// <summary>
        /// Helpful status/hint message for keyboard lighting.
        /// Shows tips if WMI-based control may not work on certain models.
        /// </summary>
        public string KeyboardLightingHint
        {
            get
            {
                if (!IsKeyboardLightingAvailable)
                    return "Keyboard RGB not detected on this system.";
                
                var backend = KeyboardLightingBackend;
                if (backend.Contains("WMI"))
                {
                    return "💡 If colors don't change, try enabling 'Experimental EC Keyboard' in Settings → Hardware.";
                }
                else if (backend.Contains("EC"))
                {
                    return "Using EC direct access (experimental). Changes may take a moment to apply.";
                }
                else if (backend.Contains("OGH"))
                {
                    return "Using OMEN Gaming Hub proxy. Ensure Gaming Hub services are running.";
                }
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Whether to automatically apply saved keyboard colors on startup.
        /// </summary>
        public bool ApplyKeyboardColorsOnStartup
        {
            get => _applyKeyboardColorsOnStartup;
            set
            {
                if (_applyKeyboardColorsOnStartup != value)
                {
                    _applyKeyboardColorsOnStartup = value;
                    OnPropertyChanged();
                    SaveKeyboardStartupSetting();
                }
            }
        }

        #region Zone Color Properties
        
        public string Zone1ColorHex
        {
            get => _zone1ColorHex;
            set
            {
                if (_zone1ColorHex != value)
                {
                    _zone1ColorHex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Zone1Brush));
                }
            }
        }
        
        public string Zone2ColorHex
        {
            get => _zone2ColorHex;
            set
            {
                if (_zone2ColorHex != value)
                {
                    _zone2ColorHex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Zone2Brush));
                }
            }
        }
        
        public string Zone3ColorHex
        {
            get => _zone3ColorHex;
            set
            {
                if (_zone3ColorHex != value)
                {
                    _zone3ColorHex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Zone3Brush));
                }
            }
        }
        
        public string Zone4ColorHex
        {
            get => _zone4ColorHex;
            set
            {
                if (_zone4ColorHex != value)
                {
                    _zone4ColorHex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Zone4Brush));
                }
            }
        }
        
        // Brushes for XAML binding
        public SolidColorBrush Zone1Brush => new(ParseMediaColor(_zone1ColorHex));
        public SolidColorBrush Zone2Brush => new(ParseMediaColor(_zone2ColorHex));
        public SolidColorBrush Zone3Brush => new(ParseMediaColor(_zone3ColorHex));
        public SolidColorBrush Zone4Brush => new(ParseMediaColor(_zone4ColorHex));
        
        public KeyboardPreset? SelectedKeyboardPreset
        {
            get => _selectedKeyboardPreset;
            set
            {
                if (_selectedKeyboardPreset != value)
                {
                    _selectedKeyboardPreset = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        ApplyKeyboardPresetColors(value);
                    }
                }
            }
        }
        
        #endregion

        #region Temperature-Responsive Lighting Properties
        
        public bool TemperatureResponsiveLightingEnabled
        {
            get => _temperatureResponsiveLightingEnabled;
            set
            {
                if (_temperatureResponsiveLightingEnabled != value)
                {
                    _temperatureResponsiveLightingEnabled = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        StartTemperatureMonitoring();
                    }
                    else
                    {
                        StopTemperatureMonitoring();
                    }
                }
            }
        }
        
        public bool PerformanceModeSyncedLightingEnabled
        {
            get => _performanceModeSyncedLightingEnabled;
            set
            {
                if (_performanceModeSyncedLightingEnabled != value)
                {
                    _performanceModeSyncedLightingEnabled = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        StartPerformanceModeMonitoring();
                    }
                    else
                    {
                        StopPerformanceModeMonitoring();
                    }
                }
            }
        }
        
        public bool ThrottlingIndicatorLightingEnabled
        {
            get => _throttlingIndicatorLightingEnabled;
            set
            {
                if (_throttlingIndicatorLightingEnabled != value)
                {
                    _throttlingIndicatorLightingEnabled = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        StartThrottlingMonitoring();
                    }
                    else
                    {
                        StopThrottlingMonitoring();
                    }
                }
            }
        }
        
        public double CpuTempThresholdLow
        {
            get => _cpuTempThresholdLow;
            set
            {
                if (_cpuTempThresholdLow != value)
                {
                    _cpuTempThresholdLow = Math.Clamp(value, 20, 80);
                    OnPropertyChanged();
                }
            }
        }
        
        public double CpuTempThresholdMedium
        {
            get => _cpuTempThresholdMedium;
            set
            {
                if (_cpuTempThresholdMedium != value)
                {
                    _cpuTempThresholdMedium = Math.Clamp(value, 40, 90);
                    OnPropertyChanged();
                }
            }
        }
        
        public double CpuTempThresholdHigh
        {
            get => _cpuTempThresholdHigh;
            set
            {
                if (_cpuTempThresholdHigh != value)
                {
                    _cpuTempThresholdHigh = Math.Clamp(value, 60, 100);
                    OnPropertyChanged();
                }
            }
        }
        
        public double GpuTempThresholdLow
        {
            get => _gpuTempThresholdLow;
            set
            {
                if (_gpuTempThresholdLow != value)
                {
                    _gpuTempThresholdLow = Math.Clamp(value, 20, 70);
                    OnPropertyChanged();
                }
            }
        }
        
        public double GpuTempThresholdMedium
        {
            get => _gpuTempThresholdMedium;
            set
            {
                if (_gpuTempThresholdMedium != value)
                {
                    _gpuTempThresholdMedium = Math.Clamp(value, 35, 85);
                    OnPropertyChanged();
                }
            }
        }
        
        public double GpuTempThresholdHigh
        {
            get => _gpuTempThresholdHigh;
            set
            {
                if (_gpuTempThresholdHigh != value)
                {
                    _gpuTempThresholdHigh = Math.Clamp(value, 50, 95);
                    OnPropertyChanged();
                }
            }
        }
        
        public string TempLowColorHex
        {
            get => _tempLowColorHex;
            set
            {
                if (_tempLowColorHex != value)
                {
                    _tempLowColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string TempMediumColorHex
        {
            get => _tempMediumColorHex;
            set
            {
                if (_tempMediumColorHex != value)
                {
                    _tempMediumColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string TempHighColorHex
        {
            get => _tempHighColorHex;
            set
            {
                if (_tempHighColorHex != value)
                {
                    _tempHighColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ThrottlingColorHex
        {
            get => _throttlingColorHex;
            set
            {
                if (_throttlingColorHex != value)
                {
                    _throttlingColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string BalancedModeColorHex
        {
            get => _balancedModeColorHex;
            set
            {
                if (_balancedModeColorHex != value)
                {
                    _balancedModeColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string PerformanceModeColorHex
        {
            get => _performanceModeColorHex;
            set
            {
                if (_performanceModeColorHex != value)
                {
                    _performanceModeColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string QuietModeColorHex
        {
            get => _quietModeColorHex;
            set
            {
                if (_quietModeColorHex != value)
                {
                    _quietModeColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string CustomModeColorHex
        {
            get => _customModeColorHex;
            set
            {
                if (_customModeColorHex != value)
                {
                    _customModeColorHex = value;
                    OnPropertyChanged();
                }
            }
        }
        
        #endregion

        public CorsairDevice? SelectedCorsairDevice
        {
            get => _selectedCorsairDevice;
            set
            {
                if (_selectedCorsairDevice != value)
                {
                    _selectedCorsairDevice = value;
                    OnPropertyChanged();
                    (ApplyCorsairLightingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedCorsairLightingPreset));
                    (ApplyCorsairLightingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (ApplyCorsairPresetToSystemCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // Alias for XAML binding compatibility
        public CorsairLightingPreset? SelectedCorsairLightingPreset
        {
            get => SelectedCorsairPreset;
            set => SelectedCorsairPreset = value;
        }

        public LogitechDevice? SelectedLogitechDevice
        {
            get => _selectedLogitechDevice;
            set
            {
                if (_selectedLogitechDevice != value)
                {
                    _selectedLogitechDevice = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        LogitechColorHex = value.CurrentColorHex;
                        LogitechBrightness = value.Status.BrightnessPercent;
                    }
                    (ApplyLogitechColorCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
                }
            }
        }

        public string CorsairColorHex
        {
            get => _corsairColorHex;
            set
            {
                if (_corsairColorHex != value)
                {
                    _corsairColorHex = value;
                    OnPropertyChanged();
                }
            }
        }

        public int LogitechRedValue
        {
            get => _logitechRedValue;
            set
            {
                if (_logitechRedValue != value)
                {
                    _logitechRedValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateLogitechHexFromRgb();
                }
            }
        }

        public int LogitechGreenValue
        {
            get => _logitechGreenValue;
            set
            {
                if (_logitechGreenValue != value)
                {
                    _logitechGreenValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateLogitechHexFromRgb();
                }
            }
        }

        public int LogitechBlueValue
        {
            get => _logitechBlueValue;
            set
            {
                if (_logitechBlueValue != value)
                {
                    _logitechBlueValue = Math.Clamp(value, 0, 255);
                    OnPropertyChanged();
                    UpdateLogitechHexFromRgb();
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
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<MacroProfile> MacroProfiles { get; } = new();

        // System RGB
        private string _systemColorHex = "#FF0000";
        public string SystemColorHex
        {
            get => _systemColorHex;
            set
            {
                if (_systemColorHex != value)
                {
                    _systemColorHex = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ApplyToSystemCommand { get; }

        private async Task ApplyColorToSystemAsync()
        {
            if (_rgbManager == null) return;
            await _rgbManager.ApplyEffectToAllAsync($"color:{SystemColorHex}");
        }

        public string CorsairDeviceStatusText => $"{CorsairDevices.Count} device(s) detected";
        public string LogitechDeviceStatusText => $"{LogitechDevices.Count} device(s) detected";
        public bool HasCorsairMouse => CorsairDevices.Any(d => d.DeviceType == CorsairDeviceType.Mouse);
        public ObservableCollection<CorsairDpiStage> CorsairDpiStages { get; } = new();
        public ObservableCollection<OmenCore.Corsair.CorsairDpiProfile> CorsairDpiProfiles { get; } = new();
        private OmenCore.Corsair.CorsairDpiProfile? _selectedCorsairDpiProfile;
        public OmenCore.Corsair.CorsairDpiProfile? SelectedCorsairDpiProfile
        {
            get => _selectedCorsairDpiProfile;
            set
            {
                if (_selectedCorsairDpiProfile != value)
                {
                    _selectedCorsairDpiProfile = value;
                    OnPropertyChanged();
                    (ApplyCorsairDpiProfileCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCorsairDpiProfileCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand DiscoverCorsairCommand { get; }
        public ICommand ApplyCorsairLightingCommand { get; }
        public ICommand ApplyCorsairCustomColorCommand { get; }
        public ICommand ApplyCorsairDpiCommand { get; }
        public ICommand RestoreCorsairDpiCommand { get; }
        public ICommand SaveCorsairDpiProfileCommand { get; }
        public ICommand ApplyCorsairDpiProfileCommand { get; }
        public ICommand DeleteCorsairDpiProfileCommand { get; }
        public ICommand FlashCorsairDeviceCommand { get; }
        public ICommand DiscoverLogitechCommand { get; }
        public ICommand ApplyLogitechColorCommand { get; }
        public ICommand DiscoverCorsairDevicesCommand { get; }
        public ICommand DiscoverLogitechDevicesCommand { get; }
        public ICommand LoadMacroProfileCommand { get; }
        
        // Razer Commands
        public ICommand DiscoverRazerDevicesCommand { get; }
        public ICommand ApplyRazerColorCommand { get; }
        public ICommand ApplyRazerBreathingCommand { get; }
        public ICommand ApplyRazerSpectrumCommand { get; }
        
        // OpenRGB Commands
        public ICommand ApplyOpenRgbColorCommand { get; }
        
        // 4-Zone Keyboard Commands
        public ICommand ApplyKeyboardColorsCommand { get; }
        public ICommand ApplyAllZonesSameColorCommand { get; }
        public ICommand ApplyQuickColorCommand { get; }
        public ICommand SetZone1ColorCommand { get; }
        public ICommand SetZone2ColorCommand { get; }
        public ICommand SetZone3ColorCommand { get; }
        public ICommand SetZone4ColorCommand { get; }

        public LightingViewModel(CorsairDeviceService? corsairService, LogitechDeviceService? logitechService, LoggingService logging, KeyboardLightingService? keyboardLightingService = null, ConfigurationService? configService = null, RazerService? razerService = null, OmenCore.Services.Rgb.RgbManager? rgbManager = null, HardwareMonitoringService? hardwareMonitoringService = null, PerformanceModeService? performanceModeService = null, RgbSceneService? sceneService = null, ScreenSamplingService? screenSamplingService = null)
        {
            _corsairService = corsairService;
            _logitechService = logitechService;
            _razerService = razerService;
            _keyboardLightingService = keyboardLightingService;
            _configService = configService;
            _logging = logging;
            _rgbManager = rgbManager;
            _hardwareMonitoringService = hardwareMonitoringService;
            _performanceModeService = performanceModeService;
            _sceneService = sceneService;
            _screenSamplingService = screenSamplingService;
            
            // Get OpenRGB provider from RgbManager if available
            _openRgbProvider = rgbManager?.GetProvider("openrgb") as OmenCore.Services.Rgb.OpenRgbProvider;
            
            // Load saved keyboard colors from config
            LoadKeyboardColorsFromConfig();

            // Initialize Corsair commands (only functional if service is available)
            DiscoverCorsairCommand = new AsyncRelayCommand(async _ => { if (_corsairService != null) await _corsairService.DiscoverAsync(); });
            DiscoverCorsairDevicesCommand = new AsyncRelayCommand(async _ => { if (_corsairService != null) await _corsairService.DiscoverAsync(); });
            ApplyCorsairLightingCommand = new AsyncRelayCommand(async _ => await ApplyCorsairLightingAsync(), _ => SelectedCorsairPreset != null && _corsairService != null);
            ApplyCorsairCustomColorCommand = new AsyncRelayCommand(async _ => await ApplyCorsairCustomColorAsync());
            ApplyCorsairDpiCommand = new AsyncRelayCommand(async _ => await ApplyCorsairDpiAsync());
            RestoreCorsairDpiCommand = new AsyncRelayCommand(async _ => await RestoreCorsairDpiAsync());
            ApplyCorsairPresetToSystemCommand = new AsyncRelayCommand(async _ => await ApplyCorsairPresetToSystemAsync(), _ => SelectedCorsairPreset != null && _corsairService != null);
            SaveCorsairDpiProfileCommand = new AsyncRelayCommand(async _ => await SaveCorsairDpiProfileAsync());
            ApplyCorsairDpiProfileCommand = new AsyncRelayCommand(async _ => await ApplyCorsairDpiProfileAsync(), _ => SelectedCorsairDpiProfile != null);
            DeleteCorsairDpiProfileCommand = new AsyncRelayCommand(async _ => await DeleteCorsairDpiProfileAsync(), _ => SelectedCorsairDpiProfile != null);
            FlashCorsairDeviceCommand = new RelayCommand(async device => 
            {
                if (device is CorsairDevice corsairDevice && _corsairService != null)
                    await _corsairService.FlashDeviceAsync(corsairDevice);
            });
            
            // Sync All RGB Command
            SyncAllRgbCommand = new AsyncRelayCommand(async _ => await SyncAllRgbAsync());
            
            // Initialize Logitech commands (only functional if service is available)
            DiscoverLogitechCommand = new AsyncRelayCommand(async _ => { if (_logitechService != null) await _logitechService.DiscoverAsync(); });
            DiscoverLogitechDevicesCommand = new AsyncRelayCommand(async _ => { if (_logitechService != null) await _logitechService.DiscoverAsync(); });
            ApplyLogitechColorCommand = new AsyncRelayCommand(async _ => await ApplyLogitechColorAsync(), _ => SelectedLogitechDevice != null && _logitechService != null);
            LoadMacroProfileCommand = new AsyncRelayCommand(async _ => await LoadMacroProfileAsync());
            
            // Razer Commands
            DiscoverRazerDevicesCommand = new AsyncRelayCommand(async _ => await DiscoverRazerDevicesAsync());
            ApplyRazerColorCommand = new AsyncRelayCommand(async _ => await ApplyRazerColorAsync());
            ApplyRazerBreathingCommand = new AsyncRelayCommand(async _ => await ApplyRazerBreathingAsync());
            ApplyRazerSpectrumCommand = new AsyncRelayCommand(async _ => await ApplyRazerSpectrumAsync());
            
            // OpenRGB Commands
            ApplyOpenRgbColorCommand = new AsyncRelayCommand(async param => await ApplyOpenRgbColorAsync(param as string));
            
            // Initialize Razer service
            _razerService?.Initialize();
            
            // 4-Zone Keyboard Commands
            ApplyKeyboardColorsCommand = new AsyncRelayCommand(async _ => await ApplyKeyboardColorsAsync());
            ApplyAllZonesSameColorCommand = new AsyncRelayCommand(async _ => await ApplyAllZonesSameColorAsync());
            ApplyQuickColorCommand = new AsyncRelayCommand(async param => await ApplyQuickColorAsync(param as string));
            SetZone1ColorCommand = new RelayCommand(_ => OpenColorPickerForZone(1, "WASD"));
            SetZone2ColorCommand = new RelayCommand(_ => OpenColorPickerForZone(2, "Left"));
            SetZone3ColorCommand = new RelayCommand(_ => OpenColorPickerForZone(3, "Right"));
            SetZone4ColorCommand = new RelayCommand(_ => OpenColorPickerForZone(4, "Far Right"));
            
            // Scene Commands
            ApplySceneCommand = new AsyncRelayCommand(async param => await ApplySceneFromParameterAsync(param));
            SaveAsSceneCommand = new AsyncRelayCommand(async _ => await SaveCurrentAsSceneAsync());
            ToggleAmbientModeCommand = new RelayCommand(_ => IsAmbientModeActive = !IsAmbientModeActive);
            
            // Initialize scenes from service
            InitializeScenesFromService();

            // Initialize lighting presets - prefer saved config presets when available
            if (_configService?.Config?.CorsairLightingPresets != null && _configService.Config.CorsairLightingPresets.Count > 0)
            {
                CorsairLightingPresets.Clear();
                foreach (var preset in _configService.Config.CorsairLightingPresets)
                {
                    CorsairLightingPresets.Add(preset);
                }
            }
            else
            {
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Red", ColorHex = "#FF0000" });
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Green", ColorHex = "#00FF00" });
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Blue", ColorHex = "#0000FF" });
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Purple", ColorHex = "#9B30FF" });
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Cyan", ColorHex = "#00FFFF" });
                CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "White", ColorHex = "#FFFFFF" });
            }

            SelectedCorsairPreset = CorsairLightingPresets.FirstOrDefault();

            // Load saved DPI profiles from config
            if (_configService?.Config?.CorsairDpiProfiles != null)
            {
                CorsairDpiProfiles.Clear();
                foreach (var p in _configService.Config.CorsairDpiProfiles)
                {
                    CorsairDpiProfiles.Add(p);
                }
            }
            
            // Initialize keyboard presets
            KeyboardPresets.Add(new KeyboardPreset { Name = "OMEN Red", Zone1 = "#E6002E", Zone2 = "#E6002E", Zone3 = "#E6002E", Zone4 = "#E6002E" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Gaming", Zone1 = "#FF0000", Zone2 = "#FF4500", Zone3 = "#FF6600", Zone4 = "#FF0000" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Cool Blue", Zone1 = "#0066FF", Zone2 = "#00CCFF", Zone3 = "#00CCFF", Zone4 = "#0066FF" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Rainbow", Zone1 = "#FF0000", Zone2 = "#00FF00", Zone3 = "#0000FF", Zone4 = "#FFFF00" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Purple Haze", Zone1 = "#9400D3", Zone2 = "#8B008B", Zone3 = "#9932CC", Zone4 = "#9400D3" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "White", Zone1 = "#FFFFFF", Zone2 = "#FFFFFF", Zone3 = "#FFFFFF", Zone4 = "#FFFFFF" });
            
            // New OMEN Light Studio style presets
            KeyboardPresets.Add(new KeyboardPreset { Name = "Wave Blue", Zone1 = "#0033FF", Zone2 = "#0066FF", Zone3 = "#0099FF", Zone4 = "#00CCFF" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Wave Red", Zone1 = "#FF0000", Zone2 = "#FF3300", Zone3 = "#FF6600", Zone4 = "#FF9900" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Breathing Green", Zone1 = "#00FF00", Zone2 = "#00FF00", Zone3 = "#00FF00", Zone4 = "#00FF00" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Reactive Purple", Zone1 = "#6600FF", Zone2 = "#9900FF", Zone3 = "#CC00FF", Zone4 = "#FF00FF" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Spectrum Flow", Zone1 = "#FF0000", Zone2 = "#FFFF00", Zone3 = "#00FF00", Zone4 = "#00FFFF" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Audio Reactive", Zone1 = "#FF4500", Zone2 = "#FF6600", Zone3 = "#FF8700", Zone4 = "#FFA800" });
            
            // Additional presets for more variety (v2.7.1)
            KeyboardPresets.Add(new KeyboardPreset { Name = "Cyberpunk", Zone1 = "#FF00FF", Zone2 = "#00FFFF", Zone3 = "#FF00FF", Zone4 = "#00FFFF" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Sunset", Zone1 = "#FF4500", Zone2 = "#FF6347", Zone3 = "#FF7F50", Zone4 = "#FFA07A" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Forest", Zone1 = "#228B22", Zone2 = "#32CD32", Zone3 = "#90EE90", Zone4 = "#228B22" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Ocean", Zone1 = "#000080", Zone2 = "#0000FF", Zone3 = "#1E90FF", Zone4 = "#00CED1" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Lava", Zone1 = "#8B0000", Zone2 = "#FF0000", Zone3 = "#FF4500", Zone4 = "#FF6600" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Ice", Zone1 = "#E0FFFF", Zone2 = "#B0E0E6", Zone3 = "#87CEEB", Zone4 = "#ADD8E6" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Stealth", Zone1 = "#1A1A1A", Zone2 = "#2D2D2D", Zone3 = "#404040", Zone4 = "#1A1A1A" });
            KeyboardPresets.Add(new KeyboardPreset { Name = "Off", Zone1 = "#000000", Zone2 = "#000000", Zone3 = "#000000", Zone4 = "#000000" });
            
            // Only select default preset if we didn't load colors from config
            // (selecting a preset overwrites the colors, which would discard saved colors)
            if (!_colorsLoadedFromConfig)
            {
                SelectedKeyboardPreset = KeyboardPresets.FirstOrDefault();
            }
            
            // Initialize default DPI stages for mouse configuration
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 1", Dpi = 800, IsDefault = true });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 2", Dpi = 1600 });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 3", Dpi = 3200 });

            // Initialize macro profiles
            MacroProfiles.Add(new MacroProfile { Name = "Default" });
            MacroProfiles.Add(new MacroProfile { Name = "Gaming" });
            MacroProfiles.Add(new MacroProfile { Name = "Productivity" });
            SelectedMacroProfile = MacroProfiles.FirstOrDefault();

            // System RGB controls
            SystemColorHex = "#FF0000";
            ApplyToSystemCommand = new AsyncRelayCommand(async _ => await ApplyColorToSystemAsync());
            
            // Initialize temperature-responsive lighting monitoring
            if (_hardwareMonitoringService != null)
            {
                _hardwareMonitoringService.SampleUpdated += OnMonitoringSampleUpdated;
            }
            
            // Initialize performance mode monitoring
            if (_performanceModeService != null)
            {
                _performanceModeService.ModeApplied += OnPerformanceModeApplied;
            }
        }

        private void OpenColorPickerForZone(int zoneNumber, string zoneName)
        {
            // Get the current color for this zone
            string currentColor = zoneNumber switch
            {
                1 => Zone1ColorHex,
                2 => Zone2ColorHex,
                3 => Zone3ColorHex,
                4 => Zone4ColorHex,
                _ => "#E6002E"
            };

            // Create and show the color picker dialog
            var dialog = new ColorPickerDialog
            {
                Owner = Application.Current.MainWindow
            };
            dialog.SetZoneInfo(zoneNumber, zoneName);
            dialog.SetInitialColor(currentColor);

            if (dialog.ShowDialog() == true && dialog.DialogResultOk)
            {
                // Update the zone color
                switch (zoneNumber)
                {
                    case 1:
                        Zone1ColorHex = dialog.SelectedHexColor;
                        break;
                    case 2:
                        Zone2ColorHex = dialog.SelectedHexColor;
                        break;
                    case 3:
                        Zone3ColorHex = dialog.SelectedHexColor;
                        break;
                    case 4:
                        Zone4ColorHex = dialog.SelectedHexColor;
                        break;
                }
                
                _logging.Info($"Zone {zoneNumber} color set to {dialog.SelectedHexColor}");
            }
        }

        private void UpdateLogitechHexFromRgb()
        {
            _logitechColorHex = $"#{_logitechRedValue:X2}{_logitechGreenValue:X2}{_logitechBlueValue:X2}";
            OnPropertyChanged(nameof(LogitechColorHex));
        }

        private void UpdateRazerHexFromRgb()
        {
            _razerColorHex = $"#{_razerRedValue:X2}{_razerGreenValue:X2}{_razerBlueValue:X2}";
            OnPropertyChanged(nameof(RazerColorHex));
        }

        #region Razer Methods

        private async Task DiscoverRazerDevicesAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _razerService?.DiscoverDevices();
                _razerDevices.Clear();
                if (_razerService?.Devices != null)
                {
                    foreach (var device in _razerService.Devices)
                    {
                        _razerDevices.Add(device);
                    }
                }
                OnPropertyChanged(nameof(RazerDevices));
                OnPropertyChanged(nameof(HasRazerDevices));
                OnPropertyChanged(nameof(RazerDeviceStatusText));
                await Task.CompletedTask;
            }, "Discovering Razer devices...");
        }

        private async Task ApplyRazerColorAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _razerService?.SetStaticColor((byte)_razerRedValue, (byte)_razerGreenValue, (byte)_razerBlueValue);
                _logging.Info($"Applied Razer static color: {RazerColorHex}");
                await Task.CompletedTask;
            }, "Applying Razer color...");
        }

        private async Task ApplyRazerBreathingAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _razerService?.SetBreathingEffect((byte)_razerRedValue, (byte)_razerGreenValue, (byte)_razerBlueValue);
                _logging.Info($"Applied Razer breathing effect: {RazerColorHex}");
                await Task.CompletedTask;
            }, "Applying Razer breathing...");
        }

        private async Task ApplyRazerSpectrumAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _razerService?.SetSpectrumEffect();
                _logging.Info("Applied Razer spectrum cycling effect");
                await Task.CompletedTask;
            }, "Applying Razer spectrum...");
        }
        
        private async Task ApplyOpenRgbColorAsync(string? colorHex)
        {
            if (_openRgbProvider == null || !_openRgbProvider.IsAvailable) return;
            
            await ExecuteWithLoadingAsync(async () =>
            {
                try
                {
                    var color = string.IsNullOrEmpty(colorHex) 
                        ? System.Drawing.Color.Red 
                        : System.Drawing.ColorTranslator.FromHtml(colorHex);
                    await _openRgbProvider.SetStaticColorAsync(color);
                    _logging.Info($"Applied OpenRGB color: {colorHex ?? "#FF0000"} to {_openRgbProvider.DeviceCount} device(s)");
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply OpenRGB color: {ex.Message}");
                }
            }, "Applying OpenRGB color...");
        }

        #endregion

        private async Task ApplyCorsairLightingAsync()
        {
            if (SelectedCorsairPreset != null && _corsairService != null)
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    await _corsairService.ApplyLightingToAllAsync(SelectedCorsairPreset.ColorHex);
                    _logging.Info($"Applied Corsair lighting preset: {SelectedCorsairPreset.Name}");
                }, "Applying Corsair lighting...");
            }
        }

        private async Task ApplyCorsairPresetToSystemAsync()
        {
            if (SelectedCorsairPreset == null || _rgbManager == null) return;

            await ExecuteWithLoadingAsync(async () =>
            {
                await _rgbManager.ApplyEffectToAllAsync($"preset:{SelectedCorsairPreset.Name}");
                _logging.Info($"Applied Corsair preset '{SelectedCorsairPreset.Name}' to system");
            }, "Applying lighting preset to system...");
        }
        
        /// <summary>
        /// Sync a color across all connected RGB devices (Corsair, Logitech, Razer, HP keyboard).
        /// Uses the current Corsair custom color as the sync color.
        /// </summary>
        private async Task SyncAllRgbAsync()
        {
            var syncColor = CorsairColorHex; // Use Corsair color picker as the sync source
            
            await ExecuteWithLoadingAsync(async () =>
            {
                int successCount = 0;
                
                // Apply to Corsair devices
                if (_corsairService != null && IsCorsairConnected && CorsairDevices.Count > 0)
                {
                    try
                    {
                        await _corsairService.ApplyLightingToAllAsync(syncColor);
                        successCount++;
                        _logging.Info($"Synced color {syncColor} to Corsair devices");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Failed to sync to Corsair: {ex.Message}");
                    }
                }
                
                // Apply to Logitech devices (apply to each device individually)
                if (_logitechService != null && IsLogitechConnected)
                {
                    try
                    {
                        foreach (var device in LogitechDevices)
                        {
                            await _logitechService.ApplyStaticColorAsync(device, syncColor, LogitechBrightness);
                        }
                        successCount++;
                        _logging.Info($"Synced color {syncColor} to Logitech devices");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Failed to sync to Logitech: {ex.Message}");
                    }
                }
                
                // Apply to Razer devices (synchronous method, wrap in Task.Run)
                if (IsRazerConnected && _razerService != null)
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            var color = System.Drawing.ColorTranslator.FromHtml(syncColor);
                            _razerService.SetStaticColor(color.R, color.G, color.B);
                        });
                        successCount++;
                        _logging.Info($"Synced color {syncColor} to Razer devices");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Failed to sync to Razer: {ex.Message}");
                    }
                }
                
                // Apply to HP OMEN keyboard (synchronous method, wrap in Task.Run)
                if (IsKeyboardLightingAvailable && _keyboardLightingService != null)
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            var color = System.Drawing.ColorTranslator.FromHtml(syncColor);
                            var colors = new[] { color, color, color, color };
                            _keyboardLightingService.SetAllZoneColors(colors);
                        });
                        successCount++;
                        _logging.Info($"Synced color {syncColor} to OMEN keyboard");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"Failed to sync to OMEN keyboard: {ex.Message}");
                    }
                }
                
                // Use RgbManager for any other registered providers
                if (_rgbManager != null)
                {
                    try
                    {
                        await _rgbManager.ApplyEffectToAllAsync($"color:{syncColor}");
                        _logging.Info($"Synced color {syncColor} via RgbManager");
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"RgbManager sync failed: {ex.Message}");
                    }
                }
                
                _logging.Info($"RGB Sync complete: {successCount} device group(s) updated");
            }, "Syncing color to all RGB devices...");
        }

        private async Task ApplyCorsairCustomColorAsync()
        {
            if (_corsairService == null) return;
            await ExecuteWithLoadingAsync(async () =>
            {
                await _corsairService.ApplyLightingToAllAsync(CorsairColorHex);
                _logging.Info($"Applied custom Corsair color: {CorsairColorHex}");
            }, "Applying custom color...");
        }

        public async Task ApplyCorsairDpiAsync(bool skipConfirmation = false)
        {
            if (SelectedCorsairDevice == null)
            {
                _logging.Warn("No Corsair device selected for DPI apply");
                return;
            }

            if (!skipConfirmation)
            {
                var res = MessageBox.Show(
                    "This will change the hardware DPI settings on the selected device. Do you want to continue?",
                    "Confirm DPI Change",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (res != MessageBoxResult.Yes)
                {
                    _logging.Info("User cancelled DPI apply");
                    return;
                }
            }

            await ExecuteWithLoadingAsync(async () =>
            {
                if (_corsairService == null) return;
                
                // Log the intended stages
                foreach (var stage in CorsairDpiStages)
                {
                    _logging.Info($"Applying DPI Stage {stage.Name}: {stage.Dpi} DPI");
                }

                try
                {
                    await _corsairService.ApplyDpiStagesAsync(SelectedCorsairDevice, CorsairDpiStages);

                    // Update the device model to reflect the new DPI values
                    SelectedCorsairDevice.DpiStages.Clear();
                    foreach (var s in CorsairDpiStages)
                    {
                        SelectedCorsairDevice.DpiStages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
                    }

                    // Optionally persist as defaults
                    if (_configService != null)
                    {
                        _configService.Config.DefaultCorsairDpi = CorsairDpiStages.Select(s => new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index }).ToList();
                        _configService.Save(_configService.Config);
                    }

                    // Also update the selected profile if one is chosen (save changes back to profile)
                    if (SelectedCorsairDpiProfile != null)
                    {
                        SelectedCorsairDpiProfile.Stages.Clear();
                        foreach (var s in CorsairDpiStages)
                        {
                            SelectedCorsairDpiProfile.Stages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
                        }
                        // Persist profile changes to config
                        if (_configService != null)
                        {
                            var cfgList = _configService.Config.CorsairDpiProfiles;
                            // find and replace by name
                            for (int i = 0; i < cfgList.Count; i++)
                            {
                                if (cfgList[i].Name == SelectedCorsairDpiProfile.Name)
                                {
                                    cfgList[i] = SelectedCorsairDpiProfile;
                                    break;
                                }
                            }
                            _configService.Save(_configService.Config);
                        }
                    }

                    _logging.Info("DPI settings applied successfully");
                }
                catch (Exception ex)
                {
                    _logging.Error("Failed to apply DPI settings", ex);
                }

                await Task.CompletedTask;
            }, "Applying DPI settings...");
        }

        private async Task ApplyLogitechColorAsync()
        {
            if (SelectedLogitechDevice != null && _logitechService != null)
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    await _logitechService.ApplyStaticColorAsync(SelectedLogitechDevice, LogitechColorHex, LogitechBrightness);
                }, "Applying Logitech lighting...");
            }
        }

        public Task RestoreCorsairDpiAsync(bool skipConfirmation = false)
        {
            if (!skipConfirmation)
            {
                var res = MessageBox.Show(
                    "This will restore DPI stages to their saved defaults (or built-in defaults if none). Continue?",
                    "Restore DPI Defaults",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (res != MessageBoxResult.Yes)
                {
                    _logging.Info("User cancelled DPI restore");
                    return Task.CompletedTask;
                }
            }

            // Use config defaults if present
            if (_configService?.Config?.DefaultCorsairDpi != null && _configService.Config.DefaultCorsairDpi.Count > 0)
            {
                CorsairDpiStages.Clear();
                foreach (var s in _configService.Config.DefaultCorsairDpi)
                {
                    CorsairDpiStages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
                }
                _logging.Info("Restored DPI stages from config defaults");
                return Task.CompletedTask;
            }

            // Fallback built-in defaults
            CorsairDpiStages.Clear();
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 1", Dpi = 800, IsDefault = true, Index = 0 });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 2", Dpi = 1600, Index = 1 });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 3", Dpi = 3200, Index = 2 });
            _logging.Info("Restored built-in DPI defaults");
            return Task.CompletedTask;
        }

        public async Task ApplyCorsairDpiProfileAsync()
        {
            if (SelectedCorsairDpiProfile == null)
            {
                _logging.Warn("No DPI profile selected");
                return;
            }

            CorsairDpiStages.Clear();
            foreach (var s in SelectedCorsairDpiProfile.Stages)
            {
                CorsairDpiStages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
            }

            // Optionally apply to device immediately
            await ApplyCorsairDpiAsync();
        }

        public async Task SaveCorsairDpiProfileAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return;
            var trimmed = profileName.Trim();

            var existing = CorsairDpiProfiles.FirstOrDefault(p => p.Name == trimmed);
            if (existing != null)
            {
                // Overwrite existing profile stages
                existing.Stages.Clear();
                foreach (var s in CorsairDpiStages)
                {
                    existing.Stages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
                }

                if (_configService != null)
                {
                    _configService.Config.CorsairDpiProfiles = CorsairDpiProfiles.ToList();
                    _configService.Save(_configService.Config);
                }

                _logging.Info($"Overwrote DPI profile '{existing.Name}'");
                await Task.CompletedTask;
                return;
            }

            var profile = new OmenCore.Corsair.CorsairDpiProfile { Name = trimmed };
            foreach (var s in CorsairDpiStages)
            {
                profile.Stages.Add(new CorsairDpiStage { Name = s.Name, Dpi = s.Dpi, IsDefault = s.IsDefault, AngleSnapping = s.AngleSnapping, LiftOffDistanceMm = s.LiftOffDistanceMm, Index = s.Index });
            }

            CorsairDpiProfiles.Add(profile);
            if (_configService != null)
            {
                _configService.Config.CorsairDpiProfiles = CorsairDpiProfiles.ToList();
                _configService.Save(_configService.Config);
            }

            _logging.Info($"Saved DPI profile '{profile.Name}'");
            await Task.CompletedTask;
        }

        private async Task SaveCorsairDpiProfileAsync()
        {
            // Prompt for a profile name
            var namePrompt = new InputPromptWindow("Save DPI Profile", "Enter a name for the DPI profile:")
            {
                Owner = Application.Current.MainWindow
            };
            if (namePrompt.ShowDialog() != true || string.IsNullOrWhiteSpace(namePrompt.Input))
            {
                _logging.Info("User cancelled Save DPI Profile");
                return;
            }

            var name = namePrompt.Input.Trim();
            var exists = CorsairDpiProfiles.Any(p => p.Name == name);
            if (exists)
            {
                var res = MessageBox.Show($"A DPI profile named '{name}' already exists. Overwrite it?", "Overwrite DPI Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (res != MessageBoxResult.Yes)
                {
                    _logging.Info("User declined to overwrite existing DPI profile");
                    return;
                }
            }

            await SaveCorsairDpiProfileAsync(name);
        }

        public async Task DeleteCorsairDpiProfileByNameAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return;
            var toRemove = CorsairDpiProfiles.FirstOrDefault(p => p.Name == profileName);
            if (toRemove == null) return;
            CorsairDpiProfiles.Remove(toRemove);
            if (SelectedCorsairDpiProfile == toRemove)
            {
                SelectedCorsairDpiProfile = CorsairDpiProfiles.FirstOrDefault();
            }
            if (_configService != null)
            {
                _configService.Config.CorsairDpiProfiles = CorsairDpiProfiles.ToList();
                _configService.Save(_configService.Config);
            }
            _logging.Info($"Deleted DPI profile '{profileName}'");
            await Task.CompletedTask;
        }

        private async Task DeleteCorsairDpiProfileAsync()
        {
            if (SelectedCorsairDpiProfile == null)
            {
                _logging.Warn("No DPI profile selected");
                return;
            }

            var res = MessageBox.Show(
                $"Are you sure you want to delete the DPI profile '{SelectedCorsairDpiProfile.Name}'? This cannot be undone.",
                "Delete DPI Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (res != MessageBoxResult.Yes)
            {
                _logging.Info("User cancelled DPI profile delete");
                return;
            }

            await DeleteCorsairDpiProfileByNameAsync(SelectedCorsairDpiProfile.Name);
        }

        private async Task LoadMacroProfileAsync()
        {
            if (SelectedMacroProfile != null)
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    _logging.Info($"Loading macro profile: {SelectedMacroProfile.Name}");
                    // Note: Full macro implementation would require iCUE/G HUB SDK integration
                    _logging.Warn("Macro profiles are UI placeholders - use iCUE or G HUB for macro configuration");
                    await Task.CompletedTask;
                }, "Loading macro profile...");
            }
        }
        
        #region 4-Zone Keyboard Methods
        
        private async Task ApplyKeyboardColorsAsync()
        {
            if (_keyboardLightingService == null || !_keyboardLightingService.IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available");
                return;
            }
            
            await ExecuteWithLoadingAsync(async () =>
            {
                // WMI Zone mapping (per HP BIOS/OmenMon):
                // WMI Z0 = Right (arrows, nav block, right modifiers)
                // WMI Z1 = Middle-R (right QWERTY: F6-F12, Y-P area)
                // WMI Z2 = Middle-L (left QWERTY: F1-F5, Q-T area)  
                // WMI Z3 = WASD (W/A/S/D keys area)
                //
                // UI Zone mapping (user-facing):
                // Zone1 = WASD (left-most, where WASD is)
                // Zone2 = Middle-L (left QWERTY area)
                // Zone3 = Middle-R (right QWERTY area)
                // Zone4 = Right (arrows, numpad area)
                //
                // So we need to reorder: UI [Z1,Z2,Z3,Z4] -> WMI [Z4,Z3,Z2,Z1]
                var colors = new System.Drawing.Color[]
                {
                    ParseDrawingColor(_zone4ColorHex), // WMI Z0 (Right) = UI Zone4
                    ParseDrawingColor(_zone3ColorHex), // WMI Z1 (Middle-R) = UI Zone3
                    ParseDrawingColor(_zone2ColorHex), // WMI Z2 (Middle-L) = UI Zone2
                    ParseDrawingColor(_zone1ColorHex)  // WMI Z3 (WASD) = UI Zone1
                };
                
                _keyboardLightingService.SetAllZoneColors(colors);
                
                // Save colors to config for persistence
                SaveKeyboardColorsToConfig();
                
                // Log telemetry to help user understand which backend works
                var telemetry = _keyboardLightingService.GetTelemetry();
                if (telemetry != null)
                {
                    _logging.Info($"Keyboard telemetry: WMI {telemetry.WmiSuccessRate:F0}% success, EC {telemetry.EcSuccessRate:F0}% success");
                    
                    // If WMI has high failure rate, suggest EC
                    if (telemetry.WmiSuccessCount == 0 && telemetry.WmiFailureCount > 0)
                    {
                        _logging.Warn("💡 WMI keyboard commands aren't working on your model. Try enabling 'Experimental EC Keyboard' in Settings if RGB doesn't change.");
                    }
                }
                
                _logging.Info($"✓ Applied keyboard zone colors: Z1={_zone1ColorHex}, Z2={_zone2ColorHex}, Z3={_zone3ColorHex}, Z4={_zone4ColorHex}");
                await Task.CompletedTask;
            }, "Applying keyboard colors...");
        }
        
        private async Task ApplyAllZonesSameColorAsync()
        {
            if (_keyboardLightingService == null || !_keyboardLightingService.IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available");
                return;
            }
            
            await ExecuteWithLoadingAsync(async () =>
            {
                // Use Zone 1 color for all zones
                var color = ParseDrawingColor(_zone1ColorHex);
                _keyboardLightingService.SetZoneColor(KeyboardLightingService.KeyboardZone.All, color);
                
                // Update all zone properties to match
                Zone2ColorHex = _zone1ColorHex;
                Zone3ColorHex = _zone1ColorHex;
                Zone4ColorHex = _zone1ColorHex;
                
                // Save colors to config for persistence
                SaveKeyboardColorsToConfig();
                
                _logging.Info($"✓ Applied {_zone1ColorHex} to all keyboard zones");
                await Task.CompletedTask;
            }, "Applying color to all zones...");
        }
        
        /// <summary>
        /// Quick color application - applies a single color to all keyboard zones instantly
        /// </summary>
        private async Task ApplyQuickColorAsync(string? colorHex)
        {
            if (string.IsNullOrEmpty(colorHex)) return;
            
            if (_keyboardLightingService == null || !_keyboardLightingService.IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available");
                return;
            }
            
            try
            {
                var color = ParseDrawingColor(colorHex);
                _keyboardLightingService.SetZoneColor(KeyboardLightingService.KeyboardZone.All, color);
                
                // Update all zone color properties to reflect the change
                Zone1ColorHex = colorHex;
                Zone2ColorHex = colorHex;
                Zone3ColorHex = colorHex;
                Zone4ColorHex = colorHex;
                
                // Save to config for persistence
                SaveKeyboardColorsToConfig();
                
                _logging.Info($"✓ Quick color {colorHex} applied to all keyboard zones");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply quick color: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }
        
        private void LoadKeyboardColorsFromConfig()
        {
            try
            {
                var config = _configService?.Config?.KeyboardLighting;
                if (config == null) return;
                
                // Check if any colors are saved (not null/default)
                bool hasCustomColors = config.Zone1Color != null || config.Zone2Color != null || 
                                       config.Zone3Color != null || config.Zone4Color != null;
                
                _zone1ColorHex = config.Zone1Color ?? "#E6002E";
                _zone2ColorHex = config.Zone2Color ?? "#E6002E";
                _zone3ColorHex = config.Zone3Color ?? "#E6002E";
                _zone4ColorHex = config.Zone4Color ?? "#E6002E";
                
                // Load the apply on startup setting
                _applyKeyboardColorsOnStartup = config.ApplyOnStartup;
                
                // Mark that we loaded colors from config - this prevents the default preset from
                // overwriting our saved colors during initialization
                _colorsLoadedFromConfig = hasCustomColors;
                
                _logging.Info($"Loaded keyboard colors from config: Z1={_zone1ColorHex}, Z2={_zone2ColorHex}, Z3={_zone3ColorHex}, Z4={_zone4ColorHex}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load keyboard colors: {ex.Message}");
            }
        }
        
        private void SaveKeyboardColorsToConfig()
        {
            try
            {
                if (_configService == null) return;
                
                if (_configService.Config.KeyboardLighting == null)
                    _configService.Config.KeyboardLighting = new KeyboardLightingSettings();
                
                _configService.Config.KeyboardLighting.Zone1Color = _zone1ColorHex;
                _configService.Config.KeyboardLighting.Zone2Color = _zone2ColorHex;
                _configService.Config.KeyboardLighting.Zone3Color = _zone3ColorHex;
                _configService.Config.KeyboardLighting.Zone4Color = _zone4ColorHex;
                
                _configService.Save(_configService.Config);
                _logging.Info("Keyboard colors saved to config");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save keyboard colors: {ex.Message}");
            }
        }
        
        private void SaveKeyboardStartupSetting()
        {
            try
            {
                if (_configService == null) return;
                
                if (_configService.Config.KeyboardLighting == null)
                    _configService.Config.KeyboardLighting = new KeyboardLightingSettings();
                
                _configService.Config.KeyboardLighting.ApplyOnStartup = _applyKeyboardColorsOnStartup;
                _configService.Save(_configService.Config);
                _logging.Info($"Keyboard apply on startup setting saved: {_applyKeyboardColorsOnStartup}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save keyboard startup setting: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Applies saved keyboard colors on app startup.
        /// Call this after the keyboard lighting service is ready.
        /// </summary>
        public Task ApplySavedKeyboardColorsAsync()
        {
            try
            {
                var config = _configService?.Config?.KeyboardLighting;
                if (config == null || !config.ApplyOnStartup)
                {
                    _logging.Info("Keyboard color restore disabled or no saved colors");
                    return Task.CompletedTask;
                }
                
                if (_keyboardLightingService == null || !_keyboardLightingService.IsAvailable)
                {
                    _logging.Warn("Keyboard lighting not available for color restore");
                    return Task.CompletedTask;
                }
                
                // Check if user had backlight OFF - don't turn it on!
                if (!config.BacklightWasEnabled)
                {
                    _logging.Info("Keyboard backlight was OFF - respecting user preference, not restoring colors");
                    return Task.CompletedTask;
                }
                
                // Apply the saved colors
                var colors = new System.Drawing.Color[]
                {
                    ParseDrawingColor(_zone1ColorHex),
                    ParseDrawingColor(_zone2ColorHex),
                    ParseDrawingColor(_zone3ColorHex),
                    ParseDrawingColor(_zone4ColorHex)
                };
                
                _keyboardLightingService.SetAllZoneColors(colors);
                _logging.Info($"✓ Restored keyboard colors on startup: Z1={_zone1ColorHex}, Z2={_zone2ColorHex}, Z3={_zone3ColorHex}, Z4={_zone4ColorHex}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore keyboard colors: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void ApplyKeyboardPresetColors(KeyboardPreset preset)
        {
            Zone1ColorHex = preset.Zone1;
            Zone2ColorHex = preset.Zone2;
            Zone3ColorHex = preset.Zone3;
            Zone4ColorHex = preset.Zone4;
        }
        
        private static System.Windows.Media.Color ParseMediaColor(string hex)
        {
            try
            {
                if (hex.StartsWith("#"))
                    hex = hex[1..];
                if (hex.Length == 6)
                {
                    return System.Windows.Media.Color.FromRgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
            }
            catch { }
            return System.Windows.Media.Colors.Red;
        }
        
        private static System.Drawing.Color ParseDrawingColor(string hex)
        {
            try
            {
                if (hex.StartsWith("#"))
                    hex = hex[1..];
                if (hex.Length == 6)
                {
                    return System.Drawing.Color.FromArgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
            }
            catch { }
            return System.Drawing.Color.Red;
        }
        
        #region Temperature-Responsive Lighting Methods
        
        private void StartTemperatureMonitoring()
        {
            if (_hardwareMonitoringService != null)
            {
                _logging.Info("Started temperature-responsive lighting monitoring");
            }
        }
        
        private void StopTemperatureMonitoring()
        {
            if (_hardwareMonitoringService != null)
            {
                _logging.Info("Stopped temperature-responsive lighting monitoring");
            }
        }
        
        private void StartPerformanceModeMonitoring()
        {
            if (_performanceModeService != null)
            {
                _logging.Info("Started performance mode synced lighting monitoring");
            }
        }
        
        private void StopPerformanceModeMonitoring()
        {
            if (_performanceModeService != null)
            {
                _logging.Info("Stopped performance mode synced lighting monitoring");
            }
        }
        
        private void StartThrottlingMonitoring()
        {
            if (_hardwareMonitoringService != null)
            {
                _logging.Info("Started throttling indicator lighting monitoring");
            }
        }
        
        private void StopThrottlingMonitoring()
        {
            if (_hardwareMonitoringService != null)
            {
                _logging.Info("Stopped throttling indicator lighting monitoring");
            }
        }
        
        private void OnMonitoringSampleUpdated(object? sender, MonitoringSample sample)
        {
            if (!TemperatureResponsiveLightingEnabled && !ThrottlingIndicatorLightingEnabled)
                return;
            
            // Temperature-responsive lighting
            if (TemperatureResponsiveLightingEnabled)
            {
                ApplyTemperatureBasedLighting(sample);
            }
            
            // Throttling indicators
            if (ThrottlingIndicatorLightingEnabled && sample.IsThrottling)
            {
                ApplyThrottlingLighting(sample);
            }
        }
        
        private void OnPerformanceModeApplied(object? sender, string modeName)
        {
            if (!PerformanceModeSyncedLightingEnabled)
                return;
            
            ApplyPerformanceModeLighting(modeName);
        }
        
        private async void ApplyTemperatureBasedLighting(MonitoringSample sample)
        {
            try
            {
                // Determine color based on highest temperature (CPU or GPU)
                var maxTemp = Math.Max(sample.CpuTemperatureC, sample.GpuTemperatureC);
                string colorHex;
                
                if (maxTemp >= Math.Max(CpuTempThresholdHigh, GpuTempThresholdHigh))
                {
                    colorHex = TempHighColorHex;
                }
                else if (maxTemp >= Math.Max(CpuTempThresholdMedium, GpuTempThresholdMedium))
                {
                    colorHex = TempMediumColorHex;
                }
                else
                {
                    colorHex = TempLowColorHex;
                }
                
                // Apply to keyboard lighting
                if (_keyboardLightingService?.IsAvailable == true)
                {
                    var color = ParseDrawingColor(colorHex);
                    _keyboardLightingService.SetAllZoneColors(new[] { color, color, color, color });
                }
                
                // Apply to system RGB
                if (_rgbManager != null)
                {
                    await _rgbManager.ApplyEffectToAllAsync($"color:{colorHex}");
                }
                
                // Apply to Corsair devices
                if (_corsairService != null && CorsairDevices.Count > 0)
                {
                    await _corsairService.ApplyLightingToAllAsync(colorHex);
                }
                
                // Apply to Logitech devices
                if (_logitechService != null && LogitechDevices.Count > 0)
                {
                    foreach (var device in LogitechDevices)
                    {
                        await _logitechService.ApplyStaticColorAsync(device, colorHex, LogitechBrightness);
                    }
                }
                
                // Apply to Razer devices
                if (_razerService?.IsAvailable == true && _razerDevices.Count > 0)
                {
                    var color = System.Drawing.ColorTranslator.FromHtml(colorHex);
                    await Task.Run(() => _razerService.SetStaticColor(color.R, color.G, color.B));
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to apply temperature-based lighting", ex);
            }
        }
        
        private async void ApplyThrottlingLighting(MonitoringSample sample)
        {
            try
            {
                // Apply throttling color to indicate thermal/power throttling
                var color = ParseDrawingColor(ThrottlingColorHex);
                
                // Apply to keyboard lighting with pulsing effect
                if (_keyboardLightingService?.IsAvailable == true)
                {
                    _keyboardLightingService.SetAllZoneColors(new[] { color, color, color, color });
                }
                
                // Apply to system RGB with pulsing
                if (_rgbManager != null)
                {
                    await _rgbManager.ApplyEffectToAllAsync($"pulse:{ThrottlingColorHex}:1000");
                }
                
                // Apply to Corsair devices
                if (_corsairService != null && CorsairDevices.Count > 0)
                {
                    await _corsairService.ApplyLightingToAllAsync(ThrottlingColorHex);
                }
                
                // Apply to Logitech devices
                if (_logitechService != null && LogitechDevices.Count > 0)
                {
                    foreach (var device in LogitechDevices)
                    {
                        await _logitechService.ApplyStaticColorAsync(device, ThrottlingColorHex, LogitechBrightness);
                    }
                }
                
                // Apply to Razer devices
                if (_razerService?.IsAvailable == true && _razerDevices.Count > 0)
                {
                    var razerColor = System.Drawing.ColorTranslator.FromHtml(ThrottlingColorHex);
                    await Task.Run(() => _razerService.SetStaticColor(razerColor.R, razerColor.G, razerColor.B));
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to apply throttling indicator lighting", ex);
            }
        }
        
        private async void ApplyPerformanceModeLighting(string modeName)
        {
            try
            {
                string colorHex;
                
                // Map performance mode to color
                switch (modeName.ToLower())
                {
                    case "balanced":
                        colorHex = BalancedModeColorHex;
                        break;
                    case "performance":
                    case "high performance":
                        colorHex = PerformanceModeColorHex;
                        break;
                    case "quiet":
                    case "power saver":
                        colorHex = QuietModeColorHex;
                        break;
                    default:
                        colorHex = CustomModeColorHex;
                        break;
                }
                
                var color = ParseDrawingColor(colorHex);
                
                // Apply to keyboard lighting
                if (_keyboardLightingService?.IsAvailable == true)
                {
                    _keyboardLightingService.SetAllZoneColors(new[] { color, color, color, color });
                }
                
                // Apply to system RGB
                if (_rgbManager != null)
                {
                    await _rgbManager.ApplyEffectToAllAsync($"color:{colorHex}");
                }
                
                // Apply to Corsair devices
                if (_corsairService != null && CorsairDevices.Count > 0)
                {
                    await _corsairService.ApplyLightingToAllAsync(colorHex);
                }
                
                // Apply to Logitech devices
                if (_logitechService != null && LogitechDevices.Count > 0)
                {
                    foreach (var device in LogitechDevices)
                    {
                        await _logitechService.ApplyStaticColorAsync(device, colorHex, LogitechBrightness);
                    }
                }
                
                // Apply to Razer devices
                if (_razerService?.IsAvailable == true && _razerDevices.Count > 0)
                {
                    var razerColor = System.Drawing.ColorTranslator.FromHtml(colorHex);
                    await Task.Run(() => _razerService.SetStaticColor(razerColor.R, razerColor.G, razerColor.B));
                }
                
                _logging.Info($"Applied performance mode lighting for '{modeName}' with color {colorHex}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply performance mode lighting for '{modeName}'", ex);
            }
        }
        
        #endregion
        
        #endregion
        
        #region Scene Methods
        
        /// <summary>
        /// Initialize scenes collection from service.
        /// </summary>
        private void InitializeScenesFromService()
        {
            if (_sceneService == null) return;
            
            Scenes.Clear();
            foreach (var scene in _sceneService.Scenes)
            {
                Scenes.Add(scene);
            }
            
            // Select current scene if any
            _selectedScene = _sceneService.CurrentScene;
            OnPropertyChanged(nameof(SelectedScene));
            OnPropertyChanged(nameof(CurrentSceneName));
            
            // Subscribe to scene changes
            _sceneService.SceneChanged += OnSceneServiceSceneChanged;
            _sceneService.ScenesListChanged += OnSceneServiceListChanged;
            
            // Subscribe to ambient color changes
            if (_screenSamplingService != null)
            {
                _screenSamplingService.ColorChanged += OnAmbientColorChanged;
            }
        }
        
        private void OnSceneServiceSceneChanged(object? sender, RgbSceneChangedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _selectedScene = e.CurrentScene;
                OnPropertyChanged(nameof(SelectedScene));
                OnPropertyChanged(nameof(CurrentSceneName));
            });
        }
        
        private void OnSceneServiceListChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Scenes.Clear();
                if (_sceneService != null)
                {
                    foreach (var scene in _sceneService.Scenes)
                    {
                        Scenes.Add(scene);
                    }
                }
            });
        }
        
        private void OnAmbientColorChanged(object? sender, System.Drawing.Color color)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                AmbientColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                OnPropertyChanged(nameof(AmbientColorHex));
            });
        }
        
        /// <summary>
        /// Apply a scene from a command parameter.
        /// </summary>
        private async Task ApplySceneFromParameterAsync(object? parameter)
        {
            if (_sceneService == null)
            {
                _logging.Warn("Cannot apply scene: Scene service is null");
                return;
            }
            
            var scene = parameter as RgbScene;
            if (scene == null)
            {
                _logging.Warn("Cannot apply scene: Invalid scene parameter");
                return;
            }
            
            try
            {
                _selectedScene = scene;
                OnPropertyChanged(nameof(SelectedScene));
                
                var result = await _sceneService.ApplySceneAsync(scene);
                if (result.Success)
                {
                    OnPropertyChanged(nameof(CurrentSceneName));
                    _logging.Info($"Applied scene '{scene.Name}' successfully");
                }
                else
                {
                    _logging.Warn($"Scene '{scene.Name}' applied with errors: {string.Join(", ", result.Errors)}");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply scene '{scene.Name}'", ex);
            }
        }
        
        /// <summary>
        /// Apply the currently selected scene.
        /// </summary>
        private async Task ApplySelectedSceneAsync()
        {
            if (_sceneService == null || _selectedScene == null)
            {
                _logging.Warn("Cannot apply scene: Scene service or scene is null");
                return;
            }
            
            try
            {
                var result = await _sceneService.ApplySceneAsync(_selectedScene);
                if (result.Success)
                {
                    OnPropertyChanged(nameof(CurrentSceneName));
                    _logging.Info($"Applied scene '{_selectedScene.Name}' successfully");
                }
                else
                {
                    _logging.Warn($"Scene '{_selectedScene.Name}' applied with errors: {string.Join(", ", result.Errors)}");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply scene '{_selectedScene.Name}'", ex);
            }
        }
        
        /// <summary>
        /// Save current lighting settings as a new scene.
        /// </summary>
        private async Task SaveCurrentAsSceneAsync()
        {
            if (_sceneService == null)
            {
                _logging.Warn("Cannot save scene: Scene service is not available");
                return;
            }
            
            await Task.Run(() =>
            {
                try
                {
                    var scene = _sceneService.CreateSceneFromCurrent($"Custom Scene {Scenes.Count + 1}");
                    
                    // Copy current zone colors
                    scene.ZoneColors = new System.Collections.Generic.Dictionary<int, string>
                    {
                        { 0, Zone1ColorHex },
                        { 1, Zone2ColorHex },
                        { 2, Zone3ColorHex },
                        { 3, Zone4ColorHex }
                    };
                    scene.PrimaryColor = Zone1ColorHex;
                    
                    _sceneService.AddScene(scene);
                    
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Scenes.Add(scene);
                        SelectedScene = scene;
                    });
                    
                    _logging.Info($"Created new scene: {scene.Name}");
                }
                catch (Exception ex)
                {
                    _logging.Error("Failed to save current settings as scene", ex);
                }
            });
        }
        
        /// <summary>
        /// Toggle ambient/screen sampling mode.
        /// </summary>
        private void ToggleAmbientMode(bool enabled)
        {
            if (_screenSamplingService == null)
            {
                _logging.Warn("Screen sampling service not available");
                return;
            }
            
            if (enabled)
            {
                _screenSamplingService.Start();
                _logging.Info("Ambient mode enabled");
            }
            else
            {
                _screenSamplingService.Stop();
                _logging.Info("Ambient mode disabled");
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents a keyboard zone color preset.
    /// </summary>
    public class KeyboardPreset
    {
        public string Name { get; set; } = "";
        public string Zone1 { get; set; } = "#E6002E";
        public string Zone2 { get; set; } = "#E6002E";
        public string Zone3 { get; set; } = "#E6002E";
        public string Zone4 { get; set; } = "#E6002E";
    }
}
