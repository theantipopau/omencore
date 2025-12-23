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
        private class TestCorsairService : CorsairDeviceService
        {
            public List<CorsairDevice> TestDevices { get; } = new();

            public TestCorsairService() : base(null)
            {
                // Mock devices
                TestDevices.Add(new CorsairDevice { Model = "K70 RGB", Type = CorsairDeviceType.Keyboard });
            }

            public override Task DiscoverAsync()
            {
                Devices.Clear();
                Devices.AddRange(TestDevices);
                return Task.CompletedTask;
            }
        }

        private class TestLogitechService : LogitechDeviceService
        {
            public List<LogitechDevice> TestDevices { get; } = new();

            public TestLogitechService() : base(null)
            {
                TestDevices.Add(new LogitechDevice { Model = "G915 TKL", Type = LogitechDeviceType.Keyboard });
            }

            public override Task DiscoverAsync()
            {
                Devices.Clear();
                Devices.AddRange(TestDevices);
                return Task.CompletedTask;
            }
        }

        private class TestKeyboardLightingService : KeyboardLightingService
        {
            public TestKeyboardLightingService() : base(null, null, null, null)
            {
                // Mock as available
            }

            public override bool IsAvailable => true;
            public override string BackendType => "WMI BIOS";
        }

        [Fact]
        public async Task RunDeviceDetection_DetectsAllServices()
        {
            var logging = new LoggingService(); logging.Initialize();
            var corsair = new TestCorsairService();
            var logitech = new TestLogitechService();
            var keyboard = new TestKeyboardLightingService();

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
            var corsair = new TestCorsairService();
            var logitech = new TestLogitechService();
            var keyboard = new TestKeyboardLightingService();

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
            var corsair = new TestCorsairService();
            var logitech = new TestLogitechService();
            var keyboard = new TestKeyboardLightingService();

            var vm = new KeyboardDiagnosticsViewModel(corsair, logitech, keyboard, null, logging);

            await vm.ClearTestPatternAsync();

            vm.DiagnosticStatus.Should().Be("Test pattern cleared.");
            vm.IsRunningTest.Should().BeFalse();

            logging.Dispose();
        }
    }
}