// Identifies the AMD SMU power-metrics table layout for THIS silicon, by anchoring.
//
// OmenCore can write the four power limits but has never been able to read them back, so every
// message it showed the user was "what the SMU accepted", not "what it is running". The PawnIO
// RyzenSMU module it already ships exposes ioctl_resolve_pm_table / ioctl_update_pm_table /
// ioctl_read_pm_table, so the table is reachable. What is missing is the LAYOUT: which float
// index holds which limit. That is per-table-version data, and LibreHardwareMonitor 0.9.6 has no
// map for this part's version.
//
// So it is measured rather than guessed. Two stages, and only the second one identifies anything:
//
//   A  Read the table, and read the same limits through an independent implementation
//      (ryzenadj). Every index whose float equals a known limit is a CANDIDATE. This alone is
//      not an answer - on this machine all four limits read 66 W, and plenty of unrelated table
//      slots hold 66.0 too.
//
//   B  --ab <watts> writes a DIFFERENT limit and re-reads. An index that held the phase-A value
//      and now holds the phase-B value tracked the write; one that held 66.0 by coincidence did
//      not. The intersection is the identification.
//
// Without --ab this mode is read-only and reports candidates, clearly labelled as such.
// With --ab it WRITES the four power limits and restores the phase-A values in a finally.
//
// Anchoring rather than porting an offset table is deliberate: see the note in
// reference/board-8d87 on why an offset guessed from another codename produces a confident wrong
// answer. Here the anchor is a second implementation reading the same registers in the same
// second, and the discriminator is a value only the real slot can follow.

using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Tools.SmuProbe;

internal static class PmTable
{
    /// <summary>
    /// How generously to read when the table size for this version is unknown. The module
    /// bounds its own read, so asking for more than the table holds costs a truncated reply
    /// rather than a fault.
    /// </summary>
    private const uint DefaultSizeBytes = 4096;

    /// <summary>
    /// Limits are written and read as whole milliwatts and come back exact, so this only has to
    /// absorb float32 rounding - not measurement noise.
    /// </summary>
    private const double MatchTolerance = 0.01;

    /// <summary>Settle time after writing the phase-B limits before re-reading the table.</summary>
    private const int SettleMs = 1500;

    internal static int Run(string[] args)
    {
        uint sizeBytes = ArgUInt(args, "--size", DefaultSizeBytes);
        uint abWatts = ArgUInt(args, "--ab", 0);
        string? ryzenAdjPath = ArgString(args, "--readback");
        string? dumpPath = ArgString(args, "--dump");

        Console.WriteLine("=== AMD SMU PM table layout identification ===");

        RyzenControl.Init();
        Console.WriteLine($"CPU    : {RyzenControl.CpuName}");
        Console.WriteLine($"Family : {RyzenControl.Family}");

        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);

        if (!smu.TryResolvePmTable(out uint version, out uint tableBase))
        {
            Console.WriteLine("FAIL: ioctl_resolve_pm_table did not answer. This part's SMU may not");
            Console.WriteLine("      expose a DRAM power table, or the module could not map it.");
            return 1;
        }

        Console.WriteLine($"Table  : version 0x{version:X}  base 0x{tableBase:X}");
        Console.WriteLine($"Reading: {sizeBytes} bytes ({sizeBytes / 4} floats)\n");

        if (!smu.TryReadPmTable(sizeBytes, out float[] phaseA))
        {
            Console.WriteLine("FAIL: the table could not be read.");
            return 1;
        }

        int populated = LastPopulatedIndex(phaseA);
        Console.WriteLine($"Populated through index {populated} " +
                          $"(byte offset 0x{populated * 4:X}); the rest read zero.");
        if (populated >= phaseA.Length - 1)
            Console.WriteLine("NOTE: no trailing zeros, so the real table may be larger than the read. " +
                              "Re-run with a bigger --size before trusting the tail.");
        Console.WriteLine();

        if (dumpPath != null)
        {
            Dump(dumpPath, phaseA, version, tableBase);
            Console.WriteLine($"Full phase-A dump written to {dumpPath}\n");
        }

        var reader = RyzenAdjReader.TryCreate(ryzenAdjPath);
        if (reader == null)
        {
            Console.WriteLine("No --readback path given, or ryzenadj.exe not found there. Without an");
            Console.WriteLine("independent read of the same limits there is nothing to anchor against,");
            Console.WriteLine("so this run can only dump the table, not identify anything in it.");
            return 0;
        }

        var snapA = reader.Read();
        if (snapA == null)
        {
            Console.WriteLine("FAIL: the readback oracle produced no rows.");
            return 1;
        }

        var rows = Rows(snapA);
        Console.WriteLine("Phase A - limits as the oracle reads them, and every table index matching:");
        foreach (var row in rows)
        {
            var hits = Matches(phaseA, row.Value);
            Console.WriteLine($"  {row.Name,-16} {row.Value,8:F3}   " +
                              (hits.Count == 0
                                  ? "no index holds this value"
                                  : $"candidates: {string.Join(", ", hits)}"));
        }
        Console.WriteLine();

        if (abWatts == 0)
        {
            Console.WriteLine("Read-only run. These are CANDIDATES, not an identification - several");
            Console.WriteLine("unrelated slots can hold the same number. Re-run with --ab <watts> to");
            Console.WriteLine("write a different limit and keep only the indices that follow it.");
            return 0;
        }

        // ---- Phase B: writes ----
        using var provider = new AmdUndervoltProvider();
        Console.WriteLine($"Backend: {provider.ActiveBackend}");
        if (provider.ActiveBackend == "None")
        {
            Console.WriteLine("FAIL: no SMU write backend, so phase B cannot run.");
            return 1;
        }

        // Restore to what the oracle read at phase A, not to a guess at stock.
        var restore = new RyzenPowerLimits
        {
            StapmLimit = (uint)Math.Round(snapA.StapmLimit * 1000),
            FastLimit = (uint)Math.Round(snapA.FastLimit * 1000),
            SlowLimit = (uint)Math.Round(snapA.SlowLimit * 1000),
            ApuSlowLimit = (uint)Math.Round(snapA.ApuLimit * 1000)
        };

        // ONE limit per sub-phase, not a spread across all four.
        //
        // A spread does not separate STAPM from FAST on this part: measured 2026-08-22, writing
        // stapm 50 / fast 53 made the oracle read BOTH as 53.000, so the two candidate indices
        // moved together and stayed mutually ambiguous. STAPM follows the fast limit here, and no
        // choice of four simultaneous values gets around that.
        //
        // Moving one limit while the other three are held at their phase-A values does: whichever
        // index changes is the one just written, and a row that drags another with it is visible
        // as two changed indices rather than hidden behind equal values. Each sub-phase is undone
        // before the next begins, so the four measurements are independent rather than cumulative.
        var probes = new (string Name, Func<uint, RyzenSmu.SmuStatus> Write, uint RestoreMw)[]
        {
            ("STAPM LIMIT", provider.SetStapmLimit,   restore.StapmLimit),
            ("PPT FAST",    provider.SetFastLimit,    restore.FastLimit),
            ("PPT SLOW",    provider.SetSlowLimit,    restore.SlowLimit),
            ("PPT APU",     provider.SetApuSlowLimit, restore.ApuSlowLimit)
        };

        Console.WriteLine($"Phase B: moving one limit at a time to {abWatts} W, restoring between.\n");
        Console.WriteLine("=== Identification ===");
        Console.WriteLine("For each limit: the table indices that changed while ONLY that limit was");
        Console.WriteLine("written. A single changed index is an identification.\n");

        var resolved = new Dictionary<string, int>();
        int unresolved = 0;

        try
        {
            for (int p = 0; p < probes.Length; p++)
            {
                var probe = probes[p];

                // Both the status AND the outcome. A write the SMU answers Ok to that moves
                // nothing is the failure shape this whole harness exists for, and it is only
                // visible if the two are reported side by side.
                var status = probe.Write(abWatts * 1000);
                Thread.Sleep(SettleMs);

                if (!smu.TryReadPmTable(sizeBytes, out float[] after))
                {
                    Console.WriteLine($"  {probe.Name,-16} FAIL: table could not be re-read.");
                    unresolved++;
                }
                else
                {
                    // Compare against phase A rather than against the previous sub-phase: every
                    // sub-phase is restored, so phase A is the common baseline.
                    var moved = Changed(phaseA, after);

                    // Only slots that were sitting on a limit value are of interest; the live
                    // power/temperature readings move on their own between any two samples.
                    var limitSlots = moved.Where(i => Matches(phaseA, rows[p].Value).Contains(i)).ToList();

                    Console.WriteLine($"  {probe.Name,-16} -> {abWatts} W  SMU {status,-8} " +
                                      (limitSlots.Count switch
                                      {
                                          0 => $"no candidate index moved ({moved.Count} unrelated slots did)" +
                                               (status == RyzenSmu.SmuStatus.Ok
                                                   ? "  <-- accepted but INERT"
                                                   : string.Empty),
                                          1 => $"index {limitSlots[0]}  (byte offset 0x{limitSlots[0] * 4:X})",
                                          _ => $"moved together: {string.Join(", ", limitSlots)}"
                                      }));

                    if (limitSlots.Count == 1) resolved[probe.Name] = limitSlots[0];
                    else unresolved++;
                }

                probe.Write(probe.RestoreMw);
                Thread.Sleep(SettleMs);
            }

            // A limit that dragged another with it leaves two indices claimed by two rows. Where
            // three of four are known, the fourth follows by elimination - stated as elimination,
            // not as a direct observation.
            Console.WriteLine();
            var claimed = new HashSet<int>(resolved.Values);
            foreach (var row in rows.Take(4).Select(r => r.Name))
            {
                if (resolved.ContainsKey(row)) continue;
                var left = Matches(phaseA, rows.First(r => r.Name == row).Value)
                    .Where(i => !claimed.Contains(i)).ToList();
                if (left.Count == 1)
                {
                    Console.WriteLine($"  {row,-16} index {left[0]} (byte offset 0x{left[0] * 4:X}) " +
                                      "by elimination - the only candidate no other row claimed.");
                    resolved[row] = left[0];
                    unresolved--;
                }
            }

            Console.WriteLine();
            Console.WriteLine(unresolved == 0
                ? $"All four power limits resolved for table version 0x{version:X}."
                : $"{unresolved} row(s) unresolved. Re-run at a different --ab wattage.");

            Console.WriteLine("\nLayout:");
            foreach (var kv in resolved.OrderBy(k => k.Value))
                Console.WriteLine($"  index {kv.Value,3}  (0x{kv.Value * 4:X3})  {kv.Key}");

            return unresolved == 0 ? 0 : 1;
        }
        finally
        {
            Console.WriteLine($"\nRestoring {snapA.StapmLimit:F0}/{snapA.FastLimit:F0}/" +
                              $"{snapA.SlowLimit:F0}/{snapA.ApuLimit:F0} W ...");
            try { provider.ApplyPowerLimits(restore); } catch { /* best effort */ }
            Console.WriteLine("Runtime SMU state only - a reboot restores the firmware's own limits.");
        }
    }

    private readonly record struct Row(string Name, double Value);

    /// <summary>
    /// The rows worth identifying, in a fixed order so phase A and phase B line up by index.
    /// </summary>
    private static List<Row> Rows(SmuSnapshot s) => new()
    {
        new("STAPM LIMIT", s.StapmLimit),
        new("PPT FAST", s.FastLimit),
        new("PPT SLOW", s.SlowLimit),
        new("PPT APU", s.ApuLimit),
        new("TDC VDD", s.TdcLimit),
        new("EDC VDD", s.EdcLimit),
        new("THM CORE", s.ThmLimit)
    };

    private static List<int> Matches(float[] table, double value)
    {
        var hits = new List<int>();
        if (value == 0) return hits;
        for (int i = 0; i < table.Length; i++)
        {
            if (Math.Abs(table[i] - value) <= MatchTolerance) hits.Add(i);
        }
        return hits;
    }

    /// <summary>
    /// Indices whose value differs between two reads of the table. Exact inequality, not a
    /// tolerance: a limit slot written to a new whole-watt value changes decisively, and a
    /// tolerance here would only let a nearly-unchanged live reading count as movement.
    /// </summary>
    private static List<int> Changed(float[] before, float[] after)
    {
        var moved = new List<int>();
        int n = Math.Min(before.Length, after.Length);
        for (int i = 0; i < n; i++)
        {
            if (before[i] != after[i]) moved.Add(i);
        }
        return moved;
    }

    /// <summary>Highest index holding anything other than zero.</summary>
    private static int LastPopulatedIndex(float[] table)
    {
        for (int i = table.Length - 1; i >= 0; i--)
        {
            if (table[i] != 0f) return i;
        }
        return -1;
    }

    private static void Dump(string path, float[] table, uint version, uint tableBase)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($"# PM table version 0x{version:X}, base 0x{tableBase:X}, {table.Length} floats");
        w.WriteLine($"# cpu={RyzenControl.CpuName} family={RyzenControl.Family}");
        w.WriteLine("index,byte_offset,value");
        for (int i = 0; i < table.Length; i++)
        {
            w.WriteLine($"{i},0x{i * 4:X},{table[i]:R}");
        }
    }

    private static uint ArgUInt(string[] args, string name, uint fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && uint.TryParse(args[i + 1], out uint v) ? v : fallback;
    }

    private static string? ArgString(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
