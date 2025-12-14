using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
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
        private double _currentTemperature;

        public ObservableCollection<FanPreset> FanPresets { get; } = new();
        public ObservableCollection<FanCurvePoint> CustomFanCurve { get; } = new();
        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples => _fanService.ThermalSamples;
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry => _fanService.FanTelemetry;
        
        /// <summary>
        /// Current max temperature (CPU/GPU) for display on the fan curve editor.
        /// </summary>
        public double CurrentTemperature
        {
            get => _currentTemperature;
            set
            {
                if (Math.Abs(_currentTemperature - value) > 0.1)
                {
                    _currentTemperature = value;
                    OnPropertyChanged();
                }
            }
        }

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
        public ICommand ImportPresetsCommand { get; }
        public ICommand ExportPresetsCommand { get; }
        
        // Curve editor commands
        public ICommand AddCurvePointCommand { get; }
        public ICommand RemoveCurvePointCommand { get; }
        public ICommand ResetCurveCommand { get; }
        
        // Quick preset commands
        public ICommand ApplyMaxCoolingCommand { get; }
        public ICommand ApplyAutoModeCommand { get; }
        public ICommand ApplyQuietModeCommand { get; }
        public ICommand ApplyGamingModeCommand { get; }

        public FanControlViewModel(FanService fanService, ConfigurationService configService, LoggingService logging)
        {
            _fanService = fanService;
            _configService = configService;
            _logging = logging;
            
            ApplyCustomCurveCommand = new RelayCommand(_ => ApplyCustomCurve());
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset());
            ImportPresetsCommand = new RelayCommand(_ => ImportPresets());
            ExportPresetsCommand = new RelayCommand(_ => ExportPresets());
            
            // Curve editor commands
            AddCurvePointCommand = new RelayCommand(_ => AddDefaultCurvePoint(), _ => CustomFanCurve.Count < 10);
            RemoveCurvePointCommand = new RelayCommand(_ => RemoveLastCurvePoint(), _ => CustomFanCurve.Count > 2);
            ResetCurveCommand = new RelayCommand(_ => ResetCurveToDefault());
            
            // Quick preset buttons
            ApplyMaxCoolingCommand = new RelayCommand(_ => ApplyFanMode("Max"));
            ApplyAutoModeCommand = new RelayCommand(_ => ApplyFanMode("Auto"));
            ApplyQuietModeCommand = new RelayCommand(_ => ApplyQuietMode());
            ApplyGamingModeCommand = new RelayCommand(_ => ApplyGamingMode());
            
            // Subscribe to thermal samples to update current temperature
            ((INotifyCollectionChanged)_fanService.ThermalSamples).CollectionChanged += ThermalSamples_CollectionChanged;
            
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
        
        private void ThermalSamples_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_fanService.ThermalSamples.Count > 0)
            {
                var latest = _fanService.ThermalSamples[^1];
                CurrentTemperature = Math.Max(latest.CpuCelsius, latest.GpuCelsius);
            }
        }
        
        /// <summary>
        /// Add a new curve point at a reasonable default position.
        /// </summary>
        private void AddDefaultCurvePoint()
        {
            if (CustomFanCurve.Count >= 10) return;
            
            // Find a gap in the temperature range to add a new point
            var sorted = CustomFanCurve.OrderBy(p => p.TemperatureC).ToList();
            
            int newTemp = 60; // Default
            int newFan = 50;
            
            if (sorted.Count > 0)
            {
                // Find the largest gap between points
                int maxGap = 0;
                int gapStart = 30;
                
                // Check gap before first point
                if (sorted[0].TemperatureC > 35)
                {
                    maxGap = sorted[0].TemperatureC - 30;
                    gapStart = 30;
                }
                
                // Check gaps between points
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    int gap = sorted[i + 1].TemperatureC - sorted[i].TemperatureC;
                    if (gap > maxGap)
                    {
                        maxGap = gap;
                        gapStart = sorted[i].TemperatureC;
                    }
                }
                
                // Check gap after last point
                if (100 - sorted[^1].TemperatureC > maxGap)
                {
                    maxGap = 100 - sorted[^1].TemperatureC;
                    gapStart = sorted[^1].TemperatureC;
                }
                
                // Place new point in the middle of the largest gap
                newTemp = gapStart + maxGap / 2;
                newTemp = (int)Math.Round(newTemp / 5.0) * 5; // Snap to 5
                newTemp = Math.Clamp(newTemp, 35, 95);
                
                // Interpolate fan speed
                var before = sorted.LastOrDefault(p => p.TemperatureC < newTemp);
                var after = sorted.FirstOrDefault(p => p.TemperatureC > newTemp);
                
                if (before != null && after != null)
                {
                    double t = (newTemp - before.TemperatureC) / (double)(after.TemperatureC - before.TemperatureC);
                    newFan = (int)(before.FanPercent + t * (after.FanPercent - before.FanPercent));
                }
                else if (before != null)
                {
                    newFan = Math.Min(100, before.FanPercent + 10);
                }
                else if (after != null)
                {
                    newFan = Math.Max(0, after.FanPercent - 10);
                }
            }
            
            CustomFanCurve.Add(new FanCurvePoint { TemperatureC = newTemp, FanPercent = newFan });
            
            // Re-sort
            var newSorted = CustomFanCurve.OrderBy(p => p.TemperatureC).ToList();
            CustomFanCurve.Clear();
            foreach (var p in newSorted)
            {
                CustomFanCurve.Add(p);
            }
            
            _logging.Info($"Added curve point: {newTemp}°C → {newFan}%");
        }
        
        /// <summary>
        /// Remove the last curve point (keeping minimum 2).
        /// </summary>
        private void RemoveLastCurvePoint()
        {
            if (CustomFanCurve.Count <= 2) return;
            
            var removed = CustomFanCurve[^1];
            CustomFanCurve.RemoveAt(CustomFanCurve.Count - 1);
            
            _logging.Info($"Removed curve point: {removed.TemperatureC}°C → {removed.FanPercent}%");
        }
        
        /// <summary>
        /// Reset curve to default auto curve.
        /// </summary>
        private void ResetCurveToDefault()
        {
            CustomFanCurve.Clear();
            foreach (var point in GetDefaultAutoCurve())
            {
                CustomFanCurve.Add(point);
            }
            _logging.Info("Reset fan curve to default");
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
            // FanService logs success/failure, no need to duplicate
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
            // FanService logs success/failure, no need to duplicate
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
        
        private static List<FanCurvePoint> GetQuietCurve()
        {
            // Silent mode: delayed fan ramp, allows higher temps for quiet operation
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 50, FanPercent = 25 },
                new() { TemperatureC = 65, FanPercent = 35 },
                new() { TemperatureC = 75, FanPercent = 50 },
                new() { TemperatureC = 85, FanPercent = 70 },
                new() { TemperatureC = 95, FanPercent = 100 }
            };
        }
        
        private static List<FanCurvePoint> GetGamingCurve()
        {
            // Gaming mode: aggressive fan ramp starting at 60°C for sustained loads
            // Recommended for gaming sessions where cooling is priority over noise
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 45, FanPercent = 30 },
                new() { TemperatureC = 55, FanPercent = 45 },
                new() { TemperatureC = 65, FanPercent = 65 },
                new() { TemperatureC = 75, FanPercent = 85 },
                new() { TemperatureC = 80, FanPercent = 100 }
            };
        }
        
        private void ApplyGamingMode()
        {
            var gamingPreset = new FanPreset
            {
                Name = "Gaming",
                Mode = FanMode.Performance, // Use Performance thermal policy for aggressive cooling
                Curve = GetGamingCurve(),
                IsBuiltIn = false
            };
            
            // Add if not exists, select it
            var existing = FanPresets.FirstOrDefault(p => p.Name == "Gaming");
            if (existing == null)
            {
                FanPresets.Add(gamingPreset);
                SelectedPreset = gamingPreset;
            }
            else
            {
                SelectedPreset = existing;
            }
            
            _logging.Info("Applied Gaming fan mode (Performance thermal policy with aggressive curve)");
        }
        
        private void ApplyQuietMode()
        {
            var quietPreset = new FanPreset
            {
                Name = "Quiet",
                Mode = FanMode.Manual,
                Curve = GetQuietCurve(),
                IsBuiltIn = false
            };
            
            // Add if not exists, select it
            var existing = FanPresets.FirstOrDefault(p => p.Name == "Quiet");
            if (existing == null)
            {
                FanPresets.Add(quietPreset);
                SelectedPreset = quietPreset;
            }
            else
            {
                SelectedPreset = existing;
            }
            
            _logging.Info("Applied Quiet fan mode");
        }

        /// <summary>
        /// Apply a fan mode by name (for hotkey integration)
        /// </summary>
        public void ApplyFanMode(string modeName)
        {
            var preset = FanPresets.FirstOrDefault(p => 
                p.Name.Equals(modeName, System.StringComparison.OrdinalIgnoreCase));
            
            if (preset != null)
            {
                SelectedPreset = preset;
            }
            else
            {
                // Try to match partial names
                preset = modeName.ToLower() switch
                {
                    "performance" or "boost" or "max" => FanPresets.FirstOrDefault(p => p.Name == "Max"),
                    "quiet" or "silent" => FanPresets.FirstOrDefault(p => p.Mode == FanMode.Manual) ?? FanPresets.FirstOrDefault(p => p.Name == "Auto"),
                    "balanced" or "auto" => FanPresets.FirstOrDefault(p => p.Name == "Auto"),
                    _ => FanPresets.FirstOrDefault(p => p.Name == "Auto")
                };
                
                if (preset != null)
                    SelectedPreset = preset;
            }
        }

        /// <summary>
        /// Import fan presets from a JSON file
        /// </summary>
        private void ImportPresets()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "Import Fan Presets"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var imported = JsonSerializer.Deserialize<FanPresetExport>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (imported?.Presets == null || imported.Presets.Count == 0)
                    {
                        System.Windows.MessageBox.Show("No valid fan presets found in file.", "Import Failed",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }

                    int count = 0;
                    foreach (var preset in imported.Presets)
                    {
                        // Skip built-in preset names
                        if (preset.Name == "Max" || preset.Name == "Auto") continue;
                        
                        preset.IsBuiltIn = false;
                        
                        // Remove existing preset with same name
                        var existing = FanPresets.FirstOrDefault(p => p.Name == preset.Name && !p.IsBuiltIn);
                        if (existing != null)
                        {
                            FanPresets.Remove(existing);
                        }

                        FanPresets.Add(preset);
                        count++;
                    }

                    SavePresetsToConfig();
                    
                    _logging.Info($"📥 Imported {count} fan preset(s) from {dialog.FileName}");
                    System.Windows.MessageBox.Show($"Imported {count} fan preset(s)", "Import Complete",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to import fan presets: {ex.Message}", ex);
                System.Windows.MessageBox.Show($"Failed to import presets: {ex.Message}", "Import Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Export fan presets to a JSON file
        /// </summary>
        private void ExportPresets()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "Export Fan Presets",
                    FileName = $"omencore-fan-presets-{DateTime.Now:yyyy-MM-dd}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var export = new FanPresetExport
                    {
                        ExportDate = DateTime.Now,
                        Version = "1.1",
                        Presets = FanPresets.Where(p => !p.IsBuiltIn).ToList()
                    };

                    var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(dialog.FileName, json);
                    
                    _logging.Info($"📤 Exported {export.Presets.Count} fan preset(s) to {dialog.FileName}");
                    System.Windows.MessageBox.Show($"Exported {export.Presets.Count} fan preset(s)", "Export Complete",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to export fan presets: {ex.Message}", ex);
                System.Windows.MessageBox.Show($"Failed to export presets: {ex.Message}", "Export Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Container for fan preset import/export
    /// </summary>
    public class FanPresetExport
    {
        public DateTime ExportDate { get; set; }
        public string Version { get; set; } = "1.1";
        public List<FanPreset> Presets { get; set; } = new();
    }
}
