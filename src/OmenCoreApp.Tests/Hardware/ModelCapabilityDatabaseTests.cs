using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;
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
            caps.HasPerKeyRgb.Should().BeTrue();
        }

        [Theory]
        [InlineData("8A43", OmenModelFamily.OMEN16)]
        [InlineData("8A44", OmenModelFamily.OMEN16)]
        [InlineData("8C76", OmenModelFamily.OMEN16)]
        [InlineData("8C77", OmenModelFamily.OMEN16)]
        [InlineData("8A3E", OmenModelFamily.Victus)]
        [InlineData("8C30", OmenModelFamily.Victus)]
        [InlineData("8DCD", OmenModelFamily.Victus)]
        [InlineData("8A26", OmenModelFamily.Victus)]
        [InlineData("8A25", OmenModelFamily.Victus)]
        [InlineData("8C58", OmenModelFamily.Transcend)]
        [InlineData("8E41", OmenModelFamily.Transcend)]
        // 8D87 is deliberately NOT in this list: the shared assertion below pins newly added
        // entries as UserVerified = false, and 8D87 is now fully owner-verified. Its ProductId,
        // Family and UserVerified are covered by GetCapabilities_8D87_OmenMaxAk0xxx_* below and by
        // ModelCapabilityDatabase8D87Tests, so nothing is lost by removing the row.
        [InlineData("8574", OmenModelFamily.Legacy)]
        [InlineData("8600", OmenModelFamily.Legacy)]
        [InlineData("8787", OmenModelFamily.Legacy)]
        [InlineData("878C", OmenModelFamily.Legacy)]
        [InlineData("88D2", OmenModelFamily.Legacy)]
        [InlineData("88EE", OmenModelFamily.Victus)]
        public void GetCapabilities_Returns_NewlyAdded_ModelEntries(string productId, OmenModelFamily expectedFamily)
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(productId);

            caps.Should().NotBeNull();
            caps.ProductId.Should().Be(productId);
            caps.Family.Should().Be(expectedFamily);
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_Victus8A25_SupportsGpuPowerBoost_DistinctFrom8A26Sibling()
        {
            // Field-confirmed by two independent Discord reporters across v3.8.0/v4.0.0/v4.1.0:
            // RTX 3060 boosts 85W -> 100W via WMI PPAB on this exact board, matching OMEN Gaming Hub.
            var caps8a25 = ModelCapabilityDatabase.GetCapabilities("8A25");
            var caps8a26 = ModelCapabilityDatabase.GetCapabilities("8A26");

            caps8a25.SupportsGpuPowerBoost.Should().BeTrue();
            caps8a26.SupportsGpuPowerBoost.Should().BeFalse();

            // Exact ProductId match must win over the shared "16-d1" ModelNamePattern the two
            // entries both carry, so 8A25 resolves to its own entry rather than falling through
            // to 8A26 (previously the only match, per the field logs' own "no entry for ProductId
            // '8A25'" note).
            var preferred = ModelCapabilityDatabase.GetPreferredCapabilities("8A25", "Victus by HP Laptop 16-d1xxx");
            preferred.Should().NotBeNull();
            preferred!.ProductId.Should().Be("8A25");
            preferred.SupportsGpuPowerBoost.Should().BeTrue();
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
            caps8e41.AllowV1AutoModeFloorClear.Should().BeTrue("8E41 WMI V1 auto handoff must clear stale manual floors after Max/curve attempts");
            caps8e41.Notes.Should().Contain("2026-06-02");
        }

        [Fact]
        public void GetCapabilities_8D87_OmenMaxAk0xxx_UsesMaxSeriesSafetyProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.ProductId.Should().Be("8D87");
            caps.ModelName.Should().Contain("OMEN MAX 16");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.MaxModeDropChecksBeforeReapply.Should().Be(1,
                "8D87 field chat reports fans become disobedient after a while, so MAX-series Max hold should reassert on first low telemetry sample");
            caps.HasPerKeyRgb.Should().BeTrue();
            caps.UserVerified.Should().BeTrue(
                "every board-specific claim in the entry is now owner-verified on the hardware, the last of them being the performance modes measured in delivered watts");
        }

        [Fact]
        public void GetCapabilities_8D41_OmenMaxAh0xxx_UsesWmiPolicyFallback()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D41");

            caps.ProductId.Should().Be("8D41");
            caps.ModelName.Should().Contain("ah0xxx");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "8D41 cannot safely use legacy EC power/fan writes, so Quick Profiles need the OEM WMI thermal-policy path when direct limits are unavailable");
            caps.MaxModeDropChecksBeforeReapply.Should().Be(1,
                "8D41 v3.7.1 logs show firmware reclaiming Max mode repeatedly, so Max hold must reassert on the first low telemetry sample");
            caps.HasPerKeyRgb.Should().BeTrue();
            caps.UserVerified.Should().BeTrue();
        }

        [Theory]
        [InlineData("8BBE")] // #211: RPM is the V1 fan level x100
        [InlineData("8BD4")] // #212 and #226: same
        [InlineData("8BC2")] // #227: same
        public void V1BoardsWhoseExportsShowNoTachometer_DoNotClaimRpmReadback(string productId)
        {
            // The database default is true, so a board that never said otherwise showed "Supported" on the capability card
            // while its RPM was only an estimate (K.3). Set false explicitly, from the export, per board.
            ModelCapabilityDatabase.GetCapabilities(productId).SupportsRpmReadback.Should().BeFalse();
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
        public void GetCapabilities_8A18_Omen17Ck1xxx_UsesConservativeV1Profile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8A18");

            caps.ProductId.Should().Be("8A18");
            caps.ModelName.Should().Contain("17-ck1");
            caps.Family.Should().Be(OmenModelFamily.OMEN17);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.SupportsRpmReadback.Should().BeFalse("V1 fan levels are not independent physical RPM evidence");
            caps.MaxFanLevel.Should().Be(55);
            caps.UserVerified.Should().BeFalse("the submitted logs still require bounded physical validation");
        }

        [Fact]
        public void GetCapabilities_8D40_OmenSlim16An0xxx_UsesConservativeUnclaimedProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D40");

            caps.ProductId.Should().Be("8D40");
            caps.ModelName.Should().Contain("Slim");
            caps.Family.Should().Be(OmenModelFamily.OMEN16);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.HasMuxSwitch.Should().BeFalse("MUX presence is unconfirmed on this new thin chassis");
            caps.HasFourZoneRgb.Should().BeFalse("keyboard/RGB surface has no database match and must not be assumed");
            caps.SupportsUndervolt.Should().BeFalse("CPU vendor/model is not yet confirmed for this board");
            caps.UserVerified.Should().BeFalse("GitHub #145 still requires hardware validation");
        }

        [Fact]
        public void GetPreferredCapabilities_8D40_MatchesOmenSlimWmiModelName()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities(
                "8D40", "OMEN Slim Gaming Laptop 16-an0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8D40");
        }

        [Fact]
        public void GetPreferredCapabilities_878C_Omen15Ek0xxx_UsesWmiThermalPolicyFallback()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("878C", "OMEN Laptop 15-ek0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("878C");
            caps.ModelName.Should().Contain("15-ek0");
            caps.Family.Should().Be(OmenModelFamily.Legacy);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse("878C profiles must avoid unverified direct EC writes and use the OEM WMI policy path");
            caps.SupportsIndependentFanCurves.Should().BeFalse("independent curve ownership is not validated by the field report");
            caps.SupportsRpmReadback.Should().BeTrue("the field screenshots show fan RPM telemetry around 1900 RPM");
            caps.MaxFanLevel.Should().Be(55);
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "Quick Profile Performance/Balanced/Quiet left fans low at 99C, so exact 878C routing must send the OEM WMI thermal policy when EC power limits are unavailable");
            caps.PerformanceCpuPl1Watts.Should().BeNull("the report does not include PL1/PL2 readback proving the correct wattage envelope");
            caps.PerformanceCpuPl2Watts.Should().BeNull("the report does not include PL1/PL2 readback proving the correct wattage envelope");
            caps.Notes.Should().Contain("Sky");
        }

        [Fact]
        public void GetCapabilities_8574_Omen15Dc1xxx_UsesConservativeEcFirstProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8574");

            caps.ProductId.Should().Be("8574");
            caps.ModelName.Should().Contain("15-dc1");
            caps.Family.Should().Be(OmenModelFamily.Legacy);
            caps.SupportsFanControlWmi.Should().BeFalse();
            caps.SupportsFanControlEc.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.HasKeyboardBacklight.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeFalse("RGB capability is held back until this board's protocol is verified");
            caps.SupportsTccOffset.Should().BeFalse();
            caps.SupportsPowerLimits.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetPreferredCapabilities_8600_Omen15Dh0xxx_UsesConservativeWmiPolicyProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8600", "OMEN by HP Laptop 15-dh0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8600");
            caps.ModelName.Should().Contain("15-dh0");
            caps.Family.Should().Be(OmenModelFamily.Legacy);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse("8600 direct EC writes are not validated and the field report had no PawnIO driver");
            caps.SupportsIndependentFanCurves.Should().BeFalse("independent curve ownership is not validated by the field report");
            caps.SupportsRpmReadback.Should().BeFalse("the field report shows 0 RPM for both fans, so RPM readback should not be trusted yet");
            caps.MaxFanLevel.Should().Be(55);
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "fan modes barely worked except Max, so Quick Profiles should still attempt the OEM WMI thermal policy path");
            caps.SupportsPowerLimits.Should().BeFalse("missing PawnIO and 0W CPU power means direct CPU PL controls should remain hidden until readback proves support");
            caps.Notes.Should().Contain("wafflist");
        }

        [Fact]
        public void GetPreferredCapabilities_88D2_Omen15zEn100_UsesConservativeLegacyProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("88D2", "OMEN by HP Laptop 15z-en100");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88D2");
            caps.ModelName.Should().Contain("15z-en100");
            caps.Family.Should().Be(OmenModelFamily.Legacy);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Theory]
        [InlineData("DESKTOP-25L")]
        [InlineData("DESKTOP-30L")]
        [InlineData("DESKTOP-35L")]
        [InlineData("DESKTOP-40L")]
        [InlineData("DESKTOP-45L")]
        public void GetCapabilities_DesktopProfiles_DisableFanWritesForSafetyGate(string productId)
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(productId);

            caps.Family.Should().Be(OmenModelFamily.Desktop);
            caps.SupportsFanControlWmi.Should().BeFalse();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsRpmReadback.Should().BeTrue();
            caps.SupportsPerformanceModes.Should().BeTrue();
            caps.Notes.Should().Contain("v3.6.3 safety gate");
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
        public void GetCapabilities_8C77_Wf1xxx_UsesExactV1WmiProfileNotV2Mismatch()
        {
            // Regression guard: without an exact 8C77 entry, the pattern match falls through to
            // 8BAB (V2, MaxFanLevel=100), which sent wrong commands and caused a crash on the
            // reporter's device (FileNotFoundException on Custom Fan Curve/Quiet mode, 2026-07).
            var caps = ModelCapabilityDatabase.GetCapabilities("8C77");

            caps.ProductId.Should().Be("8C77");
            caps.ModelName.Should().Contain("wf1xxx");
            caps.Family.Should().Be(OmenModelFamily.OMEN16);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);   // V1 WMI — must NOT be 100 (V2/8BAB mismatch)
            caps.HasMuxSwitch.Should().BeTrue();
            caps.SupportsGpuPowerBoost.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8E35_Ap0xxxAmd_UsesExactV1WmiProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8E35");

            caps.ProductId.Should().Be("8E35");
            caps.ModelName.Should().Contain("ap0xxx");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsUndervolt.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8E35_AllowsDecoupledWmiThermalPolicyFallback()
        {
            // GitHub #195 ran a controlled Quiet-vs-Performance test showing a 0.0 W CPU package
            // power difference, with the Apply Trace confirming Direct EC writes are disabled and
            // the WMI thermal-policy fallback was never attempted - Performance mode changed
            // nothing on this board. Enabling this flag gives it the same fallback path 88EC/8CC0
            // already have for the same "EC disabled, no fallback" shape.
            var caps = ModelCapabilityDatabase.GetCapabilities("8E35");

            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
        }

        // GitHub #130/#171: was resolving via OMEN17 family fallback ("Model not in database").
        // MaxFanLevel is pinned at 45 (not the sibling boards' nominal 55) because #171's own
        // Guided Fan Verification measured this exact board's real Max-hold ceiling at 45 -
        // using 55 would make WmiFanController's Max-mode floor check permanently unwinnable on
        // this board and produce an endless reassert loop, a real regression, not just a wrong
        // number. MuxSwitch/GpuPowerBoost are conservatively false, not inherited from the
        // 16-ap0xxx siblings' shared CPU generation, since neither is confirmed on this chassis.
        [Fact]
        public void GetCapabilities_8E10_Db1xxx_UsesExactV1WmiProfile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8E10");

            caps.ProductId.Should().Be("8E10");
            caps.ModelName.Should().Contain("17-db1");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsUndervolt.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8E10_MaxFanLevel_MatchesFieldConfirmedRealCeiling_NotNominal()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8E10");

            caps.MaxFanLevel.Should().Be(45,
                "GitHub #171's Guided Fan Verification measured the real Max-hold ceiling at 45 - the nominal 55 used by sibling boards is unreachable on this board and would make the Max-mode floor check permanently unwinnable, producing an endless reassert loop");
        }

        [Fact]
        public void GetCapabilities_8E10_DoesNotClaimUnconfirmedMuxOrGpuPowerBoost()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8E10");

            caps.HasMuxSwitch.Should().BeFalse("not confirmed on this board - do not inherit from the 16-ap0xxx siblings' shared CPU generation");
            caps.SupportsGpuPowerBoost.Should().BeFalse("not confirmed on this board - do not inherit from the 16-ap0xxx siblings' shared CPU generation");
        }

        [Fact]
        public void GetCapabilities_8D26_Ap0xxxAmd_UsesExactV1WmiProfile()
        {
            // GitHub #188: was falling back to 8D24 via model-name pattern match; this confirms
            // the new exact-ProductId entry carries the same V1 WMI profile 8D24/8E35 already use.
            var caps = ModelCapabilityDatabase.GetCapabilities("8D26");

            caps.ProductId.Should().Be("8D26");
            caps.ModelName.Should().Contain("ap0xxx");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsUndervolt.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_88D1_Omen15En1xxx_HasItsOwnConservativeEntry()
        {
            // #228: resolved by model name to the 8787 profile before. Measured V1, 2 fans, level readback only.
            var caps = ModelCapabilityDatabase.GetCapabilities("88D1");

            caps.ProductId.Should().Be("88D1");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.HasMuxSwitch.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Theory]
        [InlineData("8C4D")]
        [InlineData("8BB3")]
        [InlineData("88C8")]
        [InlineData("8C99")]
        [InlineData("8BD5")]
        public void GetCapabilities_IdentityOnlyBoards_AreKnownButClaimNothing(string id)
        {
            var caps = ModelCapabilityDatabase.GetCapabilities(id);

            caps.ProductId.Should().Be(id);
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8F5C_HyperxOmen15Ga0xxx_HasItsOwnConservativeEntry()
        {
            // #235: used to fall to the Unknown Legacy family default. Measured V0, 2 fans, level readback only, colour write unverified.
            var caps = ModelCapabilityDatabase.GetCapabilities("8F5C");

            caps.ProductId.Should().Be("8F5C");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(); // presets were a no-op without it (#235)
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8F21_HyperxOmen16Ap1xxx_HasItsOwnConservativeEntry()
        {
            // #230: used to fall to the Unknown OMEN16 family default. Measured V1, 2 fans, level readback only.
            var caps = ModelCapabilityDatabase.GetCapabilities("8F21");

            caps.ProductId.Should().Be("8F21");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8E41_UsesTheFirmwaresTwoFansAndHasNoRealRpm()
        {
            // #157: the firmware reports 2 fans (the database said 1) and RPM readback is null.
            var caps = ModelCapabilityDatabase.GetCapabilities("8E41");

            caps.FanZoneCount.Should().Be(2);
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8BC2_Victus16R0xxxIntel_HasItsOwnConservativeEntry()
        {
            // #227: measured V1, 2 fans, level readback only, one-zone keyboard. Resolved by model name to 8BBE before.
            var caps = ModelCapabilityDatabase.GetCapabilities("8BC2");

            caps.ProductId.Should().Be("8BC2");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsRpmReadback.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8BD4_Victus16S0xxx_UsesExactConservativeProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BD4", "Victus by HP Gaming Laptop 16-s0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BD4");
            caps.ModelName.Should().Contain("16-s0");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.HasFourZoneRgb.Should().BeTrue("Victus 16-s0xxx / 7Z5Z2EA reports point to a WMI ColorTable RGB keyboard path");
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.AllowV1AutoModeFloorClear.Should().BeFalse("8BD4 v3.7.1 logs show zero-duty / non-reactive fan symptoms after SetFanLevel(0,0)");
            caps.Notes.Should().Contain("2026-06-07");
        }

        [Fact]
        public void GetCapabilities_8C9C_VictusSR_UsesConservativeProfileInheritedFrom8BD4()
        {
            // GitHub #191: previously resolved only via Family fallback (Low confidence). Board
            // generation confirmed via cross-reference to an external project (Ohman) citing
            // Linux hp-wmi's own board table, which groups 8C9C with 8B2F/8BBE/8BD4/8BD5/8C99 as
            // a Victus 16 S/R (2023-2024) board - so flags mirror the same-generation, same-vendor
            // 8BD4 entry rather than the generic Family-fallback template.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8C9C", "Victus by HP Gaming Laptop 16-s0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8C9C");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2);
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.SupportsGpuPowerBoost.Should().BeFalse("not independently verified on this exact board yet");
            caps.SupportsUndervolt.Should().BeFalse("not independently verified on this exact board yet");
            caps.UserVerified.Should().BeFalse();
            caps.Notes.Should().Contain("#191");
        }

        [Fact]
        public void GetCapabilities_8DD2_VictusFb3xxx_ResolvesToExactEntryInsteadOfNamePattern()
        {
            // GitHub #197: confirmed the exact ProductId (8DD2) that #148's earlier "15-fb3"
            // name-pattern entry was waiting on. Flags must match #148's entry verbatim - this is
            // an identity-confidence upgrade (exact match instead of pattern match), not a widened
            // capability claim, since no diagnostics export accompanied #197's report.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8DD2", "Victus by HP Gaming Laptop 15-fb3xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8DD2");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeFalse("reporter confirms no RGB keyboard on this unit");
            caps.HasKeyboardBacklight.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
            caps.UserVerified.Should().BeFalse();
            caps.Notes.Should().Contain("#197");
        }

        [Fact]
        public void GetCapabilities_8CC0_Omen16Ae0xxx_ResolvesExactlyAndStaysConservative()
        {
            // GitHub #204: previously OMEN16 family fallback, where Performance mode applied nothing.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8CC0", "OMEN Gaming Laptop 16-ae0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8CC0");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue("Performance must not be a no-op when direct EC writes are unavailable");
            caps.SupportsFanCurves.Should().BeFalse("no evidence yet that curves work on this board");
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
            caps.Notes.Should().Contain("#204");
        }

        [Fact]
        public void GetCapabilities_8DD0_VictusFb3xxx_ResolvesExactly_WithFanControlKept()
        {
            // PR #200: this board hit the v4.3.1 fan-control regression. The exact entry must keep
            // WMI fan control on and win over the shared 15-fb3 pattern entry.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8DD0", "Victus by HP Gaming Laptop 15-fb3xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8DD0");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.UserVerified.Should().BeFalse("one contributor's run is not the full verification card");
        }

        [Fact]
        public void ModelIdentitySummary_8BD4_ReportsExactProductId()
        {
            var modelConfig = ModelCapabilityDatabase.GetPreferredCapabilities("8BD4", "Victus by HP Gaming Laptop 16-s0xxx");
            var systemInfo = new SystemInfo
            {
                Manufacturer = "HP",
                Model = "Victus by HP Gaming Laptop 16-s0xxx",
                ProductName = "8BD4",
                SystemSku = "CND3440GJ3"
            };
            var capabilities = new DeviceCapabilities
            {
                ProductId = "8BD4",
                ModelName = "Victus by HP Gaming Laptop 16-s0xxx",
                ModelFamily = OmenModelFamily.Victus,
                IsKnownModel = true,
                ModelConfig = modelConfig
            };

            var summary = ModelIdentityResolutionService.Build(systemInfo, capabilities);

            summary.ResolutionSource.Should().Be("Exact ProductId");
            summary.Confidence.Should().Be("Medium");
            summary.WarningText.Should().Contain("ProductId");
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
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
        }

        [Fact]
        public void GetCapabilitiesByModelName_Victus15Fb1_PrefersExact8C30Profile()
        {
            var caps = ModelCapabilityDatabase.GetCapabilitiesByModelName("Victus by HP Gaming Laptop 15-fb1xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8C30");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
            caps.SupportsPowerLimits.Should().BeFalse("issue #139 has no PL1/PL2 readback, so the CPU power-limit UI must stay hidden for this exact Victus board");
            caps.PerformanceModes.Should().Equal("Quiet", "Balanced", "Performance");
            caps.Notes.Should().Contain("GitHub #135/#139");
        }

        [Fact]
        public void GetPreferredCapabilities_8C30_Victus15Fb1_UsesExactProductProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8C30", "Victus by HP Gaming Laptop 15-fb1xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8C30");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
            caps.SupportsPowerLimits.Should().BeFalse("Performance/Balanced/Quiet are currently WMI policy routes, not proven CPU PL routes on 8C30");
            caps.PerformanceModes.Should().Equal("Quiet", "Balanced", "Performance");
            caps.PerformanceCpuPl1Watts.Should().BeNull("issue #139 reports no visible wattage delta but does not provide a safe target PL1");
            caps.PerformanceCpuPl2Watts.Should().BeNull("issue #139 does not provide a safe target PL2");
            caps.Notes.Should().Contain("GitHub #135/#139");
        }

        [Fact]
        public void GetPreferredCapabilities_8DCD_Victus15_UsesConservativeWmiThermalPolicyFallback()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8DCD", "Victus by HP Gaming Laptop 15");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8DCD");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "GitHub #138 reports Performance mode remaining EC-limited, so this exact board should use the OEM WMI thermal-policy fallback until wattage readback is validated");
            caps.PerformanceCpuPl1Watts.Should().BeNull("issue #138 does not include diagnostics proving the correct PL1");
            caps.PerformanceCpuPl2Watts.Should().BeNull("issue #138 does not include diagnostics proving the correct PL2");
            caps.Notes.Should().Contain("GitHub #138");
        }

        [Fact]
        public void GetPreferredCapabilities_8D2F_UsesConfirmedConservativeAm0Profile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8D2F", "OMEN Gaming Laptop 16-am0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8D2F");
            caps.ModelName.Should().Contain("16-am0");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsIndependentFanCurves.Should().BeFalse();
            caps.SupportsUndervolt.Should().BeFalse();
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue();
            caps.AllowV1AutoModeFloorClear.Should().BeTrue("8D2F V1 auto handoff must clear the manual floor so fans can ramp down after load");
            caps.UserVerified.Should().BeTrue("the exact 8D2F board identity has field confirmation, even though risky direct EC features stay disabled");
        }

        [Fact]
        public void GetPreferredCapabilities_8A43_WithN0xxModel_PrefersExactProductIdOverSiblingPattern()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8A43", "OMEN Gaming Laptop 16-n0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8A43");
            caps.MaxFanLevel.Should().Be(60, "8A43 diagnostics show practical fan-level ceiling near 60 (GPU ~60, CPU ~58)");
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

        [Fact]
        public void GetPreferredCapabilities_Ambiguous8Bb1_DoesNotClaimASpecificYear()
        {
            // GitHub #202: the entry used to say "(2022)" unconditionally, which was simply wrong
            // for a reporter's confirmed 2024 fa1082wm unit - the 15-fa1 name pattern spans more
            // than one year and cannot tell them apart, so it should not assert one.
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BB1", "Victus by HP Gaming Laptop 15-fa1xxx");

            caps.Should().NotBeNull();
            caps!.ModelName.Should().NotContain("(2022)");
            caps.ModelName.Should().NotContain("(2024)");
        }

        [Fact]
        public void GetCapabilities_88F8_ResolvesExactly_WithFirmwareFanCountAndNoRgb()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("88F8");

            caps.ProductId.Should().Be("88F8");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.FanZoneCount.Should().Be(2, "firmware reports two fans; the family default of 1 was wrong here");
            caps.HasFourZoneRgb.Should().BeFalse("the topology probe reports Normal - backlight only");
            caps.HasKeyboardBacklight.Should().BeTrue();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.UserVerified.Should().BeTrue("fans heard at every step (#207)");
        }

        [Fact]
        public void GetCapabilities_8BC8_VictusFa1xxx_HasItsOwnEntryWithTwoFans()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8BC8");

            caps.ProductId.Should().Be("8BC8");
            caps.FanZoneCount.Should().Be(2, "firmware reports two fans; the 15-fa1xxx family entry says one");
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8786_OmenEn0002_DoesNotInheritThe8787SiblingFlags()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8786");

            caps.ProductId.Should().Be("8786");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse("the firmware refuses direct level writes (#239)");
            caps.HasMuxSwitch.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_878A_ResolvesExactly_WithEvidenceBackedFlags()
        {
            // GitHub #218: was FAMILY_LEGACY. Fan-mode writes + level readback confirmed; RPM readback null.
            var caps = ModelCapabilityDatabase.GetCapabilities("878A");

            caps.ProductId.Should().Be("878A");
            caps.Family.Should().Be(OmenModelFamily.Legacy);
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsRpmReadback.Should().BeFalse("the export's RPM readback returned null");
            caps.SupportsFanCurves.Should().BeFalse("no Guided Fan Verification yet");
            caps.HasMuxSwitch.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8C2D_ResolvesExactly_WithBothFirmwareFans()
        {
            // GitHub #205: was resolving to 8BB1-VICTUS15 by name pattern, which reports one fan.
            var caps = ModelCapabilityDatabase.GetCapabilities("8C2D");

            caps.ProductId.Should().Be("8C2D");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse("fan writes not yet exercised on this board");
            caps.HasFourZoneRgb.Should().BeFalse();
            caps.SupportsGpuPowerBoost.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_88F7_ResolvesExactly_WithVerifiedLevelWritesAndMuxOff()
        {
            // GitHub #215: was Family fallback. Level writes verified at 30/60% -> curves on;
            // MUX reported by WMI but never exercised -> stays off.
            var caps = ModelCapabilityDatabase.GetCapabilities("88F7");

            caps.ProductId.Should().Be("88F7");
            caps.Family.Should().Be(OmenModelFamily.OMEN17);
            caps.FanZoneCount.Should().Be(2);
            caps.MaxFanLevel.Should().Be(55);
            caps.SupportsFanCurves.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeTrue();
            caps.HasMuxSwitch.Should().BeFalse();
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8BBE_ResolvesExactly_NoLongerFamilyFallback()
        {
            // GitHub #211: this board was Family fallback only (IsKnownModel: no) until its own
            // diagnostics export confirmed WMI fan control and V1 policy live.
            var caps = ModelCapabilityDatabase.GetCapabilities("8BBE");

            caps.ProductId.Should().Be("8BBE");
            caps.RequiredCpuVendor.Should().Be(CpuUndervoltProviderFactory.CpuVendor.Intel,
                "8C2F already guards against this exact board's WMI name pattern matching its AMD-only profile");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeFalse("not exercised in the #211 export");
            caps.UserVerified.Should().BeFalse();
        }

        [Fact]
        public void GetCapabilities_8BD4_KeepsHasFourZoneRgbTrue_DespiteTheOneZoneFirmwareReport()
        {
            // GitHub #212: flipping this to false does not make the RGB write correct - it trips
            // CapabilityDetectionService's zone-lighting-disabled branch and falls back to
            // LightingCapability.SingleColor, which means "on/off only, no colour", strictly worse
            // than the wrong-zone-count write this board already has. See the long Notes on this
            // entry for the real (still open) architecture gap.
            var caps = ModelCapabilityDatabase.GetCapabilities("8BD4");

            caps.HasFourZoneRgb.Should().BeTrue();
            caps.Notes.Should().Contain("#212");
        }

        /// <summary>
        /// GitHub #163 / Discord (Trirez): HP OMEN 16 XF0079AX, Ryzen 7 7840HS + RTX 4070,
        /// resolved to the Intel wf0xxx 8BCA entry via exact ProductId - same board/ProductId
        /// as the AMD xf0xxx SKU, but the wrong CPU vendor's profile, breaking keyboard control.
        /// The WMI model name does distinguish the two ("16-xf0xxx" vs "16-wf0xxx") even though
        /// the ProductId does not, so 8BCA is disambiguated the same way as 8BB1/Victus 15-fa1xxx.
        /// </summary>
        [Fact]
        public void GetPreferredCapabilities_Ambiguous8Bca_UsesModelNameDisambiguationForAmdVariant()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BCA", "OMEN by HP Gaming Laptop 16-xf0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BCA-AMD");
            caps.ModelName.Should().Contain("AMD");
            caps.SupportsUndervolt.Should().BeFalse("AMD part, no Intel MSR undervolt path");
            ModelCapabilityDatabase.IsAmbiguousProductId("8BCA").Should().BeTrue();
        }

        /// <summary>
        /// The original Intel wf0xxx profile must still resolve correctly for its own WMI model
        /// name - marking 8BCA ambiguous must not break the sibling it was already serving.
        /// </summary>
        [Fact]
        public void GetPreferredCapabilities_8Bca_StillResolvesIntelSiblingByModelName()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BCA", "OMEN by HP Gaming Laptop 16-wf0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BCA");
            caps.ModelName.Should().Contain("Intel");
        }

        /// <summary>
        /// Issue #128: ProductId 88EC must resolve to explicit Victus e0xxx mapping,
        /// not a broad family fallback. This ensures consistent identity on field systems.
        /// </summary>
        [Fact]
        public void GetCapabilities_88EC_ResolvesToExplicitVictusE0xxxMapping()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("88EC");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88EC", "explicit 88EC entry must exist");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.ModelName.Should().Contain("Victus 16");
            caps.ModelNamePattern.Should().Be("16-e0");
            caps.UserVerified.Should().BeFalse("pending field verification");
            caps.Notes.Should().Contain("Issue #128");
        }

        /// <summary>
        /// Issue #128: Victus 88EC capability flags are conservative pending field verification.
        /// No speculation about features without hardware evidence.
        /// </summary>
        [Fact]
        public void GetCapabilities_88EC_UsesConservativeFeatureFlags()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("88EC");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88EC");
            caps.SupportsFanControlWmi.Should().BeTrue("WMI fan control is expected on Victus");
            caps.SupportsFanCurves.Should().BeTrue("curve support expected");
            caps.HasFourZoneRgb.Should().BeFalse("no RGB proof yet");
            caps.SupportsGpuPowerBoost.Should().BeFalse("no power boost proof yet");
            caps.SupportsUndervolt.Should().BeFalse("no undervolt proof yet");
            caps.HasKeyboardBacklight.Should().BeTrue("keyboard backlight expected");
        }

        // Follow-up diagnostics on #128 confirmed live in the field: "Performance mode 'Balanced':
        // nothing was applied (Direct EC writes disabled for model 'HP Victus 16-e0xxx')". 88EC
        // never got the AllowDecoupledWmiThermalPolicyFallback flag that 8DCD/8C30/878C/8600 all
        // already have, so PerformanceModeService had no fallback path once Direct EC was
        // (correctly) disabled - switching modes silently did nothing at all.
        [Fact]
        public void GetCapabilities_88EC_AllowsDecoupledWmiThermalPolicyFallback()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("88EC");

            caps.Should().NotBeNull();
            caps!.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "Direct EC writes are disabled on this board, so performance mode switches need the WMI thermal-policy fallback to have any effect at all");
        }

        [Fact]
        public void GetPreferredCapabilities_88ED_ResolvesExactly_WithFanControlAndFallbackKept()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("88ED", "Victus by HP Laptop 16-e0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88ED", "GitHub #209 reports Baseboard ProductId 88ED");
            caps.ModelNamePattern.Should().Be("16-e0");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanCurves.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeFalse("conservative until 88ED's own RGB hardware is field-verified");
            caps.AllowDecoupledWmiThermalPolicyFallback.Should().BeTrue(
                "sibling 88EC needed this for Performance-mode switching to do anything, since Direct EC writes are disabled on this family");
            caps.UserVerified.Should().BeFalse("a support request with no diagnostics bundle isn't a field verification");
            caps.Notes.Should().Contain("GitHub #209");
        }

        [Fact]
        public void GetPreferredCapabilities_88EE_ResolvesToExactVictusE0194nwMapping()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("88EE", "Victus by HP Laptop 16-e0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("88EE", "GitHub #140 reports Baseboard ProductId 88EE");
            caps.ModelName.Should().Contain("e0194nw");
            caps.ModelNamePattern.Should().Be("16-e0");
            caps.Family.Should().Be(OmenModelFamily.Victus);
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsFanCurves.Should().BeFalse();
            caps.HasFourZoneRgb.Should().BeFalse("conservative until the exact 88EE keyboard/RGB hardware is field-verified");
            caps.Notes.Should().Contain("GitHub #140");
        }

        [Fact]
        public void GetCapabilities_8BCD_UsesConservativeWmiV1FanProfile()
        {
            var caps = ModelCapabilityDatabase.GetPreferredCapabilities("8BCD", "OMEN by HP Gaming Laptop 16-xd0xxx");

            caps.Should().NotBeNull();
            caps!.ProductId.Should().Be("8BCD");
            caps.SupportsFanControlWmi.Should().BeTrue();
            caps.SupportsFanControlEc.Should().BeFalse("8BCD field evidence points at V1 WMI fan control, not validated direct EC fan writes");
            caps.SupportsIndependentFanCurves.Should().BeFalse("independent curve UI requires validated independent fan ownership");
            caps.MaxFanLevel.Should().Be(63, "latest field evidence shows this board reaches the 63-level ceiling (~6300 RPM)");
            caps.AllowV1AutoModeFloorClear.Should().BeTrue("8BCD v3.7.0 logs repeatedly skipped V1 zero-floor clear while fans stayed elevated after load");
            caps.UserVerified.Should().BeFalse("2026-05-20 Discord report still needs physical follow-up after the 3.7.0 fixes");
            caps.Notes.Should().Contain("2026-06-05/06");
        }
    }
}
