using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class ModelIdentityResolutionSummaryTests
    {
        [Fact]
        public void Build_SeparatesBoardProductIdFromHpSupportProductNumber()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN Gaming Laptop 16-n0xxx",
                ProductName = "8A43",
                SystemSku = "6G103EA#ABU",
                SystemProductIdentifyingNumber = "5CD0000000",
                BiosVersion = "F.17"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8A43",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.OMEN16,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("8A43")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.RawBaseboardProduct.Should().Be("8A43");
            summary.RawSystemSku.Should().Be("6G103EA#ABU");
            summary.RawSystemProductIdentifyingNumber.Should().Be("5CD0000000");
            summary.HpSupportProductNumber.Should().Be("6G103EA");
            summary.RawIdentitySummary.Should().Contain("Baseboard ProductId: 8A43");
            summary.RawIdentitySummary.Should().Contain("HP support product: 6G103EA");
            summary.ClipboardSummary.Should().Contain("System product identifying number: 5CD0000000");
            summary.ClipboardSummary.Should().Contain("HP support product number: 6G103EA");
            summary.TraceText.Should().Contain("Baseboard ProductId drives OmenCore capability lookup");
        }

        [Fact]
        public void Build_8D2FExactProductId_IsHighConfidenceWithoutVerificationWarnings()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN Gaming Laptop 16-am0xxx",
                ProductName = "8D2F",
                SystemSku = "",
                BiosVersion = "F.01"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8D2F",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.OMEN16,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("8D2F")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("High");
            summary.WarningText.Should().BeEmpty();
            summary.KeyboardResolutionSource.Should().Be("Exact ProductId");
            summary.KeyboardConfidence.Should().Be("High");
            summary.KeyboardWarningText.Should().BeEmpty();
            summary.ClipboardSummary.Should().NotContain("Capability warning:");
            summary.ClipboardSummary.Should().NotContain("Keyboard warning:");
        }

        [Fact]
        public void Build_8D41ExactProductId_ResolvesKeyboardProfile()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN MAX Gaming Laptop 16t-ah000",
                ProductName = "8D41",
                SystemSku = "1H9533H07X",
                BiosVersion = "F.01"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8D41",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.OMEN2024Plus,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("8D41")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("High");
            summary.KeyboardResolutionSource.Should().Be("Exact ProductId");
            summary.KeyboardModel.Should().Contain("OMEN MAX 16");
            summary.KeyboardConfidence.Should().Be("Medium");
            summary.KeyboardWarningText.Should().Contain("not user-verified");
            summary.ClipboardSummary.Should().NotContain("Keyboard model: Unknown");
        }

        [Fact]
        public void Build_8BCDExactProductId_ReportsExactMediumConfidence()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN by HP Gaming Laptop 16-xd0xxx",
                ProductName = "8BCD",
                SystemSku = "CND42907M9",
                BiosVersion = "F.32"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8BCD",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.OMEN16,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("8BCD")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("Medium");
            summary.WarningText.Should().Contain("not user-verified");
            summary.ClipboardSummary.Should().Contain("Resolution source: Exact ProductId");
            summary.ClipboardSummary.Should().NotContain("Capability profile was inferred from the WMI model name");
        }

        [Fact]
        public void Build_878CExactProductId_ReplacesLegacyFamilyFallback()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN Laptop 15-ek0xxx",
                ProductName = "878C",
                SystemSku = "5CD045FMXY",
                BiosVersion = "F.00"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "878C",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.Legacy,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("878C")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("Medium");
            summary.ResolvedModel.Should().Contain("15-ek0");
            summary.WarningText.Should().Contain("not user-verified");
            summary.KeyboardResolutionSource.Should().Be("Exact ProductId");
            summary.KeyboardModel.Should().Contain("15-ek0");
            summary.KeyboardConfidence.Should().Be("Medium");
            summary.ClipboardSummary.Should().Contain("Resolution source: Exact ProductId");
            summary.ClipboardSummary.Should().NotContain("Family fallback");
            summary.ClipboardSummary.Should().NotContain("Keyboard model: Unknown");
        }

        [Fact]
        public void Build_8600ExactProductId_ReplacesLegacyFamilyFallback()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "OMEN by HP Laptop 15-dh0xxx",
                ProductName = "8600",
                SystemSku = "CND0127265",
                BiosVersion = "F.00"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8600",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.Legacy,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("8600")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("Medium");
            summary.ResolvedModel.Should().Contain("15-dh0");
            summary.WarningText.Should().Contain("not user-verified");
            summary.KeyboardResolutionSource.Should().Be("Exact ProductId");
            summary.KeyboardModel.Should().Contain("15-dh0");
            summary.KeyboardConfidence.Should().Be("Medium");
            summary.ClipboardSummary.Should().Contain("Resolution source: Exact ProductId");
            summary.ClipboardSummary.Should().NotContain("Family fallback");
            summary.ClipboardSummary.Should().NotContain("Keyboard model: Unknown");
        }

        [Fact]
        public void Build_88EEExactProductId_ReplacesVictusE0ModelPatternFallback()
        {
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "Victus by HP Laptop 16-e0xxx",
                ProductName = "88EE",
                SystemSku = "5CD203NZTV",
                BiosVersion = "F.00"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "88EE",
                ModelName = systemInfo.Model,
                ModelFamily = OmenModelFamily.Victus,
                IsKnownModel = true,
                ModelConfig = ModelCapabilityDatabase.GetCapabilities("88EE")
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("Medium");
            summary.ResolvedModel.Should().Contain("e0194nw");
            summary.KeyboardResolutionSource.Should().Be("Exact ProductId");
            summary.KeyboardModel.Should().Contain("e0194nw");
            summary.KeyboardConfidence.Should().Be("Medium");
            summary.ClipboardSummary.Should().Contain("Capability ProductId: 88EE");
            summary.ClipboardSummary.Should().Contain("Resolution source: Exact ProductId");
            summary.ClipboardSummary.Should().NotContain("Resolution source: Model-name pattern");
            summary.ClipboardSummary.Should().NotContain("Keyboard source: Model-name series match");
        }
    }
}
