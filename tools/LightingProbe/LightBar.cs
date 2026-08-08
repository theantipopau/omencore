// The light bar - a different device from the keyboard, on a different transport.
//
// This drives OmenCore's HpWmiBios light bar methods, which speak HP's own SetLightColor over WMI
// (class Keyboard 0x00020009, command 0x0B). Four zones, zone 0 LEFTMOST, one command carrying
// static colour, animation, brightness and off.
//
// THERE IS A SECOND PATH and it is worth knowing about before choosing this one. The bar also
// enumerates as a HID LampArray - "HID VHF Driver" 0461:0001, Kind Scene, 4 lamps at ids 0..3
// across a 323 x 5 mm strip - which is Windows exposing it through the Virtual HID Framework. So
// Windows Dynamic Lighting can drive it, which is why it sometimes changes without being asked.
// The WMI path used here is the measured one and the only one with brightness and animations.
//
// WHAT READS BACK, AND WHAT DOES NOT. Colour reads back, per zone, through Default 0x04.
// Animation and brightness do NOT - HP's own animation getter (Keyboard 0x0C) answers FAIL on this
// board. That is the OPPOSITE of the keyboard MCU, where the effect record reads back and colour
// does not. Carrying an assumption from one device to the other gets it wrong in both directions.
//
// Requires elevation, like every WMI BIOS call.

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class LightBar
{
    internal static int Run(string[] args)
    {
        bool commit = args.Contains("--commit");

        Console.WriteLine("=== Light bar ===\n");

        // Pass logging so a refusal is diagnosable rather than a bare False.
        var logging = new OmenCore.Services.LoggingService();
        logging.Initialize();
        using var bios = new HpWmiBios(logging);
        if (!bios.IsAvailable)
        {
            // Not a dead end. Colour is reachable over HID without elevation; brightness and the
            // nine animations are not reachable any other way than WMI, so say which is which
            // rather than just refusing.
            Console.WriteLine("  HP WMI BIOS unavailable (needs administrator).");
            Console.WriteLine("  Falling back to the light bar's HID LampArray, which does not.\n");
            return HidFallback(args, commit);
        }

        // Always snapshot first. Colour is the one light bar property that reads back, and it is
        // the whole difference between a bad colour being one command to undo and an evening spent
        // guessing what it used to be.
        var before = bios.GetLightBarColors();
        Console.WriteLine($"  before   : {Format(before)}");

        string? animation = Arg(args, "--lightbar-effect");
        string? colors = Arg(args, "--lightbar");
        string? brightnessArg = Arg(args, "--lightbar-brightness");
        byte brightness = byte.TryParse(brightnessArg, out byte b) ? Math.Clamp(b, (byte)0, (byte)100) : (byte)100;

        if (args.Contains("--lightbar-off"))
        {
            Console.WriteLine("  plan     : all zones off");
            if (!commit) return DryRun();

            Console.WriteLine($"  accepted : {bios.SetLightBarOff()}");
            Console.WriteLine($"  after    : {Format(bios.GetLightBarColors())}");
            return 0;
        }

        if (animation != null)
        {
            if (!Enum.TryParse(animation, ignoreCase: true, out HpWmiBios.LightBarEffect effect))
            {
                Console.WriteLine($"  Unknown --lightbar-effect '{animation}'. One of: " +
                                  string.Join(", ", Enum.GetNames<HpWmiBios.LightBarEffect>()));
                return 1;
            }

            var themeName = Arg(args, "--lightbar-theme") ?? (colors != null ? "Custom" : "Galaxy");
            if (!Enum.TryParse(themeName, ignoreCase: true, out HpWmiBios.LightBarTheme theme))
            {
                Console.WriteLine($"  Unknown --lightbar-theme '{themeName}'. One of: " +
                                  string.Join(", ", Enum.GetNames<HpWmiBios.LightBarTheme>()));
                return 1;
            }

            var speedName = Arg(args, "--lightbar-speed") ?? "Medium";
            Enum.TryParse(speedName, ignoreCase: true, out HpWmiBios.LightBarSpeed speed);

            byte tribe = byte.TryParse(Arg(args, "--tribe"), out byte t) ? t : (byte)0;
            byte bass = byte.TryParse(Arg(args, "--bass"), out byte s) ? s : (byte)0;

            Console.WriteLine($"  plan     : {effect}, theme {theme}, speed {speed}, brightness {brightness}");
            if (effect == HpWmiBios.LightBarEffect.Swipe && theme != HpWmiBios.LightBarTheme.Custom)
                Console.WriteLine("             Swipe has no preset palette - expect BLACK. Add --lightbar with colours.");
            if (effect == HpWmiBios.LightBarEffect.AudioPulse && tribe == 0 && bass == 0)
                Console.WriteLine("             Audio Pulse IS its levels - expect BLACK. Add --tribe 100 --bass 100.");

            if (!commit) return DryRun();

            bool ok = bios.SetLightBarAnimation(effect, theme, speed,
                                                HpWmiBios.LightBarDirection.Left, brightness,
                                                ParseColors(colors), tribe, bass);
            Console.WriteLine($"  accepted : {ok}");
            Console.WriteLine();
            Console.WriteLine("  THERE IS NO READBACK FOR ANIMATION STATE. Accepted means the firmware took");
            Console.WriteLine("  the frame and nothing more. Look at the bar.");
            return ok ? 0 : 1;
        }

        if (colors == null && brightnessArg == null)
        {
            Console.WriteLine();
            Console.WriteLine("  Read-only. To write:");
            Console.WriteLine("    --lightbar FF0000,00FF00,0000FF,FFFFFE --commit");
            Console.WriteLine("    --lightbar 0000FF --lightbar-brightness 30 --commit");
            Console.WriteLine("    --lightbar-effect Wave --lightbar-theme Ocean --commit");
            Console.WriteLine("    --lightbar-off --commit");
            return 0;
        }

        var wanted = ParseColors(colors);
        if (wanted.Count == 0)
        {
            // Brightness with no colours means "same picture, dimmer", which needs the colours
            // back - and they are readable, so there is no reason to make the caller retype them.
            wanted = before.Where(z => z != null).Select(z => z!.Value).ToList();
            if (wanted.Count == 0)
            {
                Console.WriteLine("  --lightbar-brightness alone needs the current colours, and the read failed.");
                return 1;
            }
        }

        Console.WriteLine($"  plan     : {string.Join("  ", wanted.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}"))}" +
                          $"   brightness {brightness}");

        if (wanted.Any(c => c is { R: 0xFF, G: 0xFF, B: 0xFF }))
            Console.WriteLine("             note: #FFFFFF is substituted to #FFFFFE - asking for it gives PINK.");

        if (!commit) return DryRun();

        bool wrote = bios.SetLightBarColors(wanted, brightness);
        Console.WriteLine($"  accepted : {wrote}");

        var after = bios.GetLightBarColors();
        Console.WriteLine($"  after    : {Format(after)}");
        Console.WriteLine();

        // Three outcomes, not two. A binary matches/does-not-match verdict reports a firmware
        // adjustment as a failure, which is the interesting case and the one worth naming.
        string beforeText = Format(before), afterText = Format(after);
        string wantedText = string.Join("  ", wanted.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}"));

        if (afterText.StartsWith(wantedText, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("  Readback matches the request.");
        else if (afterText == beforeText)
            Console.WriteLine("  READBACK IS UNCHANGED from before the write. It did not take.");
        else
            Console.WriteLine("  Readback changed but differs from the request - the firmware adjusted it.");

        Console.WriteLine();
        Console.WriteLine("  Brightness has no readback on this path; only looking confirms it.");
        return wrote ? 0 : 1;
    }

    /// <summary>
    /// Drive the bar over its own LampArray, with no elevation.
    ///
    /// Colour only. Brightness and the nine built-in animations live behind HP's WMI surface and
    /// there is no HID equivalent, so this cannot be a drop-in replacement - it is the subset that
    /// happens not to need administrator.
    /// </summary>
    private static int HidFallback(string[] args, bool commit)
    {
        using var bar = HidLampArray.OpenLightBar();
        if (bar == null)
        {
            // Two different failures, and telling them apart is the whole value of the message: an
            // ungated board is a decision this code made, a missing device is the hardware's answer.
            if (!OmenBoard.SupportsHidLightBar())
            {
                Console.WriteLine($"  Board '{OmenBoard.Product}' is not on the HID light bar list, so this path is");
                Console.WriteLine("  gated off. It is confirmed on 8D87 only. If this machine has a light bar,");
                Console.WriteLine("  run --lamps and report a Scene-kind array with a bar-shaped bounding box.");
            }
            else
            {
                Console.WriteLine("  No bar-shaped Scene LampArray found. Nothing can reach the light bar on");
                Console.WriteLine("  this machine without elevation.");
            }

            return 1;
        }

        Console.WriteLine($"  device   : {bar.VendorId:X4}:{bar.ProductId:X4}, {bar.LampCount} lamps, " +
                          $"min update {bar.MinUpdateInterval.TotalMilliseconds:F0} ms");

        if (args.Contains("--lightbar-effect") || Arg(args, "--lightbar-brightness") != null)
        {
            Console.WriteLine();
            Console.WriteLine("  Brightness and animations are NOT available on this path - they exist only");
            Console.WriteLine("  behind HP's WMI commands, which need administrator. Colour is all HID offers.");
        }

        if (args.Contains("--lightbar-off"))
        {
            Console.WriteLine("  plan     : all four lamps black");
            if (!commit) return DryRun();
            Console.WriteLine($"  accepted : {bar.SetAll(0, 0, 0)}");
            return 0;
        }

        var wanted = ParseColors(Arg(args, "--lightbar"));
        if (wanted.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Read-only. There is no colour readback over HID, so there is nothing to show");
            Console.WriteLine("  you here - the WMI path is the one that can read the bar back.");
            Console.WriteLine();
            Console.WriteLine("  To write, unelevated:  --lightbar FF0000,00FF00,0000FF,FFFFFE --commit");
            return 0;
        }

        // Zone 0 is leftmost, and lamp ids run 0..3 in the same direction, so the mapping is the
        // identity. Stated rather than assumed, because it is the kind of thing that is quietly
        // reversed on another board.
        var lamps = new List<HidLampArray.LampColor>();
        for (ushort zone = 0; zone < bar.LampCount; zone++)
        {
            var c = wanted[Math.Min(zone, wanted.Count - 1)];
            lamps.Add(new HidLampArray.LampColor(zone, c.R, c.G, c.B));
        }

        Console.WriteLine($"  plan     : {string.Join("  ", lamps.Select(l => $"#{l.R:X2}{l.G:X2}{l.B:X2}"))}");

        if (!commit) return DryRun();

        bar.SetAutonomousMode(false);
        bool ok = bar.SetLamps(lamps);
        Console.WriteLine($"  accepted : {ok}");
        Console.WriteLine();
        Console.WriteLine("  No colour readback on this path, so accepted is the whole software claim.");
        Console.WriteLine("  Look at the bar.");
        return ok ? 0 : 1;
    }

    private static int DryRun()
    {
        Console.WriteLine("\n  DRY RUN. Nothing written. Add --commit to send it.");
        return 0;
    }

    private static string Format((byte R, byte G, byte B)?[] zones) =>
        string.Join("  ", zones.Select(z => z == null ? "??????" : $"#{z.Value.R:X2}{z.Value.G:X2}{z.Value.B:X2}"));

    private static List<(byte R, byte G, byte B)> ParseColors(string? spec)
    {
        var list = new List<(byte, byte, byte)>();
        if (string.IsNullOrWhiteSpace(spec)) return list;

        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string h = part.TrimStart('#');
            if (h.Length != 6) continue;
            try
            {
                list.Add((Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16)));
            }
            catch { /* malformed swatches are dropped; the printed plan shows what survived */ }
        }

        return list.Take(HpWmiBios.LightBarZoneCount).ToList();
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
