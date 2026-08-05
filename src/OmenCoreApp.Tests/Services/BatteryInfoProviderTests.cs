using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The dashboard displayed a hardcoded 0 battery cycles on a machine whose battery reports
    /// 14. The reading itself needs real WMI, so what is pinned here is the part that made the
    /// old code wrong independently of any hardware: treating "unknown" and "zero" as the same
    /// value. A new battery legitimately reads 0 cycles.
    /// </summary>
    public class BatteryInfoProviderTests
    {
        [Fact]
        public void HealthPercent_IsFullChargeOverDesign()
        {
            // The measured values from the board that prompted this: 79416 of 83029 mWh.
            var info = new BatteryInfoProvider.BatteryInfo
            {
                DesignedCapacityMilliwattHours = 83029,
                FullChargedCapacityMilliwattHours = 79416
            };

            info.HealthPercent.Should().BeApproximately(95.65, 0.01);
        }

        [Theory]
        [InlineData(null, 79416)]
        [InlineData(83029, null)]
        [InlineData(null, null)]
        public void HealthPercent_IsUnknown_WhenEitherCapacityIsMissing(int? design, int? full)
        {
            var info = new BatteryInfoProvider.BatteryInfo
            {
                DesignedCapacityMilliwattHours = design,
                FullChargedCapacityMilliwattHours = full
            };

            info.HealthPercent.Should().BeNull("half a ratio is not a health figure");
        }

        [Fact]
        public void HealthPercent_IsUnknown_RatherThanInfinite_WhenDesignCapacityIsZero()
        {
            var info = new BatteryInfoProvider.BatteryInfo
            {
                DesignedCapacityMilliwattHours = 0,
                FullChargedCapacityMilliwattHours = 79416
            };

            info.HealthPercent.Should().BeNull();
        }

        [Fact]
        public void HealthPercent_IsNotClampedTo100()
        {
            // A pack reporting slightly over design is real and common. Trimming it to 100
            // would hide a miscalibrated gauge behind a perfect-looking number.
            var info = new BatteryInfoProvider.BatteryInfo
            {
                DesignedCapacityMilliwattHours = 80000,
                FullChargedCapacityMilliwattHours = 82000
            };

            info.HealthPercent.Should().BeApproximately(102.5, 0.01);
        }

        [Fact]
        public void ZeroCycles_IsAValue_NotAnAbsentReading()
        {
            // The distinction the old code collapsed: a brand-new battery reads 0, and that is
            // not the same as a controller that declines to answer.
            var newBattery = new BatteryInfoProvider.BatteryInfo { CycleCount = 0 };
            var noReading = new BatteryInfoProvider.BatteryInfo { CycleCount = null };

            newBattery.CycleCount.Should().Be(0);
            newBattery.CycleCount.Should().NotBeNull();
            noReading.CycleCount.Should().BeNull();
        }

        [Fact]
        public void Get_DoesNotThrow_WhateverTheMachineReports()
        {
            // Runs against real WMI. The assertion is only that an unreadable counter is an
            // expected outcome rather than an exception - this is called on every UI tick and
            // on hardware with no battery at all.
            var act = () => BatteryInfoProvider.Get();

            act.Should().NotThrow();
        }
    }
}
