// Walks the whole lamp array and builds an id -> key map.
//
// This exists to answer a question the four-lamp sample in --lamps cannot: whether a complete map
// is obtainable at all. On board 8D87's keyboard the lamp-attributes response IGNORES the
// requested id and free-runs, so "ask for lamp N, get lamp N" does not hold and a caller that
// assumed it would mislabel every key. Reading sequentially and keying on the id the DEVICE
// returned recovers the map anyway - and this mode reports coverage so that claim is checked
// rather than asserted.
//
// Each lamp carries the HID usage of the key it sits under, so the map is a table lookup rather
// than a calibration exercise.
//
// READ ONLY. No colour is written.

using OmenCore.Hardware;

namespace OmenCore.Tools.LightingProbe;

internal static class LampMap
{
    internal static int Run()
    {
        Console.WriteLine("=== Lamp -> key map ===\n");

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

            // Key on the id the device reported, not the one requested. Walk one extra pass so a
            // free-running device that starts partway through still comes back around.
            var byId = new SortedDictionary<ushort, HidLampArray.LampInfo>();
            int requests = keyboard.LampCount * 2;
            int mismatched = 0;

            for (int i = 0; i < requests && byId.Count < keyboard.LampCount; i++)
            {
                var info = keyboard.GetLampInfo((ushort)(i % keyboard.LampCount));
                if (info == null) continue;
                if (info.Value.LampId != (ushort)(i % keyboard.LampCount)) mismatched++;
                byId[info.Value.LampId] = info.Value;
            }

            Console.WriteLine($"  requests issued   : up to {requests}");
            Console.WriteLine($"  distinct lamps    : {byId.Count} of {keyboard.LampCount}");
            Console.WriteLine($"  id mismatches     : {mismatched}  (response id != requested id)");
            Console.WriteLine();

            if (byId.Count < keyboard.LampCount)
            {
                Console.WriteLine("  INCOMPLETE. Some lamps never came back, so a map built this way would have");
                Console.WriteLine("  holes. Do not treat a partial map as a complete one.");
            }
            else
            {
                Console.WriteLine("  Complete: every lamp id was seen, so keying on the reported id recovers the");
                Console.WriteLine("  full map even though the device ignores the requested one.");
            }

            Console.WriteLine();
            Console.WriteLine("  id   x(mm)  y(mm)   HID usage  key");
            Console.WriteLine("  ---  -----  -----   ---------  ---");
            foreach (var (id, info) in byId)
            {
                Console.WriteLine($"  {id,3}  {info.XMicrometres / 1000.0,5:F0}  {info.YMicrometres / 1000.0,5:F0}   " +
                                  $"0x{info.KeyUsage:X2}       {KeyName(info.KeyUsage)}");
            }

            int bound = byId.Values.Count(v => v.KeyUsage != 0);
            Console.WriteLine();
            Console.WriteLine($"  {bound} of {byId.Count} lamps carry a key binding.");
            return byId.Count == keyboard.LampCount ? 0 : 2;
        }
        finally
        {
            foreach (var a in arrays) a.Dispose();
        }
    }

    /// <summary>
    /// HID Keyboard/Keypad usage page (0x07) names, for the usages a laptop keyboard actually
    /// uses. Unknown usages print as hex rather than being guessed at.
    /// </summary>
    private static string KeyName(byte usage) => usage switch
    {
        0x00 => "(unbound)",
        >= 0x04 and <= 0x1D => ((char)('A' + (usage - 0x04))).ToString(),
        >= 0x1E and <= 0x26 => ((char)('1' + (usage - 0x1E))).ToString(),
        0x27 => "0",
        0x28 => "Enter",
        0x29 => "Esc",
        0x2A => "Backspace",
        0x2B => "Tab",
        0x2C => "Space",
        0x2D => "-",
        0x2E => "=",
        0x2F => "[",
        0x30 => "]",
        0x31 => "\\",
        0x33 => ";",
        0x34 => "'",
        0x35 => "`",
        0x36 => ",",
        0x37 => ".",
        0x38 => "/",
        0x39 => "CapsLock",
        >= 0x3A and <= 0x45 => $"F{usage - 0x39}",
        0x46 => "PrtSc",
        0x47 => "ScrollLock",
        0x48 => "Pause",
        0x49 => "Insert",
        0x4A => "Home",
        0x4B => "PgUp",
        0x4C => "Delete",
        0x4D => "End",
        0x4E => "PgDn",
        0x4F => "Right",
        0x50 => "Left",
        0x51 => "Down",
        0x52 => "Up",
        0x53 => "NumLock",
        0x54 => "Keypad /",
        0x55 => "Keypad *",
        0x56 => "Keypad -",
        0x57 => "Keypad +",
        0x58 => "Keypad Enter",
        >= 0x59 and <= 0x61 => $"Keypad {usage - 0x58}",
        0x62 => "Keypad 0",
        0x63 => "Keypad .",
        0x65 => "Menu",
        0xE0 => "LCtrl",
        0xE1 => "LShift",
        0xE2 => "LAlt",
        0xE3 => "LGui",
        0xE4 => "RCtrl",
        0xE5 => "RShift",
        0xE6 => "RAlt",
        0xE7 => "RGui",
        _ => $"0x{usage:X2}"
    };
}
