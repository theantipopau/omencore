using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Fan service that implements continuous fan curve monitoring like OmenMon.
    /// 
    /// Key differences from v1.2.x:
    /// 1. Continuous curve application: Every CurveUpdateIntervalMs, reads temps and applies curve
    /// 2. Adaptive polling: Longer intervals when temps are stable, shorter when changing
    /// 3. Separate monitoring vs control loops to reduce DPC latency
    /// 4. Hysteresis support to prevent fan oscillation
    /// </summary>
    public class FanService : IDisposable
    {
        private readonly IFanController _fanController;
        private readonly ThermalSensorProvider _thermalProvider;
        private readonly LoggingService _logging;
        private readonly NotificationService _notificationService;
        private readonly TimeSpan _monitorPollPeriod;
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private readonly ObservableCollection<FanTelemetry> _fanTelemetry = new();
        private CancellationTokenSource? _cts;

        // Active fan curves for continuous application (OmenMon-style)
        // Now supports separate CPU and GPU curves for independent fan control
        private List<FanCurvePoint>? _activeCurve;      // Legacy: unified curve (maps to both)
        private List<FanCurvePoint>? _cpuCurve;         // CPU-specific curve
        private List<FanCurvePoint>? _gpuCurve;         // GPU-specific curve
        private bool _independentCurvesEnabled = false; // When true, use separate curves
        private FanPreset? _activePreset;
        private bool _curveEnabled = false;
        private readonly object _curveLock = new();
        
        /// <summary>
        /// Expose ThermalSensorProvider for OSD and other services that need temperature data.
        /// </summary>
        public ThermalSensorProvider ThermalProvider => _thermalProvider;
        
        // Curve update timing - more aggressive for responsive fan control
        // v2.7.0: Reduced intervals to combat BIOS fan reversion on OMEN 16/Max models
        private const int CurveUpdateIntervalMs = 5000;  // 5 seconds between curve updates (reduced from 10)
        private const int CurveForceRefreshMs = 30000;   // Force re-apply every 30 seconds (reduced from 60)
        private const int MonitorMinIntervalMs = 1000;   // 1 second minimum for UI updates
        private const int MonitorMaxIntervalMs = 5000;   // 5 seconds when temps stable
        private DateTime _lastCurveUpdate = DateTime.MinValue;
        private DateTime _lastCurveForceRefresh = DateTime.MinValue;
        private int _lastAppliedFanPercent = -1;
        private int _lastAppliedCpuFanPercent = -1;      // For independent curves
        private int _lastAppliedGpuFanPercent = -1;      // For independent curves

        // Smoothing / transition settings (configurable)
        private bool _smoothingEnabled = true;
        private int _smoothingDurationMs = 1000;
        private int _smoothingStepMs = 200;

        // Expose for tests and read-only inspection
        public bool SmoothingEnabled => _smoothingEnabled;
        public int SmoothingDurationMs => _smoothingDurationMs;
        public int SmoothingStepMs => _smoothingStepMs;
        
        // Adaptive polling - reduce DPC latency by polling less when stable
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private int _stableReadings = 0;
        private const int StableThreshold = 3; // Number of stable readings before slowing down
        private const double TempChangeThreshold = 3.0; // °C change to trigger faster polling
        
        // Fan telemetry change detection - reduce UI churn by only updating on meaningful change
        private List<int> _lastFanSpeeds = new();
        private const int FanSpeedChangeThreshold = 50; // RPM change to trigger UI update
        
        // Thermal protection - override Auto mode when temps get too high
        // v2.8.0: Raised thresholds — 80°C/85°C was too aggressive for gaming laptops
        // that routinely hit 85°C under heavy load. Users reported constant fan ramp-ups
        // on Silent mode. Modern laptop CPUs throttle at 95-100°C; 85°C is normal.
        // v2.8.0: Added time-based debounce to prevent fan yo-yo on CPUs that briefly spike
        private double _thermalProtectionThreshold = 90.0;      // °C - start ramping fans (configurable)
        private const double ThermalEmergencyThreshold = 95.0;  // Emergency 100% - actual danger zone
        private const double ThermalSafeReleaseTemp = 65.0;     // Temps below this are truly safe for release
        private const int ThermalReleaseMinFanPercent = 40;     // Min fan on thermal release to prevent yo-yo
        private const double ThermalReleaseHysteresis = 10.0;   // °C below threshold to release (was 5°C, too tight)
        private volatile bool _thermalProtectionActive = false;
        
        // Debounce timers — prevent rapid activate/deactivate cycling
        private DateTime _thermalAboveThresholdSince = DateTime.MinValue;  // When temp first exceeded threshold
        private DateTime _thermalBelowReleaseSince = DateTime.MinValue;    // When temp first dropped below release
        private const double ThermalActivateDebounceSeconds = 5.0;  // Must stay above threshold for 5s to activate
        private const double ThermalReleaseDebounceSeconds = 15.0;  // Must stay below release for 15s to deactivate
        
        // Diagnostic mode - suspends curve engine to allow manual fan testing
        private volatile bool _diagnosticModeActive = false;
        
        // Fan level range note: HP WMI uses 0-55 (krpm) on classic models or 0-100 (percentage) on newer.
        // Actual conversion is handled by WmiFanController which auto-detects the max level.
        private bool _thermalProtectionEnabled = true; // Can be disabled in settings
        
        // Hysteresis state
        private FanHysteresisSettings _hysteresis = new();
        private double _lastHysteresisTemp = 0;
        private DateTime _lastFanChangeRequest = DateTime.MinValue;
        private int _pendingFanPercent = -1;
        private bool _pendingIncrease = false;

        // GPU power boost integration - adjust fan curves based on GPU power level
        private string _gpuPowerBoostLevel = "Medium";
        public string GpuPowerBoostLevel
        {
            get => _gpuPowerBoostLevel;
            set
            {
                if (_gpuPowerBoostLevel != value)
                {
                    _gpuPowerBoostLevel = value;
                    _logging.Info($"GPU Power Boost level updated to: {_gpuPowerBoostLevel} - adjusting fan curves accordingly");
                    // Force a curve re-evaluation on next cycle
                    _lastCurveUpdate = DateTime.MinValue;
                }
            }
        }

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }
        
        /// <summary>
        /// The backend being used for fan control (WMI BIOS, EC, or None).
        /// </summary>
        public string Backend => _fanController.Backend;
        
        /// <summary>
        /// Whether a custom fan curve is actively being applied.
        /// Thread-safe read of curve state.
        /// </summary>
        public bool IsCurveActive
        {
            get
            {
                lock (_curveLock)
                {
                    return _curveEnabled && (_activeCurve != null || (_cpuCurve != null && _gpuCurve != null));
                }
            }
        }
        
        /// <summary>
        /// Whether independent CPU/GPU curves are being used.
        /// </summary>
        public bool IndependentCurvesEnabled => _independentCurvesEnabled;
        
        /// <summary>
        /// The currently active preset name, if any.
        /// </summary>
        public string? ActivePresetName => _activePreset?.Name;
        
        /// <summary>
        /// The currently active preset (for diagnostic mode restoration).
        /// </summary>
        public FanPreset? ActivePreset => _activePreset;
        
        /// <summary>
        /// Whether thermal protection is currently overriding fan control.
        /// </summary>
        public bool IsThermalProtectionActive => _thermalProtectionActive;
        
        /// <summary>
        /// Whether diagnostic mode is active (suspends curve engine for manual testing).
        /// </summary>
        public bool IsDiagnosticModeActive => _diagnosticModeActive;
        
        /// <summary>
        /// Enter diagnostic mode - suspends curve engine to allow manual fan testing.
        /// Call ExitDiagnosticMode() when done to resume normal operation.
        /// </summary>
        public void EnterDiagnosticMode()
        {
            _diagnosticModeActive = true;
            _logging.Info("🔧 Entered fan diagnostic mode - curve engine suspended");
        }
        
        /// <summary>
        /// Exit diagnostic mode - resumes normal curve engine operation.
        /// </summary>
        public void ExitDiagnosticMode()
        {
            _diagnosticModeActive = false;
            _logging.Info("✓ Exited fan diagnostic mode - curve engine resumed");
        }

        /// <summary>
        /// Run a Max verification using the underlying fan controller and return the result and details.
        /// </summary>
        public (bool success, string details) VerifyMaxApplied()
        {
            try
            {
                if (_fanController == null)
                    return (false, "No fan controller available");

                var ok = _fanController.VerifyMaxApplied(out string details);
                return (ok, details);
            }
            catch (Exception ex)
            {
                return (false, $"VerifyMaxApplied exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Event raised when a preset is applied (for UI synchronization).
        /// </summary>
        public event EventHandler<string>? PresetApplied;
        
        /// <summary>
        /// Enable/disable thermal protection override.
        /// When enabled, fans will ramp to max if temps exceed 90°C, even in Auto mode.
        /// </summary>
        public bool ThermalProtectionEnabled
        {
            get => _thermalProtectionEnabled;
            set
            {
                _thermalProtectionEnabled = value;
                _logging.Info($"Thermal protection: {(value ? "Enabled" : "Disabled")}");
            }
        }
        
        /// <summary>
        /// Configure hysteresis settings to prevent fan oscillation.
        /// Also loads thermal protection threshold from settings.
        /// </summary>
        public void SetHysteresis(FanHysteresisSettings settings)
        {
            _hysteresis = settings ?? new FanHysteresisSettings();
            _thermalProtectionEnabled = _hysteresis.ThermalProtectionEnabled;
            
            // Load configurable thermal protection threshold, clamp to safe range
            // v2.8.0: Widened range — previous max of 90°C was too restrictive for
            // high-power laptops where 85°C is normal gaming temp
            _thermalProtectionThreshold = Math.Clamp(settings?.ThermalProtectionThreshold ?? 90.0, 75.0, 95.0);
            
            _logging.Info($"Fan hysteresis: {(_hysteresis.Enabled ? $"Enabled (deadzone={_hysteresis.DeadZone}°C, ramp↑={_hysteresis.RampUpDelay}s, ramp↓={_hysteresis.RampDownDelay}s)" : "Disabled")}");
            _logging.Info($"Thermal protection: {(_thermalProtectionEnabled ? $"Enabled at {_thermalProtectionThreshold}°C" : "Disabled")}");
        }

        /// <summary>
        /// Create FanService with the new IFanController interface.
        /// </summary>
        public FanService(IFanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, NotificationService notificationService, int pollMs)
        {
            _fanController = controller;
            _thermalProvider = thermalProvider;
            _logging = logging;
            _notificationService = notificationService;
            _monitorPollPeriod = TimeSpan.FromMilliseconds(Math.Max(MonitorMinIntervalMs, pollMs));
            ThermalSamples = new ReadOnlyObservableCollection<ThermalSample>(_thermalSamples);
            FanTelemetry = new ReadOnlyObservableCollection<FanTelemetry>(_fanTelemetry);
            
            _logging.Info($"FanService initialized with backend: {Backend}, curve interval: {CurveUpdateIntervalMs}ms");
        }

        /// <summary>
        /// Legacy constructor for compatibility with existing FanController.
        /// </summary>
        public FanService(FanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, NotificationService notificationService, int pollMs)
            : this(new EcFanControllerWrapper(controller, null!, logging), thermalProvider, logging, notificationService, pollMs)
        {
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            
            // Immediately populate fan telemetry so UI shows RPM right away
            try
            {
                var fanSpeeds = _fanController.ReadFanSpeeds().ToList();
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    _fanTelemetry.Clear();
                    foreach (var fan in fanSpeeds)
                    {
                        _fanTelemetry.Add(fan);
                    }
                    _lastFanSpeeds = fanSpeeds.Select(f => f.Rpm).ToList();
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not read initial fan speeds: {ex.Message}");
            }
            
            _ = Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        /// <summary>
        /// Apply a preset and start continuous curve monitoring if it has a curve.
        /// </summary>
        public void ApplyPreset(FanPreset preset, bool immediate = false)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn($"Fan preset '{preset.Name}' skipped; fan control unavailable ({_fanController.Status})");
                return;
            }
            
            // Apply the preset's thermal policy first
            if (_fanController.ApplyPreset(preset))
            {
                _logging.Info($"Fan preset '{preset.Name}' applied via {Backend}");
                
                // Update current fan mode based on preset
                var nameLower = preset.Name.ToLowerInvariant();
                if (nameLower.Contains("max") && !nameLower.Contains("extreme"))
                    _currentFanMode = "Max";
                else if (nameLower.Contains("extreme"))
                    _currentFanMode = "Extreme";
                else if (nameLower.Contains("auto") || nameLower.Contains("default") || nameLower.Contains("balanced"))
                    _currentFanMode = "Auto";
                else if (nameLower.Contains("quiet") || nameLower.Contains("silent"))
                    _currentFanMode = "Quiet";
                else
                    _currentFanMode = preset.Name; // Use preset name for custom presets
                
                bool isMaxPreset = nameLower.Contains("max") && !nameLower.Contains("auto");
                
                if (isMaxPreset)
                {
                    // Max preset: Apply max cooling mode
                    ApplyMaxCooling();
                    _activePreset = preset;
                }
                else if (nameLower.Contains("auto") || nameLower.Contains("default"))
                {
                    // Auto/Default mode: Let BIOS control fans completely
                    // Don't apply any curve - this allows fans to stop at idle temps
                    DisableCurve();
                    _activePreset = preset;
                    
                    // Restore BIOS auto control - this resets fan levels to 0 and 
                    // sets FanMode.Default so BIOS can stop fans at idle temperatures
                    _fanController.RestoreAutoControl();
                    
                    _logging.Info($"✓ Preset '{preset.Name}' using BIOS auto control (fans can stop at idle)");
                }
                else if (preset.Curve != null && preset.Curve.Any())
                {
                    // Enable continuous curve application for custom/performance presets
                    EnableCurve(preset.Curve.ToList(), preset);
                    _logging.Info($"✓ Preset '{preset.Name}' curve enabled with {preset.Curve.Count} points");

                    if (immediate)
                    {
                        // Perform an immediate curve application based on current temps
                        var temps = _thermalProvider.ReadTemperatures().ToList();
                        var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                        var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                        // Fire-and-forget: force apply now without blocking caller
                        _ = ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);
                    }
                }
                else
                {
                    // No curve defined - use BIOS defaults
                    DisableCurve();
                    _activePreset = preset;
                    _logging.Info($"Preset '{preset.Name}' using BIOS control (no curve defined)");
                }
                
                // Raise event for UI synchronization (sidebar, tray, etc.)
                PresetApplied?.Invoke(this, preset.Name);
            }
            else
            {
                _logging.Warn($"Fan preset '{preset.Name}' failed to apply via {Backend}");
            }
        }

        /// <summary>
        /// Apply a custom curve and start continuous monitoring.
        /// </summary>
        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve, bool immediate = false)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn($"Custom fan curve skipped; fan control unavailable ({_fanController.Status})");
                return;
            }
            
            var curveList = curve.ToList();
            if (!ValidateCurve(curveList, out var validationError))
            {
                _logging.Warn($"Custom fan curve rejected: {validationError}");
                return;
            }
            
            // Apply once immediately, then enable continuous monitoring
            if (_fanController.ApplyCustomCurve(curveList))
            {
                EnableCurve(curveList, null);
                _logging.Info($"Custom fan curve applied and enabled with {curveList.Count} points");

                if (immediate)
                {
                    // Apply curve immediately based on current temps
                    var temps = _thermalProvider.ReadTemperatures().ToList();
                    var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                    var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                    _ = ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true);
                }
            }
            else
            {
                _logging.Warn($"Custom fan curve failed to apply via {Backend}");
            }
        }
        
        /// <summary>
        /// Enable continuous curve application.
        /// </summary>
        private void EnableCurve(List<FanCurvePoint> curve, FanPreset? preset)
        {
            lock (_curveLock)
            {
                _activeCurve = curve.OrderBy(p => p.TemperatureC).ToList();
                _activePreset = preset;
                _curveEnabled = true;
                _lastCurveUpdate = DateTime.MinValue; // Force immediate update
                _lastAppliedFanPercent = -1;
            }
        }
        
        /// <summary>
        /// Disable continuous curve application.
        /// </summary>
        public void DisableCurve()
        {
            lock (_curveLock)
            {
                _curveEnabled = false;
                _activeCurve = null;
                _cpuCurve = null;
                _gpuCurve = null;
                _independentCurvesEnabled = false;
                _lastAppliedFanPercent = -1;
                _lastAppliedCpuFanPercent = -1;
                _lastAppliedGpuFanPercent = -1;
            }
        }
        
        /// <summary>
        /// Enable independent CPU and GPU fan curves.
        /// </summary>
        /// <param name="cpuCurve">The fan curve to use for CPU temperature</param>
        /// <param name="gpuCurve">The fan curve to use for GPU temperature</param>
        public void EnableIndependentCurves(List<FanCurvePoint> cpuCurve, List<FanCurvePoint> gpuCurve)
        {
            if (!ValidateCurve(cpuCurve, out var cpuError))
            {
                _logging.Warn($"CPU curve rejected: {cpuError}");
                return;
            }
            if (!ValidateCurve(gpuCurve, out var gpuError))
            {
                _logging.Warn($"GPU curve rejected: {gpuError}");
                return;
            }

            lock (_curveLock)
            {
                _cpuCurve = cpuCurve.OrderBy(p => p.TemperatureC).ToList();
                _gpuCurve = gpuCurve.OrderBy(p => p.TemperatureC).ToList();
                _activeCurve = null; // Clear single curve mode
                _activePreset = null;
                _curveEnabled = true;
                _independentCurvesEnabled = true;
                _lastCurveUpdate = DateTime.MinValue; // Force immediate update
                _lastAppliedCpuFanPercent = -1;
                _lastAppliedGpuFanPercent = -1;
            }
            
            _logging.Info($"Independent fan curves enabled - CPU: {_cpuCurve.Count} points, GPU: {_gpuCurve.Count} points");
        }

        /// <summary>
        /// Validate that a fan curve is monotonic in temperature and within safe percentage bounds.
        /// </summary>
        private bool ValidateCurve(IReadOnlyList<FanCurvePoint> curve, out string error)
        {
            error = string.Empty;

            if (curve.Count < 2)
            {
                error = "Curve needs at least 2 points";
                return false;
            }

            int lastTemp = int.MinValue;
            foreach (var point in curve)
            {
                if (point.TemperatureC < lastTemp)
                {
                    error = "Temperatures must be non-decreasing";
                    return false;
                }
                if (point.FanPercent < 0 || point.FanPercent > 100)
                {
                    error = "Fan % must stay between 0 and 100";
                    return false;
                }
                lastTemp = point.TemperatureC;
            }

            return true;
        }
        
        /// <summary>
        /// Apply safety bounds clamping to prevent dangerous fan curves.
        /// v2.8.0: Relaxed thresholds — previous values (60°C→40%, 70°C→70%) were
        /// far too aggressive for gaming laptops that routinely idle at 55-65°C and
        /// run 75-90°C under load. Users reported fans ramping up on Silent.
        /// Only intervene at genuinely dangerous temperatures.
        /// </summary>
        private double ApplySafetyBoundsClamping(double fanPercent, double temperatureC)
        {
            double clamped = fanPercent;
            
            // Emergency thermal protection at 95°C - force 100%
            if (temperatureC >= 95.0)
            {
                if (fanPercent < 100.0)
                {
                    _logging.Warn($"EMERGENCY: Temperature {temperatureC:F1}°C >= 95°C, forcing fans to 100% (curve wanted {fanPercent:F0}%)");
                    return 100.0;
                }
            }
            
            // Critical: 90°C+ — minimum 80% (genuine danger zone)
            if (temperatureC >= 90.0)
            {
                clamped = Math.Max(fanPercent, 80.0);
            }
            // High: 85°C+ — minimum 60% (approaching throttle temp)
            else if (temperatureC >= 85.0)
            {
                clamped = Math.Max(fanPercent, 60.0);
            }
            // Moderate: 80°C+ — minimum 40% (warm but typical under load)
            else if (temperatureC >= 80.0)
            {
                clamped = Math.Max(fanPercent, 40.0);
            }
            // Light: below 80°C — trust the user's curve entirely
            // Gaming laptops idle at 50-65°C; there's no danger here
            
            if (clamped > fanPercent)
            {
                _logging.Info($"Safety clamp: {fanPercent:F0}% → {clamped:F0}% (temp {temperatureC:F1}°C)");
            }
            
            return clamped;
        }
        
        /// <summary>
        /// Interpolate fan speed using slope-based linear interpolation (omen-fan style).
        /// This provides smoother fan speed transitions between curve points by
        /// calculating the exact speed based on where the temperature falls between points.
        /// 
        /// Based on the omen-fan Linux utility algorithm:
        /// slope[i] = (speed[i] - speed[i-1]) / (temp[i] - temp[i-1])
        /// speed = speed[i-1] + slope[i-1] * (current_temp - temp[i-1])
        /// </summary>
        /// <param name="curve">The fan curve points (must be sorted by temperature)</param>
        /// <param name="temperature">Current temperature in degrees Celsius</param>
        /// <returns>Interpolated fan speed percentage (0-100)</returns>
        private static double InterpolateFanSpeed(List<FanCurvePoint> curve, double temperature)
        {
            if (curve == null || curve.Count == 0)
                return 50; // Default fallback
                
            // Below first point - use minimum speed
            if (temperature <= curve[0].TemperatureC)
                return curve[0].FanPercent;
                
            // Above last point - use maximum speed (safety: always go to max, never drop)
            if (temperature >= curve[^1].TemperatureC)
                return curve[^1].FanPercent;
                
            // Find surrounding points and interpolate using slope calculation
            for (int i = 0; i < curve.Count - 1; i++)
            {
                var lower = curve[i];
                var upper = curve[i + 1];
                
                if (temperature >= lower.TemperatureC && temperature <= upper.TemperatureC)
                {
                    // Calculate slope: (speed_delta) / (temp_delta)
                    double tempDelta = upper.TemperatureC - lower.TemperatureC;
                    
                    // Avoid division by zero (curve points at same temperature)
                    if (tempDelta <= 0)
                        return lower.FanPercent;
                    
                    double slope = (upper.FanPercent - lower.FanPercent) / tempDelta;
                    
                    // Calculate interpolated speed
                    double interpolatedSpeed = lower.FanPercent + slope * (temperature - lower.TemperatureC);
                    
                    // Clamp to valid range (0-100%)
                    return Math.Clamp(interpolatedSpeed, 0, 100);
                }
            }
            
            // Fallback: use last curve point (shouldn't reach here if curve is sorted)
            return curve[^1].FanPercent;
        }
        
        /// <summary>
        /// Configure smoothing settings programmatically.
        /// </summary>
        public void SetSmoothingSettings(FanTransitionSettings settings)
        {
            if (settings == null) return;
            _smoothingEnabled = settings.EnableSmoothing;
            _smoothingDurationMs = Math.Max(0, settings.SmoothingDurationMs);
            _smoothingStepMs = Math.Max(50, settings.SmoothingStepMs);
            _logging.Info($"Fan smoothing: {(_smoothingEnabled ? "Enabled" : "Disabled")}, duration={_smoothingDurationMs}ms, step={_smoothingStepMs}ms");
        }

        /// <summary>
        /// Force apply the active curve immediately (or once) for the given temps.
        /// Useful for tests and immediate apply operations.
        /// </summary>
        public async Task ForceApplyCurveNowAsync(double cpuTemp, double gpuTemp, bool immediate = false, CancellationToken ct = default)
        {
            await Task.Run(async () =>
            {
                await ApplyCurveIfNeededAsync(cpuTemp, gpuTemp, immediate, ct);
            }, ct);
        }
        public bool FanWritesAvailable => _fanController.IsAvailable;

        private async Task MonitorLoop(CancellationToken token)
        {
            _logging.Info("Fan monitor loop started (with continuous curve support)");
            
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Read temperatures
                    var temps = _thermalProvider.ReadTemperatures().ToList();
                    var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius 
                                  ?? temps.FirstOrDefault()?.Celsius ?? 0;
                    var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius 
                                  ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                    
                    var sample = new ThermalSample
                    {
                        Timestamp = DateTime.Now,
                        CpuCelsius = cpuTemp,
                        GpuCelsius = gpuTemp
                    };
                    
                    // Check if temps are stable for adaptive polling
                    bool tempsStable = Math.Abs(cpuTemp - _lastCpuTemp) < TempChangeThreshold 
                                       && Math.Abs(gpuTemp - _lastGpuTemp) < TempChangeThreshold;
                    _lastCpuTemp = cpuTemp;
                    _lastGpuTemp = gpuTemp;
                    
                    if (tempsStable)
                    {
                        _stableReadings = Math.Min(_stableReadings + 1, StableThreshold + 1);
                    }
                    else
                    {
                        _stableReadings = 0;
                    }
                    
                    // Check thermal protection FIRST (overrides Auto mode when temps critical)
                    CheckThermalProtection(cpuTemp, gpuTemp);
                    
                    // Apply fan curve if enabled and enough time has passed
                    await ApplyCurveIfNeededAsync(cpuTemp, gpuTemp, immediate: false);
                    
                    // Read fan speeds (less frequently to reduce ACPI overhead)
                    var fanSpeeds = _fanController.ReadFanSpeeds().ToList();
                    
                    // Check if fan speeds changed meaningfully (reduce UI churn)
                    bool fanSpeedsChanged = fanSpeeds.Count != _lastFanSpeeds.Count;
                    if (!fanSpeedsChanged)
                    {
                        for (int i = 0; i < fanSpeeds.Count; i++)
                        {
                            if (Math.Abs(fanSpeeds[i].Rpm - _lastFanSpeeds[i]) > FanSpeedChangeThreshold)
                            {
                                fanSpeedsChanged = true;
                                break;
                            }
                        }
                    }

                    // Use BeginInvoke to avoid potential deadlocks
                    App.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        _thermalSamples.Add(sample);
                        const int window = 120;
                        while (_thermalSamples.Count > window)
                        {
                            _thermalSamples.RemoveAt(0);
                        }

                        // Only update fan telemetry if values changed meaningfully
                        // This reduces GC pressure and UI update churn
                        if (fanSpeedsChanged)
                        {
                            _fanTelemetry.Clear();
                            foreach (var fan in fanSpeeds)
                            {
                                _fanTelemetry.Add(fan);
                            }
                            _lastFanSpeeds = fanSpeeds.Select(f => f.Rpm).ToList();
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Gracefully exit on app shutdown
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error("Fan monitor loop error", ex);
                }

                // Adaptive polling delay - slower when temps stable to reduce DPC latency
                var pollDelay = _stableReadings >= StableThreshold 
                    ? MonitorMaxIntervalMs 
                    : (int)_monitorPollPeriod.TotalMilliseconds;
                    
                try
                {
                    await Task.Delay(pollDelay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logging.Info("Fan monitor loop stopped");
        }
        
        /// <summary>
        /// Thermal protection override - kicks fans to max when temps hit critical levels.
        /// This works even in Auto mode to prevent thermal throttling/damage.
        /// v2.8.0: Raised thresholds based on community feedback:
        /// - 90°C: Start ramping fans aggressively
        /// - 95°C: Emergency max fans
        /// </summary>
        // Remember the fan mode/preset BEFORE thermal protection kicks in
        private string? _preThermalFanMode;
        private FanPreset? _preThermalPreset;
        private int _preThermalFanPercent;
        
        private void CheckThermalProtection(double cpuTemp, double gpuTemp)
        {
            if (!_thermalProtectionEnabled || !FanWritesAvailable)
                return;
                
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            var now = DateTime.UtcNow;
            
            // Emergency: temps >= 95°C - immediate max fans (no debounce, safety critical)
            if (maxTemp >= ThermalEmergencyThreshold)
            {
                // Reset release timer
                _thermalBelowReleaseSince = DateTime.MinValue;
                
                if (!_thermalProtectionActive)
                {
                    // Store current fan state BEFORE thermal protection
                    _preThermalFanMode = _currentFanMode;
                    _preThermalPreset = _activePreset;
                    _preThermalFanPercent = _lastAppliedFanPercent;
                    
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL EMERGENCY: {maxTemp:F0}°C - forcing fans to 100%!");
                    
                    // Notify user of thermal protection activation
                    _notificationService?.ShowThermalProtectionActivated(maxTemp, "Emergency - Max Fans");
                }
                
                // Force max fans immediately
                _fanController.SetFanSpeed(100);
                return;
            }
            
            // Warning: temps >= configurable threshold (default 90°C) - boost fans
            // v2.8.0: Requires sustained temperature above threshold for debounce period
            if (maxTemp >= _thermalProtectionThreshold)
            {
                // Reset release timer since we're above threshold
                _thermalBelowReleaseSince = DateTime.MinValue;
                
                // Start tracking when temp first exceeded threshold
                if (_thermalAboveThresholdSince == DateTime.MinValue)
                {
                    _thermalAboveThresholdSince = now;
                }
                
                if (!_thermalProtectionActive)
                {
                    // Check debounce — temp must stay above threshold for N seconds
                    var aboveDuration = (now - _thermalAboveThresholdSince).TotalSeconds;
                    if (aboveDuration < ThermalActivateDebounceSeconds)
                    {
                        // Not yet sustained — don't activate yet
                        return;
                    }
                    
                    // Store current fan state BEFORE thermal protection
                    _preThermalFanMode = _currentFanMode;
                    _preThermalPreset = _activePreset;
                    _preThermalFanPercent = _lastAppliedFanPercent;
                    
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL WARNING: {maxTemp:F0}°C sustained for {aboveDuration:F0}s - boosting fan speed");
                    
                    // Notify user of thermal protection activation
                    _notificationService?.ShowThermalProtectionActivated(maxTemp, "Warning - Boosted Fans");
                }
                
                // Calculate thermal protection target: threshold = 85%, scaling to 100% at emergency
                var tempRange = ThermalEmergencyThreshold - _thermalProtectionThreshold;
                var thermalTargetPercent = (int)(85 + (maxTemp - _thermalProtectionThreshold) * (15.0 / tempRange));
                thermalTargetPercent = Math.Min(100, thermalTargetPercent);
                
                // BUG FIX #32: Don't REDUCE fan speed if already at higher speed!
                // If user is in Max mode at 100%, don't drop to 85%
                if (_preThermalFanPercent >= thermalTargetPercent)
                {
                    _logging.Info($"Thermal protection: keeping existing fan speed ({_preThermalFanPercent}%) >= thermal target ({thermalTargetPercent}%)");
                    return;
                }
                
                _fanController.SetFanSpeed(thermalTargetPercent);
                return;
            }
            
            // Temps dropped below threshold — reset activate timer
            _thermalAboveThresholdSince = DateTime.MinValue;
            
            // Temps back to safe range - release thermal protection
            // v2.8.0: Increased hysteresis from 5°C to 10°C and added debounce timer
            var releaseThreshold = _thermalProtectionThreshold - ThermalReleaseHysteresis;
            if (_thermalProtectionActive && maxTemp < releaseThreshold)
            {
                // Start tracking when temp first dropped below release threshold
                if (_thermalBelowReleaseSince == DateTime.MinValue)
                {
                    _thermalBelowReleaseSince = now;
                }
                
                // Check debounce — temp must stay below release threshold for N seconds
                var belowDuration = (now - _thermalBelowReleaseSince).TotalSeconds;
                if (belowDuration < ThermalReleaseDebounceSeconds)
                {
                    // Not yet sustained — keep thermal protection active
                    return;
                }
                _thermalProtectionActive = false;
                _logging.Info($"✓ Temps normalized ({maxTemp:F0}°C) - thermal protection released");
                
                // BUG FIX v2.3.1: SAFE RELEASE - Don't let BIOS drop fans to 0 RPM at warm temps!
                // If temps are still "gaming warm" (above ThermalSafeReleaseTemp), keep fans
                // spinning at minimum floor to prevent 0 RPM bug on Victus/OMEN laptops.
                bool stillWarm = maxTemp >= ThermalSafeReleaseTemp;
                
                // BUG FIX #32: Restore the ORIGINAL fan state from BEFORE thermal protection
                // Not necessarily _activePreset, which may have been changed during thermal event
                if (_preThermalFanMode == "Max")
                {
                    _logging.Info($"Restoring Max fan mode after thermal protection");
                    _fanController.ApplyMaxCooling();
                    _fanController.SetFanSpeed(100);
                    _currentFanMode = "Max";
                    _lastAppliedFanPercent = 100;
                }
                else if (_preThermalPreset != null)
                {
                    _logging.Info($"Restoring preset '{_preThermalPreset.Name}' after thermal protection");
                    _fanController.ApplyPreset(_preThermalPreset);
                    _activePreset = _preThermalPreset;
                    
                    // If still warm and restoring to Auto/Default preset, set minimum fan floor
                    var presetNameLower = _preThermalPreset.Name.ToLowerInvariant();
                    if (stillWarm && (presetNameLower.Contains("auto") || presetNameLower.Contains("default")))
                    {
                        _logging.Info($"Setting minimum {ThermalReleaseMinFanPercent}% fan floor (temps still {maxTemp:F0}°C)");
                        _fanController.SetFanSpeed(ThermalReleaseMinFanPercent);
                        _lastAppliedFanPercent = ThermalReleaseMinFanPercent;
                    }
                }
                else if (_preThermalFanPercent > 0)
                {
                    // v2.6.1: Don't restore to low fan speeds if temps are still warm!
                    // This was causing temp yo-yo on high-power laptops (i9/4090)
                    int restorePercent = _preThermalFanPercent;
                    if (stillWarm && restorePercent < ThermalReleaseMinFanPercent)
                    {
                        _logging.Info($"Temps still warm ({maxTemp:F0}°C) - using minimum {ThermalReleaseMinFanPercent}% instead of {restorePercent}%");
                        restorePercent = ThermalReleaseMinFanPercent;
                    }
                    _logging.Info($"Restoring fan speed {restorePercent}% after thermal protection");
                    _fanController.SetFanSpeed(restorePercent);
                    _lastAppliedFanPercent = restorePercent;
                }
                else
                {
                    // No pre-thermal state - restore to BIOS auto mode
                    // BUG FIX v2.3.1: If still warm, set minimum fan floor to prevent 0 RPM
                    if (stillWarm)
                    {
                        _logging.Info($"Setting minimum {ThermalReleaseMinFanPercent}% fan floor (temps still {maxTemp:F0}°C)");
                        _fanController.SetFanSpeed(ThermalReleaseMinFanPercent);
                        _lastAppliedFanPercent = ThermalReleaseMinFanPercent;
                    }
                    else
                    {
                        // Truly cool (<55°C) - safe to let BIOS control
                        _logging.Info("Restoring fan control to BIOS auto mode (temps low enough)");
                        _fanController.RestoreAutoControl();
                    }
                }
                
                // Clear pre-thermal state
                _preThermalFanMode = null;
                _preThermalPreset = null;
                _preThermalFanPercent = 0;
            }
        }
        
        /// <summary>
        /// Apply fan curve based on current temperature if curve is enabled.
        /// This is the core OmenMon-style continuous fan control with hysteresis support.
        /// </summary>
        private Task ApplyCurveIfNeededAsync(double cpuTemp, double gpuTemp, bool immediate = false, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // Skip curve application if thermal protection is active
            if (_thermalProtectionActive)
                return Task.CompletedTask;
                
            // Skip curve application if diagnostic mode is active (manual testing)
            if (_diagnosticModeActive)
                return Task.CompletedTask;
            
            // Check if curves are available
            bool hasSingleCurve = _activeCurve != null;
            bool hasIndependentCurves = _independentCurvesEnabled && _cpuCurve != null && _gpuCurve != null;
            
            if (!_curveEnabled || (!hasSingleCurve && !hasIndependentCurves) || !FanWritesAvailable)
                return Task.CompletedTask;

            // Only update curve every CurveUpdateIntervalMs
            var now = DateTime.Now;
            var timeSinceLastUpdate = (now - _lastCurveUpdate).TotalMilliseconds;
            var timeSinceForceRefresh = (now - _lastCurveForceRefresh).TotalMilliseconds;
            
            // Check if we need to force a refresh (re-apply even if unchanged)
            // This combats BIOS countdown timer that may reset fan control
            bool forceRefresh = timeSinceForceRefresh >= CurveForceRefreshMs;
            
            if (timeSinceLastUpdate < CurveUpdateIntervalMs && !forceRefresh && !immediate)
                return Task.CompletedTask;
                
            // Route to appropriate curve handler
            if (hasIndependentCurves)
            {
                return ApplyIndependentCurvesAsync(cpuTemp, gpuTemp, immediate, forceRefresh, now);
            }
            
            // Route to appropriate curve handler
            if (hasIndependentCurves)
            {
                return ApplyIndependentCurvesAsync(cpuTemp, gpuTemp, immediate, forceRefresh, now);
            }
            
            lock (_curveLock)
            {
                if (_activeCurve == null)
                    return Task.CompletedTask;
                try
                {
                    // Use max of CPU/GPU temp to determine fan speed
                    var maxTemp = Math.Max(cpuTemp, gpuTemp);
                    
                    // Calculate fan speed using slope-based interpolation (omen-fan style)
                    // This provides smoother transitions between curve points
                    double targetFanPercent = InterpolateFanSpeed(_activeCurve, maxTemp);
                    
                    // Adjust fan speed based on GPU power boost level
                    // Higher power boost levels generate more heat, so slightly increase fan speed
                    targetFanPercent = AdjustFanPercentForGpuPowerBoost((int)targetFanPercent, gpuTemp);
                    
                    // Apply safety bounds clamping based on temperature
                    targetFanPercent = ApplySafetyBoundsClamping(targetFanPercent, maxTemp);
                    
                    // If immediate flag passed, bypass hysteresis and smoothing and apply now
                    if (immediate)
                    {
                        if (_fanController.SetFanSpeed((int)targetFanPercent))
                        {
                            _lastAppliedFanPercent = (int)targetFanPercent;
                            _lastHysteresisTemp = maxTemp;
                            _pendingFanPercent = -1;
                            _lastCurveUpdate = now;
                            _logging.Info($"Immediate curve applied: {targetFanPercent}% @ {maxTemp:F1}°C (GPU boost: {_gpuPowerBoostLevel})");
                        }

                        return Task.CompletedTask;
                    }
                    
                    // Apply hysteresis if enabled
                    if (_hysteresis.Enabled && _lastAppliedFanPercent >= 0)
                    {
                        var tempDelta = Math.Abs(maxTemp - _lastHysteresisTemp);
                        
                        // Check if temperature change is within dead-zone
                        if (tempDelta < _hysteresis.DeadZone && targetFanPercent != _lastAppliedFanPercent)
                        {
                            // Within dead-zone, don't change fan speed
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                        
                        // Apply ramp delay for speed changes
                        bool isIncrease = targetFanPercent > _lastAppliedFanPercent;
                        double requiredDelay = isIncrease ? _hysteresis.RampUpDelay : _hysteresis.RampDownDelay;
                        
                        if (_pendingFanPercent != (int)targetFanPercent)
                        {
                            // New target, start delay timer
                            _pendingFanPercent = (int)targetFanPercent;
                            _pendingIncrease = isIncrease;
                            _lastFanChangeRequest = now;
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                        
                        // Check if delay has elapsed
                        var timeSinceRequest = (now - _lastFanChangeRequest).TotalSeconds;
                        if (timeSinceRequest < requiredDelay)
                        {
                            _lastCurveUpdate = now;
                            return Task.CompletedTask;
                        }
                    }                    
                    // Apply if fan percent changed OR if we're forcing a refresh
                    // Force refresh combats BIOS countdown timer that may have reset fan control
                    if (targetFanPercent != _lastAppliedFanPercent || forceRefresh)
                    {
                        // If smoothing disabled or this is a force refresh or we have no previous applied value, just set directly
                        if (!_smoothingEnabled || _lastAppliedFanPercent < 0 || forceRefresh)
                        {
                            // Add retry logic for fan control hardening
                            const int maxRetries = 2;
                            bool success = false;
                            
                            for (int retry = 0; retry <= maxRetries && !success; retry++)
                            {
                                success = _fanController.SetFanSpeed((int)targetFanPercent);
                                if (!success && retry < maxRetries)
                                {
                                    _logging.Warn($"Fan speed command failed (attempt {retry + 1}/{maxRetries + 1}), retrying...");
                                    System.Threading.Thread.Sleep(300); // Brief delay before retry
                                }
                            }
                            
                            if (success)
                            {
                                _lastAppliedFanPercent = (int)targetFanPercent;
                                _lastHysteresisTemp = maxTemp;
                                _pendingFanPercent = -1;
                                
                                if (forceRefresh)
                                {
                                    _lastCurveForceRefresh = now;
                                    _logging.Info($"Curve force-refreshed: {targetFanPercent}% @ {maxTemp:F1}°C");
                                }
                                else
                                {
                                    _logging.Info($"Curve applied: {targetFanPercent}% @ {maxTemp:F1}°C");
                                }
                            }
                            else
                            {
                                _logging.Error($"Failed to set fan speed to {targetFanPercent}% after {maxRetries + 1} attempts");
                            }
                        }
                        else
                        {
                            // Ramp to the new target asynchronously so we don't block the monitor loop
                            var cancellationToken = CancellationToken.None;
                            _ = RampFanToPercentAsync((int)targetFanPercent, cancellationToken);
                            _lastHysteresisTemp = maxTemp;
                        }
                    }
                    
                    _lastCurveUpdate = now;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to apply fan curve: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Apply independent CPU and GPU fan curves based on their respective temperatures.
        /// Uses slope-based interpolation for smooth fan speed transitions.
        /// </summary>
        private Task ApplyIndependentCurvesAsync(double cpuTemp, double gpuTemp, bool immediate, bool forceRefresh, DateTime now)
        {
            lock (_curveLock)
            {
                if (_cpuCurve == null || _gpuCurve == null)
                    return Task.CompletedTask;
                    
                try
                {
                    // Evaluate CPU curve using slope-based interpolation
                    int cpuFanPercent = (int)Math.Round(InterpolateFanSpeed(_cpuCurve, cpuTemp));
                    
                    // Evaluate GPU curve using slope-based interpolation
                    int gpuFanPercent = (int)Math.Round(InterpolateFanSpeed(_gpuCurve, gpuTemp));
                    
                    // Apply safety bounds clamping to both CPU and GPU fan speeds
                    cpuFanPercent = (int)Math.Round(ApplySafetyBoundsClamping(cpuFanPercent, cpuTemp));
                    gpuFanPercent = (int)Math.Round(ApplySafetyBoundsClamping(gpuFanPercent, gpuTemp));
                    
                    // Check if either fan needs updating
                    bool cpuChanged = cpuFanPercent != _lastAppliedCpuFanPercent;
                    bool gpuChanged = gpuFanPercent != _lastAppliedGpuFanPercent;
                    
                    if (!cpuChanged && !gpuChanged && !forceRefresh && !immediate)
                    {
                        _lastCurveUpdate = now;
                        return Task.CompletedTask;
                    }
                    
                    // Apply using the dual fan speed method
                    if (_fanController is WmiFanController wmiFanController)
                    {
                        if (wmiFanController.SetFanSpeeds(cpuFanPercent, gpuFanPercent))
                        {
                            _lastAppliedCpuFanPercent = cpuFanPercent;
                            _lastAppliedGpuFanPercent = gpuFanPercent;
                            _lastCurveUpdate = now;
                            
                            if (forceRefresh)
                            {
                                _lastCurveForceRefresh = now;
                                _logging.Info($"Independent curves force-refreshed - CPU: {cpuFanPercent}% @ {cpuTemp:F1}°C, GPU: {gpuFanPercent}% @ {gpuTemp:F1}°C");
                            }
                            else
                            {
                                _logging.Info($"Independent curves applied - CPU: {cpuFanPercent}% @ {cpuTemp:F1}°C, GPU: {gpuFanPercent}% @ {gpuTemp:F1}°C");
                            }
                        }
                    }
                    else
                    {
                        // Fallback for non-WMI controllers: use max of both targets
                        int maxPercent = Math.Max(cpuFanPercent, gpuFanPercent);
                        if (_fanController.SetFanSpeed(maxPercent))
                        {
                            _lastAppliedFanPercent = maxPercent;
                            _lastAppliedCpuFanPercent = cpuFanPercent;
                            _lastAppliedGpuFanPercent = gpuFanPercent;
                            _lastCurveUpdate = now;
                            _logging.Info($"Independent curves (fallback mode): {maxPercent}% - CPU target: {cpuFanPercent}%, GPU target: {gpuFanPercent}%");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to apply independent fan curves: {ex.Message}");
                }
            }
            
            return Task.CompletedTask;
        }

        private async Task RampFanToPercentAsync(int targetPercent, CancellationToken cancellationToken)
        {
            // Determine start point for ramp. If no previous value, start from 0% to provide a ramp-up
            int from = _lastAppliedFanPercent < 0 ? 0 : _lastAppliedFanPercent;

            if (targetPercent == from)
                return;

            int to = targetPercent;
            int diff = to - from;
            int steps = Math.Max(1, _smoothingDurationMs / Math.Max(1, _smoothingStepMs));
            double stepSize = diff / (double)steps;

            for (int i = 1; i <= steps; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                int interim = (int)Math.Round(from + stepSize * i);
                interim = Math.Clamp(interim, 0, 100);
                try
                {
                    _fanController.SetFanSpeed(interim);
                    _lastAppliedFanPercent = interim;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Error during fan ramp step: {ex.Message}");
                }

                try { await Task.Delay(_smoothingStepMs, cancellationToken); } catch { break; }
            }

            // Ensure final target applied
            if (_lastAppliedFanPercent != targetPercent)
            {
                _fanController.SetFanSpeed(targetPercent);
                _lastAppliedFanPercent = targetPercent;
            }
        }

        #region Quick Profile Methods (for GeneralView)

        /// <summary>
        /// Apply max cooling mode (100% fans).
        /// </summary>
        public void ApplyMaxCooling()
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Max cooling skipped; fan control unavailable");
                return;
            }
            
            DisableCurve();
            _fanController.ApplyMaxCooling();
            // Ensure the controller records the applied percent (defensive for various controller implementations)
            try
            {
                if (_fanController.SetFanSpeed(100))
                {
                    _lastAppliedFanPercent = 100;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"SetFanSpeed(100) during ApplyMaxCooling threw: {ex.Message}");
            }
            _currentFanMode = "Max";
            _logging.Info("Max cooling mode applied");
        }

        /// <summary>
        /// Apply auto fan mode (BIOS control).
        /// </summary>
        public void ApplyAutoMode()
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Auto mode skipped; fan control unavailable");
                return;
            }
            
            DisableCurve();
            _fanController.ApplyAutoMode();
            _currentFanMode = "Auto";
            _logging.Info("Auto fan mode applied (BIOS control)");
        }

        /// <summary>
        /// Apply quiet fan mode (low speeds).
        /// </summary>
        public void ApplyQuietMode()
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Quiet mode skipped; fan control unavailable");
                return;
            }
            
            DisableCurve();
            _fanController.ApplyQuietMode();
            _currentFanMode = "Quiet";
            _logging.Info("Quiet fan mode applied");
        }

        private string _currentFanMode = "Auto";
        
        /// <summary>
        /// Get the current fan mode name.
        /// </summary>
        public string? GetCurrentFanMode() => _currentFanMode;
        
        /// <summary>
        /// Reset EC (Embedded Controller) to factory defaults.
        /// Restores BIOS control of fans and clears all manual overrides.
        /// Use this to fix stuck fan readings, incorrect BIOS display values, and other EC-related issues.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            _logging.Info("═══════════════════════════════════════════════════");
            _logging.Info("FanService: Initiating EC Reset to Defaults...");
            _logging.Info("═══════════════════════════════════════════════════");
            
            // First, disable any active fan curve
            DisableCurve();
            
            // Clear our internal state
            _currentFanMode = "Auto";
            _lastAppliedFanPercent = 0;
            
            // Delegate to the fan controller
            var result = _fanController.ResetEcToDefaults();
            
            if (result)
            {
                _logging.Info("✓ EC Reset completed successfully via FanService");
            }
            else
            {
                _logging.Warn("EC Reset returned false - may have partially succeeded");
            }
            
            return result;
        }

        /// <summary>
        /// Adjust fan percentage based on GPU power boost level to account for increased heat generation.
        /// Higher power boost levels require slightly higher fan speeds for optimal cooling.
        /// </summary>
        private int AdjustFanPercentForGpuPowerBoost(int baseFanPercent, double gpuTemp)
        {
            if (gpuTemp < 50) // Only adjust when GPU is under load
                return baseFanPercent;

            int adjustment = _gpuPowerBoostLevel switch
            {
                "Minimum" => 0,    // Base TGP - no adjustment needed
                "Medium" => 2,     // Custom TGP - slight increase
                "Maximum" => 5,    // Custom TGP + Dynamic Boost - moderate increase
                "Extended" => 8,   // Extended boost - significant increase for higher wattage
                _ => 0
            };

            // Scale adjustment based on GPU temperature - more adjustment at higher temps
            double tempFactor = Math.Min(1.0, (gpuTemp - 50) / 30); // 0-1 scale over 50-80°C
            int scaledAdjustment = (int)(adjustment * tempFactor);

            int adjustedPercent = Math.Min(100, baseFanPercent + scaledAdjustment);

            if (scaledAdjustment > 0)
            {
                _logging.Debug($"GPU power boost adjustment: {baseFanPercent}% + {scaledAdjustment}% = {adjustedPercent}% (boost: {_gpuPowerBoostLevel}, GPU: {gpuTemp:F1}°C)");
            }

            return adjustedPercent;
        }

        #endregion

        /// <summary>
        /// Force-set fan speed directly on controller (used for restoration/diagnostics).
        /// </summary>
        public void ForceSetFanSpeed(int percent)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("ForceSetFanSpeed skipped; fan control unavailable");
                return;
            }

            try
            {
                _fanController.SetFanSpeed(percent);
                _lastAppliedFanPercent = percent;
            }
            catch (Exception ex)
            {
                _logging.Warn($"ForceSetFanSpeed({percent}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Force-set individual fan speeds directly on controller (used for diagnostics).
        /// </summary>
        public bool ForceSetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("ForceSetFanSpeeds skipped; fan control unavailable");
                return false;
            }

            try
            {
                return _fanController.SetFanSpeeds(cpuPercent, gpuPercent);
            }
            catch (Exception ex)
            {
                _logging.Warn($"ForceSetFanSpeeds({cpuPercent}, {gpuPercent}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test helper: try to read a LastSetPercent-like property from the underlying fan controller if present.
        /// Returns null if the property does not exist.
        /// </summary>
        public int? GetControllerReportedSetPercent()
        {
            try
            {
                var prop = _fanController.GetType().GetProperty("LastSetPercent");
                if (prop != null && prop.PropertyType == typeof(int))
                {
                    return (int?)prop.GetValue(_fanController);
                }
            }
            catch { }
            return null;
        }

        public void Dispose()
        {
            // Restore system auto-control before shutting down
            // This returns fans to BIOS/Windows default control instead of staying at last manual setting
            try
            {
                if (_fanController.IsAvailable)
                {
                    // Use full EC reset which is more thorough than RestoreAutoControl
                    // This resets fan state, timer, and BIOS control registers
                    _logging.Info("Resetting EC to restore BIOS fan control on shutdown...");
                    
                    // First, try RestoreAutoControl for immediate effect
                    _fanController.RestoreAutoControl();
                    
                    // Wait briefly for EC to process
                    System.Threading.Thread.Sleep(100);
                    
                    // Then do full reset to ensure BIOS takes over
                    if (_fanController.ResetEcToDefaults())
                    {
                        _logging.Info("FanService disposed (EC reset complete, BIOS should now control fans)");
                    }
                    else
                    {
                        _logging.Warn("FanService disposed (EC reset returned false, fans may not restore properly)");
                    }
                }
                else
                {
                    _logging.Info("FanService disposed (fan controller not available, no auto-control restoration)");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore auto-control on dispose: {ex.Message}");
            }
            
            DisableCurve();
            Stop();
        }
    }
}
