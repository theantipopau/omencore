using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Corsair;
using OmenCore.Logitech;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class LightingViewModel : ViewModelBase
    {
        private readonly CorsairDeviceService _corsairService;
        private readonly LogitechDeviceService _logitechService;
        private readonly LoggingService _logging;
        
        private CorsairDevice? _selectedCorsairDevice;
        private CorsairLightingPreset? _selectedCorsairPreset;
        private LogitechDevice? _selectedLogitechDevice;
        private string _logitechColorHex = "#E6002E";
        private int _logitechBrightness = 80;
        private string _corsairColorHex = "#FF0000";

        public ReadOnlyObservableCollection<CorsairDevice> CorsairDevices => _corsairService.Devices;
        public ReadOnlyObservableCollection<LogitechDevice> LogitechDevices => _logitechService.Devices;
        public ObservableCollection<CorsairLightingPreset> CorsairLightingPresets { get; } = new();

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
                    (ApplyCorsairLightingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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

        public string CorsairDeviceStatusText => $"{CorsairDevices.Count} device(s) detected";
        public string LogitechDeviceStatusText => $"{LogitechDevices.Count} device(s) detected";
        public bool HasCorsairMouse => CorsairDevices.Any(d => d.DeviceType == CorsairDeviceType.Mouse);
        public ObservableCollection<CorsairDpiStage> CorsairDpiStages { get; } = new();

        public ICommand DiscoverCorsairCommand { get; }
        public ICommand ApplyCorsairLightingCommand { get; }
        public ICommand ApplyCorsairCustomColorCommand { get; }
        public ICommand ApplyCorsairDpiCommand { get; }
        public ICommand DiscoverLogitechCommand { get; }
        public ICommand ApplyLogitechColorCommand { get; }
        public ICommand DiscoverCorsairDevicesCommand { get; }
        public ICommand DiscoverLogitechDevicesCommand { get; }

        public LightingViewModel(CorsairDeviceService corsairService, LogitechDeviceService logitechService, LoggingService logging)
        {
            _corsairService = corsairService;
            _logitechService = logitechService;
            _logging = logging;

            DiscoverCorsairCommand = new AsyncRelayCommand(async _ => await _corsairService.DiscoverAsync());
            DiscoverCorsairDevicesCommand = new AsyncRelayCommand(async _ => await _corsairService.DiscoverAsync());
            ApplyCorsairLightingCommand = new AsyncRelayCommand(async _ => await ApplyCorsairLightingAsync(), _ => SelectedCorsairPreset != null);
            ApplyCorsairCustomColorCommand = new AsyncRelayCommand(async _ => await ApplyCorsairCustomColorAsync());
            ApplyCorsairDpiCommand = new AsyncRelayCommand(async _ => await ApplyCorsairDpiAsync());
            
            DiscoverLogitechCommand = new AsyncRelayCommand(async _ => await _logitechService.DiscoverAsync());
            DiscoverLogitechDevicesCommand = new AsyncRelayCommand(async _ => await _logitechService.DiscoverAsync());
            ApplyLogitechColorCommand = new AsyncRelayCommand(async _ => await ApplyLogitechColorAsync(), _ => SelectedLogitechDevice != null);

            // Initialize lighting presets
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Red", ColorHex = "#FF0000" });
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Green", ColorHex = "#00FF00" });
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Blue", ColorHex = "#0000FF" });
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Purple", ColorHex = "#9B30FF" });
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "Cyan", ColorHex = "#00FFFF" });
            CorsairLightingPresets.Add(new CorsairLightingPreset { Name = "White", ColorHex = "#FFFFFF" });
            SelectedCorsairPreset = CorsairLightingPresets.FirstOrDefault();
            
            // Initialize default DPI stages for mouse configuration
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 1", Dpi = 800, IsDefault = true });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 2", Dpi = 1600 });
            CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 3", Dpi = 3200 });
        }

        private async Task ApplyCorsairLightingAsync()
        {
            if (SelectedCorsairPreset != null)
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    await _corsairService.ApplyLightingToAllAsync(SelectedCorsairPreset.ColorHex);
                    _logging.Info($"Applied Corsair lighting preset: {SelectedCorsairPreset.Name}");
                }, "Applying Corsair lighting...");
            }
        }

        private async Task ApplyCorsairCustomColorAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await _corsairService.ApplyLightingToAllAsync(CorsairColorHex);
                _logging.Info($"Applied custom Corsair color: {CorsairColorHex}");
            }, "Applying custom color...");
        }

        private async Task ApplyCorsairDpiAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                foreach (var stage in CorsairDpiStages)
                {
                    _logging.Info($"DPI Stage {stage.Name}: {stage.Dpi} DPI");
                }
                // Note: RGB.NET doesn't support DPI control - would need CUE SDK integration
                _logging.Warn("DPI control not yet implemented - RGB.NET doesn't support this feature");
                await Task.CompletedTask;
            }, "Configuring DPI...");
        }

        private async Task ApplyLogitechColorAsync()
        {
            if (SelectedLogitechDevice != null)
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    await _logitechService.ApplyStaticColorAsync(SelectedLogitechDevice, LogitechColorHex, LogitechBrightness);
                }, "Applying Logitech lighting...");
            }
        }
    }
}
