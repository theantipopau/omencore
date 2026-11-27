using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Rgb;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class OpenRgbProviderTests
    {
        [Fact]
        public void ProviderMetadata_ReportsExpectedIdAndSupportedEffects()
        {
            using var logging = new LoggingService();
            var provider = new OpenRgbProvider(logging);

            provider.ProviderId.Should().Be("openrgb");
            provider.ProviderName.Should().Be("OpenRGB");
            provider.SupportedEffects.Should().Contain(new[]
            {
                RgbEffectType.Static,
                RgbEffectType.Breathing,
                RgbEffectType.Spectrum,
                RgbEffectType.Wave,
                RgbEffectType.Off
            });
        }

        [Fact]
        public void BuiltInScenes_OmenRed_PreservesBrandColor()
        {
            using var logging = new LoggingService();
            var manager = new RgbManager(logging);
            using var sceneService = new RgbSceneService(logging, manager);

            var omenRed = sceneService.GetScene("omen-red");
            omenRed.Should().NotBeNull();
            omenRed!.PrimaryColor.Should().Be("#E6002E", "the laptop keyboard should keep the OMEN brand color #E6002E");
        }

        [Fact]
        public void NormalizeColorForOpenRgb_MapsOmenBrandRedToPureRed()
        {
            var method = typeof(OpenRgbProvider).GetMethod("NormalizeColorForOpenRgb",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull();

            var mappedRed = (Color)method!.Invoke(null, new object[] { Color.FromArgb(230, 0, 46) })!;
            mappedRed.R.Should().Be(255);
            mappedRed.G.Should().Be(0);
            mappedRed.B.Should().Be(0);

            var blue = Color.FromArgb(0, 102, 255);
            var mappedBlue = (Color)method.Invoke(null, new object[] { blue })!;
            mappedBlue.Should().Be(blue);
        }

        [Fact]
        public void BuiltInScenes_Rainbow_DefinesFourZoneColors()
        {
            using var logging = new LoggingService();
            var manager = new RgbManager(logging);
            using var sceneService = new RgbSceneService(logging, manager);

            var rainbow = sceneService.GetScene("rainbow");
            rainbow.Should().NotBeNull();
            rainbow!.Effect.Should().Be(RgbSceneEffect.Spectrum);
            rainbow.ZoneColors.Should().HaveCount(4, "4-zone laptop keyboards without MCU effect engines require zone colors to display a rainbow rather than falling back to static red");
            rainbow.ZoneColors[0].Should().Be("#FF0000");
            rainbow.ZoneColors[1].Should().Be("#FFFF00");
            rainbow.ZoneColors[2].Should().Be("#00FF00");
            rainbow.ZoneColors[3].Should().Be("#00FFFF");
        }

        [Fact]
        public void ParseControllerData_ExtractsSpectrumModePayload()
        {
            using var logging = new LoggingService();
            var provider = new OpenRgbProvider(logging);

            // Construct sample OpenRGB protocol 4 controller data packet with Spectrum Cycle mode
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // data_size placeholder
            writer.Write(0);
            // device_type = 6 (Mouse)
            writer.Write(6);
            // name
            WriteString(writer, "ASUS ROG Gladius III");
            // vendor
            WriteString(writer, "ASUS");
            // description
            WriteString(writer, "Gaming Mouse");
            // version
            WriteString(writer, "1.0");
            // serial
            WriteString(writer, "12345");
            // location
            WriteString(writer, "USB");
            // mode_count = 2
            writer.Write((ushort)2);
            // active_mode = 0
            writer.Write(0);

            // Mode 0: Direct
            WriteString(writer, "Direct");
            writer.Write(0); // value
            writer.Write(0x130u); // flags
            writer.Write(0u); // speed_min
            writer.Write(0u); // speed_max
            writer.Write(0u); // brightness_min
            writer.Write(0u); // brightness_max
            writer.Write(0u); // colors_min
            writer.Write(100u); // colors_max
            writer.Write(0u); // speed
            writer.Write(0u); // brightness
            writer.Write(0u); // direction
            writer.Write(1u); // color_mode
            writer.Write((ushort)0); // num_colors

            // Mode 1: Spectrum Cycle
            WriteString(writer, "Spectrum Cycle");
            writer.Write(2); // value
            writer.Write(0x110u); // flags
            writer.Write(0u); // speed_min
            writer.Write(0u); // speed_max
            writer.Write(0u); // brightness_min
            writer.Write(0u); // brightness_max
            writer.Write(0u); // colors_min
            writer.Write(100u); // colors_max
            writer.Write(0u); // speed
            writer.Write(0u); // brightness
            writer.Write(0u); // direction
            writer.Write(0u); // color_mode
            writer.Write((ushort)0); // num_colors

            // Zones (1 zone, 0 segments) + reported num_leds = 3
            writer.Write((ushort)1); // num_zones
            WriteString(writer, "Mouse Zone");
            writer.Write(0); // zone_type
            writer.Write(3u); // leds_min
            writer.Write(3u); // leds_max
            writer.Write(3u); // leds_count
            writer.Write((ushort)0); // matrix_len
            writer.Write((ushort)0); // num_segments (protocol 4)
            writer.Write((ushort)3); // num_leds

            // Update total data_size at offset 0
            var bytes = ms.ToArray();
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);

            var method = typeof(OpenRgbProvider).GetMethod("ParseControllerData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Should().NotBeNull();

            var device = method!.Invoke(provider, new object[] { 0, bytes, 4 }) as OpenRgbDevice;
            device.Should().NotBeNull();
            device!.Name.Should().Be("ASUS ROG Gladius III");
            device.Type.Should().Be(OpenRgbDeviceType.Mouse);
            device.LedCount.Should().Be(3, "LED count should come from controller data num_leds rather than name heuristics");
            device.SpectrumModePayload.Should().NotBeNull("device has 'Spectrum Cycle' mode which should be extracted for hardware spectrum animation");
            device.SpectrumModePayload!.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void ParseControllerData_Protocol0_ParsesWithoutBrightnessAndFallsBackToDefaultMouseLedCount()
        {
            using var logging = new LoggingService();
            var provider = new OpenRgbProvider(logging);

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(0); // data_size placeholder
            writer.Write((int)OpenRgbDeviceType.Mouse);
            WriteString(writer, "Generic Mouse");
            WriteString(writer, "Vendor");
            WriteString(writer, "Desc");
            WriteString(writer, "1.0");
            WriteString(writer, "SN");
            WriteString(writer, "USB");
            writer.Write((ushort)1); // mode_count
            writer.Write(0); // active_mode

            // Mode 0 in protocol 0 (no brightness_min/max/brightness)
            WriteString(writer, "Direct");
            writer.Write(0); // value
            writer.Write(0x130u); // flags
            writer.Write(0u); // speed_min
            writer.Write(0u); // speed_max
            writer.Write(0u); // colors_min
            writer.Write(10u); // colors_max
            writer.Write(0u); // speed
            writer.Write(0u); // direction
            writer.Write(1u); // color_mode
            writer.Write((ushort)0); // num_colors

            var bytes = ms.ToArray();
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);

            var method = typeof(OpenRgbProvider).GetMethod("ParseControllerData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var device = method!.Invoke(provider, new object[] { 0, bytes, 0 }) as OpenRgbDevice;

            device.Should().NotBeNull();
            device!.Name.Should().Be("Generic Mouse");
            device.LedCount.Should().Be(2, "mouse fallback should remain 2 when controller data omits num_leds");
        }

        private static void WriteString(BinaryWriter writer, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s + "\0");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }
}
