// Verifies the Curve Optimizer by OUTCOME, not by SMU status code.
//
// The counterfactual showed the SMU answers Ok to several message ids, so "Ok" proves nothing
// about whether voltage actually moved. A real All-Core CO undervolt reduces core voltage at a
// given frequency; under an all-core power-limited load the boost algorithm spends the freed
// power budget on clock. So the observable is: same load, same power limit, HIGHER effective
// clock after the offset is applied.
//
// Guards against the two false-positive shapes this project has hit before:
//   - the load must still be running in both phases (core power must not collapse)
//   - both phases are measured after a settle delay, so STAPM decay is not read as an effect
//
// Writes CO offsets, and resets to 0 in a finally.

using System.Diagnostics;
using OmenCore.Hardware;

namespace OmenCore.Tools.SmuProbe;

internal static class Outcome
{
    /// <summary>Time under load before the first pair, so STAPM decay has already happened.</summary>
    private const int WarmupMs = 45000;

    /// <summary>Settle time after each CO write before sampling.</summary>
    private const int SettleMs = 6000;

    private const int SampleMs = 5000;

    /// <summary>Number of alternating baseline/offset pairs.</summary>
    private const int Cycles = 3;

    /// <summary>
    /// Noise floor. A sham control (CO 0 in both phases) from a warmed steady state reproduces
    /// 0.0%, so anything at or above this with a consistent sign across every pair is an effect
    /// rather than drift.
    /// </summary>
    private const double MinMeaningfulPct = 1.0;

    internal static int Run(string[] args)
    {
        int offset = -25;
        int idx = Array.IndexOf(args, "--offset");
        if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsed))
            offset = parsed;

        // Which mailbox/message to test. Defaults to the post-fix MP1 0x4C; --psmu5d selects
        // the pre-fix path so its *effect* can be compared, not just its status code.
        bool usePreFixPath = args.Contains("--psmu5d");

        Console.WriteLine("=== Curve Optimizer outcome verification ===");
        Console.WriteLine($"Comparing All-Core CO 0 against CO {offset} under a sustained all-core load.\n");

        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);

        uint message = usePreFixPath ? 0x5Du : 0x4Cu;
        Console.WriteLine($"Path under test : {(usePreFixPath ? "PSMU" : "MP1")} 0x{message:X2}" +
                          $"{(usePreFixPath ? "  (pre-fix)" : "  (post-fix)")}\n");

        using var load = new CpuLoad();
        try
        {
            Console.WriteLine($"Starting all-core load on {Environment.ProcessorCount} threads ...");
            load.Start();

            // A single A-then-B comparison is not sound here. A first attempt at this measured
            // +4.8% on one run and -5.3% on another for the SAME offset, because the machine's
            // thermal/boost state at the start of a run dominates the difference: a run started
            // hot decays downward across both phases, a run started cool climbs. A sham control
            // (CO 0 in both phases) from a cool start reproduces 0.0%, which shows the sampling
            // itself is precise and the confound is purely the drift between phases.
            //
            // So alternate 0 / offset several times and pair each offset phase against the
            // baseline phase immediately before it. Monotonic drift then affects both members of
            // every pair almost equally and cancels in the mean, and the spread across pairs is
            // an honest error bar instead of an assumption.
            Console.WriteLine($"Warming up {WarmupMs / 1000}s to reach a steady thermal state ...");
            SetCo(smu, 0, message, usePreFixPath);
            Thread.Sleep(WarmupMs);

            var deltas = new List<double>();
            var baselines = new List<Reading>();
            var offsets = new List<Reading>();

            for (int cycle = 1; cycle <= Cycles; cycle++)
            {
                SetCo(smu, 0, message, usePreFixPath);
                Thread.Sleep(SettleMs);
                var a = Sample(SampleMs);

                SetCo(smu, offset, message, usePreFixPath);
                Thread.Sleep(SettleMs);
                var b = Sample(SampleMs);

                if (b.CoreWatts < a.CoreWatts * 0.5)
                {
                    Console.WriteLine("INVALID: core power collapsed - the load did not stay up.");
                    return 1;
                }

                double pct = a.EffectiveMhz > 0
                    ? (b.EffectiveMhz - a.EffectiveMhz) / a.EffectiveMhz * 100.0
                    : 0;

                baselines.Add(a);
                offsets.Add(b);
                deltas.Add(pct);

                Console.WriteLine($"  cycle {cycle}: CO 0 {a}  ->  CO {offset} {b}   ({pct:+0.0;-0.0;0.0}%)");
            }

            double mean = deltas.Average();
            double min = deltas.Min();
            double max = deltas.Max();
            bool consistentSign = deltas.All(d => d > 0) || deltas.All(d => d < 0);

            Console.WriteLine();
            Console.WriteLine($"Paired clock delta : mean {mean:+0.0;-0.0;0.0}%  (min {min:+0.0;-0.0;0.0}%, max {max:+0.0;-0.0;0.0}%, n={deltas.Count})");
            Console.WriteLine($"Mean core power    : CO 0 {baselines.Average(r => r.CoreWatts):F2} W" +
                              $"  ->  CO {offset} {offsets.Average(r => r.CoreWatts):F2} W");

            // Require BOTH a mean above the noise floor the sham control established AND the
            // same sign in every pair. One large pair cannot carry the result on its own.
            if (!consistentSign)
            {
                Console.WriteLine("\nNOT CONFIRMED: the sign is not consistent across pairs, so this is drift, not an effect.");
                return 2;
            }

            if (mean >= MinMeaningfulPct)
            {
                Console.WriteLine($"\nCONFIRMED: All-Core CO {offset} raised sustained clock in every pair.");
                Console.WriteLine("           The offset is reaching the silicon.");
                return 0;
            }

            if (mean <= -MinMeaningfulPct)
            {
                Console.WriteLine("\nUNEXPECTED: clock fell consistently. Something was written, but this is not");
                Console.WriteLine("            a working undervolt. Do not ship on this evidence.");
                return 1;
            }

            Console.WriteLine("\nNOT CONFIRMED: no effect above the noise floor.");
            Console.WriteLine("  The SMU accepted the command but nothing measurable changed, so this does");
            Console.WriteLine("  NOT establish that Curve Optimizer reaches the silicon on this part.");
            return 2;
        }
        finally
        {
            Console.WriteLine("\nResetting All-Core CO to 0 ...");
            SetCo(smu, 0, message, usePreFixPath);
            load.Stop();
        }
    }

    private static void SetCo(RyzenSmu smu, int co, uint message, bool usePsmu)
    {
        uint[] args = new uint[6];
        args[0] = Counterfactual.Encode(co);
        var status = usePsmu
            ? smu.SendPsmu(message, ref args)
            : smu.SendMp1(message, ref args);
        Console.WriteLine($"  SetCo({co}) -> 0x{message:X2} = {status}");
    }

    private readonly record struct Reading(double EffectiveMhz, double CoreWatts)
    {
        public override string ToString() => $"{EffectiveMhz:F0} MHz, core {CoreWatts:F2} W";
    }

    private static Reading Sample(int durationMs)
    {
        // "% Processor Performance" is relative to nominal; scaled by the nominal clock it gives
        // an effective-frequency estimate that tracks boost behaviour.
        using var perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
        using var corePower = TryCounter("Energy Meter", "Power", "CPU Power");

        perf.NextValue();
        corePower?.NextValue();
        Thread.Sleep(1000);

        double nominalMhz = NominalMhz();
        var perfSamples = new List<double>();
        var powerSamples = new List<double>();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            perfSamples.Add(perf.NextValue() / 100.0 * nominalMhz);
            if (corePower != null) powerSamples.Add(corePower.NextValue() / 1000.0);
            Thread.Sleep(500);
        }

        return new Reading(
            perfSamples.Count > 0 ? perfSamples.Average() : 0,
            powerSamples.Count > 0 ? powerSamples.Average() : 0);
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string instance)
    {
        try { return new PerformanceCounter(category, counter, instance); }
        catch { return null; }
    }

    private static double NominalMhz()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "select MaxClockSpeed from Win32_Processor");
            foreach (System.Management.ManagementObject o in searcher.Get())
                return Convert.ToDouble(o["MaxClockSpeed"]);
        }
        catch { }
        return 2000;
    }

    private sealed class CpuLoad : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Thread> _threads = new();

        public void Start()
        {
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                var t = new Thread(() =>
                {
                    double x = 1.0000001;
                    while (!_cts.IsCancellationRequested) { x = x * x; if (x > 1e300) x = 1.0000001; }
                }) { IsBackground = true, Priority = ThreadPriority.Normal };
                t.Start();
                _threads.Add(t);
            }
        }

        public void Stop() => _cts.Cancel();
        public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
    }
}
