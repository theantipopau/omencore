using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class Victus8DD0CapabilityTests
    {
        [Fact]
        public void GetCapabilities_ReturnsVerifiedProfile_ForVictus8DD0()
        {
            var capabilities = ModelCapabilityDatabase.GetCapabilities("8DD0");

            capabilities.Should().NotBeNull();
            capabilities.ProductId.Should().Be("8DD0");
            capabilities.ModelName.Should().Be("HP Victus 15 (2025) fb3xxx");

            capabilities.UserVerified.Should().BeTrue();
            capabilities.SupportsFanControlWmi.Should().BeTrue();
            capabilities.SupportsFanControlEc.Should().BeFalse();
            capabilities.SupportsFanCurves.Should().BeTrue();
            capabilities.SupportsIndependentFanCurves.Should().BeFalse();
            capabilities.FanZoneCount.Should().Be(1);
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
            capabilities.UserVerified.Should().BeTrue();
            capabilities.SupportsFanControlWmi.Should().BeTrue();
        }
    }
}