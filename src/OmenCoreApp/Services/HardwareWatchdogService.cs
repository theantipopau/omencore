using OmenCore.Hardware;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Hardware watchdog that monitors for frozen temperature sensors.
    /// Automatically reverts to safe fan speeds if temperature monitoring fails.
    /// </summary>
    public class HardwareWatchdogService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly FanService _fanService;

        private Timer? _watchdogTimer;
        private DateTime _lastTempUpdate = DateTime.Now;
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private bool _isWatchdogArmed = true;
        private bool _failsafeActive;
        private int _consecutiveFreezeBreaches;
        private bool _disposed;

        private const int WatchdogIntervalMs = 10000; // Check every 10 seconds
        private const int FreezeThresholdSeconds = 90; // Require longer stall to reduce false positives
        private const int FreezeBreachConfirmations = 2; // Require two consecutive breaches before failsafe
        private const int FailsafeFanPercent = 90;

        public HardwareWatchdogService(LoggingService logging, FanService fanService)
        {
            _logging = logging;
            _fanService = fanService;
        }

        /// <summary>
        /// Start watchdog monitoring
        /// </summary>
        public void Start()
        {
            if (_watchdogTimer != null) return;

            _logging.Info("🐕 Hardware watchdog started");
            _watchdogTimer = new Timer(CheckWatchdog, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(WatchdogIntervalMs));
        }

        /// <summary>
        /// Stop watchdog monitoring
        /// </summary>
        public void Stop()
        {
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            _logging.Info("🐕 Hardware watchdog stopped");
        }

        /// <summary>
        /// Update temperature reading (called by hardware monitoring)
        /// </summary>
        public void UpdateTemperature(double cpuTemp, double gpuTemp)
        {
            // Receiving ANY call means the monitoring pipeline is alive — update the heartbeat
            // unconditionally. Stable idle temps are normal and must not trigger a false alarm.
            _lastTempUpdate = DateTime.Now;
            _lastCpuTemp = cpuTemp;
            _lastGpuTemp = gpuTemp;
            _consecutiveFreezeBreaches = 0;

            if (_failsafeActive)
            {
                _failsafeActive = false;
                _isWatchdogArmed = true;
                _logging.Warn("WATCHDOG: Monitoring heartbeat recovered — attempting to restore BIOS auto fan control");
                try
                {
                    _fanService.RestoreAutoControl();
                }
                catch (Exception ex)
                {
                    _logging.Warn($"WATCHDOG: Recovery restore auto control failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Disarm watchdog temporarily (e.g., during diagnostic mode)
        /// </summary>
        public void Disarm()
        {
            _isWatchdogArmed = false;
            _logging.Debug("Watchdog disarmed");
        }

        /// <summary>
        /// Re-arm watchdog after disarm
        /// </summary>
        public void Arm()
        {
            _isWatchdogArmed = true;
            _lastTempUpdate = DateTime.Now; // Reset timer to avoid false trigger
            _logging.Debug("Watchdog armed");
        }

        private void CheckWatchdog(object? state)
        {
            if (!_isWatchdogArmed || _disposed) return;

            try
            {
                var timeSinceLastUpdate = DateTime.Now - _lastTempUpdate;

                if (timeSinceLastUpdate.TotalSeconds > FreezeThresholdSeconds)
                {
                    _consecutiveFreezeBreaches++;
                    if (_consecutiveFreezeBreaches < FreezeBreachConfirmations)
                    {
                        _logging.Warn($"WATCHDOG: Potential monitoring stall ({timeSinceLastUpdate.TotalSeconds:F0}s, confirmation {_consecutiveFreezeBreaches}/{FreezeBreachConfirmations})");
                        return;
                    }

                    _logging.Error($"🚨 WATCHDOG: Temperature monitoring frozen for {timeSinceLastUpdate.TotalSeconds:F0}s - applying failsafe fan speed");

                    // Emergency: set a high but non-max speed to avoid sticky max countdown mode.
                    Task.Run(() =>
                    {
                        try
                        {
                            _failsafeActive = true;
                            _isWatchdogArmed = false;
                            _fanService.ForceSetFanSpeed(FailsafeFanPercent);
                            _logging.Warn($"Fans set to {FailsafeFanPercent}% due to frozen temperature monitoring");

                            // Notify user
                            _logging.Warn($"🚨 WATCHDOG: Hardware monitoring frozen — fans set to {FailsafeFanPercent}%. Waiting for monitoring recovery.");
                            _logging.Warn("If this issue persists, check: WMI BIOS availability, system stability, or Windows updates.");
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"Watchdog emergency fan set failed: {ex.Message}", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Watchdog check error: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }
    }
}
