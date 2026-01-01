using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services.Corsair;
using OmenCore.Services;
using Xunit;
using System;
using OmenCore.Corsair;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class CorsairHidDpiIntegrationTests
    {
        public CorsairHidDpiIntegrationTests()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class FlakyDpiCorsairHidDirect : CorsairHidDirect
        {
            private int _attempts = 0;
            private readonly int _failAttempts;

            public FlakyDpiCorsairHidDirect(LoggingService logging, int failAttempts, TelemetryService telemetry) : base(logging, telemetry)
            {
                _failAttempts = failAttempts;
            }

            // Provide a public wrapper to add test devices (calls protected helper)
            public void AddTestDevice(string deviceId, int productId, CorsairDeviceType type, string name = "Test Device")
            {
                AddTestHidDevice(deviceId, productId, type, name);
            }

            protected override Task<bool> WriteReportAsync(CorsairHidDevice device, byte[] report)
            {
                _attempts++;
                if (_attempts <= _failAttempts)
                {
                    return Task.FromResult(false);
                }
                // record received report for inspection by tests via side-effect (the tests check telemetry instead)
                return Task.FromResult(true);
            }
        }

        [Fact]
        public async Task ApplyDpi_RetriesAndRecordsSuccess()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            cfg.Config.TelemetryEnabled = true;
            var telemetry = new TelemetryService(log, cfg);

            var flaky = new FlakyDpiCorsairHidDirect(log, failAttempts: 2, telemetry);

            // Add test device
            flaky.AddTestDevice("test:dc", 0x1B2E, CorsairDeviceType.Mouse, "DarkCoreTest");
            var device = await flaky.DiscoverDevicesAsync();
            CorsairDevice? d = null;
            foreach (var x in device) if (x.DeviceId == "test:dc") d = x;
            d.Should().NotBeNull();

            // Apply DPI (should retry and succeed)
            await flaky.ApplyDpiStagesAsync(d!, new[] { new OmenCore.Corsair.CorsairDpiStage { Index = 0, Dpi = 800 } });

            var stats = telemetry.GetStats();
            stats.Should().ContainKey(((int)0x1B2E).ToString());
            stats[((int)0x1B2E).ToString()].Success.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task ApplyDpi_FailsAndRecordsFailure()
        {
            var log = new LoggingService();
            var cfg = new ConfigurationService();
            cfg.Config.TelemetryEnabled = true;
            var telemetry = new TelemetryService(log, cfg);

            var flaky = new FlakyDpiCorsairHidDirect(log, failAttempts: 5, telemetry);

            // Add test device
            flaky.AddTestDevice("test:m65", 0x1B1E, CorsairDeviceType.Mouse, "M65Test");
            var device = await flaky.DiscoverDevicesAsync();
            CorsairDevice? d = null;
            foreach (var x in device) if (x.DeviceId == "test:m65") d = x;
            d.Should().NotBeNull();

            // Apply DPI (will fail after retries)
            await flaky.ApplyDpiStagesAsync(d!, new[] { new OmenCore.Corsair.CorsairDpiStage { Index = 1, Dpi = 1600 } });

            var stats = telemetry.GetStats();
            stats.Should().ContainKey(((int)0x1B1E).ToString());
            stats[((int)0x1B1E).ToString()].Failure.Should().BeGreaterThan(0);
        }
    }
}
