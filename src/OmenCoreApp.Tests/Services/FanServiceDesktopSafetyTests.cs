using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class FanServiceDesktopSafetyTests
    {
        [Fact]
        public void FanWritesAvailable_IsFalse_ForDesktopCapabilities_EvenWhenControllerIsAvailable()
        {
            var controller = new CountingFanController();
            using var service = CreateService(controller);

            service.FanWritesAvailable.Should().BeFalse();
        }

        [Fact]
        public void DesktopSafetyGate_BlocksDirectFanWriteCommands()
        {
            var controller = new CountingFanController();
            using var service = CreateService(controller);

            service.ApplyMaxCooling();
            service.ApplyAutoMode();
            service.ApplyQuietMode();
            service.ForceSetFanSpeed(80);
            service.ForceSetFanSpeeds(70, 75).Should().BeFalse();
            service.ResetEcToDefaults().Should().BeFalse();

            controller.WriteCount.Should().Be(0);
            service.GetFanCommandHistoryReport().Should().Contain("Desktop fan writes disabled by v3.6.3 safety gate");
        }

        private static FanService CreateService(CountingFanController controller)
        {
            var logging = new LoggingService();
            logging.Initialize();
            var notificationService = new NotificationService(logging);
            var thermalProvider = new ThermalSensorProvider(new LibreHardwareMonitorImpl());
            var capabilities = new DeviceCapabilities
            {
                Chassis = ChassisType.Tower,
                ModelFamily = OmenModelFamily.Desktop,
                ModelConfig = new ModelCapabilities
                {
                    Family = OmenModelFamily.Desktop,
                    SupportsFanCurves = false
                }
            };

            return new FanService(
                controller,
                thermalProvider,
                logging,
                notificationService,
                1000,
                new ResumeRecoveryDiagnosticsService(),
                capabilities: capabilities);
        }

        private sealed class CountingFanController : IFanController
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
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1200, DutyCyclePercent = 35 } };
            public bool ApplyMaxCooling() { WriteCount++;  return true; }
            public void ApplyAutoMode() { WriteCount++; }
            public void ApplyQuietMode() { WriteCount++; }
            public bool ResetEcToDefaults() { WriteCount++; return true; }
            public bool VerifyMaxApplied(out string details) { details = "test"; return true; }
            public bool ApplyThrottlingMitigation() { WriteCount++; return true; }
            public void Dispose() { }
        }
    }
}
