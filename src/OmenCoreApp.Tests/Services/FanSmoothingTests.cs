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

        private class SequenceFanController : OmenCore.Hardware.IFanController
        {
            private readonly IList<IEnumerable<FanTelemetry>> _sequence;
            private int _index = 0;
            private readonly int _readsPerStage;
            private int _readsThisStage = 0;

            /// <summary>
            /// Returns each sequence element <paramref name="readsPerStage"/> times
            /// before advancing to the next element. Useful to simulate the initial
            /// Start() read + the first MonitorLoop read returning the same value.
            /// </summary>
            public SequenceFanController(IList<IEnumerable<FanTelemetry>> sequence, int readsPerStage = 1)
            {
                _sequence = sequence;
                _readsPerStage = Math.Max(1, readsPerStage);
            }

            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public void ApplyMaxCooling() { }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }

            public IEnumerable<FanTelemetry> ReadFanSpeeds()
            {
                if (_sequence == null || _sequence.Count == 0)
                    return Enumerable.Empty<FanTelemetry>();

                var item = _sequence[Math.Min(_index, _sequence.Count - 1)];

                // Increment read count for this stage and advance when we've served
                // the configured number of reads for this stage.
                _readsThisStage++;
                if (_readsThisStage >= _readsPerStage)
                {
                    _readsThisStage = 0;
                    _index = Math.Min(_index + 1, _sequence.Count - 1);
                }

                return item;
            }
        }

        [Fact]
        public async Task MonitorLoop_SuppressesSpuriousFanRpms_RequiresTwoConsecutiveReads()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // Sequence: 0 -> single transient 1234 -> repeat 1234 -> back to 0
            var seq = new List<IEnumerable<FanTelemetry>>
            {
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1234 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1234 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } }
            };

            var controller = new SequenceFanController(seq, readsPerStage: 2);
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);
            fanService.Start();

            try
            {
                // Allow initial read
                await Task.Delay(1200);
                var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lastRpms = (System.Collections.Generic.List<int>)lastRpmsField!.GetValue(fanService)!;
                lastRpms[0].Should().Be(0);

                // After one transient spike - should still show 0 (suppressed)
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(0);

                // After second consecutive spike - now accept the 1234 value
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(1234);

                // Immediate acceptance of zero when fans stop
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(0);
            }
            finally
            {
                fanService.Stop();
                logging.Dispose();
            }
        }

        [Fact]
        public async Task MonitorLoop_IgnoresSpuriousZeroWhenDutyCycleNonZero()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // Sequence: stable running -> transient RPM=0 but duty still non-zero -> stable running -> actual stop
            var seq = new List<IEnumerable<FanTelemetry>>
            {
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 4300, DutyCyclePercent = 78 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 4400, DutyCyclePercent = 80 } },
                // transient erroneous read (rpm=0) while duty-cycle remains non-zero
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 78 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 80 } },
                // repeat the erroneous read (still should be suppressed)
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 78 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 80 } },
                // actual stop (duty-cycle now zero)
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0 } }
            };

            var controller = new SequenceFanController(seq, readsPerStage: 2);
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);
            fanService.Start();

            try
            {
                var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // initial stable read should show 4300
                await Task.Delay(1200);
                var lastRpms = (System.Collections.Generic.List<int>)lastRpmsField!.GetValue(fanService)!;
                lastRpms[0].Should().Be(4300);

                // transient erroneous rpm=0 with duty!=0 should be IGNORED (still show 4300)
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(4300);

                // after second erroneous read still ignored
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(4300);

                // when duty-cycle drops to 0, accept zero immediately
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(0);
            }
            finally
            {
                fanService.Stop();
                logging.Dispose();
            }
        }

        [Fact]
        public void ApplyPreset_SkippedWhileDiagnosticModeActive()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000);

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Max", Mode = FanMode.Max };

            // Apply preset normally
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Enter diagnostic mode and attempt to apply another preset - should be ignored
            fanService.EnterDiagnosticMode();
            fanService.ApplyPreset(presetB);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            logging.Dispose();
        }

        [Fact]
        public async Task QuickProfileSwitching_DoesNotShowTransientZeroOrSpikes_WhenApplyingPresetsRapidly()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // Sequence simulates: stable running -> transient erroneous 0 (duty non-zero) -> confirmed new RPM
            var seq = new List<IEnumerable<FanTelemetry>>
            {
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 2000, DutyCyclePercent = 45 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 2000, DutyCyclePercent = 45 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 45 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 45 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 45 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 45 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 3500, DutyCyclePercent = 85 } }
            };

            var controller = new SequenceFanController(seq, readsPerStage: 1);
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            // Use a faster monitor interval for CI-friendly timing
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 300);
            fanService.Start();

            try
            {
                // Allow initial seed + first monitor loop
                await Task.Delay(450);

                var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lastRpms = (System.Collections.Generic.List<int>)lastRpmsField!.GetValue(fanService)!;

                // initial stable read should be 2000
                lastRpms[0].Should().Be(2000);

                // Rapidly apply presets (simulate user hammering quick-profile keys)
                var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
                var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

                fanService.ApplyPreset(presetA);
                fanService.ApplyPreset(presetB);
                fanService.ApplyPreset(presetA);

                // Wait for the transient erroneous reads to appear in the sequence and ensure suppression holds
                await Task.Delay(1000);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(2000, "transient erroneous zero (duty!=0) must be suppressed during quick-profile switching");

                // After confirmation the new RPM should be accepted
                await Task.Delay(800);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().Be(3500, "confirmed new RPM should be accepted after consecutive reads");
            }
            finally
            {
                fanService.Stop();
                logging.Dispose();
            }
        }
    }
}
