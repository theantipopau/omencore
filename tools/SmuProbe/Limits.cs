// Verifies the AMD SMU power limits by OUTCOME.
//
// The SMU answers Ok to messages that change nothing, and the PawnIO transport has no readback,
// so AmdUndervoltProvider cannot check its own work. This mode checks it from outside, on three
// signals that fail independently:
//
//   1. LIMIT READBACK - what the SMU says its limit now is, read by an external ryzenadj.exe over
//      its own driver. A value OmenCore wrote and ryzenadj reads back has crossed two unrelated
//      transports.
//
//   2. SMU POWER VALUE - what the SMU says the part is actually drawing in the domain the limit
//      governs (STAPM VALUE / PPT VALUE). This is the outcome: a limit that is in force shows up
//      as the value pinned against it under load.
//
//   3. WINDOWS ENERGY METER - an OS-level witness, reported for direction only.
//
// Signal 3 is deliberately NOT the primary axis, and the reason is worth keeping. An earlier
// version of this file used it as the only measurement and concluded the load "was not
// power-bound" at 27 W against a 45 W limit - while the SMU's own telemetry read STAPM VALUE
// 44.994 against LIMIT 45.000 at the same moment. The counter is a different domain, reads about
// 60% of PPT on this part, and comparing it to a watt figure the SMU set was comparing two
// different quantities. It tracks direction faithfully and absolute level not at all. That is the
// same "checked a proxy instead of the outcome" mistake this project keeps finding, so it is now
// labelled as a witness rather than a measurement.
//
// The run is A -> B -> A' [-> C]:
//
//   A    stock limits
//   B    a LOW limit. Clamping down is the strong direction: lowering below current draw MUST
//        reduce power if the write landed, so a null result is real evidence of failure rather
//        than an ambiguous one. It is also the safe direction, so it can run unattended.
//   A'   stock again. This is the control - drift is monotonic across a run, so an effect that
//        appears in B and goes away again in A' is not drift.
//   C    optionally a HIGH limit, above stock. Only answerable when the load is power-bound at
//        stock, which A establishes; otherwise there is no headroom for the answer to show in.
//
// A NOTE ON ryzenadj. This repo does not use WinRing0 and must not start - ryzenadj ships it, and
// that driver is exactly what OmenCore removed over the Defender VulnerableDriver detection. So:
// nothing here bundles, installs or downloads it, the path must be given explicitly with
// --readback, and it is only ever invoked to READ. It is a lab oracle for confirming OmenCore's
// own writes, not a dependency, and it is not reachable from the application.
//
// WRITES power limits. Restores the stock values in a finally.

using System.Diagnostics;
using System.Globalization;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Tools.SmuProbe;

internal static class Limits
{
    private const int WarmupMs = 45000;
    private const int SettleMs = 20000;
    private const int SampleMs = 15000;

    private const uint DefaultLowWatts = 20;

    /// <summary>
    /// How close the SMU's own power value must sit to the requested limit to count as pinned
    /// against it. The value floats by a few hundred milliwatts under a steady load.
    /// </summary>
    private const double PinnedToleranceWatts = 2.5;

    internal static int Run(string[] args)
    {
        uint lowWatts = ArgUInt(args, "--watts", DefaultLowWatts);
        uint highWatts = ArgUInt(args, "--high", 0);
        string? ryzenAdjPath = ArgString(args, "--readback");

        Console.WriteLine("=== AMD SMU power limit outcome verification ===");

        RyzenControl.Init();
        Console.WriteLine($"CPU    : {RyzenControl.CpuName}");
        Console.WriteLine($"Family : {RyzenControl.Family}");
        Console.WriteLine($"Ceiling: {RyzenControl.GetMaxPowerLimitMw() / 1000} W (what this build will ask for)");

        using var provider = new AmdUndervoltProvider();
        Console.WriteLine($"Backend: {provider.ActiveBackend}\n");

        if (provider.ActiveBackend == "None")
        {
            Console.WriteLine("FAIL: SMU backend unavailable. Run elevated with PawnIO installed.");
            return 1;
        }

        var reader = RyzenAdjReader.TryCreate(ryzenAdjPath);
        if (reader == null)
        {
            Console.WriteLine("No --readback path given, or ryzenadj.exe not found there.");
            Console.WriteLine("Without it there is no SMU-domain measurement and no independent readback,");
            Console.WriteLine("only the Windows counter - which is direction-only. Pass --readback for a");
            Console.WriteLine("result worth citing.\n");
        }

        uint stockWatts = 0;
        var stock = reader?.Read();
        if (stock != null)
        {
            Console.WriteLine("Stock limits (read by ryzenadj):");
            Console.WriteLine($"  stapm {stock.StapmLimit:F3} W   fast {stock.FastLimit:F3} W   " +
                              $"slow {stock.SlowLimit:F3} W   apu-slow {stock.ApuLimit:F3} W");
            Console.WriteLine($"  tdc {stock.TdcLimit:F1} A   edc {stock.EdcLimit:F1} A   thm {stock.ThmLimit:F0} C\n");
            stockWatts = (uint)Math.Round(stock.StapmLimit);
        }

        if (stockWatts == 0)
        {
            Console.WriteLine("WARNING: stock limits could not be read, so the original values are unknown.");
            Console.WriteLine("         This run will restore 45 W, which may not be what this machine had.");
            Console.WriteLine("         A reboot restores the real stock values - nothing here is persistent.\n");
            stockWatts = 45;
        }

        Console.Write($"Plan   : A ({stockWatts} W) -> B ({lowWatts} W) -> A' ({stockWatts} W)");
        Console.WriteLine(highWatts > 0 ? $" -> C ({highWatts} W)\n" : "\n");

        using var load = new CpuLoad();
        bool restored = false;
        try
        {
            Console.WriteLine($"Starting all-core vector load on {Environment.ProcessorCount} threads ...");
            load.Start();
            Console.WriteLine($"Warming up {WarmupMs / 1000}s so STAPM decay has already happened ...");
            Thread.Sleep(WarmupMs);

            var a = Phase(provider, reader, "A  (stock)", stockWatts);
            var b = Phase(provider, reader, $"B  (low {lowWatts} W)", lowWatts);
            var aPrime = Phase(provider, reader, "A' (stock)", stockWatts);
            Phased? c = highWatts > 0 ? Phase(provider, reader, $"C  (high {highWatts} W)", highWatts) : null;

            // Put it back before judging, so a long analysis is not spent at a raised limit.
            provider.ApplyPowerLimits(All(stockWatts));
            restored = true;

            Console.WriteLine();
            Console.WriteLine("=== Result ===");
            Console.WriteLine($"  A  {stockWatts,3} W : {a}");
            Console.WriteLine($"  B  {lowWatts,3} W : {b}");
            Console.WriteLine($"  A' {stockWatts,3} W : {aPrime}");
            if (c != null) Console.WriteLine($"  C  {highWatts,3} W : {c}");
            Console.WriteLine();

            return Judge(a, b, aPrime, c, lowWatts, highWatts, stockWatts, reader != null);
        }
        finally
        {
            load.Stop();
            if (!restored)
            {
                Console.WriteLine($"\nRestoring limits to {stockWatts} W ...");
                try { provider.ApplyPowerLimits(All(stockWatts)); } catch { /* best effort */ }
            }
            Console.WriteLine("Runtime SMU state only - a reboot restores the firmware's own limits.");
        }
    }

    private static int Judge(
        Phased a, Phased b, Phased aPrime, Phased? c,
        uint lowWatts, uint highWatts, uint stockWatts, bool hadReader)
    {
        if (!hadReader)
        {
            Console.WriteLine("INCOMPLETE: no independent reader, so only the Windows counter was available.");
            Console.WriteLine($"            Direction was {a.CounterWatts:F1} -> {b.CounterWatts:F1} -> {aPrime.CounterWatts:F1} W.");
            Console.WriteLine("            Re-run with --readback for a citable result.");
            return 2;
        }

        // --- Signal 1: did the SMU take the value? ---
        bool wroteLow = Near(b.LimitWatts, lowWatts, 1.0);
        Console.WriteLine(wroteLow
            ? $"Limit readback   : phase B reads {b.LimitWatts:F3} W - ryzenadj read back what OmenCore wrote."
            : $"Limit readback   : phase B reads {b.LimitWatts:F3} W, not {lowWatts} W. The write did not take.");

        // --- Signal 2: is the part behaving as if the limit is in force? ---
        double dropped = a.ValueWatts - b.ValueWatts;
        double recovered = aPrime.ValueWatts - b.ValueWatts;
        bool pinnedLow = Near(b.ValueWatts, lowWatts, PinnedToleranceWatts);
        bool movedAndReturned = dropped > PinnedToleranceWatts && recovered > dropped * 0.5;

        Console.WriteLine($"SMU power value  : {a.ValueWatts:F2} -> {b.ValueWatts:F2} -> {aPrime.ValueWatts:F2} W" +
                          (c != null ? $" -> {c.Value.ValueWatts:F2} W" : ""));
        Console.WriteLine($"Windows counter  : {a.CounterWatts:F2} -> {b.CounterWatts:F2} -> {aPrime.CounterWatts:F2} W" +
                          (c != null ? $" -> {c.Value.CounterWatts:F2} W" : "")  + "   (direction only)");

        bool downConfirmed = wroteLow && pinnedLow && movedAndReturned;

        Console.WriteLine();
        if (!downConfirmed)
        {
            if (!pinnedLow)
                Console.WriteLine($"NOT CONFIRMED: the SMU reports {b.ValueWatts:F2} W drawn with a {lowWatts} W limit requested.");
            if (!movedAndReturned)
                Console.WriteLine("NOT CONFIRMED: power did not fall and then recover, so any change is as consistent");
            if (!wroteLow)
                Console.WriteLine("NOT CONFIRMED: the independent reader disagrees with what was written.");
            return 2;
        }

        Console.WriteLine($"CONFIRMED (down): the SMU held the part at the {lowWatts} W limit and released it again.");
        Console.WriteLine("                  The power limits are reaching the silicon.");

        if (c == null)
        {
            Console.WriteLine();
            Console.WriteLine("           Not tested: raising a limit above stock. Pass --high <watts> for that,");
            Console.WriteLine("           which is the direction that matters for lifting a clamp.");
            return 0;
        }

        // --- The upward direction, which is only answerable if stock was actually binding. ---
        bool boundAtStock = Near(a.ValueWatts, stockWatts, PinnedToleranceWatts);
        if (!boundAtStock)
        {
            Console.WriteLine();
            Console.WriteLine($"INCONCLUSIVE (up): at the stock {stockWatts} W limit the part drew only {a.ValueWatts:F2} W,");
            Console.WriteLine("                   so it was not power-bound and raising the limit had no headroom");
            Console.WriteLine("                   to show in. Needs a heavier load, not a different limit.");
            return 2;
        }

        var high = c.Value;
        bool wroteHigh = Near(high.LimitWatts, highWatts, 1.0);
        double gained = high.ValueWatts - aPrime.ValueWatts;
        bool gainedPower = gained > PinnedToleranceWatts;

        Console.WriteLine();
        Console.WriteLine($"Stock was binding: {a.ValueWatts:F2} W drawn against a {stockWatts} W limit.");
        Console.WriteLine($"High limit readback: {high.LimitWatts:F3} W");
        Console.WriteLine($"Power gained     : {gained:+0.00;-0.00;0.00} W above the stock ceiling");

        if (wroteHigh && gainedPower)
        {
            Console.WriteLine();
            Console.WriteLine($"CONFIRMED (up)  : raising the limit to {highWatts} W let the part draw {high.ValueWatts:F2} W,");
            Console.WriteLine($"                  past the {stockWatts} W the firmware had it pinned at.");
            return 0;
        }

        Console.WriteLine();
        if (!wroteHigh)
            Console.WriteLine($"NOT CONFIRMED (up): the {highWatts} W limit did not read back.");
        else
            Console.WriteLine($"NOT CONFIRMED (up): the limit was accepted and read back, but power stayed at " +
                              $"{high.ValueWatts:F2} W. Something other than this limit is binding.");
        return 2;
    }

    private static bool Near(double value, double target, double tolerance) =>
        Math.Abs(value - target) <= tolerance;

    private static Phased Phase(AmdUndervoltProvider provider, RyzenAdjReader? reader, string label, uint watts)
    {
        Console.WriteLine();
        Console.WriteLine($"--- phase {label} ---");

        var report = provider.ApplyPowerLimits(All(watts));
        Console.WriteLine($"  apply: {report}");

        Thread.Sleep(SettleMs);

        double counter = SampleCounterWatts(SampleMs);

        // Read the SMU last, so its VALUE reflects the same window the counter sampled.
        var smu = reader?.Read();
        if (smu != null)
            Console.WriteLine($"  SMU: limit {smu.StapmLimit:F3} W, drawing {smu.StapmValue:F2} W, " +
                              $"tdc {smu.TdcValue:F1}/{smu.TdcLimit:F0} A, thm {smu.ThmValue:F0}/{smu.ThmLimit:F0} C");
        Console.WriteLine($"  Windows counter: {counter:F2} W");

        return new Phased(
            LimitWatts: smu?.StapmLimit ?? 0,
            ValueWatts: smu?.StapmValue ?? 0,
            CounterWatts: counter,
            AllAccepted: report.AllAccepted);
    }

    private static RyzenPowerLimits All(uint watts) => new()
    {
        StapmLimit = watts * 1000,
        FastLimit = watts * 1000,
        SlowLimit = watts * 1000,
        ApuSlowLimit = watts * 1000
    };

    private readonly record struct Phased(double LimitWatts, double ValueWatts, double CounterWatts, bool AllAccepted)
    {
        public override string ToString() =>
            $"limit {LimitWatts,7:F3} W   drawing {ValueWatts,6:F2} W   counter {CounterWatts,6:F2} W" +
            (AllAccepted ? "" : "   [mailbox refused something]");
    }

    /// <summary>
    /// Mean APU package power over the sample window, in watts, from the Windows Energy Meter.
    /// A different domain from the SMU's PPT - see the header. Direction only.
    /// </summary>
    private static double SampleCounterWatts(int durationMs)
    {
        using var apu = TryCounter("Energy Meter", "Power", "Apu Power")
                     ?? TryCounter("Energy Meter", "Power", "CPU Power");
        if (apu == null)
        {
            Thread.Sleep(durationMs);
            return 0;
        }

        apu.NextValue();
        Thread.Sleep(1000);

        var samples = new List<double>();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            samples.Add(apu.NextValue() / 1000.0);
            Thread.Sleep(500);
        }

        return samples.Count > 0 ? samples.Average() : 0;
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string instance)
    {
        try
        {
            var c = new PerformanceCounter(category, counter, instance);
            c.NextValue();
            return c;
        }
        catch { return null; }
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

    private sealed class SmuSnapshot
    {
        public double StapmLimit, StapmValue;
        public double FastLimit, SlowLimit, ApuLimit;
        public double TdcLimit, TdcValue, EdcLimit, ThmLimit, ThmValue;
    }

    /// <summary>
    /// Reads the SMU power table by shelling out to an external ryzenadj.exe. READ ONLY - this
    /// type has no write path and never passes a --*-limit argument.
    /// </summary>
    private sealed class RyzenAdjReader
    {
        private readonly string _exe;
        private RyzenAdjReader(string exe) => _exe = exe;

        internal static RyzenAdjReader? TryCreate(string? path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new RyzenAdjReader(path) : null;

        internal SmuSnapshot? Read()
        {
            var rows = ReadRows();
            if (rows == null) return null;

            return new SmuSnapshot
            {
                StapmLimit = Get(rows, "STAPM LIMIT"),
                StapmValue = Get(rows, "STAPM VALUE"),
                FastLimit = Get(rows, "PPT LIMIT FAST"),
                SlowLimit = Get(rows, "PPT LIMIT SLOW"),
                ApuLimit = Get(rows, "PPT LIMIT APU"),
                TdcLimit = Get(rows, "TDC LIMIT VDD"),
                TdcValue = Get(rows, "TDC VALUE VDD"),
                EdcLimit = Get(rows, "EDC LIMIT VDD"),
                ThmLimit = Get(rows, "THM LIMIT CORE"),
                ThmValue = Get(rows, "THM VALUE CORE")
            };

            static double Get(Dictionary<string, double> r, string key) =>
                r.TryGetValue(key, out double v) ? v : 0;
        }

        /// <summary>Row label as ryzenadj prints it, to its value.</summary>
        private Dictionary<string, double>? ReadRows()
        {
            try
            {
                var psi = new ProcessStartInfo(_exe, "--info")
                {
                    // ryzenadj loads its own DLLs by bare name, so it has to run from its directory.
                    WorkingDirectory = Path.GetDirectoryName(_exe)!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);

                // | STAPM LIMIT | 45.000 | stapm-limit |   ->  cells 1 = label, 2 = value
                var rows = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in output.Split('\n'))
                {
                    var cells = line.Split('|', StringSplitOptions.TrimEntries);
                    if (cells.Length < 4 || cells[1].Length == 0) continue;
                    if (double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        rows[cells[1]] = v;
                }

                return rows.Count > 0 ? rows : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  (ryzenadj read failed: {ex.Message})");
                return null;
            }
        }
    }

    /// <summary>
    /// A load heavy enough to be power-bound.
    ///
    /// The scalar multiply loop used by --outcome is fine there - that test compares two clock
    /// readings under identical load - but a limit can only be shown to bind if the load would
    /// otherwise exceed it. Vector FMA chains with four independent accumulators keep the vector
    /// units fed without a dependency stall between iterations. The constants converge to a fixed
    /// point rather than running off to infinity: denormals and infinities have their own power
    /// behaviour and would make the load's draw drift over the run.
    /// </summary>
    private sealed class CpuLoad : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private static double _sink;

        public void Start()
        {
            for (int i = 0; i < Environment.ProcessorCount; i++)
                new Thread(Spin) { IsBackground = true, Priority = ThreadPriority.Highest }.Start();
        }

        private void Spin()
        {
            var a = new System.Numerics.Vector<double>(0.5);
            var b = new System.Numerics.Vector<double>(0.25);
            System.Numerics.Vector<double> x0 = a, x1 = b, x2 = a, x3 = b;

            while (!_cts.IsCancellationRequested)
            {
                for (int i = 0; i < 4096; i++)
                {
                    x0 = x0 * a + b;
                    x1 = x1 * b + a;
                    x2 = x2 * a + b;
                    x3 = x3 * b + a;
                }

                // Publish so the JIT cannot decide the whole loop is dead.
                _sink = x0[0] + x1[0] + x2[0] + x3[0];
            }
        }

        public void Stop() => _cts.Cancel();
        public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
    }
}
