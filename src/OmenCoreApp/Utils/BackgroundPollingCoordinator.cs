using System;
using System.Threading;

namespace OmenCore.Utils
{
    /// <summary>
    /// Shared background-thread polling coordinator — the off-UI-thread counterpart to
    /// <see cref="UiPollingCoordinator"/>, for services that deliberately keep their polling off
    /// the UI thread (docs/ROADMAP_v4.2.0.md Pillar 2.4). Consolidates independent thread-pool
    /// timers onto one base-cadence <see cref="Timer"/> that fans out via the same
    /// <see cref="PollingScheduler"/> <see cref="UiPollingCoordinator"/> already uses and is
    /// already unit-tested against — this class only adds the background-timer plumbing around
    /// it, not new scheduling logic.
    ///
    /// Callbacks fire on a thread-pool thread, never the UI thread. Do not subscribe work here
    /// that touches bound properties or visual-tree state without marshalling to the UI thread
    /// yourself first — that is exactly the mistake <see cref="UiPollingCoordinator"/>'s own
    /// doc comment warns against in the other direction.
    ///
    /// Base cadence is 1000ms rather than <see cref="UiPollingCoordinator"/>'s 500ms: background
    /// polling has none of the "must feel responsive to render" pressure the UI cluster has, and
    /// halving the wake-up frequency is a real, if small, win toward Pillar 2.3's idle-CPU goal.
    /// Still evenly divides every cadence a background poller is realistically likely to use
    /// (2s/5s/10s and similar).
    /// </summary>
    public static class BackgroundPollingCoordinator
    {
        private const int BaseTickIntervalMs = 1000;

        private static readonly PollingScheduler _scheduler = new();
        private static readonly object _startLock = new();
        private static Timer? _baseTimer;
        private static int _pumpInProgress;

        /// <summary>
        /// Register a callback to fire roughly every <paramref name="interval"/>, on a
        /// thread-pool thread. Dispose the returned handle to unsubscribe (e.g. from the owning
        /// service's Dispose()/StopMonitoring()).
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

                _baseTimer = new Timer(
                    static _ => Pump(),
                    null,
                    TimeSpan.FromMilliseconds(BaseTickIntervalMs),
                    TimeSpan.FromMilliseconds(BaseTickIntervalMs));
            }
        }

        private static void Pump()
        {
            // System.Threading.Timer keeps firing on its own schedule even if a previous
            // callback is still running (unlike DispatcherTimer, which UiPollingCoordinator can
            // rely on to process ticks serially on one thread). A subscriber slow enough to
            // outlast one base tick must not cause the next tick to pile another concurrent
            // Pump() on top of it - skip this tick rather than let thread-pool threads
            // accumulate under load. Skipping is safe: PollingScheduler tracks each
            // subscription's own due time, so a skipped tick just means that subscriber's next
            // fire is checked on the following tick instead, not that it's lost.
            if (Interlocked.CompareExchange(ref _pumpInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _scheduler.Pump(static (name, ex) =>
                    App.Logging.Warn($"[BackgroundPollingCoordinator] Subscriber '{name}' threw: {ex.Message}"));
            }
            finally
            {
                Interlocked.Exchange(ref _pumpInProgress, 0);
            }
        }
    }
}
