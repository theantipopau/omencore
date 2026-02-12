using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        private bool _suppressApplyOnSelection;

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
                    OnPropertyChanged(nameof(PredictedFanPercent));
                    OnPropertyChanged(nameof(CurvePreviewText));
                }
            }
        }
        
        #region Curve Preview (v2.7.0)
        
        /// <summary>
        /// Predicted fan percentage based on current temperature and active curve.
        /// Shows what fan speed the curve would produce at the current temperature.
        /// </summary>
        public int PredictedFanPercent
        {
            get
            {
                if (CustomFanCurve == null || CustomFanCurve.Count == 0)
                    return 0;
                
                var sorted = CustomFanCurve.OrderBy(p => p.TemperatureC).ToList();
                var temp = CurrentTemperature;
                
                // Find the curve point for current temperature
                // If temp is below all points, use lowest
                if (temp <= sorted.First().TemperatureC)
                    return sorted.First().FanPercent;
                
                // If temp is above all points, use highest  
                if (temp >= sorted.Last().TemperatureC)
                    return sorted.Last().FanPercent;
                
                // Interpolate between points
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    if (temp >= sorted[i].TemperatureC && temp <= sorted[i + 1].TemperatureC)
                    {
                        var t1 = sorted[i].TemperatureC;
                        var t2 = sorted[i + 1].TemperatureC;
                        var f1 = sorted[i].FanPercent;
                        var f2 = sorted[i + 1].FanPercent;
                        
                        // Linear interpolation
                        var ratio = (temp - t1) / (t2 - t1);
                        return (int)(f1 + (f2 - f1) * ratio);
                    }
                }
                
                return sorted.Last().FanPercent;
            }
        }
        
        /// <summary>
        /// Human-readable curve preview text showing predicted fan speed.
        /// </summary>
        /// <summary>
        /// Effective fan percentage after safety clamping is applied.
        /// Shows what fan speed will actually be commanded.
        /// </summary>
        public int EffectiveFanPercent
        {
            get
            {
                var raw = PredictedFanPercent;
                var temp = CurrentTemperature;
                
                // Mirror FanService.ApplySafetyBoundsClamping thresholds
                if (temp >= 95) return 100;
                if (temp >= 90) return Math.Max(raw, 80);
                if (temp >= 85) return Math.Max(raw, 60);
                if (temp >= 80) return Math.Max(raw, 40);
                return raw;
            }
        }
        
        public string CurvePreviewText
        {
            get
            {
                var raw = PredictedFanPercent;
                var effective = EffectiveFanPercent;
                if (effective != raw)
                    return $"At {CurrentTemperature:F0}°C → {raw}% (safety: {effective}%)";
                return $"At {CurrentTemperature:F0}°C → {raw}%";
            }
        }
        
        /// <summary>
        /// Validates the curve and returns any warnings.
        /// </summary>
        public string CurveValidationMessage
        {
            get
            {
                if (CustomFanCurve == null || CustomFanCurve.Count < 2)
                    return "⚠️ Curve needs at least 2 points";
                
                var sorted = CustomFanCurve.OrderBy(p => p.TemperatureC).ToList();
                
                // Check for decreasing fan speed at higher temps (dangerous)
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    if (sorted[i + 1].FanPercent < sorted[i].FanPercent && 
                        sorted[i + 1].TemperatureC > 60) // Only warn above 60°C
                    {
                        return $"⚠️ Fan drops to {sorted[i + 1].FanPercent}% at {sorted[i + 1].TemperatureC}°C";
                    }
                }
                
                // Check if max temp point is too low
                var maxTempPoint = sorted.Last();
                if (maxTempPoint.TemperatureC < 80)
                {
                    return $"ℹ️ Consider adding a point at 85-95°C";
                }
                
                // Check if fan never reaches 100%
                if (sorted.All(p => p.FanPercent < 90))
                {
                    return "ℹ️ Curve never reaches 100% - may cause thermal throttling";
                }
                
                // Warn if safety clamping will override at current temp
                var currentRaw = PredictedFanPercent;
                var currentEffective = EffectiveFanPercent;
                if (currentEffective > currentRaw)
                {
                    return $"ℹ️ Safety floor active: {currentRaw}% → {currentEffective}% at {CurrentTemperature:F0}°C";
                }
                
                return "✓ Curve looks good";
            }
        }
        
        private void NotifyCurvePreviewChanged()
        {
            OnPropertyChanged(nameof(PredictedFanPercent));
            OnPropertyChanged(nameof(EffectiveFanPercent));
            OnPropertyChanged(nameof(CurvePreviewText));
            OnPropertyChanged(nameof(CurveValidationMessage));
        }
        
        #endregion

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
                        if (!_suppressApplyOnSelection)
                        {
                            ApplyPreset(value);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Selects a preset by name without applying it (for external sync from GeneralViewModel).
        /// Use this when the fan mode has already been applied externally.
        /// </summary>
        public void SelectPresetByNameNoApply(string presetName)
        {
            var preset = FanPresets.FirstOrDefault(p => 
                p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            
            if (preset != null)
            {
                _suppressApplyOnSelection = true;
                SelectedPreset = preset;
                _suppressApplyOnSelection = false;
            }
        }
        
        public string CurrentFanModeName => SelectedPreset?.Name ?? "Auto";
        
        // Unified preset selection state for RadioButton cards
        private string _activeFanMode = "Auto";
        
        public string ActiveFanMode
        {
            get => _activeFanMode;
            set
            {
                if (_activeFanMode != value)
                {
                    _activeFanMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMaxSelected));
                    OnPropertyChanged(nameof(IsExtremeSelected));
                    OnPropertyChanged(nameof(IsGamingSelected));
                    OnPropertyChanged(nameof(IsAutoSelected));
                    OnPropertyChanged(nameof(IsSilentSelected));
                    OnPropertyChanged(nameof(IsCustomSelected));
                    OnPropertyChanged(nameof(IsConstantSelected));
                }
            }
        }
        
        public bool IsMaxSelected
        {
            get => _activeFanMode == "Max";
            set { if (value) ActiveFanMode = "Max"; }
        }
        
        public bool IsExtremeSelected
        {
            get => _activeFanMode == "Extreme";
            set { if (value) ActiveFanMode = "Extreme"; }
        }
        
        public bool IsGamingSelected
        {
            get => _activeFanMode == "Gaming";
            set { if (value) ActiveFanMode = "Gaming"; }
        }
        
        public bool IsAutoSelected
        {
            get => _activeFanMode == "Auto";
            set { if (value) ActiveFanMode = "Auto"; }
        }
        
        public bool IsSilentSelected
        {
            get => _activeFanMode == "Silent";
            set { if (value) ActiveFanMode = "Silent"; }
        }
        
        public bool IsCustomSelected
        {
            get => _activeFanMode == "Custom";
            set { if (value) ActiveFanMode = "Custom"; }
        }
        
        public bool IsConstantSelected
        {
            get => _activeFanMode == "Constant";
            set { if (value) ActiveFanMode = "Constant"; }
        }
        
        // Constant speed mode properties (OmenMon-style fixed percentage)
        private int _constantFanPercent = 50;
        public int ConstantFanPercent
        {
            get => _constantFanPercent;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (_constantFanPercent != clamped)
                {
                    _constantFanPercent = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConstantFanRpmEstimate));
                    
                    // If in constant mode, apply immediately
                    if (IsConstantSelected)
                    {
                        ApplyConstantSpeed();
                    }
                }
            }
        }
        
        /// <summary>
        /// Estimated RPM based on percentage (assuming max ~5500 RPM)
        /// </summary>
        public int ConstantFanRpmEstimate => (int)(_constantFanPercent / 100.0 * 5500);
        
        /// <summary>
        /// Whether a custom fan curve is currently being actively applied by FanService.
        /// </summary>
        public bool IsCurveActive => _fanService.IsCurveActive;

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
        public ICommand DeleteSelectedPresetCommand { get; }
        
        /// <summary>
        /// Filtered view of FanPresets showing only user-saved (non-built-in) presets.
        /// </summary>
        public IEnumerable<FanPreset> SavedCustomPresets => FanPresets.Where(p => !p.IsBuiltIn);
        
        /// <summary>
        /// Whether there are any saved custom presets to show.
        /// </summary>
        public bool HasSavedPresets => FanPresets.Any(p => !p.IsBuiltIn);
        
        // Curve editor commands
        public ICommand AddCurvePointCommand { get; }
        public ICommand RemoveCurvePointCommand { get; }
        public ICommand ResetCurveCommand { get; }
        
        // Quick preset commands
        public ICommand ApplyMaxCoolingCommand { get; }
        public ICommand ApplyExtremeModeCommand { get; }
        public ICommand ApplyAutoModeCommand { get; }
        public ICommand ApplyQuietModeCommand { get; }
        public ICommand ApplyGamingModeCommand { get; }
        public ICommand ApplyConstantSpeedCommand { get; }
        public ICommand ReapplySavedPresetCommand { get; }

        private bool _immediateApplyOnApply;
        public bool ImmediateApplyOnApply
        {
            get => _immediateApplyOnApply;
            set
            {
                if (_immediateApplyOnApply != value)
                {
                    _immediateApplyOnApply = value;
                    OnPropertyChanged();

                    // Persist setting and apply to config
                    var cfg = _configService.Load();
                    cfg.FanTransition.ApplyImmediatelyOnUserAction = _immediateApplyOnApply;
                    _configService.Save(cfg);
                }
            }
        }

        private int _smoothingDurationMs;
        public int SmoothingDurationMs
        {
            get => _smoothingDurationMs;
            set
            {
                if (_smoothingDurationMs != value)
                {
                    _smoothingDurationMs = value;
                    OnPropertyChanged();

                    // Persist and apply to service
                    var cfg = _configService.Load();
                    cfg.FanTransition.SmoothingDurationMs = Math.Max(0, _smoothingDurationMs);
                    _configService.Save(cfg);
                    _fanService.SetSmoothingSettings(cfg.FanTransition);
                }
            }
        }

        private int _smoothingStepMs;
        public int SmoothingStepMs
        {
            get => _smoothingStepMs;
            set
            {
                if (_smoothingStepMs != value)
                {
                    _smoothingStepMs = value;
                    OnPropertyChanged();

                    var cfg = _configService.Load();
                    cfg.FanTransition.SmoothingStepMs = Math.Max(50, _smoothingStepMs);
                    _configService.Save(cfg);
                    _fanService.SetSmoothingSettings(cfg.FanTransition);
                }
            }
        }

        #region Independent CPU/GPU Curves (Disabled by default - XAML bindings require these)
        
        /// <summary>
        /// When true, shows separate CPU and GPU curve editors. When false, shows a single unified curve.
        /// Currently disabled (false) until full implementation is complete.
        /// </summary>
        private bool _independentCurvesEnabled = false;
        public bool IndependentCurvesEnabled
        {
            get => _independentCurvesEnabled;
            set
            {
                if (_independentCurvesEnabled != value)
                {
                    _independentCurvesEnabled = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>GPU fan curve for independent mode (placeholder).</summary>
        public ObservableCollection<FanCurvePoint> GpuFanCurve { get; } = new();
        
        /// <summary>Current CPU temperature for the CPU-specific curve editor.</summary>
        public double CurrentCpuTemperature => CurrentTemperature; // Use unified temp for now
        
        /// <summary>Current GPU temperature for the GPU-specific curve editor.</summary>
        public double CurrentGpuTemperature => CurrentTemperature; // Use unified temp for now
        
        /// <summary>Whether thermal protection is currently active.</summary>
        public bool ThermalProtectionActive => _fanService.IsThermalProtectionActive;
        
        /// <summary>Human-readable thermal protection status.</summary>
        public string ThermalProtectionStatusText => ThermalProtectionActive 
            ? $"⚠️ Active - CPU at {CurrentTemperature:F0}°C" 
            : "✓ Normal";
        
        /// <summary>When the fan telemetry was last updated.</summary>
        public string LastTelemetryUpdatedText => $"Updated: {DateTime.Now:HH:mm:ss}";
        
        /// <summary>Shows which source is being used for RPM readings.</summary>
        public string RpmSourceDisplay => _fanService.Backend;
        
        // GPU curve editor commands (stubs - will use same curve for now)
        public ICommand AddGpuCurvePointCommand { get; }
        public ICommand RemoveGpuCurvePointCommand { get; }
        public ICommand ResetGpuCurveCommand { get; }
        public ICommand CopyFromCpuCurveCommand { get; }
        
        #endregion

        public FanControlViewModel(FanService fanService, ConfigurationService configService, LoggingService logging)
        {
            _fanService = fanService;
            _configService = configService;
            _logging = logging;
            
            // Load hysteresis and transition settings from config
            _fanService.SetHysteresis(_configService.Config.FanHysteresis);
            ImmediateApplyOnApply = _configService.Config.FanTransition.ApplyImmediatelyOnUserAction;
            
            ApplyCustomCurveCommand = new RelayCommand(_ => ApplyCustomCurve());

            // Initialize transition values from config
            SmoothingDurationMs = _configService.Config.FanTransition.SmoothingDurationMs;
            SmoothingStepMs = _configService.Config.FanTransition.SmoothingStepMs;
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset());
            ImportPresetsCommand = new RelayCommand(_ => ImportPresets());
            ExportPresetsCommand = new RelayCommand(_ => ExportPresets());
            DeleteSelectedPresetCommand = new RelayCommand(_ => DeleteSelectedPreset(), _ => SelectedPreset != null && !SelectedPreset.IsBuiltIn);
            
            // Curve editor commands
            AddCurvePointCommand = new RelayCommand(_ => AddDefaultCurvePoint(), _ => CustomFanCurve.Count < 10);
            RemoveCurvePointCommand = new RelayCommand(_ => RemoveLastCurvePoint(), _ => CustomFanCurve.Count > 2);
            ResetCurveCommand = new RelayCommand(_ => ResetCurveToDefault());
            
            // GPU curve editor commands (stubs - use same logic as CPU for now)
            AddGpuCurvePointCommand = new RelayCommand(_ => AddDefaultGpuCurvePoint(), _ => GpuFanCurve.Count < 10);
            RemoveGpuCurvePointCommand = new RelayCommand(_ => RemoveLastGpuCurvePoint(), _ => GpuFanCurve.Count > 2);
            ResetGpuCurveCommand = new RelayCommand(_ => ResetGpuCurveToDefault());
            CopyFromCpuCurveCommand = new RelayCommand(_ => CopyCpuCurveToGpu());
            
            // Quick preset buttons
            ApplyMaxCoolingCommand = new RelayCommand(_ => ApplyFanMode("Max"));
            ApplyExtremeModeCommand = new RelayCommand(_ => ApplyFanMode("Extreme"));
            ApplyAutoModeCommand = new RelayCommand(_ => ApplyFanMode("Auto"));
            ApplyQuietModeCommand = new RelayCommand(_ => ApplyQuietMode());
            ApplyGamingModeCommand = new RelayCommand(_ => ApplyGamingMode());
            ApplyConstantSpeedCommand = new RelayCommand(_ => ApplyConstantSpeed());
            ReapplySavedPresetCommand = new RelayCommand(async _ => await ReapplySavedPresetAsync());
            
            // Subscribe to thermal samples to update current temperature
            ((INotifyCollectionChanged)_fanService.ThermalSamples).CollectionChanged += ThermalSamples_CollectionChanged;
            
            // Subscribe to curve changes for live preview (v2.7.0)
            CustomFanCurve.CollectionChanged += (s, e) => NotifyCurvePreviewChanged();
            
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
                Name = "Extreme", 
                Mode = FanMode.Performance,
                IsBuiltIn = true,
                Curve = GetExtremeCurve()
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
            
            // Default to Auto without applying/saving to config
            _suppressApplyOnSelection = true;
            SelectedPreset = FanPresets.FirstOrDefault(p => p.Name == "Auto") ?? FanPresets[2]; // Default to Auto
            _suppressApplyOnSelection = false;
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
        
        #region GPU Curve Editor Methods (Stubs for independent curves)
        
        private void AddDefaultGpuCurvePoint()
        {
            if (GpuFanCurve.Count >= 10) return;
            
            // Simple default point
            int newTemp = GpuFanCurve.Count > 0 
                ? GpuFanCurve.Max(p => p.TemperatureC) + 10 
                : 50;
            int newFan = Math.Min(100, newTemp);
            
            GpuFanCurve.Add(new FanCurvePoint { TemperatureC = Math.Clamp(newTemp, 30, 95), FanPercent = newFan });
            _logging.Info($"Added GPU curve point: {newTemp}°C → {newFan}%");
        }
        
        private void RemoveLastGpuCurvePoint()
        {
            if (GpuFanCurve.Count <= 2) return;
            GpuFanCurve.RemoveAt(GpuFanCurve.Count - 1);
            _logging.Info("Removed last GPU curve point");
        }
        
        private void ResetGpuCurveToDefault()
        {
            GpuFanCurve.Clear();
            foreach (var point in GetDefaultAutoCurve())
            {
                GpuFanCurve.Add(point);
            }
            _logging.Info("Reset GPU fan curve to default");
        }
        
        private void CopyCpuCurveToGpu()
        {
            GpuFanCurve.Clear();
            foreach (var point in CustomFanCurve)
            {
                GpuFanCurve.Add(new FanCurvePoint { TemperatureC = point.TemperatureC, FanPercent = point.FanPercent });
            }
            _logging.Info($"Copied CPU curve ({CustomFanCurve.Count} points) to GPU curve");
        }
        
        #endregion

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
            
            // Update UI state
            ActiveFanMode = preset.Name switch
            {
                "Max" => "Max",
                "Extreme" => "Extreme",
                "Gaming" => "Gaming",
                "Auto" => "Auto",
                "Quiet" or "Silent" => "Silent",
                _ => preset.Mode == FanMode.Manual ? "Custom" : "Auto"
            };
            
            // Save last applied preset name to config for persistence across restarts
            SaveLastPresetToConfig(preset.Name);
            // FanService logs success/failure, no need to duplicate
        }
        
        /// <summary>
        /// Save the last applied preset name to config for restoration on next startup.
        /// </summary>
        private void SaveLastPresetToConfig(string presetName)
        {
            try
            {
                var config = _configService.Load();
                config.LastFanPresetName = presetName;
                _configService.Save(config);
                _logging.Info($"💾 Last fan preset saved to config: {presetName}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save last fan preset to config: {ex.Message}");
            }
        }

        private void ApplyCustomCurve()
        {
            // Validate the curve before applying
            var validationError = ValidateFanCurve(CustomFanCurve);
            if (validationError != null)
            {
                _logging.Warn($"Invalid fan curve: {validationError}");
                System.Windows.MessageBox.Show(
                    $"{validationError}\n\nThe fan curve was not applied. Please fix the issues and try again.",
                    "Invalid Fan Curve",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var customPreset = new FanPreset
            {
                Name = "Custom (Applied)",
                Mode = FanMode.Manual,
                Curve = CustomFanCurve.ToList()
            };

            _fanService.ApplyPreset(customPreset, ImmediateApplyOnApply);
            ActiveFanMode = "Custom";
            OnPropertyChanged(nameof(CurrentFanModeName));
            // FanService logs success/failure, no need to duplicate
        }
        
        /// <summary>
        /// Validate a fan curve for safety and correctness.
        /// Returns null if valid, or an error message if invalid.
        /// </summary>
        private string? ValidateFanCurve(IEnumerable<FanCurvePoint> curve)
        {
            var points = curve.ToList();
            
            // Minimum 2 points required
            if (points.Count < 2)
                return "Fan curve must have at least 2 points to define a proper cooling response.";
            
            // Check temperature ranges
            foreach (var point in points)
            {
                if (point.TemperatureC < 0 || point.TemperatureC > 100)
                    return $"Temperature {point.TemperatureC}°C is out of range. Valid range: 0-100°C.";
                
                if (point.FanPercent < 0 || point.FanPercent > 100)
                    return $"Fan speed {point.FanPercent}% is out of range. Valid range: 0-100%.";
            }
            
            // Check for duplicate temperatures
            var sorted = points.OrderBy(p => p.TemperatureC).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i].TemperatureC == sorted[i + 1].TemperatureC)
                    return $"Duplicate temperature point at {sorted[i].TemperatureC}°C. Each temperature can only appear once.";
            }
            
            // Warn if curve doesn't cover high temperatures
            if (sorted[^1].TemperatureC < 80)
                return $"Fan curve should extend to at least 80°C to protect against thermal throttling. Highest point is {sorted[^1].TemperatureC}°C.";
            
            // Warn if low temperature has too high fan speed (noisy)
            if (sorted[0].TemperatureC < 50 && sorted[0].FanPercent > 60)
                return $"Low temperature ({sorted[0].TemperatureC}°C) has high fan speed ({sorted[0].FanPercent}%). This may cause unnecessary noise.";
            
            // Warn about potentially inverted/non-monotonic curves (fan% decreases as temp increases)
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i + 1].FanPercent < sorted[i].FanPercent - 10)
                {
                    // Log but don't block - user might have a valid reason
                    _logging.Warn($"Fan curve has decreasing speed: {sorted[i].TemperatureC}°C={sorted[i].FanPercent}% → {sorted[i+1].TemperatureC}°C={sorted[i+1].FanPercent}%. Is this intentional?");
                }
            }
            
            return null; // Valid curve
        }

        private void SaveCustomPreset()
        {
            // Validate before saving
            var validationError = ValidateFanCurve(CustomFanCurve);
            if (validationError != null)
            {
                _logging.Warn($"Cannot save invalid fan curve: {validationError}");
                System.Windows.MessageBox.Show(
                    $"{validationError}\n\nPlease fix the issues before saving.",
                    "Invalid Fan Curve",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrWhiteSpace(CustomPresetName))
            {
                _logging.Warn("Cannot save preset with empty name");
                System.Windows.MessageBox.Show(
                    "Please enter a name for the preset.",
                    "Name Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
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

            // Select and apply the newly saved preset immediately for better UX
            SelectedPreset = preset;
            _fanService.ApplyPreset(preset);
            SaveLastPresetToConfig(preset.Name);
            
            // Notify UI that saved presets list changed
            OnPropertyChanged(nameof(SavedCustomPresets));
            OnPropertyChanged(nameof(HasSavedPresets));
            
            _logging.Info($"✓ Saved and applied custom fan preset: '{preset.Name}' with {preset.Curve.Count} points");
        }
        
        private void DeleteSelectedPreset()
        {
            if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
                return;
            
            var presetName = SelectedPreset.Name;
            var result = System.Windows.MessageBox.Show(
                $"Delete custom preset '{presetName}'?",
                "Delete Preset",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            
            if (result != System.Windows.MessageBoxResult.Yes)
                return;
            
            FanPresets.Remove(SelectedPreset);
            SelectedPreset = null;
            SavePresetsToConfig();
            
            // Notify UI that saved presets list changed
            OnPropertyChanged(nameof(SavedCustomPresets));
            OnPropertyChanged(nameof(HasSavedPresets));
            
            _logging.Info($"🗑️ Deleted custom fan preset: '{presetName}'");
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
            // v2.6.1: More aggressive curve for high-power laptops (i9/i7 + RTX 40xx)
            // Reaches 100% by 85°C to prevent thermal throttling
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 35 },
                new() { TemperatureC = 50, FanPercent = 45 },
                new() { TemperatureC = 60, FanPercent = 60 },
                new() { TemperatureC = 70, FanPercent = 75 },
                new() { TemperatureC = 75, FanPercent = 85 },
                new() { TemperatureC = 80, FanPercent = 95 },
                new() { TemperatureC = 85, FanPercent = 100 }
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
            // Gaming mode: aggressive ramp, maxes out at 75°C
            // Similar to OGH Performance mode but with 75°C ceiling
            // Recommended for gaming sessions where cooling is priority over noise
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 35 },
                new() { TemperatureC = 50, FanPercent = 50 },
                new() { TemperatureC = 60, FanPercent = 70 },
                new() { TemperatureC = 70, FanPercent = 90 },
                new() { TemperatureC = 75, FanPercent = 100 }
            };
        }
        
        private static List<FanCurvePoint> GetExtremeCurve()
        {
            // Extreme mode: maximum cooling, maxes out at 70°C (like OGH Performance)
            // Starts aggressive early, fans at 100% by 70°C to keep temps pinned low
            // Recommended for: sustained gaming, benchmarks, thermal throttling prevention
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 45 },
                new() { TemperatureC = 50, FanPercent = 60 },
                new() { TemperatureC = 60, FanPercent = 80 },
                new() { TemperatureC = 70, FanPercent = 100 }
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
            
            ActiveFanMode = "Gaming";
            _logging.Info("Applied Gaming fan mode (Performance thermal policy with aggressive curve)");
        }

        /// <summary>
        /// Re-apply the last saved fan preset from config. Useful as a manual "force reapply" button.
        /// </summary>
        private async Task ReapplySavedPresetAsync()
        {
            var saved = _configService.Config.LastFanPresetName;
            if (string.IsNullOrEmpty(saved))
            {
                System.Windows.MessageBox.Show("No saved fan preset found in configuration.", "Reapply Saved Preset",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // First, see if it's a custom preset
            var preset = _configService.Config.FanPresets.FirstOrDefault(p => p.Name.Equals(saved, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                _fanService.ApplyPreset(preset);
                SelectedPreset = FanPresets.FirstOrDefault(p => p.Name == preset.Name) ?? SelectedPreset;
                SaveLastPresetToConfig(preset.Name);
                _logging.Info($"Manually reapplied saved preset: {preset.Name}");
                return;
            }

            // Handle built-in names
            var nameLower = saved.ToLowerInvariant();
            if (nameLower.Contains("max"))
            {
                _fanService.ApplyMaxCooling();
                _logging.Info($"Manually reapplied saved preset: {saved} (Max)");
                return;
            }

            if (nameLower == "auto" || nameLower == "default")
            {
                _fanService.ApplyAutoMode();
                _logging.Info($"Manually reapplied saved preset: {saved} (Auto)");
                return;
            }

            if (nameLower == "quiet" || nameLower == "silent")
            {
                _fanService.ApplyQuietMode();
                _logging.Info($"Manually reapplied saved preset: {saved} (Quiet)");
                return;
            }

            // Fallback - attempt to apply by name
            var fallback = new FanPreset { Name = saved, Mode = FanMode.Performance };
            _fanService.ApplyPreset(fallback);
            _logging.Info($"Manually reapplied saved preset via fallback: {saved}");
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
            
            ActiveFanMode = "Silent";
            _logging.Info("Applied Quiet fan mode");
        }
        
        /// <summary>
        /// Apply constant fan speed mode (OmenMon-style fixed percentage).
        /// Fans are held at ConstantFanPercent regardless of temperature.
        /// </summary>
        private void ApplyConstantSpeed()
        {
            _fanService.DisableCurve(); // Stop any active curve
            _fanService.ForceSetFanSpeed(ConstantFanPercent);
            ActiveFanMode = "Constant";
            _logging.Info($"Applied constant fan speed: {ConstantFanPercent}% (~{ConstantFanRpmEstimate} RPM)");
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
