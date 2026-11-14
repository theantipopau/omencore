using System;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// 5.0.1 rules from GitHub #222 (Ryzen 8940HX Tctl spikes, idles at 63-68 C) and #249 (an
    /// unconfirmed ACPI zone reading 87-96 C at idle on 8E5F).
    /// </summary>
    public class ThermalSpikeRulesTests
    {
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]   // one sample of a 2-3 s spike no longer forces 100 %
        [InlineData(2, true)]
        [InlineData(5, true)]
        public void Emergency_NeedsConsecutiveSamples(int samples, bool trips)
        {
            FanService.ShouldTripThermalEmergency(samples).Should().Be(trips);
        }

        [Fact]
        public void ReleaseFloor_EndsAfterItsTime_UnlessProtectionIsActiveAgain()
        {
            var since = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            var later = since.AddSeconds(FanService.ThermalReleaseFloorSeconds);

            FanService.ReleaseFloorExpired(since, since.AddSeconds(30), protectionActive: false).Should().BeFalse();
            FanService.ReleaseFloorExpired(since, later, protectionActive: false).Should().BeTrue();
            FanService.ReleaseFloorExpired(since, later, protectionActive: true).Should().BeFalse();
            FanService.ReleaseFloorExpired(DateTime.MinValue, later, protectionActive: false).Should().BeFalse();
        }

        [Theory]
        [InlineData(true, 55.0, true)]    // a confirmed zone keeps its authority
        [InlineData(false, 55.0, false)]  // an unconfirmed guess does not override another source
        [InlineData(false, 0.0, true)]    // ...but is still better than no CPU temperature at all
        public void UnconfirmedAcpiZone_OnlyFillsAGap(bool confirmed, double otherSource, bool mayTake)
        {
            WmiBiosMonitor.AcpiZoneMayTakeCpuAuthority(confirmed, otherSource).Should().Be(mayTake);
        }
    }
}
