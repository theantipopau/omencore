using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class FanControlViewModel : ViewModelBase
    {
        private readonly FanService _fanService;
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private FanPreset? _selectedPreset;
        private string _customPresetName = "Custom";

        public ObservableCollection<FanPreset> FanPresets { get; } = new();
        public ObservableCollection<FanCurvePoint> CustomFanCurve { get; } = new();
        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples => _fanService.ThermalSamples;
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry => _fanService.FanTelemetry;

        public FanPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentFanModeName));
                    if (value != null)
                    {
                        LoadCurve(value);
                        ApplyPreset(value);
                    }
                }
            }
        }
        
        public string CurrentFanModeName => SelectedPreset?.Name ?? "Auto";

        public string CustomPresetName
        {
            get => _customPresetName;
            set
            {
                if (_customPresetName != value)
                {
                    _customPresetName = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ApplyCustomCurveCommand { get; }
        public ICommand SaveCustomPresetCommand { get; }

        public FanControlViewModel(FanService fanService, ConfigurationService configService, LoggingService logging)
        {
            _fanService = fanService;
            _configService = configService;
            _logging = logging;
            
            ApplyCustomCurveCommand = new RelayCommand(_ => ApplyCustomCurve());
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset());
            
            // Initialize built-in presets
            FanPresets.Add(new FanPreset 
            { 
                Name = "Max", 
                Mode = FanMode.Max,
                IsBuiltIn = true,
                Curve = new() { new FanCurvePoint { TemperatureC = 0, FanPercent = 100 } }
            });
            FanPresets.Add(new FanPreset 
            { 
                Name = "Auto", 
                Mode = FanMode.Auto,
                IsBuiltIn = true,
                Curve = GetDefaultAutoCurve()
            });
            FanPresets.Add(new FanPreset 
            { 
                Name = "Manual", 
                Mode = FanMode.Manual,
                IsBuiltIn = false,
                Curve = GetDefaultManualCurve()
            });
            
            // Load custom presets from config file
            LoadPresetsFromConfig();
            
            SelectedPreset = FanPresets[1]; // Default to Auto
        }

        private void LoadCurve(FanPreset preset)
        {
            CustomFanCurve.Clear();
            
            if (preset.Mode == FanMode.Manual && preset.Curve != null && preset.Curve.Count > 0)
            {
                foreach (var point in preset.Curve)
                {
                    CustomFanCurve.Add(new FanCurvePoint 
                    { 
                        TemperatureC = point.TemperatureC, 
                        FanPercent = point.FanPercent 
                    });
                }
            }
            else if (preset.Mode == FanMode.Max)
            {
                CustomFanCurve.Add(new FanCurvePoint { TemperatureC = 0, FanPercent = 100 });
            }
            else // Auto mode
            {
                foreach (var point in GetDefaultAutoCurve())
                {
                    CustomFanCurve.Add(point);
                }
            }
        }

        private void ApplyPreset(FanPreset preset)
        {
            _fanService.ApplyPreset(preset);
            _logging.Info($"Applied fan preset: {preset.Name}");
        }

        private void ApplyCustomCurve()
        {
            if (CustomFanCurve.Count == 0)
            {
                _logging.Warn("Cannot apply empty fan curve");
                return;
            }

            var customPreset = new FanPreset
            {
                Name = "Custom (Applied)",
                Mode = FanMode.Manual,
                Curve = CustomFanCurve.ToList()
            };

            _fanService.ApplyPreset(customPreset);
            _logging.Info("Applied custom fan curve");
        }

        private void SaveCustomPreset()
        {
            if (CustomFanCurve.Count == 0)
            {
                _logging.Warn("Cannot save empty fan curve");
                return;
            }

            if (string.IsNullOrWhiteSpace(CustomPresetName))
            {
                _logging.Warn("Cannot save preset with empty name");
                return;
            }

            var preset = new FanPreset
            {
                Name = CustomPresetName,
                Mode = FanMode.Manual,
                Curve = CustomFanCurve.ToList(),
                IsBuiltIn = false
            };

            // Remove existing preset with same name
            var existing = FanPresets.FirstOrDefault(p => p.Name == preset.Name && !p.IsBuiltIn);
            if (existing != null)
            {
                FanPresets.Remove(existing);
            }

            FanPresets.Add(preset);
            
            // Persist to config file
            SavePresetsToConfig();
            
            _logging.Info($"✓ Saved custom fan preset: '{preset.Name}' with {preset.Curve.Count} points");
        }
        
        private void SavePresetsToConfig()
        {
            try
            {
                var config = _configService.Load();
                
                // Update only the custom (non-built-in) presets
                config.FanPresets = FanPresets
                    .Where(p => !p.IsBuiltIn)
                    .ToList();
                
                _configService.Save(config);
                _logging.Info($"💾 Fan presets saved to config ({config.FanPresets.Count} custom presets)");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to save fan presets to config", ex);
            }
        }
        
        private void LoadPresetsFromConfig()
        {
            try
            {
                var config = _configService.Load();
                
                // Load custom presets from config (built-in presets are added in constructor)
                foreach (var preset in config.FanPresets)
                {
                    if (!FanPresets.Any(p => p.Name == preset.Name))
                    {
                        FanPresets.Add(preset);
                    }
                }
                
                _logging.Info($"📂 Loaded {config.FanPresets.Count} custom fan presets from config");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to load fan presets from config", ex);
            }
        }

        private static List<FanCurvePoint> GetDefaultAutoCurve()
        {
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 30 },
                new() { TemperatureC = 50, FanPercent = 40 },
                new() { TemperatureC = 60, FanPercent = 55 },
                new() { TemperatureC = 70, FanPercent = 70 },
                new() { TemperatureC = 80, FanPercent = 85 },
                new() { TemperatureC = 90, FanPercent = 100 }
            };
        }

        private static List<FanCurvePoint> GetDefaultManualCurve()
        {
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 35 },
                new() { TemperatureC = 60, FanPercent = 50 },
                new() { TemperatureC = 80, FanPercent = 80 }
            };
        }
    }
}
