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

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8A26()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8A26");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("Victus");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8A44()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8A44");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("OMEN 16");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8A3E()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8A3E");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("Victus 15");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8E41()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8E41");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("Transcend 14");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.NewWmi2023);
            cfg.KeyboardType.Should().Be(KeyboardType.PerKeyRgb);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8D87()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8D87");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("OMEN MAX 16");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.HidPerKey);
            cfg.KeyboardType.Should().Be(KeyboardType.PerKeyRgb);
            cfg.UserVerified.Should().BeFalse();
        }
    }
}