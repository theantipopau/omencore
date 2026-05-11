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
        [InlineData("8A43", OmenModelFamily.OMEN16)]
        [InlineData("8A44", OmenModelFamily.OMEN16)]
        [InlineData("8C76", OmenModelFamily.OMEN16)]
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

        [Fact]
        public void GetCapabilities_8C76_Wf1015ns_UsesExactV1WmiProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8C76");

            caps.ProductId.Should().Be("8C76");
            caps.ModelName.Should().Contain("wf1xxx");
            caps.Family.Should().Be(OmenModelFamily.OMEN16);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.HasMuxSwitch.Should().BeTrue();
            caps.SupportsGpuPowerBoost.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilitiesByModelName_16Am0IntelFallback_DisablesDirectEc()
        {
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName("OMEN Gaming Laptop 16-am0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("am0xxx_intel_2025_unverified");
            caps.Notes.Should().Contain("GitHub #124");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8D2F_StillUsesExactAmdProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8D2F", "OMEN Gaming Laptop 16-am0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8D2F");
            caps.ModelName.Should().Contain("AMD");
        }

        [Fact]
        public void GetPreferredCapabilities_8A43_WithN0xxModel_PrefersExactProductIdOverSiblingPattern()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8A43", "OMEN Gaming Laptop 16-n0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8A43");
            caps.Notes.Should().Contain("16-n0002ni");
            caps.Notes.Should().Contain("6G103EA");
        }

        [Fact]
        public void GetPreferredCapabilities_Ambiguous8Bb1_UsesModelNameDisambiguation()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BB1", "Victus by HP Gaming Laptop 15-fa1xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BB1-VICTUS15");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            ModelCapabilityDatabase.IsAmbiguousProductId("8BB1").Should().BeTrue();
        }
    }
}
