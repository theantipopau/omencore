using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OmenCore.Controls;
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
        private readonly IFanVerificationService? _fanVerificationService;
        private readonly FanCalibrationStorageService _fanCalibrationStorage;
        private FanPreset? _selectedPreset;
        private string _customPresetName = "Custom";
        private double _currentTemperature;
        private double _currentCpuTemperature;
        private double _currentGpuTemperature;
        private bool _suppressApplyOnSelection;
        private IEnumerable<FanCurvePoint>? _hoveredPresetCurve;
        private bool _isApplyingCustomCurve;
        private volatile bool _isApplyingConstantSpeed;
        private string _curveApplyStatus = "Ready to apply";
        private string _fanCalibrationModelId = "unknown";
        private string _fanCalibrationModelName = "Unknown Model";
        private bool _showRpmSanityWarning = false;
        private string _rpmSanityWarningMessage = string.Empty;
        private readonly SemaphoreSlim _fanApplySemaphore = new(1, 1);
        private DateTime _lastCurveVerificationKickUtc = DateTime.MinValue;
        private string? _lastCurveVerificationKickKey;
        private static readonly TimeSpan CurveVerificationKickCooldown = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CurveVerificationKickTimeout = TimeSpan.FromSeconds(15);
        private const int CurveVerificationKickFanLimit = 1;

        public ObservableCollection<FanPreset> FanPresets { get; } = new();
        public ObservableCollection<FanCurvePoint> CustomFanCurve { get; } = new();
        public ObservableCollection<FanCalibrationMapRow> FanCalibrationMapRows { get; } = new();
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
                    NotifyCurvePreviewChanged();
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

                if (temp <= sorted.First().TemperatureC)
                    return sorted.First().FanPercent;

                if (temp >= sorted.Last().TemperatureC)
                    return sorted.Last().FanPercent;

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    if (temp >= sorted[i].TemperatureC && temp <= sorted[i + 1].TemperatureC)
                    {
                        var t1 = sorted[i].TemperatureC;
                        var t2 = sorted[i + 1].TemperatureC;
                        var f1 = sorted[i].FanPercent;
                        var f2 = sorted[i + 1].FanPercent;
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
                return ApplySafetyFloor(PredictedFanPercent, CurrentTemperature);
            }
        }

        public bool IsSafetyFloorActive => EffectiveFanPercent > PredictedFanPercent;

        public string SafetyFloorNoticeText
        {
            get
            {
                if (!IsSafetyFloorActive)
                {
                    return string.Empty;
                }

                return $"Thermal guard active: curve requests {PredictedFanPercent}% at {CurrentTemperature:F0}C; OmenCore will command {EffectiveFanPercent}% until temperatures fall.";
            }
        }
        
        public string CurvePreviewText
        {
            get
            {
                var raw = PredictedFanPercent;
                var effective = EffectiveFanPercent;
                if (effective != raw)
                    return $"At {CurrentTemperature:F0}C -> requested {raw}%, effective {effective}%";
                return $"At {CurrentTemperature:F0}C -> {raw}%";
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

                if (IsSafetyFloorActive)
                {
                    return SafetyFloorNoticeText;
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
            OnPropertyChanged(nameof(IsSafetyFloorActive));
            OnPropertyChanged(nameof(SafetyFloorNoticeText));
            OnPropertyChanged(nameof(CurvePreviewText));
            OnPropertyChanged(nameof(CurveValidationMessage));
            OnPropertyChanged(nameof(ShowCurveEditor));
        }

        private void NotifyFanOwnershipChanged()
        {
            OnPropertyChanged(nameof(FanOwnershipSummary));
            OnPropertyChanged(nameof(FanOwnershipDetail));
            OnPropertyChanged(nameof(FanOwnershipVisualState));
            OnPropertyChanged(nameof(FanOwnershipVisualLabel));
            OnPropertyChanged(nameof(FanCapabilityBadgeText));
            OnPropertyChanged(nameof(FanCapabilityVisualState));
            OnPropertyChanged(nameof(FanProfileHeaderHint));
            OnPropertyChanged(nameof(ExtremePresetSubtitle));
            OnPropertyChanged(nameof(GamingPresetSubtitle));
            OnPropertyChanged(nameof(CustomPresetSubtitle));
            OnPropertyChanged(nameof(CustomCurveTooltip));
            OnPropertyChanged(nameof(ShowCurveEditor));
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
                    OnPropertyChanged(nameof(CanDeleteSelectedPreset));
                    RaisePresetCommandStateChanged();
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
        /// Use this when the fan mode has already been applied externally, AND the change should
        /// NOT become the persisted startup preference (e.g. automatic power-source switching).
        /// For a deliberate user action that already applied the preset through a different entry
        /// point (tray menu, hotkey, General quick-profile buttons), use
        /// <see cref="SelectPresetByNameNoApplyAndSave"/> instead so the choice survives a relaunch.
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

        /// <summary>
        /// Select a fan preset by name without re-applying it to hardware, and persist it as the
        /// startup preference. Use this when another entry point (tray menu, hotkey, General page
        /// quick-profile buttons, OMEN key) already applied the preset directly through
        /// <see cref="FanService"/> and only needs this view-model's selection and config
        /// persistence kept in sync — otherwise the choice silently reverts to the last value saved
        /// via this page's own controls on next launch (same root cause as GitHub #145, for fans).
        /// </summary>
        public void SelectPresetByNameNoApplyAndSave(string presetName)
        {
            SelectPresetByNameNoApply(presetName);
            var preset = FanPresets.FirstOrDefault(p =>
                p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                SaveLastPresetToConfig(preset.Name);
            }
        }
        
        public string CurrentFanModeName => IsConstantSelected ? "Direct" : SelectedPreset?.Name ?? "Auto";
        
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
                    OnPropertyChanged(nameof(ShowCurveEditor));
                    OnPropertyChanged(nameof(ShowConstantControl));
                    OnPropertyChanged(nameof(CurrentFanModeName));
                    NotifyFanOwnershipChanged();
                    RaisePresetCommandStateChanged();
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
            set { if (value && FanCurvesAvailable) ActiveFanMode = "Custom"; }
        }
        
        public bool IsConstantSelected
        {
            get => _activeFanMode == "Constant";
            set { if (value && ManualFanControlAvailable) ActiveFanMode = "Constant"; }
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
                    OnPropertyChanged(nameof(DirectPresetSubtitle));
                    NotifyFanOwnershipChanged();
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

        public bool IsApplyingCustomCurve
        {
            get => _isApplyingCustomCurve;
            private set
            {
                if (_isApplyingCustomCurve != value)
                {
                    _isApplyingCustomCurve = value;
                    OnPropertyChanged();
                    NotifyFanOwnershipChanged();
                }
            }
        }

        public string CurveApplyStatus
        {
            get => _curveApplyStatus;
            private set
            {
                if (_curveApplyStatus != value)
                {
                    _curveApplyStatus = value;
                    OnPropertyChanged();
                    NotifyFanOwnershipChanged();
                }
            }
        }

        public string FanOwnershipSummary
        {
            get
            {
                if (IsApplyingCustomCurve)
                    return "Applying custom curve";

                if (IsCurveActive)
                    return ActiveFanMode == "Custom"
                        ? "Custom curve is actively managing fans"
                        : $"{ActiveFanMode} curve is actively managing fans";

                return ActiveFanMode switch
                {
                    "Auto" => "Auto mode: firmware owns fan control",
                    "Max" => "Max mode: OmenCore requests full cooling",
                    "Constant" => $"Constant mode: OmenCore requests {ConstantFanPercent}%",
                    "Custom" => "Custom curve loaded, not actively applied",
                    _ => $"{ActiveFanMode} preset selected"
                };
            }
        }

        public string FanOwnershipDetail
        {
            get
            {
                var backend = string.IsNullOrWhiteSpace(RpmSourceDisplay) ? "unknown backend" : RpmSourceDisplay;
                var status = string.IsNullOrWhiteSpace(CurveApplyStatus) ? "Ready" : CurveApplyStatus;
                if (ProfileOnlyFanControl)
                {
                    return $"Backend: {backend}. Profile-only firmware control; custom curves and fixed fan levels are unavailable. {status}";
                }

                return $"Backend: {backend}. {status}";
            }
        }

        public string FanOwnershipVisualState
        {
            get
            {
                if (ShowRpmSanityWarning)
                    return "blocked";

                if (IsApplyingCustomCurve || (ActiveFanMode == "Custom" && !IsCurveActive))
                    return "degraded";

                return "confirmed";
            }
        }

        public string FanOwnershipVisualLabel => FanOwnershipVisualState switch
        {
            "blocked" => "Blocked",
            "degraded" => "Degraded",
            _ => "Confirmed"
        };

        public bool FanCurvesAvailable => _fanService.FanCurvesAvailable;

        public bool ManualFanControlAvailable => _fanService.ManualFanControlAvailable;

        public bool ProfileOnlyFanControl => !FanCurvesAvailable;

        public bool ShowCurveEditor => IsCustomSelected && FanCurvesAvailable;

        public bool ShowConstantControl => IsConstantSelected && ManualFanControlAvailable;

        public string FanCapabilityBadgeText => ProfileOnlyFanControl ? "Profile-only" : "Curves";

        public string FanCapabilityVisualState => ProfileOnlyFanControl ? "degraded" : "confirmed";

        public string FanProfileHeaderHint => ProfileOnlyFanControl
            ? "Select an OEM fan profile"
            : "Select a preset, direct fan level, or custom curve";

        public string ExtremePresetSubtitle => ProfileOnlyFanControl ? "Profile" : "@75C";

        public string GamingPresetSubtitle => ProfileOnlyFanControl ? "Profile" : "@80C";

        public string CustomPresetSubtitle => FanCurvesAvailable ? "Editor" : "Unavailable";

        public string DirectPresetSubtitle => ManualFanControlAvailable ? $"{ConstantFanPercent}%" : "Unavailable";

        public string CustomCurveTooltip => FanCurvesAvailable
            ? "Apply your custom fan curve defined in the editor below."
            : "This model's fan controller doesn't expose a custom-curve interface — a hardware/firmware limitation, not related to whether the model profile is verified. Use OEM fan profiles instead.";

        public string DirectFanControlTooltip => ManualFanControlAvailable
            ? "Hold fans at a fixed requested level using manual fan control."
            : "This model's fan controller doesn't expose direct fixed-level control — a hardware/firmware limitation, not related to whether the model profile is verified. Use OEM fan profiles instead.";

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
        public bool CanDeleteSelectedPreset => SelectedPreset != null && !SelectedPreset.IsBuiltIn;
        
        /// <summary>
        /// Filtered view of FanPresets showing only user-saved (non-built-in) presets.
        /// </summary>
        public IEnumerable<FanPreset> SavedCustomPresets => FanCurvesAvailable
            ? FanPresets.Where(p => !p.IsBuiltIn)
            : Enumerable.Empty<FanPreset>();

        /// <summary>
        /// Ghost curve shown on the FanCurveEditor when a preset card is hovered.
        /// Bound to FanCurveEditor.GhostCurvePoints to preview without applying.
        /// </summary>
        public IEnumerable<FanCurvePoint>? HoveredPresetCurve
        {
            get => _hoveredPresetCurve;
            private set
            {
                if (_hoveredPresetCurve != value)
                {
                    _hoveredPresetCurve = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Show the given preset as a ghost curve on the editor.</summary>
        public void SetHoveredPreset(FanPreset? preset) => HoveredPresetCurve = FanCurvesAvailable ? preset?.Curve : null;

        /// <summary>Clear the ghost overlay.</summary>
        public void ClearHoveredPreset() => HoveredPresetCurve = null;
        
        /// <summary>
        /// Whether there are any saved custom presets to show.
        /// </summary>
        public bool HasSavedPresets => FanCurvesAvailable && FanPresets.Any(p => !p.IsBuiltIn);
        
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
        public ICommand RestoreOemAutoCommand { get; }
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
                // Independent CPU/GPU curve UI remains intentionally disabled until
                // model-level support detection is complete.
                var requested = value && IndependentCurvesFeatureAvailable;
                if (_independentCurvesEnabled != requested)
                {
                    _independentCurvesEnabled = requested;
                    OnPropertyChanged();
                }
            }
        }

        public bool IndependentCurvesFeatureAvailable => false;

        public string GpuFanControlSupportSummary =>
            "GPU fan curve write is model-dependent and currently not guaranteed. OmenCore applies a unified duty target and the GPU fan typically tracks CPU duty.";

        public string GpuFanDutyRatioSummary
        {
            get
            {
                var cpu = FanTelemetry.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
                var gpu = FanTelemetry.FirstOrDefault(f => f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase));

                if (cpu == null || gpu == null || cpu.DutyCyclePercent <= 0)
                {
                    return "Observed GPU/CPU duty ratio: unavailable (insufficient telemetry).";
                }

                var ratio = (double)gpu.DutyCyclePercent / cpu.DutyCyclePercent;
                return $"Observed GPU/CPU duty ratio: {ratio:P0} ({gpu.DutyCyclePercent}% GPU vs {cpu.DutyCyclePercent}% CPU).";
            }
        }
        
        /// <summary>GPU fan curve for independent mode (placeholder).</summary>
        public ObservableCollection<FanCurvePoint> GpuFanCurve { get; } = new();
        
        /// <summary>Current CPU temperature for the CPU-specific curve editor.</summary>
        public double CurrentCpuTemperature
        {
            get => _currentCpuTemperature > 0 ? _currentCpuTemperature : CurrentTemperature;
            private set { _currentCpuTemperature = value; OnPropertyChanged(); }
        }

        /// <summary>Current GPU temperature for the GPU-specific curve editor.</summary>
        public double CurrentGpuTemperature
        {
            get => _currentGpuTemperature > 0 ? _currentGpuTemperature : CurrentTemperature;
            private set { _currentGpuTemperature = value; OnPropertyChanged(); }
        }

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

        public bool IsFanCalibrationAvailable => _fanVerificationService?.IsAvailable == true;

        public bool HasFanCalibrationData => _fanCalibrationStorage.HasCalibration(_fanCalibrationModelId);

        public bool HasFanCalibrationMap => FanCalibrationMapRows.Count > 0;

        public string FanCalibrationStatusText
        {
            get
            {
                if (!IsFanCalibrationAvailable)
                {
                    return $"Calibration unavailable: {FanCalibrationUnavailableReason}";
                }

                var calibration = _fanCalibrationStorage.GetCalibration(_fanCalibrationModelId);
                if (calibration == null)
                {
                    return $"No calibration data stored for {_fanCalibrationModelName}. Run the wizard to build a duty-to-RPM map.";
                }

                return $"Calibration available for {calibration.ModelName} from {calibration.CreatedDate:g}. {calibration.FanCalibrations.Count} fan map(s) stored.";
            }
        }

        public string FanCalibrationUnavailableReason
        {
            get
            {
                if (_fanVerificationService == null)
                {
                    return "fan verification service is not initialized in this runtime context.";
                }

                if (!_fanVerificationService.IsAvailable)
                {
                    return $"fan verification backend is inactive (active fan backend: {_fanService.Backend}).";
                }

                return "fan verification is available.";
            }
        }

        public string FanCalibrationActionText => HasFanCalibrationData ? "Recalibrate" : "Start Calibration";

        public string FanCalibrationMapSummary => HasFanCalibrationMap
            ? "Measured duty-to-RPM map from the latest calibration run."
            : "No duty-to-RPM map available yet.";

        public bool IsFanPerformanceLinked => _configService.Config.LinkFanToPerformanceMode;

        public string FanPerformanceLinkBadgeText => IsFanPerformanceLinked
            ? "Linked: performance may change fan mode"
            : "Independent: fan mode stays under fan controls";

        public string FanPerformanceLinkDescription => IsFanPerformanceLinked
            ? "When linked, selecting a performance profile can also switch the active fan mode. Use this only if you want profile-driven fan behavior."
            : "When independent, performance profile changes do not rewrite fan policy. Your selected fan preset/curve remains active until you change fan controls directly.";

        public bool ShowFanPerformanceInfoBanner => !IsFanPerformanceLinked && !_configService.Config.DismissedFanPerformanceDecouplingNotice;

        public ICommand DismissFanPerformanceInfoBannerCommand { get; }
        
        /// <summary>
        /// Whether to show the RPM sanity warning banner (zero RPM with active duty cycle for >30s).
        /// </summary>
        public bool ShowRpmSanityWarning
        {
            get => _showRpmSanityWarning;
            set
            {
                if (SetProperty(ref _showRpmSanityWarning, value, nameof(ShowRpmSanityWarning)))
                {
                    NotifyFanOwnershipChanged();
                }
            }
        }

        /// <summary>
        /// The message text to display in the RPM sanity warning banner.
        /// </summary>
        public string RpmSanityWarningMessage
        {
            get => _rpmSanityWarningMessage;
            set => SetProperty(ref _rpmSanityWarningMessage, value, nameof(RpmSanityWarningMessage));
        }

        public ICommand DismissRpmSanityWarningCommand { get; }
        public ICommand OpenFanCalibrationWizardCommand { get; }
        
        // GPU curve editor commands (stubs - will use same curve for now)
        public ICommand AddGpuCurvePointCommand { get; }
        public ICommand RemoveGpuCurvePointCommand { get; }
        public ICommand ResetGpuCurveCommand { get; }
        public ICommand CopyFromCpuCurveCommand { get; }
        
        #endregion

        public FanControlViewModel(FanService fanService, ConfigurationService configService, LoggingService logging, IFanVerificationService? fanVerificationService = null)
        {
            _fanService = fanService;
            _configService = configService;
            _logging = logging;
            _fanVerificationService = fanVerificationService;
            _fanCalibrationStorage = new FanCalibrationStorageService(logging);

            InitializeFanCalibrationContext();
            RefreshFanCalibrationStatus();
            
            // Load hysteresis and transition settings from config
            _fanService.SetHysteresis(_configService.Config.FanHysteresis);
            ImmediateApplyOnApply = _configService.Config.FanTransition.ApplyImmediatelyOnUserAction;
            
            ApplyCustomCurveCommand = new AsyncRelayCommand(async _ => await ApplyCustomCurveAsync(), _ => FanCurvesAvailable && !IsApplyingCustomCurve);

            // Initialize transition values from config
            SmoothingDurationMs = _configService.Config.FanTransition.SmoothingDurationMs;
            SmoothingStepMs = _configService.Config.FanTransition.SmoothingStepMs;
            SaveCustomPresetCommand = new RelayCommand(_ => SaveCustomPreset(), _ => FanCurvesAvailable);
            ImportPresetsCommand = new RelayCommand(_ => ImportPresets());
            ExportPresetsCommand = new RelayCommand(_ => ExportPresets());
            DeleteSelectedPresetCommand = new RelayCommand(_ => DeleteSelectedPreset(), _ => CanDeleteSelectedPreset);
            
            // Curve editor commands
            AddCurvePointCommand = new RelayCommand(_ => AddDefaultCurvePoint(), _ => FanCurvesAvailable && CustomFanCurve.Count < 10);
            RemoveCurvePointCommand = new RelayCommand(_ => RemoveLastCurvePoint(), _ => FanCurvesAvailable && CustomFanCurve.Count > 2);
            ResetCurveCommand = new RelayCommand(_ => ResetCurveToDefault(), _ => FanCurvesAvailable);
            
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
            ApplyConstantSpeedCommand = new RelayCommand(_ => ApplyConstantSpeed(), _ => ManualFanControlAvailable);
            RestoreOemAutoCommand = new RelayCommand(_ => RestoreOemAutoControl());
            ReapplySavedPresetCommand = new RelayCommand(async _ => await ReapplySavedPresetAsync());
            OpenFanCalibrationWizardCommand = new RelayCommand(_ => OpenFanCalibrationWizard(), _ => IsFanCalibrationAvailable);
            DismissFanPerformanceInfoBannerCommand = new RelayCommand(_ => DismissFanPerformanceInfoBanner());
            DismissRpmSanityWarningCommand = new RelayCommand(_ => DismissRpmSanityWarning());
            
            // Subscribe to RPM sanity check events
            _fanService.RpmSanityCheckWarning += FanService_RpmSanityCheckWarning;
            
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
                Name = "Gaming",
                Mode = FanMode.Performance,
                IsBuiltIn = true,
                Curve = GetGamingCurve()
            });
            FanPresets.Add(new FanPreset
            {
                Name = "Quiet",
                Mode = FanMode.Quiet,
                IsBuiltIn = true,
                Curve = GetQuietCurve()
            });
            
            // Load custom presets from config file
            LoadPresetsFromConfig();
            RestoreAdHocCustomCurveFromConfig();

            // Initialise GPU curve from config, or defaults if not yet saved
            var savedGpuCurve = _configService.Config.GpuFanCurve;
            if (savedGpuCurve != null && savedGpuCurve.Count >= 2)
            {
                foreach (var point in savedGpuCurve)
                    GpuFanCurve.Add(new FanCurvePoint { TemperatureC = point.TemperatureC, FanPercent = point.FanPercent });
                _logging.Info($"📂 Loaded GPU fan curve from config ({savedGpuCurve.Count} points)");
            }
            else
            {
                foreach (var point in GetDefaultAutoCurve())
                    GpuFanCurve.Add(point);
            }

            // Persist GPU curve changes automatically
            GpuFanCurve.CollectionChanged += (s, e) => SaveGpuCurveToConfig();

            // Show the last user-facing preset/curve in the editor without applying it.
            _suppressApplyOnSelection = true;
            SelectedPreset = ResolveInitialPresetSelection();
            _suppressApplyOnSelection = false;
        }

        public void RefreshFanLinkState()
        {
            OnPropertyChanged(nameof(IsFanPerformanceLinked));
            OnPropertyChanged(nameof(FanPerformanceLinkBadgeText));
            OnPropertyChanged(nameof(FanPerformanceLinkDescription));
            OnPropertyChanged(nameof(ShowFanPerformanceInfoBanner));
        }

        private void RestoreOemAutoControl()
        {
            var success = _fanService.RestoreOemAutoControl();
            ActiveFanMode = "Auto";
            SelectPresetByNameNoApplyAndSave("Auto");
            CurveApplyStatus = success
                ? "OEM auto restored"
                : "OEM auto restore did not report success";
            NotifyFanOwnershipChanged();
        }

        private void InitializeFanCalibrationContext()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                    var model = obj["Model"]?.ToString() ?? string.Empty;
                    _fanCalibrationModelName = string.Join(" ", new[] { manufacturer, model }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                    break;
                }

                if (string.IsNullOrWhiteSpace(_fanCalibrationModelName))
                {
                    _fanCalibrationModelName = "Unknown Model";
                }

                _fanCalibrationModelId = GenerateFanCalibrationModelId(_fanCalibrationModelName);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to initialize fan calibration model context: {ex.Message}");
                _fanCalibrationModelName = "Unknown Model";
                _fanCalibrationModelId = "unknown";
            }
        }

        private void RefreshFanCalibrationStatus()
        {
            FanCalibrationMapRows.Clear();

            var calibration = _fanCalibrationStorage.GetCalibration(_fanCalibrationModelId);
            if (calibration != null)
            {
                var cpuMap = calibration.FanCalibrations.FirstOrDefault(f => f.FanIndex == 0)?.CalibrationPoints;
                var gpuMap = calibration.FanCalibrations.FirstOrDefault(f => f.FanIndex == 1)?.CalibrationPoints;
                var percents = new HashSet<int>((cpuMap ?? Enumerable.Empty<FanCalibrationDataPoint>()).Select(p => p.Percent));

                foreach (var percent in (gpuMap ?? Enumerable.Empty<FanCalibrationDataPoint>()).Select(p => p.Percent))
                {
                    percents.Add(percent);
                }

                foreach (var percent in percents.OrderBy(p => p))
                {
                    var cpu = cpuMap?.FirstOrDefault(p => p.Percent == percent)?.MeasuredRpm;
                    var gpu = gpuMap?.FirstOrDefault(p => p.Percent == percent)?.MeasuredRpm;
                    FanCalibrationMapRows.Add(new FanCalibrationMapRow
                    {
                        DutyPercent = percent,
                        CpuRpmText = cpu.HasValue ? $"{cpu.Value} RPM" : "-",
                        GpuRpmText = gpu.HasValue ? $"{gpu.Value} RPM" : "-"
                    });
                }
            }

            OnPropertyChanged(nameof(HasFanCalibrationData));
            OnPropertyChanged(nameof(HasFanCalibrationMap));
            OnPropertyChanged(nameof(FanCalibrationStatusText));
            OnPropertyChanged(nameof(FanCalibrationActionText));
            OnPropertyChanged(nameof(FanCalibrationMapSummary));
        }

        private void OpenFanCalibrationWizard()
        {
            if (!IsFanCalibrationAvailable)
            {
                CurveApplyStatus = $"Calibration is unavailable: {FanCalibrationUnavailableReason}";
                return;
            }

            try
            {
                var window = new Window
                {
                    Title = "Fan Calibration Wizard",
                    Width = 920,
                    Height = 760,
                    MinWidth = 820,
                    MinHeight = 640,
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new FanCalibrationControl(_fanVerificationService!, _logging)
                };

                window.ShowDialog();
                RefreshFanCalibrationStatus();
                CurveApplyStatus = HasFanCalibrationData
                    ? "Fan calibration map refreshed from the latest wizard run."
                    : "Fan calibration wizard closed.";
            }
            catch (Exception ex)
            {
                CurveApplyStatus = $"Failed to open calibration wizard: {ex.Message}";
                _logging.Error("Failed to open fan calibration wizard", ex);
            }
        }

        private static string GenerateFanCalibrationModelId(string modelInfo)
        {
            return FanCalibrationStorageService.NormalizeModelId(modelInfo);
        }

        private void DismissFanPerformanceInfoBanner()
        {
            var config = _configService.Config;
            if (config.DismissedFanPerformanceDecouplingNotice)
            {
                return;
            }

            config.DismissedFanPerformanceDecouplingNotice = true;
            _configService.Save(config);
            RefreshFanLinkState();
        }

        /// <summary>
        /// Event handler for RPM sanity check warnings from FanService.
        /// </summary>
        private void FanService_RpmSanityCheckWarning(object? sender, RpmSanityCheckEventArgs e)
        {
            ShowRpmSanityWarning = true;
            RpmSanityWarningMessage = e.Message;
            _logging.Warn($"RPM sanity check warning: {e.Message}");
        }

        /// <summary>
        /// Dismiss the RPM sanity warning banner.
        /// </summary>
        private void DismissRpmSanityWarning()
        {
            ShowRpmSanityWarning = false;
            RpmSanityWarningMessage = string.Empty;
            _fanService.DismissRpmSanityWarning();
        }
        
        private void ThermalSamples_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_fanService.ThermalSamples.Count > 0)
            {
                var latest = _fanService.ThermalSamples[^1];
                CurrentTemperature = Math.Max(latest.CpuCelsius, latest.GpuCelsius);
                // Update dedicated per-sensor properties for independent curve editor
                if (latest.CpuCelsius > 0) CurrentCpuTemperature = latest.CpuCelsius;
                if (latest.GpuCelsius > 0) CurrentGpuTemperature = latest.GpuCelsius;
                OnPropertyChanged(nameof(GpuFanDutyRatioSummary));
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
            
            if (preset.Curve != null && preset.Curve.Count > 0)
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

        private FanPreset ResolveInitialPresetSelection()
        {
            var lastPresetName = _configService.Config.LastFanPresetName;
            if (!string.IsNullOrWhiteSpace(lastPresetName))
            {
                var lastPreset = FanPresets.FirstOrDefault(p =>
                    p.Name.Equals(lastPresetName, StringComparison.OrdinalIgnoreCase));
                if (lastPreset != null)
                {
                    if (!FanCurvesAvailable && lastPreset.Mode == FanMode.Manual)
                    {
                        _logging.Info($"Skipping restored fan preset '{lastPreset.Name}' because custom fan curves are unavailable on this model");
                    }
                    else
                    {
                        return lastPreset;
                    }
                }
            }

            // Older configs can contain the last applied ad-hoc curve without the
            // corresponding preset name. A stale name can produce the same state
            // after a preset is renamed outside OmenCore. RestoreAdHocCustomCurveFromConfig
            // has already materialized that saved curve as the manual Custom preset,
            // so prefer it for UI hydration and repair the selection metadata. This
            // does not apply the curve because constructor selection is suppressed.
            if (FanCurvesAvailable &&
                _configService.Config.CustomFanCurve is { Count: >= 2 })
            {
                var customPreset = FanPresets.FirstOrDefault(p =>
                    !p.IsBuiltIn &&
                    p.Mode == FanMode.Manual &&
                    p.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase));
                if (customPreset != null)
                {
                    RepairLastPresetSelection(customPreset.Name, lastPresetName);
                    return customPreset;
                }
            }

            if (!FanCurvesAvailable)
            {
                var autoPreset = FanPresets.FirstOrDefault(p => p.Name == "Auto");
                if (autoPreset != null)
                {
                    ActiveFanMode = "Auto";
                    return autoPreset;
                }
            }

            return FanPresets.FirstOrDefault(p => p.Name == "Auto") ?? FanPresets[2];
        }

        private void RepairLastPresetSelection(string presetName, string? stalePresetName)
        {
            try
            {
                var config = _configService.Config;
                config.LastFanPresetName = presetName;
                _configService.Save(config);

                var previousSelection = string.IsNullOrWhiteSpace(stalePresetName)
                    ? "missing"
                    : $"'{stalePresetName}'";
                _logging.Info($"Repaired fan preset selection from {previousSelection} to '{presetName}' using the saved custom curve");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to repair saved fan preset selection: {ex.Message}");
            }
        }

        private void RestoreAdHocCustomCurveFromConfig()
        {
            if (!FanCurvesAvailable)
            {
                _logging.Info("Skipped restoring saved custom fan curve because this model is limited to OEM fan profiles");
                return;
            }

            var savedCurve = _configService.Config.CustomFanCurve;
            if (savedCurve == null || savedCurve.Count < 2)
            {
                return;
            }

            var existing = FanPresets.FirstOrDefault(p =>
                !p.IsBuiltIn && p.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                FanPresets.Add(new FanPreset
                {
                    Name = "Custom",
                    Mode = FanMode.Manual,
                    Curve = CloneCurve(savedCurve),
                    IsBuiltIn = false
                });
            }
            else
            {
                existing.Curve = CloneCurve(savedCurve);
            }

            _logging.Info($"Loaded last applied custom fan curve from config ({savedCurve.Count} points)");
        }

        private void ApplyPreset(FanPreset preset)
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn($"Skipped preset '{preset.Name}' apply request because fan diagnostics mode is active");
                return;
            }

            // Run the blocking WMI + verification calls on a background thread to avoid
            // freezing the UI (WmiFanController has multiple Thread.Sleep calls, and the
            // old VerificationPasses() had a 4×200ms polling loop on the calling thread).
            _ = ApplyPresetAsync(preset);
        }

        private async Task ApplyPresetAsync(FanPreset preset)
        {
            await _fanApplySemaphore.WaitAsync();
            try
            {
                CurveApplyStatus = $"Applying '{preset.Name}'… (transitioning)";
                var immediateApply = ImmediateApplyOnApply || (preset.Curve?.Count > 0);
                var applied = await Task.Run(() => _fanService.ApplyPreset(preset, immediate: immediateApply));
                if (!applied)
                {
                    CurveApplyStatus = $"Preset '{preset.Name}' failed verification";
                    _logging.Warn($"Preset '{preset.Name}' was not saved because fan service reported apply failure");
                    return;
                }

                var dispatcher = App.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                await dispatcher.InvokeAsync(() =>
                {
                    // Update UI state
                    ActiveFanMode = FanModeNameResolver.ResolveCardMode(preset);

                    // Save last applied preset name to config for persistence across restarts
                    SaveLastPresetToConfig(preset.Name);

                    if (!preset.IsBuiltIn && preset.Mode == FanMode.Manual && preset.Curve != null && preset.Curve.Count > 0)
                    {
                        _ = VerifySavedPresetApplyAsync(preset);
                    }
                    else
                    {
                        CurveApplyStatus = $"Preset '{preset.Name}' applied";
                    }
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to apply preset '{preset.Name}': {ex.Message}");
            }
            finally
            {
                _fanApplySemaphore.Release();
            }
        }

        private async Task VerifySavedPresetApplyAsync(FanPreset preset)
        {
            if (IsApplyingCustomCurve)
            {
                CurveApplyStatus = "Verification already in progress...";
                return;
            }

            IsApplyingCustomCurve = true;
            try
            {
                await RunCurveVerificationKickAsync(
                    sourceLabel: $"Preset '{preset.Name}'",
                    curvePoints: preset.Curve,
                    reapplyCurveAction: () => _fanService.ApplyPreset(preset, ImmediateApplyOnApply));
            }
            catch (Exception ex)
            {
                CurveApplyStatus = $"Preset '{preset.Name}' apply failed: {ex.Message}";
                _logging.Error($"Preset '{preset.Name}' verification kick failed", ex);
            }
            finally
            {
                IsApplyingCustomCurve = false;
            }
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

        private async Task ApplyCustomCurveAsync()
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn("Skipped custom curve apply request because fan diagnostics mode is active");
                return;
            }

            if (!FanCurvesAvailable)
            {
                CurveApplyStatus = "Custom curves unavailable on this model";
                _logging.Warn("Skipped custom curve apply request because this model is limited to OEM fan profiles");
                return;
            }

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
                Name = "Custom",
                Mode = FanMode.Manual,
                Curve = CustomFanCurve.ToList(),
                IsBuiltIn = true
            };

            IsApplyingCustomCurve = true;
            try
            {
                CurveApplyStatus = "Applying custom curve...";
                var applied = await Task.Run(() => _fanService.ApplyPreset(customPreset, immediate: true));
                if (!applied)
                {
                    CurveApplyStatus = "Custom curve failed verification";
                    _logging.Warn("Custom curve was not saved because fan service reported apply failure");
                    return;
                }

                PersistSelectedCustomCurveIfNeeded();
                ActiveFanMode = "Custom";
                OnPropertyChanged(nameof(CurrentFanModeName));

                await RunCurveVerificationKickAsync(
                    sourceLabel: "Custom curve",
                    curvePoints: CustomFanCurve,
                    reapplyCurveAction: () => _fanService.ApplyPreset(customPreset, immediate: true));
            }
            catch (Exception ex)
            {
                CurveApplyStatus = $"Custom curve apply failed: {ex.Message}";
                _logging.ErrorWithContext(
                    component: "FanControlViewModel",
                    operation: "ApplyCustomCurve",
                    message: "Custom curve apply failed",
                    ex: ex);
            }
            finally
            {
                IsApplyingCustomCurve = false;
            }
        }

        private async Task RunCurveVerificationKickAsync(
            string sourceLabel,
            System.Collections.Generic.IEnumerable<FanCurvePoint> curvePoints,
            Action reapplyCurveAction)
        {
            if (_fanVerificationService?.IsAvailable != true)
            {
                CurveApplyStatus = $"{sourceLabel} applied";
                return;
            }

            if (FanTelemetry.Count == 0)
            {
                CurveApplyStatus = $"{sourceLabel} applied; verification skipped (no RPM telemetry)";
                return;
            }

            var controlTemp = Math.Max(CurrentCpuTemperature, CurrentGpuTemperature);
            if (controlTemp <= 0)
                controlTemp = CurrentTemperature;

            var requestedPercent = EvaluateCurvePercent(curvePoints, controlTemp);
            var targetPercent = ApplySafetyFloor(requestedPercent, controlTemp);
            var verificationTargetText = requestedPercent == targetPercent
                ? $"{targetPercent}%"
                : $"{targetPercent}% (curve requested {requestedPercent}%; thermal guard raised it)";

            var verificationKey = $"{sourceLabel}|{targetPercent}";
            var nowUtc = DateTime.UtcNow;
            if (string.Equals(_lastCurveVerificationKickKey, verificationKey, StringComparison.Ordinal) &&
                nowUtc - _lastCurveVerificationKickUtc < CurveVerificationKickCooldown)
            {
                CurveApplyStatus = $"{sourceLabel} applied; verification recently checked";
                _logging.Info($"[FanCurve] Skipped post-apply verification kick for {sourceLabel}; recent check still inside cooldown");
                return;
            }

            _lastCurveVerificationKickKey = verificationKey;
            _lastCurveVerificationKickUtc = nowUtc;

            CurveApplyStatus = $"Verifying {sourceLabel.ToLowerInvariant()} at {controlTemp:F0}C -> {verificationTargetText}...";
            _logging.Info($"[FanCurve] Running post-apply verification kick for {sourceLabel} at {controlTemp:F1}C -> {verificationTargetText}");

            var fanCount = Math.Min(CurveVerificationKickFanLimit, FanTelemetry.Count);
            var results = new System.Collections.Generic.List<FanApplyResult>();

            _fanService.EnterDiagnosticMode();
            using var verificationCts = new CancellationTokenSource(CurveVerificationKickTimeout);
            try
            {
                for (int fanIndex = 0; fanIndex < fanCount; fanIndex++)
                {
                    results.Add(await _fanVerificationService.ApplyAndVerifyFanSpeedAsync(fanIndex, targetPercent, verificationCts.Token));
                }
            }
            catch (OperationCanceledException)
            {
                CurveApplyStatus = $"{sourceLabel} applied; verification timed out";
                _logging.Warn($"[FanCurve] Post-apply verification kick for {sourceLabel} timed out after {CurveVerificationKickTimeout.TotalSeconds:F0}s; curve was restored");
                return;
            }
            finally
            {
                _fanService.ExitDiagnosticMode();
                reapplyCurveAction();
            }

            var passedCount = results.Count(r => r.VerificationPassed);
            if (passedCount == results.Count)
            {
                CurveApplyStatus = results.Count == 1
                    ? $"{sourceLabel} verified at {verificationTargetText} ({results[0].ActualRpmAfter} RPM)"
                    : $"{sourceLabel} verified on both fans at {verificationTargetText}";
            }
            else if (passedCount > 0)
            {
                CurveApplyStatus = $"{sourceLabel} applied; verification partial ({passedCount}/{results.Count} fans passed)";
            }
            else
            {
                var firstFailure = results.FirstOrDefault(r => !r.VerificationPassed)?.ErrorMessage;
                CurveApplyStatus = string.IsNullOrWhiteSpace(firstFailure)
                    ? $"{sourceLabel} applied; verification did not confirm RPM change"
                    : $"{sourceLabel} applied; verification warning: {firstFailure}";
            }
        }

        private static int EvaluateCurvePercent(System.Collections.Generic.IEnumerable<FanCurvePoint> curve, double temp)
        {
            var points = curve.OrderBy(p => p.TemperatureC).ToList();
            if (points.Count == 0)
                return 0;

            if (temp <= points[0].TemperatureC)
                return points[0].FanPercent;

            if (temp >= points[^1].TemperatureC)
                return points[^1].FanPercent;

            for (int i = 0; i < points.Count - 1; i++)
            {
                var left = points[i];
                var right = points[i + 1];
                if (temp < left.TemperatureC || temp > right.TemperatureC)
                    continue;

                var ratio = (temp - left.TemperatureC) / (right.TemperatureC - left.TemperatureC);
                return (int)Math.Round(left.FanPercent + ((right.FanPercent - left.FanPercent) * ratio));
            }

            return points[^1].FanPercent;
        }

        private static int ApplySafetyFloor(int fanPercent, double temp)
        {
            if (temp >= 95)
                return 100;
            if (temp >= 90)
                return Math.Max(fanPercent, 80);
            if (temp >= 85)
                return Math.Max(fanPercent, 60);
            if (temp >= 80)
                return Math.Max(fanPercent, 40);
            return fanPercent;
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
            if (!FanCurvesAvailable)
            {
                _logging.Warn("Skipped custom fan preset save because this model is limited to OEM fan profiles");
                System.Windows.MessageBox.Show(
                    "Custom fan curves are unavailable for this model. Use the OEM fan profiles instead.",
                    "Custom Curves Unavailable",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

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

            // Select and apply the newly saved preset immediately for better UX.
            // Setting SelectedPreset triggers ApplyPreset(value) → ApplyPresetAsync → Task.Run;
            // do NOT call _fanService.ApplyPreset(preset) a second time here — it would block
            // the UI thread AND race with the background apply already in flight.
            SelectedPreset = preset;
            SaveLastPresetToConfig(preset.Name);
            
            // Notify UI that saved presets list changed
            OnPropertyChanged(nameof(SavedCustomPresets));
            OnPropertyChanged(nameof(HasSavedPresets));
            OnPropertyChanged(nameof(CanDeleteSelectedPreset));
            RaisePresetCommandStateChanged();

            _logging.Info($"✓ Saved and applied custom fan preset: '{preset.Name}' with {preset.Curve.Count} points");
        }
        
        private void PersistSelectedCustomCurveIfNeeded()
        {
            if (!FanCurvesAvailable)
            {
                _logging.Warn("Skipped persisting custom fan curve because this model is limited to OEM fan profiles");
                return;
            }

            var curve = CloneCurve(CustomFanCurve);
            var targetPreset = SelectedPreset is { IsBuiltIn: false, Mode: FanMode.Manual }
                ? SelectedPreset
                : FanPresets.FirstOrDefault(p =>
                    !p.IsBuiltIn &&
                    p.Mode == FanMode.Manual &&
                    p.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase));

            if (targetPreset == null)
            {
                targetPreset = new FanPreset
                {
                    Name = "Custom",
                    Mode = FanMode.Manual,
                    IsBuiltIn = false
                };
                FanPresets.Add(targetPreset);
            }

            targetPreset.Curve = curve;

            _suppressApplyOnSelection = true;
            SelectedPreset = targetPreset;
            _suppressApplyOnSelection = false;

            SaveAdHocCustomCurveToConfig(curve, targetPreset.Name);
            SavePresetsToConfig();
            SaveLastPresetToConfig(targetPreset.Name);
            OnPropertyChanged(nameof(SavedCustomPresets));
            OnPropertyChanged(nameof(HasSavedPresets));
            OnPropertyChanged(nameof(CanDeleteSelectedPreset));
            RaisePresetCommandStateChanged();
            _logging.Info($"Updated saved fan preset '{targetPreset.Name}' with {targetPreset.Curve.Count} curve points");
        }

        private void SaveAdHocCustomCurveToConfig(List<FanCurvePoint> curve, string presetName)
        {
            try
            {
                var config = _configService.Load();
                config.CustomFanCurve = CloneCurve(curve);
                config.LastFanPresetName = presetName;
                _configService.Save(config);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save last applied custom fan curve: {ex.Message}");
            }
        }

        private static List<FanCurvePoint> CloneCurve(IEnumerable<FanCurvePoint> points)
        {
            return points
                .Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent })
                .ToList();
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
            SelectedPreset = FanPresets.FirstOrDefault(p => p.Name.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                ?? FanPresets.FirstOrDefault();
            SavePresetsToConfig();
            ClearDeletedPresetConfigState(presetName);
            
            // Notify UI that saved presets list changed
            OnPropertyChanged(nameof(SavedCustomPresets));
            OnPropertyChanged(nameof(HasSavedPresets));
            OnPropertyChanged(nameof(CanDeleteSelectedPreset));
            RaisePresetCommandStateChanged();

            _logging.Info($"🗑️ Deleted custom fan preset: '{presetName}'");
        }

        private void ClearDeletedPresetConfigState(string presetName)
        {
            try
            {
                var config = _configService.Load();
                var deletedWasLastPreset = config.LastFanPresetName?.Equals(presetName, StringComparison.OrdinalIgnoreCase) == true;
                var deletedWasAdHocCustom = presetName.Equals("Custom", StringComparison.OrdinalIgnoreCase);

                if (!deletedWasLastPreset && !deletedWasAdHocCustom)
                {
                    return;
                }

                if (deletedWasLastPreset)
                {
                    config.LastFanPresetName = "Auto";
                }

                config.CustomFanCurve = null;
                _configService.Save(config);
                _logging.Info($"Cleared stale custom fan curve config after deleting '{presetName}'");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to clear deleted fan preset config state: {ex.Message}");
            }
        }

        private void RaisePresetCommandStateChanged()
        {
            (ApplyCustomCurveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SaveCustomPresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteSelectedPresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddCurvePointCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveCurvePointCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ResetCurveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyConstantSpeedCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                _logging.ErrorWithContext(
                    component: "FanControlViewModel",
                    operation: "SavePresetsToConfig",
                    message: "Failed to save fan presets to config",
                    ex: ex);
            }
        }

        private void SaveGpuCurveToConfig()
        {
            try
            {
                var config = _configService.Load();
                config.GpuFanCurve = GpuFanCurve
                    .Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent })
                    .ToList();
                _configService.Save(config);
                _logging.Info($"💾 GPU fan curve saved to config ({config.GpuFanCurve.Count} points)");
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "FanControlViewModel",
                    operation: "SaveGpuCurveToConfig",
                    message: "Failed to save GPU fan curve to config",
                    ex: ex);
            }
        }
        
        private void LoadPresetsFromConfig()
        {
            try
            {
                var config = _configService.Load();

                if (!FanCurvesAvailable)
                {
                    _logging.Info($"Skipped loading {config.FanPresets.Count} custom fan preset(s); this model is limited to OEM fan profiles");
                    return;
                }
                
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
                _logging.ErrorWithContext(
                    component: "FanControlViewModel",
                    operation: "LoadPresetsFromConfig",
                    message: "Failed to load fan presets from config",
                    ex: ex);
            }
        }

        private static List<FanCurvePoint> GetDefaultAutoCurve()
        {
            // v3.7.0: field reports on OMEN 16-xd0xxx need Balanced/Auto to reach max
            // before the high-80s instead of hovering below full cooling.
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 30 },
                new() { TemperatureC = 50, FanPercent = 38 },
                new() { TemperatureC = 60, FanPercent = 50 },
                new() { TemperatureC = 68, FanPercent = 72 },
                new() { TemperatureC = 75, FanPercent = 100 }
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
            // Quiet stays capped; QuietSafetyMonitor owns emergency Max fan override.
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 50, FanPercent = 25 },
                new() { TemperatureC = 65, FanPercent = 38 },
                new() { TemperatureC = 75, FanPercent = 60 },
                new() { TemperatureC = 80, FanPercent = 85 }
            };
        }
        
        private static List<FanCurvePoint> GetGamingCurve()
        {
            // Gaming mode: stronger than Auto, full cooling at 80C for sustained load.
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 30 },
                new() { TemperatureC = 50, FanPercent = 42 },
                new() { TemperatureC = 60, FanPercent = 58 },
                new() { TemperatureC = 70, FanPercent = 72 },
                new() { TemperatureC = 80, FanPercent = 100 }
            };
        }
        
        private static List<FanCurvePoint> GetExtremeCurve()
        {
            // Extreme mode: highest non-Max curve; restored to the long-standing 75C max target.
            return new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 38 },
                new() { TemperatureC = 50, FanPercent = 50 },
                new() { TemperatureC = 60, FanPercent = 66 },
                new() { TemperatureC = 70, FanPercent = 88 },
                new() { TemperatureC = 75, FanPercent = 100 }
            };
        }
        
        private void ApplyGamingMode()
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn("Skipped Gaming fan mode request because fan diagnostics mode is active");
                return;
            }

            var preset = FanPresets.FirstOrDefault(p => p.Name == "Gaming");
            if (preset != null)
            {
                SelectedPreset = preset;
            }
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

            // First, see if it's a custom preset.
            // All fan service calls use Task.Run so the WMI + Thread.Sleep chain runs on a
            // background thread and does not block the WPF UI thread.
            var preset = _configService.Config.FanPresets.FirstOrDefault(p => p.Name.Equals(saved, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                await Task.Run(() => _fanService.ApplyPreset(preset));
                SelectedPreset = FanPresets.FirstOrDefault(p => p.Name == preset.Name) ?? SelectedPreset;
                SaveLastPresetToConfig(preset.Name);
                _logging.Info($"Manually reapplied saved preset: {preset.Name}");
                return;
            }

            // Handle built-in names
            var nameLower = saved.ToLowerInvariant();
            if (FanModeNameResolver.IsMaxAlias(nameLower))
            {
                await Task.Run(() => _fanService.ApplyMaxCooling());
                _logging.Info($"Manually reapplied saved preset: {saved} (Max)");
                return;
            }

            if (FanModeNameResolver.IsAutoAlias(nameLower))
            {
                await Task.Run(() => _fanService.ApplyAutoMode());
                _logging.Info($"Manually reapplied saved preset: {saved} (Auto)");
                return;
            }

            if (FanModeNameResolver.IsQuietAlias(nameLower))
            {
                await Task.Run(() => _fanService.ApplyQuietMode());
                _logging.Info($"Manually reapplied saved preset: {saved} (Quiet)");
                return;
            }

            // Fallback - attempt to apply by name
            var fallback = new FanPreset { Name = saved, Mode = FanMode.Performance };
            await Task.Run(() => _fanService.ApplyPreset(fallback));
            _logging.Info($"Manually reapplied saved preset via fallback: {saved}");
        }
        
        private void ApplyQuietMode()
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn("Skipped Quiet fan mode request because fan diagnostics mode is active");
                return;
            }

            var preset = FanPresets.FirstOrDefault(p => p.Name == "Quiet");
            if (preset != null)
            {
                SelectedPreset = preset;
            }
            _logging.Info("Applied Quiet fan mode");
        }
        
        /// <summary>
        /// Apply constant fan speed mode (OmenMon-style fixed percentage).
        /// Fans are held at ConstantFanPercent regardless of temperature.
        /// </summary>
        private void ApplyConstantSpeed()
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn("Skipped constant fan speed apply request because fan diagnostics mode is active");
                return;
            }

            if (!ManualFanControlAvailable)
            {
                CurveApplyStatus = "Fixed fan speed unavailable on this model";
                _logging.Warn("Skipped constant fan speed apply request because manual fan levels are disabled for this model");
                return;
            }

            // Snapshot the percent now (slider may move again before the Task runs)
            var percent = ConstantFanPercent;

            // Guard against overlapping WMI writes from repeated Apply actions.
            if (_isApplyingConstantSpeed)
                return;

            _isApplyingConstantSpeed = true;
            ActiveFanMode = "Constant";
            _ = Task.Run(() =>
            {
                try
                {
                    _fanService.DisableCurve();
                    _fanService.ForceSetFanSpeed(percent);
                    _logging.Info($"Applied constant fan speed: {percent}% (~{(int)(percent / 100.0 * 5500)} RPM)");
                }
                finally
                {
                    _isApplyingConstantSpeed = false;
                }
            });
        }

        /// <summary>
        /// Apply a fan mode by name (for hotkey integration)
        /// </summary>
        public void ApplyFanMode(string modeName)
        {
            if (_fanService.IsDiagnosticModeActive)
            {
                _logging.Warn($"Skipped quick fan mode '{modeName}' because fan diagnostics mode is active");
                return;
            }

            var preset = FanPresets.FirstOrDefault(p => 
                p.Name.Equals(modeName, System.StringComparison.OrdinalIgnoreCase));
            
            if (preset != null)
            {
                SelectedPreset = preset;
            }
            else
            {
                // Try to match partial names
                if (FanModeNameResolver.IsMaxAlias(modeName))
                {
                    preset = FanPresets.FirstOrDefault(p => p.Name == "Max");
                }
                else if (FanModeNameResolver.IsQuietAlias(modeName))
                {
                    preset = FanPresets.FirstOrDefault(p => p.Name == "Quiet" || p.Name == "Silent")
                        ?? FanPresets.FirstOrDefault(p => p.Name == "Auto");
                }
                else if (FanModeNameResolver.IsAutoAlias(modeName))
                {
                    preset = FanPresets.FirstOrDefault(p => p.Name == "Auto")
                        ?? FanPresets.FirstOrDefault(p => p.Name == "Balanced");
                }
                else if (FanModeNameResolver.IsPerformanceAlias(modeName))
                {
                    // Prefer explicit name match: Gaming → Gaming, Extreme → Extreme.
                    // IsPerformanceAlias lumps both together; separate here to preserve cycle identity.
                    if (modeName.Contains("gaming", System.StringComparison.OrdinalIgnoreCase))
                        preset = FanPresets.FirstOrDefault(p => p.Name == "Gaming")
                            ?? FanPresets.FirstOrDefault(p => p.Name == "Extreme");
                    else
                        preset = FanPresets.FirstOrDefault(p => p.Name == "Extreme")
                            ?? FanPresets.FirstOrDefault(p => p.Name == "Gaming")
                            ?? FanPresets.FirstOrDefault(p => p.Name == "Max");
                }
                else
                {
                    preset = FanPresets.FirstOrDefault(p => p.Name == "Auto");
                }
                
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

    public class FanCalibrationMapRow
    {
        public int DutyPercent { get; set; }
        public string CpuRpmText { get; set; } = "-";
        public string GpuRpmText { get; set; } = "-";
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
