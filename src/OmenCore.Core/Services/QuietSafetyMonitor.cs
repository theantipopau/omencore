using System;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// v3.7.0: Thermal safety hysteresis monitor for the Quiet profile.
    ///
    /// When the Quiet profile is active and CPU or GPU temperature crosses SafetyOnTempC,
    /// this monitor fires SafetyOverrideActivated. MainViewModel responds by switching fans
    /// to Max while keeping Quiet power limits. Once both CPU and GPU temperatures drop
    /// below SafetyOffTempC, SafetyOverrideCleared fires and Quiet fan cooling resumes.
    ///
    /// Design principles:
    /// - Pure state machine; no UI or hardware access.
    /// - Thread-safe: samples may arrive from a monitoring thread.
    /// - MainViewModel must call ClearAndDisarm() whenever the user explicitly switches
    ///   away from the Quiet profile so the override state does not persist.
    /// </summary>
    public class QuietSafetyMonitor
    {
        private readonly object _lock = new();
        private bool _overrideActive;
        private bool _armed;

        /// <summary>Temperature threshold (°C) at which safety override activates.</summary>
        public double SafetyOnTempC { get; set; } = 90.0;

        /// <summary>Temperature threshold (°C) below which safety override releases.</summary>
        public double SafetyOffTempC { get; set; } = 70.0;

        /// <summary>True while the safety override is active (fans at Max).</summary>
        public bool IsOverrideActive
        {
            get { lock (_lock) return _overrideActive; }
        }

        /// <summary>
        /// Fired on the calling thread when temperature rises above SafetyOnTempC
        /// and the monitor is armed (Quiet profile active).
        /// </summary>
        public event EventHandler? SafetyOverrideActivated;

        /// <summary>
        /// Fired on the calling thread when temperature drops below SafetyOffTempC
        /// after a safety override was active.
        /// </summary>
        public event EventHandler? SafetyOverrideCleared;

        /// <summary>
        /// Arm the monitor for the current Quiet profile session.
        /// Call this each time the user (or system) enters the Quiet profile.
        /// </summary>
        public void Arm(double safetyOnTempC, double safetyOffTempC)
        {
            lock (_lock)
            {
                SafetyOnTempC = Math.Clamp(safetyOnTempC, 70.0, 100.0);
                SafetyOffTempC = Math.Clamp(safetyOffTempC, 50.0, 90.0);
                _armed = true;
            }
        }

        /// <summary>
        /// Disarm the monitor and clear any active override without firing events.
        /// Call this when the user explicitly leaves the Quiet profile.
        /// </summary>
        public void ClearAndDisarm()
        {
            lock (_lock)
            {
                _armed = false;
                _overrideActive = false;
            }
        }

        /// <summary>
        /// Process a monitoring sample. Transitions the hysteresis state machine and
        /// fires events when state changes. No-op if the monitor is not armed.
        /// </summary>
        public void ProcessSample(MonitoringSample? sample)
        {
            if (sample == null) return;

            bool shouldActivate, shouldClear;

            lock (_lock)
            {
                if (!_armed) return;

                // Use the higher of CPU or GPU temp as the governing temperature.
                var temp = Math.Max(sample.CpuTemperatureC, sample.GpuTemperatureC);

                shouldActivate = !_overrideActive && temp >= SafetyOnTempC;
                shouldClear = _overrideActive && temp < SafetyOffTempC;

                if (shouldActivate) _overrideActive = true;
                else if (shouldClear) _overrideActive = false;
            }

            // Fire events outside the lock to avoid re-entrancy deadlocks.
            if (shouldActivate)
                SafetyOverrideActivated?.Invoke(this, EventArgs.Empty);
            else if (shouldClear)
                SafetyOverrideCleared?.Invoke(this, EventArgs.Empty);
        }
    }
}
