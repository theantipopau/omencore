using System;
using System.Collections.Generic;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class PerformanceModeService
    {
        private readonly FanController _fanController;
        private readonly PowerPlanService _powerPlanService;
        private readonly PowerLimitController? _powerLimitController;
        private readonly LoggingService _logging;

        public PerformanceModeService(
            FanController fanController, 
            PowerPlanService powerPlanService, 
            PowerLimitController? powerLimitController,
            LoggingService logging)
        {
            _fanController = fanController;
            _powerPlanService = powerPlanService;
            _powerLimitController = powerLimitController;
            _logging = logging;
        }

        public void Apply(PerformanceMode mode)
        {
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
                }
                catch (Exception ex)
                {
                    _logging.Warn($"⚠️ Could not apply EC power limits: {ex.Message}");
                }
            }
            else
            {
                _logging.Info("ℹ️ EC power limit control not available - using Windows power plan only");
            }
            
            // Step 3: Adjust fan curve based on power profile
            if (_fanController.IsEcReady)
            {
                var fanPercent = Math.Max(20, mode.CpuPowerLimitWatts / 2);
                _fanController.ApplyCustomCurve(new[]
                {
                    new FanCurvePoint { TemperatureC = 0, FanPercent = fanPercent }
                });
                _logging.Info($"🌀 Fan speed set to {fanPercent}% for '{mode.Name}' mode");
            }
            else
            {
                _logging.Warn("⚠️ Skipping EC fan override; WinRing0 driver unavailable");
            }
            
            _logging.Info($"✓ Performance mode '{mode.Name}' applied successfully");
        }

        public IReadOnlyList<PerformanceMode> GetModes(AppConfig config) => config.PerformanceModes;
    }
}
