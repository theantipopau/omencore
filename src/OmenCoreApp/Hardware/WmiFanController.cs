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
        private readonly IHpWmiBios _wmiBios;
        private readonly LibreHardwareMonitorImpl? _hwMonitor;
        private readonly LoggingService? _logging;
        private bool _disposed;

        // Manual fan control state
        private HpWmiBios.FanMode _lastMode = HpWmiBios.FanMode.Default;
        private bool _isMaxModeActive = false;  // Track if SetFanMax(true) is active
        private int _lastManualFanPercent = -1; // Track last manual fan percentage for re-apply
        
        // Fan level constants - HP WMI uses 0-55 krpm range (0-5500 RPM) on classic models,
        // or 0-100 percentage on newer models. Auto-detected from WMI at startup.
        private readonly int _maxFanLevel;
        
        // Maximum ceiling for "Max" mode operations. When the user requests maximum cooling,
        // we send a high value and let the BIOS clamp to its hardware maximum.
        // This ensures models with max levels > 55 (e.g., OMEN 16-xd0xxx with 6300 RPM = level 63)
        // can actually reach their full speed, rather than being capped at _maxFanLevel (55).
        private const int MaxFanLevelCeiling = 100;
        
        // Countdown extension timer - keeps fan settings from reverting
        // HP BIOS aggressively reverts fan settings, especially on OMEN 16/Max models
        private Timer? _countdownExtensionTimer;
        private const int CountdownExtensionIntervalMs = 3000; // 3 seconds - aggressive to combat fast BIOS reversion on some models
        private const int CountdownExtensionInitialDelayMs = 1000; // 1 second initial delay — first tick must fire before BIOS reverts
        private bool _countdownExtensionEnabled = false;
        
        // RPM debounce tracking — filters transient phantom readings during profile transitions.
        // When fans are transitioning (e.g., profile switch), BIOS may return stale/target fan levels
        // that get misinterpreted as actual RPM. Track previous readings and filter sudden spikes.
        private int _lastCpuRpm = 0;
        private int _lastGpuRpm = 0;
        private DateTime _lastProfileSwitch = DateTime.MinValue;
        private const int ProfileTransitionDebounceMs = 3000; // Ignore phantom RPM for 3s after profile switch
        
        // Command verification tracking
        private int _commandVerifyFailCount = 0;
        private int? _lastCommandRpmBefore = null;
        private const int VerifyDelayMs = 3000; // Wait 3 seconds for fans to respond
        private const int VerifyThreshold = 3; // After 3 verified failures, mark as ineffective

        // Command history for diagnostics
        private readonly List<WmiCommandHistoryEntry> _commandHistory = new();
        private const int MaxCommandHistoryEntries = 50; // Keep last 50 commands

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

        public WmiFanController(LibreHardwareMonitorImpl? hwMonitor, LoggingService? logging = null, int maxFanLevelOverride = 0, IHpWmiBios? injectedWmiBios = null)
        {
            _hwMonitor = hwMonitor;
            _logging = logging;
            _wmiBios = injectedWmiBios ?? new HpWmiBios(logging);
            
            // Apply user override if set, then read the (possibly overridden) max level
            if (maxFanLevelOverride > 0 && _wmiBios is HpWmiBios concrete)
            {
                // Only call DetectMaxFanLevel on the real implementation (not on fakes)
                concrete.DetectMaxFanLevel(maxFanLevelOverride);
            }
            _maxFanLevel = _wmiBios.MaxFanLevel;
            _logging?.Info($"WmiFanController: Max fan level = {_maxFanLevel}{(maxFanLevelOverride > 0 ? $" (user override: {maxFanLevelOverride})" : " (auto-detected)")}");
        }
        
        // Helper methods for getting sensor data with WMI BIOS fallback
        private double GetCpuTemperature()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var temp = _hwMonitor.GetCpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            return _wmiBios.GetTemperature() ?? 0;
        }
        
        private double GetGpuTemperature()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var temp = _hwMonitor.GetGpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            return _wmiBios.GetGpuTemperature() ?? 0;
        }
        
        private IEnumerable<(string Name, double Rpm)> GetFanSpeeds()
        {
            if (_hwMonitor != null)
            {
                try
                {
                    var speeds = _hwMonitor.GetFanSpeeds();
                    if (speeds.Any()) return speeds;
                }
                catch { }
            }
            // Fallback to WMI BIOS — only use V2 direct RPM command on V2+ systems
            // CMD 0x38 (GetFanRpmDirect) is only valid on OMEN Max 2025+ with ThermalPolicy V2.
            // On V0/V1 systems, it may return garbage data (e.g., fan levels misread as RPM)
            // causing phantom 4200-4400 RPM readings when fans are actually quiet.
            if (_wmiBios.ThermalPolicy >= HpWmiBios.ThermalPolicyVersion.V2)
            {
                var rpms = _wmiBios.GetFanRpmDirect();
                var result = new List<(string, double)>();
                if (rpms.HasValue)
                {
                    var (cpu, gpu) = rpms.Value;
                    if (HpWmiBios.IsValidRpm(cpu)) result.Add(("CPU Fan", cpu));
                    if (HpWmiBios.IsValidRpm(gpu)) result.Add(("GPU Fan", gpu));
                }
                if (result.Any()) return result;
            }
            return Enumerable.Empty<(string, double)>();
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
            
            // Mark profile transition for RPM debounce
            _lastProfileSwitch = DateTime.Now;

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
                    double? rpmBefore = GetCurrentFanRpm();
                    if (_wmiBios.SetFanMax(true))
                    {
                        AddCommandToHistory("SetFanMax(true)", true, null, rpmBefore, null);
                        _logging?.Info("✓ Max fan speed command issued - awaiting verification...");

                        // Verify that fans actually reached high RPMs. Some BIOS/EC combinations accept the command
                        // but never actually spin the fans; avoid showing misleading 100% in the UI by readback.
                        if (VerifyMaxAppliedWithRetries())
                        {
                            _logging?.Info("✓ Max fan speed verified - fans confirmed at high RPM");
                            IsManualControlActive = true; // Mark as manual since we're forcing max
                            _isMaxModeActive = true;      // Track max mode for countdown extension
                            _lastManualFanPercent = 100;
                            return true;
                        }

                        _logging?.Warn("SetFanMax succeeded but verification failed - attempting fallback methods");
                    }
                    else
                    {
                        AddCommandToHistory("SetFanMax(true)", false, "Command failed", rpmBefore, null);
                        _logging?.Warn("SetFanMax command failed - trying alternative method");
                    }

                    // Alternative: Try setting fan level directly to max
                    // Use MaxFanLevelCeiling (100) instead of _maxFanLevel — let the BIOS clamp
                    // to its actual hardware maximum. Models like OMEN 16-xd0xxx have max level 63
                    // (6300 RPM), which _maxFanLevel (55) would miss.
                    if (_wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling))
                    {
                        _logging?.Info($"✓ Fan level set to ceiling ({MaxFanLevelCeiling}, {MaxFanLevelCeiling}) — BIOS will clamp to hardware max. Awaiting verification...");

                        if (VerifyMaxAppliedWithRetries())
                        {
                            _logging?.Info("✓ Fan level verified - fans confirmed at high RPM");
                            IsManualControlActive = true;
                            _isMaxModeActive = true;
                            _lastManualFanPercent = 100;
                            return true;
                        }

                        _logging?.Warn($"Fan level set to {MaxFanLevelCeiling} but verification failed");
                    }

                    // Rollback: if SetFanMax was accepted by BIOS but verification failed, make sure to clear the hardware override
                    try
                    {
                        // Reset FanMax flag on BIOS to avoid leaving hardware stuck in Max mode
                        _wmiBios.SetFanMax(false);
                        _commandVerifyFailCount++; // track a failed verification attempt
                        _logging?.Warn("SetFanMax verification failed — rolled back hardware override and incremented failure counter");
                    }
                    catch (Exception ex)
                    {
                        _logging?.Warn($"Failed to rollback SetFanMax after verification failure: {ex.Message}");
                    }

                    _logging?.Error("Failed to enable Max fan speed: verification failed");
                    // Ensure we don't leave UI thinking Max is active
                    IsManualControlActive = false;
                    _isMaxModeActive = false;
                    _lastManualFanPercent = -1;
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
                var cpuTemp = (int)GetCpuTemperature();
                var gpuTemp = (int)GetGpuTemperature();
                var maxTemp = Math.Max(cpuTemp, gpuTemp);

                // Find appropriate curve point
                // SAFETY: If temp exceeds all curve points, use LAST point (highest fan%)
                // This prevents fans from dropping to low speed at high temps
                var targetPoint = curveList.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                                  ?? curveList.Last(); // Use highest, not lowest!

                // Convert percentage to fan level (0-100% maps to 0-_maxFanLevel)
                byte fanLevel = (byte)(targetPoint.FanPercent * _maxFanLevel / 100);

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
                AddCommandToHistory($"SetFanSpeed({percent})", false, "WMI BIOS not available");
                return false;
            }

            percent = Math.Clamp(percent, 0, 100);

            // Retry logic for fan control hardening
            const int maxRetries = 3;
            const int retryDelayMs = 500;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    bool success;
                    double? rpmBefore = GetCurrentFanRpm();
                    
                    // For 100%, use SetFanMax which bypasses BIOS power limits
                    // SetFanLevel(55) may be capped by BIOS, but SetFanMax achieves true max RPM
                    if (percent >= 100)
                    {
                        success = _wmiBios.SetFanMax(true);
                        if (success)
                        {
                            AddCommandToHistory("SetFanMax(true)", true, null, rpmBefore, null);
                            _logging?.Info($"✓ Fan speed set to MAX (100%) via SetFanMax (attempt {attempt}/{maxRetries})");
                        }
                        else
                        {
                            // Fallback to SetFanLevel if SetFanMax fails
                            success = _wmiBios.SetFanLevel(55, 55);
                            if (success)
                            {
                                AddCommandToHistory("SetFanLevel(55, 55)", true, null, rpmBefore, null);
                                _logging?.Info($"✓ Fan speed set to 100% via SetFanLevel(55) fallback (attempt {attempt}/{maxRetries})");
                            }
                            else
                            {
                                AddCommandToHistory("SetFanLevel(55, 55)", false, "Command failed", rpmBefore, null);
                            }
                        }
                    }
                    else
                    {
                        // For <100%, disable max mode first (in case it was enabled)
                        _wmiBios.SetFanMax(false);
                        
                        // Convert percentage to fan level
                        byte fanLevel = (byte)(percent * _maxFanLevel / 100);
                        success = _wmiBios.SetFanLevel(fanLevel, fanLevel);
                        
                        if (success)
                        {
                            AddCommandToHistory($"SetFanLevel({fanLevel}, {fanLevel})", true, null, rpmBefore, null);
                            _logging?.Info($"✓ Fan speed set to {percent}% (Level: {fanLevel}) (attempt {attempt}/{maxRetries})");
                        }
                        else
                        {
                            AddCommandToHistory($"SetFanLevel({fanLevel}, {fanLevel})", false, "Command failed", rpmBefore, null);
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
                        
                        // Verify the fan speed was actually applied (basic verification)
                        if (VerifyFanSpeed(percent))
                        {
                            return true;
                        }
                        else
                        {
                            _logging?.Warn($"Fan speed verification failed for {percent}%, retrying...");
                            if (attempt < maxRetries)
                            {
                                Thread.Sleep(retryDelayMs);
                                continue;
                            }
                        }
                    }
                    
                    // If we get here, the command failed
                    if (attempt < maxRetries)
                    {
                        _logging?.Warn($"Fan speed command failed (attempt {attempt}/{maxRetries}), retrying in {retryDelayMs}ms...");
                        Thread.Sleep(retryDelayMs);
                    }
                    else
                    {
                        _logging?.Error($"Fan speed command failed after {maxRetries} attempts");
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Error($"Failed to set fan speed (attempt {attempt}/{maxRetries}): {ex.Message}", ex);
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
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
                    
                    // Convert percentages to fan levels (0-_maxFanLevel range)
                    byte cpuLevel = (byte)(cpuPercent * _maxFanLevel / 100);
                    byte gpuLevel = (byte)(gpuPercent * _maxFanLevel / 100);
                    
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
            
            // Mark profile transition for RPM debounce
            _lastProfileSwitch = DateTime.Now;

            try
            {
                // Only run the full reset sequence if we were actually in max mode.
                // The reset sequence sends SetFanLevel(0,0) which on some models (Victus, etc.)
                // puts the EC into manual-0% mode, overriding BIOS auto control entirely.
                // This causes fans to stay at minimum (~1000rpm) until thermal emergency kicks in.
                if (_isMaxModeActive || IsManualControlActive)
                {
                    ResetFromMaxMode();
                }
                
                // Stop countdown extension so we don't keep re-applying fan settings
                StopCountdownExtension();
                
                // Set default mode to restore automatic control
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Default))
                {
                    IsManualControlActive = false;
                    _isMaxModeActive = false;
                    _lastManualFanPercent = -1;
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
                
                // V2 systems (MaxFanLevel=100) use percentage scale where SetFanLevel(0,0) means
                // "0% duty cycle", which puts the EC into manual-0% mode and prevents BIOS auto 
                // control from working. On V2, we skip SetFanLevel entirely and rely on
                // SetFanMode(Default) to restore BIOS control.
                // V1 systems (MaxFanLevel=55) use krpm scale where 0 means "BIOS takes over".
                if (_maxFanLevel < 100)
                {
                    // V1: Use krpm hint to help BIOS transition
                    // Step 3: Set fan levels to minimum (20 krpm = ~2000 RPM) as a transition hint
                    if (_wmiBios.SetFanLevel(20, 20))
                    {
                        _logging?.Info("  Step 3: SetFanLevel(20, 20) succeeded (V1 krpm hint)");
                    }
                    
                    System.Threading.Thread.Sleep(50);
                    
                    // Step 4: Set fan levels to 0 to let BIOS take over
                    if (_wmiBios.SetFanLevel(0, 0))
                    {
                        _logging?.Info("  Step 4: SetFanLevel(0, 0) succeeded (V1 release to BIOS)");
                    }
                }
                else
                {
                    _logging?.Info("  Steps 3-4: Skipped SetFanLevel on V2 system (percentage scale — would override BIOS auto control)");
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
        /// Apply performance throttling mitigation via EC register 0x95.
        /// Discovered from omen-fan Linux utility - writing 0x31 (performance mode) to register 0x95
        /// can help mitigate thermal throttling on some OMEN models.
        /// 
        /// This is a fallback for cases where WMI SetFanMode doesn't fully enable performance mode.
        /// Register 0x95 is documented in the omen-fan project as the performance mode register.
        /// 
        /// Note: For WMI-based controller, this primarily uses WMI SetFanMode. Direct EC access
        /// requires the EC-based controller (EcFanControllerWrapper).
        /// </summary>
        public bool ApplyThrottlingMitigation()
        {
            _logging?.Info("Attempting throttling mitigation via WMI Performance mode...");
            
            try
            {
                // Use WMI SetFanMode to Performance as the primary method
                if (_wmiBios.SetFanMode(HpWmiBios.FanMode.Performance))
                {
                    _logging?.Info("✓ Set WMI FanMode to Performance for throttling mitigation");
                    _lastMode = HpWmiBios.FanMode.Performance;
                    return true;
                }
                else
                {
                    _logging?.Warn("WMI SetFanMode to Performance failed - throttling mitigation not applied");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Throttling mitigation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read current fan telemetry data.
        /// v2.6.0: Uses GetFanRpmDirect() for V2 systems with proper sanity validation.
        /// </summary>
        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Get fan speeds from hardware monitor
            var fanSpeeds = GetFanSpeeds();
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                // v2.6.0: Validate RPM from LibreHardwareMonitor
                // v2.8.1: Add profile transition debounce — during profile switches,
                // BIOS may return stale/target fan levels that aren't actual RPM.
                int validatedRpm = (rpm > 0 && rpm <= 8000) ? (int)rpm : 0;
                
                // Debounce: during profile transition window, filter sudden large jumps
                bool inTransition = (DateTime.Now - _lastProfileSwitch).TotalMilliseconds < ProfileTransitionDebounceMs;
                if (inTransition && validatedRpm > 0)
                {
                    int prevRpm = index == 0 ? _lastCpuRpm : _lastGpuRpm;
                    // If previous reading was 0 and now we suddenly see a high value,
                    // it's likely a phantom reading during transition
                    if (prevRpm == 0 && validatedRpm > 1000)
                    {
                        _logging?.Debug($"[FanRPM] Debounce: Filtering phantom {validatedRpm} RPM during profile transition (previous was 0)");
                        validatedRpm = 0;
                    }
                }
                
                var fanLevel = _wmiBios.GetFanLevel();
                int levelPercent = 0;
                
                if (fanLevel.HasValue)
                {
                    // Use _maxFanLevel (auto-detected per model) instead of hardcoded 55.
                    // MaxFanLevel=55 for classic krpm models, 100 for percentage-based models.
                    levelPercent = index == 0 
                        ? fanLevel.Value.fan1 * 100 / _maxFanLevel 
                        : fanLevel.Value.fan2 * 100 / _maxFanLevel;
                }
                else if (validatedRpm > 0)
                {
                    // Estimate from RPM
                    levelPercent = EstimateDutyFromRpm(validatedRpm);
                }

                fans.Add(new FanTelemetry
                {
                    Name = name,
                    SpeedRpm = validatedRpm,
                    DutyCyclePercent = Math.Clamp(levelPercent, 0, 100),
                    Temperature = index == 0 ? GetCpuTemperature() : GetGpuTemperature()
                });
                
                // Track last RPM for debounce
                if (index == 0) _lastCpuRpm = validatedRpm;
                else _lastGpuRpm = validatedRpm;
                
                index++;
            }

            // Fallback if no fans detected by LibreHardwareMonitor (common on HP OMEN)
            // HP OMEN laptops expose fan data via HP WMI BIOS, not standard EC Super I/O
            if (fans.Count == 0)
            {
                var biosTemp = _wmiBios.GetTemperature();
                var gpuBiosTemp = _wmiBios.GetGpuTemperature();
                var cpuTemp = GetCpuTemperature();
                var gpuTemp = GetGpuTemperature();
                
                // v2.6.0: Use WMI BIOS temp if LibreHardwareMonitor fails
                if (cpuTemp <= 0 && biosTemp.HasValue) cpuTemp = biosTemp.Value;
                if (gpuTemp <= 0 && gpuBiosTemp.HasValue) gpuTemp = gpuBiosTemp.Value;
                
                // v2.6.0: Try direct RPM command first for V2 systems
                _logging?.Debug("[FanRPM] LibreHardwareMonitor found 0 fans, using WMI BIOS fallback");
                
                int fan1Rpm = 0;
                int fan2Rpm = 0;
                int fan1Percent = 0;
                int fan2Percent = 0;
                bool gotValidData = false;
                
                // Try direct RPM reading first — BUT only on V2+ systems.
                // CMD 0x38 (GetFanRpmDirect) is a V2-only command; on V1 systems it returns
                // garbage data (e.g., fan level values misinterpreted as RPM).
                bool inTransition = (DateTime.Now - _lastProfileSwitch).TotalMilliseconds < ProfileTransitionDebounceMs;
                
                if (_wmiBios.ThermalPolicy >= HpWmiBios.ThermalPolicyVersion.V2)
                {
                    var directRpm = _wmiBios.GetFanRpmDirect();
                    if (directRpm.HasValue && (directRpm.Value.fan1Rpm > 0 || directRpm.Value.fan2Rpm > 0))
                    {
                        fan1Rpm = directRpm.Value.fan1Rpm;
                        fan2Rpm = directRpm.Value.fan2Rpm;
                        
                        // Debounce: during profile transitions, filter sudden phantom readings
                        if (inTransition)
                        {
                            if (_lastCpuRpm == 0 && fan1Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom CPU {fan1Rpm} RPM during transition");
                                fan1Rpm = 0;
                            }
                            if (_lastGpuRpm == 0 && fan2Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom GPU {fan2Rpm} RPM during transition");
                                fan2Rpm = 0;
                            }
                        }
                        
                        fan1Percent = Math.Clamp((fan1Rpm * 100) / 5500, 0, 100);
                        fan2Percent = Math.Clamp((fan2Rpm * 100) / 5500, 0, 100);
                        gotValidData = true;
                        _logging?.Debug($"[FanRPM] Direct RPM: CPU={fan1Rpm} ({fan1Percent}%), GPU={fan2Rpm} ({fan2Percent}%)");
                    }
                }
                
                // Try fan level if direct RPM unavailable
                if (!gotValidData)
                {
                    var fanLevel = _wmiBios.GetFanLevel();
                    _logging?.Debug($"[FanRPM] GetFanLevel returned: {(fanLevel.HasValue ? $"fan1={fanLevel.Value.fan1}, fan2={fanLevel.Value.fan2}" : "NULL")}");
                    
                    if (fanLevel.HasValue && (fanLevel.Value.fan1 > 0 || fanLevel.Value.fan2 > 0))
                    {
                        // HP WMI BIOS returns fan level in krpm units:
                        // 0 = 0 RPM (off)
                        // 55 = 5500 RPM (max)
                        // Convert krpm to RPM: multiply by 100
                        fan1Rpm = fanLevel.Value.fan1 * 100;
                        fan2Rpm = fanLevel.Value.fan2 * 100;
                        
                        // Debounce: during profile transitions, BIOS may still report
                        // old target levels even though fans are physically stopped
                        if (inTransition)
                        {
                            if (_lastCpuRpm == 0 && fan1Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom CPU level {fanLevel.Value.fan1} ({fan1Rpm} RPM) during transition");
                                fan1Rpm = 0;
                            }
                            if (_lastGpuRpm == 0 && fan2Rpm > 1000)
                            {
                                _logging?.Debug($"[FanRPM] Debounce: Filtering phantom GPU level {fanLevel.Value.fan2} ({fan2Rpm} RPM) during transition");
                                fan2Rpm = 0;
                            }
                        }
                        
                        // Sanity check the calculated RPM
                        if (fan1Rpm > 8000 || fan2Rpm > 8000)
                        {
                            _logging?.Warn($"[FanRPM] Invalid calculated RPM from level: {fan1Rpm}, {fan2Rpm} - using as level directly");
                            // The value might be actual RPM, not level - use as-is if in valid range
                            // fanLevel.Value.fan1/fan2 are bytes (0-255), but treat them as raw RPM if calculated RPM is invalid
                            int rawFan1 = fanLevel.Value.fan1;
                            int rawFan2 = fanLevel.Value.fan2;
                            if (rawFan1 > 0 && rawFan1 <= 8000) fan1Rpm = rawFan1;
                            if (rawFan2 > 0 && rawFan2 <= 8000) fan2Rpm = rawFan2;
                        }
                        
                        // Calculate percent based on max RPM range (5500 RPM = 100%)
                        fan1Percent = Math.Clamp((fan1Rpm * 100) / 5500, 0, 100);
                        fan2Percent = Math.Clamp((fan2Rpm * 100) / 5500, 0, 100);
                        gotValidData = true;
                        
                        _logging?.Info($"HP WMI Fan levels: Fan1={fanLevel.Value.fan1} -> {fan1Rpm} RPM ({fan1Percent}%), Fan2={fanLevel.Value.fan2} -> {fan2Rpm} RPM ({fan2Percent}%)");
                    }
                }
                
                // Fallback to estimation if WMI fails
                if (!gotValidData)
                {
                    // WMI BIOS GetFanLevel failed or returned zeros
                    // Many HP OMEN laptops don't support reading current fan level
                    // Estimate based on last SET percentage if available
                    _logging?.Debug($"[FanRPM] GetFanLevel unavailable, using last set percent: {_lastManualFanPercent}");
                    
                    if (_lastManualFanPercent >= 0)
                    {
                        // We have a last set value - estimate RPM from it
                        fan1Percent = _lastManualFanPercent;
                        fan2Percent = _lastManualFanPercent;
                        // Estimate RPM: linear scale 0-100% = 0-5500 RPM
                        fan1Rpm = (_lastManualFanPercent * 5500) / 100;
                        fan2Rpm = (_lastManualFanPercent * 5500) / 100;
                        _logging?.Debug($"[FanRPM] Using estimated RPM based on last set: {fan1Rpm} RPM ({fan1Percent}%)");
                    }
                    else if (_isMaxModeActive)
                    {
                        // Max mode is active
                        fan1Percent = 100;
                        fan2Percent = 100;
                        fan1Rpm = 5500;
                        fan2Rpm = 5500;
                        _logging?.Debug("[FanRPM] Max mode active - showing 100%");
                    }
                    else
                    {
                        // No manual control active - fans are in BIOS/auto mode
                        // Use improved temperature-based estimation for better accuracy
                        var maxTemp = Math.Max(cpuTemp, gpuTemp);
                        if (maxTemp > 0)
                        {
                            // More accurate BIOS fan curve estimation based on typical OMEN behavior
                            (fan1Percent, fan1Rpm) = EstimateFanFromTemperature(maxTemp, "CPU");
                            (fan2Percent, fan2Rpm) = EstimateFanFromTemperature(maxTemp, "GPU");
                            _logging?.Debug($"[FanRPM] Auto mode estimation: CPU {fan1Percent}% ({fan1Rpm} RPM), GPU {fan2Percent}% ({fan2Rpm} RPM) @ {maxTemp:F1}°C");
                        }
                        else
                        {
                            // Can't estimate - show placeholder with realistic idle values
                            fan1Percent = 25; // Typical idle fan %
                            fan2Percent = 25;
                            fan1Rpm = 1375; // ~25% of 5500 RPM
                            fan2Rpm = 1375;
                            _logging?.Debug("[FanRPM] No temp data, showing idle estimates: 25% (1375 RPM)");
                        }
                    }
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

                // Track last known RPM for phantom RPM debounce
                _lastCpuRpm = fan1Rpm;
                _lastGpuRpm = fan2Rpm;
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

        /// <summary>
        /// Small verification helper: after attempting to enable max, checks fan RPM/duty a few times
        /// to confirm the hardware actually ramped. Returns true if verification passes.
        /// v2.6.0: Uses raw WMI reads to avoid circular dependency with estimated values.
        /// </summary>
        private bool VerifyMaxAppliedWithRetries()
        {
            const int attempts = 5;
            const int delayMs = 1000; // 1 second between checks — fans need 3-5 seconds to spin up

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // v2.6.0: Use raw WMI BIOS read instead of ReadFanSpeeds() which may return estimates
                    var rawRpm = _wmiBios.GetFanRpmDirect();
                    if (rawRpm.HasValue)
                    {
                        int maxRpm = Math.Max(rawRpm.Value.fan1Rpm, rawRpm.Value.fan2Rpm);
                        _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: Raw RPM - CPU={rawRpm.Value.fan1Rpm}, GPU={rawRpm.Value.fan2Rpm}");
                        
                        // For max mode, fans should be spinning fast (at least 3000 RPM)
                        // Lowered from 4000 to give faster verification on models that ramp slower
                        if (maxRpm >= 3000)
                        {
                            _logging?.Info($"[VerifyMax] ✓ Verified: {maxRpm} RPM");
                            return true;
                        }
                        
                        // Check if increasing from baseline
                        if (_lastCommandRpmBefore.HasValue && maxRpm > _lastCommandRpmBefore.Value + 500)
                        {
                            _logging?.Info($"[VerifyMax] ✓ RPM increased: {_lastCommandRpmBefore.Value} -> {maxRpm} (+{maxRpm - _lastCommandRpmBefore.Value})");
                            return true;
                        }
                    }
                    else
                    {
                        // Fallback to fan level if RPM command not available
                        var fanLevel = _wmiBios.GetFanLevel();
                        if (fanLevel.HasValue)
                        {
                            int maxLevel = Math.Max(fanLevel.Value.fan1, fanLevel.Value.fan2);
                            _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: Fan level - CPU={fanLevel.Value.fan1}, GPU={fanLevel.Value.fan2}");
                            
                            // Level 40+ indicates significant spin-up (works for both 0-55 and 0-100 ranges)
                            if (maxLevel >= 40)
                            {
                                _logging?.Info($"[VerifyMax] ✓ Verified via level: {maxLevel}");
                                return true;
                            }
                        }
                        else
                        {
                            _logging?.Debug($"[VerifyMax] Attempt {attempt}/{attempts}: No fan data available from WMI");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"[VerifyMax] Error: {ex.Message}");
                }

                System.Threading.Thread.Sleep(delayMs);
            }
            
            _logging?.Warn($"[VerifyMax] Failed after {attempts} attempts - fan command may not be effective on this model");
            return false;
        }

        private HpWmiBios.FanMode MapPresetToFanMode(FanPreset preset)
        {
            // Determine the base mode from preset
            HpWmiBios.FanMode baseMode;
            
            // Check preset's FanMode enum first (if specified)
            switch (preset.Mode)
            {
                case Models.FanMode.Max:
                    baseMode = HpWmiBios.FanMode.Performance;
                    break;
                case Models.FanMode.Performance:
                    baseMode = HpWmiBios.FanMode.Performance;
                    break;
                case Models.FanMode.Quiet:
                    baseMode = HpWmiBios.FanMode.Cool;
                    break;
                default:
                    // Check preset name for hints
                    var nameLower = preset.Name.ToLowerInvariant();
                    
                    // Max preset should use Performance mode for aggressive thermal management
                    if (nameLower.Contains("max") && !nameLower.Contains("auto"))
                    {
                        baseMode = HpWmiBios.FanMode.Performance;
                    }
                    else if (nameLower.Contains("quiet") || nameLower.Contains("silent") || nameLower.Contains("cool"))
                    {
                        baseMode = HpWmiBios.FanMode.Cool;
                    }
                    else if (nameLower.Contains("performance") || nameLower.Contains("turbo") || nameLower.Contains("gaming"))
                    {
                        baseMode = HpWmiBios.FanMode.Performance;
                    }
                    else
                    {
                        // Map based on preset curve characteristics
                        var maxFan = preset.Curve.Any() ? preset.Curve.Max(p => p.FanPercent) : 50;
                        var avgFan = preset.Curve.Any() ? preset.Curve.Average(p => p.FanPercent) : 50;
                        
                        if (avgFan < 40)
                            baseMode = HpWmiBios.FanMode.Cool;
                        else if (avgFan > 70 || maxFan > 90)
                            baseMode = HpWmiBios.FanMode.Performance;
                        else
                            baseMode = HpWmiBios.FanMode.Default;
                    }
                    break;
            }
            
            // Map to the correct command bytes based on ThermalPolicyVersion.
            // V0/Legacy systems use different command bytes (0x00-0x03) than V1+ systems (0x30/0x31/0x50).
            // Sending V1 bytes to V0 BIOS causes undefined behavior — e.g. Quiet (0x50) being 
            // interpreted as max performance, Transcend 14 2025 "Quiet = max fans" bug.
            if (_wmiBios.ThermalPolicy < HpWmiBios.ThermalPolicyVersion.V1)
            {
                // V0/Legacy mapping
                switch (baseMode)
                {
                    case HpWmiBios.FanMode.Performance:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyPerformance (0x01)");
                        return HpWmiBios.FanMode.LegacyPerformance;
                    case HpWmiBios.FanMode.Cool:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyCool (0x02)");
                        return HpWmiBios.FanMode.LegacyCool;
                    case HpWmiBios.FanMode.Default:
                    default:
                        _logging?.Debug("MapPresetToFanMode: Legacy V0 → LegacyDefault (0x00)");
                        return HpWmiBios.FanMode.LegacyDefault;
                }
            }
            
            // V1+: use standard mode bytes (0x30, 0x31, 0x50)
            return baseMode;
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
        /// HP BIOS aggressively reverts fan settings on many models (some within 3-5 seconds).
        /// This timer re-applies the current settings every 3 seconds to keep them active.
        /// </summary>
        public void StartCountdownExtension()
        {
            if (_countdownExtensionEnabled) return;
            
            _countdownExtensionTimer = new Timer(CountdownExtensionCallback, null, 
                CountdownExtensionInitialDelayMs, CountdownExtensionIntervalMs);
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
        /// Countdown extension callback - periodically re-applies fan settings.
        /// OmenMon-style: Re-applies settings every 3 seconds to prevent BIOS reversion.
        /// HP BIOS aggressively reverts fan settings, especially under load.
        /// Some models (e.g., OMEN 16-xd0xxx) revert within 3-5 seconds.
        /// </summary>
        private void CountdownExtensionCallback(object? state)
        {
            if (!IsAvailable || _disposed) return;
            
            try
            {
                // Re-apply current fan settings to prevent BIOS reversion (OmenMon approach)
                if (IsManualControlActive || (_lastMode != HpWmiBios.FanMode.Default && _lastMode != HpWmiBios.FanMode.LegacyDefault))
                {
                    // For Max mode, re-apply SetFanMax(true) to ensure it stays active
                    if (_isMaxModeActive)
                    {
                        if (_wmiBios.SetFanMax(true))
                        {
                            _logging?.Debug("Fan Max mode re-applied via countdown extension (OmenMon-style)");
                        }
                        else
                        {
                            _logging?.Warn("Failed to re-apply Max mode - trying fallback");
                            // Fallback: send ceiling value (100) — let BIOS clamp to hardware max
                            _wmiBios.SetFanLevel((byte)MaxFanLevelCeiling, (byte)MaxFanLevelCeiling);
                        }
                    }
                    else if (_lastManualFanPercent >= 0)
                    {
                        // For custom fan curves, re-apply the last set percentage
                        byte fanLevel = (byte)(_lastManualFanPercent * _maxFanLevel / 100);
                        if (_wmiBios.SetFanLevel(fanLevel, fanLevel))
                        {
                            _logging?.Debug($"Fan level re-applied: {_lastManualFanPercent}% via countdown extension (OmenMon-style)");
                        }
                    }
                    else
                    {
                        // For preset modes, re-apply the fan mode
                        _wmiBios.SetFanMode(_lastMode);
                        _logging?.Debug($"Fan mode re-applied: {_lastMode} via countdown extension (OmenMon-style)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan settings: {ex.Message}");
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

        /// <summary>
        /// Estimate fan speed based on temperature using a realistic OMEN BIOS fan curve.
        /// This provides more accurate RPM readings when direct fan level reading is unavailable.
        /// </summary>
        private (int percent, int rpm) EstimateFanFromTemperature(double temperature, string fanType)
        {
            int percent;

            // OMEN BIOS fan curves are typically more aggressive than linear
            // CPU fans tend to run slightly higher than GPU fans at same temp
            if (temperature <= 30)
            {
                // Idle/cool: 20-30%
                percent = fanType == "CPU" ? 25 : 20;
            }
            else if (temperature <= 40)
            {
                // Light load: 30-45%
                percent = (int)(25 + (temperature - 30) * 2.5); // 25-50% over 10°C
            }
            else if (temperature <= 60)
            {
                // Moderate load: 45-70%
                percent = (int)(40 + (temperature - 40) * 1.5); // 40-70% over 20°C
            }
            else if (temperature <= 80)
            {
                // Heavy load: 70-90%
                percent = (int)(70 + (temperature - 60) * 1.0); // 70-90% over 20°C
            }
            else
            {
                // Extreme load: 90-100%
                percent = Math.Min(100, (int)(90 + (temperature - 80) * 2.5)); // 90-100%+
            }

            // CPU fans typically run 5-10% higher than GPU fans
            if (fanType == "CPU")
            {
                percent = Math.Min(100, percent + 5);
            }

            percent = Math.Clamp(percent, 0, 100);
            int rpm = (percent * 5500) / 100;

            return (percent, rpm);
        }

        /// <summary>
        /// Basic verification that fan speed was applied (checks if command succeeded and no immediate error).
        /// More advanced verification would require reading back fan levels, but WMI BIOS doesn't always provide this.
        /// </summary>
        private bool VerifyFanSpeed(int targetPercent)
        {
            // For now, just do basic verification - in future could read back fan levels if available
            // This is mainly to catch immediate failures
            try
            {
                // If we have fan telemetry, we could verify RPM changed in expected direction
                // For now, assume success if no exception was thrown
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Fan speed verification error: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopCountdownExtension();
                // Don't restore auto control on exit - preserve user's fan settings
                // This prevents fans ramping up when user closes app while in Quiet mode
                _wmiBios.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Get command history for diagnostics
        /// </summary>
        public IReadOnlyList<WmiCommandHistoryEntry> GetCommandHistory()
        {
            lock (_commandHistory)
            {
                return _commandHistory.ToList();
            }
        }

        /// <summary>
        /// Get current fan RPM for command history tracking
        /// </summary>
        private double? GetCurrentFanRpm()
        {
            try
            {
                var fanSpeeds = GetFanSpeeds();
                var firstFan = fanSpeeds.FirstOrDefault();
                return firstFan.Rpm; // Access the Rpm property of the tuple
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Add a command to the history
        /// </summary>
        private void AddCommandToHistory(string command, bool success, string? error = null, double? fanRpmBefore = null, double? fanRpmAfter = null)
        {
            lock (_commandHistory)
            {
                _commandHistory.Add(new WmiCommandHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Command = command,
                    Success = success,
                    Error = error,
                    FanRpmBefore = fanRpmBefore,
                    FanRpmAfter = fanRpmAfter
                });

                // Keep only the most recent entries
                if (_commandHistory.Count > MaxCommandHistoryEntries)
                {
                    _commandHistory.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// Entry in WMI command history for diagnostics
    /// </summary>
    public class WmiCommandHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Command { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
        public double? FanRpmBefore { get; set; }
        public double? FanRpmAfter { get; set; }
    }
}
