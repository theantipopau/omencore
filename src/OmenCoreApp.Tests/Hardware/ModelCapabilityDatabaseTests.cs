using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class ModelCapabilityDatabaseTests
    {
        [Fact]
        public void GetCapabilitiesByModelName_Returns_OmenMaxAk0003nr()
        {
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName("OMEN MAX 16 ak0003nr");
            caps.Should().NotBeNull();
            caps!.ModelName.Should().Contain("OMEN MAX 16");
            caps.ModelName.Should().Contain("ak0003nr");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.FanZoneCount.Should().Be(2);
        }

        [Theory]
        [InlineData("8A44", OmenModelFamily.OMEN16)]
        [InlineData("8A3E", OmenModelFamily.Victus)]
        [InlineData("8A26", OmenModelFamily.Victus)]
        [InlineData("8C58", OmenModelFamily.Transcend)]
        [InlineData("8E41", OmenModelFamily.Transcend)]
        [InlineData("8D87", OmenModelFamily.OMEN2024Plus)]
        [InlineData("8787", OmenModelFamily.Legacy)]
        public void GetCapabilities_Returns_NewlyAdded_ModelEntries(string productId, OmenModelFamily expectedFamily)
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(productId);

            caps.Should().NotBeNull();
            caps.ProductId.Should().Be(productId);
            caps.Family.Should().Be(expectedFamily);
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_Transcend14Entries_DisableDirectEcAndCurves()
        {
            var caps8c58 = ModelCapabilityDatabase.GetCapabilities("8C58");
            var caps8e41 = ModelCapabilityDatabase.GetCapabilities("8E41");

            caps8c58.SupportsFanControlEc.Should().BeFalse();
            caps8c58.SupportsFanCurves.Should().BeFalse();
            caps8e41.SupportsFanControlEc.Should().BeFalse();
            caps8e41.SupportsFanCurves.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8D87_OmenMaxAk0xxx_UsesMaxSeriesSafetyProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.ProductId.Should().Be("8D87");
            caps.ModelName.Should().Contain("OMEN MAX 16");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.HasPerKeyRgb.Should().BeTrue();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8787_Omen15En0038ur_UsesReportedSafeCapabilities()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8787");

            caps.ProductId.Should().Be("8787");
            caps.ModelName.Should().Contain("15-en0038ur");
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.HasMuxSwitch.Should().BeTrue();
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsRpmReadback.Should().BeFalse("GitHub #120 reports accepted fan commands but 0 RPM readback");
            caps.MaxFanLevel.Should().Be(55);
        }
    }
}
