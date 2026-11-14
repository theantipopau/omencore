using System.Drawing;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services.KeyboardLighting;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Primax per-key keyboard MCU (0461:4E9A / 4E9B). No hardware here - these pin the parts that
    /// carry the risk: which frames may ever be sent, the map layout, and when detection uses it.
    /// </summary>
    public class PrimaxKeyboardMcuTests
    {
        [Fact]
        public void StoreToFlash_IsNeverAllowed()
        {
            PrimaxKeyboardMcu.IsAllowedCommand(0x0A, 0x00, new byte[] { 0xAC, 0x53 }).Should().BeFalse();
        }

        [Fact]
        public void RestoreOrFirmwareUpdateMode_IsNeverAllowed()
        {
            PrimaxKeyboardMcu.IsAllowedCommand(0x10, 0x07, new byte[] { 0x94, 0x10, 0x98, 0x27 }).Should().BeFalse();
        }

        [Theory]
        [InlineData(0x03)] // effect install
        [InlineData(0x0C)] // brightness
        [InlineData(0x00)]
        [InlineData(0xFF)]
        public void OtherCommands_AreRefused(byte command)
        {
            PrimaxKeyboardMcu.IsAllowedCommand(command, 0x00, new byte[] { 0x01 }).Should().BeFalse();
        }

        [Fact]
        public void DocumentedSafeFrames_AreAllowed()
        {
            PrimaxKeyboardMcu.IsAllowedCommand(0x80, 0x01, System.Array.Empty<byte>()).Should().BeTrue();
            PrimaxKeyboardMcu.IsAllowedCommand(0x80, 0x02, System.Array.Empty<byte>()).Should().BeTrue();
            PrimaxKeyboardMcu.IsAllowedCommand(0x83, 0x00, System.Array.Empty<byte>()).Should().BeTrue();
            PrimaxKeyboardMcu.IsAllowedCommand(0x09, 0x00, new byte[] { 0x00 }).Should().BeTrue();
            PrimaxKeyboardMcu.IsAllowedCommand(0x09, 0x00, new byte[] { 0x01 }).Should().BeTrue();
            for (byte page = 0; page < 3; page++)
            {
                PrimaxKeyboardMcu.IsAllowedCommand(0x05, page, new byte[60]).Should().BeTrue();
                PrimaxKeyboardMcu.IsAllowedCommand(0x06, page, new byte[60]).Should().BeTrue();
                PrimaxKeyboardMcu.IsAllowedCommand(0x07, page, new byte[60]).Should().BeTrue();
            }
        }

        [Fact]
        public void ColourPageBeyondTheThird_AndOddLightingPayload_AreRefused()
        {
            PrimaxKeyboardMcu.IsAllowedCommand(0x05, 3, new byte[60]).Should().BeFalse();
            PrimaxKeyboardMcu.IsAllowedCommand(0x09, 0x00, new byte[] { 0x02 }).Should().BeFalse();
            PrimaxKeyboardMcu.IsAllowedCommand(0x09, 0x00, System.Array.Empty<byte>()).Should().BeFalse();
        }

        [Theory]
        [InlineData(0x4E9A, 168)]
        [InlineData(0x4E9B, 167)]
        public void UniformMap_LightsRealLeds_AndLeavesPaddingZero(int pid, int leds)
        {
            PrimaxKeyboardMcu.GetLedCount((ushort)pid).Should().Be(leds);
            var (r, g, b) = PrimaxKeyboardMcu.BuildUniformMap(leds, 10, 20, 30);

            r.Should().HaveCount(180);
            r[0].Should().Be(10); g[leds - 1].Should().Be(20); b[leds - 1].Should().Be(30);
            r[leds].Should().Be(0); b[179].Should().Be(0);
        }

        [Theory]
        [InlineData(@"\?\HID#VID_0461&PID_4E9A&MI_02#7&abc", true)]
        [InlineData(@"\?\HID#VID_0461&PID_4E9B&MI_02#7&abc", true)]
        [InlineData(@"\?\HID#VID_0461&PID_4F1E&MI_02#7&abc", true)]   // OMEN 16-wf1xxx (8C77)
        [InlineData(@"\?\HID#VID_0461&PID_4F1E&MI_01&COL01#7&abc", false)]
        [InlineData(@"\?\HID#VID_0461&PID_4E9B&MI_00#7&abc", false)]
        [InlineData(@"\?\HID#VID_0461&PID_1234&MI_02#7&abc", false)]
        [InlineData(@"\?\HID#VID_0D62&PID_54BF&MI_02#7&abc", false)]
        public void PathMatching_RequiresPrimaxVidKnownPidAndMi02(string path, bool expected)
        {
            PrimaxKeyboardMcu.IsPrimaxPerKeyPath(path).Should().Be(expected);
        }

        [Fact]
        public void UnknownLedCount_LightsEverySlot()
        {
            PrimaxKeyboardMcu.GetLedCount(PrimaxKeyboardMcu.Wf1Pid).Should().Be(180);
        }

        [Fact]
        public void OnlyFirmwarePerKeyAnswer_ProbesPrimaxFirst()
        {
            KeyboardLightingServiceV2.ShouldProbePrimaxFirst(HpWmiBios.KeyboardLightingType.RgbPerKey).Should().BeTrue();
            KeyboardLightingServiceV2.ShouldProbePrimaxFirst(HpWmiBios.KeyboardLightingType.FourZoneWithoutNumpad).Should().BeFalse();
            KeyboardLightingServiceV2.ShouldProbePrimaxFirst(HpWmiBios.KeyboardLightingType.OneZoneWithNumpad).Should().BeFalse();
            KeyboardLightingServiceV2.ShouldProbePrimaxFirst(null).Should().BeFalse();
        }

        [Fact]
        public void DifferingZones_CollapseToZoneOne_AndBrightnessScales()
        {
            var (color, collapsed) = PrimaxPerKeyBackend.ResolveUniformColor(
                new[] { Color.FromArgb(200, 100, 50), Color.Blue, Color.Blue, Color.Blue }, 50);

            collapsed.Should().BeTrue();
            color.R.Should().Be(100); color.G.Should().Be(50); color.B.Should().Be(25);
        }

        [Fact]
        public void IdenticalZones_AreNotReportedAsCollapsed()
        {
            var (_, collapsed) = PrimaxPerKeyBackend.ResolveUniformColor(
                new[] { Color.Red, Color.Red, Color.Red, Color.Red }, 100);
            collapsed.Should().BeFalse();
        }
    }
}
