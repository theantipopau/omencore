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
        private readonly ResumeRecoveryDiagnosticsService _resumeDiagnostics; // Always non-null; STEP-12 Option A
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

        // Keep failsafe fan ownership after monitoring heartbeat recovery until
        // temperatures are demonstrably safe. Heartbeat recovery alone is not enough.
        private DateTime _failsafeLastFanApplyUtc = DateTime.MinValue;
        private DateTime _failsafeSafeSinceUtc = DateTime.MinValue;

        private const int FailsafeFanReapplyIntervalSeconds = 15;
        private const double FailsafeSafeReleaseTempC = 65.0;
        private const int FailsafeSafeReleaseSeconds = 15;
        private const int WatchdogIntervalMs = 10000; // Check every 10 seconds
        internal const int FreezeThresholdSeconds = 90; // Require longer stall to reduce false positives
        private const int FreezeBreachConfirmations = 2; // Require two consecutive breaches before failsafe
        private const int FailsafeFanPercent = 90;
        private const int ResumeGraceSeconds = 120; // Ignore freeze detection briefly after wake while sensors reattach

        public HardwareWatchdogService(LoggingService logging, FanService fanService, ResumeRecoveryDiagnosticsService resumeDiagnostics)
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
                _lastTempUpdate = DateTime.Now;
                _lastCpuTemp = cpuTemp;
                _lastGpuTemp = gpuTemp;
                _consecutiveFreezeBreaches = 0;

                if (_failsafeActive)
                {
                    if (TryGetHottestValidTemperature(cpuTemp, gpuTemp, out var hottestTemp) &&
                        hottestTemp <= FailsafeSafeReleaseTempC)
                    {
                        if (_failsafeSafeSinceUtc == DateTime.MinValue)
                        {
                            _failsafeSafeSinceUtc = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - _failsafeSafeSinceUtc).TotalSeconds >= FailsafeSafeReleaseSeconds)
                        {
                            _failsafeActive = false;
                            _isWatchdogArmed = true;
                            _failsafeLastFanApplyUtc = DateTime.MinValue;
                            _failsafeSafeSinceUtc = DateTime.MinValue;
                            shouldRestoreAuto = true;
                        }
                    }
                    else
                    {
                        _failsafeSafeSinceUtc = DateTime.MinValue;
                    }
                }
            }

            if (shouldRestoreAuto)
            {
                _logging.Warn(
                    $"WATCHDOG: Monitoring recovered and temperatures are safe (<= {FailsafeSafeReleaseTempC:F0}°C for {FailsafeSafeReleaseSeconds}s) — restoring BIOS auto fan control");

                try
                {
                    _fanService.RestoreAutoControl();
                }
                catch (Exception ex)
                {
                    _logging.Warn($"WATCHDOG: Safe recovery restore auto control failed: {ex.Message}");
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
                _failsafeLastFanApplyUtc = DateTime.MinValue;
                _failsafeSafeSinceUtc = DateTime.MinValue;            }

            _logging.Info("WATCHDOG: Suspended freeze detection for system sleep");
            _resumeDiagnostics.RecordStep("watchdog", "Freeze detection suspended for sleep");
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
                _failsafeLastFanApplyUtc = DateTime.MinValue;
                _failsafeSafeSinceUtc = DateTime.MinValue;            }

            _logging.Info($"WATCHDOG: Resumed after sleep — freeze detection delayed for {ResumeGraceSeconds}s while monitoring recovers");
            _resumeDiagnostics.RecordStep("watchdog", $"Resume grace window started ({ResumeGraceSeconds}s)");

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
            TimeSpan timeSinceLastUpdate = TimeSpan.Zero;
            bool applyFailsafe = false;
            bool reapplyFailsafe = false;

            lock (_stateLock)
            {
                if (_disposed || _suspendActive)
                {
                    return;
                }

                // Failsafe remains active even though normal freeze detection is disarmed.
                if (_failsafeActive)
                {
                    var nowUtc = DateTime.UtcNow;

                    if ((nowUtc - _failsafeLastFanApplyUtc).TotalSeconds >= FailsafeFanReapplyIntervalSeconds)
                    {
                        _failsafeLastFanApplyUtc = nowUtc;
                        reapplyFailsafe = true;
                    }
                }
                else
                {
                    if (!_isWatchdogArmed)
                    {
                        return;
                    }

                    if (DateTime.UtcNow < _resumeGraceUntilUtc)
                    {
                        return;
                    }

                    timeSinceLastUpdate = DateTime.Now - _lastTempUpdate;

                    if (timeSinceLastUpdate.TotalSeconds > FreezeThresholdSeconds)
                    {
                        _consecutiveFreezeBreaches++;

                        if (_consecutiveFreezeBreaches >= FreezeBreachConfirmations)
                        {
                            _failsafeActive = true;
                            _isWatchdogArmed = false;
                            _failsafeLastFanApplyUtc = DateTime.UtcNow;
                            _failsafeSafeSinceUtc = DateTime.MinValue;
                            applyFailsafe = true;
                        }
                        else
                        {
                            _logging.Warn(
                                $"WATCHDOG: Potential monitoring stall ({timeSinceLastUpdate.TotalSeconds:F0}s, confirmation {_consecutiveFreezeBreaches}/{FreezeBreachConfirmations})");
                        }
                    }
                }
            }

            try
            {
                if (applyFailsafe)
                {
                    _logging.Error(
                        $"WATCHDOG: Temperature monitoring frozen for >{FreezeThresholdSeconds}s — applying failsafe fan speed");

                    _fanService.ForceSetFanSpeed(FailsafeFanPercent);

                    _logging.Warn(
                        $"Fans set to {FailsafeFanPercent}% due to frozen temperature monitoring");

                    _logging.Warn(
                        $"WATCHDOG: Failsafe fan control remains active until temperatures are <= {FailsafeSafeReleaseTempC:F0}°C for {FailsafeSafeReleaseSeconds}s");

                    return;
                }

                if (reapplyFailsafe)
                {
                    _logging.Warn(
                        $"WATCHDOG: Failsafe still active — reapplying fan speed {FailsafeFanPercent}%");

                    _fanService.ForceSetFanSpeed(FailsafeFanPercent);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Watchdog emergency fan set failed: {ex.Message}", ex);
            }
        }

        private static bool TryGetHottestValidTemperature(
            double cpuTemp,
            double gpuTemp,
            out double hottestTemp)
        {
            var hasCpu =
                !double.IsNaN(cpuTemp) &&
                !double.IsInfinity(cpuTemp) &&
                cpuTemp > 0 &&
                cpuTemp <= 150;

            var hasGpu =
                !double.IsNaN(gpuTemp) &&
                !double.IsInfinity(gpuTemp) &&
                gpuTemp > 0 &&
                gpuTemp <= 150;

            if (!hasCpu && !hasGpu)
            {
                hottestTemp = double.NaN;
                return false;
            }

            hottestTemp = hasCpu && hasGpu
                ? Math.Max(cpuTemp, gpuTemp)
                : hasCpu
                    ? cpuTemp
                    : gpuTemp;

            return true;
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
