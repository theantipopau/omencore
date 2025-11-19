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
        private readonly LoggingService _logging;

        public PerformanceModeService(FanController fanController, PowerPlanService powerPlanService, LoggingService logging)
        {
            _fanController = fanController;
            _powerPlanService = powerPlanService;
            _logging = logging;
        }

        public void Apply(PerformanceMode mode)
        {
            _logging.Info($"Applying performance mode '{mode.Name}'");
            _powerPlanService.Apply(mode);
            if (_fanController.IsEcReady)
            {
                _fanController.ApplyCustomCurve(new[]
                {
                    new FanCurvePoint { TemperatureC = 0, FanPercent = Math.Max(20, mode.CpuPowerLimitWatts / 2) }
                });
            }
            else
            {
                _logging.Warn("Skipping EC fan override; WinRing0 driver unavailable");
            }
            // TODO: Talk to ACPI/SMBus for PL1/PL2/TGP values.
        }

        public IReadOnlyList<PerformanceMode> GetModes(AppConfig config) => config.PerformanceModes;
    }
}
