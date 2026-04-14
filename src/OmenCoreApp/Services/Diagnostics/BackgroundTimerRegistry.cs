using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OmenCore.Services.Diagnostics
{
    /// <summary>
    /// Opt-in registry for background timers and polling loops.
    /// Services call Register when they start a recurring background loop and
    /// Unregister when they stop it.
    /// DiagnosticExportService reads the snapshot at export time.
    /// </summary>
    public static class BackgroundTimerRegistry
    {
        private static readonly Dictionary<string, BackgroundTimerInfo> _entries = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Register a background timer. Overwrites any prior entry with the same name.
        /// </summary>
        public static void Register(
            string name,
            string ownerService,
            string description,
            int intervalMs,
            BackgroundTimerTier tier = BackgroundTimerTier.Optional)
        {
            var entry = new BackgroundTimerInfo(name, ownerService, description, intervalMs, DateTime.UtcNow, tier);
            lock (_lock)
                _entries[name] = entry;
        }

        /// <summary>
        /// Remove the entry for a stopped timer.
        /// </summary>
        public static void Unregister(string name)
        {
            lock (_lock)
                _entries.Remove(name);
        }

        /// <summary>
        /// Return a point-in-time snapshot of all currently registered timers.
        /// </summary>
        public static IReadOnlyList<BackgroundTimerInfo> GetAll()
        {
            lock (_lock)
                return _entries.Values.OrderBy(e => e.OwnerService, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Descriptor for a registered background timer or polling loop.
    /// </summary>
    public sealed class BackgroundTimerInfo
    {
        public string Name { get; }
        public string OwnerService { get; }
        public string Description { get; }
        public int IntervalMs { get; }
        public DateTime RegisteredUtc { get; }
        public BackgroundTimerTier Tier { get; }

        public BackgroundTimerInfo(
            string name,
            string ownerService,
            string description,
            int intervalMs,
            DateTime registeredUtc,
            BackgroundTimerTier tier)
        {
            Name = name;
            OwnerService = ownerService;
            Description = description;
            IntervalMs = intervalMs;
            RegisteredUtc = registeredUtc;
            Tier = tier;
        }
    }

    public enum BackgroundTimerTier
    {
        Critical,
        VisibleOnly,
        Optional
    }
}
