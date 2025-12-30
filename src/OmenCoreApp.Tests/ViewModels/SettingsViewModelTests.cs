using System;
using System.IO;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class SettingsViewModelTests : IDisposable
    {
        private readonly string _tempDir;

        public SettingsViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "omen_test_config_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public void CorsairDisableIcueFallback_Toggle_PersistsToConfig()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();

            // Sanity: default false
            cfgService.Config.CorsairDisableIcueFallback.Should().BeFalse();

            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios);

            // Enable the HID-only mode
            vm.CorsairDisableIcueFallback = true;

            // ConfigurationService writes to disk on Save; reload from disk to verify persistence
            var cfgReload = new ConfigurationService();
            cfgReload.Config.CorsairDisableIcueFallback.Should().BeTrue();

            // Toggle back to false
            vm.CorsairDisableIcueFallback = false;
            var cfgReload2 = new ConfigurationService();
            cfgReload2.Config.CorsairDisableIcueFallback.Should().BeFalse();
        }
    }
}
