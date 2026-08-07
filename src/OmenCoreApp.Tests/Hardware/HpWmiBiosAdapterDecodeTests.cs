using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Decode tests for the <c>Legacy 0x0F</c> adapter reply.
    ///
    /// The three 4-byte payloads used here are real captures from an OMEN MAX 16 (board 8D87,
    /// BIOS F.07) taken with three physical adapters plugged into the same machine. They are the
    /// reason this decode can be trusted: the wattage byte was checked against the printed rating on
    /// three different supplies, not inferred from one.
    /// </summary>
    public class HpWmiBiosAdapterDecodeTests
    {
        [Fact]
        public void DecodeAdapterData_Decodes_330W_Adapter_Capture()
        {
            // Measured: 330 W supply. 0x42 * 5 = 330.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x01, 0xC2, 0x00, 0x42 });

            decoded.Should().NotBeNull();
            decoded!.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.MeetsRequirement);
            decoded.Value.PowerRatingWatts.Should().Be(330);
            decoded.Value.PowerRatingKnown.Should().BeTrue();
            decoded.Value.UsbcDesignRatingWatts.Should().Be(0);
            decoded.Value.SupportsBarrelConnector.Should().BeTrue();
            decoded.Value.IsLowWattage.Should().BeFalse();
        }

        [Fact]
        public void DecodeAdapterData_Decodes_280W_Adapter_Capture()
        {
            // Measured: 280 W supply. 0x38 * 5 = 280, and the firmware calls it below requirement.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x02, 0xC2, 0x00, 0x38 });

            decoded.Should().NotBeNull();
            decoded!.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.BelowRequirement);
            decoded.Value.PowerRatingWatts.Should().Be(280);
            decoded.Value.IsLowWattage.Should().BeTrue();
        }

        [Fact]
        public void DecodeAdapterData_Decodes_200W_Adapter_Capture()
        {
            // Measured: 200 W supply. 0x28 * 5 = 200.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x02, 0xC2, 0x00, 0x28 });

            decoded.Should().NotBeNull();
            decoded!.Value.PowerRatingWatts.Should().Be(200);
            decoded.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.BelowRequirement);
            decoded.Value.IsLowWattage.Should().BeTrue();
        }

        [Fact]
        public void DecodeAdapterData_Decodes_UsbC_Dock_Capture()
        {
            // Measured: 100 W USB-C PD dock on the same machine. 0x14 * 5 = 100.
            //
            // This is the capture the Type-C branch below was written against, and it is the one
            // that shows why the branch cannot be simplified to the wattage comparison: the design
            // rating reads 0 with the source live and negotiating 100 W, so the comparison is
            // 100 < 0 and only the barrel special case reaches the right answer. The EC clamps the
            // GPU to 35 W on this dock, so "under-rated" is the correct verdict to arrive at.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x05, 0xC2, 0x00, 0x14 });

            decoded.Should().NotBeNull();
            decoded!.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.ConnectedTypeC);
            decoded.Value.PowerRatingWatts.Should().Be(100);
            decoded.Value.PowerRatingKnown.Should().BeTrue();
            decoded.Value.UsbcDesignRatingWatts.Should().Be(0);
            decoded.Value.SupportsBarrelConnector.Should().BeTrue();
            decoded.Value.IsLowWattage.Should().BeTrue();
        }

        [Fact]
        public void DecodeAdapterData_Treats_0xFF_As_Unknown_Not_1275W()
        {
            // 0xFF is HP's "unknown" sentinel. Decoding it arithmetically would report 1275 W and
            // make an unidentified supply look like the best-equipped machine in the field reports.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x01, 0xC2, 0x00, 0xFF });

            decoded.Should().NotBeNull();
            decoded!.Value.PowerRatingKnown.Should().BeFalse();
            decoded.Value.PowerRatingWatts.Should().Be(0);
        }

        [Fact]
        public void DecodeAdapterData_Reads_BarrelSupport_From_Bit7_Only()
        {
            // 0xC2 has bit 7 set; 0x42 is the same byte without it. Nothing else in byte 1 matters.
            HpWmiBios.DecodeAdapterData(new byte[] { 0x01, 0xC2, 0x00, 0x42 })!
                .Value.SupportsBarrelConnector.Should().BeTrue();

            HpWmiBios.DecodeAdapterData(new byte[] { 0x01, 0x42, 0x00, 0x42 })!
                .Value.SupportsBarrelConnector.Should().BeFalse();
        }

        [Fact]
        public void DecodeAdapterData_Returns_Null_For_Short_Or_Missing_Payload()
        {
            HpWmiBios.DecodeAdapterData(null).Should().BeNull();
            HpWmiBios.DecodeAdapterData(new byte[] { 0x01, 0xC2, 0x00 }).Should().BeNull();
        }

        // ── IsLowWattage: HP's own comparison, including the Type-C branch most tables miss ──

        [Fact]
        public void IsLowWattage_TypeC_Compares_Against_UsbcDesignRating_Not_The_Status()
        {
            // 100 W PD supply on a chassis designed for 140 W: under-rated, even though the status
            // byte is ConnectedTypeC rather than BelowRequirement. Folding value 5 into
            // "anything that isn't MeetsRequirement" would get the right answer here by accident and
            // the wrong one in the next test.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x05, 0x00, 0x1C, 0x14 });

            decoded!.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.ConnectedTypeC);
            decoded.Value.UsbcDesignRatingWatts.Should().Be(140);
            decoded.Value.PowerRatingWatts.Should().Be(100);
            decoded.Value.IsLowWattage.Should().BeTrue();
        }

        [Fact]
        public void IsLowWattage_TypeC_At_Design_Rating_Is_Not_Low_Wattage()
        {
            // 140 W PD on a 140 W design: adequate. This is the case a naive
            // "status != MeetsRequirement" test gets wrong - it would warn the user about a supply
            // that is exactly what the chassis was designed for.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x05, 0x00, 0x1C, 0x1C });

            decoded!.Value.IsLowWattage.Should().BeFalse();
        }

        [Fact]
        public void IsLowWattage_TypeC_With_Barrel_Support_And_Zero_Design_Rating_Is_Low_Wattage()
        {
            // HP's special case: a barrel-capable chassis reporting no USB-C design rating is
            // treated as under-rated on PD regardless of what the supply claims.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x05, 0x80, 0x00, 0x42 });

            decoded!.Value.SupportsBarrelConnector.Should().BeTrue();
            decoded.Value.UsbcDesignRatingWatts.Should().Be(0);
            decoded.Value.IsLowWattage.Should().BeTrue();
        }

        [Fact]
        public void IsLowWattage_TypeC_Without_Barrel_Support_And_Zero_Design_Rating_Is_Not_Low_Wattage()
        {
            // Same zero design rating, no barrel jack: the special case does not apply, and the
            // primary comparison (0 < 0) is false.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0x05, 0x00, 0x00, 0x42 });

            decoded!.Value.IsLowWattage.Should().BeFalse();
        }

        [Theory]
        [InlineData(0x02)] // BelowRequirement
        [InlineData(0x03)] // BatteryPower
        [InlineData(0x04)] // NotFunctioning
        public void IsLowWattage_NonTypeC_Is_Anything_Other_Than_MeetsRequirement(byte status)
        {
            HpWmiBios.DecodeAdapterData(new byte[] { status, 0xC2, 0x00, 0x42 })!
                .Value.IsLowWattage.Should().BeTrue();
        }

        [Theory]
        [InlineData(0x00)] // NotSupported
        [InlineData(0xFF)] // Error
        public void NoVerdict_Statuses_Are_Not_Reported_As_Low_Wattage(byte status)
        {
            // HP evaluates its low-wattage rule only after a successful query. Feeding a non-answer
            // through the "anything that isn't MeetsRequirement" branch turns "no verdict" into "bad
            // adapter" - and a board that replies with zeroes would then be told, confidently and on
            // screen, to go and check a power supply that is probably fine.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { status, 0x00, 0x00, 0x00 });

            decoded!.Value.HasVerdict.Should().BeFalse();
            decoded.Value.IsLowWattage.Should().BeFalse();
        }

        [Fact]
        public void Status_0xFF_Decodes_To_Error_Not_255()
        {
            // The enum is sbyte-backed for this reason. With the default int backing, a 0xFF wire
            // byte produced 255, which fell through every switch to render as the literal "255" in
            // the UI and the diagnostics export.
            var decoded = HpWmiBios.DecodeAdapterData(new byte[] { 0xFF, 0xC2, 0x00, 0x42 });

            decoded!.Value.Status.Should().Be(HpWmiBios.SmartAdapterStatus.Error);
        }

        [Fact]
        public void SmartAdapterStatus_Includes_ConnectedTypeC()
        {
            // Guards the sixth value. Tables sourced from third-party projects commonly stop at 4,
            // which silently reclassifies a USB-C PD machine as a fault state.
            ((int)HpWmiBios.SmartAdapterStatus.ConnectedTypeC).Should().Be(5);
            ((int)HpWmiBios.SmartAdapterStatus.Error).Should().Be(-1);
        }
    }
}
