using FluentAssertions;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// The colour swatches beside the effect and light bar hex boxes.
    ///
    /// Worth a test for one reason: the hex boxes bind with UpdateSourceTrigger=PropertyChanged, so
    /// the swatch is asked to render the value after EVERY keystroke. Typing "#FF8800" walks through
    /// "#", "#F", "#FF" and so on, none of which are colours. A converter that throws on those turns
    /// ordinary typing into a stream of binding failures, and the swatch stops tracking the box.
    /// </summary>
    public class DeviceLightingSwatchTests
    {
        [Theory]
        [InlineData("#FF8800", 0xFF, 0x88, 0x00)]
        [InlineData("FF8800", 0xFF, 0x88, 0x00)]  // the leading # is optional in the box
        [InlineData("#000000", 0x00, 0x00, 0x00)]
        public void Parses_a_complete_colour(string hex, byte r, byte g, byte b)
        {
            var brush = DeviceLightingViewModel.BrushFrom(hex);

            brush.Color.R.Should().Be(r);
            brush.Color.G.Should().Be(g);
            brush.Color.B.Should().Be(b);
        }

        [Theory]
        [InlineData("")]
        [InlineData("#")]
        [InlineData("#F")]
        [InlineData("#FF")]
        [InlineData("#FF888")]
        [InlineData("#GGGGGG")]
        [InlineData("not a colour")]
        public void Falls_back_to_black_on_anything_half_typed(string hex)
        {
            // Black rather than an exception. Every one of these is a state the box is legitimately
            // in while someone types a valid colour.
            var act = () => DeviceLightingViewModel.BrushFrom(hex);

            act.Should().NotThrow();
            act().Color.Should().Be(System.Windows.Media.Colors.Black);
        }

        [Fact]
        public void Four_characters_is_a_colour_to_WPF_not_a_half_typed_one()
        {
            // Measured, not assumed: ColorConverter reads 4-char hex as #ARGB shorthand, so "#FF88"
            // is F8/F8/88 at alpha FF rather than a parse failure. It is a real state the box passes
            // through on the way to "#FF8800", and the swatch will briefly show that colour. Not a
            // bug - but worth pinning, because the obvious guess is that it falls back to black.
            var brush = DeviceLightingViewModel.BrushFrom("#FF88");

            brush.Color.Should().NotBe(System.Windows.Media.Colors.Black);
            brush.Color.A.Should().Be(0xFF);
        }
    }
}
