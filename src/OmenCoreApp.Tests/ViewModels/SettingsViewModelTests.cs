using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
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
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport)
            {

                // Enable the HID-only mode
                CorsairDisableIcueFallback = true
            };

            // ConfigurationService writes to disk on Save; reload from disk to verify persistence
            var cfgReload = new ConfigurationService();
            cfgReload.Config.CorsairDisableIcueFallback.Should().BeTrue();

            // Toggle back to false
            vm.CorsairDisableIcueFallback = false;
            var cfgReload2 = new ConfigurationService();
            cfgReload2.Config.CorsairDisableIcueFallback.Should().BeFalse();
        }

        [Fact]
        public void HotkeysWindowFocused_DefaultTrueAndPersists()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            
            // default should be true (window-focused behaviour enabled)
            cfgService.Config.Monitoring.WindowFocusedHotkeys.Should().BeTrue();
            
            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport)
            {
                HotkeysWindowFocused = false
            };

            var cfgReload = new ConfigurationService();
            cfgReload.Config.Monitoring.WindowFocusedHotkeys.Should().BeFalse();

            // flip back to true and verify persistence again
            vm.HotkeysWindowFocused = true;
            var cfgReload2 = new ConfigurationService();
            cfgReload2.Config.Monitoring.WindowFocusedHotkeys.Should().BeTrue();
        }

        [Fact]
        public void LowOverheadMode_PersistsCanonicalLegacyMonitoringValues()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport)
            {
                LowOverheadMode = true
            };

            var cfgReload = new ConfigurationService();
            cfgReload.Config.Monitoring.LowOverheadMode.Should().BeTrue();
            cfgReload.Config.Monitoring.PollingProfile.Should().Be("Low overhead");
            cfgReload.Config.Monitoring.PollIntervalMs.Should().Be(2000);
        }

        [Fact]
        public void DisablingLowOverheadMode_PersistsBalancedCompatibilityValues()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            cfgService.Config.Monitoring.LowOverheadMode = true;
            cfgService.Config.Monitoring.PollingProfile = "Performance";
            cfgService.Config.Monitoring.PollIntervalMs = 500;
            cfgService.Save(cfgService.Config);

            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport)
            {
                LowOverheadMode = false
            };

            var cfgReload = new ConfigurationService();
            cfgReload.Config.Monitoring.LowOverheadMode.Should().BeFalse();
            cfgReload.Config.Monitoring.PollingProfile.Should().Be("Balanced");
            cfgReload.Config.Monitoring.PollIntervalMs.Should().Be(1000);
        }

        [Fact]
        public void MonitoringCadenceStatus_ReflectsTrayOnlyUltraLowState()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());
            var monitoringService = new HardwareMonitoringService(
                new LibreHardwareMonitorBridge(),
                logging,
                new MonitoringPreferences { LowOverheadMode = true },
                new ResumeRecoveryDiagnosticsService());

            monitoringService.SetLowOverheadMode(true);
            monitoringService.SetUiWindowActive(false);
            monitoringService.SetTrayOnlyMode(true);
            monitoringService.SetOverlayRealtimeMode(false);

            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateCadenceTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
            updateMethod.Should().NotBeNull();
            updateMethod!.Invoke(monitoringService, new object[] { TimeSpan.FromSeconds(10) });

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport,
                hardwareMonitoringService: monitoringService);

            vm.MonitoringCadenceTier.Should().Be("Tray-only ultra-low (10s)");
            vm.MonitoringCadenceReason.Should().Contain("low-overhead/tray-only");
            vm.MonitoringCadenceBlockers.Should().Contain("None - ultra-low cadence eligible");
        }

        [Fact]
        public void MonitoringCadenceStatus_ListsCurrentUltraLowBlockers()
        {
            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());
            var monitoringService = new HardwareMonitoringService(
                new LibreHardwareMonitorBridge(),
                logging,
                new MonitoringPreferences { LowOverheadMode = false },
                new ResumeRecoveryDiagnosticsService());

            monitoringService.SetUiWindowActive(true);
            monitoringService.SetTrayOnlyMode(false);
            monitoringService.SetOverlayRealtimeMode(false);

            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateCadenceTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
            updateMethod.Should().NotBeNull();
            updateMethod!.Invoke(monitoringService, new object[] { TimeSpan.FromSeconds(1) });

            var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport,
                hardwareMonitoringService: monitoringService);

            vm.MonitoringCadenceTier.Should().Be("Active (1s)");
            vm.MonitoringCadenceBlockers.Should().Contain("Low overhead mode disabled");
            vm.MonitoringCadenceBlockers.Should().Contain("Main window active");
        }

        [Fact]
        public void ScheduleTimer_StaysStoppedUntilRulesExist()
        {
            const string timerName = "SettingsScheduleEnforcement";
            BackgroundTimerRegistry.Unregister(timerName);

            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            cfgService.Config.ScheduleRules.Clear();
            cfgService.Save(cfgService.Config);

            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            using var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport);

            BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == timerName,
                because: "opening Settings with no schedule rules should not create a 30s background wakeup");
        }

        [Fact]
        public void ScheduleTimer_StartsAndStopsWithScheduleRules()
        {
            const string timerName = "SettingsScheduleEnforcement";
            BackgroundTimerRegistry.Unregister(timerName);

            var logging = new OmenCore.Services.LoggingService();
            var cfgService = new ConfigurationService();
            cfgService.Config.ScheduleRules.Clear();
            cfgService.Save(cfgService.Config);

            var sysInfo = new OmenCore.Services.SystemInfoService(logging);
            var fanCleaning = new OmenCore.Services.FanCleaningService(logging, null, sysInfo);
            var bios = new OmenCore.Services.BiosUpdateService(logging);
            var profileExport = new OmenCore.Services.ProfileExportService(logging, cfgService);
            var diagnosticsExport = new DiagnosticExportService(logging, System.IO.Path.GetTempPath());

            using var vm = new SettingsViewModel(logging, cfgService, sysInfo, fanCleaning, bios, profileExport, diagnosticsExport);

            vm.AddScheduleRuleCommand.Execute(null);

            BackgroundTimerRegistry.GetAll().Should().Contain(t => t.Name == timerName,
                because: "schedule enforcement is only needed after at least one schedule rule exists");

            vm.RemoveScheduleRuleCommand.Execute(vm.ScheduleRules.Single());

            BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == timerName,
                because: "removing the last schedule rule should remove the timer");
        }
    }
}
