using System.Collections.Generic;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// HID keyboard usage -> HP's key name, the join between the two interfaces this keyboard has.
    ///
    /// mi_04 (LampArray) reports, for every lamp, the HID usage of the key it sits under. mi_03
    /// (the MCU colour map) is indexed by LED position and grouped by HP's key names. Neither
    /// mentions the other, so a lamp id becomes an LED position only through the key it lights:
    ///
    ///     lamp id -> LampInfo.KeyUsage -> this table -> HP key name -> KeyboardKey.Leds
    ///
    /// That matters because only the mi_03 map survives the Fn overlay. Anything painted on mi_04
    /// alone is gone the moment the MCU redraws its base layer.
    ///
    /// Usages are from the standard HID Keyboard/Keypad page (0x07), which is what the device
    /// actually reports — measured on 8D87, e.g. 0x29 on the lamp under Esc and 0x2C on all five
    /// under Space.
    ///
    /// NOT EVERY KEY HAS ONE. The vendor keys — Omen, Calculator, Settings, Power, Fn, Copilot —
    /// report no usable usage (the capture shows 0x00 or 0x03 filler), so they cannot be reached
    /// this way and are absent here deliberately. A caller that needs them must address them by
    /// name. Names follow the layout tables, so a name missing from a given layout simply does not
    /// resolve; that is expected, not an error.
    /// </summary>
    internal static class HidUsageKeyNames
    {
        private static readonly Dictionary<byte, string> Map = Build();

        /// <summary>HP's key name for a HID usage, or null when the usage has no key on this map.</summary>
        public static string? Name(byte usage) => Map.TryGetValue(usage, out var name) ? name : null;

        private static Dictionary<byte, string> Build()
        {
            var map = new Dictionary<byte, string>();

            // 0x04..0x1D: A-Z, in alphabetical order.
            for (byte usage = 0x04; usage <= 0x1D; usage++)
                map[usage] = "Key" + (char)('A' + (usage - 0x04));

            // 0x1E..0x26: 1-9. 0 is 0x27, after 9 rather than before 1.
            for (byte usage = 0x1E; usage <= 0x26; usage++)
                map[usage] = "Key" + (char)('1' + (usage - 0x1E));
            map[0x27] = "Key0";

            // 0x3A..0x45: F1-F12.
            for (byte usage = 0x3A; usage <= 0x45; usage++)
                map[usage] = "KeyF" + (usage - 0x3A + 1);

            // 0x59..0x61: keypad 1-9, again with 0 following at 0x62.
            for (byte usage = 0x59; usage <= 0x61; usage++)
                map[usage] = "KeyNum" + (char)('1' + (usage - 0x59));
            map[0x62] = "KeyNum0";

            map[0x28] = "KeyEnter";
            map[0x29] = "KeyEsc";
            map[0x2A] = "KeyBack";
            map[0x2B] = "KeyTab";
            map[0x2C] = "KeySpace";
            map[0x2D] = "KeyHyphen";
            map[0x2E] = "KeyEqual";
            map[0x2F] = "KeyBracketsL";
            map[0x30] = "KeyBracketsR";
            map[0x31] = "KeyBackslash";
            map[0x33] = "KeyColon";      // ';' unshifted - HP names it for the shifted legend
            map[0x34] = "KeyQuote";
            map[0x35] = "KeyTilde";      // '`' unshifted, likewise
            map[0x36] = "KeyComma";
            map[0x37] = "KeyDot";
            map[0x38] = "KeySlash";
            map[0x39] = "KeyCaps";
            map[0x4C] = "KeyDel";
            map[0x4F] = "KeyArrRight";
            map[0x50] = "KeyArrLeft";
            map[0x51] = "KeyArrDown";
            map[0x52] = "KeyArrUP";      // HP's own casing; do not "fix" it, it is a lookup key
            map[0x53] = "KeyNumPad";     // Num Lock
            map[0x54] = "KeyNumSlash";
            map[0x55] = "KeyNumStar";
            map[0x56] = "KeyNumDash";
            map[0x57] = "KeyNumPlus";
            map[0x58] = "KeyNumEnter";
            map[0x63] = "KeyNumDel";     // keypad '.'
            map[0xE0] = "KeyCtrlL";
            map[0xE1] = "KeyShiftL";
            map[0xE2] = "KeyAltL";
            map[0xE3] = "KeyWin";
            map[0xE4] = "KeyCtrlR";
            map[0xE5] = "KeyShiftR";
            map[0xE6] = "KeyAltR";

            return map;
        }
    }
}
