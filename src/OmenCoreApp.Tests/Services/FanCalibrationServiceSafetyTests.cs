using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.FanCalibration;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class FanCalibrationServiceSafetyTests
    {
        [Fact]
        public async Task StartCalibrationAsync_BlocksWrites_WhenDesktopSafetyGateIsActive()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var controller = new RecordingFanController();
            var service = CreateService(controller, logging);
            string? error = null;
            service.CalibrationError += (_, message) => error = message;

            await service.StartCalibrationAsync();

            controller.WriteCount.Should().Be(0);
            service.IsCalibrating.Should().BeFalse();
            error.Should().Contain("fan writes are disabled");
            logging.Dispose();
        }

        [Fact]
        public async Task ApplyAndVerifyAsync_BlocksWrites_WhenDesktopSafetyGateIsActive()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var controller = new RecordingFanController();
            var service = CreateService(controller, logging);

            var result = await service.ApplyAndVerifyAsync(fanIndex: 0, targetPercent: 70);

            controller.WriteCount.Should().Be(0);
            result.WmiCallSucceeded.Should().BeFalse();
            logging.Dispose();
        }

        private static FanCalibrationService CreateService(RecordingFanController controller, LoggingService logging)
        {
            var capabilities = new DeviceCapabilities
            {
                Chassis = ChassisType.Tower,
                ModelFamily = OmenModelFamily.Desktop,
                ModelConfig = new ModelCapabilities { Family = OmenModelFamily.Desktop }
            };

            return new FanCalibrationService(
                controller,
                new SystemInfoService(logging),
                logging,
                capabilities);
        }

        private sealed class RecordingFanController : IFanController
        {
            public int WriteCount { get; private set; }
            public bool IsAvailable => true;
            public string Status => "Available";
            public string Backend => "Test";

            public bool ApplyPreset(FanPreset preset) { WriteCount++; return true; }
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) { WriteCount++; return true; }
            public bool SetFanSpeed(int percent) { WriteCount++; return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { WriteCount++; return true; }
            public bool SetMaxFanSpeed(bool enabled) { WriteCount++; return true; }
            public bool SetPerformanceMode(string modeName) { WriteCount++; return true; }
            public bool RestoreAutoControl() { WriteCount++; return true; }
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1200 } };
            public bool ApplyMaxCooling() { WriteCount++;  return true; }
            public void ApplyAutoMode() { WriteCount++; }
            public void ApplyQuietMode() { WriteCount++; }
            public bool ResetEcToDefaults() { WriteCount++; return true; }
            public bool ApplyThrottlingMitigation() { WriteCount++; return true; }
            public bool VerifyMaxApplied(out string details) { details = "test"; return true; }
            public void Dispose() { }
        }
    }
}
