// The one thing software cannot check: whether a colour actually reached the keys.
//
// There is no colour readback anywhere in the HID LampArray spec, so this mode cannot report
// success. What it can do is drive an unmistakable, self-describing pattern and tell a person
// exactly what they should be seeing - which turns "did the write work" into a question someone
// can answer in two seconds by looking down.
//
// The pattern is chosen so that a PARTIAL success is still readable. Solid red everywhere would
// look identical whether one report landed or all fifteen did.
//
// WRITES lamp colours. Restores host control and asks the device to resume its own effects on
// the way out, so nothing is left in a state a reboot would be needed to clear.

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class SelfTest
{
    internal static int Run(string[] args)
    {
        // --static writes the pattern once and leaves it, with no hold loop. It answers a
        // different question from the loop: whether a SINGLE write sticks. If it does, the loop
        // was only ever needed to out-run the device's own effect.
        bool staticOnce = args.Contains("--static");

        int holdSeconds = 10;
        int i = Array.IndexOf(args, "--hold");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed))
            holdSeconds = Math.Clamp(parsed, 2, 120);

        Console.WriteLine("=== LampArray self-test ===\n");
        Console.WriteLine("This WRITES colours to the keyboard. Nothing it does survives a reboot,");
        Console.WriteLine("and the device is handed back to its own effects at the end.\n");

        var arrays = HidLampArray.OpenAll();
        var keyboard = arrays.FirstOrDefault(a => a.Kind == 1 && a.LampCount > 8);

        if (keyboard == null)
        {
            Console.WriteLine("No keyboard-kind LampArray with more than 8 lamps found.");
            foreach (var a in arrays)
                Console.WriteLine($"  (saw kind {a.Kind}, {a.LampCount} lamps, {a.VendorId:X4}:{a.ProductId:X4})");
            foreach (var a in arrays) a.Dispose();
            return 1;
        }

        Console.WriteLine($"Target: {keyboard.VendorId:X4}:{keyboard.ProductId:X4}, " +
                          $"{keyboard.LampCount} lamps, min update {keyboard.MinUpdateInterval.TotalMilliseconds:F0} ms\n");

        try
        {
            bool tookControl = keyboard.SetAutonomousMode(false);
            Console.WriteLine($"  host control requested : {(tookControl ? "accepted" : "REFUSED")}");

            // Thirds, in three unmistakable colours. If only the first report lands, the keyboard
            // is one third red and the rest untouched - which looks different from every-report
            // landing, and different again from nothing landing.
            ushort last = (ushort)(keyboard.LampCount - 1);
            ushort third = (ushort)(keyboard.LampCount / 3);
            ushort twoThirds = (ushort)(keyboard.LampCount * 2 / 3);

            bool a = keyboard.SetRange(0, third, 255, 0, 0);
            bool b = keyboard.SetRange((ushort)(third + 1), twoThirds, 0, 255, 0);
            bool c = keyboard.SetRange((ushort)(twoThirds + 1), last, 0, 0, 255);

            Console.WriteLine($"  red   lamps {$"{0}-{third}",-9} : {Accepted(a)}");
            Console.WriteLine($"  green lamps {$"{third + 1}-{twoThirds}",-9} : {Accepted(b)}");
            Console.WriteLine($"  blue  lamps {$"{twoThirds + 1}-{last}",-9} : {Accepted(c)}");
            // Exercise the multi-update report too. It has a different layout from the range
            // update - a packed id array followed by a packed channel array, with both sized for
            // eight lamps whether or not all eight are used - so "range update works" says
            // nothing about it. Sixteen lamps forces a second batch, which is also where the
            // update-complete flag handling would show up if it were wrong.
            var white = new List<HidLampArray.LampColor>();
            ushort whiteCount = keyboard.LampCount < 16 ? keyboard.LampCount : (ushort)16;
            for (ushort id = 0; id < whiteCount; id++)
                white.Add(new HidLampArray.LampColor(id, 255, 255, 255));

            bool multi = keyboard.SetLamps(white);
            Console.WriteLine($"  white lamps 0-{white.Count - 1,-6} : {Accepted(multi)}   (multi-update, {(white.Count + 7) / 8} batches)");
            Console.WriteLine();
            Console.WriteLine("  \"accepted\" means the device took the report without stalling, which says the");
            Console.WriteLine("  report layout is right. It does NOT say the keys changed colour - there is no");
            Console.WriteLine("  colour readback to check that with.");


            Console.WriteLine();
            Console.WriteLine("  LOOK AT THE KEYBOARD NOW. What you should see, left to right:");
            Console.WriteLine("    the first third RED, the middle third GREEN, the last third BLUE,");
            Console.WriteLine("    except the first 16 lamps, which the multi-update turned WHITE.");
            Console.WriteLine();
            Console.WriteLine("    all three bands, in order  -> the write path works");
            Console.WriteLine("    only some bands            -> partial; report which ones");
            Console.WriteLine("    a rainbow, or flickering   -> the device's own effect is outrunning us,");
            Console.WriteLine("                                  or Windows Dynamic Lighting owns the device");
            Console.WriteLine("                                  (Settings > Personalisation > Dynamic");
            Console.WriteLine("                                  Lighting). Both look the same from here.");
            Console.WriteLine();
            if (staticOnce)
            {
                Console.WriteLine("  --static: written once, no hold loop. Whatever is on the keyboard now is");
                Console.WriteLine("  what a single write leaves behind. The device is NOT handed back to its");
                Console.WriteLine("  own effect engine, so nothing here will repaint it.");
                return 0;
            }

            Console.WriteLine($"  Holding {holdSeconds}s ...");

            // Drive at the device's own update rate, not once a second.
            //
            // Measured on board 8D87: a 1 Hz hold produced a RAINBOW with a few keys flickering
            // once a second - the device's own effect running at ~30 Hz, with each of our frames
            // visible for one repaint before being painted over. The reports were landing; they
            // were just being outrun.
            //
            // The HID LampArray spec lets a device resume autonomous effects when the host stops
            // updating, and MinUpdateInterval is what "updating" means. At 33 ms this host looked
            // idle between frames, so AutonomousMode was accepted and then effectively undone.
            // So: re-send the control report periodically as well as the colours, and pace the
            // loop off the device's own declared interval rather than a number picked by hand.
            int frameMs = Math.Max(16, (int)keyboard.MinUpdateInterval.TotalMilliseconds);
            int frames = holdSeconds * 1000 / frameMs;
            int framesPerSecond = Math.Max(1, 1000 / frameMs);

            for (int f = 0; f < frames; f++)
            {
                if (f % framesPerSecond == 0) keyboard.SetAutonomousMode(false);

                keyboard.SetRange(0, third, 255, 0, 0);
                keyboard.SetRange((ushort)(third + 1), twoThirds, 0, 255, 0);
                keyboard.SetRange((ushort)(twoThirds + 1), last, 0, 0, 255);
                keyboard.SetLamps(white);
                Thread.Sleep(frameMs);
            }

            Console.WriteLine("\n  Handing the keyboard back to its own effects.");
            keyboard.SetAutonomousMode(true);
            return 0;
        }
        finally
        {
            keyboard.SetAutonomousMode(true);
            foreach (var arr in arrays) arr.Dispose();
        }
    }

    private static string Accepted(bool ok) => ok ? "accepted" : "REFUSED";
}
