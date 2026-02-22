using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class HpWmiBiosRpmParsingTests
    {
        [Fact]
        public void ParseFanRpmBuffer_Parses_LittleEndian_Correctly()
        {
            // 400 => 0x0190 => bytes: 0x90,0x01
            // 372 => 0x0174 => bytes: 0x74,0x01
            var buf = new byte[] { 0x90, 0x01, 0x74, 0x01 };
            var parsed = HpWmiBios.ParseFanRpmBuffer(buf);
            parsed.Should().NotBeNull();
            parsed.Value.fan1Rpm.Should().Be(400);
            parsed.Value.fan2Rpm.Should().Be(372);
        }

        [Fact]
        public void ParseFanRpmBuffer_Parses_BigEndian_When_LE_Invalid()
        {
            // Big-endian representation for 400,372 -> [0x01,0x90, 0x01,0x74]
            var buf = new byte[] { 0x01, 0x90, 0x01, 0x74 };
            var parsed = HpWmiBios.ParseFanRpmBuffer(buf);
            parsed.Should().NotBeNull();
            parsed.Value.fan1Rpm.Should().Be(400);
            parsed.Value.fan2Rpm.Should().Be(372);
        }

        [Fact]
        public void ParseFanRpmBuffer_ReturnsNull_For_Invalid_Data()
        {
            var buf = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            var parsed = HpWmiBios.ParseFanRpmBuffer(buf);
            parsed.Should().BeNull();
        }

        [Fact]
        public void ParseFanRpmBuffer_Handles_Zero_RPM()
        {
            var buf = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var parsed = HpWmiBios.ParseFanRpmBuffer(buf);
            parsed.Should().NotBeNull();
            parsed.Value.fan1Rpm.Should().Be(0);
            parsed.Value.fan2Rpm.Should().Be(0);
        }

        [Fact]
        public void ParseFanRpmBuffer_Parses_High_RPMS_Correctly()
        {
            // 4200 -> 0x1068 => bytes LE: 0x68,0x10
            // 4100 -> 0x1004 => bytes LE: 0x04,0x10
            var buf = new byte[] { 0x68, 0x10, 0x04, 0x10 };
            var parsed = HpWmiBios.ParseFanRpmBuffer(buf);
            parsed.Should().NotBeNull();
            parsed.Value.fan1Rpm.Should().Be(4200);
            parsed.Value.fan2Rpm.Should().Be(4100);
        }
    }
}
