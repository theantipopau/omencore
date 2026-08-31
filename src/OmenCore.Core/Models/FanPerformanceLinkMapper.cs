using System;

namespace OmenCore.Models
{
    /// <summary>
    /// Canonical bidirectional fan-to-performance and performance-to-fan mode mapping
    /// used by the linked synchronization pipeline in MainViewModel.
    /// Extracted from MainViewModel private helpers to allow independent unit testing
    /// and shared use by future sync gateways (tray, game profiles, automation).
    /// </summary>
    public static class FanPerformanceLinkMapper
    {
        /// <summary>
        /// Maps a fan preset name to the canonical performance mode name used
        /// when fan-to-performance link sync is active.
        /// </summary>
        public static string MapFanModeToPerformanceMode(string? fanMode)
        {
            if (string.IsNullOrWhiteSpace(fanMode) || FanModeNameResolver.IsAutoAlias(fanMode))
            {
                return "Balanced";
            }

            if (FanModeNameResolver.IsQuietAlias(fanMode))
            {
                return "Quiet";
            }

            if (FanModeNameResolver.IsMaxAlias(fanMode) ||
                fanMode.Contains("Gaming", StringComparison.OrdinalIgnoreCase) ||
                fanMode.Contains("Extreme", StringComparison.OrdinalIgnoreCase) ||
                FanModeNameResolver.IsPerformanceAlias(fanMode))
            {
                return "Performance";
            }

            return "Balanced";
        }

        /// <summary>
        /// Maps a performance mode name to the canonical fan mode name used
        /// when performance-to-fan link sync is active.
        /// </summary>
        public static string MapPerformanceModeToFanMode(string? performanceMode)
        {
            if (string.IsNullOrWhiteSpace(performanceMode))
            {
                return "Auto";
            }

            if (string.Equals(performanceMode, "Quiet", StringComparison.OrdinalIgnoreCase))
            {
                return "Quiet";
            }

            if (string.Equals(performanceMode, "Performance", StringComparison.OrdinalIgnoreCase))
            {
                return "Extreme";
            }

            return "Auto";
        }
    }
}
