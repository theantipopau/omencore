using System;
using System.Windows.Threading;

namespace OmenCore.Utils
{
    /// <summary>
    /// Shared UI-thread polling coordinator. Consolidates independent per-window/per-service
    /// DispatcherTimers into a single base-cadence DispatcherTimer that fans out to subscribers
    /// via <see cref="PollingScheduler"/>, instead of each UI surface running its own timer.
    ///
    /// This is a UI-thread-only coordinator — every subscriber's callback fires from the base
    /// DispatcherTimer's Tick, i.e. on the thread that owns that Dispatcher (the app's UI
    /// thread in practice). Do not subscribe background/off-UI-thread polling loops here
    /// (e.g. ProcessMonitoringService, which deliberately runs on a thread-pool timer to keep
    /// that work off the UI thread) — that would be a regression, not a consolidation.
    ///
    /// Base cadence is 500ms, chosen because it evenly divides every cadence in the first
    /// migrated cluster (Tray 2000ms, OSD stats 1000ms, OSD network ping 5000ms, Quick Popup
    /// 1000ms) with no remainder, so none of them drift relative to their old fixed interval —
    /// only the first fire after subscribing can lag by up to one base tick (≤500ms), which is
    /// the same order of jitter DispatcherTimer itself already has relative to the UI thread's
    /// message queue.
    /// </summary>
    public static class UiPollingCoordinator
    {
        private const int BaseTickIntervalMs = 500;

        private static readonly PollingScheduler _scheduler = new();
        private static readonly object _startLock = new();
        private static DispatcherTimer? _baseTimer;

        /// <summary>
        /// Register a callback to fire roughly every <paramref name="interval"/>, on the UI
        /// thread. Dispose the returned handle to unsubscribe (e.g. from the owning window's
        /// Closed handler or a service's Dispose()).
        /// </summary>
        public static IDisposable Subscribe(string name, TimeSpan interval, Action callback)
        {
            EnsureStarted();
            return _scheduler.Subscribe(name, interval, callback);
        }

        /// <summary>
        /// Current subscriber count. For diagnostics only.
        /// </summary>
        public static int SubscriptionCount => _scheduler.SubscriptionCount;

        private static void EnsureStarted()
        {
            if (_baseTimer != null) return;

            lock (_startLock)
            {
                if (_baseTimer != null) return;

                // Must be constructed on a thread that owns a Dispatcher — callers are expected
                // to make their first Subscribe() call from the UI thread, same requirement the
                // DispatcherTimers being replaced already had.
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(BaseTickIntervalMs)
                };
                timer.Tick += (_, _) => _scheduler.Pump(static (name, ex) =>
                {
                    App.Logging.Warn($"[UiPollingCoordinator] Subscriber '{name}' threw: {ex.Message}");
                });
                timer.Start();
                _baseTimer = timer;
            }
        }
    }
}
