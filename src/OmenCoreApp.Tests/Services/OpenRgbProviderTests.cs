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
        public void BuiltInScenes_OmenRed_UsesPureRed()
        {
            using var logging = new LoggingService();
            var manager = new RgbManager(logging);
            using var sceneService = new RgbSceneService(logging, manager);

            var omenRed = sceneService.GetScene("omen-red");
            omenRed.Should().NotBeNull();
            omenRed!.PrimaryColor.Should().Be("#FF0000", "pure red #FF0000 is required so uncolored diffuser LEDs on mice and keyboards do not turn magenta/pink from blue diode spill");
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

            // Update total data_size at offset 0
            var bytes = ms.ToArray();
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);

            var method = typeof(OpenRgbProvider).GetMethod("ParseControllerData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Should().NotBeNull();

            var device = method!.Invoke(provider, new object[] { 0, bytes }) as OpenRgbDevice;
            device.Should().NotBeNull();
            device!.Name.Should().Be("ASUS ROG Gladius III");
            device.Type.Should().Be(OpenRgbDeviceType.Mouse);
            device.SpectrumModePayload.Should().NotBeNull("device has 'Spectrum Cycle' mode which should be extracted for hardware spectrum animation");
            device.SpectrumModePayload!.Length.Should().BeGreaterThan(0);
        }

        private static void WriteString(BinaryWriter writer, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s + "\0");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }
}
