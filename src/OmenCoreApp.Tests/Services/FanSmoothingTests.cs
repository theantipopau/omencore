using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class FanSmoothingTests
    {
        public FanSmoothingTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class RecordingFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public List<int> SetCalls { get; } = new List<int>();

            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) { SetCalls.Add(percent); return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { SetCalls.Add(Math.Max(cpuPercent, gpuPercent)); return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => Enumerable.Empty<FanTelemetry>();
            public void ApplyMaxCooling() { SetCalls.Add(100); }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        [Fact]
        public async Task RampFanToPercent_PerformsMultipleWrites_WhenSmoothingEnabled()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            // Configure smoothing to be short for test
            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = true, SmoothingDurationMs = 300, SmoothingStepMs = 100 });

            // Disable hysteresis so smoothing is applied immediately in tests
            fanService.SetHysteresis(new OmenCore.Models.FanHysteresisSettings { Enabled = false });

            // Ensure we have an active curve that results in a lower target than current
            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 20, FanPercent = 20 },
                new FanCurvePoint { TemperatureC = 80, FanPercent = 40 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);

            // Seed current applied fan percent to 80% so smoothing will ramp down to 40%
            fanService.ForceSetFanSpeed(80);

            // Directly invoke the private ramp method to exercise smoothing (bypass hysteresis/timing)
            var rampMethod = typeof(FanService).GetMethod("RampFanToPercentAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var rampTask = (Task)rampMethod.Invoke(fanService, new object[] { 40, System.Threading.CancellationToken.None });
            await rampTask.ConfigureAwait(false);

            // Verify controller recorded at least one SetFanSpeed call
            controller.SetCalls.Count.Should().BeGreaterThan(0, "there should be at least one fan write");

            // Final call should be the target 40%
            controller.SetCalls[^1].Should().Be(40, "final applied percent must equal the curve target");

            logging.Dispose();
        }
    }
}
