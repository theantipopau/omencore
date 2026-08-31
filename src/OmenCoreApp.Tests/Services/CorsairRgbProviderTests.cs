using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
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
            public bool ShouldFail;
            public int ApplyLightingCallCount;

            public Task<bool> InitializeAsync() { return Task.FromResult(true); }
            public Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync() => Task.FromResult<IEnumerable<CorsairDevice>>(new[] { Device });
            public Task<bool> ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset)
            {
                ApplyLightingCallCount++;
                LastPreset = preset;
                return Task.FromResult(!ShouldFail);
            }
            public int ApplyDpiCallCount;
            public Task<bool> ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
            {
                ApplyDpiCallCount++;
                return Task.FromResult(!ShouldFail);
            }
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

        [Fact]
        public async Task ApplyLightingToAllAsync_SdkReturnsFalse_ReportsFailureInsteadOfClaimingSuccess()
        {
            // Regression test: CorsairDeviceService previously treated "the SDK call didn't
            // throw" as success, even when the SDK's own ApplyLightingAsync returned false
            // (e.g. iCUE found the device missing from the RGB surface, or a direct-HID write
            // failed after retries) without throwing. It then unconditionally logged
            // "Applied ... to N device(s)" regardless. This pins that a false return from the
            // SDK now propagates as a real failure.
            var logging = new LoggingService();
            logging.Initialize();

            var testProvider = new TestProvider { ShouldFail = true };
            var corsairService = new CorsairDeviceService(testProvider, logging);
            var initField = typeof(CorsairDeviceService).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("_initialized field not found");
            initField.SetValue(corsairService, true);

            await corsairService.DiscoverAsync();

            var applied = await corsairService.ApplyLightingToAllAsync("#112233");

            applied.Should().BeFalse("the SDK reported failure for every device, so the sync as a whole must not be reported as successful");
            testProvider.ApplyLightingCallCount.Should().Be(1, "the write should still have been attempted");
        }

        [Fact]
        public async Task ApplyDpiStagesAsync_SdkReturnsFalse_DoesNotUpdateDeviceModelOrReportSuccess()
        {
            // GitHub roadmap v4.2.1: CorsairICueSdk (the RGB.NET-backed provider) cannot
            // actually write DPI stages - it has no DPI API - and used to complete as if it
            // had, so the DPI editor's device model, saved defaults, and saved profile all
            // silently updated to values that were never written to the mouse, right after a
            // confirmation dialog told the user hardware settings were about to change. Same
            // "SDK didn't throw != SDK succeeded" bug class as the lighting fix above, for DPI.
            var logging = new LoggingService();
            logging.Initialize();

            var device = new CorsairDevice { Name = "Test Mouse", DeviceType = CorsairDeviceType.Mouse };
            var testProvider = new TestProvider { Device = device, ShouldFail = true };
            var corsairService = new CorsairDeviceService(testProvider, logging);

            var originalStages = new List<CorsairDpiStage> { new() { Name = "Low", Dpi = 800 } };
            device.DpiStages = originalStages;

            var newStages = new List<CorsairDpiStage> { new() { Name = "High", Dpi = 16000 } };
            var applied = await corsairService.ApplyDpiStagesAsync(device, newStages);

            applied.Should().BeFalse("the RGB.NET backend has no DPI write path and must report that honestly instead of a silent no-op success");
            testProvider.ApplyDpiCallCount.Should().Be(1, "the write should still have been attempted");
            device.DpiStages.Should().BeSameAs(originalStages,
                "the device model must not be updated to reflect DPI values that were never actually written to the hardware");
        }
    }
}
