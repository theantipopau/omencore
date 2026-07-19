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
        /// Optional: substring (case-insensitive) that must appear in the process's main window title
        /// for this profile to match. Disambiguates multiple profiles that share the same executable
        /// (e.g. a common launcher/emulator/runtime hosting different games under one process name).
        /// When set, a process whose window title doesn't contain this text will not match this profile
        /// even if the executable name matches.
        /// </summary>
        public string? WindowTitleContains { get; set; }

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
        /// Whether OmenCore should restore default fan/performance settings when this game exits.
        /// Disable for launchers or workflows where another automation rule should keep control.
        /// </summary>
        public bool RestoreDefaultsOnExit { get; set; } = true;

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
                WindowTitleContains = WindowTitleContains,
                IsEnabled = IsEnabled,
                FanPresetName = FanPresetName,
                PerformanceModeName = PerformanceModeName,
                CpuCoreOffsetMv = CpuCoreOffsetMv,
                CpuCacheOffsetMv = CpuCacheOffsetMv,
                GpuMode = GpuMode,
                KeyboardLightingProfileName = KeyboardLightingProfileName,
                PeripheralLightingProfileName = PeripheralLightingProfileName,
                RestoreDefaultsOnExit = RestoreDefaultsOnExit,
                Notes = Notes,
                Priority = Priority,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                LaunchCount = 0,
                TotalPlaytimeMs = 0
            };
        }

        private static string NormalizeExecutableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            name = name.Trim();

            // Windows process names from Process.ProcessName typically omit the ".exe" extension.
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[0..^4];
            }

            return name;
        }

        /// <summary>
        /// Checks if this profile matches a given process.
        /// </summary>
        public bool MatchesProcess(string processName, string? processPath = null, string? windowTitle = null)
        {
            return GetProcessMatchScore(processName, processPath, windowTitle) > 0;
        }

        /// <summary>
        /// Returns a match strength for profile conflict resolution: 0 = no match, 1 = executable-name
        /// match, +1 for an exact executable-path match, +2 for a <see cref="WindowTitleContains"/> match.
        /// A profile with <see cref="WindowTitleContains"/> configured requires the title to match — this
        /// is what lets two profiles sharing the same executable (a launcher/emulator/runtime hosting
        /// different games) resolve to the right one instead of a coin-flip on registration order.
        /// </summary>
        public int GetProcessMatchScore(string processName, string? processPath = null, string? windowTitle = null)
        {
            if (!IsEnabled) return 0;

            // Exact executable name match (case-insensitive), tolerant of optional ".exe" suffix
            var normalizedProcess = NormalizeExecutableName(processName);
            var normalizedProfile = NormalizeExecutableName(ExecutableName);

            if (string.IsNullOrEmpty(normalizedProfile) ||
                !string.Equals(normalizedProcess, normalizedProfile, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            // If a path is specified, it must match exactly.
            var pathMatched = !string.IsNullOrWhiteSpace(ExecutablePath);
            if (pathMatched)
            {
                if (string.IsNullOrWhiteSpace(processPath) ||
                    !string.Equals(processPath, ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
            }

            // If a window-title disambiguator is configured, it must match too.
            var titleMatched = false;
            if (!string.IsNullOrWhiteSpace(WindowTitleContains))
            {
                if (string.IsNullOrWhiteSpace(windowTitle) ||
                    windowTitle.IndexOf(WindowTitleContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return 0;
                }
                titleMatched = true;
            }

            var score = 1;
            if (pathMatched) score += 1;
            if (titleMatched) score += 2;
            return score;
        }
    }
}
