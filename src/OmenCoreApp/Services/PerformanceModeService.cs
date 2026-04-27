using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ModelCapabilities? _modelCapabilities;
        private PerformanceMode? _currentMode;

        /// <summary>
        /// When false (default), switching performance modes does NOT write fan policy.
        /// Users who manage fan curves or presets manually are unaffected by profile switches.
        /// Set to true to restore legacy coupled behavior where each profile also sets a fan policy.
        /// </summary>
        public bool LinkFanToPerformanceMode { get; set; } = false;

        /// <summary>
        /// Event raised when a performance mode is applied (for UI synchronization).
        /// </summary>
        public event EventHandler<string>? ModeApplied;

        public PerformanceModeService(
            IFanController fanController, 
            PowerPlanService powerPlanService, 
            PowerLimitController? powerLimitController,
            LoggingService logging,
            IPowerVerificationService? powerVerificationService = null,
            ModelCapabilities? modelCapabilities = null)
        {
            _fanController = fanController;
            _powerPlanService = powerPlanService;
            _powerLimitController = powerLimitController;
            _powerVerificationService = powerVerificationService;
            _logging = logging;
            _modelCapabilities = modelCapabilities;
        }

        public void Apply(PerformanceMode mode)
        {
            _currentMode = mode;
            // Apply model-specific TDP overrides if the database has values for this model/mode.
            var effectiveMode = ApplyModelCapabilityOverrides(mode);
            var modeInfo = $"⚡ Applying performance mode: '{effectiveMode.Name}'";
            if (!string.IsNullOrEmpty(effectiveMode.LinkedPowerPlanGuid))
            {
                modeInfo += $" (Power Plan: {effectiveMode.LinkedPowerPlanGuid})";
            }
            _logging.Info(modeInfo);
            
            // Step 1: Apply Windows power plan
            _powerPlanService.Apply(effectiveMode);
            
            // Step 2: Apply EC-level power limits (CPU PL1/PL2, GPU TGP)
            if (_powerLimitController != null && _powerLimitController.IsAvailable)
            {
                try
                {
                    if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                    {
                        var before = _powerVerificationService.GetCurrentPowerLimits();
                        _logging.Info($"⚡ Power limits before apply ({effectiveMode.Name}): PL1={before.cpuPl1}W, PL2={before.cpuPl2}W, GPU={before.gpuTgp}W, ModeReg={before.performanceMode}");
                    }

                    _powerLimitController.ApplyPerformanceLimits(effectiveMode);
                    _logging.Info($"⚡ Power limits applied: CPU={effectiveMode.CpuPowerLimitWatts}W, GPU={effectiveMode.GpuPowerLimitWatts}W");

                    if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                    {
                        var after = _powerVerificationService.GetCurrentPowerLimits();
                        _logging.Info($"⚡ Power limits after apply ({effectiveMode.Name}): PL1={after.cpuPl1}W, PL2={after.cpuPl2}W, GPU={after.gpuTgp}W, ModeReg={after.performanceMode}");
                    }

                    // Verify the power limits were applied correctly
                    if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                    {
                        _ = VerifyPowerLimitsAndLogAsync(effectiveMode);
                    }
                }
                catch (Exception ex)
                {
                    // Type-safe contention check: both TimeoutException (PawnIO mutex timeout) and
                    // AbandonedMutexException (.NET mutex abandoned by dying thread) indicate EC
                    // contention and should be treated identically. Do NOT use ex.Message.Contains
                    // — exception messages are locale-dependent.
                    if (ex is TimeoutException or AbandonedMutexException)
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
            
            // Step 3: Adjust fan curve based on power profile.
            // Only runs when LinkFanToPerformanceMode is true (opt-in, default off).
            // By default, performance mode switches only affect power plan and EC power limits;
            // existing fan presets or curves set by the user are left untouched.
            if (LinkFanToPerformanceMode)
            {
                if (_fanController.IsAvailable)
                {
                    // Try to set performance mode via WMI BIOS first
                    if (_fanController.SetPerformanceMode(effectiveMode.Name))
                    {
                        _logging.Info($"🌀 Fan mode set to '{effectiveMode.Name}' via {_fanController.Backend}");
                    }
                    else
                    {
                        // Fallback to custom curve
                        var fanPercent = Math.Max(20, effectiveMode.CpuPowerLimitWatts / 2);
                        _fanController.ApplyCustomCurve(new[]
                        {
                            new FanCurvePoint { TemperatureC = 0, FanPercent = fanPercent }
                        });
                        _logging.Info($"🌀 Fan speed set to {fanPercent}% for '{effectiveMode.Name}' mode");
                    }
                }
                else
                {
                    _logging.Warn("⚠️ Fan control unavailable");
                }
            }
            else
            {
                _logging.Info("ℹ️ Fan policy unchanged — LinkFanToPerformanceMode is off");
            }
            
            _logging.Info($"✓ Performance mode '{effectiveMode.Name}' applied successfully");
            
            // Raise event for UI synchronization (sidebar, tray, etc.)
            ModeApplied?.Invoke(this, effectiveMode.Name);
        }

        /// <summary>
        /// Returns a copy of <paramref name="mode"/> with TDP values overridden by model-specific
        /// capability database entries when available.  Global config values are used as fallback
        /// so existing behaviour is preserved for all other models.
        /// </summary>
        private PerformanceMode ApplyModelCapabilityOverrides(PerformanceMode mode)
        {
            return ResolveEffectiveMode(mode);
        }

        /// <summary>
        /// Resolves the effective <see cref="PerformanceMode"/> that will actually be applied,
        /// incorporating any model-specific TDP overrides from the capability database.
        /// Exposed publicly for diagnostics and unit tests.
        /// </summary>
        public PerformanceMode ResolveEffectiveMode(PerformanceMode mode)
        {
            if (_modelCapabilities == null)
            {
                return mode;
            }

            var modeName = mode.Name.ToLowerInvariant().Trim();
            int? cpuOverride = null;
            int? boostOverride = null;
            int? gpuOverride = null;

            if (modeName is "performance" or "extreme" or "turbo")
            {
                cpuOverride = _modelCapabilities.PerformanceCpuPl1Watts;
                boostOverride = _modelCapabilities.PerformanceCpuPl2Watts;
                gpuOverride = _modelCapabilities.PerformanceGpuTgpWatts;
            }
            else if (modeName is "balanced" or "default" or "normal")
            {
                cpuOverride = _modelCapabilities.BalancedCpuPl1Watts;
                gpuOverride = _modelCapabilities.BalancedGpuTgpWatts;
            }
            else if (modeName is "eco" or "quiet" or "silent" or "powersaver")
            {
                cpuOverride = _modelCapabilities.EcoCpuPl1Watts;
            }

            if (cpuOverride == null && gpuOverride == null)
            {
                return mode;
            }

            var overriddenCpu = cpuOverride ?? mode.CpuPowerLimitWatts;
            var overriddenBoost = boostOverride ?? mode.CpuBoostPowerLimitWatts;
            var overriddenGpu = gpuOverride ?? mode.GpuPowerLimitWatts;

            _logging.Info(
                $"⚡ Model capability override for '{mode.Name}': " +
                $"CPU PL1 {mode.CpuPowerLimitWatts}W → {overriddenCpu}W, " +
                $"CPU PL2 {(mode.CpuBoostPowerLimitWatts?.ToString() ?? "auto")}W → {(overriddenBoost?.ToString() ?? "auto")}W, " +
                $"GPU {mode.GpuPowerLimitWatts}W → {overriddenGpu}W " +
                $"(model: {_modelCapabilities.ModelName})");

            return new PerformanceMode
            {
                Name = mode.Name,
                CpuPowerLimitWatts = overriddenCpu,
                CpuBoostPowerLimitWatts = overriddenBoost,
                GpuPowerLimitWatts = overriddenGpu,
                LinkedPowerPlanGuid = mode.LinkedPowerPlanGuid,
                Description = mode.Description
            };
        }

        private async Task VerifyPowerLimitsAndLogAsync(PerformanceMode mode)
        {
            try
            {
                if (_powerVerificationService == null || !_powerVerificationService.IsAvailable)
                {
                    return;
                }

                var verified = await _powerVerificationService.VerifyPowerLimitsAsync(mode).ConfigureAwait(false);
                if (verified)
                {
                    _logging.Info("✓ Power limits verified successfully");
                }
                else
                {
                    _logging.Warn("⚠️ Power limits verification failed - values may not have been applied");
                }

                var observed = _powerVerificationService.GetCurrentPowerLimits();
                _logging.Info(
                    $"⚡ Power verify snapshot ({mode.Name}): ExpectedCPU={mode.CpuPowerLimitWatts}W, ExpectedGPU={mode.GpuPowerLimitWatts}W, " +
                    $"ObservedPL1={observed.cpuPl1}W, ObservedPL2={observed.cpuPl2}W, ObservedGPU={observed.gpuTgp}W, ModeReg={observed.performanceMode}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"⚠️ Power limits verification threw: {ex.Message}");
            }
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
