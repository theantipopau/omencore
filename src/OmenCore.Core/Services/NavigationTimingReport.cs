using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OmenCore.Services
{
    /// <summary>
    /// Per-page navigation timings, written to a file so a second process can read them.
    ///
    /// <see cref="StartupAndNavigationPerformanceTracker"/> already accumulates these in memory for
    /// the diagnostics export, but the cost being profiled belongs to the WPF window, which only
    /// exists inside the app process. The CLI's <c>nav</c> verb therefore launches the app in a
    /// cycling mode and reads what it left here, instead of a console process trying to drive a UI
    /// it cannot host (see docs/ROADMAP_v5.0.0.md, WP-33).
    ///
    /// The file is a plain snapshot, not a history: a run overwrites it, and the timestamps are there
    /// so a report found lying around can be told apart from the one a measurement just produced.
    /// </summary>
    public sealed class NavigationTimingReport
    {
        public string GeneratedAtUtc { get; set; } = string.Empty;

        /// <summary>How many times the cycle walked the pages.</summary>
        public int Passes { get; set; }

        /// <summary>Time to interactive for the run that produced this report, when it was recorded.</summary>
        public double? StartupTimeToInteractiveMs { get; set; }

        public List<NavigationTimingEntry> Pages { get; set; } = new();

        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        /// <summary>%LOCALAPPDATA%\OmenCore\nav-report.json - next to the logs, not in the install directory.</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmenCore",
            "nav-report.json");

        public static NavigationTimingReport FromSnapshot(StartupAndNavigationSnapshot snapshot, int passes) => new()
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Passes = passes,
            StartupTimeToInteractiveMs = snapshot.StartupTimeToInteractiveMs,
            Pages = snapshot.TabSwitches
                .Select(page => new NavigationTimingEntry
                {
                    Page = page.TabName,
                    Samples = page.Count,
                    AverageMs = Math.Round(page.AverageMs, 1),
                    MaxMs = Math.Round(page.MaxMs, 1),
                })
                .ToList(),
        };

        /// <summary>Slowest page by average, or null when nothing was measured.</summary>
        public NavigationTimingEntry? Slowest => Pages.Count == 0
            ? null
            : Pages.OrderByDescending(page => page.AverageMs).First();

        public void Save(string? path = null)
        {
            string target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(target, JsonSerializer.Serialize(this, SerializerOptions));
        }

        /// <summary>
        /// Reads a report, or null when the file is missing or unreadable. A failed read is not worth
        /// an exception: the caller's job is to say "no timings were written", which covers both.
        /// </summary>
        public static NavigationTimingReport? TryRead(string? path = null)
        {
            string target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;

            try
            {
                if (!File.Exists(target))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<NavigationTimingReport>(File.ReadAllText(target), SerializerOptions);
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class NavigationTimingEntry
    {
        public string Page { get; set; } = string.Empty;
        public int Samples { get; set; }
        public double AverageMs { get; set; }
        public double MaxMs { get; set; }
    }
}
