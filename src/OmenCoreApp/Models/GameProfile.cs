using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    /// <summary>
    /// Per-game profile with automatic settings switching.
    /// </summary>
    public class GameProfile
    {
        /// <summary>
        /// Unique profile identifier.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Whether this profile is enabled for auto-switching.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Profile display name.
        /// </summary>
        public string Name { get; set; } = "New Profile";

        /// <summary>
        /// Game executable name (e.g., "RocketLeague.exe", "csgo.exe").
        /// </summary>
        public string ExecutableName { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Full path to executable for more specific matching.
        /// </summary>
        public string? ExecutablePath { get; set; }

        /// <summary>
        /// Fan preset to apply when game launches.
        /// </summary>
        public string? FanPresetName { get; set; }

        /// <summary>
        /// Performance mode to apply when game launches.
        /// </summary>
        public string? PerformanceModeName { get; set; }

        /// <summary>
        /// CPU core undervolt offset in millivolts (negative values).
        /// </summary>
        public int? CpuCoreOffsetMv { get; set; }

        /// <summary>
        /// CPU cache undervolt offset in millivolts (negative values).
        /// </summary>
        public int? CpuCacheOffsetMv { get; set; }

        /// <summary>
        /// GPU mode (Hybrid, Discrete, Integrated).
        /// </summary>
        public GpuSwitchMode? GpuMode { get; set; }

        /// <summary>
        /// Keyboard lighting profile to apply.
        /// </summary>
        public string? KeyboardLightingProfileName { get; set; }

        /// <summary>
        /// Peripheral lighting profile for Corsair/Logitech devices.
        /// </summary>
        public string? PeripheralLightingProfileName { get; set; }

        /// <summary>
        /// Custom notes for this profile.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// When this profile was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// When this profile was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Priority for conflict resolution (higher = takes precedence).
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Launch count for analytics/sorting.
        /// </summary>
        public int LaunchCount { get; set; } = 0;

        /// <summary>
        /// Total playtime with this profile active (milliseconds).
        /// </summary>
        public long TotalPlaytimeMs { get; set; } = 0;

        /// <summary>
        /// Formatted playtime string for display.
        /// </summary>
        public string FormattedPlaytime
        {
            get
            {
                var ts = TimeSpan.FromMilliseconds(TotalPlaytimeMs);
                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}h {ts.Minutes}m";
                else if (ts.TotalMinutes >= 1)
                    return $"{(int)ts.TotalMinutes}m";
                else
                    return "< 1m";
            }
        }

        /// <summary>
        /// Creates a deep copy of this profile.
        /// </summary>
        public GameProfile Clone()
        {
            return new GameProfile
            {
                Id = Guid.NewGuid().ToString(), // Generate new ID for clone
                Name = $"{Name} (Copy)",
                ExecutableName = ExecutableName,
                ExecutablePath = ExecutablePath,
                IsEnabled = IsEnabled,
                FanPresetName = FanPresetName,
                PerformanceModeName = PerformanceModeName,
                CpuCoreOffsetMv = CpuCoreOffsetMv,
                CpuCacheOffsetMv = CpuCacheOffsetMv,
                GpuMode = GpuMode,
                KeyboardLightingProfileName = KeyboardLightingProfileName,
                PeripheralLightingProfileName = PeripheralLightingProfileName,
                Notes = Notes,
                Priority = Priority,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                LaunchCount = 0,
                TotalPlaytimeMs = 0
            };
        }

        /// <summary>
        /// Checks if this profile matches a given process.
        /// </summary>
        public bool MatchesProcess(string processName, string? processPath = null)
        {
            if (!IsEnabled) return false;

            // Exact executable name match (case-insensitive)
            if (!string.IsNullOrEmpty(ExecutableName) &&
                string.Equals(processName, ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                // If path is specified, verify it matches too
                if (!string.IsNullOrEmpty(ExecutablePath) && !string.IsNullOrEmpty(processPath))
                {
                    return string.Equals(processPath, ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            }

            return false;
        }
    }
}
