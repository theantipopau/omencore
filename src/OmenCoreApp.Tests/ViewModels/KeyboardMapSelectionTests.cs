using System.Linq;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Rubber-band selection maths.
    ///
    /// Worth testing precisely because it cannot fail loudly: an off-by-one in a bounds check does
    /// not throw, it selects the key next to the one the user dragged over, and the only symptom is
    /// the wrong key lighting up on a keyboard nobody can read back.
    ///
    /// The view-model is exercised with no keyboard service, which is the supported way to construct
    /// it - a machine with no per-key backend gets an empty map and hides the editor.
    /// </summary>
    public class KeyboardMapSelectionTests
    {
        /// <summary>Three keys in a row, 100 wide, at x = 0, 200 and 400.</summary>
        private static KeyboardMapViewModel ThreeKeysInARow()
        {
            var vm = new KeyboardMapViewModel(null, new OmenCore.Services.LoggingService());

            for (int i = 0; i < 3; i++)
            {
                vm.Keys.Add(new KeyLampViewModel
                {
                    LampIds = new ushort[] { (ushort)i },
                    Label = i.ToString(),
                    X = i * 200,
                    Y = 0,
                    Width = 100,
                    Height = 100
                });
            }

            return vm;
        }

        [Fact]
        public void Band_across_all_three_selects_all_three()
        {
            var vm = ThreeKeysInARow();

            vm.SelectWithin(-10, -10, 510, 110, additive: false);

            Assert.Equal(3, vm.SelectedCount);
        }

        [Fact]
        public void Band_over_the_first_key_only_leaves_the_others_alone()
        {
            var vm = ThreeKeysInARow();

            vm.SelectWithin(10, 10, 90, 90, additive: false);

            Assert.True(vm.Keys[0].IsSelected);
            Assert.False(vm.Keys[1].IsSelected);
            Assert.False(vm.Keys[2].IsSelected);
        }

        [Fact]
        public void A_band_that_merely_clips_a_key_still_catches_it()
        {
            var vm = ThreeKeysInARow();

            // Ends one pixel inside the second key. Selecting only keys wholly enclosed is the
            // behaviour users read as "the drag missed some".
            vm.SelectWithin(0, 0, 201, 50, additive: false);

            Assert.True(vm.Keys[0].IsSelected);
            Assert.True(vm.Keys[1].IsSelected);
            Assert.False(vm.Keys[2].IsSelected);
        }

        [Fact]
        public void A_band_touching_nothing_deselects_when_not_additive()
        {
            var vm = ThreeKeysInARow();
            vm.SelectWithin(-10, -10, 510, 110, additive: false);

            vm.SelectWithin(120, 0, 180, 50, additive: false); // the gap between keys 0 and 1

            Assert.Equal(0, vm.SelectedCount);
        }

        [Fact]
        public void Additive_bands_accumulate_across_separate_regions()
        {
            var vm = ThreeKeysInARow();

            vm.SelectWithin(10, 10, 90, 90, additive: false);   // first key
            vm.SelectWithin(410, 10, 490, 90, additive: true);  // third key, holding a modifier

            Assert.True(vm.Keys[0].IsSelected);
            Assert.False(vm.Keys[1].IsSelected);
            Assert.True(vm.Keys[2].IsSelected);
        }

        [Fact]
        public void A_band_dragged_up_and_left_selects_the_same_keys_as_down_and_right()
        {
            var downRight = ThreeKeysInARow();
            var upLeft = ThreeKeysInARow();

            downRight.SelectWithin(10, 10, 290, 90, additive: false);
            upLeft.SelectWithin(290, 90, 10, 10, additive: false);

            Assert.Equal(
                downRight.Keys.Select(k => k.IsSelected),
                upLeft.Keys.Select(k => k.IsSelected));
        }

        [Fact]
        public void Toggling_a_key_selects_then_deselects_it()
        {
            var vm = ThreeKeysInARow();

            vm.ToggleKey(vm.Keys[1]);
            Assert.True(vm.Keys[1].IsSelected);
            Assert.Equal(1, vm.SelectedCount);

            vm.ToggleKey(vm.Keys[1]);
            Assert.False(vm.Keys[1].IsSelected);
            Assert.Equal(0, vm.SelectedCount);
        }

        [Fact]
        public void No_keyboard_service_means_no_map_and_a_hidden_editor()
        {
            var vm = new KeyboardMapViewModel(null, new OmenCore.Services.LoggingService());

            Assert.False(vm.IsAvailable);
            Assert.Empty(vm.Keys);
        }

        [Theory]
        [InlineData(0x04, "A")]
        [InlineData(0x1D, "Z")]
        [InlineData(0x1E, "1")]
        [InlineData(0x27, "0")]
        [InlineData(0x29, "Esc")]
        [InlineData(0x2C, "Space")]
        [InlineData(0x3A, "F1")]
        [InlineData(0x45, "F12")]
        [InlineData(0xE1, "Shift")]
        public void Key_usages_map_to_readable_names(byte usage, string expected)
        {
            Assert.Equal(expected, KeyboardMapViewModel.KeyName(usage));
        }
    }
}
