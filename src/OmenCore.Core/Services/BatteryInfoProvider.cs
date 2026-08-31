using System;
using System.Management;

namespace OmenCore.Services
{
    /// <summary>
    /// Battery facts that Win32_Battery does not carry.
    ///
    /// The dashboard previously showed a hardcoded cycle count because Win32_Battery has no
    /// cycle-count property. That is true of Win32_Battery and false of Windows: the battery
    /// class driver publishes these values under <c>root\wmi</c>, which is where powercfg's
    /// own battery report gets them. Measured on an OMEN MAX 16 whose dashboard was showing
    /// 0 cycles and "Capacity unavailable": 14 cycles, 79416 of 83029 mWh.
    ///
    /// Not every pack populates these - plenty leave the fields unimplemented and the query
    /// then throws or returns nothing. Every accessor here distinguishes "read a value" from
    /// "could not read" instead of folding both into 0. A battery that genuinely has 0 cycles
    /// is a real state; a controller that declines to answer is not the same thing, and
    /// showing them identically is what made the old reading indistinguishable from a
    /// placeholder.
    /// </summary>
    public static class BatteryInfoProvider
    {
        /// <summary>
        /// These change on the order of once per full discharge, so re-querying them on the
        /// monitoring cadence would be pure overhead. Long enough to be free, short enough
        /// that a long uptime still tracks reality.
        /// </summary>
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private static readonly object Gate = new();
        private static BatteryInfo _cached = BatteryInfo.Unknown;
        private static DateTime _cachedAtUtc = DateTime.MinValue;
        private static bool _unavailableLogged;

        /// <summary>
        /// Everything readable about the pack's wear state. Any member may be <c>null</c>,
        /// independently of the others - the three values come from three separate WMI
        /// classes and a pack can implement one without the others.
        /// </summary>
        public readonly struct BatteryInfo
        {
            public int? CycleCount { get; init; }
            public int? DesignedCapacityMilliwattHours { get; init; }
            public int? FullChargedCapacityMilliwattHours { get; init; }

            public static BatteryInfo Unknown => default;

            /// <summary>
            /// Remaining capacity as a percentage of design capacity, or <c>null</c> when
            /// either capacity is unreadable. Not clamped to 100: a pack reporting slightly
            /// over design is a real (and common) reading, and silently trimming it would
            /// hide a miscalibrated gauge.
            /// </summary>
            public double? HealthPercent =>
                DesignedCapacityMilliwattHours is > 0 && FullChargedCapacityMilliwattHours is > 0
                    ? FullChargedCapacityMilliwattHours.Value * 100.0 / DesignedCapacityMilliwattHours.Value
                    : null;
        }

        /// <summary>
        /// Cached battery wear state. Never throws: unreadable counters are an expected
        /// outcome on many packs, not an error.
        /// </summary>
        public static BatteryInfo Get(LoggingService? logging = null)
        {
            lock (Gate)
            {
                if (DateTime.UtcNow - _cachedAtUtc < CacheLifetime)
                {
                    return _cached;
                }

                _cached = Query(logging);
                _cachedAtUtc = DateTime.UtcNow;
                return _cached;
            }
        }

        private static BatteryInfo Query(LoggingService? logging)
        {
            var info = new BatteryInfo
            {
                CycleCount = ReadFirst("BatteryCycleCount", "CycleCount", logging),

                // WARNING: this MUST stay a projected query. "SELECT * FROM BatteryStaticData"
                // fails outright with "Generic failure" on this hardware - one of the class's
                // string properties does not marshal, and asking for all of them poisons the
                // whole result set. Naming the one column needed returns it fine. Anything
                // added here should name its columns for the same reason.
                DesignedCapacityMilliwattHours =
                    ReadFirst("BatteryStaticData", "DesignedCapacity", logging),

                FullChargedCapacityMilliwattHours =
                    ReadFirst("BatteryFullChargedCapacity", "FullChargedCapacity", logging)
            };

            if (info.CycleCount == null &&
                info.DesignedCapacityMilliwattHours == null &&
                info.FullChargedCapacityMilliwattHours == null &&
                !_unavailableLogged)
            {
                logging?.Debug("[Battery] No root\\wmi battery wear data available on this system");
                _unavailableLogged = true;
            }

            return info;
        }

        private static int? ReadFirst(string className, string property, LoggingService? logging)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\wmi", $"SELECT {property}, Active FROM {className}");
                using var results = searcher.Get();

                int? firstSeen = null;
                foreach (ManagementObject instance in results)
                {
                    using (instance)
                    {
                        var raw = instance[property];
                        if (raw == null)
                        {
                            continue;
                        }

                        var value = Convert.ToInt32(raw);

                        // Multi-battery machines enumerate every bay, including empty ones.
                        // Prefer the active pack; fall back to the first that answered so a
                        // single-battery machine reporting Active = false still gets a value.
                        if (instance["Active"] is bool active && active)
                        {
                            return value;
                        }

                        firstSeen ??= value;
                    }
                }

                return firstSeen;
            }
            catch (Exception ex)
            {
                // ManagementException "Not supported" is the normal answer from a controller
                // that does not implement the counter. Debug, not Warn - this is not a fault.
                logging?.Debug($"[Battery] {className}.{property} unavailable: {ex.Message}");
                return null;
            }
        }
    }
}
