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
        public async Task DiscoverAsync_WithStubProvider_ReturnsStubDevices()
        {
            // Arrange
            var logging = new LoggingService();
            var service = await CorsairDeviceService.CreateAsync(logging);
            
            // Act
            await service.DiscoverAsync();
            
            // Assert
            service.Devices.Should().NotBeEmpty("stub provider should return fake devices for testing");
            service.Devices.Should().Contain(d => d.Name.Contains("Stub"), "stub devices should be identifiable");
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
    }
}
