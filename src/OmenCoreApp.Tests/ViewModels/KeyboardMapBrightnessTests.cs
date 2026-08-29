using System.Linq;
using FluentAssertions;
using OmenCore.Services.KeyboardLighting;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// The two halves of per-key brightness: a master over the whole picture, and a level per cell.
    ///
    /// The thing worth pinning is that they stay SEPARATE from the painted colour. Brightness that
    /// writes back into the colour it scales is lossy and one-way — drag a key to 10% and back to
    /// 100% and the colour the user picked is gone. That failure is invisible in a screenshot and
    /// obvious three edits later, which is exactly the kind that needs a test rather than an eye.
    ///
    /// The view-model is exercised with no keyboard service, the supported way to construct it.
    /// </summary>
    public class KeyboardMapBrightnessTests
    {
        private static KeyboardMapViewModel WithCells(int count)
        {
            var vm = new KeyboardMapViewModel(null, new OmenCore.Services.LoggingService());

            for (int i = 0; i < count; i++)
            {
                vm.Keys.Add(new KeyLampViewModel
                {
                    LedPositions = new[] { i },
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
        public void Cells_start_at_full_so_nothing_changes_for_a_user_who_never_touches_it()
        {
            var vm = WithCells(3);
            vm.Keys.Should().OnlyContain(k => k.Level == 100);
        }

        [Fact]
        public void Dimming_the_selection_leaves_unselected_cells_alone()
        {
            var vm = WithCells(3);
            vm.Keys[1].IsSelected = true;

            vm.SelectionBrightness = 40;

            vm.Keys[0].Level.Should().Be(100);
            vm.Keys[1].Level.Should().Be(40);
            vm.Keys[2].Level.Should().Be(100);
        }

        [Fact]
        public void Dimming_never_touches_the_painted_colour()
        {
            // The whole reason Level is a separate field. If this ever starts failing, someone has
            // "simplified" the level away by baking it into the hex, and the round trip below is
            // the damage that does.
            var vm = WithCells(1);
            vm.Keys[0].ColorHex = "#3366FF";
            vm.Keys[0].IsSelected = true;

            vm.SelectionBrightness = 5;
            vm.SelectionBrightness = 100;

            vm.Keys[0].ColorHex.Should().Be("#3366FF", "the picked colour is the user's, not a scratch buffer");
        }

        [Fact]
        public void Selection_slider_reads_back_the_level_the_selection_agrees_on()
        {
            var vm = WithCells(3);
            foreach (var key in vm.Keys) key.IsSelected = true;

            vm.SelectionBrightness = 60;

            vm.SelectionBrightness.Should().Be(60);
        }

        [Fact]
        public void A_mixed_selection_reports_full_rather_than_one_members_value()
        {
            // Showing one key's 20 while eleven others sit at 100 would make the slider lie about
            // the selection. The neutral end is the only position that is not a claim.
            var vm = WithCells(3);
            vm.Keys[0].IsSelected = true;
            vm.SelectionBrightness = 20;

            vm.Keys[1].IsSelected = true;

            vm.SelectionBrightness.Should().Be(100);
        }

        [Fact]
        public void Dimming_with_nothing_selected_does_nothing_at_all()
        {
            var vm = WithCells(3);

            vm.SelectionBrightness = 10;

            vm.Keys.Should().OnlyContain(k => k.Level == 100);
            vm.HasSelection.Should().BeFalse();
        }

        [Theory]
        [InlineData(100)]
        [InlineData(50)]
        [InlineData(1)]
        [InlineData(0)]
        public void The_cap_preview_scales_by_exactly_what_the_backend_would_send(int level)
        {
            // The editor is a preview of the keyboard. If the view's rounding and the backend's
            // rounding disagree, the cap and the key differ by a step and nobody can tell which is
            // right — there is no colour readback on this hardware to settle it.
            var cell = new KeyLampViewModel { LedPositions = new[] { 0 }, ColorHex = "#C86432", Level = level };

            byte intensity = (byte)(level * 255 / 100);
            var expected = System.Windows.Media.Color.FromRgb(
                DojoPerKeyBackend.Scale(0xC8, intensity),
                DojoPerKeyBackend.Scale(0x64, intensity),
                DojoPerKeyBackend.Scale(0x32, intensity));

            cell.KeyBrush.Color.Should().Be(expected);
        }

        [Fact]
        public void Full_level_passes_the_colour_through_untouched()
        {
            var cell = new KeyLampViewModel { LedPositions = new[] { 0 }, ColorHex = "#FF8000", Level = 100 };

            cell.KeyBrush.Color.R.Should().Be(0xFF);
            cell.KeyBrush.Color.G.Should().Be(0x80);
            cell.KeyBrush.Color.B.Should().Be(0x00);
        }

        [Fact]
        public void Level_is_clamped_rather_than_trusted()
        {
            var cell = new KeyLampViewModel { LedPositions = new[] { 0 } };

            cell.Level = 500;
            cell.Level.Should().Be(100);

            cell.Level = -20;
            cell.Level.Should().Be(0);
        }

        [Fact]
        public void The_hint_states_the_product_of_the_two_sliders()
        {
            // Two numbers side by side do not tell a user what they multiply to, and this hint is
            // the only place the arithmetic is spelled out.
            var vm = WithCells(2);
            vm.Brightness = 50;
            vm.Keys[0].IsSelected = true;
            vm.SelectionBrightness = 40;

            vm.BrightnessHint.Should().Contain("20%", "50% of 40% is 20%");
        }
    }
}
