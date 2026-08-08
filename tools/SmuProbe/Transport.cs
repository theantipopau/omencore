using System.Threading;
using OmenCore.Hardware;

namespace OmenCore.Tools.SmuProbe;

/// <summary>
/// Verifies the AMD SMU transport through OmenCore's own <see cref="RyzenSmu"/> and
/// <see cref="RyzenControl"/>, and optionally applies a Curve Optimizer offset via
/// <see cref="AmdUndervoltProvider"/>.
///
/// Read-only unless <c>--co</c> is passed.
/// </summary>
internal static class Transport
{
    internal static int Run(string[] args)
    {
        Console.WriteLine("=== OmenCore AMD SMU transport probe ===\n");

        RyzenControl.Init();
        Console.WriteLine($"CPU               : {RyzenControl.CpuName.Trim()}");
        Console.WriteLine($"Caption           : {RyzenControl.CpuModel}");
        Console.WriteLine($"Detected family   : {RyzenControl.Family}");
        Console.WriteLine($"SupportsUndervolt : {RyzenControl.SupportsUndervolt()}");
        Console.WriteLine($"AI 9 guard trips  : {RyzenControl.IsRyzenAi9CurveOptimizerUnsupported()}");

        using var smu = new RyzenSmu();
        Console.WriteLine();
        Console.WriteLine($"Initialize()            : {smu.Initialize()}");
        Console.WriteLine($"IsAvailable             : {smu.IsAvailable}");
        Console.WriteLine($"Cross-process PCI lock  : {RyzenSmu.HasCrossProcessPciLock}");

        if (!smu.IsAvailable)
        {
            Console.WriteLine($"UnavailableReason       : {smu.UnavailableReason}");
            Console.WriteLine("\nFAIL: transport unavailable.");
            return 1;
        }

        // A successful version read means the mailbox handshake completed end to end: the
        // module drove the PCI-config register pair, the SMU answered, and the response
        // register cleared. It is liveness evidence, not a capability claim.
        Console.WriteLine(smu.TryGetSmuVersion(out uint version)
            ? $"SMU version             : 0x{version:X8}"
            : "SMU version             : not readable");

        RyzenControl.ConfigureSmuAddresses(smu);
        Console.WriteLine($"MP1  mailbox            : msg=0x{smu.Mp1AddrMsg:X} rsp=0x{smu.Mp1AddrRsp:X} arg=0x{smu.Mp1AddrArg:X}");
        Console.WriteLine($"PSMU mailbox            : msg=0x{smu.PsmuAddrMsg:X} rsp=0x{smu.PsmuAddrRsp:X} arg=0x{smu.PsmuAddrArg:X}");

        int coIndex = Array.IndexOf(args, "--co");
        if (coIndex < 0 || coIndex + 1 >= args.Length)
        {
            Console.WriteLine("\nRead-only probe complete. Nothing was written.");
            Console.WriteLine("Pass --co <offset> to apply an All-Core CO offset, or --outcome to measure one.");
            return 0;
        }

        if (!int.TryParse(args[coIndex + 1], out int co))
        {
            Console.WriteLine("\nFAIL: --co requires an integer offset.");
            return 1;
        }

        Console.WriteLine($"\n--- Applying All-Core Curve Optimizer offset {co} ---");
        using var provider = new AmdUndervoltProvider();
        Console.WriteLine($"ActiveBackend : {provider.ActiveBackend}");
        Console.WriteLine($"IsSupported   : {provider.IsSupported}");

        try
        {
            provider.ApplyRyzenOffsetAsync(co, 0, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"\nRESULT: the SMU accepted All-Core CO = {co}.");
            Console.WriteLine("        An accepted command is NOT proof of a voltage change - the SMU");
            Console.WriteLine("        answers Ok to ids that do nothing. Use --outcome to measure it.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nRESULT: rejected - {ex.Message}");
            return 1;
        }
    }
}
