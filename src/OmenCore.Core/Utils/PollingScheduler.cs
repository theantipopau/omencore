using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Utils
{
    /// <summary>
    /// A live subscription handle: dispose to unsubscribe, or call <see cref="UpdateInterval"/>
    /// to change cadence without paying an unsubscribe/resubscribe cost. Exists so a service
    /// whose natural polling rate changes at runtime (e.g. faster while something is active,
    /// slower while idle) can consolidate onto a shared coordinator instead of running its own
    /// independent timer just to keep that ability.
    /// </summary>
    public interface IPollingSubscription : IDisposable
    {
        /// <summary>
        /// Change this subscription's interval, effective from the next due-time check onward.
        /// The new interval is measured from now, not from when the subscription was originally
        /// created - a service that just sped itself up wants its next fire measured against
        /// the new, shorter interval, not whatever was left of the old one.
        /// </summary>
        void UpdateInterval(TimeSpan newInterval);
    }

    /// <summary>
    /// Pure due-time scheduling logic for coalescing multiple independent polling cadences
    /// onto a single driving tick, with no WPF/Dispatcher dependency so it can be unit
    /// tested directly. <see cref="UiPollingCoordinator"/> is the production wrapper that
    /// drives this with a real <c>DispatcherTimer</c>.
    ///
    /// Deliberately not thread-affine: callers decide what thread <see cref="Pump"/> runs on.
    /// For UI consumers that expect callbacks on the UI thread (the intended use case),
    /// only pump from the UI thread.
    /// </summary>
    public sealed class PollingScheduler
    {
        private sealed class Subscription
        {
            public required string Name;
            public required TimeSpan Interval;
            public required Action Callback;
            public DateTime NextDueUtc;
        }

        private readonly List<Subscription> _subscriptions = new();
        private readonly object _lock = new();
        private readonly Func<DateTime> _utcNow;

        public PollingScheduler(Func<DateTime>? utcNowProvider = null)
        {
            _utcNow = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Number of currently active subscriptions. For diagnostics/tests only.
        /// </summary>
        public int SubscriptionCount
        {
            get { lock (_lock) return _subscriptions.Count; }
        }

        /// <summary>
        /// Register a callback to fire roughly every <paramref name="interval"/>. The first
        /// fire happens no sooner than one interval after subscribing. Dispose the returned
        /// handle to unsubscribe, or call <see cref="IPollingSubscription.UpdateInterval"/> on
        /// it to change cadence later without unsubscribing.
        /// </summary>
        public IPollingSubscription Subscribe(string name, TimeSpan interval, Action callback)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Subscription name is required", nameof(name));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive");
            ArgumentNullException.ThrowIfNull(callback);

            var sub = new Subscription
            {
                Name = name,
                Interval = interval,
                Callback = callback,
                NextDueUtc = _utcNow() + interval
            };

            lock (_lock)
            {
                _subscriptions.Add(sub);
            }

            return new Unsubscriber(this, sub);
        }

        /// <summary>
        /// Drive the scheduler forward one tick: fire every subscription whose interval has
        /// elapsed, then reschedule it. A callback that throws does not stop the others —
        /// the exception is reported via <paramref name="onError"/> and swallowed, matching
        /// how the individual DispatcherTimers this replaces already isolate Tick handler
        /// failures from each other (each had its own timer and its own try/catch).
        /// </summary>
        public void Pump(Action<string, Exception>? onError = null)
        {
            var now = _utcNow();
            List<Subscription> due;

            lock (_lock)
            {
                if (_subscriptions.Count == 0) return;

                due = _subscriptions.Where(s => now >= s.NextDueUtc).ToList();
                foreach (var s in due)
                {
                    s.NextDueUtc = now + s.Interval;
                }
            }

            foreach (var s in due)
            {
                try
                {
                    s.Callback();
                }
                catch (Exception ex)
                {
                    onError?.Invoke(s.Name, ex);
                }
            }
        }

        private void Remove(Subscription sub)
        {
            lock (_lock)
            {
                _subscriptions.Remove(sub);
            }
        }

        private void ChangeInterval(Subscription sub, TimeSpan newInterval)
        {
            if (newInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(newInterval), "Interval must be positive");

            lock (_lock)
            {
                sub.Interval = newInterval;
                sub.NextDueUtc = _utcNow() + newInterval;
            }
        }

        private sealed class Unsubscriber : IPollingSubscription
        {
            private readonly PollingScheduler _owner;
            private readonly Subscription _sub;
            private bool _disposed;

            public Unsubscriber(PollingScheduler owner, Subscription sub)
            {
                _owner = owner;
                _sub = sub;
            }

            public void UpdateInterval(TimeSpan newInterval)
            {
                if (_disposed) return;
                _owner.ChangeInterval(_sub, newInterval);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.Remove(_sub);
            }
        }
    }
}
