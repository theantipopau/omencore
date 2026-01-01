using FluentAssertions;
using Moq;
using OmenCore.Services;
using OmenCore.Services.Corsair;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class CorsairDeviceServiceTests
    {
        [Fact]
        public async Task CreateAsync_WithStubProvider_CreatesServiceSuccessfully()
        {
            // Arrange
            var logging = new LoggingService();
            
            // Act
            var service = await CorsairDeviceService.CreateAsync(logging);
            
            // Assert
            service.Should().NotBeNull("service should be created even without real SDK");
            // Note: Service uses stub by default since no real SDK is available
        }

        [Fact]
        public async Task DiscoverAsync_WithStubProvider_ReturnsEmptyList()
        {
            // Arrange
            var logging = new LoggingService();
            // Create service with explicit Corsair stub provider to avoid detecting real hardware in CI
            var service = new CorsairDeviceService(new CorsairSdkStub(logging), logging);

            // Mark service as initialized (CreateAsync normally sets the internal flag)
            var initField = typeof(CorsairDeviceService).GetField("_initialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? throw new System.Exception("_initialized field not found");
            initField.SetValue(service, true);

            // Act
            await service.DiscoverAsync();

            // Assert - Stub should return empty list (no fake devices)
            service.Devices.Should().BeEmpty("stub provider should not return fake devices");
        }

        [Fact]
        public async Task ApplyLightingPresetAsync_WithNullDevice_DoesNotThrow()
        {
            // Arrange
            var logging = new LoggingService();
            var service = await CorsairDeviceService.CreateAsync(logging);
            
            // Act
            var act = async () => await service.ApplyLightingPresetAsync(null!, null!);
            
            // Assert - Should handle gracefully
            await act.Should().NotThrowAsync("service should handle null inputs gracefully");
        }

        [Fact]
        public async Task CreateAsync_WithIcueFallbackDisabled_RespectsConfig()
        {
            // Arrange
            var logging = new LoggingService
            {
                Level = LogLevel.Info
            };

            var logEvents = new System.Collections.Generic.List<string>();
            logging.LogEmitted += s => logEvents.Add(s);

            var config = new ConfigurationService();
            var cfg = config.Config;
            cfg.CorsairDisableIcueFallback = true; // do not allow iCUE fallback
            config.Replace(cfg);

            // Act
            var svc = await CorsairDeviceService.CreateAsync(logging, config);
            await svc.DiscoverAsync();

            // If direct HID failed and iCUE fallback was disabled, we expect the specific log message
            var disabledMsg = "Corsair direct HID failed and iCUE fallback disabled via config";
            if (logEvents.Exists(l => l.Contains(disabledMsg)))
            {
                // In this case the stub provider should be in use and no devices discovered
                svc.Devices.Should().BeEmpty("stub provider used when HID failed and fallback disabled");
            }
            else
            {
                // Otherwise direct HID likely worked and devices (at least one) should be present
                svc.Devices.Should().NotBeEmpty("direct HID available or fallback to iCUE allowed");
            }
        }
    }
}
