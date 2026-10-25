using System;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Pins the keyboard lighting topology decode and how it maps onto capability flags.
    ///
    /// The behaviour these guard was measured on board 8D87 (OMEN MAX 16, 2025 AMD): the topology
    /// probe (Default 0x2B) returns a stable 0x03 = RgbPerKey, while the command usually called
    /// "GetKbdType" (Keyboard 0x01) returns a byte that grows on every identical call and
    /// saturates at 0xFF. The point of these tests is that a per-key board must never come out of
    /// detection claiming four-zone lighting.
    /// </summary>
    public class KeyboardLightingTopologyTests
    {
        [Theory]
        [InlineData(0xFF, HpWmiBios.KeyboardLightingType.None)]        // -1 as a signed byte
        [InlineData(0x00, HpWmiBios.KeyboardLightingType.Normal)]
        [InlineData(0x01, HpWmiBios.KeyboardLightingType.FourZoneWithNumpad)]
        [InlineData(0x02, HpWmiBios.KeyboardLightingType.FourZoneWithoutNumpad)]
        [InlineData(0x03, HpWmiBios.KeyboardLightingType.RgbPerKey)]
        [InlineData(0x04, HpWmiBios.KeyboardLightingType.OneZoneWithNumpad)]
        [InlineData(0x05, HpWmiBios.KeyboardLightingType.OneZoneWithoutNumpad)]
        public void LightingType_CoversHpsEnumeration(byte raw, HpWmiBios.KeyboardLightingType expected)
        {
            ((HpWmiBios.KeyboardLightingType)(sbyte)raw).Should().Be(expected);
        }

        [Fact]
        public void LightingType_None_IsMinusOne_NotTwoFiftyFive()
        {
            // The probe returns a single byte and None is -1, so it arrives as 0xFF. Decoding it
            // unsigned would land on 255, which is not a declared value, and the board would be
            // reported as "unknown topology" rather than "no keyboard lighting".
            ((int)HpWmiBios.KeyboardLightingType.None).Should().Be(-1);
            ((HpWmiBios.KeyboardLightingType)unchecked((sbyte)0xFF)).Should().Be(HpWmiBios.KeyboardLightingType.None);
        }

        [Fact]
        public void MapLightingTypeToKbdType_OnlyClaimsPerKey()
        {
            // KbdType describes key LAYOUT, the topology describes LIGHTING. Only per-key has an
            // honest counterpart; a four-zone keyboard could have any layout, so claiming one
            // would be inventing an answer the probe never gave.
            HpWmiBios.MapLightingTypeToKbdType(HpWmiBios.KeyboardLightingType.RgbPerKey)
                .Should().Be(HpWmiBios.KbdType.PerKeyRgb);

            foreach (HpWmiBios.KeyboardLightingType t in Enum.GetValues<HpWmiBios.KeyboardLightingType>())
            {
                if (t == HpWmiBios.KeyboardLightingType.RgbPerKey) continue;
                HpWmiBios.MapLightingTypeToKbdType(t).Should().BeNull(
                    $"{t} says nothing about the key layout");
            }
        }

        [Fact]
        public void MapLightingTypeToKbdType_NoAnswerStaysNoAnswer()
        {
            HpWmiBios.MapLightingTypeToKbdType(null).Should().BeNull();
        }

        [Theory]
        [InlineData(HpWmiBios.KeyboardLightingType.OneZoneWithNumpad, 1)]
        [InlineData(HpWmiBios.KeyboardLightingType.OneZoneWithoutNumpad, 1)]
        [InlineData(HpWmiBios.KeyboardLightingType.FourZoneWithNumpad, 4)]
        [InlineData(HpWmiBios.KeyboardLightingType.FourZoneWithoutNumpad, 4)]
        [InlineData(HpWmiBios.KeyboardLightingType.RgbPerKey, 4)]
        [InlineData(HpWmiBios.KeyboardLightingType.Normal, 4)]
        [InlineData(HpWmiBios.KeyboardLightingType.None, 4)]
        public void MapLightingTypeToZoneCount_OnlySingleZoneTopologiesDeviateFromFour(
            HpWmiBios.KeyboardLightingType topology, byte expectedZoneCount)
        {
            // GitHub #212 (board 8BD4): a firmware-reported single-zone keyboard rejected every
            // ColorTable write that declared 4 zones in byte 0. Only the two single-zone topologies
            // should report anything other than the historical default of 4 - RgbPerKey has its own
            // command surface and never reaches SetColorTable, and Normal/None have no colour
            // control to write at all, so their zone count is moot but stays at the old default
            // rather than inventing a new meaning for it.
            HpWmiBios.MapLightingTypeToZoneCount(topology).Should().Be(expectedZoneCount);
        }

        [Fact]
        public void MapLightingTypeToZoneCount_NoAnswerStaysAtTheHistoricalDefault()
        {
            HpWmiBios.MapLightingTypeToZoneCount(null).Should().Be(4);
        }

        private static readonly byte[] FourSlotRed =
            { 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x11, 0x22, 0x33 };

        [Fact]
        public void BuildColorTable_FourZone_PutsColoursAtOffset25AndDeclaresFour()
        {
            // 8A13 and 8787 verify this layout; it must not move.
            var t = HpWmiBios.BuildColorTable(FourSlotRed, 4);

            t.Should().HaveCount(128);
            t[0].Should().Be(4);
            t.AsSpan(1, 24).ToArray().Should().OnlyContain(b => b == 0);
            t.AsSpan(25, 12).ToArray().Should().Equal(FourSlotRed);
        }

        [Fact]
        public void BuildColorTable_OneZone_PutsTheColourRightAfterTheCount()
        {
            // GitHub #212, 8BD4 probe step 2: count 1 with the colour at offsets 1 to 3 lit the keyboard; the same
            // colour at offset 25 did not. Slots 1 to 3 of the four-slot input are not part of a one-zone write.
            var t = HpWmiBios.BuildColorTable(FourSlotRed, 1, oneZoneColourAfterCount: true);

            t.Should().HaveCount(128);
            t[0].Should().Be(1);
            t.AsSpan(1, 3).ToArray().Should().Equal(0xFF, 0x00, 0x00);
            t.AsSpan(4).ToArray().Should().OnlyContain(b => b == 0);
        }

        [Fact]
        public void BuildColorTable_OneZone_WithoutTheBoardFlag_KeepsTheOffset25Payload()
        {
            // GitHub #130, 8E10: answers one-zone but its backlight died on 4.5.0 with the offset-1 layout.
            var t = HpWmiBios.BuildColorTable(FourSlotRed, 1, oneZoneColourAfterCount: false);

            t[0].Should().Be(1);
            t.AsSpan(1, 24).ToArray().Should().OnlyContain(b => b == 0);
            t.AsSpan(25, 12).ToArray().Should().Equal(FourSlotRed);
        }

        [Fact]
        public void KeyboardModelDatabase_OnlyThe8BD4RowUsesTheOneZoneLayout()
        {
            KeyboardModelDatabase.GetConfig("8BD4")!.OneZoneColourAfterCount.Should().BeTrue();
            KeyboardModelDatabase.GetConfig("8E10")?.OneZoneColourAfterCount.Should().NotBe(true);
        }

        [Fact]
        public void BuildColorTable_OneZone_AcceptsAThreeByteColour()
        {
            var t = HpWmiBios.BuildColorTable(new byte[] { 1, 2, 3 }, 1, oneZoneColourAfterCount: true);

            t.AsSpan(0, 4).ToArray().Should().Equal(1, 1, 2, 3);
        }

        [Fact]
        public void DecodeKeyboardType_RejectsTheAccumulatorValues()
        {
            // The exact byte sequence measured from ten identical Keyboard 0x01 calls on 8D87.
            // Every one of them must be rejected: they are not keyboard types, and the older code
            // cast this byte straight to the enum.
            foreach (byte raw in new byte[] { 0x0D, 0x0F, 0x1F, 0x3F, 0x7F, 0xFF })
            {
                HpWmiBios.DecodeKeyboardType(new[] { raw })
                    .Should().BeNull($"0x{raw:X2} is accumulator residue, not a KbdType");
            }
        }

        [Fact]
        public void DecodeKeyboardType_StillAcceptsRealValues()
        {
            // Boards where Keyboard 0x01 does answer must keep working.
            HpWmiBios.DecodeKeyboardType(new byte[] { 0x00 }).Should().Be(HpWmiBios.KbdType.Standard);
            HpWmiBios.DecodeKeyboardType(new byte[] { 0x03 }).Should().Be(HpWmiBios.KbdType.PerKeyRgb);
        }

        [Fact]
        public void Board8D87_IsPerKeyAndNotFourZone()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");

            caps.HasPerKeyRgb.Should().BeTrue("the topology probe returns RgbPerKey on this board");
            caps.HasFourZoneRgb.Should().BeFalse(
                "HP's own four-zone gate returns false for lighting type 3, and on this chassis " +
                "the four-zone commands drive the light bar instead of the keyboard");
            caps.HasKeyboardBacklight.Should().BeTrue();
        }

        /// <summary>
        /// Boards known to claim both, which this test deliberately does not fail on.
        ///
        /// 8D41 is the Intel sibling of 8D87 - same OMEN MAX 16 2025 chassis, Intel Core Ultra
        /// instead of Ryzen AI - and it is marked UserVerified, so the entry is somebody's real
        /// report rather than a guess. On 8D87 the same-looking pair turned out to mean "per-key
        /// keyboard plus a four-zone LIGHT BAR", and 8D41 very likely has that same arrangement.
        /// But "very likely" is not what UserVerified means, and nobody here has an 8D41 to run
        /// the topology probe on, so its flags are left exactly as its owner reported them.
        ///
        /// Settling it needs one command from an 8D41 owner: tools/LightingProbe --wmi. If the
        /// topology reads RgbPerKey, HasFourZoneRgb should become false and HasLightBar true,
        /// and this entry should come off the list.
        /// </summary>
        private static readonly string[] UnsettledDualClaimBoards = { "8D41" };

        [Fact]
        public void NoModelClaimsBothPerKeyAndFourZoneKeyboardRgb()
        {
            // They are different command surfaces. A board claiming both offers the user a zone
            // control that writes somewhere its keyboard does not listen - which is worse than no
            // control, because it reports success.
            foreach (var model in ModelCapabilityDatabase.GetAllModels())
            {
                if (!model.HasPerKeyRgb) continue;
                if (Array.Exists(UnsettledDualClaimBoards,
                                 id => string.Equals(id, model.ProductId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                model.HasFourZoneRgb.Should().BeFalse(
                    $"{model.ProductId} ({model.ModelName}) claims per-key AND four-zone keyboard RGB");
            }
        }

        [Fact]
        public void TheDualClaimExclusionListStaysHonest()
        {
            // If someone fixes one of these entries, the exclusion has to go with it - otherwise
            // the list quietly becomes a place where new violations can hide.
            foreach (string id in UnsettledDualClaimBoards)
            {
                var model = ModelCapabilityDatabase.GetCapabilities(id);
                (model.HasPerKeyRgb && model.HasFourZoneRgb).Should().BeTrue(
                    $"{id} no longer claims both, so it should be removed from UnsettledDualClaimBoards");
            }
        }
    }
}
