// Drives the LampArray control report ON ITS OWN, with no colour writes at all.
//
// This isolates a question the self-test cannot answer. There, AutonomousMode = 0 is sent and
// then colours are written on top, so "the keyboard shows the device's own effect" has two
// possible causes: the control report was ignored, or it worked and the colour writes are losing
// a race. Sending the control report alone separates them - if the device's effect stops, the
// report works; if it carries on, the report is being accepted and ignored.
//
// It is also the way back. The self-test hands the device back with AutonomousMode = 1 on exit,
// which on a keyboard that was NOT running an effect beforehand does not restore anything - it
// starts one. --autonomous off is the undo.
//
// Also tries to READ the control report. The HID spec does not require it to be readable, and a
// device that answers is worth knowing about: it would let a caller restore the mode it found
// rather than assuming one.

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class Autonomous
{
    internal static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--autonomous");
        string? mode = i >= 0 && i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : null;

        if (mode is not ("on" or "off"))
        {
            Console.WriteLine("Usage: LightingProbe --autonomous <on|off> [--hold <seconds>]");
            Console.WriteLine();
            Console.WriteLine("  off  take the lamps away from the device's own effect engine");
            Console.WriteLine("  on   hand them back (this is what starts a built-in effect)");
            return 1;
        }

        int hold = 0;
        int h = Array.IndexOf(args, "--hold");
        if (h >= 0 && h + 1 < args.Length && int.TryParse(args[h + 1], out int parsed))
            hold = Math.Clamp(parsed, 0, 120);

        bool autonomous = mode == "on";

        Console.WriteLine("=== LampArray autonomous mode ===\n");

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
            Console.WriteLine($"  {keyboard.VendorId:X4}:{keyboard.ProductId:X4}, {keyboard.LampCount} lamps\n");

            var before = keyboard.TryReadAutonomousMode();
            Console.WriteLine($"  control report readable : {(before.HasValue ? $"yes, AutonomousMode = {(before.Value ? 1 : 0)}" : "no")}");

            bool ok = keyboard.SetAutonomousMode(autonomous);
            Console.WriteLine($"  set AutonomousMode = {(autonomous ? 1 : 0)}   : {(ok ? "accepted" : "REFUSED")}");

            var after = keyboard.TryReadAutonomousMode();
            if (after.HasValue)
            {
                Console.WriteLine($"  reads back              : {(after.Value ? 1 : 0)}");
                if (after.Value != autonomous)
                {
                    Console.WriteLine("  -> ACCEPTED BUT NOT APPLIED. The device took the report and kept its own");
                    Console.WriteLine("     setting, which is the whole reason this mode exists separately.");
                }
            }

            if (hold > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  Holding {hold}s with NO colour writes. Look at the keyboard:");
                Console.WriteLine(autonomous
                    ? "    an effect should be running."
                    : "    the device's own effect should have STOPPED. Whatever the lamps show now\n" +
                      "    is static - dark, or the last colours written to them.");
                Thread.Sleep(hold * 1000);
            }

            Console.WriteLine();
            Console.WriteLine("  Nothing else was written. No colour was set.");
            return 0;
        }
        finally
        {
            foreach (var a in arrays) a.Dispose();
        }
    }
}
