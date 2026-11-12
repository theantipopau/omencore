using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class ConfigurationServiceTests : IDisposable
    {
        private readonly string _previousConfigDir;
        private readonly string _tempDir;

        public ConfigurationServiceTests()
        {
            _previousConfigDir = Environment.GetEnvironmentVariable("OMENCORE_CONFIG_DIR") ?? string.Empty;
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreConfigServiceTest_" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        [Fact]
        public void Save_RecreatesConfigDirectory_WhenItWasRemovedAfterConstruction()
        {
            var service = new ConfigurationService();
            Directory.Delete(_tempDir, recursive: true);

            service.Save(service.Config);

            File.Exists(Path.Combine(_tempDir, "config.json")).Should().BeTrue();
        }

        [Fact]
        public void Load_KeepsACopyOfAnUnreadableConfig_BeforeDefaultsOverwriteIt()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(path, "{ not json");

            var service = new ConfigurationService();
            service.Save(service.Config);

            var backups = Directory.GetFiles(_tempDir, "config.json.corrupt-*");
            backups.Should().ContainSingle();
            File.ReadAllText(backups[0]).Should().Be("{ not json");
        }

        [Fact]
        public void Load_MigratesA4xConfig_KeepsTheOriginal_AndKeepsItsSettings()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "config.json");
            const string old = "{ \"monitoringIntervalMs\": 2000 }";
            File.WriteAllText(path, old);

            var service = new ConfigurationService();

            service.Config.SchemaVersion.Should().Be(ConfigurationService.CurrentSchemaVersion);
            service.Config.MonitoringIntervalMs.Should().Be(2000);
            File.ReadAllText(path + ".schema0.bak").Should().Be(old);
        }

        [Fact]
        public void Load_LeavesAConfigFromANewerBuildAtItsVersion()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "config.json");
            var newer = ConfigurationService.CurrentSchemaVersion + 1;
            File.WriteAllText(path, "{ \"schemaVersion\": " + newer + " }");

            var service = new ConfigurationService();

            service.Config.SchemaVersion.Should().Be(newer);
            File.Exists(path + ".schema" + newer + ".bak").Should().BeTrue();
        }

        [Fact]
        public void Save_KeepsKeysThisBuildDoesNotKnow()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(path, "{ \"schemaVersion\": 1, \"someFutureSetting\": { \"level\": 3 } }");

            var service = new ConfigurationService();
            service.Save(service.Config);

            File.ReadAllText(path).Should().Contain("someFutureSetting").And.Contain("\"level\": 3");
        }

        [Fact]
        public void FreshConfig_IsWrittenAtTheCurrentSchema_AndNeedsNoMigration()
        {
            var service = new ConfigurationService();

            service.Config.SchemaVersion.Should().Be(ConfigurationService.CurrentSchemaVersion);
            Directory.GetFiles(_tempDir, "*.bak").Should().BeEmpty();
        }

        [Fact]
        public void Load_ReturnsSameConfigReference_AcrossMultipleCalls()
        {
            var service = new ConfigurationService();

            var first = service.Load();
            var second = service.Load();

            second.Should().BeSameAs(first);
            second.Should().BeSameAs(service.Config);
        }

        [Fact]
        public void Load_MergesDiskChangesOntoExistingConfigReference_WithoutReplacingIt()
        {
            // GitHub #191 / Discord (board 8BAD): a ViewModel that captures Config once (e.g.
            // SettingsViewModel's `_config = configService.Config;`) must keep seeing live
            // updates through that same reference, not a detached snapshot frozen at capture time.
            var service = new ConfigurationService();
            var held = service.Config;

            var onDisk = service.Load();
            onDisk.CorsairDisableIcueFallback = true;
            service.Save(onDisk);

            service.Load();

            held.CorsairDisableIcueFallback.Should().BeTrue(
                "the already-held reference should observe the change through the same object");
        }

        [Fact]
        public void Load_StillRunsValidateAndRepair_OnMergedResult()
        {
            var service = new ConfigurationService();
            File.WriteAllText(
                Path.Combine(_tempDir, "config.json"),
                "{\"monitoringIntervalMs\": 50}"); // below the 500 minimum

            var config = service.Load();

            config.MonitoringIntervalMs.Should().Be(1000);
            config.Should().BeSameAs(service.Config);
        }

        [Fact]
        public void TwoIndependentlyHeldConfigSnapshots_DoNotClobberEachOthersSaves()
        {
            // The direct regression test for GitHub #191 and the Discord board-8BAD report: a
            // custom fan curve (or an AMD power limit) saves successfully, then vanishes after
            // something unrelated elsewhere in the app saves through a different, stale-config
            // reference. pathA mirrors SettingsViewModel's `_config = configService.Config;`;
            // pathB mirrors MainViewModel's `_config = configService.Load();`.
            var service = new ConfigurationService();
            var pathA = service.Config;
            var pathB = service.Load();

            pathA.CustomFanCurve = new List<FanCurvePoint>
            {
                new() { TemperatureC = 40, FanPercent = 30 },
                new() { TemperatureC = 80, FanPercent = 90 }
            };
            service.Save(pathA);

            pathB.AmdPowerLimits = new AmdPowerLimits { StapmLimitWatts = 35, TempLimitC = 90 };
            service.Save(pathB);

            var verifier = new ConfigurationService();
            verifier.Config.CustomFanCurve.Should().NotBeNull().And.HaveCount(2);
            verifier.Config.AmdPowerLimits.Should().NotBeNull();
            verifier.Config.AmdPowerLimits!.StapmLimitWatts.Should().Be(35);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                "OMENCORE_CONFIG_DIR",
                string.IsNullOrWhiteSpace(_previousConfigDir) ? null : _previousConfigDir);

            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
