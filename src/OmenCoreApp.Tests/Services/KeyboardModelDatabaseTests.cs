using FluentAssertions;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class KeyboardModelDatabaseTests
    {
        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8BD5()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8BD5");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("Victus");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
        }
    }
}