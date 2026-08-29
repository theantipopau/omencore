using FluentAssertions;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Services.KeyboardLighting
{
    /// <summary>
    /// Pins when OmenCore warns that Windows will repaint the keyboard.
    ///
    /// The decision is two booleans wide and still worth pinning, because both directions are
    /// expensive to get wrong and in opposite ways. Warn when Windows is not going to touch the
    /// device and the banner is noise that trains users to ignore it. Fail to warn when it is, and
    /// the user is left with a feature that appears to work perfectly until they close the app —
    /// the exact report this was written for.
    ///
    /// <see cref="DynamicLightingState.Read"/> itself is not tested here. It reads the current
    /// user's registry, so a test of it would assert something about the machine running the suite
    /// rather than about this code; it is written to swallow everything and report "nothing to warn
    /// about", which is the safe direction for a diagnostic.
    /// </summary>
    public class DynamicLightingStateTests
    {
        [Fact]
        public void WarnsWhenBothTheMasterAndTheDeviceAreOn()
        {
            new DynamicLightingState { GlobalEnabled = true, DeviceFound = true, DeviceEnabled = true }
                .WillRepaintWhenReleased.Should().BeTrue();
        }

        [Fact]
        public void SaysNothingWhenTheMasterSwitchIsOff()
        {
            // The measured case. With this off on 8D87 a per-key picture survived closing the app;
            // with it on, the same picture did not.
            new DynamicLightingState { GlobalEnabled = false, DeviceFound = true, DeviceEnabled = true }
                .WillRepaintWhenReleased.Should().BeFalse();
        }

        [Fact]
        public void SaysNothingWhenTheDeviceItselfIsOptedOut()
        {
            // The per-device card is treated as a veto rather than as something that tracks the
            // master, because it was only ever read in one state and inferring the other would be
            // guessing at another feature's semantics.
            new DynamicLightingState { GlobalEnabled = true, DeviceFound = true, DeviceEnabled = false }
                .WillRepaintWhenReleased.Should().BeFalse();
        }

        [Fact]
        public void AbsentDeviceCard_CountsAsEnabled()
        {
            // A device Windows has not written a card for is still driven by Dynamic Lighting when
            // the master is on. Absence is not opt-out, and defaulting the other way would silence
            // the warning on exactly the machines that have never opened the settings page.
            new DynamicLightingState { GlobalEnabled = true, DeviceFound = false }
                .WillRepaintWhenReleased.Should().BeTrue();
        }

        [Fact]
        public void DefaultState_WarnsAboutNothing()
        {
            // What the catch block returns. A diagnostic that failed to read must not manufacture
            // a warning out of its own failure.
            new DynamicLightingState().WillRepaintWhenReleased.Should().BeFalse();
        }

        [Fact]
        public void ForegroundAppSetting_DoesNotAffectTheWarning()
        {
            // Recorded but deliberately not acted on: it arbitrates between two RUNNING apps, and
            // the case being warned about is what happens after this one exits.
            var withIt = new DynamicLightingState
            {
                GlobalEnabled = true, DeviceFound = true, DeviceEnabled = true,
                ControlledByForegroundApp = true
            };
            var without = new DynamicLightingState
            {
                GlobalEnabled = true, DeviceFound = true, DeviceEnabled = true,
                ControlledByForegroundApp = false
            };

            withIt.WillRepaintWhenReleased.Should().Be(without.WillRepaintWhenReleased);
        }
    }
}
