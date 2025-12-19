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
            typeof(CorsairDeviceService)
                .GetField("_initialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(service, true);

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
    }
}
