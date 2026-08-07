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
        public void GetConfig_ReturnsConfig_For_ProductId_8A43()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8A43");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("OMEN 16");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("6G103EA");
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
        public void GetConfig_ReturnsConfig_For_ProductId_8C30()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8C30");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("15-fb1xxx");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("GitHub #135 diagnostics");
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
        public void GetConfig_ReturnsConfig_For_ProductId_8C76()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8C76");
            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("wf1xxx");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
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

            // Was BeFalse. This board is now owner-verified on hardware: the keyboard is Darfon
            // 0d62:54bf, driven over mi_04 for static per-key colour and mi_03 for the twelve
            // device effects, all confirmed by eye. UserVerified is a claim about evidence, and
            // the evidence changed - so the assertion follows it rather than pinning the entry to
            // what was known when the test was written.
            cfg.UserVerified.Should().BeTrue();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8D41()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8D41");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("OMEN MAX 16");
            cfg.ModelName.Should().Contain("ah0xxx");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.HidPerKey);
            cfg.FallbackMethods.Should().Contain(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.PerKeyRgb);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfigByModelName_ReturnsPerKeyConfig_ForOmenMaxAk0003nr()
        {
            var cfg = KeyboardModelDatabase.GetConfigByModelName("OMEN MAX 16 ak0003nr");

            cfg.Should().NotBeNull();
            cfg!.KeyboardType.Should().Be(KeyboardType.PerKeyRgb);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.HidPerKey);
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8E35()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8E35");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("ap0xxx");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("1H85430PWY");
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8BD4()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8BD4");

            cfg.Should().NotBeNull();
            cfg!.ProductId.Should().Be("8BD4");
            cfg.ModelName.Should().Contain("16-s0");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("7Z5Z2EA");
        }

        [Fact]
        public void GetConfig_ReturnsColorTableConfig_For_Victus16S0035NtSku()
        {
            var cfg = KeyboardModelDatabase.GetConfig("7Z5Z2EA");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("16-s0035nt");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfigByModelName_ReturnsIntelAm0Fallback_ForIssue124()
        {
            var cfg = KeyboardModelDatabase.GetConfigByModelName("OMEN Gaming Laptop 16-am0xxx");

            cfg.Should().NotBeNull();
            cfg!.ProductId.Should().Be("am0xxx_intel_2025_unverified");
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.Notes.Should().Contain("GitHub #124");
        }

        [Fact]
        public void GetConfigByModelName_ReturnsVictus15Fb1Exact8C30_ForIssue135()
        {
            var cfg = KeyboardModelDatabase.GetConfigByModelName("Victus by HP Gaming Laptop 15-fb1xxx");

            cfg.Should().NotBeNull();
            cfg!.ProductId.Should().Be("8C30");
            cfg.ModelName.Should().Contain("15-fb1xxx");
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.Notes.Should().Contain("GitHub #135 diagnostics");
        }

        [Fact]
        public void GetConfig_8D2F_IsVerifiedExactKeyboardProfile()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8D2F");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("16-am0xxx");
            cfg.KeyboardType.Should().Be(KeyboardType.FourZone);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.UserVerified.Should().BeTrue();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8787()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8787");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("15-en0038ur");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZoneTkl);
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_878C()
        {
            var cfg = KeyboardModelDatabase.GetConfig("878C");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("15-ek0");
            cfg.ModelNamePattern.Should().Be("15-ek0");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.ColorTable2020);
            cfg.KeyboardType.Should().Be(KeyboardType.FourZoneTkl);
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("Sky");
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8574()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8574");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("15-dc1");
            cfg.ModelNamePattern.Should().Be("15-dc1");
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.FallbackMethods.Should().BeEmpty();
            cfg.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_8600()
        {
            var cfg = KeyboardModelDatabase.GetConfig("8600");

            cfg.Should().NotBeNull();
            cfg!.ModelName.Should().Contain("15-dh0");
            cfg.ModelNamePattern.Should().Be("15-dh0");
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.FallbackMethods.Should().BeEmpty();
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("wafflist");
        }

        [Fact]
        public void GetDefaultVictusConfig_IsBacklightOnly()
        {
            var cfg = KeyboardModelDatabase.GetDefaultVictusConfig(2024);

            cfg.ModelName.Should().Contain("Victus");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.FallbackMethods.Should().BeEmpty();
        }

        /// <summary>
        /// Issue #128: ProductId 88EC must have explicit keyboard mapping to prevent
        /// unknown fallback on Victus 16-e0xxx systems.
        /// </summary>
        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_88EC()
        {
            var cfg = KeyboardModelDatabase.GetConfig("88EC");
            cfg.Should().NotBeNull("88EC must have explicit keyboard entry");
            cfg!.ProductId.Should().Be("88EC");
            cfg.ModelName.Should().Contain("Victus 16");
            cfg.ModelNamePattern.Should().Be("16-e0");
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly, "conservative default pending RGB verification");
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.FallbackMethods.Should().BeEmpty("no fallbacks for explicit mapping");
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("Issue #128");
        }

        [Fact]
        public void GetConfig_ReturnsConfig_For_ProductId_88EE()
        {
            var cfg = KeyboardModelDatabase.GetConfig("88EE");

            cfg.Should().NotBeNull("88EE must have an exact keyboard entry for GitHub #140");
            cfg!.ProductId.Should().Be("88EE");
            cfg.ModelName.Should().Contain("e0194nw");
            cfg.ModelNamePattern.Should().Be("16-e0");
            cfg.KeyboardType.Should().Be(KeyboardType.BacklightOnly);
            cfg.PreferredMethod.Should().Be(KeyboardMethod.BacklightOnly);
            cfg.FallbackMethods.Should().BeEmpty();
            cfg.UserVerified.Should().BeFalse();
            cfg.Notes.Should().Contain("GitHub #140");
        }
    }
}
