using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OmenCore.Services
{
    /// <summary>
    /// Phase-by-phase startup timing for the current process, measured from the operating system's
    /// own process start time rather than from the first line of OmenCore code.
    ///
    /// Roadmap WP-31: the app had one number for startup (time-to-interactive) and no breakdown of
    /// where the time went, so any "startup is slow" work was guesswork. Each <see cref="Mark"/>
    /// records how long after process start the phase completed and how much of that was that phase
    /// alone, which is what makes a fix attributable: without the per-phase delta, moving work into
    /// the background looks identical to deleting it.
    ///
    /// Cheap by construction - one <see cref="DateTime.UtcNow"/> read and a list add per call, no
    /// allocation per sample beyond the record, first occurrence of a name wins so a loop cannot
    /// grow the list.
    /// </summary>
    public static class StartupTimeline
    {
        private static readonly object Lock = new();
        private static readonly List<StartupPhaseMark> Marks = new();
        private static readonly HashSet<string> Recorded = new(StringComparer.Ordinal);
        private static readonly DateTime ProcessStartUtc = ResolveProcessStartUtc();

        /// <summary>
        /// Optional sink for writing each mark to the log as it happens. Set once, right after the
        /// logging service is initialized; null before that, which is why the earliest marks are
        /// still recorded (they just are not logged individually).
        /// </summary>
        public static Action<string>? Sink { get; set; }

        /// <summary>
        /// Records a named phase. Only the first call for a given name counts - callers are not
        /// expected to guard against repeats, and a page that is constructed twice must not make
        /// the timeline ambiguous.
        /// </summary>
        public static void Mark(string phase)
        {
            if (string.IsNullOrWhiteSpace(phase))
            {
                return;
            }

            StartupPhaseMark mark;
            lock (Lock)
            {
                if (!Recorded.Add(phase))
                {
                    return;
                }

                var elapsedMs = Math.Max(0, (DateTime.UtcNow - ProcessStartUtc).TotalMilliseconds);
                var previousMs = Marks.Count == 0 ? 0 : Marks[^1].ElapsedMs;
                mark = new StartupPhaseMark(phase, elapsedMs, Math.Max(0, elapsedMs - previousMs));
                Marks.Add(mark);
            }

            Sink?.Invoke($"[startup] {mark.Phase}: {mark.ElapsedMs:F0} ms (phase +{mark.SincePreviousMs:F0} ms)");
        }

        public static IReadOnlyList<StartupPhaseMark> GetMarks()
        {
            lock (Lock)
            {
                return Marks.ToList();
            }
        }

        /// <summary>Total milliseconds from process start to the last recorded phase.</summary>
        public static double? GetTotalElapsedMs()
        {
            lock (Lock)
            {
                return Marks.Count == 0 ? null : Marks[^1].ElapsedMs;
            }
        }

        // Intended for deterministic tests.
        public static void ResetForTests()
        {
            lock (Lock)
            {
                Marks.Clear();
                Recorded.Clear();
            }
        }

        private static DateTime ResolveProcessStartUtc()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.StartTime.ToUniversalTime();
            }
            catch
            {
                // StartTime is unavailable in some restricted contexts; falling back to "now" only
                // makes the first mark read as ~0 ms, which is still internally consistent.
                return DateTime.UtcNow;
            }
        }
    }

    public sealed record StartupPhaseMark(string Phase, double ElapsedMs, double SincePreviousMs);
}
