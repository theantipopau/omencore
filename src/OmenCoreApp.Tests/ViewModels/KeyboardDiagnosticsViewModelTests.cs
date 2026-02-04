using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class KeyboardDiagnosticsViewModelTests
    {
        private class TestCorsairSdkProvider : OmenCore.Services.Corsair.ICorsairSdkProvider
        {
            public Task<bool> InitializeAsync() => Task.FromResult(true);
            public Task<IEnumerable<OmenCore.Corsair.CorsairDevice>> DiscoverDevicesAsync()
            {
                var list = new[] { new OmenCore.Corsair.CorsairDevice { Name = "K70 RGB", DeviceType = OmenCore.Corsair.CorsairDeviceType.Keyboard } };
                return Task.FromResult<IEnumerable<OmenCore.Corsair.CorsairDevice>>(list);
            }
            public Task ApplyLightingAsync(OmenCore.Corsair.CorsairDevice device, OmenCore.Corsair.CorsairLightingPreset preset) => Task.CompletedTask;
            public Task ApplyDpiStagesAsync(OmenCore.Corsair.CorsairDevice device, IEnumerable<OmenCore.Corsair.CorsairDpiStage> stages) => Task.CompletedTask;
            public Task ApplyMacroAsync(OmenCore.Corsair.CorsairDevice device, OmenCore.Corsair.MacroProfile macro) => Task.CompletedTask;
            public Task SyncWithThemeAsync(IEnumerable<OmenCore.Corsair.CorsairDevice> devices, OmenCore.Models.LightingProfile theme) => Task.CompletedTask;
            public Task<OmenCore.Corsair.CorsairDeviceStatus> GetDeviceStatusAsync(OmenCore.Corsair.CorsairDevice device) => Task.FromResult(device.Status);
            public Task FlashDeviceAsync(OmenCore.Corsair.CorsairDevice device, int flashCount = 3, int intervalMs = 300) => Task.CompletedTask;
            public void Shutdown() { }
        }

        private class TestLogitechSdkProvider : OmenCore.Services.Logitech.ILogitechSdkProvider
        {
            public Task<bool> InitializeAsync() => Task.FromResult(true);
            public Task<IEnumerable<OmenCore.Logitech.LogitechDevice>> DiscoverDevicesAsync()
            {
                var list = new[] { new OmenCore.Logitech.LogitechDevice { Name = "G915 TKL", DeviceType = OmenCore.Logitech.LogitechDeviceType.Keyboard } };
                return Task.FromResult<IEnumerable<OmenCore.Logitech.LogitechDevice>>(list);
            }
            public Task ApplyStaticColorAsync(OmenCore.Logitech.LogitechDevice device, string hexColor, int brightness) { device.CurrentColorHex = hexColor; return Task.CompletedTask; }
            public Task ApplyBreathingEffectAsync(OmenCore.Logitech.LogitechDevice device, string hexColor, int speed) => Task.CompletedTask;
            public Task ApplySpectrumEffectAsync(OmenCore.Logitech.LogitechDevice device, int speed) => Task.CompletedTask;
            public Task ApplyFlashEffectAsync(OmenCore.Logitech.LogitechDevice device, string hexColor, int durationMs, int intervalMs) => Task.CompletedTask;
            public Task<int> GetDpiAsync(OmenCore.Logitech.LogitechDevice device) => Task.FromResult(device.Status.Dpi);
            public Task SetDpiAsync(OmenCore.Logitech.LogitechDevice device, int dpi) { device.Status.Dpi = dpi; return Task.CompletedTask; }
            public Task<OmenCore.Logitech.LogitechDeviceStatus> GetDeviceStatusAsync(OmenCore.Logitech.LogitechDevice device) => Task.FromResult(device.Status);
            public Task SyncWithThemeAsync(IEnumerable<OmenCore.Logitech.LogitechDevice> devices, OmenCore.Models.LightingProfile profile) => Task.CompletedTask;
            public void Shutdown() { }
        }

        private class TestKeyboardLightingService : KeyboardLightingService
        {
            public TestKeyboardLightingService(LoggingService logging) : base(logging, null, null, null)
            {
                // Force internal flags to mark WMI available for tests
                var t = this.GetType().BaseType ?? this.GetType();
                var wmiField = t.GetField("_wmiAvailable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (wmiField != null) wmiField.SetValue(this, true);
            }
        }

        [Fact]
        public async Task RunDeviceDetection_DetectsAllServices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var corsairProvider = new TestCorsairSdkProvider();
            var corsair = new CorsairDeviceService(corsairProvider, logging);
            var cField = typeof(CorsairDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            cField.SetValue(corsair, true);

            var logitechProvider = new TestLogitechSdkProvider();
            var logitech = new LogitechDeviceService(logitechProvider, logging);
            var lField = typeof(LogitechDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            lField.SetValue(logitech, true);

            var keyboard = new TestKeyboardLightingService(logging);

            var vm = new KeyboardDiagnosticsViewModel(corsair, logitech, keyboard, null, logging);

            await vm.RunDeviceDetectionAsync();

            vm.DetectedDevices.Should().HaveCount(3);
            vm.DetectedDevices.Should().Contain(d => d.Brand == "Corsair" && d.Model == "K70 RGB");
            vm.DetectedDevices.Should().Contain(d => d.Brand == "Logitech" && d.Model == "G915 TKL");
            vm.DetectedDevices.Should().Contain(d => d.Brand == "HP Omen" && d.Model == "Integrated Keyboard");

            vm.HasCorsair.Should().BeTrue();
            vm.HasLogitech.Should().BeTrue();
            vm.HasKeyboardLighting.Should().BeTrue();
            vm.HasRazer.Should().BeFalse();

            logging.Dispose();
        }

        [Fact]
        public async Task RunTestPattern_AppliesToAvailableServices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var corsairProvider = new TestCorsairSdkProvider();
            var corsair = new CorsairDeviceService(corsairProvider, logging);
            var cField = typeof(CorsairDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            cField.SetValue(corsair, true);

            var logitechProvider = new TestLogitechSdkProvider();
            var logitech = new LogitechDeviceService(logitechProvider, logging);
            var lField = typeof(LogitechDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            lField.SetValue(logitech, true);

            var keyboard = new TestKeyboardLightingService(logging);

            var vm = new KeyboardDiagnosticsViewModel(corsair, logitech, keyboard, null, logging);

            await vm.RunTestPatternAsync();

            vm.DiagnosticStatus.Should().Be("Test pattern applied successfully.");
            vm.IsRunningTest.Should().BeFalse();

            // Verify HP Omen keyboard zones were set (log entries present)
            vm.DiagnosticLogs.Should().Contain(log => log.Contains("Set zone"));

            logging.Dispose();
        }

        [Fact]
        public async Task ClearTestPattern_ResetsServices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var corsairProvider = new TestCorsairSdkProvider();
            var corsair = new CorsairDeviceService(corsairProvider, logging);
            var cField = typeof(CorsairDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            cField.SetValue(corsair, true);

            var logitechProvider = new TestLogitechSdkProvider();
            var logitech = new LogitechDeviceService(logitechProvider, logging);
            var lField = typeof(LogitechDeviceService).GetField("_initialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ?? throw new System.Exception("_initialized field not found");
            lField.SetValue(logitech, true);

            var keyboard = new TestKeyboardLightingService(logging);

            var vm = new KeyboardDiagnosticsViewModel(corsair, logitech, keyboard, null, logging);

            await vm.ClearTestPatternAsync();

            vm.DiagnosticStatus.Should().Be("Test pattern cleared.");
            vm.IsRunningTest.Should().BeFalse();

            logging.Dispose();
        }
    }
}