using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Services.KeyboardLighting
{
    /// <summary>
    /// The write plan for the keyboard's Fn+1 / Fn+2 cycle.
    ///
    /// This is where the firmware's rules live, and they are not guessable from the UI:
    ///
    ///   * ONE SLOT PER EFFECT TYPE. Writing Wave twice leaves one Wave entry, so two staged Waves
    ///     could never become two cycle positions.
    ///   * THE LAST WRITE IS WHAT THE KEYBOARD SHOWS. "Leave this one displayed" is expressed by
    ///     ordering the writes, because the firmware has no command for it.
    ///
    /// Both were measured on 8D87 rather than inferred. A plan that violated either would produce a
    /// cycle that does not match the list the user built, and nothing in the app could detect it —
    /// the MCU will not enumerate its own list.
    /// </summary>
    public class FnCyclePlanTests
    {
        private static FnCycleSlot Slot(
            DojoKeyboardMcu.Effect effect, string primary = "#FF0000", bool active = false, bool custom = true) =>
            new()
            {
                Effect = effect.ToString(),
                DisplayName = effect.ToString(),
                UseCustomColors = custom,
                PrimaryColorHex = primary,
                SecondaryColorHex = "#0000FF",
                Theme = "Volcano",
                Speed = "Medium",
                Direction = "Left to right",
                IsActive = active
            };

        // ── Ordering ───────────────────────────────────────────────────────────────

        [Fact]
        public void Nothing_staged_plans_no_writes()
        {
            FnCyclePlan.Order(null).Should().BeEmpty();
            FnCyclePlan.Order(Array.Empty<FnCycleSlot>()).Should().BeEmpty();
        }

        [Fact]
        public void Order_is_kept_when_there_is_nothing_to_reorder()
        {
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave),
                Slot(DojoKeyboardMcu.Effect.Ripple),
                Slot(DojoKeyboardMcu.Effect.Starlight)
            };

            FnCyclePlan.Order(staged).Select(s => s.Effect)
                .Should().Equal("Wave", "Ripple", "Starlight");
        }

        [Fact]
        public void The_same_effect_twice_collapses_to_the_later_one()
        {
            // The firmware would do this anyway: the second Wave overwrites the first Wave's slot.
            // Planning both wastes a frame installing an entry nothing could ever recall.
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave, "#FF0000"),
                Slot(DojoKeyboardMcu.Effect.Ripple),
                Slot(DojoKeyboardMcu.Effect.Wave, "#00FF00")
            };

            var plan = FnCyclePlan.Order(staged);

            plan.Should().HaveCount(2);
            plan.Single(s => s.Effect == "Wave").PrimaryColorHex.Should().Be("#00FF00");
        }

        [Fact]
        public void Collapsing_a_duplicate_does_not_shuffle_the_rest()
        {
            // The survivor takes the FIRST position, not the second. If it moved, removing a
            // duplicate would silently reorder the cycle a user had already learned.
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave, "#FF0000"),
                Slot(DojoKeyboardMcu.Effect.Ripple),
                Slot(DojoKeyboardMcu.Effect.Starlight),
                Slot(DojoKeyboardMcu.Effect.Wave, "#00FF00")
            };

            FnCyclePlan.Order(staged).Select(s => s.Effect)
                .Should().Equal("Wave", "Ripple", "Starlight");
        }

        [Fact]
        public void The_active_profile_is_written_last_so_the_keyboard_is_left_showing_it()
        {
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave, active: true),
                Slot(DojoKeyboardMcu.Effect.Ripple),
                Slot(DojoKeyboardMcu.Effect.Starlight)
            };

            FnCyclePlan.Order(staged).Last().Effect.Should().Be("Wave");
        }

        [Fact]
        public void An_active_profile_already_last_is_not_rewritten()
        {
            // Guards against "move to end" being implemented as "append", which would send the
            // frame twice for no gain.
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave),
                Slot(DojoKeyboardMcu.Effect.Ripple, active: true)
            };

            var plan = FnCyclePlan.Order(staged);

            plan.Should().HaveCount(2);
            plan.Last().Effect.Should().Be("Ripple");
        }

        [Fact]
        public void Moving_the_active_one_to_the_end_keeps_the_others_in_order()
        {
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave, active: true),
                Slot(DojoKeyboardMcu.Effect.Ripple),
                Slot(DojoKeyboardMcu.Effect.Starlight),
                Slot(DojoKeyboardMcu.Effect.Ghosting)
            };

            FnCyclePlan.Order(staged).Select(s => s.Effect)
                .Should().Equal("Ripple", "Starlight", "Ghosting", "Wave");
        }

        [Fact]
        public void An_effect_name_this_firmware_does_not_have_is_dropped_rather_than_sent()
        {
            // config.json is user-editable and survives upgrades, so an unknown name is a state
            // that reaches this code in the field. Sending it would put an arbitrary byte in the
            // effect field of a frame the MCU is about to act on.
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave),
                new FnCycleSlot { Effect = "Hyperbeam", DisplayName = "Hyperbeam" }
            };

            FnCyclePlan.Order(staged).Select(s => s.Effect).Should().Equal("Wave");
        }

        [Fact]
        public void Effect_names_round_trip_case_insensitively()
        {
            // Hand-edited config, or a name written by an older build.
            var staged = new[]
            {
                new FnCycleSlot { Effect = "wave", DisplayName = "Wave" },
                new FnCycleSlot { Effect = "WAVE", DisplayName = "Wave", PrimaryColorHex = "#00FF00" }
            };

            FnCyclePlan.Order(staged).Should().HaveCount(1);
        }

        // ── The record on the wire ─────────────────────────────────────────────────

        [Fact]
        public void Custom_colours_send_a_zero_based_count_not_a_count()
        {
            var record = FnCyclePlan.ToRecord(Slot(DojoKeyboardMcu.Effect.Wave));

            record.ShowMode.Should().Be(DojoKeyboardMcu.ShowMode.MultipleCustomColors);
            record.ColorNumber.Should().Be(1, "two colours are sent as 1");
            record.Colors.Should().HaveCount(2);
            record.Colors[0].Should().Be(((byte)0xFF, (byte)0x00, (byte)0x00));
        }

        [Fact]
        public void A_preset_palette_sends_the_sentinel_and_the_theme()
        {
            var slot = Slot(DojoKeyboardMcu.Effect.Ripple, custom: false);
            slot.Theme = "Ocean";

            var record = FnCyclePlan.ToRecord(slot);

            record.ColorNumber.Should().Be(DojoKeyboardMcu.ColorNumberPreset);
            record.ShowMode.Should().Be(DojoKeyboardMcu.ShowMode.Ocean);
            record.Colors.Should().BeEmpty();
        }

        [Fact]
        public void Brightness_is_left_at_zero_because_no_effect_on_this_firmware_reads_it()
        {
            // Measured, not assumed: 60 and 160 were both sent in frames the MCU demonstrably
            // consumed - effect, colour and speed all took - and the readback stayed at 200 with no
            // visible change. Sending a value here is an unexercised path with nothing to gain.
            FnCyclePlan.ToRecord(Slot(DojoKeyboardMcu.Effect.Wave)).Brightness.Should().Be(0);
        }

        [Theory]
        [InlineData("Slow", DojoKeyboardMcu.EffectSpeed.Slow)]
        [InlineData("Medium", DojoKeyboardMcu.EffectSpeed.Medium)]
        [InlineData("Fast", DojoKeyboardMcu.EffectSpeed.Fast)]
        [InlineData("nonsense", DojoKeyboardMcu.EffectSpeed.Medium)]
        public void Speed_falls_back_to_medium(string name, DojoKeyboardMcu.EffectSpeed expected)
        {
            FnCyclePlan.ToSpeed(name).Should().Be(expected);
        }

        [Fact]
        public void Raindrop_frequency_tracks_speed_the_way_HPs_own_app_sends_it()
        {
            var slot = Slot(DojoKeyboardMcu.Effect.Raindrop);
            slot.Speed = "Fast";

            var record = FnCyclePlan.ToRecord(slot);

            record.RaindropFrequency.Should().Be((byte)DojoKeyboardMcu.EffectSpeed.Fast);
        }

        [Theory]
        [InlineData("Right to left", DojoKeyboardMcu.EffectDirection.RightToLeft)]
        [InlineData("Clockwise", DojoKeyboardMcu.EffectDirection.Clockwise)]
        [InlineData("Anticlockwise", DojoKeyboardMcu.EffectDirection.CounterClockwise)]
        [InlineData("", DojoKeyboardMcu.EffectDirection.LeftToRight)]
        public void Direction_names_map_to_the_wire_enum(string name, DojoKeyboardMcu.EffectDirection expected)
        {
            FnCyclePlan.ToDirection(name).Should().Be(expected);
        }

        [Theory]
        [InlineData("#FF8800", 0xFF, 0x88, 0x00)]
        [InlineData("FF8800", 0xFF, 0x88, 0x00)]
        [InlineData("", 0, 0, 0)]
        [InlineData("not a colour", 0, 0, 0)]
        public void Unparseable_colours_become_black_rather_than_throwing(string hex, byte r, byte g, byte b)
        {
            FnCyclePlan.ToRgb(hex).Should().Be((r, g, b));
        }

        // ── Warnings ───────────────────────────────────────────────────────────────

        [Fact]
        public void An_empty_plan_says_so()
        {
            FnCyclePlan.Validate(Array.Empty<FnCycleSlot>()).Should().NotBeEmpty();
        }

        [Fact]
        public void Swipe_with_a_preset_is_flagged_before_the_frames_go_out()
        {
            // Swipe has no preset palette on this firmware and renders BLACK with one selected.
            // In the cycle that is worse than on the effects card: the user finds it days later by
            // pressing Fn+1 into a dead keyboard, with no obvious cause.
            var plan = new[] { Slot(DojoKeyboardMcu.Effect.Swipe, custom: false) };

            FnCyclePlan.Validate(plan).Should().Contain("Swipe");
        }

        [Fact]
        public void Swipe_with_custom_colours_is_fine()
        {
            var plan = new[] { Slot(DojoKeyboardMcu.Effect.Swipe, custom: true) };

            FnCyclePlan.Validate(plan).Should().BeEmpty();
        }

        [Fact]
        public void Audio_pulse_is_allowed_but_explained()
        {
            // It IS its two host-fed level bytes, so in a cycle recalled with OmenCore closed it
            // shows a steady colour. Not an error - but not what "audio pulse" promises either.
            var plan = new[] { Slot(DojoKeyboardMcu.Effect.AudioPulse) };

            FnCyclePlan.Validate(plan).Should().Contain("Audio pulse");
        }

        [Fact]
        public void A_plain_plan_has_nothing_to_say()
        {
            var plan = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Wave),
                Slot(DojoKeyboardMcu.Effect.Ripple, custom: false)
            };

            FnCyclePlan.Validate(plan).Should().BeEmpty();
        }

        [Fact]
        public void The_cap_is_one_slot_per_animation()
        {
            // Off is in the enum but blanks the keyboard rather than animating it, so it is not one
            // of the twelve and never occupies a slot.
            int animations = Enum.GetValues<DojoKeyboardMcu.Effect>()
                .Count(e => e != DojoKeyboardMcu.Effect.Off);

            FnCyclePlan.MaxSlots.Should().Be(animations);
        }

        [Fact]
        public void Off_is_not_a_profile_and_is_dropped_from_a_plan()
        {
            // Reachable only from a hand-edited config.json, but it would blank the keyboard in the
            // middle of writing a cycle, which looks exactly like the write having failed.
            var staged = new[]
            {
                Slot(DojoKeyboardMcu.Effect.Off),
                Slot(DojoKeyboardMcu.Effect.Wave)
            };

            FnCyclePlan.Order(staged).Select(s => s.Effect).Should().Equal("Wave");
        }
    }
}
