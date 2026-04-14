using OmenCore.Hardware;
using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services.Diagnostics;

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
        private readonly ResumeRecoveryDiagnosticsService? _resumeDiagnostics;
        private readonly object _stateLock = new();

        private Timer? _watchdogTimer;
        private DateTime _lastTempUpdate = DateTime.Now;
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private bool _isWatchdogArmed = true;
        private bool _failsafeActive;
        private bool _suspendActive;
        private int _consecutiveFreezeBreaches;
        private bool _disposed;
        private DateTime _resumeGraceUntilUtc = DateTime.MinValue;

        private const int WatchdogIntervalMs = 10000; // Check every 10 seconds
        private const int FreezeThresholdSeconds = 90; // Require longer stall to reduce false positives
        private const int FreezeBreachConfirmations = 2; // Require two consecutive breaches before failsafe
        private const int FailsafeFanPercent = 90;
        private const int ResumeGraceSeconds = 120; // Ignore freeze detection briefly after wake while sensors reattach

        public HardwareWatchdogService(LoggingService logging, FanService fanService, ResumeRecoveryDiagnosticsService? resumeDiagnostics = null)
        {
            _logging = logging;
            _fanService = fanService;
            _resumeDiagnostics = resumeDiagnostics;
        }

        /// <summary>
        /// Start watchdog monitoring
        /// </summary>
        public void Start()
        {
            if (_watchdogTimer != null) return;

            _logging.Info("🐕 Hardware watchdog started");
            _watchdogTimer = new Timer(CheckWatchdog, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(WatchdogIntervalMs));
            BackgroundTimerRegistry.Register(
                "HardwareWatchdog",
                "HardwareWatchdogService",
                "Monitors for frozen temperature sensors; triggers failsafe fan speeds",
                WatchdogIntervalMs,
                BackgroundTimerTier.Critical);
        }

        /// <summary>
        /// Stop watchdog monitoring
        /// </summary>
        public void Stop()
        {
            BackgroundTimerRegistry.Unregister("HardwareWatchdog");
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            _logging.Info("🐕 Hardware watchdog stopped");
        }

        /// <summary>
        /// Update temperature reading (called by hardware monitoring)
        /// </summary>
        public void UpdateTemperature(double cpuTemp, double gpuTemp)
        {
            bool shouldRestoreAuto = false;

            lock (_stateLock)
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
                    shouldRestoreAuto = true;
                }
            }

            if (shouldRestoreAuto)
            {
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
        /// Pause watchdog freeze detection while the system is suspended.
        /// Long sleep intervals must not be treated as frozen monitoring.
        /// </summary>
        public void HandleSystemSuspend()
        {
            lock (_stateLock)
            {
                _suspendActive = true;
                _isWatchdogArmed = false;
                _failsafeActive = false;
                _consecutiveFreezeBreaches = 0;
                _lastTempUpdate = DateTime.Now;
                _resumeGraceUntilUtc = DateTime.MinValue;
            }

            _logging.Info("WATCHDOG: Suspended freeze detection for system sleep");
            _resumeDiagnostics?.RecordStep("watchdog", "Freeze detection suspended for sleep");
        }

        /// <summary>
        /// Resume watchdog monitoring after wake with a short grace period for sensor stack recovery.
        /// </summary>
        public void HandleSystemResume()
        {
            var nowUtc = DateTime.UtcNow;

            lock (_stateLock)
            {
                _suspendActive = false;
                _failsafeActive = false;
                _isWatchdogArmed = true;
                _consecutiveFreezeBreaches = 0;
                _lastTempUpdate = DateTime.Now;
                _resumeGraceUntilUtc = nowUtc.AddSeconds(ResumeGraceSeconds);
            }

            _logging.Info($"WATCHDOG: Resumed after sleep — freeze detection delayed for {ResumeGraceSeconds}s while monitoring recovers");
            _resumeDiagnostics?.RecordStep("watchdog", $"Resume grace window started ({ResumeGraceSeconds}s)");

            if (_resumeDiagnostics != null)
            {
                var cycleId = _resumeDiagnostics.CurrentCycleId;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(ResumeGraceSeconds));
                    if (_resumeDiagnostics.CurrentCycleId == cycleId)
                    {
                        _resumeDiagnostics.RecordStep("watchdog", "Resume grace window ended");
                    }
                });
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
            TimeSpan timeSinceLastUpdate;

            lock (_stateLock)
            {
                if (_disposed || !_isWatchdogArmed || _suspendActive)
                {
                    return;
                }

                if (DateTime.UtcNow < _resumeGraceUntilUtc)
                {
                    return;
                }

                timeSinceLastUpdate = DateTime.Now - _lastTempUpdate;
            }

            try
            {
                if (timeSinceLastUpdate.TotalSeconds > FreezeThresholdSeconds)
                {
                    bool shouldApplyFailsafe = false;
                    int currentBreaches;

                    lock (_stateLock)
                    {
                        if (_disposed || !_isWatchdogArmed || _suspendActive || _failsafeActive)
                        {
                            return;
                        }

                        _consecutiveFreezeBreaches++;
                        currentBreaches = _consecutiveFreezeBreaches;

                        if (_consecutiveFreezeBreaches >= FreezeBreachConfirmations)
                        {
                            _failsafeActive = true;
                            _isWatchdogArmed = false;
                            shouldApplyFailsafe = true;
                        }
                    }

                    if (!shouldApplyFailsafe)
                    {
                        _logging.Warn($"WATCHDOG: Potential monitoring stall ({timeSinceLastUpdate.TotalSeconds:F0}s, confirmation {currentBreaches}/{FreezeBreachConfirmations})");
                        return;
                    }

                    _logging.Error($"🚨 WATCHDOG: Temperature monitoring frozen for {timeSinceLastUpdate.TotalSeconds:F0}s - applying failsafe fan speed");

                    // Emergency: set a high but non-max speed to avoid sticky max countdown mode.
                    Task.Run(() =>
                    {
                        try
                        {
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
