using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace OmenCore.Models
{
    /// <summary>
    /// Identifies processes and services that conflict with OmenCore's CPU/GPU tuning operations.
    /// Call <see cref="CheckAsync"/> immediately before applying a CPU undervolt or GPU OC
    /// so the UI can warn the user and require explicit opt-in before proceeding.
    /// </summary>
    public static class TuningConflictGuard
    {
        // -----------------------------------------------------------------------------------------
        // Known conflicting processes for tuning operations
        // -----------------------------------------------------------------------------------------
        private static readonly TuningConflictEntry[] KnownConflicts =
        {
            new("XTU",
                TuningConflictKind.CpuUndervolt,
                new[] { "XTU", "IntelXTU", "XTU3Service" },
                serviceName: "XTU3SERVICE",
                title: "Intel Extreme Tuning Utility (XTU)",
                detail: "XTU loads its own WinRing0 kernel driver and applies its own CPU voltage offsets, " +
                        "which will race with OmenCore's undervolt writes and may cause a crash or blue screen.",
                suggestion: "Close XTU and stop the XTU3Service before applying a CPU undervolt."),

            new("ThrottleStop",
                TuningConflictKind.CpuUndervolt,
                new[] { "ThrottleStop" },
                serviceName: null,
                title: "ThrottleStop",
                detail: "ThrottleStop writes its own power limits and voltage offsets continuously. " +
                        "Running both apps simultaneously will produce unpredictable CPU behaviour.",
                suggestion: "Close ThrottleStop before applying a CPU undervolt in OmenCore."),

            new("IntelDTT",
                TuningConflictKind.CpuUndervolt,
                new[] { "esif_uf", "EsifUpSvc" },
                serviceName: "esif_uf",
                title: "Intel Dynamic Tuning Technology (DTT)",
                detail: "Intel DTT manages dynamic power limits via the ESIF platform. " +
                        "Its live adjustments may override or conflict with OmenCore's static undervolt.",
                suggestion: "Stop the esif_uf / Intel DTT service before applying a CPU undervolt."),

            new("MSIAfterburner",
                TuningConflictKind.GpuOc,
                new[] { "MSIAfterburner" },
                serviceName: null,
                title: "MSI Afterburner",
                detail: "MSI Afterburner applies its own GPU clock and voltage curves. " +
                        "Running both simultaneously may result in unpredictable clocks or driver crashes.",
                suggestion: "Close MSI Afterburner before applying a GPU OC in OmenCore, " +
                            "or use only one tool for GPU overclocking."),

            new("NvidiaApp",
                TuningConflictKind.GpuOc,
                new[] { "NvOverlayCM", "NVIDIA app", "NVDisplay.Container" },
                serviceName: null,
                title: "NVIDIA App / GeForce Experience",
                detail: "NVIDIA App can apply its own GPU performance tuning (auto-OC). " +
                        "This may conflict with OmenCore's GPU clock offset.",
                suggestion: "Disable NVIDIA App auto-performance tuning before applying a GPU OC in OmenCore."),

            new("OmenLightStudio",
                TuningConflictKind.All,
                new[] { "OMEN Light Studio", "OmenLightStudio", "OmenCap" },
                serviceName: null,
                title: "OMEN Light Studio / OmenCap",
                detail: "OMEN Light Studio and OmenCap manage RGB lighting and peripheral devices. " +
                        "While not directly tuning, they can compete for hardware control and cause instability.",
                suggestion: "Close OMEN Light Studio and OmenCap before applying critical CPU or GPU tuning."),
        };

        /// <summary>
        /// Scan running processes and services for conflicts relevant to the requested tuning operation.
        /// Returns all detected conflicts. Empty list means no conflicts.
        /// </summary>
        /// <param name="kind">Which kind of tuning is about to be applied.</param>
        public static TuningConflictReport Check(TuningConflictKind kind)
        {
            var detected = new List<TuningConflictEntry>();
            try
            {
                var runningProcesses = Process.GetProcesses()
                    .Select(p => { try { return p.ProcessName; } catch { return string.Empty; } })
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var conflict in KnownConflicts)
                {
                    if ((conflict.Kind & kind) == 0)
                        continue;

                    bool processRunning = conflict.ProcessNames.Any(runningProcesses.Contains);

                    bool serviceRunning = false;
                    if (!processRunning && conflict.ServiceName != null)
                    {
                        serviceRunning = IsServiceRunning(conflict.ServiceName);
                    }

                    if (processRunning || serviceRunning)
                        detected.Add(conflict);
                }
            }
            catch
            {
                // Scanning is best-effort; don't block the tuning apply path on scan failures.
            }

            return new TuningConflictReport(kind, detected);
        }

        private static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
                if (key == null)
                    return false;
                // Start type 4 = disabled; state is live in SCM, but registry start value gives best-effort info
                var start = key.GetValue("Start");
                return start is int s && s != 4;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Which tuning operations a conflict entry applies to. Flags so a single entry can cover both.
    /// </summary>
    [Flags]
    public enum TuningConflictKind
    {
        None = 0,
        CpuUndervolt = 1,
        GpuOc = 2,
        All = CpuUndervolt | GpuOc,
    }

    /// <summary>
    /// A single known conflicting application or service entry.
    /// </summary>
    public class TuningConflictEntry
    {
        public string Id { get; }
        public TuningConflictKind Kind { get; }
        public string[] ProcessNames { get; }
        public string? ServiceName { get; }
        public string Title { get; }
        public string Detail { get; }
        public string Suggestion { get; }

        public TuningConflictEntry(
            string id,
            TuningConflictKind kind,
            string[] processNames,
            string? serviceName,
            string title,
            string detail,
            string suggestion)
        {
            Id = id;
            Kind = kind;
            ProcessNames = processNames;
            ServiceName = serviceName;
            Title = title;
            Detail = detail;
            Suggestion = suggestion;
        }
    }

    /// <summary>
    /// Result of a <see cref="TuningConflictGuard.Check"/> call.
    /// </summary>
    public class TuningConflictReport
    {
        public TuningConflictKind Kind { get; }
        public IReadOnlyList<TuningConflictEntry> Conflicts { get; }
        public bool HasConflicts => Conflicts.Count > 0;

        /// <summary>
        /// True when any detected conflict has high-risk implications (i.e. it actively writes
        /// the same hardware registers that OmenCore will write).
        /// </summary>
        public bool HasHighRiskConflict => Conflicts.Any(c =>
            c.Id is "XTU" or "ThrottleStop" or "IntelDTT" or "MSIAfterburner");

        /// <summary>
        /// One-line banner text for display in the UI (empty when no conflicts).
        /// </summary>
        public string BannerText => HasConflicts
            ? $"Potential conflict: {string.Join(", ", Conflicts.Select(c => c.Title))} detected — see details before applying."
            : string.Empty;

        public TuningConflictReport(TuningConflictKind kind, IReadOnlyList<TuningConflictEntry> conflicts)
        {
            Kind = kind;
            Conflicts = conflicts;
        }

        /// <summary>Returns an empty (no-conflict) report.</summary>
        public static TuningConflictReport None(TuningConflictKind kind) =>
            new(kind, Array.Empty<TuningConflictEntry>());
    }
}
