using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Decode tests for the two BIOS replies that are cast to an enum: <c>Keyboard 0x01</c> keyboard
    /// type and <c>Legacy 0x52</c> GPU mode.
    ///
    /// These exist because casting a wire byte straight to an enum silently manufactures values the
    /// enum never declared. The repo has already been bitten by that shape once - SmartAdapterStatus
    /// was int-backed, so a 0xFF byte decoded to 255 rather than Error and rendered as the literal
    /// "255" in the UI. The 0xFF keyboard payload below is a real capture from board 8D87.
    /// </summary>
    public class HpWmiBiosEnumDecodeTests
    {
        // ---- keyboard type -------------------------------------------------------------------

        [Theory]
        [InlineData(0x00, HpWmiBios.KbdType.Standard)]
        [InlineData(0x01, HpWmiBios.KbdType.WithNumPad)]
        [InlineData(0x02, HpWmiBios.KbdType.TenKeyLess)]
        [InlineData(0x03, HpWmiBios.KbdType.PerKeyRgb)]
        public void DecodeKeyboardType_Accepts_Every_Declared_Value(byte wire, HpWmiBios.KbdType expected)
        {
            HpWmiBios.DecodeKeyboardType(new byte[] { wire, 0, 0, 0 }).Should().Be(expected);
        }

        [Fact]
        public void DecodeKeyboardType_Rejects_The_0xFF_Reply_Measured_On_Board_8D87()
        {
            // Measured on an OMEN MAX 16 (8D87, BIOS F.07): this board does not answer Keyboard 0x01
            // and returns 0xFF. Before validation that became (KbdType)255 - not a member, but not a
            // failure either, so callers had no way to tell it apart from a real answer.
            HpWmiBios.DecodeKeyboardType(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })
                .Should().BeNull("0xFF is not a KbdType and must not be manufactured into one");
        }

        [Theory]
        [InlineData(0x04)]
        [InlineData(0x7F)]
        [InlineData(0xFE)]
        public void DecodeKeyboardType_Rejects_Anything_Outside_The_Declared_Range(byte wire)
        {
            HpWmiBios.DecodeKeyboardType(new byte[] { wire, 0, 0, 0 }).Should().BeNull();
        }

        [Fact]
        public void DecodeKeyboardType_Handles_Missing_And_Empty_Replies()
        {
            HpWmiBios.DecodeKeyboardType(null).Should().BeNull();
            HpWmiBios.DecodeKeyboardType(new byte[0]).Should().BeNull();
        }

        // ---- GPU mode ------------------------------------------------------------------------

        [Theory]
        [InlineData(0x00, HpWmiBios.GpuMode.Hybrid)]
        [InlineData(0x01, HpWmiBios.GpuMode.Discrete)]
        [InlineData(0x02, HpWmiBios.GpuMode.Optimus)]
        public void DecodeGpuMode_Accepts_Every_Declared_Value(byte wire, HpWmiBios.GpuMode expected)
        {
            HpWmiBios.DecodeGpuMode(new byte[] { wire, 0, 0, 0 }).Should().Be(expected);
        }

        [Fact]
        public void DecodeGpuMode_Decodes_The_Hybrid_Reply_Measured_On_Board_8D87()
        {
            // Measured: 00 00 00 00 with return code 0. Worth pinning as a decode, but note that on
            // this transport an all-zero reply is also what an ACPI timeout produces, so the value
            // being Hybrid is not on its own evidence that a MUX exists to be in hybrid.
            HpWmiBios.DecodeGpuMode(new byte[] { 0x00, 0x00, 0x00, 0x00 })
                .Should().Be(HpWmiBios.GpuMode.Hybrid);
        }

        [Theory]
        [InlineData(0x03)]
        [InlineData(0x04)]
        [InlineData(0xFF)]
        public void DecodeGpuMode_Rejects_Anything_Outside_The_Declared_Range(byte wire)
        {
            HpWmiBios.DecodeGpuMode(new byte[] { wire, 0, 0, 0 })
                .Should().BeNull("GpuMode declares only 0x00-0x02");
        }

        [Fact]
        public void DecodeGpuMode_Handles_Missing_And_Empty_Replies()
        {
            HpWmiBios.DecodeGpuMode(null).Should().BeNull();
            HpWmiBios.DecodeGpuMode(new byte[0]).Should().BeNull();
        }
    }
}
