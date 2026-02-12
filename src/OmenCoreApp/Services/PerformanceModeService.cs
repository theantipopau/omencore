using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class PerformanceModeService
    {
        private readonly IFanController _fanController;
        private readonly PowerPlanService _powerPlanService;
        private readonly PowerLimitController? _powerLimitController;
        private readonly IPowerVerificationService? _powerVerificationService;
        private readonly LoggingService _logging;
        private PerformanceMode? _currentMode;

        /// <summary>
        /// Event raised when a performance mode is applied (for UI synchronization).
        /// </summary>
        public event EventHandler<string>? ModeApplied;

        public PerformanceModeService(
            IFanController fanController, 
            PowerPlanService powerPlanService, 
            PowerLimitController? powerLimitController,
            LoggingService logging,
            IPowerVerificationService? powerVerificationService = null)
        {
            _fanController = fanController;
            _powerPlanService = powerPlanService;
            _powerLimitController = powerLimitController;
            _powerVerificationService = powerVerificationService;
            _logging = logging;
        }

        public void Apply(PerformanceMode mode)
        {
            _currentMode = mode;
            var modeInfo = $"⚡ Applying performance mode: '{mode.Name}'";
            if (!string.IsNullOrEmpty(mode.LinkedPowerPlanGuid))
            {
                modeInfo += $" (Power Plan: {mode.LinkedPowerPlanGuid})";
            }
            _logging.Info(modeInfo);
            
            // Step 1: Apply Windows power plan
            _powerPlanService.Apply(mode);
            
            // Step 2: Apply EC-level power limits (CPU PL1/PL2, GPU TGP)
            if (_powerLimitController != null && _powerLimitController.IsAvailable)
            {
                try
                {
                    _powerLimitController.ApplyPerformanceLimits(mode);
                    _logging.Info($"⚡ Power limits applied: CPU={mode.CpuPowerLimitWatts}W, GPU={mode.GpuPowerLimitWatts}W");

                    // Verify the power limits were applied correctly
                    if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                    {
                        var verificationTask = _powerVerificationService.VerifyPowerLimitsAsync(mode);
                        // Don't await to avoid blocking, but log result
                        verificationTask.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                _logging.Warn($"⚠️ Power limits verification threw: {t.Exception?.InnerException?.Message}");
                                return;
                            }
                            if (t.Result)
                            {
                                _logging.Info("✓ Power limits verified successfully");
                            }
                            else
                            {
                                _logging.Warn("⚠️ Power limits verification failed - values may not have been applied");
                            }
                        }, TaskContinuationOptions.NotOnCanceled);
                    }
                }
                catch (Exception ex)
                {
                    // Only log EC power limits failure once per session if it's contention
                    if (ex.Message.Contains("mutex", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Hardware.PawnIOEcAccess.EcContentionWarningLogged)
                        {
                            Hardware.PawnIOEcAccess.EcContentionWarningLogged = true;
                            _logging.Warn($"⚠️ Could not apply EC power limits due to contention: {ex.Message}. This warning will only appear once per session.");
                        }
                    }
                    else
                    {
                        _logging.Warn($"⚠️ Could not apply EC power limits: {ex.Message}");
                    }
                }
            }
            else
            {
                _logging.Info("ℹ️ EC power limit control not available - using Windows power plan only");
            }
            
            // Step 3: Adjust fan curve based on power profile
            if (_fanController.IsAvailable)
            {
                // Try to set performance mode via WMI BIOS first
                if (_fanController.SetPerformanceMode(mode.Name))
                {
                    _logging.Info($"🌀 Fan mode set to '{mode.Name}' via {_fanController.Backend}");
                }
                else
                {
                    // Fallback to custom curve
                    var fanPercent = Math.Max(20, mode.CpuPowerLimitWatts / 2);
                    _fanController.ApplyCustomCurve(new[]
                    {
                        new FanCurvePoint { TemperatureC = 0, FanPercent = fanPercent }
                    });
                    _logging.Info($"🌀 Fan speed set to {fanPercent}% for '{mode.Name}' mode");
                }
            }
            else
            {
                _logging.Warn("⚠️ Fan control unavailable");
            }
            
            _logging.Info($"✓ Performance mode '{mode.Name}' applied successfully");
            
            // Raise event for UI synchronization (sidebar, tray, etc.)
            ModeApplied?.Invoke(this, mode.Name);
        }

        /// <summary>
        /// Set performance mode by name (for GeneralView quick profiles).
        /// </summary>
        public void SetPerformanceMode(string modeName)
        {
            // Map common names to default modes
            PerformanceMode? mode = modeName.ToLowerInvariant() switch
            {
                "performance" => new PerformanceMode 
                { 
                    Name = "Performance", 
                    CpuPowerLimitWatts = 95, 
                    GpuPowerLimitWatts = 140 
                },
                "quiet" or "silent" or "powersaver" => new PerformanceMode 
                { 
                    Name = "Quiet", 
                    CpuPowerLimitWatts = 35, 
                    GpuPowerLimitWatts = 60 
                },
                _ => new PerformanceMode 
                { 
                    Name = "Default", 
                    CpuPowerLimitWatts = 65, 
                    GpuPowerLimitWatts = 100 
                }
            };
            
            Apply(mode);
        }

        /// <summary>
        /// Expose the currently applied performance mode instance.
        /// </summary>
        public PerformanceMode? CurrentMode => _currentMode;

        /// <summary>
        /// Get the current performance mode name.
        /// </summary>
        public string? GetCurrentMode() => _currentMode?.Name;

        /// <summary>
        /// Whether EC-level power limit control is available.
        /// When false, performance modes only change Windows power plan and fan policy.
        /// </summary>
        public bool EcPowerControlAvailable => _powerLimitController != null && _powerLimitController.IsAvailable;
        
        /// <summary>
        /// Get a human-readable description of what controls are available.
        /// Useful for UI to show users what changing performance mode actually does.
        /// </summary>
        public string ControlCapabilityDescription
        {
            get
            {
                var capabilities = new List<string> { "Windows Power Plan" };
                
                if (_fanController.IsAvailable)
                    capabilities.Add("Fan Policy");
                    
                if (_powerLimitController != null && _powerLimitController.IsAvailable)
                    capabilities.Add("CPU/GPU Power Limits");
                    
                return string.Join(", ", capabilities);
            }
        }

        /// <summary>
        /// Return the configured performance modes available to the UI.
        /// </summary>
        public IEnumerable<PerformanceMode> GetAvailableModes()
        {
            return App.Configuration?.Config.PerformanceModes ?? new List<PerformanceMode>();
        }

        public IReadOnlyList<PerformanceMode> GetModes(AppConfig config) => config.PerformanceModes;
    }
}
