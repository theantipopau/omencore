using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Pins the board 8D87 profile (OMEN MAX 16, 2025 AMD) to what was measured on the hardware,
    /// and pins the Vibrance25C1 platform list that bounds any raw EC offset derived from it.
    /// </summary>
    public class ModelCapabilityDatabase8D87Tests
    {
        [Fact]
        public void Board8D87_Profile_Matches_What_Was_Measured_On_Hardware()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.ProductId.Should().Be("8D87");
            // Flipped once the last inherited field - the named performance modes - was measured in
            // delivered watts rather than return codes. Every board-specific claim in the entry is
            // now owner-verified on the hardware.
            caps.UserVerified.Should().BeTrue();

            // The EC on this board is memory-mapped and does not follow the legacy offset layout, so
            // neither EC fan control nor EC power-limit writes may be attempted on it. The power-limit
            // flag is not merely unconfirmed here - forcing that block was measured to change nothing,
            // because it is a mirror the SMU never reads back.
            caps.SupportsFanControlEc.Should().BeFalse();
            caps.SupportsEcPowerLimits.Should().BeFalse();

            // True: this part undervolts via AMD Curve Optimizer, owner-confirmed. The field
            // describes the hardware, not whether OmenCore's SMU backend can drive it today -
            // ShowUndervolt already ANDs this with UndervoltRuntimeReady, so backend gaps surface
            // as AmdUndervoltProvider's RuntimeBlockReason rather than as a false capability claim.
            // RyzenControl's exclusion is (family 0x1A && model >= 0x40); this part is family 0x1A
            // model 0x24, below that bound, so Curve Optimizer is not ruled out here either.
            caps.SupportsUndervolt.Should().BeTrue();

            // Intel thermal-control-circuit knob; there is no such register on a Ryzen AI 9 HX 375.
            caps.SupportsTccOffset.Should().BeFalse();

            // Default 0x28 byte 4 = 0x03: ExtremeMode is SUPPORTED (bit 1) but ExtremeModeUnlock
            // (bit 2) is CLEAR, so the effective capability is false. Guards against a later reader
            // seeing "ExtremeMode supported" in the capability block and flipping this.
            caps.SupportsOverboost.Should().BeFalse();

            // Independently corroborated by the observed performance-mode decay on this machine.
            caps.MaxModeDropChecksBeforeReapply.Should().Be(1);
        }

        [Fact]
        public void Board8D87_PerformanceModes_Are_The_Three_The_Wmi_Path_Can_Actually_Send()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            // "L5P" was inherited from the adjacent AK0003NR entry and is NOT sendable: there is no
            // L5P member in HpWmiBios.FanMode at all, so SetPerformanceMode can never produce it.
            // The name resolves only inside OghServiceProxy.ThermalPolicy, a different transport.
            caps.PerformanceModes.Should().Equal("Default", "Performance", "Cool");
            caps.PerformanceModes.Should().NotContain("L5P");

            // Measured in delivered watts on the hardware: SetFanMode writes NPCF.MODE and the
            // firmware consumes (MODE & 0x0F), so Default (0x30) and Cool (0x50) both select
            // nibble 0 and delivered ~102 W, while Performance (0x31) selects nibble 1 and pinned
            // enforced.power.limit at 175 W. All three return RTCD 0, so the return code never
            // distinguished them - Cool differs as fan policy, not as a power tier.
            caps.SupportsPerformanceModes.Should().BeTrue();
        }

        [Fact]
        public void Board8D87_MaxFanLevel_Is_The_Measured_V1_Ceiling_Not_The_V2_Percentage()
        {
            // Regression guard, and the same defect class as
            // GetCapabilities_8C77_Wf1xxx_UsesExactV1WmiProfileNotV2Mismatch.
            //
            // This board reports thermal policy V1 and rejects the V2 fan commands outright (0x37 ->
            // RTCD 6, 0x38 -> RTCD 4, at every input and output buffer size). Levels therefore come
            // from the V1 0x2D fallback in krpm/100. MapFanPercentToWmiLevel scales a requested
            // percent by MaxFanLevel, so leaving this at 100 makes it an identity and a request for
            // 50% writes raw level 50 - about 5000 rpm on hardware whose ceiling is 6000.
            //
            // 60 is measured: 0x2D against the EC tachometers and OGH's own readout at four points,
            // 0/0, 22/2220, 47/4680 and 60/6000, linear within ~1%.
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.MaxFanLevel.Should().Be(60);
            caps.MaxFanLevel.Should().NotBe(100, "V1 levels are krpm/100, not a percentage");
            caps.FanZoneCount.Should().Be(2);
            caps.SupportsRpmReadback.Should().BeTrue();
        }

        [Fact]
        public void Board8D87_Is_PerKey_Rgb_And_Therefore_Not_FourZone()
        {
            // The keyboard topology probe - class 0x00020008, command 0x2B, null input, 4-byte
            // return - reads 0x03 = NbKeyboardLightingType.RgbPerKey, stable across repeated reads.
            // HP's own FourZoneHelper.IsSupported returns false for type 3, so the four-zone path
            // does not drive this keyboard. HasFourZoneRgb defaults to true on ModelCapabilities,
            // so it must be set explicitly here or the entry claims both at once.
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.HasPerKeyRgb.Should().BeTrue();

            // The four-zone flag gates the four-zone KEYBOARD path, which HP's own
            // FourZoneHelper.IsSupported refuses for lighting type 3. The four-zone commands are
            // not inert on this chassis though - owner-observed, they drive the LIGHT BAR and leave
            // the keyboard alone, which is why "four-zone succeeded" was never evidence here.
            caps.HasFourZoneRgb.Should().BeFalse();
            caps.HasLightBar.Should().BeTrue();
            caps.HasKeyboardBacklight.Should().BeTrue();
            caps.HasFourZoneRgb.Should().BeFalse("HP gates the four-zone path off for lighting type 3");
        }

        [Fact]
        public void Board8D87_Has_A_Mux_But_Not_Advanced_Optimus()
        {
            // These two flags are different claims and this board splits them, so they are pinned
            // together - setting either from the other is the mistake this test exists to catch.
            //
            // HasMuxSwitch: measured by switching BIOS setup from Hybrid to Discrete and back, with
            // a reboot each way. In Discrete the sole display is the internal panel (CMN1652) at its
            // native 2560x1600 driven by the RTX 5080, the Radeon reports no active mode, and
            // nvidia-smi reports display_active = Enabled. Legacy 0x52 byte 0 moves 0x00 -> 0x01 and
            // back. The panel changes which GPU drives it, which is what the flag claims.
            //
            // SupportsAdvancedOptimus: false, and inherited true. Advanced Optimus is the *dynamic*
            // form - the mux moves under live ACPI control, no reboot. The AMD_PBS_SETUP Smart Mux
            // block (0xB6 Support, 0xC7 Acpi Control, 0xC8 Display Panel Multiplexer, 0xCA MDM
            // Support Level) reads 0 in Hybrid, in Discrete, and again after returning to Hybrid.
            // It stays off through a switch that demonstrably works, so routing is decided at boot.
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.HasMuxSwitch.Should().BeTrue(
                "in Discrete the internal panel is driven by the dGPU and Legacy 0x52 reads 0x01");
            caps.SupportsAdvancedOptimus.Should().BeFalse(
                "the Smart Mux block stays Disabled through a working mode switch, so there is no runtime control surface");
        }

        [Fact]
        public void Board8D87_Does_Not_Opt_Into_The_Decoupled_Thermal_Policy_Fallback()
        {
            // Deliberate: that flag changes fan/thermal behaviour and is gated on field confirmation
            // of the behaviour itself, which this board has not had. Read-only identification work
            // does not license it.
            ModelCapabilityDatabase.GetCapabilities("8D87")
                .AllowDecoupledWmiThermalPolicyFallback.Should().BeFalse();
        }

        [Fact]
        public void Vibrance25C1_Platform_List_Covers_The_MAX_Sibling_Boards()
        {
            ModelCapabilityDatabase.Vibrance25C1BoardIds.Should()
                .BeEquivalentTo(new[] { "8D87", "8D88", "8DD5", "8DD6" });

            ModelCapabilityDatabase.IsVibrance25C1Board("8D87").Should().BeTrue();
            ModelCapabilityDatabase.IsVibrance25C1Board("8d88").Should().BeTrue("board IDs are matched case-insensitively");
            ModelCapabilityDatabase.IsVibrance25C1Board("8A44").Should().BeFalse();
            ModelCapabilityDatabase.IsVibrance25C1Board(null).Should().BeFalse();
            ModelCapabilityDatabase.IsVibrance25C1Board("").Should().BeFalse();
        }

        [Fact]
        public void Vibrance25C1_Siblings_Are_Not_Given_Invented_Capability_Profiles()
        {
            // The list is a scope boundary for raw offsets, not a claim about these boards. Only 8D87
            // has been measured; adding database entries for the others would assert fan, RGB and MUX
            // behaviour nobody has confirmed. If a real owner of one of them submits a profile, this
            // test is the thing to update - deliberately, not incidentally.
            foreach (var boardId in new[] { "8D88", "8DD5", "8DD6" })
            {
                ModelCapabilityDatabase.GetCapabilities(boardId).ProductId
                    .Should().NotBe(boardId, $"no measured profile exists for {boardId}");
            }
        }
    }
}
