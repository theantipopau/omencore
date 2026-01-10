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
    /// 
    /// Note: Some newer models (OMEN Transcend, 2024+ models) may return success
    /// from WMI commands but not actually change fan behavior. The verification
    /// system can detect this and report if a different backend should be used.
    /// </summary>
    public class WmiFanController : IDisposable
    {
        private readonly HpWmiBios _wmiBios;
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly LoggingService? _logging;
        private bool _disposed;

        // Manual fan control state
        private HpWmiBios.FanMode _lastMode = HpWmiBios.FanMode.Default;
        private bool _isMaxModeActive = false;  // Track if SetFanMax(true) is active
        private int _lastManualFanPercent = -1; // Track last manual fan percentage for re-apply
        
        // Fan level constants - HP WMI uses 0-55 krpm range (0-5500 RPM)
        private const int MaxFanLevel = 55;
        
        // Countdown extension timer - keeps fan settings from reverting
        private Timer? _countdownExtensionTimer;
        private const int CountdownExtensionIntervalMs = 30000; // 30 seconds (more aggressive to handle load changes)
        private bool _countdownExtensionEnabled = false;
        
        // Command verification tracking
        private int _commandVerifyFailCount = 0;
        private int? _lastCommandRpmBefore = null;
        private const int VerifyDelayMs = 3000; // Wait 3 seconds for fans to respond
        private const int VerifyThreshold = 3; // After 3 verified failures, mark as ineffective

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
        
        /// <summary>
        /// Indicates if WMI commands appear to be ineffective (return success but no change).
        /// On some newer OMEN models (Transcend, 2024+), WMI may report success but
        /// not actually change fan speed. In this case, OGH proxy should be used.
        /// </summary>
        public bool CommandsIneffective => _commandVerifyFailCount >= VerifyThreshold;
        
        /// <summary>
        /// Number of times commands returned success but verification showed no change.
        /// </summary>
        public int VerifyFailCount => _commandVerifyFailCount;

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
                // 
                // NOTE: We intentionally do NOT change GPU power here.
                // "Max cooling" means maximum fan speed to reduce temps, not increased GPU power.
                // Increasing GPU power (TGP/PPAB) would generate MORE heat, counteracting cooling.
                // User can independently control GPU power via the dedicated GPU Power settings.
                
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
                        _isMaxModeActive = true;      // Track max mode for countdown extension
                        _lastManualFanPercent = 100;
                        // GPU power left unchanged - max fans is for COOLING, not more power
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
                            _isMaxModeActive = true;
                            _lastManualFanPercent = 100;
                            return true;
                        }
                    }
                    return false;
                }
                
                // For non-Max presets, only run reset sequence if we were in max mode
                // This avoids unnecessary delays when switching between non-max presets
                if (IsManualControlActive)
                {
                    ResetFromMaxMode();
                }
                
                // Map preset to fan mode
                var mode = MapPresetToFanMode(preset);
                
                if (_wmiBios.SetFanMode(mode))
                {
                    _lastMode = mode;
                    IsManualControlActive = false;
                    _isMaxModeActive = false;          // Clear max mode flag
                    _lastManualFanPercent = -1;
                    
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
                    
                    // NOTE: GPU Power Boost is now controlled independently via System tab
                    // We no longer override the user's GPU power setting when applying fan presets
                    // This fixes the bug where Maximum TGP was being reset to Medium
                    
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
                // SAFETY: If temp exceeds all curve points, use LAST point (highest fan%)
                // This prevents fans from dropping to low speed at high temps
                var targetPoint = curveList.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                  ?? curveList.Last(); // Use highest, not lowest!

                // Convert percentage to krpm (0-100% maps to 0-MaxFanLevel)
                byte fanLevel = (byte)(targetPoint.FanPercent * MaxFanLevel / 100);

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
        /// For 100%, uses SetFanMax(true) to achieve maximum RPM beyond SetFanLevel limits.
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
                bool success;
                
                // For 100%, use SetFanMax which bypasses BIOS power limits
                // SetFanLevel(55) may be capped by BIOS, but SetFanMax achieves true max RPM
                if (percent >= 100)
                {
                    success = _wmiBios.SetFanMax(true);
                    if (success)
                    {
                        _logging?.Info($"✓ Fan speed set to MAX (100%) via SetFanMax");
                    }
                    else
                    {
                        // Fallback to SetFanLevel if SetFanMax fails
                        success = _wmiBios.SetFanLevel(55, 55);
                        if (success)
                        {
                            _logging?.Info($"✓ Fan speed set to 100% via SetFanLevel(55) fallback");
                        }
                    }
                }
                else
                {
                    // For <100%, disable max mode first (in case it was enabled)
                    _wmiBios.SetFanMax(false);
                    
                    // Convert percentage to krpm level
                    byte fanLevel = (byte)(percent * MaxFanLevel / 100);
                    success = _wmiBios.SetFanLevel(fanLevel, fanLevel);
                    
                    if (success)
                    {
                        _logging?.Info($"✓ Fan speed set to {percent}% (Level: {fanLevel})");
                    }
                }
                
                if (success)
                {
                    IsManualControlActive = true;
                    _isMaxModeActive = percent >= 100;  // Track if we're at max
                    _lastManualFanPercent = percent;     // Track for re-apply
                    
                    // Start countdown extension to prevent BIOS from reverting
                    // HP BIOS has a 120-second timeout that resets fan control to auto
                    StartCountdownExtension();
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan speed: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Set fan speeds independently for CPU and GPU fans.
        /// Allows different curves for each component for optimized cooling.
        /// </summary>
        /// <param name="cpuPercent">CPU fan speed percentage (0-100)</param>
        /// <param name="gpuPercent">GPU fan speed percentage (0-100)</param>
        /// <returns>True if both fans were set successfully</returns>
        public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot set fan speeds: WMI BIOS not available");
                return false;
            }

            cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            gpuPercent = Math.Clamp(gpuPercent, 0, 100);

            try
            {
                bool success;
                
                // If both are 100%, use SetFanMax for true maximum
                if (cpuPercent >= 100 && gpuPercent >= 100)
                {
                    success = _wmiBios.SetFanMax(true);
                    if (success)
                    {
                        _logging?.Info($"✓ Both fans set to MAX (100%) via SetFanMax");
                    }
                    else
                    {
                        success = _wmiBios.SetFanLevel(55, 55);
                        if (success)
                        {
                            _logging?.Info($"✓ Both fans set to 100% via SetFanLevel(55,55) fallback");
                        }
                    }
                }
                else
                {
                    // Disable max mode first
                    _wmiBios.SetFanMax(false);
                    
                    // Convert percentages to krpm levels (0-55 range)
                    byte cpuLevel = (byte)(cpuPercent * MaxFanLevel / 100);
                    byte gpuLevel = (byte)(gpuPercent * MaxFanLevel / 100);
                    
                    success = _wmiBios.SetFanLevel(cpuLevel, gpuLevel);
                    
                    if (success)
                    {
                        _logging?.Info($"✓ Fan speeds set: CPU={cpuPercent}% (L{cpuLevel}), GPU={gpuPercent}% (L{gpuLevel})");
                    }
                }
                
                if (success)
                {
                    IsManualControlActive = true;
                    _isMaxModeActive = cpuPercent >= 100 && gpuPercent >= 100;
                    _lastManualFanPercent = Math.Max(cpuPercent, gpuPercent);
                    StartCountdownExtension();
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan speeds: {ex.Message}", ex);
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
                
                // Start/stop countdown extension to prevent BIOS from reverting
                // Performance and Cool modes need extension; Default lets BIOS take over
                if (fanMode == HpWmiBios.FanMode.Default || fanMode == HpWmiBios.FanMode.LegacyDefault)
                {
                    StopCountdownExtension();
                }
                else
                {
                    // Performance/Cool modes need countdown extension to maintain TDP settings
                    StartCountdownExtension();
                }
                
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
                // Use the robust reset sequence
                ResetFromMaxMode();
                
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
        /// Robust reset sequence to exit MAX fan mode.
        /// Some HP BIOS versions require multiple steps to properly exit max mode.
        /// Based on OmenMon's approach and user feedback from Issue #7.
        /// </summary>
        private void ResetFromMaxMode()
        {
            _logging?.Info("Executing MAX mode reset sequence...");
            
            try
            {
                // Step 1: Disable max fan speed
                if (_wmiBios.SetFanMax(false))
                {
                    _logging?.Info("  Step 1: SetFanMax(false) succeeded");
                }
                else
                {
                    _logging?.Warn("  Step 1: SetFanMax(false) failed");
                }
                
                // Small delay between commands (reduced from 50ms to 25ms)
                System.Threading.Thread.Sleep(25);
                
                // Step 2: Reset thermal policy to Default
                // This forces BIOS to reconsider fan control
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Default))
                {
                    _logging?.Info("  Step 2: SetFanMode(Default) succeeded");
                }
                
                System.Threading.Thread.Sleep(25);
                
                // Step 3: Set fan levels to minimum (20 krpm = ~2000 RPM)
                // This gives BIOS a "hint" to reduce speed
                if (_wmiBios.SetFanLevel(20, 20))
                {
                    _logging?.Info("  Step 3: SetFanLevel(20, 20) succeeded");
                }
                
                System.Threading.Thread.Sleep(50);
                
                // Step 4: Set fan levels to 0 to let BIOS take over
                if (_wmiBios.SetFanLevel(0, 0))
                {
                    _logging?.Info("  Step 4: SetFanLevel(0, 0) succeeded");
                }
                
                // Final delay to let BIOS process (reduced from 100ms to 50ms)
                System.Threading.Thread.Sleep(50);
                
                _logging?.Info("MAX mode reset sequence completed");
            }
            catch (Exception ex)
            {
                _logging?.Warn($"MAX mode reset sequence error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset EC to factory defaults.
        /// Performs a comprehensive reset sequence to restore BIOS control and clear all manual overrides.
        /// This fixes stuck fan readings, incorrect BIOS display values, and other EC-related issues.
        /// </summary>
        public bool ResetEcToDefaults()
        {
            if (!IsAvailable)
            {
                _logging?.Warn("Cannot reset EC: WMI BIOS not available");
                return false;
            }
            
            _logging?.Info("═══════════════════════════════════════════════════");
            _logging?.Info("Starting EC Reset to Defaults...");
            _logging?.Info("═══════════════════════════════════════════════════");
            
            try
            {
                // Step 1: Disable max fan speed
                _logging?.Info("Step 1: Disabling max fan mode...");
                _wmiBios.SetFanMax(false);
                System.Threading.Thread.Sleep(50);
                
                // Step 2: Set fan mode to Default (restores BIOS thermal policy)
                _logging?.Info("Step 2: Setting fan mode to Default...");
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                System.Threading.Thread.Sleep(50);
                
                // Step 3: Reset fan levels to 0 (let BIOS control)
                _logging?.Info("Step 3: Clearing manual fan levels...");
                _wmiBios.SetFanLevel(0, 0);
                System.Threading.Thread.Sleep(50);
                
                // Step 4: Extend countdown to prevent immediate timeout
                _logging?.Info("Step 4: Extending BIOS countdown timer...");
                _wmiBios.ExtendFanCountdown();
                System.Threading.Thread.Sleep(50);
                
                // Step 5: Set performance mode to Balanced
                _logging?.Info("Step 5: Setting performance mode to Balanced...");
                SetPerformanceMode("Balanced");
                System.Threading.Thread.Sleep(100);
                
                // Step 6: Final fan mode reset to Default
                _logging?.Info("Step 6: Final fan mode reset to Default...");
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                
                // Clear internal state
                IsManualControlActive = false;
                _isMaxModeActive = false;
                _lastMode = HpWmiBios.FanMode.Default;
                
                _logging?.Info("═══════════════════════════════════════════════════");
                _logging?.Info("✓ EC Reset to Defaults completed successfully");
                _logging?.Info("  BIOS should now have full control of fans");
                _logging?.Info("═══════════════════════════════════════════════════");
                
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"EC Reset failed: {ex.Message}", ex);
                return false;
            }
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

        // NOTE: ApplyGpuPowerFromPreset() was removed in v1.5.0-beta3
        // GPU Power Boost is now controlled independently via the System tab
        // Fan presets no longer override the user's GPU power setting

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
        /// For Max mode, also re-applies SetFanMax(true) to ensure it stays active.
        /// HP BIOS is aggressive about reverting fan settings, especially under load.
        /// </summary>
        private void CountdownExtensionCallback(object? state)
        {
            if (!IsAvailable || _disposed) return;
            
            try
            {
                // Extend countdown for any manual control mode (preset or custom curve)
                // IsManualControlActive is set when we apply fan levels directly
                if (IsManualControlActive || (_lastMode != HpWmiBios.FanMode.Default && _lastMode != HpWmiBios.FanMode.LegacyDefault))
                {
                    // For Max mode, re-apply SetFanMax(true) to ensure it stays active
                    // This is more aggressive than just extending countdown, but necessary
                    // because HP BIOS can reset max mode under high load conditions
                    if (_isMaxModeActive)
                    {
                        if (_wmiBios.SetFanMax(true))
                        {
                            _logging?.Info("Fan Max mode re-applied via countdown extension");
                        }
                        else
                        {
                            _logging?.Warn("Failed to re-apply Max mode - trying fallback");
                            // Fallback: try setting level to max
                            _wmiBios.SetFanLevel(55, 55);
                        }
                    }
                    else if (_lastManualFanPercent >= 0)
                    {
                        // For custom fan curves, re-apply the last set percentage
                        // This combats BIOS resetting under load
                        byte fanLevel = (byte)(_lastManualFanPercent * MaxFanLevel / 100);
                        if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                        {
                            _logging?.Info($"Fan level re-applied: {_lastManualFanPercent}% via countdown extension");
                        }
                    }
                    else if (_wmiBios.ExtendFanCountdown())
                    {
                        _logging?.Info($"Fan countdown extended (manual: {IsManualControlActive}, mode: {_lastMode})");
                    }
                    else
                    {
                        _logging?.Warn("Fan countdown extension failed - BIOS may revert to auto");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan countdown: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Test if WMI commands actually affect fan speed.
        /// Some newer OMEN models (Transcend, 2024+) return success but don't change speed.
        /// This helps detect if OGH proxy should be used instead.
        /// </summary>
        /// <returns>True if commands appear effective, false if they seem to have no effect</returns>
        public bool TestCommandEffectiveness()
        {
            if (!IsAvailable)
                return false;
                
            _logging?.Info("Testing WMI command effectiveness...");
            
            try
            {
                // Get baseline RPM
                var initialSpeeds = ReadFanSpeeds().ToList();
                var initialRpm = initialSpeeds.FirstOrDefault()?.SpeedRpm ?? 0;
                _logging?.Info($"  Initial fan RPM: {initialRpm}");
                
                // Try setting max fan mode
                bool setResult = _wmiBios.SetFanMax(true);
                _logging?.Info($"  SetFanMax(true) returned: {setResult}");
                
                if (!setResult)
                {
                    _logging?.Warn("  WMI command returned failure - commands may not work on this model");
                    return false;
                }
                
                // Wait for fans to respond (they have inertia)
                System.Threading.Thread.Sleep(VerifyDelayMs);
                
                // Check if RPM increased
                var newSpeeds = ReadFanSpeeds().ToList();
                var newRpm = newSpeeds.FirstOrDefault()?.SpeedRpm ?? 0;
                _logging?.Info($"  New fan RPM after max command: {newRpm}");
                
                // Restore default mode
                _wmiBios.SetFanMax(false);
                _wmiBios.SetFanMode(HpWmiBios.FanMode.Default);
                
                // If initial RPM was already high (> 4000), command might not show increase
                // In that case, we consider it likely working
                if (initialRpm >= 4000)
                {
                    _logging?.Info("  Initial RPM already high - assuming commands work");
                    return true;
                }
                
                // If RPM increased by at least 500, commands are working
                int rpmIncrease = newRpm - initialRpm;
                if (rpmIncrease >= 500)
                {
                    _logging?.Info($"  ✓ Fan RPM increased by {rpmIncrease} - commands are effective");
                    return true;
                }
                
                // Commands returned success but no RPM change
                _commandVerifyFailCount++;
                _logging?.Warn($"  ⚠️ WMI returned success but fan RPM didn't change significantly (+{rpmIncrease})");
                _logging?.Warn($"  This model may require OGH proxy for fan control (fail count: {_commandVerifyFailCount})");
                
                if (CommandsIneffective)
                {
                    _logging?.Error("  ❌ WMI commands confirmed ineffective on this model");
                    _logging?.Info("  → Consider using OGH proxy or reinstalling OMEN Gaming Hub");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Command effectiveness test failed: {ex.Message}");
                return true; // Assume working on exception to avoid false negatives
            }
        }
        
        /// <summary>
        /// Record the current fan RPM before sending a command.
        /// Call VerifyCommandEffect() after the command to check if it worked.
        /// </summary>
        public void RecordPreCommandRpm()
        {
            try
            {
                var speeds = ReadFanSpeeds().ToList();
                _lastCommandRpmBefore = speeds.FirstOrDefault()?.SpeedRpm ?? 0;
            }
            catch
            {
                _lastCommandRpmBefore = null;
            }
        }
        
        /// <summary>
        /// Verify if the last command had an effect on fan speed.
        /// Should be called ~3 seconds after a fan command.
        /// </summary>
        /// <param name="wasIncreaseExpected">True if we expected fans to speed up, false if slow down</param>
        /// <returns>True if command appeared effective</returns>
        public bool VerifyCommandEffect(bool wasIncreaseExpected)
        {
            if (!_lastCommandRpmBefore.HasValue)
                return true; // No baseline, assume success
                
            try
            {
                var speeds = ReadFanSpeeds().ToList();
                var currentRpm = speeds.FirstOrDefault()?.SpeedRpm ?? 0;
                var rpmChange = currentRpm - _lastCommandRpmBefore.Value;
                
                bool effectDetected;
                if (wasIncreaseExpected)
                {
                    // For increase commands, we expect at least 300 RPM increase
                    effectDetected = rpmChange >= 300 || currentRpm >= 4000;
                }
                else
                {
                    // For decrease commands, we expect at least 300 RPM decrease
                    effectDetected = rpmChange <= -300 || currentRpm <= 2500;
                }
                
                if (!effectDetected)
                {
                    _commandVerifyFailCount++;
                    _logging?.Warn($"Command verification failed: RPM change was {rpmChange} (expected {(wasIncreaseExpected ? "increase" : "decrease")})");
                }
                
                _lastCommandRpmBefore = null;
                return effectDetected;
            }
            catch
            {
                _lastCommandRpmBefore = null;
                return true; // Assume success on error
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
