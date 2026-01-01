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
        private readonly TimeSpan _monitorPollPeriod;
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private readonly ObservableCollection<FanTelemetry> _fanTelemetry = new();
        private CancellationTokenSource? _cts;

        // Active fan curve for continuous application (OmenMon-style)
        private List<FanCurvePoint>? _activeCurve;
        private FanPreset? _activePreset;
        private bool _curveEnabled = false;
        private readonly object _curveLock = new();
        
        /// <summary>
        /// Expose ThermalSensorProvider for OSD and other services that need temperature data.
        /// </summary>
        public ThermalSensorProvider ThermalProvider => _thermalProvider;
        
        // Curve update timing (like OmenMon's 15-second interval)
        private const int CurveUpdateIntervalMs = 10000; // 10 seconds between curve updates (reduced from 15)
        private const int CurveForceRefreshMs = 60000;   // Force re-apply every 60 seconds even if unchanged
        private const int MonitorMinIntervalMs = 1000;   // 1 second minimum for UI updates
        private const int MonitorMaxIntervalMs = 5000;   // 5 seconds when temps stable
        private DateTime _lastCurveUpdate = DateTime.MinValue;
        private DateTime _lastCurveForceRefresh = DateTime.MinValue;
        private int _lastAppliedFanPercent = -1;

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
        // Lowered thresholds based on user feedback - fans were spinning up too late
        private const double ThermalProtectionThreshold = 80.0; // °C - start ramping fans (was 90)
        private const double ThermalEmergencyThreshold = 88.0;  // °C - max fans immediately (was 95)
        private bool _thermalProtectionActive = false;
        private bool _thermalProtectionEnabled = true; // Can be disabled in settings
        
        // Hysteresis state
        private FanHysteresisSettings _hysteresis = new();
        private double _lastHysteresisTemp = 0;
        private DateTime _lastFanChangeRequest = DateTime.MinValue;
        private int _pendingFanPercent = -1;
        private bool _pendingIncrease = false;

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }
        
        /// <summary>
        /// The backend being used for fan control (WMI BIOS, EC, or None).
        /// </summary>
        public string Backend => _fanController.Backend;
        
        /// <summary>
        /// Whether a custom fan curve is actively being applied.
        /// </summary>
        public bool IsCurveActive => _curveEnabled && _activeCurve != null;
        
        /// <summary>
        /// The currently active preset name, if any.
        /// </summary>
        public string? ActivePresetName => _activePreset?.Name;
        
        /// <summary>
        /// Whether thermal protection is currently overriding fan control.
        /// </summary>
        public bool IsThermalProtectionActive => _thermalProtectionActive;
        
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
        /// </summary>
        public void SetHysteresis(FanHysteresisSettings settings)
        {
            _hysteresis = settings ?? new FanHysteresisSettings();
            _logging.Info($"Fan hysteresis: {(_hysteresis.Enabled ? $"Enabled (deadzone={_hysteresis.DeadZone}°C, ramp↑={_hysteresis.RampUpDelay}s, ramp↓={_hysteresis.RampDownDelay}s)" : "Disabled")}");
        }

        /// <summary>
        /// Create FanService with the new IFanController interface.
        /// </summary>
        public FanService(IFanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, int pollMs)
        {
            _fanController = controller;
            _thermalProvider = thermalProvider;
            _logging = logging;
            _monitorPollPeriod = TimeSpan.FromMilliseconds(Math.Max(MonitorMinIntervalMs, pollMs));
            ThermalSamples = new ReadOnlyObservableCollection<ThermalSample>(_thermalSamples);
            FanTelemetry = new ReadOnlyObservableCollection<FanTelemetry>(_fanTelemetry);
            
            _logging.Info($"FanService initialized with backend: {Backend}, curve interval: {CurveUpdateIntervalMs}ms");
        }

        /// <summary>
        /// Legacy constructor for compatibility with existing FanController.
        /// </summary>
        public FanService(FanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, int pollMs)
            : this(new EcFanControllerWrapper(controller, null!, logging), thermalProvider, logging, pollMs)
        {
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
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
                
                // Check if preset has a custom curve to apply
                var nameLower = preset.Name.ToLowerInvariant();
                bool isMaxPreset = nameLower.Contains("max") && !nameLower.Contains("auto");
                
                if (isMaxPreset)
                {
                    // Max preset: Disable curve, let fans run at 100%
                    DisableCurve();
                    _activePreset = preset;
                    _logging.Info($"Preset '{preset.Name}' using maximum fan speed (curve disabled)");
                    if (immediate)
                    {
                        // Apply immediate 100%
                        _fanController.SetFanSpeed(100);
                        _lastAppliedFanPercent = 100;
                    }
                }
                else if (preset.Curve != null && preset.Curve.Any())
                {
                    // Enable continuous curve application for ALL presets with curves (including Auto)
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
            if (!curveList.Any())
            {
                _logging.Warn("Empty curve provided");
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
                _lastAppliedFanPercent = -1;
            }
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
        /// Thresholds lowered based on user feedback:
        /// - 80°C: Start ramping fans aggressively
        /// - 88°C: Emergency max fans
        /// </summary>
        private void CheckThermalProtection(double cpuTemp, double gpuTemp)
        {
            if (!_thermalProtectionEnabled || !FanWritesAvailable)
                return;
                
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            
            // Emergency: temps >= 88°C - immediate max fans
            if (maxTemp >= ThermalEmergencyThreshold)
            {
                if (!_thermalProtectionActive)
                {
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL EMERGENCY: {maxTemp:F0}°C - forcing fans to 100%!");
                }
                
                // Force max fans immediately
                _fanController.SetFanSpeed(100);
                return;
            }
            
            // Warning: temps >= 80°C - ramp up fans aggressively
            if (maxTemp >= ThermalProtectionThreshold)
            {
                if (!_thermalProtectionActive)
                {
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL WARNING: {maxTemp:F0}°C - boosting fan speed");
                }
                
                // Calculate aggressive fan speed: 80°C = 70%, scaling to 100% at 88°C
                // More aggressive than before: 70% + ~3.75% per °C above 80
                var fanPercent = (int)(70 + (maxTemp - ThermalProtectionThreshold) * 3.75);
                fanPercent = Math.Min(100, fanPercent);
                
                _fanController.SetFanSpeed(fanPercent);
                return;
            }
            
            // Temps back to safe range - release thermal protection
            // Use 5°C hysteresis (release at 75°C instead of 80°C)
            if (_thermalProtectionActive && maxTemp < ThermalProtectionThreshold - 5)
            {
                _thermalProtectionActive = false;
                _logging.Info($"✓ Temps normalized ({maxTemp:F0}°C) - thermal protection released");
                
                // Re-apply the current preset to restore BIOS control
                if (_activePreset != null)
                {
                    _logging.Info($"Restoring preset '{_activePreset.Name}' after thermal protection");
                    _fanController.ApplyPreset(_activePreset);
                }
                else
                {
                    // No preset - use RestoreAutoControl() to properly return control to BIOS
                    // NOTE: SetFanSpeed(0) is NOT safe on WMI backend - some firmware treats
                    // it as "minimum speed" rather than "auto". Always use RestoreAutoControl().
                    _logging.Info("Restoring fan control to BIOS auto mode");
                    _fanController.RestoreAutoControl();
                }
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
            if (!_curveEnabled || _activeCurve == null || !FanWritesAvailable)
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
            lock (_curveLock)
            {
                if (_activeCurve == null)
                    return Task.CompletedTask;
                try
                {
                    // Use max of CPU/GPU temp to determine fan speed
                    var maxTemp = Math.Max(cpuTemp, gpuTemp);
                    
                    // Find the appropriate curve point
                    var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                      ?? _activeCurve.FirstOrDefault();
                                      
                    if (targetPoint == null)
                        return Task.CompletedTask;
                    var targetFanPercent = targetPoint.FanPercent;
                    
                    // If immediate flag passed, bypass hysteresis and smoothing and apply now
                    if (immediate)
                    {
                        if (_fanController.SetFanSpeed(targetFanPercent))
                        {
                            _lastAppliedFanPercent = targetFanPercent;
                            _lastHysteresisTemp = maxTemp;
                            _pendingFanPercent = -1;
                            _lastCurveUpdate = now;
                            _logging.Info($"Immediate curve applied: {targetFanPercent}% @ {maxTemp:F1}°C");
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
                        
                        if (_pendingFanPercent != targetFanPercent)
                        {
                            // New target, start delay timer
                            _pendingFanPercent = targetFanPercent;
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
                        // Convert percentage to krpm (0-100% maps to 0-55 krpm)
                        byte fanLevel = (byte)(targetFanPercent * 55 / 100);
                        
                        // If smoothing disabled or this is a force refresh or we have no previous applied value, just set directly
                        if (!_smoothingEnabled || _lastAppliedFanPercent < 0 || forceRefresh)
                        {
                            if (_fanController.SetFanSpeed(targetFanPercent))
                            {
                                _lastAppliedFanPercent = targetFanPercent;
                                _lastHysteresisTemp = maxTemp;
                                _pendingFanPercent = -1;
                                
                                if (forceRefresh)
                                {
                                    _lastCurveForceRefresh = now;
                                    _logging.Info($"Curve force-refreshed: {targetFanPercent}% @ {maxTemp:F1}°C (level: {fanLevel})");
                                }
                                else
                                {
                                    _logging.Info($"Curve applied: {targetFanPercent}% @ {maxTemp:F1}°C (level: {fanLevel})");
                                }
                            }
                        }
                        else
                        {
                            // Ramp to the new target asynchronously so we don't block the monitor loop
                            var cancellationToken = CancellationToken.None;
                            _ = RampFanToPercentAsync(targetFanPercent, cancellationToken);
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
            DisableCurve();
            Stop();
            
            // Restore fan control to BIOS on exit so fans don't stay stuck
            try
            {
                _fanController.RestoreAutoControl();
                _logging.Info("Fan control restored to BIOS on shutdown");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not restore fan control on shutdown: {ex.Message}");
            }
        }
    }
}
