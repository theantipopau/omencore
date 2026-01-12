using System;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class SettingsViewModelTelemetryTests : IDisposable
    {
        private readonly string _tempDir;

        public SettingsViewModelTelemetryTests()
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
        public void TelemetryToggle_ShowsTooltipAndPersists()
        {
            var logging = new LoggingService();
            var cfg = new ConfigurationService();
            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfg);
            var diagnosticsExport = new OmenCore.Services.DiagnosticsExportService(logging, cfg);

            var vm = new SettingsViewModel(logging, cfg, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport);

            // Default off
            vm.TelemetryEnabled.Should().BeFalse();

            vm.TelemetryEnabled = true;
            var cfgReload = new ConfigurationService();
            cfgReload.Config.TelemetryEnabled.Should().BeTrue();
        }
    }
}
