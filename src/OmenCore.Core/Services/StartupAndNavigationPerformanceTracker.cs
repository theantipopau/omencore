using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Services
{
    /// <summary>
    /// Startup time-to-interactive and per-tab switch cost, captured automatically on every run.
    /// Pillar 2.1 of docs/ROADMAP_v4.2.0.md ("measure before optimizing") found that three prior
    /// UI-responsiveness passes fixed real problems without moving user-perceived "feels laggy"
    /// reports, because the actual cost of startup and view switching had never been measured -
    /// only guessed at. This exists so a future report has real numbers to check against, and so a
    /// change that regresses either one shows up in a diagnostics export instead of staying
    /// invisible until someone complains.
    /// </summary>
    public static class StartupAndNavigationPerformanceTracker
    {
        private static readonly object Lock = new();
        private static double? _startupTimeToInteractiveMs;
        private static readonly Dictionary<string, TabSwitchStats> TabStats = new(StringComparer.Ordinal);

        /// <summary>
        /// Records the one-shot cold/warm start time-to-interactive for this process. Idempotent -
        /// only the first call has any effect, since there is only one startup per process.
        /// </summary>
        public static void RecordStartupTimeToInteractive(TimeSpan elapsed)
        {
            lock (Lock)
            {
                if (_startupTimeToInteractiveMs.HasValue)
                {
                    return;
                }

                _startupTimeToInteractiveMs = Math.Max(0, elapsed.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Records one tab-switch sample. Accumulates count/average/max per tab rather than storing
        /// every sample, so this stays cheap to call on every switch for the life of the process.
        /// </summary>
        public static void RecordTabSwitch(string tabName, TimeSpan elapsed)
        {
            if (string.IsNullOrEmpty(tabName))
            {
                return;
            }

            var elapsedMs = Math.Max(0, elapsed.TotalMilliseconds);

            lock (Lock)
            {
                if (!TabStats.TryGetValue(tabName, out var stats))
                {
                    stats = new TabSwitchStats();
                    TabStats[tabName] = stats;
                }

                stats.Record(elapsedMs);
            }
        }

        public static StartupAndNavigationSnapshot GetSnapshot()
        {
            lock (Lock)
            {
                var tabs = TabStats
                    .Select(kv => new TabSwitchSnapshot(kv.Key, kv.Value.Count, kv.Value.AverageMs, kv.Value.MaxMs))
                    .OrderBy(t => t.TabName, StringComparer.Ordinal)
                    .ToList();

                return new StartupAndNavigationSnapshot(_startupTimeToInteractiveMs, tabs);
            }
        }

        // Intended for deterministic tests.
        public static void ResetForTests()
        {
            lock (Lock)
            {
                _startupTimeToInteractiveMs = null;
                TabStats.Clear();
            }
        }

        private sealed class TabSwitchStats
        {
            public int Count { get; private set; }
            public double TotalMs { get; private set; }
            public double MaxMs { get; private set; }
            public double AverageMs => Count == 0 ? 0 : TotalMs / Count;

            public void Record(double elapsedMs)
            {
                Count++;
                TotalMs += elapsedMs;
                if (elapsedMs > MaxMs)
                {
                    MaxMs = elapsedMs;
                }
            }
        }
    }

    public sealed record TabSwitchSnapshot(string TabName, int Count, double AverageMs, double MaxMs);

    public sealed record StartupAndNavigationSnapshot(
        double? StartupTimeToInteractiveMs,
        IReadOnlyList<TabSwitchSnapshot> TabSwitches);
}
