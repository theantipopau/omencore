using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Corsair;
using OmenCore.Services;
using OmenCore.Services.Rgb;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class LightingViewModelTests
    {
        public LightingViewModelTests()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        [Fact]
        public async Task ApplyCorsairPresetToSystem_AppliesPresetToAllRegisteredProviders()
        {
            // Arrange
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");
            var logging = new LoggingService
            {
                Level = LogLevel.Info
            };

            var corsairStub = new OmenCore.Services.Corsair.CorsairSdkStub(logging);
            var corsairService = new CorsairDeviceService(corsairStub, logging);

            var logitechStub = new OmenCore.Services.Logitech.LogitechSdkStub(logging);
            var logitechService = new OmenCore.Services.LogitechDeviceService(logitechStub, logging);

            var configService = new ConfigurationService();
            var cfg = new OmenCore.Models.AppConfig
            {
                CorsairLightingPresets = new System.Collections.Generic.List<OmenCore.Corsair.CorsairLightingPreset>
            {
                new OmenCore.Corsair.CorsairLightingPreset { Name = "TestPreset", ColorHex = "#112233" }
            }
            };
            configService.Replace(cfg);

            var rgbManager = new RgbManager();
            var testProvider = new TestRgbProvider();
            rgbManager.RegisterProvider(testProvider);

            var vm = new LightingViewModel(corsairService, logitechService, logging, null, configService, null, rgbManager);

            vm.SelectedCorsairPreset = vm.CorsairLightingPresets.First(p => p.Name == "TestPreset");

            // Act
            vm.ApplyCorsairPresetToSystemCommand.Execute(null);
            await Task.Delay(150); // allow async command to complete

            // Assert
            testProvider.LastEffect.Should().Be("preset:TestPreset");
        }

        private class TestRgbProvider : IRgbProvider
        {
            public string ProviderName => "TestProvider";
            public bool IsAvailable { get; private set; } = true;
            public string? LastEffect { get; private set; }

            public Task InitializeAsync()
            {
                IsAvailable = true;
                return Task.CompletedTask;
            }

            public Task ApplyEffectAsync(string effectId)
            {
                LastEffect = effectId;
                return Task.CompletedTask;
            }
        }
    }
}
