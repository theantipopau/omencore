using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// WMI-based fan controller for HP OMEN/Victus laptops.
    /// Uses HP WMI BIOS interface instead of direct EC access.
    /// This eliminates the need for the WinRing0 driver.
    /// 
    /// Features automatic countdown extension to prevent BIOS from reverting
    /// fan settings after 120 seconds (HP BIOS limitation).
    /// </summary>
    public class WmiFanController : IDisposable
    {
        private readonly HpWmiBios _wmiBios;
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly LoggingService? _logging;
        private bool _disposed;

        // Manual fan control state
        private HpWmiBios.FanMode _lastMode = HpWmiBios.FanMode.Default;
        
        // Countdown extension timer - keeps fan settings from reverting
        private Timer? _countdownExtensionTimer;
        private const int CountdownExtensionIntervalMs = 90000; // 90 seconds (timer is 120s)
        private bool _countdownExtensionEnabled = false;

        public bool IsAvailable => _wmiBios.IsAvailable;
        public string Status => _wmiBios.Status;
        public int FanCount => _wmiBios.FanCount;
        
        /// <summary>
        /// Indicates if manual fan control is currently active (vs automatic BIOS control).
        /// </summary>
        public bool IsManualControlActive { get; private set; }
        
        /// <summary>
        /// Indicates if countdown extension is enabled to prevent fan mode reverting.
        /// </summary>
        public bool CountdownExtensionEnabled => _countdownExtensionEnabled;

        public WmiFanController(LibreHardwareMonitorImpl hwMonitor, LoggingService? logging = null)
        {
            _hwMonitor = hwMonitor;
            _logging = logging;
            _wmiBios = new HpWmiBios(logging);
        }

        /// <summary>
        /// Apply a fan preset using WMI BIOS commands.
        /// For Max preset: Sets Performance mode AND enables max fan speed for immediate 100% fan.
        /// For other presets: Sets the appropriate thermal policy via WMI BIOS.
        /// </summary>
        public bool ApplyPreset(FanPreset preset)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot apply preset: WMI BIOS not available");
                return false;
            }

            try
            {
                var nameLower = preset.Name.ToLowerInvariant();
                
                // Check if this is a "Max" preset - enable full fan speed
                bool isMaxPreset = nameLower.Contains("max") && !nameLower.Contains("auto");
                
                // Check if this is an "Auto" or "Default" preset - should restore automatic control
                bool isAutoPreset = nameLower.Contains("auto") || nameLower.Contains("default");
                
                // For Max preset, we need to:
                // 1. First set Performance mode (for aggressive thermal management)
                // 2. Then enable SetFanMax for 100% immediate fan speed
                
                if (isMaxPreset)
                {
                    // Set Performance mode first
                    _logging?.Info("Applying Max fan preset: Setting Performance mode...");
                    _wmiBios.SetFanMode(HpWmiBios.FanMode.Performance);
                    _lastMode = HpWmiBios.FanMode.Performance;
                    
                    // Start countdown extension to keep fan settings active
                    StartCountdownExtension();
                    
                    // Now enable max fan speed (forces 100%)
                    if (_wmiBios.SetFanMax(true))
                    {
                        _logging?.Info("✓ Max fan speed enabled - fans should ramp to 100%");
                        IsManualControlActive = true; // Mark as manual since we're forcing max
                        
                        // Apply GPU power for maximum cooling
                        _wmiBios.SetGpuPower(HpWmiBios.GpuPowerLevel.Maximum);
                        return true;
                    }
                    else
                    {
                        _logging?.Warn("SetFanMax command failed - trying alternative method");
                        // Alternative: Try setting fan level directly to max (55 = ~5500 RPM)
                        if (_wmiBios.SetFanLevel(55, 55))
                        {
                            _logging?.Info("✓ Fan level set to maximum (55, 55)");
                            IsManualControlActive = true;
                            return true;
                        }
                    }
                    return false;
                }
                
                // For non-Max presets, disable max fan first if it was enabled
                // This is critical - without this, fans stay at max speed!
                if (_wmiBios.SetFanMax(false))
                {
                    _logging?.Info("✓ Disabled fan max mode before applying new preset");
                }
                
                // Small delay to ensure BIOS processes the fan max disable
                System.Threading.Thread.Sleep(100);
                
                // Map preset to fan mode
                var mode = MapPresetToFanMode(preset);
                
                if (_wmiBios.SetFanMode(mode))
                {
                    _lastMode = mode;
                    IsManualControlActive = false;
                    
                    // Start/stop countdown extension based on mode
                    if (isAutoPreset || mode == HpWmiBios.FanMode.Default || mode == HpWmiBios.FanMode.LegacyDefault)
                    {
                        // Auto/Default mode - stop countdown extension, let BIOS handle it
                        StopCountdownExtension();
                    }
                    else
                    {
                        // Non-default mode - start countdown extension to keep it active
                        StartCountdownExtension();
                    }
                    
                    // Apply GPU power settings if needed
                    ApplyGpuPowerFromPreset(preset);
                    
                    _logging?.Info($"✓ Applied preset: {preset.Name} (Mode: {mode})");
                    return true;
                }
                else
                {
                    _logging?.Warn($"SetFanMode failed for preset: {preset.Name} (Mode: {mode})");
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply preset: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Apply a custom fan curve by setting direct fan levels.
        /// </summary>
        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot apply custom curve: WMI BIOS not available");
                return false;
            }

            try
            {
                var curveList = curve.OrderBy(p => p.TemperatureC).ToList();
                if (!curveList.Any())
                {
                    _logging?.Warn("Empty curve provided");
                    return false;
                }

                // Get current temperature to determine fan level
                var cpuTemp = (int)_hwMonitor.GetCpuTemperature();
                var gpuTemp = (int)_hwMonitor.GetGpuTemperature();
                var maxTemp = Math.Max(cpuTemp, gpuTemp);

                // Find appropriate curve point
                var targetPoint = curveList.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                  ?? curveList.First();

                // Convert percentage to krpm (0-255 maps to ~0-5.5 krpm)
                // Typical: 0% = 0, 50% = ~2.5 krpm (25), 100% = ~5.5 krpm (55)
                byte fanLevel = (byte)(targetPoint.FanPercent * 55 / 100);

                if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                {
                    IsManualControlActive = true;
                    _logging?.Info($"✓ Custom curve applied: {targetPoint.FanPercent}% @ {maxTemp}°C (Level: {fanLevel})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply custom curve: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Set fan speed as a percentage (0-100).
        /// </summary>
        public bool SetFanSpeed(int percent)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set fan speed: WMI BIOS not available");
                return false;
            }

            percent = Math.Clamp(percent, 0, 100);

            try
            {
                // Convert percentage to krpm level
                byte fanLevel = (byte)(percent * 55 / 100);
                
                if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                {
                    IsManualControlActive = true;
                    _logging?.Info($"✓ Fan speed set to {percent}% (Level: {fanLevel})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan speed: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Enable maximum fan speed mode.
        /// </summary>
        public bool SetMaxFanSpeed(bool enabled)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set max fan: WMI BIOS not available");
                return false;
            }

            return _wmiBios.SetFanMax(enabled);
        }

        /// <summary>
        /// Set performance mode (Default/Performance/Cool).
        /// </summary>
        public bool SetPerformanceMode(string modeName)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set performance mode: WMI BIOS not available");
                return false;
            }

            var nameLower = modeName?.ToLowerInvariant() ?? "default";
            
            HpWmiBios.FanMode fanMode;
            if (nameLower.Contains("performance") || nameLower.Contains("turbo") || nameLower.Contains("gaming"))
            {
                fanMode = HpWmiBios.FanMode.Performance;
            }
            else if (nameLower.Contains("quiet") || nameLower.Contains("silent") || nameLower.Contains("cool") || nameLower.Contains("battery"))
            {
                fanMode = HpWmiBios.FanMode.Cool;
            }
            else
            {
                fanMode = HpWmiBios.FanMode.Default;
            }

            if (_wmiBios.SetFanMode(fanMode))
            {
                _lastMode = fanMode;
                IsManualControlActive = false;
                _logging?.Info($"✓ Performance mode set: {modeName} → {fanMode}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Set performance mode using PerformanceMode object.
        /// </summary>
        public bool SetPerformanceMode(PerformanceMode mode)
        {
            return SetPerformanceMode(mode?.Name ?? "Default");
        }

        /// <summary>
        /// Restore automatic fan control.
        /// </summary>
        public bool RestoreAutoControl()
        {
            if (!IsAvailable)
            {
                return false;
            }

            try
            {
                // Disable max fan speed first - this is critical!
                if (_wmiBios.SetFanMax(false))
                {
                    _logging?.Info("✓ Disabled fan max mode for auto control restore");
                }
                
                // Small delay to ensure BIOS processes the command
                System.Threading.Thread.Sleep(100);
                
                // Set default mode to restore automatic control
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Default))
                {
                    IsManualControlActive = false;
                    _lastMode = HpWmiBios.FanMode.Default;
                    _logging?.Info("✓ Restored automatic fan control");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to restore auto control: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Read current fan telemetry data.
        /// </summary>
        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Get fan speeds from hardware monitor
            var fanSpeeds = _hwMonitor.GetFanSpeeds();
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                var fanLevel = _wmiBios.GetFanLevel();
                int levelPercent = 0;
                
                if (fanLevel.HasValue)
                {
                    levelPercent = index == 0 
                        ? fanLevel.Value.fan1 * 100 / 55 
                        : fanLevel.Value.fan2 * 100 / 55;
                }
                else
                {
                    // Estimate from RPM
                    levelPercent = EstimateDutyFromRpm((int)rpm);
                }

                fans.Add(new FanTelemetry
                {
                    Name = name,
                    SpeedRpm = (int)rpm,
                    DutyCyclePercent = Math.Clamp(levelPercent, 0, 100),
                    Temperature = index == 0 ? _hwMonitor.GetCpuTemperature() : _hwMonitor.GetGpuTemperature()
                });
                index++;
            }

            // Fallback if no fans detected by LibreHardwareMonitor (common on HP OMEN)
            // HP OMEN laptops expose fan data via HP WMI BIOS, not standard EC Super I/O
            if (fans.Count == 0)
            {
                var biosTemp = _wmiBios.GetTemperature();
                var cpuTemp = _hwMonitor.GetCpuTemperature();
                var gpuTemp = _hwMonitor.GetGpuTemperature();
                
                // Get fan speed from HP WMI BIOS - returns krpm (0-55 = 0-5500 RPM)
                var fanLevel = _wmiBios.GetFanLevel();
                int fan1Rpm = 0;
                int fan2Rpm = 0;
                int fan1Percent = 0;
                int fan2Percent = 0;
                
                if (fanLevel.HasValue)
                {
                    // Convert krpm to RPM: value * 100 (e.g., 35 = 3500 RPM)
                    fan1Rpm = fanLevel.Value.fan1 * 100;
                    fan2Rpm = fanLevel.Value.fan2 * 100;
                    
                    // Calculate percent: 55 krpm = 100%
                    fan1Percent = Math.Clamp(fanLevel.Value.fan1 * 100 / 55, 0, 100);
                    fan2Percent = Math.Clamp(fanLevel.Value.fan2 * 100 / 55, 0, 100);
                    
                    _logging?.Info($"HP WMI Fan levels: Fan1={fanLevel.Value.fan1} krpm ({fan1Rpm} RPM), Fan2={fanLevel.Value.fan2} krpm ({fan2Rpm} RPM)");
                }

                fans.Add(new FanTelemetry 
                { 
                    Name = "CPU Fan", 
                    SpeedRpm = fan1Rpm, 
                    DutyCyclePercent = fan1Percent, 
                    Temperature = cpuTemp > 0 ? cpuTemp : biosTemp ?? 0
                });
                
                fans.Add(new FanTelemetry 
                { 
                    Name = "GPU Fan", 
                    SpeedRpm = fan2Rpm, 
                    DutyCyclePercent = fan2Percent, 
                    Temperature = gpuTemp
                });
            }

            return fans;
        }

        /// <summary>
        /// Get current GPU power settings.
        /// </summary>
        public (bool customTgp, bool ppab, int dState)? GetGpuPowerSettings()
        {
            return _wmiBios.GetGpuPower();
        }

        /// <summary>
        /// Set GPU power level.
        /// </summary>
        public bool SetGpuPower(HpWmiBios.GpuPowerLevel level)
        {
            return _wmiBios.SetGpuPower(level);
        }

        /// <summary>
        /// Get current GPU mode.
        /// </summary>
        public HpWmiBios.GpuMode? GetGpuMode()
        {
            return _wmiBios.GetGpuMode();
        }

        private HpWmiBios.FanMode MapPresetToFanMode(FanPreset preset)
        {
            // Check preset's FanMode enum first (if specified)
            switch (preset.Mode)
            {
                case Models.FanMode.Max:
                    return HpWmiBios.FanMode.Performance; // Max preset uses Performance thermal policy
                case Models.FanMode.Performance:
                    return HpWmiBios.FanMode.Performance;
                case Models.FanMode.Quiet:
                    return HpWmiBios.FanMode.Cool;
            }
            
            // Check preset name for hints
            var nameLower = preset.Name.ToLowerInvariant();
            
            // Max preset should use Performance mode for aggressive thermal management
            if (nameLower.Contains("max") && !nameLower.Contains("auto"))
            {
                return HpWmiBios.FanMode.Performance;
            }
            
            if (nameLower.Contains("quiet") || nameLower.Contains("silent") || nameLower.Contains("cool"))
            {
                return HpWmiBios.FanMode.Cool;
            }
            
            if (nameLower.Contains("performance") || nameLower.Contains("turbo") || nameLower.Contains("gaming"))
            {
                return HpWmiBios.FanMode.Performance;
            }

            // Map based on preset curve characteristics
            var maxFan = preset.Curve.Any() ? preset.Curve.Max(p => p.FanPercent) : 50;
            var avgFan = preset.Curve.Any() ? preset.Curve.Average(p => p.FanPercent) : 50;
            
            // Use curve characteristics
            if (avgFan < 40)
            {
                return HpWmiBios.FanMode.Cool;
            }
            
            if (avgFan > 70 || maxFan > 90)
            {
                return HpWmiBios.FanMode.Performance;
            }

            return HpWmiBios.FanMode.Default;
        }

        private void ApplyGpuPowerFromPreset(FanPreset preset)
        {
            var nameLower = preset.Name.ToLowerInvariant();
            
            if (nameLower.Contains("performance") || nameLower.Contains("turbo") || nameLower.Contains("gaming"))
            {
                _wmiBios.SetGpuPower(HpWmiBios.GpuPowerLevel.Maximum);
            }
            else if (nameLower.Contains("quiet") || nameLower.Contains("silent") || nameLower.Contains("battery"))
            {
                _wmiBios.SetGpuPower(HpWmiBios.GpuPowerLevel.Minimum);
            }
            else
            {
                _wmiBios.SetGpuPower(HpWmiBios.GpuPowerLevel.Medium);
            }
        }

        private int EstimateDutyFromRpm(int rpm)
        {
            if (rpm == 0) return 0;
            
            const int minRpm = 1500;
            const int maxRpm = 6000;
            
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        /// <summary>
        /// Start the countdown extension timer to prevent BIOS from reverting fan settings.
        /// HP BIOS will revert to default fan control after 120 seconds.
        /// This timer re-applies the current settings every 90 seconds to keep them active.
        /// </summary>
        public void StartCountdownExtension()
        {
            if (_countdownExtensionEnabled) return;
            
            _countdownExtensionTimer = new Timer(CountdownExtensionCallback, null, 
                CountdownExtensionIntervalMs, CountdownExtensionIntervalMs);
            _countdownExtensionEnabled = true;
            _logging?.Info("✓ Fan countdown extension enabled (prevents settings reverting)");
        }
        
        /// <summary>
        /// Stop the countdown extension timer.
        /// </summary>
        public void StopCountdownExtension()
        {
            if (!_countdownExtensionEnabled) return;
            
            _countdownExtensionTimer?.Dispose();
            _countdownExtensionTimer = null;
            _countdownExtensionEnabled = false;
            _logging?.Info("Fan countdown extension stopped");
        }
        
        /// <summary>
        /// Countdown extension callback - periodically extends the BIOS timer.
        /// </summary>
        private void CountdownExtensionCallback(object? state)
        {
            if (!IsAvailable || _disposed) return;
            
            try
            {
                // Only extend if we have an active non-default mode
                if (_lastMode != HpWmiBios.FanMode.Default && _lastMode != HpWmiBios.FanMode.LegacyDefault)
                {
                    if (_wmiBios.ExtendFanCountdown())
                    {
                        _logging?.Info($"Fan countdown extended (mode: {_lastMode})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan countdown: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopCountdownExtension();
                RestoreAutoControl();
                _wmiBios.Dispose();
                _disposed = true;
            }
        }
    }
}
