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
    public class FanPresetVerificationTests
    {
        public FanPresetVerificationTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class NoEffectController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public List<FanPreset> AppliedPresets { get; } = new();

            public bool ApplyPreset(FanPreset preset)
            {
                AppliedPresets.Add(preset);
                return true; // pretend controller accepted the command but has no effect
            }

            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public virtual IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public void ApplyMaxCooling() { }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = "not supported"; return false; }
        }

        private class ReactiveController : NoEffectController
        {
            private int _stage = 0;
            public override IEnumerable<FanTelemetry> ReadFanSpeeds()
            {
                // first call: baseline 1000rpm, subsequent calls: 3500rpm to simulate success
                if (_stage++ == 0)
                    return new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
                return new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 } };
            }
        }

        [Fact]
        public void ApplyPreset_VerificationFails_RollsBackToPreviousPreset()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new NoEffectController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

            // Apply initial preset
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Attempt to apply presetB — controller accepts command but ReadFanSpeeds shows no change => verification fails
            fanService.ApplyPreset(presetB);

            // Should have rolled back to presetA
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Controller should have seen both the attempted preset and the rollback to previous preset
            controller.AppliedPresets.Count.Should().BeGreaterThanOrEqualTo(2);
            controller.AppliedPresets[^1].Name.Should().Be(presetA.Name);

            logging.Dispose();
        }

        [Fact]
        public void ApplyPreset_VerificationSucceeds_LeavesNewPresetActive()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new ReactiveController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

            // Apply initial preset
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Apply presetB — controller will show reactive change on read => verification succeeds
            fanService.ApplyPreset(presetB);

            // New preset should remain active
            fanService.ActivePresetName.Should().Be(presetB.Name);

            logging.Dispose();
        }
    }
}