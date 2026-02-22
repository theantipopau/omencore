using System;
using System.IO;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class TelemetryServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public TelemetryServiceTests()
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
        public void IncrementPidStats_RespectsOptIn()
        {
            var cfg = new ConfigurationService();
            var logging = new LoggingService();
            logging.Initialize();
            var telemetry = new TelemetryService(logging, cfg);

            // By default telemetry disabled
            cfg.Config.TelemetryEnabled.Should().BeFalse();

            telemetry.IncrementPidSuccess(0x1B2E);
            telemetry.GetStats().Should().BeEmpty();

            // Enable telemetry and increment
            cfg.Config.TelemetryEnabled = true;
            telemetry.IncrementPidSuccess(0x1B2E);
            var stats = telemetry.GetStats();
            stats.Should().ContainKey("6958"); // decimal of 0x1B2E
            stats["6958"].Success.Should().BeGreaterThan(0);

            telemetry.IncrementPidFailure(0x1B2E);
            stats = telemetry.GetStats();
            stats["6958"].Failure.Should().BeGreaterThan(0);
        }

        [Fact]
        public void ExportTelemetry_CreatesCopyOfFile()
        {
            var cfg = new ConfigurationService();
            cfg.Config.TelemetryEnabled = true;
            var logging = new LoggingService();
            logging.Initialize();
            var telemetry = new TelemetryService(logging, cfg);

            telemetry.IncrementPidSuccess(123);
            var exportPath = telemetry.ExportTelemetry();
            exportPath.Should().NotBeNullOrEmpty();
            File.Exists(exportPath).Should().BeTrue();

            // exported file should be different from original location
            File.ReadAllText(exportPath).Should().Contain("123");
        }
    }
}
