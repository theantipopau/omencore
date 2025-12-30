using System;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Corsair;
using OmenCore.Services;
using OmenCore.Services.Corsair;
using OmenCore.Services.Logitech;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class LightingViewModelDpiTests : IDisposable
    {
        private readonly string _tempDir;

        public LightingViewModelDpiTests()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public async Task RestoreCorsairDpi_UsesConfigDefaults_WhenPresent()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();

            // Set config defaults
            cfg.Config.DefaultCorsairDpi.Clear();
            cfg.Config.DefaultCorsairDpi.Add(new CorsairDpiStage { Name = "Low", Dpi = 400, Index = 0 });
            cfg.Config.DefaultCorsairDpi.Add(new CorsairDpiStage { Name = "High", Dpi = 3200, Index = 1 });
            cfg.Save(cfg.Config);

            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            // Change DPI stages and then restore
            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "Tmp", Dpi = 2000 });

            await vm.RestoreCorsairDpiAsync(skipConfirmation: true);

            vm.CorsairDpiStages.Count.Should().Be(2);
            vm.CorsairDpiStages[0].Dpi.Should().Be(400);
            vm.CorsairDpiStages[1].Dpi.Should().Be(3200);
        }

        [Fact]
        public async Task ApplyCorsairDpi_CallsService_WhenDeviceSelected()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();

            var corsairService = new CorsairDeviceService(new CorsairSdkStub(log), log);
            var logi = new LogitechDeviceService(new LogitechSdkStub(log), log);
            var vm = new LightingViewModel(corsairService, logi, log, null, cfg, null, null);

            // Prepare a fake device
            var device = new CorsairDevice { DeviceId = "test", Name = "Test Mouse", DeviceType = CorsairDeviceType.Mouse };
            vm.SelectedCorsairDevice = device;

            // Add a stage and apply (skip confirmation)
            vm.CorsairDpiStages.Clear();
            vm.CorsairDpiStages.Add(new CorsairDpiStage { Name = "Stage 1", Dpi = 800, Index = 0 });

            // No exception should be thrown when applying with stub service
            await vm.ApplyCorsairDpiAsync(skipConfirmation: true);
        }
    }
}
