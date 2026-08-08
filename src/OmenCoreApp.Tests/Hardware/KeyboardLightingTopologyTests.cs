using System;
using FluentAssertions;
using OmenCore.Hardware;
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
