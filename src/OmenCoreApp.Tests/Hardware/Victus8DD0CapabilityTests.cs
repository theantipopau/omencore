using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class Victus8DD0CapabilityTests
    {
        [Fact]
        public void GetCapabilities_ReturnsExactProfile_ForVictus8DD0()
        {
            var capabilities = ModelCapabilityDatabase.GetCapabilities("8DD0");

            capabilities.Should().NotBeNull();
            capabilities.ProductId.Should().Be("8DD0");
            capabilities.ModelName.Should().Be("HP Victus 15 (2025) fb3xxx");

            capabilities.UserVerified.Should().BeFalse();
            capabilities.SupportsFanControlWmi.Should().BeTrue();
            capabilities.SupportsFanControlEc.Should().BeFalse();
            capabilities.SupportsFanCurves.Should().BeTrue();
            capabilities.SupportsIndependentFanCurves.Should().BeFalse();
            capabilities.SupportsRpmReadback.Should().BeFalse();
            capabilities.FanZoneCount.Should().Be(2);
            capabilities.MaxFanLevel.Should().Be(55);
            capabilities.HasMuxSwitch.Should().BeFalse();
            capabilities.SupportsGpuPowerBoost.Should().BeFalse();
            capabilities.SupportsUndervolt.Should().BeFalse();
            capabilities.SupportsPowerLimits.Should().BeFalse();
            capabilities.HasFourZoneRgb.Should().BeFalse();
            capabilities.HasKeyboardBacklight.Should().BeTrue();

            capabilities.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
        }

        [Fact]
        public void GetPreferredCapabilities_PrefersExact8DD0Profile_OverFb3PatternFallback()
        {
            var capabilities = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8DD0",
                "Victus by HP Gaming Laptop 15-fb3xxx",
                CpuUndervoltProviderFactory.CpuVendor.AMD);

            capabilities.Should().NotBeNull();
            capabilities!.ProductId.Should().Be("8DD0");
            capabilities.UserVerified.Should().BeFalse();
            capabilities.SupportsFanControlWmi.Should().BeTrue();
        }
    }
}