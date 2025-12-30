using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OmenCore.Logitech;
using OmenCore.Services;
using OmenCore.Services.Logitech;
using OmenCore.Services.Rgb;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class LogitechRgbProviderTests
    {
        private class TestLogitechSdkProvider : ILogitechSdkProvider
        {
            public string? LastColorHex;
            public int LastBrightness = -1;
            public string? LastBreathingHex;
            public int LastBreathingSpeed = -1;
            public LogitechDevice Device = new LogitechDevice { Name = "G915 TKL", DeviceType = LogitechDeviceType.Keyboard };

            public Task<bool> InitializeAsync() { return Task.FromResult(true); }
            public Task<IEnumerable<LogitechDevice>> DiscoverDevicesAsync() => Task.FromResult<IEnumerable<LogitechDevice>>(new[] { Device });
            public Task ApplyStaticColorAsync(LogitechDevice device, string hexColor, int brightness)
            {
                LastColorHex = hexColor;
                LastBrightness = brightness;
                device.CurrentColorHex = hexColor;
                device.Status.BrightnessPercent = brightness;
                return Task.CompletedTask;
            }

            public Task ApplyBreathingEffectAsync(LogitechDevice device, string hexColor, int speed)
            {
                LastBreathingHex = hexColor;
                LastBreathingSpeed = speed;
                return Task.CompletedTask;
            }

            public Task<int> GetDpiAsync(LogitechDevice device) => Task.FromResult(device.Status.Dpi);
            public Task SetDpiAsync(LogitechDevice device, int dpi) { device.Status.Dpi = dpi; return Task.CompletedTask; }
            public Task<LogitechDeviceStatus> GetDeviceStatusAsync(LogitechDevice device) => Task.FromResult(device.Status);
            public void Shutdown() { }
        }

        [Fact]
        public async Task ApplyEffect_ColorWithBrightness_AppliesToDevices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var provider = new LogitechRgbProvider(logging);

            var testSdk = new TestLogitechSdkProvider();
            var service = new LogitechDeviceService(testSdk, logging);
            var f = typeof(LogitechDeviceService).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(service, true);
            await service.DiscoverAsync();

            // inject service instance into provider
            var serviceField = typeof(LogitechRgbProvider).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance);
            serviceField.SetValue(provider, service);

            // set IsAvailable via reflection
            var isAvailableProp = typeof(LogitechRgbProvider).GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Instance);
            var setMethod = isAvailableProp?.GetSetMethod(true);
            setMethod?.Invoke(provider, new object[] { true });

            await provider.ApplyEffectAsync("color:#00FF00@75");

            Assert.Equal("#00FF00", testSdk.LastColorHex);
            Assert.Equal(75, testSdk.LastBrightness);
        }

        [Fact]
        public async Task ApplyEffect_Breathing_AppliesToDevices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var provider = new LogitechRgbProvider(logging);

            var testSdk = new TestLogitechSdkProvider();
            var service = new LogitechDeviceService(testSdk, logging);
            var f = typeof(LogitechDeviceService).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(service, true);
            await service.DiscoverAsync();

            var serviceField = typeof(LogitechRgbProvider).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance);
            serviceField.SetValue(provider, service);
            var isAvailableProp = typeof(LogitechRgbProvider).GetProperty("IsAvailable", BindingFlags.Public | BindingFlags.Instance);
            var setMethod = isAvailableProp?.GetSetMethod(true);
            setMethod?.Invoke(provider, new object[] { true });

            await provider.ApplyEffectAsync("breathing:#FF00FF@5");

            Assert.Equal("#FF00FF", testSdk.LastBreathingHex);
            Assert.Equal(5, testSdk.LastBreathingSpeed);
        }
    }
}
