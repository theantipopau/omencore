using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

public class EcFanRpmTests
{
    [Theory]
    [InlineData(0xFF, 0)]   // GitHub #248: an unmapped register printed as 25,500 RPM on 8D40
    [InlineData(0x00, 0)]
    [InlineData(0x1A, 2600)]
    public void EcFanByteToRpm_TreatsAnUnmappedRegisterAsNoReading(int raw, int expected)
    {
        LinuxEcController.EcFanByteToRpm((byte)raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(3600, 0, true)]      // GitHub #238: 8EEC stopped the fans on pwm_enable=0
    [InlineData(3600, 2400, true)]   // still spinning down 3 s later is not full speed
    [InlineData(0, 4200, false)]     // idle to full speed (toggle-only boards, #174)
    [InlineData(5400, 5300, false)]  // already at full speed, tachometer jitter
    public void FullSpeedWriteSlowedFans_FlagsAWriteThatStoppedOrSlowedTheFans(int before, int after, bool slowed)
    {
        LinuxEcController.FullSpeedWriteSlowedFans(before, after).Should().Be(slowed);
    }

    [Theory]
    [InlineData(0, 2200, 4600, true)]     // GitHub #238, 5.0.1: pwm1_enable=0 settled near BIOS-auto speed
    [InlineData(0, 4400, 4600, false)]    // real full speed
    [InlineData(0, 2200, 0, false)]       // no fan1_max published: only the stop/slow rule applies
    public void FullSpeedWriteSlowedFans_UsesFanMaxWhenPublished(int before, int after, int max, bool slowed)
    {
        LinuxEcController.FullSpeedWriteSlowedFans(before, after, max).Should().Be(slowed);
    }

    [Fact]
    public void EcFanByteToRpm_NoReadIsZero()
    {
        LinuxEcController.EcFanByteToRpm(null).Should().Be(0);
    }
}
