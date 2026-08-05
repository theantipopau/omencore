using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Decode tests for HP's <c>SystemDesignData</c> block (<c>Default 0x28</c>).
    ///
    /// The reply used throughout is a real capture from board 8D87 (BIOS F.07). The command returns
    /// 128 bytes and OmenCore historically read exactly one of them.
    /// </summary>
    public class HpWmiBiosSystemDesignDataTests
    {
        /// <summary>
        /// Measured 0x28 reply, padded to the 128 bytes the firmware actually returns.
        /// 4A 01 = 330 W shipping adapter, 3A = unused, 01 = thermal policy V1, 03 = SW fan control
        /// + extreme mode support, 00 = PL4 default, 01 = BIOS OC support, 07 = GPU mode switch,
        /// 3C = 60 W CPU budget with GPU, 00 = load lines, 03 00 = sensor/hotkey bits.
        /// </summary>
        private static byte[] Board8D87Reply()
        {
            var reply = new byte[128];
            byte[] head = { 0x4A, 0x01, 0x3A, 0x01, 0x03, 0x00, 0x01, 0x07, 0x3C, 0x00, 0x03, 0x00 };
            head.CopyTo(reply, 0);
            return reply;
        }

        [Fact]
        public void DecodeSystemDesignData_Decodes_Board8D87_Capture()
        {
            var decoded = HpWmiBios.DecodeSystemDesignData(Board8D87Reply());

            decoded.Should().NotBeNull();
            var design = decoded!.Value;

            design.ShippingAdapterPowerRatingWatts.Should().Be(330);
            design.ThermalPolicyVersion.Should().Be(HpWmiBios.ThermalPolicyVersion.V1);
            design.IsSwFanControlSupport.Should().BeTrue();
            design.IsExtremeModeSupport.Should().BeTrue();
            design.IsExtremeModeUnlock.Should().BeFalse();
            design.IsDTBiosControl.Should().BeFalse();
            design.IsTwoBytePL4Support.Should().BeFalse();
            design.PL4DefaultValue.Should().Be(0);
            design.IsBiosDefinedOcSupport.Should().BeTrue();
            design.GpuModeSwitch.Should().Be(0x07);
            design.DefaultCpuPowerLimitWithGpuWatts.Should().Be(60);
            design.LoadLineSupportLevels.Should().Be(0);
            design.DefaultLoadLine.Should().Be(0);

            // Byte 10 is 0x03 and byte 11 is 0x00 on this board. These five were omitted from an
            // earlier version of this test, which is exactly where a decode bug hid: reading
            // ChangeIrSensorToBoard as bit 0 alone returned true here, where HP's two-bit rule
            // (bit 0 clear AND bit 1 set) returns false. Assert them against the unmodified capture.
            design.ChangeIrSensorToBoard.Should().BeFalse();
            design.IsPchOverheatSupport.Should().BeFalse();
            design.IsVrSensorSupport.Should().BeFalse();
            design.IsHotkeySupportFnP.Should().BeFalse();
            design.IsHotkeySupportFnF1.Should().BeFalse();
        }

        [Theory]
        [InlineData(0x00, false)] // both clear
        [InlineData(0x01, false)] // bit 0 set   -> excluded by the "bit 0 clear" half
        [InlineData(0x02, true)]  // bit 1 only  -> the only pattern that qualifies
        [InlineData(0x03, false)] // both set    -> this board; the case a bit-0-only read gets wrong
        public void ChangeIrSensorToBoard_RequiresBit0Clear_AndBit1Set(byte byte10, bool expected)
        {
            var reply = Board8D87Reply();
            reply[10] = byte10;

            HpWmiBios.DecodeSystemDesignData(reply)!
                .Value.ChangeIrSensorToBoard.Should().Be(expected);
        }

        [Fact]
        public void ShippingAdapterPowerRating_Is_LittleEndian_Watts_Not_A_Flag_Field()
        {
            // 0x014A = 330. This field was once read as a 16-bit flag field whose decimal value
            // happened to be 330; HP's own accessor compares it numerically against 200 and 280,
            // which settles it as watts.
            HpWmiBios.DecodeSystemDesignData(Board8D87Reply())!
                .Value.ShippingAdapterPowerRatingWatts.Should().Be(330);

            var twoHundredEighty = Board8D87Reply();
            twoHundredEighty[0] = 0x18;
            twoHundredEighty[1] = 0x01; // 0x0118 = 280
            HpWmiBios.DecodeSystemDesignData(twoHundredEighty)!
                .Value.ShippingAdapterPowerRatingWatts.Should().Be(280);
        }

        [Fact]
        public void IsTwoBytePL4Support_Reads_Byte4_Bit4()
        {
            // This bit changes the wire format of Default 0x29 SetPL4 from one byte to two. Nothing
            // issues 0x29 today; the bit is decoded so that whatever does can branch on it rather
            // than assume a width. It is not board-specific - any board may set it.
            var reply = Board8D87Reply();
            reply[4] |= 0x10;

            var design = HpWmiBios.DecodeSystemDesignData(reply)!.Value;
            design.IsTwoBytePL4Support.Should().BeTrue();

            // and the neighbouring bits in the same byte are unaffected
            design.IsSwFanControlSupport.Should().BeTrue();
            design.IsExtremeModeSupport.Should().BeTrue();
            design.IsExtremeModeUnlock.Should().BeFalse();
            design.IsDTBiosControl.Should().BeFalse();
        }

        [Fact]
        public void LoadLine_Splits_Byte9_Into_Nibbles()
        {
            var reply = Board8D87Reply();
            reply[9] = 0x34; // low nibble = supported levels, high nibble = default

            var design = HpWmiBios.DecodeSystemDesignData(reply)!.Value;
            design.LoadLineSupportLevels.Should().Be(4);
            design.DefaultLoadLine.Should().Be(3);
        }

        [Fact]
        public void DecodeSystemDesignData_Reads_Sensor_And_Hotkey_Bits()
        {
            var reply = Board8D87Reply();
            reply[10] = 0x0E; // bit 1 IR sensor (bit 0 clear), bit 2 PCH overheat, bit 3 VR sensor
            reply[11] = 0x03; // bit 0 Fn+P, bit 1 Fn+F1

            var design = HpWmiBios.DecodeSystemDesignData(reply)!.Value;
            design.ChangeIrSensorToBoard.Should().BeTrue();
            design.IsPchOverheatSupport.Should().BeTrue();
            design.IsVrSensorSupport.Should().BeTrue();
            design.IsHotkeySupportFnP.Should().BeTrue();
            design.IsHotkeySupportFnF1.Should().BeTrue();
        }

        [Fact]
        public void DecodeSystemDesignData_Returns_Null_When_Reply_Is_Too_Short()
        {
            // The existing thermal-policy read only needed 9 bytes. Firmware that answers with fewer
            // than the 12 HP's accessors cover must not be decoded past what it sent.
            HpWmiBios.DecodeSystemDesignData(null).Should().BeNull();
            HpWmiBios.DecodeSystemDesignData(new byte[11]).Should().BeNull();
            HpWmiBios.DecodeSystemDesignData(new byte[12]).Should().NotBeNull();
        }

        [Fact]
        public void DecodeSystemDesignData_Keeps_Only_The_First_12_Bytes_Of_Raw_Data()
        {
            var design = HpWmiBios.DecodeSystemDesignData(Board8D87Reply())!.Value;

            design.RawData.Should().HaveCount(12);
            design.RawData.Should().Equal(0x4A, 0x01, 0x3A, 0x01, 0x03, 0x00, 0x01, 0x07, 0x3C, 0x00, 0x03, 0x00);
        }
    }
}
