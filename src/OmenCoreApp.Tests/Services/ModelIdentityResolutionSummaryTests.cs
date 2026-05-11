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
            summary.HpSupportProductNumber.Should().Be("6G103EA");
            summary.RawIdentitySummary.Should().Contain("Baseboard ProductId: 8A43");
            summary.RawIdentitySummary.Should().Contain("HP support product: 6G103EA");
            summary.ClipboardSummary.Should().Contain("HP support product number: 6G103EA");
            summary.TraceText.Should().Contain("Baseboard ProductId drives OmenCore capability lookup");
        }
    }
}
