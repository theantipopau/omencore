using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Turns a staged set of profiles into the exact sequence of effect writes that leaves the
    /// keyboard's <c>Fn+1</c> / <c>Fn+2</c> cycle holding them.
    ///
    /// WHY THIS IS A PLAN AND NOT JUST A LOOP. The MCU's cycle is not a list the host can address.
    /// Measured on 8D87 (BIOS F.07, keyboard MCU 0D62:54BF):
    ///
    /// * <c>Fn+1</c> is next and <c>Fn+2</c> is previous through ONE list. <c>Fn+3</c>..<c>Fn+0</c>
    ///   do nothing to lighting.
    /// * The list holds what the host wrote. An ordinary command <c>0x03</c> effect write is enough
    ///   to put an entry in it; no flash write is needed for the entry to exist.
    /// * There is ONE SLOT PER EFFECT TYPE, holding that effect's last parameters. Writing Wave red
    ///   and then Wave green leaves one Wave entry, green. So this is not an append-only history,
    ///   and staging the same effect twice cannot produce two cycle positions.
    /// * <c>0x0A StoreLightingToFlash</c> does not create or reorder slots. It is what makes the
    ///   lighting survive a power cycle, which is all its name claims.
    ///
    /// WHAT WE CANNOT DO, and why the UI must not imply otherwise: there is no known command to
    /// remove a slot or to reorder the list. Writing a cycle ADDS TO OR UPDATES what the keyboard
    /// already holds — it does not replace it. A profile the user removes from the staged list stays
    /// on the keyboard until something overwrites that effect type.
    /// </summary>
    public static class FnCyclePlan
    {
        /// <summary>Twelve animations, so twelve slots is the ceiling the firmware imposes.
        /// <c>Effect.Off</c> is in the enum but is a command rather than an animation, and never
        /// takes a slot.</summary>
        public const int MaxSlots = 12;

        /// <summary>
        /// The write order for a staged set: duplicates collapsed, active last.
        ///
        /// Deduplication keeps the LAST slot for each effect type because that is what the firmware
        /// would do anyway — writing both would spend a frame installing an entry that the next
        /// frame overwrites, and the user could never recall the first one.
        ///
        /// The active slot goes last because the keyboard displays whatever was written most
        /// recently. Ordering the writes is how "leave this one showing" is expressed; there is no
        /// separate command for it, and appending an extra write would just re-send a frame.
        /// </summary>
        public static IReadOnlyList<FnCycleSlot> Order(IEnumerable<FnCycleSlot>? staged)
        {
            if (staged == null) return Array.Empty<FnCycleSlot>();

            var deduped = new List<FnCycleSlot>();
            foreach (var slot in staged)
            {
                if (slot == null || !TryParseEffect(slot.Effect, out var parsed)) continue;

                // Off blanks the keyboard rather than animating it, so it has no slot to occupy.
                // Unreachable from the UI, which does not list it — but config.json is hand-editable.
                if (parsed == DojoKeyboardMcu.Effect.Off) continue;

                // Last one wins, in place - so removing the earlier duplicate must not shuffle the
                // remaining order, or the cycle a user sees would not match the list they built.
                int existing = deduped.FindIndex(s =>
                    string.Equals(s.Effect, slot.Effect, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0) deduped[existing] = slot;
                else deduped.Add(slot);
            }

            int active = deduped.FindIndex(s => s.IsActive);
            if (active >= 0 && active != deduped.Count - 1)
            {
                var moved = deduped[active];
                deduped.RemoveAt(active);
                deduped.Add(moved);
            }

            return deduped;
        }

        /// <summary>
        /// The wire record for one staged profile.
        ///
        /// Shared with the effects card's Apply rather than duplicated: a profile written into the
        /// cycle must be byte-identical to the same profile applied directly, or "add current
        /// settings" would stage something the user has not actually seen.
        /// </summary>
        public static DojoKeyboardMcu.EffectRecord ToRecord(FnCycleSlot slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));

            TryParseEffect(slot.Effect, out DojoKeyboardMcu.Effect wire);

            var speed = ToSpeed(slot.Speed);

            var colors = slot.UseCustomColors
                ? new[] { ToRgb(slot.PrimaryColorHex), ToRgb(slot.SecondaryColorHex) }
                : Array.Empty<(byte, byte, byte)>();

            return new DojoKeyboardMcu.EffectRecord
            {
                Effect = wire,
                ShowMode = slot.UseCustomColors
                    ? DojoKeyboardMcu.ShowMode.MultipleCustomColors
                    : ToTheme(slot.Theme),

                // Zero-based count when custom, and the preset sentinel otherwise. Sending a count
                // where the sentinel belongs is what makes Swipe render black.
                ColorNumber = slot.UseCustomColors
                    ? (byte)(colors.Length - 1)
                    : DojoKeyboardMcu.ColorNumberPreset,

                Speed = speed,

                // No effect on this firmware consumes the brightness field - measured at 60 and 160
                // through frames the MCU demonstrably accepted, with no change - so it stays at what
                // OGH sends rather than becoming an untested path.
                Brightness = 0,

                Direction = ToDirection(slot.Direction),
                RippleSize = 1,
                RaindropFrequency = (byte)speed,
                Colors = colors
            };
        }

        /// <summary>
        /// Whether a staged set can be written as the user expects, and what to tell them if not.
        /// Empty string means it is fine.
        /// </summary>
        public static string Validate(IReadOnlyList<FnCycleSlot> ordered)
        {
            if (ordered == null || ordered.Count == 0)
                return "Add at least one profile before writing.";

            if (ordered.Count > MaxSlots)
                return $"The keyboard holds one profile per effect type, so at most {MaxSlots}.";

            // Both of these render black by design and would look like a failed write from the
            // user's chair - worth saying before the frames go out, not after.
            var swipe = ordered.FirstOrDefault(s =>
                string.Equals(s.Effect, nameof(DojoKeyboardMcu.Effect.Swipe), StringComparison.OrdinalIgnoreCase)
                && !s.UseCustomColors);

            if (swipe != null)
                return "Swipe renders black with a theme rather than custom colours. Tick custom colours for it, or remove it.";

            var audio = ordered.FirstOrDefault(s =>
                string.Equals(s.Effect, nameof(DojoKeyboardMcu.Effect.AudioPulse), StringComparison.OrdinalIgnoreCase));

            if (audio != null)
                return "Audio pulse is fed live audio levels by the host, so in the Fn cycle it shows a steady colour. It can stay, but it will not pulse without OmenCore running.";

            return string.Empty;
        }

        // ── Parsing ────────────────────────────────────────────────────────────────

        public static bool TryParseEffect(string? name, out DojoKeyboardMcu.Effect effect) =>
            Enum.TryParse(name, ignoreCase: true, out effect) &&
            Enum.IsDefined(typeof(DojoKeyboardMcu.Effect), effect);

        public static DojoKeyboardMcu.EffectSpeed ToSpeed(string? name) => name switch
        {
            "Slow" => DojoKeyboardMcu.EffectSpeed.Slow,
            "Fast" => DojoKeyboardMcu.EffectSpeed.Fast,
            _ => DojoKeyboardMcu.EffectSpeed.Medium
        };

        public static DojoKeyboardMcu.ShowMode ToTheme(string? name) => name switch
        {
            "Jungle" => DojoKeyboardMcu.ShowMode.Jungle,
            "Ocean" => DojoKeyboardMcu.ShowMode.Ocean,
            "Rainbow" => DojoKeyboardMcu.ShowMode.Rainbow,
            _ => DojoKeyboardMcu.ShowMode.Volcano
        };

        public static DojoKeyboardMcu.EffectDirection ToDirection(string? name) => name switch
        {
            "Right to left" => DojoKeyboardMcu.EffectDirection.RightToLeft,
            "Up" => DojoKeyboardMcu.EffectDirection.Up,
            "Down" => DojoKeyboardMcu.EffectDirection.Down,
            "Inward" => DojoKeyboardMcu.EffectDirection.Inward,
            "Outward" => DojoKeyboardMcu.EffectDirection.Outward,
            "Clockwise" => DojoKeyboardMcu.EffectDirection.Clockwise,
            "Anticlockwise" => DojoKeyboardMcu.EffectDirection.CounterClockwise,
            _ => DojoKeyboardMcu.EffectDirection.LeftToRight
        };

        /// <summary>Black for anything unparseable, matching the hex boxes elsewhere in the UI.</summary>
        public static (byte R, byte G, byte B) ToRgb(string? hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return (0, 0, 0);

                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    hex.StartsWith('#') ? hex : "#" + hex);

                return (c.R, c.G, c.B);
            }
            catch
            {
                return (0, 0, 0);
            }
        }
    }
}
