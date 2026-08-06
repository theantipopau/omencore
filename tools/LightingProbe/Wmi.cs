// Exercises OmenCore's own keyboard-lighting detection against the BIOS, so a mistake in the
// shipping code shows up here rather than in a user's log.
//
// Three commands are involved and it is easy to conflate them:
//
//   Default  0x2B  lighting topology   -> NbKeyboardLightingType. HP's own gate.
//   Keyboard 0x01  lighting supported  -> bit 0 of byte 0. NOT a keyboard-type getter,
//                                         despite being widely described as one.
//   Keyboard 0x06  LED animation       -> wants a 128-byte reply; a 4-byte request is
//                                         refused with RTCD 5 (wrong buffer size).

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class Wmi
{
    internal static int Run()
    {
        Console.WriteLine("=== BIOS keyboard lighting ===\n");

        using var bios = new HpWmiBios();
        if (!bios.IsAvailable)
        {
            Console.WriteLine("FAIL: HP WMI BIOS unavailable. Run elevated on an HP OMEN/Victus machine.");
            return 1;
        }

        var topology = bios.GetKeyboardLightingType();
        var supported = bios.IsKeyboardLightingSupported();
        var kbdType = bios.GetKeyboardType();
        bool backlight = bios.HasBacklight();

        Console.WriteLine($"  Lighting topology (Default 0x2B) : {Describe(topology)}");
        Console.WriteLine($"  Lighting supported (Kbd 0x01 b0) : {Describe(supported)}");
        Console.WriteLine($"  Keyboard type (mapped)           : {Describe(kbdType)}");
        Console.WriteLine($"  HasBacklight()                   : {backlight}");

        Console.WriteLine();
        switch (topology)
        {
            case HpWmiBios.KeyboardLightingType.RgbPerKey:
                Console.WriteLine("  -> Per-key RGB. The four-zone command path does NOT apply to this board.");
                Console.WriteLine("     Note the trap: the four-zone colour block (Keyboard 0x02) still reads back");
                Console.WriteLine("     plausibly here, because HP's ZONE_NUM is computed as (type - 4) <= 1 and a");
                Console.WriteLine("     per-key type falls through to 4. Reading that block alone would give a");
                Console.WriteLine("     confident wrong answer. Gate on this topology probe first.");
                break;
            case HpWmiBios.KeyboardLightingType.FourZoneWithNumpad:
            case HpWmiBios.KeyboardLightingType.FourZoneWithoutNumpad:
                Console.WriteLine("  -> Four-zone. Colours at block offset 25 + 3*zone, RGB order, Keyboard 0x02/0x03.");
                break;
            case HpWmiBios.KeyboardLightingType.OneZoneWithNumpad:
            case HpWmiBios.KeyboardLightingType.OneZoneWithoutNumpad:
                Console.WriteLine("  -> Single zone.");
                break;
            case HpWmiBios.KeyboardLightingType.Normal:
                Console.WriteLine("  -> Backlit but not addressable. No colour control.");
                break;
            case HpWmiBios.KeyboardLightingType.None:
                Console.WriteLine("  -> No keyboard lighting.");
                break;
            default:
                Console.WriteLine("  -> Topology probe gave no usable answer on this board.");
                Console.WriteLine("     That is information, not a failure: it means this board does not");
                Console.WriteLine("     implement Default 0x2B and its capability has to come from elsewhere.");
                break;
        }

        Console.WriteLine();
        return 0;
    }

    private static string Describe<T>(T? value) where T : struct =>
        value.HasValue ? value.Value.ToString()! : "(no answer)";
}
