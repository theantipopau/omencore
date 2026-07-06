using System;
using System.Windows.Media;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the simplified General view with paired Performance + Fan profiles.
    /// Each profile combines a performance mode with an optimal fan configuration.
    /// </summary>
    public class GeneralViewModel : ViewModelBase
    {
        private static readonly Brush SelectedProfileBorderBrush = CreateSelectedProfileBorderBrush();

        private readonly FanService _fanService;
        private readonly PerformanceModeService _performanceModeService;
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly SystemInfoService? _systemInfoService;
        private FanControlViewModel? _fanControlViewModel;
        private SystemControlViewModel? _systemControlViewModel;

        private string _currentPerformanceMode = "Default";
        private string _currentFanMode = "Auto";
        private string _selectedProfile = "Balanced";
        private bool _isQuietSafetyOverrideActive;
        private double _cpuTemp;
        private double _gpuTemp;
        private int _cpuFanPercent;
        private int _gpuFanPercent;
        private int _cpuFanRpm;
        private int _gpuFanRpm;
        
        // v2.6.1: Additional telemetry for enhanced General tab
        private double _cpuLoad;
        private double _gpuLoad;
        private double _gpuPowerWatts;
        private double _cpuPowerWatts;
        private TelemetryDataState _cpuPowerState = TelemetryDataState.Unknown;
        private TelemetryDataState _gpuPowerState = TelemetryDataState.Unknown;
        private double _ramUsedGb;
        private double _ramTotalGb;
        private MonitoringSample? _lastProjectedSample;

        private static Brush CreateSelectedProfileBorderBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x5C));
            brush.Freeze();
            return brush;
        }

        private DateTime _lastUiProjectionUtc = DateTime.MinValue;
        private bool _telemetryProjectionEnabled = true;

        private static readonly TimeSpan UiProjectionMinInterval = TimeSpan.FromMilliseconds(750);
        private const double UiProjectionTempDelta = 0.5;
        private const double UiProjectionLoadDelta = 2.0;
        private const double UiProjectionPowerDelta = 1.0;
        private const int UiProjectionFanRpmDelta = 120;

        public GeneralViewModel(
            FanService fanService,
            PerformanceModeService performanceModeService,
            ConfigurationService configService,
            LoggingService logging,
            SystemInfoService? systemInfoService = null)
        {
            _fanService = fanService;
            _performanceModeService = performanceModeService;
            _configService = configService;
            _logging = logging;
            _systemInfoService = systemInfoService;

            // Initial load
            LoadCurrentState();
        }
        
        /// <summary>
        /// Sets the FanControlViewModel reference for syncing preset selections.
        /// Call this after both ViewModels are created.
        /// </summary>
        public void SetFanControlViewModel(FanControlViewModel? fanControlViewModel)
        {
            _fanControlViewModel = fanControlViewModel;
        }
        
        /// <summary>
        /// Sets the SystemControlViewModel reference for syncing performance mode UI.
        /// v2.8.6: Fixes quick profile switching not updating the OMEN tab display.
        /// </summary>
        public void SetSystemControlViewModel(SystemControlViewModel? systemControlViewModel)
        {
            _systemControlViewModel = systemControlViewModel;
        }

        public void SyncRuntimeState(string? performanceMode, string? fanMode)
        {
            if (!string.IsNullOrWhiteSpace(performanceMode))
            {
                CurrentPerformanceMode = performanceMode;
            }

            if (!string.IsNullOrWhiteSpace(fanMode))
            {
                CurrentFanMode = fanMode;
            }

            DetermineActiveProfile(preferSavedPreset: false);
        }
        
        /// <summary>
        /// Returns the brand logo path based on detected system type.
        /// HP Spectre systems get the Spectre logo, others get OMEN logo.
        /// </summary>
        public string BrandLogoPath
        {
            get
            {
                try
                {
                    var sysInfo = _systemInfoService?.GetSystemInfo();
                    if (sysInfo?.IsHpSpectre == true)
                    {
                        return "pack://application:,,,/Assets/spectre.png";
                    }
                }
                catch
                {
                    // Fallback to OMEN logo on any detection failure
                }
                return "pack://application:,,,/Assets/omen.png";
            }
        }

        public void SetTelemetryProjectionEnabled(bool enabled)
        {
            _telemetryProjectionEnabled = enabled;
        }

        #region Properties

        public string CurrentPerformanceMode
        {
            get => _currentPerformanceMode;
            set { _currentPerformanceMode = value; OnPropertyChanged(); }
        }

        public string CurrentFanMode
        {
            get => _currentFanMode;
            set { _currentFanMode = value; OnPropertyChanged(); }
        }

        public string SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); UpdateProfileIndicators(); }
        }

        public double CpuTemp
        {
            get => _cpuTemp;
            set
            {
                var sanitized = double.IsFinite(value) ? Math.Max(0, Math.Min(125, value)) : _cpuTemp;
                _cpuTemp = sanitized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuTempDisplay));
                OnPropertyChanged(nameof(IsCpuTempAvailable));
            }
        }

        public double GpuTemp
        {
            get => _gpuTemp;
            set
            {
                var sanitized = double.IsFinite(value) ? Math.Max(0, Math.Min(125, value)) : _gpuTemp;
                _gpuTemp = sanitized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuTempDisplay));
                OnPropertyChanged(nameof(IsGpuTempAvailable));
            }
        }

        public int CpuFanPercent
        {
            get => _cpuFanPercent;
            set { _cpuFanPercent = value; OnPropertyChanged(); }
        }

        public int GpuFanPercent
        {
            get => _gpuFanPercent;
            set { _gpuFanPercent = value; OnPropertyChanged(); }
        }

        public int CpuFanRpm
        {
            get => _cpuFanRpm;
            set { _cpuFanRpm = value; OnPropertyChanged(); }
        }

        public int GpuFanRpm
        {
            get => _gpuFanRpm;
            set { _gpuFanRpm = value; OnPropertyChanged(); }
        }
        
        // v2.6.1: Additional telemetry properties
        public double CpuLoad
        {
            get => _cpuLoad;
            set
            {
                var sanitized = double.IsFinite(value) ? Math.Max(0, Math.Min(100, value)) : _cpuLoad;
                _cpuLoad = sanitized;
                OnPropertyChanged();
            }
        }
        
        public double GpuLoad
        {
            get => _gpuLoad;
            set
            {
                var sanitized = double.IsFinite(value) ? Math.Max(0, Math.Min(100, value)) : _gpuLoad;
                _gpuLoad = sanitized;
                OnPropertyChanged();
            }
        }
        
        public double GpuPowerWatts
        {
            get => _gpuPowerWatts;
            set
            {
                _gpuPowerWatts = SanitizePowerWatts(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuPowerDisplay));
                OnPropertyChanged(nameof(GpuPowerTooltip));
                OnPropertyChanged(nameof(IsGpuPowerAvailable));
            }
        }
        
        public double CpuPowerWatts
        {
            get => _cpuPowerWatts;
            set
            {
                _cpuPowerWatts = SanitizePowerWatts(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuPowerDisplay));
                OnPropertyChanged(nameof(CpuPowerTooltip));
                OnPropertyChanged(nameof(IsCpuPowerAvailable));
            }
        }

        public TelemetryDataState CpuPowerState
        {
            get => _cpuPowerState;
            private set
            {
                if (_cpuPowerState == value) return;
                _cpuPowerState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuPowerDisplay));
                OnPropertyChanged(nameof(CpuPowerTooltip));
                OnPropertyChanged(nameof(IsCpuPowerAvailable));
            }
        }

        public TelemetryDataState GpuPowerState
        {
            get => _gpuPowerState;
            private set
            {
                if (_gpuPowerState == value) return;
                _gpuPowerState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuPowerDisplay));
                OnPropertyChanged(nameof(GpuPowerTooltip));
                OnPropertyChanged(nameof(IsGpuPowerAvailable));
            }
        }
        
        public double RamUsedGb
        {
            get => _ramUsedGb;
            set { _ramUsedGb = value; OnPropertyChanged(); }
        }
        
        public double RamTotalGb
        {
            get => _ramTotalGb;
            set { _ramTotalGb = value; OnPropertyChanged(); }
        }
        
        public double RamPercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100 : 0;

        // Temperature display helpers — mirror DashboardViewModel for consistent "—°C" treatment
        // across all views. Used by any binding that needs a formatted string rather than a raw double.
        public string CpuTempDisplay => CpuTemp > 0 ? $"{CpuTemp:F0}°C" : "—°C";
        public string GpuTempDisplay => GpuTemp > 0 ? $"{GpuTemp:F0}°C" : "—°C";
        public bool IsCpuTempAvailable => CpuTemp > 0;
        public bool IsGpuTempAvailable => GpuTemp > 0;
        public string CpuPowerDisplay => FormatPowerDisplay(CpuPowerWatts, CpuPowerState);
        public string GpuPowerDisplay => FormatPowerDisplay(GpuPowerWatts, GpuPowerState);
        public bool IsCpuPowerAvailable => HasDisplayablePower(CpuPowerWatts, CpuPowerState);
        public bool IsGpuPowerAvailable => HasDisplayablePower(GpuPowerWatts, GpuPowerState);
        public string CpuPowerTooltip => BuildPowerTooltip("CPU", CpuPowerWatts, CpuPowerState);
        public string GpuPowerTooltip => BuildPowerTooltip("GPU", GpuPowerWatts, GpuPowerState);

        // Profile selection indicators
        public bool IsPerformanceSelected => SelectedProfile == "Performance";
        public bool IsBalancedSelected => SelectedProfile == "Balanced";
        public bool IsQuietSelected => SelectedProfile == "Quiet";
        public bool IsCustomSelected => SelectedProfile == "Custom";

        // Border colors for selection highlighting
        public Brush SelectedProfileBorder_Performance => IsPerformanceSelected ? SelectedProfileBorderBrush : Brushes.Transparent;
        public Brush SelectedProfileBorder_Balanced => IsBalancedSelected ? SelectedProfileBorderBrush : Brushes.Transparent;
        public Brush SelectedProfileBorder_Quiet => IsQuietSelected ? SelectedProfileBorderBrush : Brushes.Transparent;
        public Brush SelectedProfileBorder_Custom => IsCustomSelected ? SelectedProfileBorderBrush : Brushes.Transparent;

        // v3.7.0: Quiet thermal safety override state
        public bool IsQuietSafetyOverrideActive
        {
            get => _isQuietSafetyOverrideActive;
            private set
            {
                if (_isQuietSafetyOverrideActive == value) return;
                _isQuietSafetyOverrideActive = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Raised when the user clicks the Custom profile card and the profile should
        /// navigate to the Custom tab for configuration.
        /// </summary>
        public event EventHandler? CustomTabNavigationRequested;

        #endregion

        #region Profile Application Methods

        /// <summary>
        /// Performance profile: maximum power with an aggressive cooling curve.
        /// </summary>
        public void ApplyPerformanceProfile()
        {
            try
            {
                _logging.Info("Applying Performance profile (Max Power + Performance cooling)");

                // Apply Performance mode
                _performanceModeService.SetPerformanceMode("Performance");

                var coolingPreset = CreatePerformanceCoolingPreset();

                var fanApplied = _fanService.ApplyPreset(coolingPreset, immediate: true);
                if (fanApplied)
                {
                    _fanControlViewModel?.SelectPresetByNameNoApplyAndSave(coolingPreset.Name);
                }
                
                // v2.8.6: Sync OMEN tab performance mode display; v3.8.1: also persist (GitHub #145)
                _systemControlViewModel?.SelectModeByNameNoApplyAndSave("Performance");

                // Set GPU Power Boost to Maximum for Performance profile
                if (_systemControlViewModel?.GpuPowerBoostAvailable == true)
                    _systemControlViewModel.GpuPowerBoostLevel = "Maximum";

                SyncFromConfirmedRuntime("Performance profile");

                _logging.Info("Performance profile applied successfully");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply Performance profile: {ex.Message}");
            }
        }

        private static FanPreset CreatePerformanceCoolingPreset()
        {
            return new FanPreset
            {
                Name = "Performance",
                Mode = FanMode.Performance,
                IsBuiltIn = true,
                Curve = new()
                {
                    new FanCurvePoint { TemperatureC = 40, FanPercent = 38 },
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 52 },
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 72 },
                    new FanCurvePoint { TemperatureC = 70, FanPercent = 100 }
                }
            };
        }

        private void SyncFromConfirmedRuntime(string context)
        {
            var confirmedPerformanceMode = _performanceModeService.GetCurrentMode()
                ?? CurrentPerformanceMode;
            var confirmedFanMode = _fanService.GetCurrentFanMode()
                ?? CurrentFanMode;

            CurrentPerformanceMode = confirmedPerformanceMode;
            CurrentFanMode = confirmedFanMode;
            DetermineActiveProfile(preferSavedPreset: false);
            _logging.Info($"{context}: confirmed Performance='{confirmedPerformanceMode}', Fan='{confirmedFanMode}', GeneralProfile='{SelectedProfile}'");
        }

        /// <summary>
        /// Balanced profile: Default power + auto fans
        /// </summary>
        public void ApplyBalancedProfile()
        {
            try
            {
                _logging.Info("Applying Balanced profile (Default Power + Auto Cooling)");

                _performanceModeService.SetPerformanceMode("Default");

                var coolingPreset = _fanControlViewModel?.FanPresets.FirstOrDefault(p =>
                    p.Name.Equals("Auto", StringComparison.OrdinalIgnoreCase));
                var fanApplied = coolingPreset != null
                    ? _fanService.ApplyPreset(coolingPreset, immediate: true)
                    : false;

                if (fanApplied)
                {
                    _fanControlViewModel?.SelectPresetByNameNoApplyAndSave("Auto");
                }
                else
                {
                    _fanService.ApplyAutoMode();
                }
                
                // v2.8.6: Sync OMEN tab performance mode display; v3.8.1: also persist (GitHub #145)
                _systemControlViewModel?.SelectModeByNameNoApplyAndSave("Balanced");

                // Set GPU Power Boost to Medium for Balanced profile
                if (_systemControlViewModel?.GpuPowerBoostAvailable == true)
                    _systemControlViewModel.GpuPowerBoostLevel = "Medium";

                SyncFromConfirmedRuntime("Balanced profile");

                _logging.Info("Balanced profile applied successfully");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply Balanced profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Quiet profile: Power saver + silent fans
        /// </summary>
        public void ApplyQuietProfile()
        {
            try
            {
                _logging.Info("Applying Quiet profile (Power Saver + Silent Cooling)");

                _performanceModeService.SetPerformanceMode("Quiet");

                var coolingPreset = _fanControlViewModel?.FanPresets.FirstOrDefault(p =>
                    p.Name.Equals("Quiet", StringComparison.OrdinalIgnoreCase));
                var fanApplied = coolingPreset != null
                    ? _fanService.ApplyPreset(coolingPreset, immediate: true)
                    : false;

                if (fanApplied)
                {
                    _fanControlViewModel?.SelectPresetByNameNoApplyAndSave("Quiet");
                }
                else
                {
                    _fanService.ApplyQuietMode();
                }
                
                // v2.8.6: Sync OMEN tab performance mode display; v3.8.1: also persist (GitHub #145)
                _systemControlViewModel?.SelectModeByNameNoApplyAndSave("Quiet");

                // Set GPU Power Boost to Minimum for Quiet profile
                if (_systemControlViewModel?.GpuPowerBoostAvailable == true)
                    _systemControlViewModel.GpuPowerBoostLevel = "Minimum";

                SyncFromConfirmedRuntime("Quiet profile");

                _logging.Info("Quiet profile applied successfully");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply Quiet profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Custom profile: User-defined settings (redirect to Custom tab, re-apply last preset)
        /// </summary>
        public void ApplyCustomProfile()
        {
            try
            {
                _logging.Info("Custom profile selected — navigating to Custom tab for manual control");
                SelectedProfile = "Custom";
                CustomTabNavigationRequested?.Invoke(this, EventArgs.Empty);

                // Re-apply the last custom preset so the fan state is restored to what the user
                // last configured (avoids a "dead" click that only navigates without changing fans).
                var lastPresetName = _configService.Config?.LastFanPresetName;
                if (!string.IsNullOrEmpty(lastPresetName) &&
                    FanModeNameResolver.ResolveGeneralProfileFromPresetName(lastPresetName) == "Custom")
                {
                    var preset = _fanControlViewModel?.FanPresets.FirstOrDefault(p =>
                        string.Equals(p.Name, lastPresetName, StringComparison.OrdinalIgnoreCase));
                    if (preset != null)
                    {
                        var fanApplied = _fanService.ApplyPreset(preset, immediate: true);
                        if (fanApplied)
                            _fanControlViewModel?.SelectPresetByNameNoApply(preset.Name);
                        _logging.Info($"Custom profile: re-applied last preset '{preset.Name}' (success={fanApplied})");
                    }
                    else
                    {
                        _logging.Debug($"Custom profile: last preset '{lastPresetName}' not found in FanPresets — navigate only");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set Custom profile: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        // v3.7.0: Called by MainViewModel to reflect safety override state in UI
        internal void SetQuietSafetyOverride(bool active)
        {
            IsQuietSafetyOverrideActive = active;
        }

        private void LoadCurrentState()
        {
            try
            {
                // Load current performance mode
                var perfMode = _performanceModeService.GetCurrentMode();
                CurrentPerformanceMode = perfMode ?? "Default";

                // Load current fan mode
                var fanMode = _fanService.GetCurrentFanMode();
                CurrentFanMode = fanMode ?? "Auto";

                // Determine which profile matches current state
                DetermineActiveProfile();

                // Update telemetry
                UpdateTelemetry();
                UpdateTemperatures();
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to load current state: {ex.Message}");
            }
        }

        private void DetermineActiveProfile(bool preferSavedPreset = true)
        {
            // First try to match using the saved fan preset name from config
            var savedPreset = _configService.Config?.LastFanPresetName;
            if (preferSavedPreset && !string.IsNullOrEmpty(savedPreset))
            {
                SelectedProfile = FanModeNameResolver.ResolveGeneralProfileFromPresetName(savedPreset);
                if (SelectedProfile != "Custom")
                    return;
            }
            
            var performanceMode = CurrentPerformanceMode?.Trim();
            var fanMode = CurrentFanMode?.Trim();

            // Fallback: Match current state to a profile
            if (PerformanceModeNameResolver.IsPerformanceAlias(performanceMode) &&
                (FanModeNameResolver.IsPerformanceAlias(fanMode) || FanModeNameResolver.IsMaxAlias(fanMode)))
            {
                SelectedProfile = "Performance";
            }
            else if (PerformanceModeNameResolver.IsQuietAlias(performanceMode) &&
                     FanModeNameResolver.IsQuietAlias(fanMode))
            {
                SelectedProfile = "Quiet";
            }
            else if (PerformanceModeNameResolver.IsBalancedAlias(performanceMode) &&
                     FanModeNameResolver.IsAutoAlias(fanMode))
            {
                SelectedProfile = "Balanced";
            }
            else
            {
                SelectedProfile = "Custom";
            }
        }

        private void UpdateProfileIndicators()
        {
            OnPropertyChanged(nameof(IsPerformanceSelected));
            OnPropertyChanged(nameof(IsBalancedSelected));
            OnPropertyChanged(nameof(IsQuietSelected));
            OnPropertyChanged(nameof(IsCustomSelected));
            OnPropertyChanged(nameof(SelectedProfileBorder_Performance));
            OnPropertyChanged(nameof(SelectedProfileBorder_Balanced));
            OnPropertyChanged(nameof(SelectedProfileBorder_Quiet));
            OnPropertyChanged(nameof(SelectedProfileBorder_Custom));
        }

        private void UpdateTelemetry()
        {
            try
            {
                var telemetry = _fanService.FanTelemetry;
                if (telemetry.Count >= 1)
                {
                    CpuFanPercent = telemetry[0].DutyCyclePercent;
                    CpuFanRpm = telemetry[0].SpeedRpm;
                }
                if (telemetry.Count >= 2)
                {
                    GpuFanPercent = telemetry[1].DutyCyclePercent;
                    GpuFanRpm = telemetry[1].SpeedRpm;
                }
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"[GeneralVM] Fan speed update error: {ex.Message}"); 
            }
        }

        private void UpdateTemperatures()
        {
            try
            {
                var samples = _fanService.ThermalSamples;
                if (samples.Count > 0)
                {
                    var latest = samples[^1];
                    CpuTemp = latest.CpuCelsius;
                    GpuTemp = latest.GpuCelsius;
                }
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"[GeneralVM] Temperature update error: {ex.Message}"); 
            }
        }
        
        /// <summary>
        /// v2.6.1: Update from MainViewModel's monitoring sample for enhanced telemetry
        /// </summary>
        public void UpdateFromMonitoringSample(MonitoringSample? sample)
        {
            if (sample == null) return;
            RuntimeUiPerformanceCounters.RecordGeneralSampleReceived();
            if (!_telemetryProjectionEnabled)
            {
                RuntimeUiPerformanceCounters.RecordGeneralSampleSkipped();
                   // v3.6.2: Track hidden-surface suppression
                   RuntimeUiPerformanceCounters.RecordHiddenSurfaceSampleSkipped();
                return;
            }

            if (!ShouldProjectMonitoringSample(sample))
            {
                RuntimeUiPerformanceCounters.RecordGeneralSampleSkipped();
                return;
            }
            
            try
            {
                if (sample.CpuTemperatureState == TelemetryDataState.Valid ||
                    sample.CpuTemperatureState == TelemetryDataState.Stale)
                {
                    CpuTemp = sample.CpuTemperatureC;
                }

                if (sample.GpuTemperatureState == TelemetryDataState.Valid ||
                    sample.GpuTemperatureState == TelemetryDataState.Stale ||
                    sample.GpuTemperatureState == TelemetryDataState.Inactive)
                {
                    GpuTemp = sample.GpuTemperatureC;
                }

                CpuLoad = sample.CpuLoadPercent;
                GpuLoad = sample.GpuLoadPercent;
                CpuPowerState = sample.CpuPowerState;
                GpuPowerState = InferGpuPowerState(sample);
                GpuPowerWatts = sample.GpuPowerWatts;
                CpuPowerWatts = sample.CpuPowerWatts;
                RamUsedGb = sample.RamUsageGb;
                RamTotalGb = sample.RamTotalGb;

                if (sample.Fan1RpmState == TelemetryDataState.Valid ||
                    sample.Fan1RpmState == TelemetryDataState.Stale)
                {
                    CpuFanRpm = sample.Fan1Rpm;
                    CpuFanPercent = EstimateFanPercent(sample.Fan1Rpm);
                }

                if (sample.Fan2RpmState == TelemetryDataState.Valid ||
                    sample.Fan2RpmState == TelemetryDataState.Stale)
                {
                    GpuFanRpm = sample.Fan2Rpm;
                    GpuFanPercent = sample.GpuFanPercent > 0
                        ? (int)Math.Clamp(Math.Round(sample.GpuFanPercent), 0, 100)
                        : EstimateFanPercent(sample.Fan2Rpm);
                }

                OnPropertyChanged(nameof(RamPercent));
                _lastProjectedSample = new MonitoringSample(sample);
                _lastUiProjectionUtc = DateTime.UtcNow;
                RuntimeUiPerformanceCounters.RecordGeneralSampleProjected();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneralVM] Monitoring sample update error: {ex.Message}");
            }
        }

        private bool ShouldProjectMonitoringSample(MonitoringSample sample)
        {
            if (_lastProjectedSample == null)
            {
                return true;
            }

            var elapsed = DateTime.UtcNow - _lastUiProjectionUtc;
            if (elapsed >= UiProjectionMinInterval)
            {
                return true;
            }

            var previous = _lastProjectedSample;

            return Math.Abs(sample.CpuTemperatureC - previous.CpuTemperatureC) >= UiProjectionTempDelta
                || Math.Abs(sample.GpuTemperatureC - previous.GpuTemperatureC) >= UiProjectionTempDelta
                || Math.Abs(sample.CpuLoadPercent - previous.CpuLoadPercent) >= UiProjectionLoadDelta
                || Math.Abs(sample.GpuLoadPercent - previous.GpuLoadPercent) >= UiProjectionLoadDelta
                || Math.Abs(sample.CpuPowerWatts - previous.CpuPowerWatts) >= UiProjectionPowerDelta
                || Math.Abs(sample.GpuPowerWatts - previous.GpuPowerWatts) >= UiProjectionPowerDelta
                || sample.CpuPowerState != previous.CpuPowerState
                || Math.Abs(sample.Fan1Rpm - previous.Fan1Rpm) >= UiProjectionFanRpmDelta
                || Math.Abs(sample.Fan2Rpm - previous.Fan2Rpm) >= UiProjectionFanRpmDelta
                || sample.CpuTemperatureState != previous.CpuTemperatureState
                || sample.GpuTemperatureState != previous.GpuTemperatureState
                || sample.Fan1RpmState != previous.Fan1RpmState
                || sample.Fan2RpmState != previous.Fan2RpmState;
        }

        private static int EstimateFanPercent(int rpm)
        {
            if (rpm <= 0) return 0;
            if (rpm >= 5500) return 100;
            return Math.Clamp((int)Math.Round(rpm / 55.0), 0, 100);
        }

        private static double SanitizePowerWatts(double watts)
        {
            if (!double.IsFinite(watts) || watts < 0)
                return 0;

            return watts;
        }

        private static bool HasDisplayablePower(double watts, TelemetryDataState state)
        {
            if (!double.IsFinite(watts) || watts <= 0)
                return false;

            return state is not TelemetryDataState.Unavailable and not TelemetryDataState.Invalid;
        }

        private static string FormatPowerDisplay(double watts, TelemetryDataState state)
        {
            return HasDisplayablePower(watts, state)
                ? $"{watts:F0}W"
                : "--W";
        }

        private static string BuildPowerTooltip(string label, double watts, TelemetryDataState state)
        {
            if (HasDisplayablePower(watts, state))
            {
                return $"{label} package power from monitoring telemetry.";
            }

            return state switch
            {
                TelemetryDataState.Stale => $"{label} power telemetry is stale.",
                TelemetryDataState.Invalid => $"{label} power telemetry returned an invalid value.",
                TelemetryDataState.Unavailable => $"{label} power sensor is unavailable on this backend.",
                TelemetryDataState.Inactive => $"{label} power telemetry is inactive.",
                TelemetryDataState.Zero => $"{label} power sensor returned 0W; hiding it as unavailable.",
                _ => $"{label} power telemetry is not available yet."
            };
        }

        private static TelemetryDataState InferGpuPowerState(MonitoringSample sample)
        {
            if (sample.GpuTemperatureState == TelemetryDataState.Inactive)
                return TelemetryDataState.Inactive;

            if (!double.IsFinite(sample.GpuPowerWatts) || sample.GpuPowerWatts < 0)
                return TelemetryDataState.Invalid;

            return sample.GpuPowerWatts > 0
                ? TelemetryDataState.Valid
                : TelemetryDataState.Unknown;
        }

        #endregion
    }
}
