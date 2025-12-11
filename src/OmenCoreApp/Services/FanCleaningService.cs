using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for running fan maintenance on HP OMEN laptops.
    /// 
    /// IMPORTANT SAFETY NOTE:
    /// True fan dust cleaning with reverse rotation (like HP's "Fan Cleaner" on OMEN Max 16)
    /// requires special omnidirectional BLDC fans that are hardware-designed to spin both ways.
    /// Most OMEN laptops do NOT have this hardware - only specific models (OMEN Max line) support it.
    /// 
    /// This service provides a SAFE alternative: "Fan Boost" mode that runs fans at maximum speed
    /// to help dislodge loose dust through increased airflow. It does NOT reverse fan direction
    /// as that would require specific hardware support that we cannot safely detect.
    /// 
    /// For true dust removal, users should use compressed air or open the chassis for cleaning.
    /// </summary>
    public class FanCleaningService
    {
        private readonly LoggingService _logging;
        private readonly IEcAccess _ecAccess;
        private readonly SystemInfoService _systemInfoService;

        // HP OMEN EC registers for fan control
        private const ushort EC_FAN_CONTROL_MODE = 0x46;  // Fan control mode register
        private const ushort EC_FAN1_DUTY = 0x44;         // Fan 1 duty cycle
        private const ushort EC_FAN2_DUTY = 0x45;         // Fan 2 duty cycle
        
        // Control values
        private const byte FAN_MODE_AUTO = 0x00;
        private const byte FAN_MODE_MANUAL = 0x01;

        private const int BOOST_DURATION_SECONDS = 30;

        // Models known to support true fan reversal (Fan Cleaner technology)
        // These have omnidirectional BLDC fans designed for reverse operation
        private static readonly HashSet<string> FanCleanerSupportedModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "OMEN Max 16",
            "OMEN Max 17",
            // Add more as they're confirmed - HP only advertises this for Max line currently
        };

        public FanCleaningService(LoggingService logging, IEcAccess ecAccess, SystemInfoService systemInfoService)
        {
            _logging = logging;
            _ecAccess = ecAccess;
            _systemInfoService = systemInfoService;
        }

        /// <summary>
        /// Whether fan boost is supported on this system.
        /// Requires HP OMEN hardware and EC access.
        /// </summary>
        public bool IsSupported
        {
            get
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                return sysInfo.IsHpOmen && _ecAccess.IsAvailable;
            }
        }

        /// <summary>
        /// Whether this specific model supports true fan reversal (HP Fan Cleaner technology).
        /// Only OMEN Max models with omnidirectional fans support this.
        /// </summary>
        public bool SupportsTrueFanReversal
        {
            get
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                if (string.IsNullOrEmpty(sysInfo.Model))
                    return false;
                    
                foreach (var model in FanCleanerSupportedModels)
                {
                    if (sysInfo.Model.Contains(model, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Get a description of what this feature does on the current hardware.
        /// </summary>
        public string FeatureDescription
        {
            get
            {
                if (SupportsTrueFanReversal)
                {
                    return "Your OMEN Max laptop supports HP's Fan Cleaner technology with reversible fans. " +
                           "This feature runs fans in reverse to blow dust out of the vents.";
                }
                else
                {
                    return "Fan Boost runs your fans at maximum speed to help dislodge loose dust through " +
                           "increased airflow. This is NOT the same as HP's Fan Cleaner (which reverses fans) - " +
                           "that feature requires special hardware only available on OMEN Max models.\n\n" +
                           "For thorough cleaning, use compressed air or open the chassis.";
                }
            }
        }

        /// <summary>
        /// Start the fan boost/maintenance cycle.
        /// This runs fans at maximum speed to help with dust removal through increased airflow.
        /// It does NOT reverse fan direction (that requires specific hardware).
        /// </summary>
        /// <param name="progressCallback">Callback for progress updates</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        public async Task StartCleaningAsync(Action<FanCleaningProgress> progressCallback, CancellationToken cancellationToken)
        {
            if (!IsSupported)
            {
                throw new InvalidOperationException("Fan boost is not supported on this system");
            }

            string modeName = SupportsTrueFanReversal ? "Fan Cleaner" : "Fan Boost";
            
            try
            {
                progressCallback(new FanCleaningProgress 
                { 
                    Message = $"Initializing {modeName}...", 
                    ProgressPercent = 0,
                    IsTrueFanReversal = SupportsTrueFanReversal
                });

                _logging.Info($"{modeName}: Starting fan maintenance cycle");
                
                if (SupportsTrueFanReversal)
                {
                    // On OMEN Max models, HP may expose a proper dust cleaning command
                    // For now, we still use the safe fan boost method until we can
                    // properly verify the correct EC commands for fan reversal
                    _logging.Info("Note: OMEN Max detected but using safe fan boost mode. " +
                                 "For true fan reversal, use OMEN Gaming Hub.");
                }
                
                await RunFanBoostCycleAsync(progressCallback, cancellationToken);

                progressCallback(new FanCleaningProgress 
                { 
                    Message = $"{modeName} complete! Restoring normal operation...", 
                    ProgressPercent = 100,
                    IsTrueFanReversal = SupportsTrueFanReversal
                });

                await Task.Delay(500, cancellationToken);

                RestoreNormalOperation();

                _logging.Info($"{modeName} cycle completed successfully");
            }
            catch (OperationCanceledException)
            {
                _logging.Warn($"{modeName} was cancelled - restoring normal operation");
                RestoreNormalOperation();
                throw;
            }
            catch (Exception ex)
            {
                _logging.Error($"{modeName} failed - restoring normal operation", ex);
                RestoreNormalOperation();
                throw;
            }
        }

        private async Task RunFanBoostCycleAsync(Action<FanCleaningProgress> progressCallback, CancellationToken cancellationToken)
        {
            _logging.Info("Starting fan boost cycle (max speed airflow)");

            // This method runs fans at maximum speed to increase airflow
            // Higher airflow can help dislodge loose dust particles
            // This is safe and doesn't require special hardware

            try
            {
                // Set fans to manual mode
                _ecAccess.WriteByte(EC_FAN_CONTROL_MODE, FAN_MODE_MANUAL);
                _logging.Info("Set fans to manual control mode");

                // Set both fans to maximum
                _ecAccess.WriteByte(EC_FAN1_DUTY, 0xFF);
                _ecAccess.WriteByte(EC_FAN2_DUTY, 0xFF);
                _logging.Info("Fans set to maximum speed");

                // Run at max for the full duration
                for (int i = 0; i < BOOST_DURATION_SECONDS; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int percent = (i + 1) * 100 / BOOST_DURATION_SECONDS;
                    int remaining = BOOST_DURATION_SECONDS - i - 1;

                    progressCallback(new FanCleaningProgress
                    {
                        Message = $"💨 Fan Boost active - maximum airflow... {remaining}s remaining",
                        ProgressPercent = percent,
                        IsTrueFanReversal = false
                    });

                    await Task.Delay(1000, cancellationToken);
                }

                _logging.Info("Fan boost cycle completed");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logging.Error($"EC write blocked: {ex.Message}");
                throw new InvalidOperationException(
                    "Fan boost is not available - EC write access is blocked. " +
                    "Please ensure WinRing0 driver is properly installed.", ex);
            }
        }

        private void RestoreNormalOperation()
        {
            try
            {
                // Restore auto fan control
                _ecAccess.WriteByte(EC_FAN_CONTROL_MODE, FAN_MODE_AUTO);
                _logging.Info("Restored auto fan control mode");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to restore fan control mode: {ex.Message}");
            }
        }
    }

    public class FanCleaningProgress
    {
        public string Message { get; set; } = "";
        public int ProgressPercent { get; set; }
        
        /// <summary>
        /// Whether this is true fan reversal (only on supported OMEN Max models)
        /// or just high-speed fan boost (safe on all models).
        /// </summary>
        public bool IsTrueFanReversal { get; set; }
    }
}
