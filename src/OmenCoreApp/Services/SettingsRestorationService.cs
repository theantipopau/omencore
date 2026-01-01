using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Handles restoration of saved settings (GPU Power Boost, TCC Offset, Fan Preset) on startup.
    /// Uses the StartupSequencer for proper retry logic and hardware readiness checks.
    /// 
    /// This fixes the issue where settings don't survive reboot because:
    /// 1. Hardware (WMI BIOS) may not be ready immediately after Windows login
    /// 2. Previous implementation used fragile Task.Run delays without proper retry
    /// 3. No verification that settings were actually applied
    /// </summary>
    public class SettingsRestorationService
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly HpWmiBios? _wmiBios;
        private readonly FanService? _fanService;
        private readonly FanVerificationService? _fanVerifier;
        
        public event EventHandler<SettingsRestoredEventArgs>? SettingsRestored;
        
        // Track which settings were restored
        public bool GpuPowerBoostRestored { get; private set; }
        public bool TccOffsetRestored { get; private set; }
        public bool FanPresetRestored { get; private set; }
        
        public SettingsRestorationService(
            LoggingService logging,
            ConfigurationService configService,
            HpWmiBios? wmiBios = null,
            FanService? fanService = null,
            FanVerificationService? fanVerifier = null)
        {
            _logging = logging;
            _configService = configService;
            _wmiBios = wmiBios;
            _fanService = fanService;
            _fanVerifier = fanVerifier;
        }
        
        /// <summary>
        /// Register settings restoration tasks with the StartupSequencer.
        /// Call this during app initialization before ExecuteAsync.
        /// </summary>
        public void RegisterTasks(StartupSequencer sequencer)
        {
            // Priority 10: Wait for WMI BIOS to be available
            sequencer.AddTask("Wait for Hardware", WaitForHardwareAsync, priority: 10, maxRetries: 10, retryDelayMs: 1500);
            
            // Priority 20: Restore GPU Power Boost
            sequencer.AddTask("Restore GPU Power Boost", RestoreGpuPowerBoostAsync, priority: 20, maxRetries: 3, retryDelayMs: 1000);
            
            // Priority 30: Restore TCC Offset (if supported)
            sequencer.AddTask("Restore TCC Offset", RestoreTccOffsetAsync, priority: 30, maxRetries: 3, retryDelayMs: 1000);
            
            // Priority 40: Restore Fan Preset
            sequencer.AddTask("Restore Fan Preset", RestoreFanPresetAsync, priority: 40, maxRetries: 3, retryDelayMs: 1000);
            
            _logging.Info("SettingsRestorationService: Registered 4 startup tasks");
        }
        
        /// <summary>
        /// Wait for WMI BIOS to be available and responding.
        /// </summary>
        private Task<bool> WaitForHardwareAsync(CancellationToken ct)
        {
            if (_wmiBios == null)
            {
                _logging.Warn("SettingsRestoration: WMI BIOS not available");
                return Task.FromResult(false);
            }
            
            try
            {
                // Try to get fan count - a simple query that confirms WMI is working
                var fanCount = _wmiBios.FanCount;
                if (fanCount > 0)
                {
                    _logging.Info($"SettingsRestoration: Hardware ready (detected {fanCount} fans)");
                    return Task.FromResult(true);
                }
                
                // Fallback: check if WMI is available
                if (_wmiBios.IsAvailable)
                {
                    _logging.Info("SettingsRestoration: Hardware ready (WMI available)");
                    return Task.FromResult(true);
                }
                
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logging.Warn($"SettingsRestoration: Hardware not ready - {ex.Message}");
                return Task.FromResult(false);
            }
        }
        
        /// <summary>
        /// Restore GPU Power Boost level from saved config.
        /// </summary>
        private async Task<bool> RestoreGpuPowerBoostAsync(CancellationToken ct)
        {
            if (_wmiBios == null)
            {
                _logging.Info("SettingsRestoration: Skipping GPU Power Boost (WMI not available)");
                return true; // Not a failure, just not applicable
            }
            
            var savedLevel = _configService.Config.LastGpuPowerBoostLevel;
            if (string.IsNullOrEmpty(savedLevel))
            {
                _logging.Info("SettingsRestoration: No saved GPU Power Boost level");
                return true;
            }
            
            try
            {
                var level = savedLevel switch
                {
                    "Minimum" => HpWmiBios.GpuPowerLevel.Minimum,
                    "Medium" => HpWmiBios.GpuPowerLevel.Medium,
                    "Maximum" => HpWmiBios.GpuPowerLevel.Maximum,
                    "Extended" => HpWmiBios.GpuPowerLevel.Extended3,
                    _ => HpWmiBios.GpuPowerLevel.Medium
                };
                
                var success = _wmiBios.SetGpuPower(level);
                if (success)
                {
                    // Verify it was applied
                    await Task.Delay(500, ct); // Brief delay for hardware to update
                    var currentState = _wmiBios.GetGpuPower();
                    
                    // Verify by checking the expected flags for each level
                    // Note: Extended levels still show as customTgp=true, ppab=true (can't distinguish via bool)
                    bool verified = false;
                    if (currentState.HasValue)
                    {
                        verified = level switch
                        {
                            HpWmiBios.GpuPowerLevel.Minimum => !currentState.Value.customTgp && !currentState.Value.ppab,
                            HpWmiBios.GpuPowerLevel.Medium => currentState.Value.customTgp && !currentState.Value.ppab,
                            HpWmiBios.GpuPowerLevel.Maximum => currentState.Value.customTgp && currentState.Value.ppab,
                            HpWmiBios.GpuPowerLevel.Extended3 => currentState.Value.customTgp && currentState.Value.ppab, // Extended shows same as Maximum
                            HpWmiBios.GpuPowerLevel.Extended4 => currentState.Value.customTgp && currentState.Value.ppab,
                            _ => false
                        };
                    }
                    
                    if (verified)
                    {
                        _logging.Info($"✓ GPU Power Boost restored and verified: {savedLevel}");
                        GpuPowerBoostRestored = true;
                        RaiseSettingsRestored("GpuPowerBoost", savedLevel, true);
                        return true;
                    }
                    else
                    {
                        _logging.Warn($"GPU Power Boost set but verification failed: expected {level}, got state={currentState}");
                        return false;
                    }
                }
                
                _logging.Warn("GPU Power Boost restoration returned false");
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to restore GPU Power Boost: {ex.Message}");
                RaiseSettingsRestored("GpuPowerBoost", savedLevel, false, ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Restore TCC Offset from saved config.
        /// </summary>
        private async Task<bool> RestoreTccOffsetAsync(CancellationToken ct)
        {
            var savedOffset = _configService.Config.LastTccOffset;
            if (!savedOffset.HasValue || savedOffset.Value <= 0)
            {
                _logging.Info("SettingsRestoration: No saved TCC Offset");
                return true;
            }
            
            try
            {
                // TCC offset requires MSR access - use factory to get best available backend
                using var msrAccess = MsrAccessFactory.Create(_logging);
                if (msrAccess == null || !msrAccess.IsAvailable)
                {
                    _logging.Info("SettingsRestoration: Skipping TCC Offset (MSR access not available - install PawnIO)");
                    return true; // Not a failure, just not applicable
                }
                
                var currentOffset = msrAccess.ReadTccOffset();
                if (currentOffset == savedOffset.Value)
                {
                    _logging.Info($"TCC Offset already at saved value: {savedOffset.Value}°C");
                    return true;
                }
                
                msrAccess.SetTccOffset(savedOffset.Value);
                
                // Verify
                await Task.Delay(200, ct);
                var verifiedOffset = msrAccess.ReadTccOffset();
                
                if (verifiedOffset == savedOffset.Value)
                {
                    _logging.Info($"✓ TCC Offset restored and verified: {savedOffset.Value}°C");
                    TccOffsetRestored = true;
                    RaiseSettingsRestored("TccOffset", savedOffset.Value.ToString(), true);
                    return true;
                }
                else
                {
                    _logging.Warn($"TCC Offset set but verification failed: expected {savedOffset.Value}, got {verifiedOffset}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to restore TCC Offset: {ex.Message}");
                RaiseSettingsRestored("TccOffset", savedOffset.Value.ToString(), false, ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Restore fan preset from saved config.
        /// </summary>
        private async Task<bool> RestoreFanPresetAsync(CancellationToken ct)
        {
            var savedPresetName = _configService.Config.LastFanPresetName;
            if (string.IsNullOrEmpty(savedPresetName))
            {
                _logging.Info("SettingsRestoration: No saved fan preset");
                return true;
            }
            
            if (_fanService == null)
            {
                _logging.Info("SettingsRestoration: Skipping fan preset (FanService not available)");
                return true;
            }
            
            try
            {
                // First, look up the preset by name from config (custom presets)
                var preset = _configService.Config.FanPresets
                    .FirstOrDefault(p => p.Name.Equals(savedPresetName, StringComparison.OrdinalIgnoreCase));

                if (preset != null)
                {
                    _fanService.ApplyPreset(preset);
                    FanPresetRestored = true;
                    _logging.Info($"✓ Fan preset restored from config: {savedPresetName}");
                    RaiseSettingsRestored("FanPreset", savedPresetName, true);
                    return true;
                }

                // Not a custom preset - handle built-in names
                var nameLower = savedPresetName.ToLowerInvariant();
                if (nameLower == "max" || nameLower.Contains("max"))
                {
                    // Apply an explicit 100% curve and force an immediate apply to ensure SetFanSpeed(100) is invoked
                    var maxPreset = new FanPreset { Name = savedPresetName, Mode = FanMode.Max, Curve = new System.Collections.Generic.List<FanCurvePoint> { new FanCurvePoint { TemperatureC = 0, FanPercent = 100 } } };
                    _fanService.ApplyPreset(maxPreset, immediate: true);

                    // Force an immediate curve application using current temps and wait for it to complete so controller state is set
                    var temps = _fanService.ThermalProvider.ReadTemperatures().ToList();
                    var cpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0;
                    var gpuTemp = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0;
                    await _fanService.ForceApplyCurveNowAsync(cpuTemp, gpuTemp, immediate: true, ct: ct);

                    FanPresetRestored = true;
                    _logging.Info($"✓ Fan preset restored: {savedPresetName} (Max)");
                    RaiseSettingsRestored("FanPreset", savedPresetName, true);
                    return true;
                }

                if (nameLower == "auto" || nameLower == "default")
                {
                    _fanService.ApplyAutoMode();
                    FanPresetRestored = true;
                    _logging.Info($"✓ Fan preset restored: {savedPresetName} (Auto)");
                    RaiseSettingsRestored("FanPreset", savedPresetName, true);
                    return true;
                }

                if (nameLower == "quiet" || nameLower == "silent")
                {
                    _fanService.ApplyQuietMode();
                    FanPresetRestored = true;
                    _logging.Info($"✓ Fan preset restored: {savedPresetName} (Quiet)");
                    RaiseSettingsRestored("FanPreset", savedPresetName, true);
                    return true;
                }

                // Fallback: attempt to apply preset by name via a generic FanPreset (use Mode=Performance as hint)
                var fallbackPreset = new FanPreset { Name = savedPresetName, Mode = FanMode.Performance };
                _fanService.ApplyPreset(fallbackPreset);
                FanPresetRestored = true;
                _logging.Info($"✓ Fan preset restored via fallback: {savedPresetName}");
                RaiseSettingsRestored("FanPreset", savedPresetName, true);
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to restore fan preset: {ex.Message}");
                RaiseSettingsRestored("FanPreset", savedPresetName, false, ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Force reapply of the saved fan preset (exposed for UI / diagnostics).
        /// </summary>
        public Task<bool> ForceReapplyFanPresetAsync(CancellationToken ct = default)
        {
            return RestoreFanPresetAsync(ct);
        }

        private void RaiseSettingsRestored(string settingName, string value, bool success, string? error = null)
        {
            SettingsRestored?.Invoke(this, new SettingsRestoredEventArgs
            {
                SettingName = settingName,
                Value = value,
                Success = success,
                Error = error
            });
        }
    }
    
    public class SettingsRestoredEventArgs : EventArgs
    {
        public string SettingName { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
