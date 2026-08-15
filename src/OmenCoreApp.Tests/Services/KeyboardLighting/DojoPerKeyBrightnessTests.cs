using FluentAssertions;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Services.KeyboardLighting
{
    /// <summary>
    /// Pins the host-side brightness scaling for the OMEN MAX per-key colour map.
    ///
    /// Worth testing because the bug this replaced was invisible to every other check. The slider
    /// moved, the view-model stored the value, the backend stored the value, the write returned
    /// true and the keyboard stayed at full brightness — the mi_03 colour map is raw RGB with no
    /// intensity channel, and nothing on that path ever read the setting. Only the mi_04 fallback
    /// did, and that path is for boards this one is not. So the arithmetic is the last place the
    /// behaviour can be checked automatically: past here it is HID feature reports and a person
    /// looking at a keyboard.
    ///
    /// The device end cannot be tested at all. <see cref="OmenCore.Hardware.DojoKeyboardMcu"/> is
    /// sealed over a raw handle, and there is no colour readback on either interface.
    /// </summary>
    public class DojoPerKeyBrightnessTests
    {
        [Fact]
        public void FullIntensity_IsExactlyTransparent()
        {
            // The common case, and the one a rounding error would quietly ruin: at 100% every
            // channel must survive byte-for-byte, or every colour the user picks is slightly wrong.
            for (int channel = 0; channel <= 255; channel++)
                DojoPerKeyBackend.Scale((byte)channel, 255).Should().Be((byte)channel,
                    "brightness 100 must not alter the picture at all");
        }

        [Fact]
        public void ZeroIntensity_IsBlack()
        {
            DojoPerKeyBackend.Scale(255, 0).Should().Be(0);
            DojoPerKeyBackend.Scale(1, 0).Should().Be(0);
        }

        [Theory]
        [InlineData(255, 128, 128)] // half of full is half, not 127 - truncation would lose a step
        [InlineData(255, 51, 51)]   // slider 20 on a full channel
        [InlineData(128, 128, 64)]
        [InlineData(0, 255, 0)]
        public void ScalesLinearly_RoundingToNearest(int channel, int intensity, int expected)
        {
            DojoPerKeyBackend.Scale((byte)channel, (byte)intensity).Should().Be((byte)expected);
        }

        [Fact]
        public void NeverOverflowsTheChannel()
        {
            // channel * intensity is 65025 at the top end, which does not fit a byte. The cast is
            // only safe because of the divide, and that is the kind of thing a refactor drops.
            for (int channel = 0; channel <= 255; channel++)
                for (int intensity = 0; intensity <= 255; intensity += 17)
                    DojoPerKeyBackend.Scale((byte)channel, (byte)intensity)
                        .Should().BeLessThanOrEqualTo((byte)channel,
                            "scaling can only ever darken a channel");
        }

        [Fact]
        public void IsMonotonic_SoEverySliderStepIsAtLeastNotBackwards()
        {
            // A user dragging the slider expects the keyboard to move one way. Round-to-nearest
            // makes that easy to get wrong at the boundaries.
            byte previous = 0;
            for (int intensity = 0; intensity <= 255; intensity++)
            {
                byte current = DojoPerKeyBackend.Scale(200, (byte)intensity);
                current.Should().BeGreaterThanOrEqualTo(previous,
                    $"raising brightness to {intensity} must not darken the keyboard");
                previous = current;
            }
        }
    }
}
