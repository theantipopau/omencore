// Verifies the iGPU (graphics) Curve Optimizer by OUTCOME, on a part where the upstream
// implementations disagree about whether the message exists at all.
//
// THE QUESTION. RyzenAdj v0.19.0 set_cogfx enumerates families deliberately: Renoir/Cezanne get
// MP1 0x64, Rembrandt/Phoenix/HawkPoint/VanGogh get PSMU 0xB7, Strix Halo gets an explicit
// "0xB7 is rejected on this architecture", and Strix Point gets no case at all - so ryzenadj
// returns ADJ_ERR_FAM_UNSUPPORTED here. UXTU disagrees: its socket table for FT6/FP7/FP8, which
// Strix Point routes into, contains ("set-cogfx", false, 0xb7) and would happily send it.
//
// Both cannot be right, and OmenCore has to pick one to ship. A status code cannot settle it -
// the counterfactual already showed this SMU answers Ok to ids that do nothing. So this measures.
//
// THE OBSERVABLE. A graphics Curve Optimizer undervolt shifts the GFX V/F curve down. Under a
// sustained iGPU load that is power-bound rather than utilisation-bound, the freed budget is
// spent on clock. So: same load, same power ceiling, HIGHER sustained GFX clock - and/or LOWER
// GFX voltage at a comparable clock. Both are read here, because which one moves depends on
// whether the operating point is against the power ceiling or against the curve.
//
// THE INSTRUMENT is deliberately not ours. GFX clock and voltage are read out of the SMU power
// metrics table by an external ryzenadj.exe over its own driver, so a value that moves has
// crossed two unrelated transports. READ ONLY - ryzenadj is never asked to change anything.
// This repo does not use WinRing0 and must not start; invoking someone else's copy as a reader
// is the one exception, same as --limits.
//
// THE OFFSETS ARE MEASURED HERE, NOT TAKEN FROM RyzenAdj. lib/api.c maps table 0x005D000B as
// 0x4B4 gfx power, 0x4B8 volt, 0x4BC temp, 0x4C0 clk. On board 8D87 (BIOS F.07, SMU BIOS
// interface 22) that block is shifted by one field. Measured idle vs a saturating WebGL load:
//
//   0x4B0   1.33 ->  35.07   gfx power (W)
//   0x4B4   0.33 ->   0.86   gfx voltage (V)
//   0x4B8  48.06 ->  70.35   gfx temperature (C)
//   0x4BC 791.68 -> 2341.99  gfx clock (MHz)
//   0x4C0 434.23 -> 2386.76  gfx clock (MHz)
//   0x4C4  65.08 -> 2355.47  gfx clock (MHz)
//   0x4C8   2.75 ->  97.47   gfx busy (%)
//   0x4CC  94.89 ->   2.06   gfx idle (%) - complement of 0x4C8, which is what pins the pair
//
// So RyzenAdj's "gfx power" is this board's voltage and its "volt" is this board's temperature.
// Its clock offset still lands on a clock only because three adjacent fields are clocks, which
// is exactly the kind of coincidence that keeps a wrong map looking right. Reading 0x4B4 as
// watts here would have produced a plausible 0.86 W and a silently meaningless comparison.
//
// GUARDS, each of which exists because this project has been fooled by its absence before:
//   - the load must be up in BOTH phases, or the comparison is between two idle states
//   - phases alternate and pair, so thermal drift cancels instead of being read as an effect
//   - --sham runs the identical protocol writing 0 in both phases, to establish the noise floor
//   - the offset is reset to 0 in a finally
//
// Writes iGPU CO offsets.

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OmenCore.Hardware;

namespace OmenCore.Tools.SmuProbe;

internal static class Igpu
{
    /// <summary>Settle time after each CO write before sampling.</summary>
    private const int SettleMs = 6000;

    private const int SampleMs = 8000;

    /// <summary>Time under load before the first pair, so the iGPU has reached a steady state.</summary>
    private const int WarmupMs = 40000;

    private const int Cycles = 3;

    /// <summary>
    /// Below this the iGPU is not doing enough work for its clock to be evidence of anything.
    /// Busy percent is the guard rather than watts, because it is the one field whose scale is
    /// self-evident: 0x4C8 and 0x4CC are complements that sum to ~100, which is what identified
    /// them. Idle here reads ~3%, the WebGL load holds ~97%.
    /// </summary>
    private const double MinLoadedBusyPct = 50.0;

    /// <summary>Noise floor, in percent. Established by --sham, not assumed.</summary>
    private const double MinMeaningfulPct = 1.0;

    internal static int Run(string[] args)
    {
        int offset = -20;
        int idx = Array.IndexOf(args, "--offset");
        if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsed))
            offset = parsed;

        bool sham = args.Contains("--sham");
        bool useMp1 = args.Contains("--mp1");
        string ryzenAdj = ArgString(args, "--readback") ?? @"D:\Apps\RyzenAdj\ryzenadj.exe";

        // Clamp for the same reason AmdUndervoltProvider clamps: a graphics CO count is roughly
        // 4 mV, and beyond about 30 counts an unstable iGPU takes the display with it.
        offset = Math.Clamp(offset, -30, 30);

        if (args.Contains("--scan"))
            return Scan();

        if (args.Contains("--prereq"))
            return Prereq();

        if (args.Contains("--oracle"))
            return Oracle();

        uint message = 0xB7;
        string path = useMp1 ? "MP1 0xB7" : "PSMU 0xB7";

        Console.WriteLine("=== iGPU Curve Optimizer outcome verification ===");
        Console.WriteLine($"Path under test : {path}   {(useMp1 ? "(mirror of UXTU's id on the other mailbox)" : "(UXTU's set-cogfx for this socket group)")}");
        Console.WriteLine(sham
            ? "Mode            : SHAM CONTROL - writes 0 in both phases, measures the noise floor"
            : $"Mode            : offset {offset} counts (~{offset * 4} mV) against 0");
        Console.WriteLine();

        if (!File.Exists(ryzenAdj))
        {
            Console.WriteLine($"FAIL: no ryzenadj.exe at {ryzenAdj}. Pass --readback <path>.");
            Console.WriteLine("      There is no other GFX clock/voltage readout on this path, so");
            Console.WriteLine("      without it this mode can only report status codes, which prove nothing.");
            return 1;
        }
        var reader = new TableReader(ryzenAdj);

        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);
        Console.WriteLine($"CPU family      : {RyzenControl.Family}");

        var probe = reader.Read();
        if (probe == null)
        {
            Console.WriteLine("FAIL: could not read the power metrics table.");
            return 1;
        }
        Console.WriteLine($"Table probe     : {probe.Value}\n");

        if (probe.Value.Busy < MinLoadedBusyPct)
        {
            Console.WriteLine($"FAIL: the iGPU is idle ({probe.Value.Busy:F0}% busy < {MinLoadedBusyPct:F0}%).");
            Console.WriteLine("      Start a sustained iGPU load FIRST and leave it running, then re-run.");
            Console.WriteLine("      An undervolt has no observable effect on a parked graphics block, so");
            Console.WriteLine("      measuring now would produce a confident null that means nothing.");
            return 1;
        }

        try
        {
            Console.WriteLine($"Warming up {WarmupMs / 1000}s under the existing load ...");
            SetIgpuCo(smu, 0, message, useMp1);
            Thread.Sleep(WarmupMs);

            var clockDeltas = new List<double>();
            var voltDeltas = new List<double>();
            var baselines = new List<Reading>();
            var offsets = new List<Reading>();

            for (int cycle = 1; cycle <= Cycles; cycle++)
            {
                SetIgpuCo(smu, 0, message, useMp1);
                Thread.Sleep(SettleMs);
                var a = Sample(reader, SampleMs);

                SetIgpuCo(smu, sham ? 0 : offset, message, useMp1);
                Thread.Sleep(SettleMs);
                var b = Sample(reader, SampleMs);

                if (a.Busy < MinLoadedBusyPct || b.Busy < MinLoadedBusyPct)
                {
                    Console.WriteLine($"INVALID: the iGPU load did not stay up ({a.Busy:F0}% -> {b.Busy:F0}% busy).");
                    return 1;
                }

                double clkPct = a.Clock > 0 ? (b.Clock - a.Clock) / a.Clock * 100.0 : 0;
                double vPct = a.Volts > 0 ? (b.Volts - a.Volts) / a.Volts * 100.0 : 0;
                clockDeltas.Add(clkPct);
                voltDeltas.Add(vPct);
                baselines.Add(a);
                offsets.Add(b);

                Console.WriteLine($"  cycle {cycle}: CO 0 {a}  ->  CO {(sham ? 0 : offset)} {b}");
                Console.WriteLine($"           clock {clkPct,6:+0.0;-0.0;0.0}%   volt-field {vPct,6:+0.0;-0.0;0.0}%");
            }

            double clkMean = clockDeltas.Average();
            double vMean = voltDeltas.Average();
            bool clkConsistent = clockDeltas.All(d => d > 0) || clockDeltas.All(d => d < 0);
            bool vConsistent = voltDeltas.All(d => d > 0) || voltDeltas.All(d => d < 0);

            Console.WriteLine();
            Console.WriteLine($"Paired clock delta : mean {clkMean:+0.0;-0.0;0.0}%  (min {clockDeltas.Min():+0.0;-0.0;0.0}%, max {clockDeltas.Max():+0.0;-0.0;0.0}%, n={clockDeltas.Count}, sign consistent: {clkConsistent})");
            Console.WriteLine($"Paired volt delta  : mean {vMean:+0.0;-0.0;0.0}%  (min {voltDeltas.Min():+0.0;-0.0;0.0}%, max {voltDeltas.Max():+0.0;-0.0;0.0}%, n={voltDeltas.Count}, sign consistent: {vConsistent})");
            Console.WriteLine($"Mean gfx power     : CO 0 {baselines.Average(r => r.Watts):F2} W  ->  {offsets.Average(r => r.Watts):F2} W");

            if (sham)
            {
                Console.WriteLine("\nSHAM CONTROL COMPLETE. Both phases wrote 0, so every number above is noise.");
                Console.WriteLine($"  Treat |mean| >= {Math.Max(Math.Abs(clkMean), Math.Abs(vMean)):F1}% as the floor a real run must clear.");
                return 0;
            }

            bool clockEffect = clkConsistent && clkMean >= MinMeaningfulPct;
            bool voltEffect = vConsistent && vMean <= -MinMeaningfulPct;

            if (clockEffect || voltEffect)
            {
                Console.WriteLine($"\nCONFIRMED: {path} moved the graphics operating point.");
                Console.WriteLine("           Re-run with --sham before believing this - a result that");
                Console.WriteLine("           the sham control also reproduces is drift, not an effect.");
                return 0;
            }

            Console.WriteLine($"\nNOT CONFIRMED: {path} changed nothing measurable on this part.");
            Console.WriteLine("  The SMU may well have answered Ok. That is not evidence, and this is why:");
            Console.WriteLine("  no clock gain, no voltage drop, across every pair. On this reading RyzenAdj");
            Console.WriteLine("  is right to withhold set_cogfx from Strix Point and UXTU's table entry is");
            Console.WriteLine("  inherited from its socket group rather than verified on it.");
            return 2;
        }
        finally
        {
            Console.WriteLine("\nResetting iGPU CO to 0 ...");
            SetIgpuCo(smu, 0, message, useMp1);
        }
    }

    /// <summary>
    /// Sends the graphics CO id on both mailboxes and prints the status. A rejection here IS
    /// evidence, which is the asymmetry that makes this worth running: Ok proves nothing (the
    /// counterfactual established that this SMU answers Ok to ids that do nothing), but a
    /// refusal means the firmware was asked and declined.
    ///
    /// Scoped to 0xB7 deliberately. It is the only id either upstream associates with graphics
    /// CO - RyzenAdj gives it to Rembrandt/Phoenix/HawkPoint/VanGogh, UXTU puts it in the socket
    /// table Strix Point routes into. RyzenAdj's other set_cogfx id, MP1 0x64, is mapped only to
    /// Renoir and Cezanne, parts three generations older; on a 2024 MP1 enum that number is
    /// some other command, and sending it a CO-encoded argument would be firing a made-up
    /// message at the SMU to test a hypothesis nothing supports. Both mailboxes are tried because
    /// OmenCore has just been wrong about which mailbox a family uses.
    /// </summary>
    private static int Scan()
    {
        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);

        Console.WriteLine("=== graphics Curve Optimizer id scan ===");
        Console.WriteLine($"CPU family : {RyzenControl.Family}\n");

        foreach (int co in new[] { -20, 0 })
        {
            Console.WriteLine($"argument: CO {co} (0x{Counterfactual.Encode(co):X})");
            SetIgpuCo(smu, co, 0xB7, useMp1: false);
            SetIgpuCo(smu, co, 0xB7, useMp1: true);
            Console.WriteLine();
        }

        Console.WriteLine("A status of Ok would NOT establish that the offset lands - re-run");
        Console.WriteLine("without --scan to measure the outcome. A refusal is conclusive.");
        return 0;
    }

    /// <summary>
    /// Chases the precondition behind PSMU 0xB7's CmdRejectedPrereq.
    ///
    /// That status is the whole reason this mode exists. UnknownCmd would have closed the
    /// question - the firmware does not have the message. CmdRejectedPrereq says the opposite:
    /// the PSMU knows message 0xB7 and declined it, identically for CO -20 and CO 0, so the
    /// refusal is about machine state and not about the argument.
    ///
    /// The candidate state is OC mode. UXTU's socket table carries ("enable-oc", false, 0x17)
    /// and ("disable-oc", false, 0x18) on the same PSMU mailbox as set-cogfx, and RyzenAdj's
    /// set_enable_oc sends PSMU 0x17 with argument 0 on Rembrandt - the same part it gives
    /// PSMU 0xB7 to. So on the one family where upstream grants graphics CO, both messages live
    /// together on the same mailbox.
    ///
    /// The reversal is the point. Enabling and re-testing could produce a coincidence; enabling,
    /// re-testing, disabling and re-testing AGAIN turns it into a controlled comparison. OC mode
    /// is restored to off in a finally either way, and it is volatile - a power cycle clears it.
    /// </summary>
    private static int Prereq()
    {
        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);

        Console.WriteLine("=== graphics CO precondition test ===");
        Console.WriteLine($"CPU family : {RyzenControl.Family}\n");

        Console.WriteLine("Query messages first (these read; they change nothing):");
        Query(smu, "get-coper-options       ", 0xE1);
        Query(smu, "get-pbo-scalar          ", 0x0F);
        Query(smu, "get-pbo-fused-tctl-temp ", 0xE5);

        try
        {
            Console.WriteLine("\nBaseline, OC mode off:");
            SetIgpuCo(smu, -20, 0xB7, useMp1: false);

            Console.WriteLine("\nEnabling OC mode (PSMU 0x17, argument 0):");
            Send(smu, 0x17, 0, "enable-oc ");

            Console.WriteLine("\nRetrying graphics CO with OC mode on:");
            SetIgpuCo(smu, -20, 0xB7, useMp1: false);
        }
        finally
        {
            Console.WriteLine("\nRestoring: graphics CO to 0, then OC mode off (PSMU 0x18):");
            SetIgpuCo(smu, 0, 0xB7, useMp1: false);
            Send(smu, 0x18, 0, "disable-oc");

            Console.WriteLine("\nControl - graphics CO with OC mode off again:");
            SetIgpuCo(smu, -20, 0xB7, useMp1: false);
            SetIgpuCo(smu, 0, 0xB7, useMp1: false);
        }

        Console.WriteLine("\nRead it as a pair. If 0xB7 answered Ok only in the middle, OC mode is");
        Console.WriteLine("the precondition - and the offset still has to be measured, not believed.");
        return 0;
    }

    /// <summary>
    /// Establishes whether the PawnIO path has a readback at all, by watching one move.
    ///
    /// get-pbo-scalar (PSMU 0x0F) answered Ok with 0x3F800000 in args[0], which is 1.0f and is
    /// exactly the default PBO scalar. That is the most seductive shape a wrong reading can
    /// take: a plausible number, in the right units, from a message that returned Ok. This
    /// project has a rule about it - a buffer is not state until you have watched it change
    /// with a change you caused.
    ///
    /// So: set the scalar to 2x, read it, set it back to 1x, read it again. UXTU sends this
    /// value in hundredths (--pbo-scalar={value * 100}), so 2x is 200. If the readback tracks,
    /// there is a real oracle here, and the standing claim that this transport is write-only
    /// is wrong. If it reads 1.0f throughout, args[0] is a stale buffer and nothing more.
    ///
    /// PBO scalar is volatile and restored to 1x in a finally.
    /// </summary>
    private static int Oracle()
    {
        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }
        RyzenControl.ConfigureSmuAddresses(smu);

        Console.WriteLine("=== readback oracle test: does args[] carry a reply? ===");
        Console.WriteLine($"CPU family : {RyzenControl.Family}\n");

        try
        {
            ReadScalar(smu, "before        ");
            Console.WriteLine("\nSetting PBO scalar to 2x (PSMU 0x3E, value 200):");
            Send(smu, 0x3E, 200, "pbo-scalar");
            ReadScalar(smu, "after 2x      ");

            Console.WriteLine("\nSetting PBO scalar back to 1x (value 100):");
            Send(smu, 0x3E, 100, "pbo-scalar");
            ReadScalar(smu, "after 1x      ");
        }
        finally
        {
            Send(smu, 0x3E, 100, "pbo-scalar");
        }

        Console.WriteLine("\nIf the middle read differs from the outer two, args[0] is a real reply");
        Console.WriteLine("and this transport has a readback. If all three agree, it is a stale buffer.");
        return 0;
    }

    private static void ReadScalar(RyzenSmu smu, string label)
    {
        uint[] args = new uint[6];
        var status = smu.SendPsmu(0x0F, ref args);
        float asFloat = BitConverter.Int32BitsToSingle((int)args[0]);
        Console.WriteLine($"  get-pbo-scalar {label} = {status,-18} args[0] = 0x{args[0]:X8}  ({asFloat:F3}x)");
    }

    private static void Query(RyzenSmu smu, string label, uint message)
    {
        uint[] args = new uint[6];
        var status = smu.SendPsmu(message, ref args);
        Console.WriteLine($"  PSMU 0x{message:X2} {label} = {status,-18} args: " +
                          string.Join(" ", args.Select(a => $"0x{a:X8}")));
    }

    private static void Send(RyzenSmu smu, uint message, uint arg0, string label)
    {
        uint[] args = new uint[6];
        args[0] = arg0;
        var status = smu.SendPsmu(message, ref args);
        Console.WriteLine($"  PSMU 0x{message:X2} {label} = {status}");
    }

    private static void SetIgpuCo(RyzenSmu smu, int co, uint message, bool useMp1)
    {
        uint[] args = new uint[6];
        args[0] = Counterfactual.Encode(co);
        var status = useMp1 ? smu.SendMp1(message, ref args) : smu.SendPsmu(message, ref args);
        Console.WriteLine($"  SetIgpuCo({co}) -> {(useMp1 ? "MP1" : "PSMU")} 0x{message:X2} = {status}");
    }

    private readonly record struct Reading(double Watts, double Volts, double Clock, double Busy)
    {
        public override string ToString() =>
            $"{Clock:F0} MHz, {Volts:F4} V, {Watts:F2} W, {Busy:F0}% busy";
    }

    private static Reading Sample(TableReader reader, int durationMs)
    {
        var w = new List<double>();
        var v = new List<double>();
        var c = new List<double>();
        var b = new List<double>();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            var r = reader.Read();
            if (r != null)
            {
                w.Add(r.Value.Watts); v.Add(r.Value.Volts);
                c.Add(r.Value.Clock); b.Add(r.Value.Busy);
            }
            Thread.Sleep(250);
        }
        return new Reading(
            w.Count > 0 ? w.Average() : 0,
            v.Count > 0 ? v.Average() : 0,
            c.Count > 0 ? c.Average() : 0,
            b.Count > 0 ? b.Average() : 0);
    }

    private static string? ArgString(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Reads the GFX block of the SMU power metrics table by shelling out to ryzenadj
    /// --dump-table. READ ONLY - no ryzenadj setting is ever passed.
    /// </summary>
    private sealed class TableReader
    {
        private const string GfxPower = "0x04B0";
        private const string GfxVolt = "0x04B4";
        private const string GfxClock = "0x04C0";
        private const string GfxBusy = "0x04C8";

        private static readonly Regex Row =
            new(@"^\|\s(0x[0-9A-F]{4})\s\|\s0x[0-9A-F]{8}\s\|\s*(-?[0-9.]+)\s", RegexOptions.Compiled);

        private readonly string _exe;

        internal TableReader(string exe) => _exe = exe;

        internal Reading? Read()
        {
            try
            {
                var psi = new ProcessStartInfo(_exe, "--dump-table")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // ryzenadj loads its own DLLs by bare name, so it has to run from its directory.
                    WorkingDirectory = Path.GetDirectoryName(_exe) ?? ".",
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);

                double? watts = null, volts = null, clock = null, busy = null;
                foreach (string line in stdout.Split('\n'))
                {
                    var m = Row.Match(line);
                    if (!m.Success) continue;
                    double val = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (m.Groups[1].Value)
                    {
                        case GfxPower: watts = val; break;
                        case GfxVolt: volts = val; break;
                        case GfxClock: clock = val; break;
                        case GfxBusy: busy = val; break;
                    }
                }
                if (watts == null || volts == null || clock == null || busy == null) return null;
                return new Reading(watts.Value, volts.Value, clock.Value, busy.Value);
            }
            catch
            {
                return null;
            }
        }
    }
}
