using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Staging profiles for the keyboard's Fn+1 / Fn+2 cycle.
    ///
    /// The list in the UI is NOT a reading of the keyboard — the MCU answers "what is showing now"
    /// and will not enumerate its cycle. So this list is the only account anyone has of what was
    /// curated, and every rule it follows has to match the firmware's rather than the other way
    /// round. The one that bites: there is one slot per effect type, so the list must never show two
    /// entries of the same effect as though they were two cycle positions.
    ///
    /// Constructed with no keyboard service and no config service, the supported way.
    /// </summary>
    public class DeviceLightingFnCycleTests
    {
        private static DeviceLightingViewModel New() =>
            new(null, new LoggingService());

        private static void Stage(DeviceLightingViewModel vm, DojoKeyboardMcu.Effect effect, string hex)
        {
            vm.SelectedEffect = vm.Effects.First(e => e.Wire == effect);
            vm.UseCustomColors = true;
            vm.PrimaryColorHex = hex;
            vm.StageCurrentEffectCommand.Execute(null);
        }

        [Fact]
        public void Starts_empty_and_says_so()
        {
            var vm = New();

            vm.FnCycleSlots.Should().BeEmpty();
            vm.HasFnCycleSlots.Should().BeFalse();
            vm.FnCycleEmptyHint.Should().NotBeEmpty();
        }

        [Fact]
        public void Staging_captures_what_the_effects_card_is_showing()
        {
            var vm = New();
            vm.Speed = "Fast";
            vm.Direction = "Clockwise";
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#123456");

            var slot = vm.FnCycleSlots.Single().Model;

            slot.Effect.Should().Be("Wave");
            slot.PrimaryColorHex.Should().Be("#123456");
            slot.Speed.Should().Be("Fast");
            slot.Direction.Should().Be("Clockwise");
        }

        [Fact]
        public void The_first_profile_staged_is_the_one_left_showing()
        {
            // Otherwise a user who stages one profile and writes gets whatever the plan happened to
            // order last, which for one profile is the same thing but for two is a surprise.
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");

            vm.FnCycleSlots.Single().IsActive.Should().BeTrue();
        }

        [Fact]
        public void Staging_the_same_effect_again_updates_it_rather_than_adding_a_second()
        {
            // The firmware has one slot per effect type. A list showing two Waves would promise a
            // cycle position that cannot exist.
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#00FF00");

            vm.FnCycleSlots.Should().HaveCount(1);
            vm.FnCycleSlots.Single().Model.PrimaryColorHex.Should().Be("#00FF00");
        }

        [Fact]
        public void Updating_a_profile_in_place_does_not_steal_or_drop_active()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");     // active
            Stage(vm, DojoKeyboardMcu.Effect.Ripple, "#00FF00");
            Stage(vm, DojoKeyboardMcu.Effect.Ripple, "#0000FF");   // update, not a new slot

            vm.FnCycleSlots.Should().HaveCount(2);
            vm.FnCycleSlots.Count(s => s.IsActive).Should().Be(1);
            vm.FnCycleSlots.First(s => s.Model.Effect == "Wave").IsActive.Should().BeTrue();
        }

        [Fact]
        public void Re_staging_the_active_profile_keeps_it_active()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#00FF00");

            vm.FnCycleSlots.Single().IsActive.Should().BeTrue();
        }

        [Fact]
        public void Exactly_one_profile_is_active_at_a_time()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");
            Stage(vm, DojoKeyboardMcu.Effect.Ripple, "#00FF00");
            Stage(vm, DojoKeyboardMcu.Effect.Starlight, "#0000FF");

            vm.MakeFnSlotActiveCommand.Execute(vm.FnCycleSlots[2]);

            vm.FnCycleSlots.Count(s => s.IsActive).Should().Be(1);
            vm.FnCycleSlots[2].IsActive.Should().BeTrue();
        }

        [Fact]
        public void Removing_the_active_profile_hands_active_to_another()
        {
            // A list with entries but no active profile would write a cycle that ends on whichever
            // slot happened to be last - silently ignoring the user's choice.
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");     // active
            Stage(vm, DojoKeyboardMcu.Effect.Ripple, "#00FF00");

            vm.RemoveFnSlotCommand.Execute(vm.FnCycleSlots[0]);

            vm.FnCycleSlots.Should().HaveCount(1);
            vm.FnCycleSlots.Count(s => s.IsActive).Should().Be(1);
        }

        [Fact]
        public void Removing_the_last_profile_leaves_an_empty_list_not_a_crash()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");

            vm.RemoveFnSlotCommand.Execute(vm.FnCycleSlots[0]);

            vm.FnCycleSlots.Should().BeEmpty();
            vm.HasFnCycleSlots.Should().BeFalse();
            vm.FnCycleEmptyHint.Should().NotBeEmpty();
        }

        [Fact]
        public void Removing_says_the_profile_is_still_on_the_keyboard()
        {
            // The single thing about this feature a user cannot discover by trying it. Removal is
            // host-side only; there is no firmware command to take a slot back.
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");

            vm.RemoveFnSlotCommand.Execute(vm.FnCycleSlots[0]);

            vm.FnCycleStatus.Should().Contain("still on the keyboard");
        }

        [Fact]
        public void The_limitation_is_stated_in_the_card_rather_than_only_on_removal()
        {
            New().FnCycleLimitation.Should().Contain("no command to remove");
        }

        [Fact]
        public void Removing_something_not_in_the_list_does_nothing()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");
            var stray = new DeviceLightingViewModel.FnCycleSlotViewModel(new OmenCore.Models.FnCycleSlot());

            vm.RemoveFnSlotCommand.Execute(stray);
            vm.RemoveFnSlotCommand.Execute(null);

            vm.FnCycleSlots.Should().HaveCount(1);
        }

        [Fact]
        public void A_staged_swipe_with_a_theme_is_warned_about_in_the_card()
        {
            var vm = New();
            vm.SelectedEffect = vm.Effects.First(e => e.Wire == DojoKeyboardMcu.Effect.Swipe);
            vm.UseCustomColors = false;
            vm.StageCurrentEffectCommand.Execute(null);

            vm.FnCycleAdvice.Should().Contain("Swipe");
        }

        [Fact]
        public void An_empty_list_has_no_advice_to_give()
        {
            // Validate() reports "add something first" for an empty plan, which is right for a write
            // attempt and wrong as a standing warning on an untouched card.
            New().FnCycleAdvice.Should().BeEmpty();
        }

        [Fact]
        public void Writing_is_not_offered_without_a_keyboard()
        {
            var vm = New();
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#FF0000");

            vm.WriteFnCycleCommand.CanExecute(null).Should().BeFalse();
        }

        [Fact]
        public void The_summary_reads_the_way_the_effects_card_is_set()
        {
            var vm = New();
            vm.Speed = "Slow";
            vm.Direction = "Up";
            Stage(vm, DojoKeyboardMcu.Effect.Wave, "#AABBCC");

            vm.FnCycleSlots.Single().Summary.Should().Contain("#AABBCC").And.Contain("slow").And.Contain("up");
        }

        [Fact]
        public void A_preset_profile_summarises_by_theme_rather_than_a_colour_it_will_not_use()
        {
            var vm = New();
            vm.SelectedEffect = vm.Effects.First(e => e.Wire == DojoKeyboardMcu.Effect.Ripple);
            vm.SelectedTheme = vm.Themes.First(t => t.Name == "Ocean");
            vm.UseCustomColors = false;
            vm.StageCurrentEffectCommand.Execute(null);

            var summary = vm.FnCycleSlots.Single().Summary;

            summary.Should().Contain("Ocean");
            summary.Should().NotContain("#");
        }
    }
}
