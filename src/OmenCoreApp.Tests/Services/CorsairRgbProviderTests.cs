using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OmenCore.Corsair;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Corsair;
using OmenCore.Services.Rgb;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class CorsairRgbProviderTests
    {
        public CorsairRgbProviderTests()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class TestProvider : ICorsairSdkProvider
        {
            public CorsairLightingPreset? LastPreset;
            public CorsairDevice Device = new() { Name = "Test Keyboard", DeviceType = CorsairDeviceType.Keyboard };

            public Task<bool> InitializeAsync() { return Task.FromResult(true); }
            public Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync() => Task.FromResult<IEnumerable<CorsairDevice>>(new[] { Device });
            public Task ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset)
            {
                LastPreset = preset;
                return Task.CompletedTask;
            }
            public Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages) => Task.CompletedTask;
            public Task ApplyMacroAsync(CorsairDevice device, MacroProfile macro) => Task.CompletedTask;
            public Task SyncWithThemeAsync(IEnumerable<CorsairDevice> devices, LightingProfile theme) => Task.CompletedTask;
            public Task<CorsairDeviceStatus> GetDeviceStatusAsync(CorsairDevice device) => Task.FromResult(device.Status);
            public Task FlashDeviceAsync(CorsairDevice device, int flashCount = 3, int intervalMs = 300) => Task.CompletedTask;
            public void Shutdown() { }
        }

        [Fact]
        public async Task ApplyEffect_Preset_AppliesPresetToAllDevices()
        {
            var logging = new LoggingService(); logging.Initialize();

            // Create a test configuration service and inject a Corsair preset
            var cfg = new ConfigurationService();
            var config = cfg.Config;
            config.CorsairLightingPresets = new System.Collections.Generic.List<CorsairLightingPreset>
            {
                new CorsairLightingPreset { Name = "TestPreset", ColorHex = "#112233" }
            };
            cfg.Replace(config);

            // Create a test corsair device service with a test provider
            var testProvider = new TestProvider();
            var corsairService = new CorsairDeviceService(testProvider, logging);
            // Mark internal initialized flag so DiscoverAsync runs
            var f = typeof(CorsairDeviceService).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new Exception("_initialized field not found");
            f.SetValue(corsairService, true);

            // Discover devices (populate Devices)
            await corsairService.DiscoverAsync();

            // Create the provider and inject the test service
            var provider = new CorsairRgbProvider(logging, cfg);
            var serviceField = typeof(CorsairRgbProvider).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new Exception("_service field not found");
            serviceField.SetValue(provider, corsairService);
            // Mark available
            var availableField = typeof(CorsairRgbProvider).GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Instance) ?? throw new Exception("IsAvailable property not found");
            var setMethod = availableField.GetSetMethod(true) ?? throw new Exception("setter for IsAvailable not found");
            setMethod.Invoke(provider, new object[] { true });

            // Apply preset
            await provider.ApplyEffectAsync("preset:TestPreset");

            Assert.NotNull(testProvider.LastPreset);
            Assert.Equal("TestPreset", testProvider.LastPreset.Name);
        }
    }
}
