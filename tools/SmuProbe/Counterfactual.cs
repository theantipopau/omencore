using OmenCore.Hardware;

namespace OmenCore.Tools.SmuProbe;

/// <summary>
/// Confirms a message-id diagnosis instead of assuming it: sends the same Curve Optimizer
/// offset through the id OmenCore used before the transport fix (PSMU 0x5D) and through the one
/// RyzenAdj specifies for Strix Point (MP1 0x4C), and prints both SMU status codes.
///
/// Result on board 8D87 / Ryzen AI 9 HX 375: BOTH return Ok. That falsified the theory that the
/// message id was why Curve Optimizer did not work, and is why <see cref="Outcome"/> exists -
/// a status code says nothing about effect.
///
/// Writes CO offsets, and resets to 0 before returning.
/// </summary>
internal static class Counterfactual
{
    internal static int Run()
    {
        Console.WriteLine("=== Curve Optimizer message-id counterfactual ===\n");

        RyzenControl.Init();
        using var smu = new RyzenSmu();
        if (!smu.Initialize())
        {
            Console.WriteLine($"FAIL: {smu.UnavailableReason}");
            return 1;
        }

        RyzenControl.ConfigureSmuAddresses(smu);
        Console.WriteLine($"CPU family : {RyzenControl.Family}\n");

        const int co = -10;
        uint encoded = Encode(co);
        Console.WriteLine($"All-Core CO {co} encodes to 0x{encoded:X}\n");

        Console.WriteLine("Pre-fix path:");
        Console.WriteLine($"  PSMU 0x5D -> {SendPsmu(smu, 0x5D, encoded)}");
        Console.WriteLine($"  MP1  0x5D -> {SendMp1(smu, 0x5D, encoded)}   (the old fallback)");

        Console.WriteLine("\nPost-fix path:");
        Console.WriteLine($"  MP1  0x4C -> {SendMp1(smu, 0x4C, encoded)}");

        Console.WriteLine("\nResetting All-Core CO to 0 ...");
        Console.WriteLine($"  MP1  0x4C -> {SendMp1(smu, 0x4C, 0)}");

        Console.WriteLine("\nIf more than one id returns Ok, the id is not discriminating -");
        Console.WriteLine("measure the effect with --outcome before concluding anything.");
        return 0;
    }

    /// <summary>Matches AmdUndervoltProvider's signed-offset encoding.</summary>
    internal static uint Encode(int co) =>
        co < 0 ? (uint)(0x100000 - (uint)(-co)) : (uint)co;

    private static RyzenSmu.SmuStatus SendMp1(RyzenSmu smu, uint message, uint arg0)
    {
        uint[] args = new uint[6];
        args[0] = arg0;
        return smu.SendMp1(message, ref args);
    }

    private static RyzenSmu.SmuStatus SendPsmu(RyzenSmu smu, uint message, uint arg0)
    {
        uint[] args = new uint[6];
        args[0] = arg0;
        return smu.SendPsmu(message, ref args);
    }
}
