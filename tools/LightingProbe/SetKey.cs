// Light exactly ONE key, and blank every other one.
//
// This is the narrowest possible test of per-key control, and it is deliberately not the
// three-band pattern --self-test draws. Bands prove reports are landing somewhere; one lit key on
// an otherwise black keyboard proves the lamp INDEX is right, which is the thing a per-key feature
// actually needs. A wrong index still produces a lit key - just the wrong one - and that is
// readable at a glance in a way a band is not.
//
// Board 8D87 note: the lamp-attributes response ignores the requested id and free-runs, so the map
// is built by keying on the id the DEVICE reports (same approach as --map). A caller that assumed
// "ask for N, get N" would light the wrong key and have no way to tell.
//
// WRITES lamp colours. Gated behind --commit, like every other write in this investigation: the
// dry run resolves the key, prints the lamp id it would drive, and sends nothing.
//
// Two things will fight you, and both look identical from here - a rainbow that ignores you:
//   * Windows Dynamic Lighting owns LampArray devices when enabled and repaints continuously.
//   * The device's own effect engine runs at ~30 Hz and resumes whenever the host looks idle.
// Stop OGH's background process and turn Dynamic Lighting off before trusting a negative result.

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class SetKey
{
    internal static int Run(string[] args)
    {
        string keyArg = Arg(args, "--key") ?? "F4";
        string colorArg = Arg(args, "--color") ?? "FF0000";
        bool commit = args.Contains("--commit");

        // ONE WRITE IS THE DEFAULT, and that is a measured decision rather than an assumption.
        //
        // This mode originally always ran a 30 Hz repaint loop, inherited from --self-test, whose
        // comment reasons that the device resumes its own effects whenever the host looks idle for
        // longer than MinUpdateInterval. Measured 2026-08-06, after the keyboard was recovered: a
        // single blank-around + single-lamp update, with the process then EXITING, left the key lit
        // and steady. So AutonomousMode = 0 is honoured and persists with no host process at all.
        //
        // The earlier "the device outruns us" reading came from two things that were both true at
        // the time and are not properties of the protocol: the keyboard was in the stuck state, and
        // this file was painting the target black every frame (see BlankAround). Neither justifies a
        // permanent repaint thread, which is what shipping this in OmenCore would have inherited.
        //
        // --hold <seconds> still runs the loop, because "does it decay after N seconds" is a
        // different question from "does one write land" and only the loop can answer it.
        bool holdRequested = Arg(args, "--hold") != null;
        int holdSeconds = 10;
        if (int.TryParse(Arg(args, "--hold"), out int parsedHold))
            holdSeconds = Math.Clamp(parsedHold, 2, 120);

        if (!TryParseColor(colorArg, out byte r, out byte g, out byte b))
        {
            Console.WriteLine($"Could not parse --color '{colorArg}'. Expected RRGGBB.");
            return 1;
        }

        Console.WriteLine("=== Light a single key ===\n");

        var arrays = HidLampArray.OpenAll();
        var keyboard = arrays.FirstOrDefault(a => a.Kind == 1 && a.LampCount > 8);
        if (keyboard == null)
        {
            Console.WriteLine("  No keyboard-kind LampArray found.");
            foreach (var a in arrays) a.Dispose();
            return 1;
        }

        try
        {
            Console.WriteLine($"  target : {keyboard.VendorId:X4}:{keyboard.ProductId:X4}, " +
                              $"{keyboard.LampCount} lamps, min update " +
                              $"{keyboard.MinUpdateInterval.TotalMilliseconds:F0} ms");

            // Build the map the same way --map does: key on the reported id, walk an extra pass.
            var byId = new SortedDictionary<ushort, HidLampArray.LampInfo>();
            for (int i = 0; i < keyboard.LampCount * 2 && byId.Count < keyboard.LampCount; i++)
            {
                var info = keyboard.GetLampInfo((ushort)(i % keyboard.LampCount));
                if (info != null) byId[info.Value.LampId] = info.Value;
            }
            if (byId.Count < keyboard.LampCount)
                Console.WriteLine($"  WARNING: map incomplete, {byId.Count} of {keyboard.LampCount} lamps seen.");

            // Resolve --key to a lamp id. A bare number is a lamp id; anything else is a key.
            ushort lampId;
            string label;
            if (ushort.TryParse(keyArg, out ushort direct) && byId.ContainsKey(direct))
            {
                lampId = direct;
                label = $"lamp {direct} (usage 0x{byId[direct].KeyUsage:X2})";
            }
            else
            {
                if (!TryParseUsage(keyArg, out byte usage))
                {
                    Console.WriteLine($"  Could not resolve --key '{keyArg}'. Try a key name (F4, Esc, A, Space),");
                    Console.WriteLine("  a HID usage (0x3D), or a bare lamp id. Run --map for the full table.");
                    return 1;
                }
                var hits = byId.Where(kv => kv.Value.KeyUsage == usage).Select(kv => kv.Key).ToList();
                if (hits.Count == 0)
                {
                    Console.WriteLine($"  No lamp carries usage 0x{usage:X2}. Run --map to see what exists.");
                    return 1;
                }
                if (hits.Count > 1)
                    Console.WriteLine($"  note: usage 0x{usage:X2} maps to {hits.Count} lamps ({string.Join(", ", hits)}); using the first.");
                lampId = hits[0];
                label = $"'{keyArg}' -> usage 0x{usage:X2} -> lamp {lampId}";
            }

            var pos = byId[lampId];
            Console.WriteLine($"  key    : {label}");
            Console.WriteLine($"  at     : ({pos.XMicrometres / 1000.0:F0}, {pos.YMicrometres / 1000.0:F0}) mm");
            Console.WriteLine($"  colour : #{r:X2}{g:X2}{b:X2}");
            Console.WriteLine($"  plan   : blank every lamp EXCEPT {lampId}, then set lamp {lampId}");
            Console.WriteLine($"  mode   : {(holdRequested ? $"repaint loop, {holdSeconds}s, then hand back" : "ONE write, then exit (nothing maintains it)")}");

            if (!commit)
            {
                Console.WriteLine();
                Console.WriteLine("  DRY RUN. Nothing was written. Re-run with --commit.");
                return 0;
            }

            ushort last = (ushort)(keyboard.LampCount - 1);
            var one = new List<HidLampArray.LampColor> { new(lampId, r, g, b) };

            Console.WriteLine();
            bool control = keyboard.SetAutonomousMode(false);
            Console.WriteLine($"  host control  : {(control ? "accepted" : "REFUSED")}   (unverifiable - the control report is not readable on this device)");
            Console.WriteLine($"  blank others  : {(BlankAround(keyboard, lampId, last) ? "accepted" : "REFUSED")}");
            Console.WriteLine($"  set lamp {lampId,-4} : {(keyboard.SetLamps(one) ? "accepted" : "REFUSED")}");

            Console.WriteLine();
            Console.WriteLine("  \"accepted\" means the device took the report. There is no colour readback in");
            Console.WriteLine("  the LampArray spec, so only looking can confirm it.");
            Console.WriteLine();
            Console.WriteLine($"  LOOK AT THE KEYBOARD. Expect: everything dark except ONE key lit #{r:X2}{g:X2}{b:X2}.");
            Console.WriteLine($"  It should be the key at x={pos.XMicrometres / 1000.0:F0} mm, y={pos.YMicrometres / 1000.0:F0} mm.");
            Console.WriteLine();
            Console.WriteLine("    exactly that key      -> per-key control works, and the map is right");
            Console.WriteLine("    a different key       -> control works, the lamp index is wrong");
            Console.WriteLine("    all dark              -> the blank landed, the single-lamp update did not");
            Console.WriteLine("    rainbow / flickering  -> something else owns the device (DL, OGH, or the");
            Console.WriteLine("                             device's own effect engine outrunning us)");

            if (!holdRequested)
            {
                Console.WriteLine();
                Console.WriteLine("  Written ONCE. This process is about to exit and nothing will maintain it,");
                Console.WriteLine("  so whatever is on the keyboard in a minute is what one report achieves.");
                Console.WriteLine("  The device is NOT handed back to its own effects. To restore it:");
                Console.WriteLine("    LightingProbe --autonomous on      (or launch OGH, or reboot)");
                return 0;
            }

            // Pace off the device's declared interval and re-assert control, or its own effect
            // engine resumes the moment the host looks idle. See the note in SelfTest.
            int frameMs = Math.Max(16, (int)keyboard.MinUpdateInterval.TotalMilliseconds);
            int frames = holdSeconds * 1000 / frameMs;
            int framesPerSecond = Math.Max(1, 1000 / frameMs);
            Console.WriteLine();
            Console.WriteLine($"  Holding {holdSeconds}s at {frameMs} ms/frame ...");

            for (int f = 0; f < frames; f++)
            {
                if (f % framesPerSecond == 0) keyboard.SetAutonomousMode(false);
                BlankAround(keyboard, lampId, last);
                keyboard.SetLamps(one);
                Thread.Sleep(frameMs);
            }

            Console.WriteLine("  Handing the keyboard back to its own effects.");
            return 0;
        }
        finally
        {
            // Only hand back when a hold was asked for. The default is a single write whose whole
            // point is that it survives this process exiting, and restoring autonomous mode here
            // would repaint it before anyone could look.
            if (holdRequested) keyboard.SetAutonomousMode(true);
            foreach (var a in arrays) a.Dispose();
        }
    }

    /// <summary>
    /// Blank every lamp EXCEPT the target, as two ranges either side of it.
    ///
    /// Measured 2026-08-06: blanking 0..last and then setting the target in a second report made
    /// the target visibly STROBE at the frame rate. Of course it did - the target was painted black
    /// and then red every frame, with a report boundary in between. The first read of that was
    /// "flickering, so something is fighting us", which was wrong: the fight was with the previous
    /// line of this method. Never paint the target black.
    /// </summary>
    private static bool BlankAround(HidLampArray kb, ushort target, ushort last)
    {
        bool ok = true;
        if (target > 0) ok &= kb.SetRange(0, (ushort)(target - 1), 0, 0, 0);
        if (target < last) ok &= kb.SetRange((ushort)(target + 1), last, 0, 0, 0);
        return ok;
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static bool TryParseColor(string s, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        string h = s.Trim().TrimStart('#');
        if (h.Length != 6) return false;
        try
        {
            r = Convert.ToByte(h[..2], 16);
            g = Convert.ToByte(h[2..4], 16);
            b = Convert.ToByte(h[4..], 16);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Inverse of LampMap.KeyName for the names worth typing, plus raw usages. Deliberately small:
    /// --map prints the authoritative table, so this only has to cover the convenient cases.
    /// </summary>
    internal static bool TryParseUsage(string s, out byte usage)
    {
        usage = 0;
        string k = s.Trim();

        if (k.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(k[2..], System.Globalization.NumberStyles.HexNumber, null, out usage);

        if (k.Length == 1)
        {
            char c = char.ToUpperInvariant(k[0]);
            if (c is >= 'A' and <= 'Z') { usage = (byte)(0x04 + (c - 'A')); return true; }
            if (c is >= '1' and <= '9') { usage = (byte)(0x1E + (c - '1')); return true; }
            if (c == '0') { usage = 0x27; return true; }
        }

        if ((k.StartsWith('F') || k.StartsWith('f')) && int.TryParse(k[1..], out int n) && n is >= 1 and <= 12)
        {
            usage = (byte)(0x39 + n);
            return true;
        }

        usage = k.ToLowerInvariant() switch
        {
            "esc" or "escape" => 0x29,
            "enter" or "return" => 0x28,
            "backspace" => 0x2A,
            "tab" => 0x2B,
            "space" or "spacebar" => 0x2C,
            "capslock" or "caps" => 0x39,
            "insert" or "ins" => 0x49,
            "home" => 0x4A,
            "pgup" or "pageup" => 0x4B,
            "delete" or "del" => 0x4C,
            "end" => 0x4D,
            "pgdn" or "pagedown" => 0x4E,
            "right" => 0x4F,
            "left" => 0x50,
            "down" => 0x51,
            "up" => 0x52,
            "menu" => 0x65,
            "lctrl" => 0xE0,
            "lshift" => 0xE1,
            "lalt" => 0xE2,
            "lgui" or "win" => 0xE3,
            _ => 0
        };
        return usage != 0;
    }
}
