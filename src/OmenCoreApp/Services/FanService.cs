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
        
        // Curve update timing (like OmenMon's 15-second interval)
        private const int CurveUpdateIntervalMs = 15000; // 15 seconds between curve updates
        private const int MonitorMinIntervalMs = 1000;   // 1 second minimum for UI updates
        private const int MonitorMaxIntervalMs = 5000;   // 5 seconds when temps stable
        private DateTime _lastCurveUpdate = DateTime.MinValue;
        private int _lastAppliedFanPercent = -1;
        
        // Adaptive polling - reduce DPC latency by polling less when stable
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private int _stableReadings = 0;
        private const int StableThreshold = 3; // Number of stable readings before slowing down
        private const double TempChangeThreshold = 3.0; // °C change to trigger faster polling
        
        // Thermal protection - override Auto mode when temps critical
        private const double ThermalProtectionThreshold = 90.0; // °C - start ramping fans
        private const double ThermalEmergencyThreshold = 95.0;  // °C - max fans immediately
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
        public void ApplyPreset(FanPreset preset)
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
                
                // For Max/Auto presets, don't enable curve - they use BIOS control
                var nameLower = preset.Name.ToLowerInvariant();
                bool isMaxPreset = nameLower.Contains("max") && !nameLower.Contains("auto");
                bool isAutoPreset = nameLower.Contains("auto") || nameLower.Contains("default");
                
                if (isMaxPreset || isAutoPreset)
                {
                    // Disable curve for Max/Auto - let BIOS handle it
                    DisableCurve();
                    _activePreset = preset;
                    _logging.Info($"Preset '{preset.Name}' using BIOS control (curve disabled)");
                }
                else if (preset.Curve != null && preset.Curve.Any())
                {
                    // Enable continuous curve application for custom presets
                    EnableCurve(preset.Curve.ToList(), preset);
                    _logging.Info($"Preset '{preset.Name}' curve enabled with {preset.Curve.Count} points");
                }
            }
            else
            {
                _logging.Warn($"Fan preset '{preset.Name}' failed to apply via {Backend}");
            }
        }

        /// <summary>
        /// Apply a custom curve and start continuous monitoring.
        /// </summary>
        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
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
                    ApplyCurveIfNeeded(cpuTemp, gpuTemp);
                    
                    // Read fan speeds (less frequently to reduce ACPI overhead)
                    var fanSpeeds = _fanController.ReadFanSpeeds().ToList();

                    // Use BeginInvoke to avoid potential deadlocks
                    App.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        _thermalSamples.Add(sample);
                        const int window = 120;
                        while (_thermalSamples.Count > window)
                        {
                            _thermalSamples.RemoveAt(0);
                        }

                        // Update fan telemetry
                        _fanTelemetry.Clear();
                        foreach (var fan in fanSpeeds)
                        {
                            _fanTelemetry.Add(fan);
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
        /// </summary>
        private void CheckThermalProtection(double cpuTemp, double gpuTemp)
        {
            if (!_thermalProtectionEnabled || !FanWritesAvailable)
                return;
                
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            
            // Emergency: temps >= 95°C - immediate max fans
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
            
            // Warning: temps >= 90°C - ramp up fans aggressively
            if (maxTemp >= ThermalProtectionThreshold)
            {
                if (!_thermalProtectionActive)
                {
                    _thermalProtectionActive = true;
                    _logging.Warn($"⚠️ THERMAL WARNING: {maxTemp:F0}°C - boosting fan speed");
                }
                
                // Calculate aggressive fan speed: 90°C = 80%, scaling to 100% at 95°C
                var fanPercent = (int)(80 + (maxTemp - ThermalProtectionThreshold) * 4); // 80% + 4% per °C
                fanPercent = Math.Min(100, fanPercent);
                
                _fanController.SetFanSpeed(fanPercent);
                return;
            }
            
            // Temps back to safe range - release thermal protection
            if (_thermalProtectionActive && maxTemp < ThermalProtectionThreshold - 5) // 5°C hysteresis
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
                    // No preset - reset fans to let BIOS take over
                    _logging.Info("Resetting fan control to BIOS default");
                    _fanController.SetFanSpeed(0); // 0 = let BIOS control
                }
            }
        }
        
        /// <summary>
        /// Apply fan curve based on current temperature if curve is enabled.
        /// This is the core OmenMon-style continuous fan control with hysteresis support.
        /// </summary>
        private void ApplyCurveIfNeeded(double cpuTemp, double gpuTemp)
        {
            // Skip curve application if thermal protection is active
            if (_thermalProtectionActive)
                return;
                
            if (!_curveEnabled || _activeCurve == null || !FanWritesAvailable)
                return;
                
            // Only update curve every CurveUpdateIntervalMs (15 seconds like OmenMon)
            var now = DateTime.Now;
            var timeSinceLastUpdate = (now - _lastCurveUpdate).TotalMilliseconds;
            
            if (timeSinceLastUpdate < CurveUpdateIntervalMs)
                return;
                
            lock (_curveLock)
            {
                if (_activeCurve == null) return;
                
                try
                {
                    // Use max of CPU/GPU temp to determine fan speed
                    var maxTemp = Math.Max(cpuTemp, gpuTemp);
                    
                    // Find the appropriate curve point
                    var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                      ?? _activeCurve.FirstOrDefault();
                                      
                    if (targetPoint == null) return;
                    
                    var targetFanPercent = targetPoint.FanPercent;
                    
                    // Apply hysteresis if enabled
                    if (_hysteresis.Enabled && _lastAppliedFanPercent >= 0)
                    {
                        var tempDelta = Math.Abs(maxTemp - _lastHysteresisTemp);
                        
                        // Check if temperature change is within dead-zone
                        if (tempDelta < _hysteresis.DeadZone && targetFanPercent != _lastAppliedFanPercent)
                        {
                            // Within dead-zone, don't change fan speed
                            _lastCurveUpdate = now;
                            return;
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
                            return;
                        }
                        
                        // Check if delay has elapsed
                        var timeSinceRequest = (now - _lastFanChangeRequest).TotalSeconds;
                        if (timeSinceRequest < requiredDelay)
                        {
                            _lastCurveUpdate = now;
                            return;
                        }
                    }
                    
                    // Only apply if fan percent changed (avoid unnecessary WMI calls)
                    if (targetFanPercent != _lastAppliedFanPercent)
                    {
                        // Convert percentage to krpm (0-100% maps to 0-55 krpm)
                        byte fanLevel = (byte)(targetFanPercent * 55 / 100);
                        
                        // Use SetFanSpeed which internally calls SetFanLevel
                        if (_fanController.SetFanSpeed(targetFanPercent))
                        {
                            _lastAppliedFanPercent = targetFanPercent;
                            _lastHysteresisTemp = maxTemp;
                            _pendingFanPercent = -1;
                            _logging.Info($"Curve applied: {targetFanPercent}% @ {maxTemp:F1}°C (level: {fanLevel})");
                        }
                    }
                    
                    _lastCurveUpdate = now;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to apply fan curve: {ex.Message}");
                }
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

        public void Dispose()
        {
            DisableCurve();
            Stop();
        }
    }
}
