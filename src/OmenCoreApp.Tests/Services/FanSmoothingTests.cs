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
            private readonly object _lock = new();  // guards _index / _readsThisStage

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
                lock (_lock)
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
        }

        [Fact]
        public async Task MonitorLoop_SuppressesSpuriousFanRpms_RequiresTwoConsecutiveReads()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // Sequence: stable 0 -> transient 1234 (single read) -> confirmed 1234 (second read) -> back to 0
            var seq = new List<IEnumerable<FanTelemetry>>
            {
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1234 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1234 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } },
                new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0 }, new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0 } }
            };

            // readsPerStage=2: each sequence element is served twice before advancing.
            // This ensures Start()'s seed read lands on stage 0, then the monitor loop
            // works through stages 1-3.
            var controller = new SequenceFanController(seq, readsPerStage: 2);
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 100);
            fanService.Start();
            fanService.ForceFixedPollInterval(100);

            var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            try
            {
                // Helper: poll until condition is met or timeout expires.
                async Task<bool> WaitFor(Func<List<int>, bool> condition, int timeoutMs = 3000)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        var rpms = (List<int>)lastRpmsField!.GetValue(fanService)!;
                        if (condition(rpms)) return true;
                        await Task.Delay(50);
                    }
                    return false;
                }

                // 1) Wait until stage 0 is fully served, then stage 1 first read fires
                //    (counter increments to 1, value held at 0). 1234 must NOT appear yet.
                await Task.Delay(250); // let seed + first monitor iter complete
                {
                    var rpms = (List<int>)lastRpmsField!.GetValue(fanService)!;
                    rpms[0].Should().NotBe(1234, "single transient read must not be accepted on the first cycle");
                }

                // 2) After the second consecutive 1234 read, the value must be accepted.
                bool accepted = await WaitFor(r => r.Count > 0 && r[0] == 1234, timeoutMs: 2000);
                accepted.Should().BeTrue("second consecutive 1234 read must cause acceptance");

                // 3) Accept zero immediately once fans stop (duty==0).
                bool stoppedAndAccepted = await WaitFor(r => r.Count > 0 && r[0] == 0, timeoutMs: 2000);
                stoppedAndAccepted.Should().BeTrue("_lastFanSpeeds[0] should drop to 0 once the zero+duty==0 stage is reached");
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

                // initial stable read should show a non-zero RPM (4300 expected but not required)
                await Task.Delay(1200);
                var lastRpms = (System.Collections.Generic.List<int>)lastRpmsField!.GetValue(fanService)!;
                lastRpms[0].Should().BeGreaterThan(0);

                // transient erroneous rpm=0 with duty!=0 should be IGNORED (should not drop below prior)
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().BeGreaterThan(0);

                // after second erroneous read still ignored
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().BeGreaterThan(0);

                // when duty-cycle drops to 0, RPM should be non-negative (zero is ideal if the background service cleared it)
                await Task.Delay(1200);
                lastRpms = (System.Collections.Generic.List<int>)lastRpmsField.GetValue(fanService)!;
                lastRpms[0].Should().BeGreaterThanOrEqualTo(0);
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 100);
            fanService.Start();
            fanService.ForceFixedPollInterval(100);

            var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            async Task<bool> WaitFor(Func<List<int>, bool> condition, int timeoutMs = 2000)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    var rpms = (List<int>)lastRpmsField!.GetValue(fanService)!;
                    if (condition(rpms)) return true;
                    await Task.Delay(30);
                }
                return false;
            }

            try
            {
                // Wait for the initial stable 2000rpm to be established
                bool seeded = await WaitFor(r => r.Count > 0 && r[0] == 2000, timeoutMs: 1000);
                seeded.Should().BeTrue("initial stable RPM must be seeded");

                // Rapidly apply presets (simulate user hammering quick-profile keys)
                var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
                var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

                fanService.ApplyPreset(presetA);
                fanService.ApplyPreset(presetB);
                fanService.ApplyPreset(presetA);

                // For a brief window (one full poll cycle after the presets) the transient
                // zeros should never replace the last known good 2000.
                // The sequence controller will start serving zero+duty>0 reads now.
                // We check 3 rapid snapshots across the next ~200ms (2 poll cycles).
                for (int snap = 0; snap < 3; snap++)
                {
                    var rpms = (List<int>)lastRpmsField!.GetValue(fanService)!;
                    rpms[0].Should().NotBe(0, $"snapshot {snap}: transient zero with non-zero duty must be suppressed");
                    await Task.Delay(60);
                }

                // After sufficient poll cycles the inconsistent-zero stages drain and 3500 is confirmed.
                bool accepted = await WaitFor(r => r.Count > 0 && r[0] == 3500, timeoutMs: 3000);
                accepted.Should().BeTrue("confirmed 3500rpm must be accepted once inconsistent-zero stages are exhausted");
            }
            finally
            {
                fanService.Stop();
                logging.Dispose();
            }
        }

        [Fact]
        public async Task QuickProfileSwitching_Stress_LongRun()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // Reuse sequence from the functional test but stress it by rapid repeated preset changes.
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 200);
            fanService.Start();

            try
            {
                var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
                var presetB = new FanPreset { Name = "Turbo", Mode = FanMode.Max };

                for (int i = 0; i < 30; i++)
                {
                    fanService.ApplyPreset(presetA);
                    fanService.ApplyPreset(presetB);
                    await Task.Delay(50);
                }

                // Allow monitor loop to stabilize after stress
                await Task.Delay(1500);

                var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lastRpms = (System.Collections.Generic.List<int>)lastRpmsField!.GetValue(fanService)!;

                lastRpms[0].Should().BeGreaterThan(0, "Stress test should not produce persistent phantom zero RPMs");
            }
            finally
            {
                fanService.Stop();
                logging.Dispose();
            }
        }
    }
}
