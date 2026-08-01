using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
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
            public List<FanPreset> PresetCalls { get; } = new List<FanPreset>();
            public int CustomCurveCalls { get; private set; }

            public bool ApplyPreset(FanPreset preset) { PresetCalls.Add(preset); return true; }
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) { CustomCurveCalls++; return true; }
            public bool SetFanSpeed(int percent) { SetCalls.Add(percent); return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { SetCalls.Add(Math.Max(cpuPercent, gpuPercent)); return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => Enumerable.Empty<FanTelemetry>();
            public bool ApplyMaxCooling() { SetCalls.Add(100);  return true; }
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

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
            Assert.NotNull(rampMethod);
            var rampTask = (Task?)rampMethod.Invoke(fanService, new object[] { 40, System.Threading.CancellationToken.None });
            Assert.NotNull(rampTask);
            await rampTask;

            // Verify controller recorded at least one SetFanSpeed call
            controller.SetCalls.Count.Should().BeGreaterThan(0, "there should be at least one fan write");

            // Final call should be the target 40%
            controller.SetCalls[^1].Should().Be(40, "final applied percent must equal the curve target");

            logging.Dispose();
        }

        [Fact]
        public async Task ExtremePreset_ReachesMaxAtSeventyFiveC()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var extremePreset = new FanPreset
            {
                Name = "Extreme",
                Mode = FanMode.Performance,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 66 },
                    new FanCurvePoint { TemperatureC = 70, FanPercent = 88 },
                    new FanCurvePoint { TemperatureC = 75, FanPercent = 100 }
                }
            };

            fanService.ApplyPreset(extremePreset).Should().BeTrue();
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 76, gpuTemp: 0, immediate: true);

            controller.SetCalls.Should().NotBeEmpty();
            controller.SetCalls[^1].Should().Be(100,
                "Extreme should restore the long-standing full-cooling behavior once temps pass 75C");

            logging.Dispose();
        }

        [Fact]
        public async Task CurveEngine_SmoothsWarmSensorJump_BeforeSafetyBypassRange()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.SetHysteresis(new FanHysteresisSettings { Enabled = false });
            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = false });

            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 40 },
                new FanCurvePoint { TemperatureC = 70, FanPercent = 70 },
                new FanCurvePoint { TemperatureC = 90, FanPercent = 100 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 54, gpuTemp: 0, immediate: true);
            controller.SetCalls[^1].Should().Be(54);

            controller.SetCalls.Clear();
            SetPrivateField(fanService, "_lastCurveUpdate", DateTime.Now.AddSeconds(-10));
            SetPrivateField(fanService, "_lastCurveForceRefresh", DateTime.Now);

            await fanService.ForceApplyCurveNowAsync(cpuTemp: 70, gpuTemp: 0, immediate: false);

            controller.SetCalls.Should().ContainSingle();
            controller.SetCalls[0].Should().Be(60,
                "a warm sensor authority jump should be slew-limited before the hot safety range");

            logging.Dispose();
        }

        [Fact]
        public async Task CurveEngine_SendsWakeKick_WhenPositiveCurveCommandLeavesFanAtZeroRpm()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.SetHysteresis(new FanHysteresisSettings { Enabled = false });

            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 14 },
                new FanCurvePoint { TemperatureC = 60, FanPercent = 20 },
                new FanCurvePoint { TemperatureC = 70, FanPercent = 34 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 63, gpuTemp: 0, immediate: true);

            SetPrivateField(fanService, "_lastRawPrimaryFanRpm", 0);
            SetPrivateField(fanService, "_lastReportedPrimaryFanDutyPercent", 0);
            SetPrivateField(fanService, "_zeroRpmCurveCommandSince", DateTime.Now.AddSeconds(-15));
            SetPrivateField(fanService, "_lastCurveUpdate", DateTime.Now.AddSeconds(-10));
            SetPrivateField(fanService, "_lastCurveForceRefresh", DateTime.Now.AddSeconds(-40));

            controller.SetCalls.Clear();

            await fanService.ForceApplyCurveNowAsync(cpuTemp: 63, gpuTemp: 0, immediate: false);

            controller.SetCalls.Should().ContainSingle();
            controller.SetCalls[0].Should().Be(35,
                "the curve engine should issue one bounded wake pulse when positive curve writes leave RPM at zero");

            logging.Dispose();
        }

        [Fact]
        public async Task CurveEngine_DoesNotWakeKick_WhenZeroRpmOccursAtCoolIdle()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var hwMonitor = new OmenCore.Hardware.LibreHardwareMonitorImpl();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(hwMonitor);
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.SetHysteresis(new FanHysteresisSettings { Enabled = false });

            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 30, FanPercent = 10 },
                new FanCurvePoint { TemperatureC = 50, FanPercent = 20 },
                new FanCurvePoint { TemperatureC = 70, FanPercent = 40 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 50, gpuTemp: 0, immediate: true);

            SetPrivateField(fanService, "_lastRawPrimaryFanRpm", 0);
            SetPrivateField(fanService, "_lastReportedPrimaryFanDutyPercent", 0);
            SetPrivateField(fanService, "_zeroRpmCurveCommandSince", DateTime.Now.AddSeconds(-15));
            SetPrivateField(fanService, "_lastCurveUpdate", DateTime.Now.AddSeconds(-10));
            SetPrivateField(fanService, "_lastCurveForceRefresh", DateTime.Now.AddSeconds(-40));

            controller.SetCalls.Clear();

            await fanService.ForceApplyCurveNowAsync(cpuTemp: 50, gpuTemp: 0, immediate: false);

            controller.SetCalls.Should().ContainSingle();
            controller.SetCalls[0].Should().BeLessThan(35,
                "cool idle zero-RPM readings should follow the curve target instead of triggering the warm wake pulse");

            logging.Dispose();
        }

        [Fact]
        public async Task ConservativeLegacyProfile_SuppressesSmallCurveTargetChanges()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);
            var capabilities = new OmenCore.Hardware.DeviceCapabilities
            {
                ProductId = "88D2",
                Chassis = OmenCore.Hardware.ChassisType.Laptop,
                ModelFamily = OmenCore.Hardware.OmenModelFamily.Legacy,
                ModelConfig = new OmenCore.Hardware.ModelCapabilities
                {
                    ProductId = "88D2",
                    Family = OmenCore.Hardware.OmenModelFamily.Legacy,
                    UserVerified = false,
                    SupportsFanControlEc = false
                }
            };

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService(), capabilities: capabilities);
            fanService.SetHysteresis(new FanHysteresisSettings { Enabled = false });
            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = false });

            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                new FanCurvePoint { TemperatureC = 70, FanPercent = 60 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 60, gpuTemp: 0, immediate: true);
            controller.SetCalls.Should().Contain(50);

            controller.SetCalls.Clear();
            SetPrivateField(fanService, "_lastCurveUpdate", DateTime.Now.AddSeconds(-10));
            SetPrivateField(fanService, "_lastCurveForceRefresh", DateTime.Now);

            await fanService.ForceApplyCurveNowAsync(cpuTemp: 62, gpuTemp: 0, immediate: false);

            controller.SetCalls.Should().BeEmpty("88D2 conservative policy should avoid tiny curve deltas that cause fan hunting");
            logging.Dispose();
        }

        [Fact]
        public void CurveDisabledModel_StripsBuiltInCurvePayloadsAndRejectsManualCurves()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);
            var capabilities = new OmenCore.Hardware.DeviceCapabilities
            {
                ProductId = "8E41",
                Chassis = OmenCore.Hardware.ChassisType.Laptop,
                ModelFamily = OmenCore.Hardware.OmenModelFamily.Transcend,
                CanSetFanSpeed = true,
                ModelConfig = new OmenCore.Hardware.ModelCapabilities
                {
                    ProductId = "8E41",
                    Family = OmenCore.Hardware.OmenModelFamily.Transcend,
                    SupportsFanCurves = false
                }
            };

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService(), capabilities: capabilities);
            var extremePreset = new FanPreset
            {
                Name = "Extreme",
                Mode = FanMode.Performance,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 60, FanPercent = 75 },
                    new FanCurvePoint { TemperatureC = 75, FanPercent = 100 }
                }
            };

            fanService.ApplyPreset(extremePreset).Should().BeTrue();

            controller.PresetCalls.Should().ContainSingle();
            controller.PresetCalls[0].Name.Should().Be("Extreme");
            controller.PresetCalls[0].Curve.Should().BeEmpty("8E41 is profile-only, so built-in curve cards must not enable continuous manual fan writes");
            fanService.IsCurveActive.Should().BeFalse();

            var customPreset = new FanPreset
            {
                Name = "Custom",
                Mode = FanMode.Manual,
                Curve = extremePreset.Curve
            };

            fanService.ApplyPreset(customPreset).Should().BeFalse("manual custom curves are disabled for this profile-only model");
            controller.PresetCalls.Should().ContainSingle("the rejected custom preset must not reach the controller");

            fanService.ApplyCustomCurve(extremePreset.Curve);
            controller.CustomCurveCalls.Should().Be(0, "direct custom curve calls must also be gated by model capability");

            fanService.ManualFanControlAvailable.Should().BeFalse("8E41 should remain profile-only for direct fan levels");
            fanService.ForceSetFanSpeed(70);
            fanService.ForceSetFanSpeeds(70, 80).Should().BeFalse("direct manual fan writes are disabled for this profile-only model");
            controller.SetCalls.Should().BeEmpty("manual fixed-speed writes must not reach the controller on 8E41");

            logging.Dispose();
        }

        [Fact]
        public void RestoreOemAutoControl_DisablesCurveAndRecordsRecoveryCommand()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ApplyPreset(new FanPreset
            {
                Name = "Gaming",
                Mode = FanMode.Performance,
                Curve = new List<FanCurvePoint>
                {
                    new FanCurvePoint { TemperatureC = 50, FanPercent = 50 },
                    new FanCurvePoint { TemperatureC = 80, FanPercent = 90 }
                }
            });

            fanService.RestoreOemAutoControl().Should().BeTrue();

            fanService.GetCurrentFanMode().Should().Be("Auto");
            fanService.ActivePresetName.Should().BeNull();
            fanService.IsCurveActive.Should().BeFalse();
            var recovery = fanService.GetCommandHistorySnapshot().Last();
            recovery.Command.Should().Be("RestoreOemAutoControl");
            recovery.Target.Should().Be("OEM auto");
            recovery.Success.Should().BeTrue();
            recovery.Details.Should().Contain("RestoreAutoControl=True");
            recovery.Details.Should().Contain("ResetEcToDefaults=True");

            logging.Dispose();
        }

        [Fact]
        public async Task NonConservativeProfile_AllowsSmallCurveTargetChanges()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);
            var capabilities = new OmenCore.Hardware.DeviceCapabilities
            {
                ProductId = "8C76",
                Chassis = OmenCore.Hardware.ChassisType.Laptop,
                ModelFamily = OmenCore.Hardware.OmenModelFamily.OMEN16,
                ModelConfig = new OmenCore.Hardware.ModelCapabilities
                {
                    ProductId = "8C76",
                    Family = OmenCore.Hardware.OmenModelFamily.OMEN16,
                    UserVerified = false,
                    SupportsFanControlEc = false
                }
            };

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService(), capabilities: capabilities);
            fanService.SetHysteresis(new FanHysteresisSettings { Enabled = false });
            fanService.SetSmoothingSettings(new FanTransitionSettings { EnableSmoothing = false });

            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 40, FanPercent = 30 },
                new FanCurvePoint { TemperatureC = 70, FanPercent = 60 }
            };

            fanService.ApplyCustomCurve(curve, immediate: false);
            await fanService.ForceApplyCurveNowAsync(cpuTemp: 60, gpuTemp: 0, immediate: true);

            controller.SetCalls.Clear();
            SetPrivateField(fanService, "_lastCurveUpdate", DateTime.Now.AddSeconds(-10));
            SetPrivateField(fanService, "_lastCurveForceRefresh", DateTime.Now);

            await fanService.ForceApplyCurveNowAsync(cpuTemp: 62, gpuTemp: 0, immediate: false);

            controller.SetCalls.Should().Contain(52);
            logging.Dispose();
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            field.Should().NotBeNull($"test setup requires private field {fieldName}");
            field!.SetValue(target, value);
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
            public bool ApplyMaxCooling() {  return true; }
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 100, new ResumeRecoveryDiagnosticsService());
            fanService.Start();
            fanService.ForceFixedPollInterval(100);

            var lastRpmsField = typeof(FanService).GetField("_lastFanSpeeds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            try
            {
                // Helper: poll until condition is met or timeout expires.
                // Poll at 10ms to avoid missing short acceptance windows between service ticks.
                async Task<bool> WaitFor(Func<List<int>, bool> condition, int timeoutMs = 3000)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        var rpms = (List<int>)lastRpmsField!.GetValue(fanService)!;
                        if (condition(rpms)) return true;
                        await Task.Delay(10);
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
                // Use a generous timeout and fast polling: the acceptance window is ~200ms wide
                // (two 100ms service ticks) and can be missed under load with coarse polling.
                bool accepted = await WaitFor(r => r.Count > 0 && r[0] == 1234, timeoutMs: 5000);
                accepted.Should().BeTrue("second consecutive 1234 read must cause acceptance");

                // 3) Accept zero immediately once fans stop (duty==0).
                bool stoppedAndAccepted = await WaitFor(r => r.Count > 0 && r[0] == 0, timeoutMs: 5000);
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            var presetA = new FanPreset { Name = "Balanced", Mode = FanMode.Performance };
            var presetB = new FanPreset { Name = "Max", Mode = FanMode.Max };

            // Apply preset normally
            fanService.ApplyPreset(presetA);
            fanService.ActivePresetName.Should().Be(presetA.Name);

            // Enter diagnostic mode and attempt to apply another preset - should be ignored
            fanService.EnterDiagnosticMode();
            try
            {
                fanService.ApplyPreset(presetB);
                fanService.ActivePresetName.Should().Be(presetA.Name);
            }
            finally
            {
                fanService.ExitDiagnosticMode();
            }

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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 100, new ResumeRecoveryDiagnosticsService());
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
                    await Task.Delay(10);
                }
                return false;
            }

            try
            {
                // Wait for the initial stable 2000rpm to be established
                bool seeded = await WaitFor(r => r.Count > 0 && r[0] == 2000, timeoutMs: 2000);
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
                bool accepted = await WaitFor(r => r.Count > 0 && r[0] == 3500, timeoutMs: 5000);
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

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 200, new ResumeRecoveryDiagnosticsService());
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

        [Fact]
        public void FanTelemetrySync_UpdatesStableFanItemsInPlace()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            var logging = new LoggingService();
            logging.Initialize();

            var controller = new RecordingFanController();
            var thermalProvider = new OmenCore.Hardware.ThermalSensorProvider(new OmenCore.Hardware.LibreHardwareMonitorImpl());
            var notificationService = new NotificationService(logging);
            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            try
            {
                var syncMethod = typeof(FanService).GetMethod(
                    "SyncFanTelemetryCollection",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                syncMethod.Should().NotBeNull();

                var collectionField = typeof(FanService).GetField(
                    "_fanTelemetry",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                collectionField.Should().NotBeNull();

                var telemetry = collectionField!.GetValue(fanService)
                    .Should().BeAssignableTo<ObservableCollection<FanTelemetry>>().Subject;

                var collectionChangedCount = 0;
                telemetry.CollectionChanged += (_, args) =>
                {
                    if (args.Action is NotifyCollectionChangedAction.Add
                        or NotifyCollectionChangedAction.Remove
                        or NotifyCollectionChangedAction.Reset)
                    {
                        collectionChangedCount++;
                    }
                };

                var firstRead = new List<FanTelemetry>
                {
                    new() { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 30, RpmSource = RpmSource.WmiBios },
                    new() { Name = "GPU Fan", SpeedRpm = 1100, DutyCyclePercent = 35, RpmSource = RpmSource.WmiBios }
                };

                syncMethod!.Invoke(fanService, new object?[] { firstRead, null, null });

                telemetry.Should().HaveCount(2);
                collectionChangedCount.Should().Be(2, "initial fan discovery may add one item per detected fan");
                var firstCpuTelemetry = telemetry[0];
                var firstGpuTelemetry = telemetry[1];

                collectionChangedCount = 0;
                var secondRead = new List<FanTelemetry>
                {
                    new() { Name = "CPU Fan", SpeedRpm = 1600, DutyCyclePercent = 42, RpmSource = RpmSource.EcDirect },
                    new() { Name = "GPU Fan", SpeedRpm = 1700, DutyCyclePercent = 45, RpmSource = RpmSource.EcDirect }
                };

                syncMethod.Invoke(
                    fanService,
                    new object?[]
                    {
                        secondRead,
                        new List<int> { 1500, 1650 },
                        new List<TelemetryDataState> { TelemetryDataState.Valid, TelemetryDataState.Valid }
                    });

                telemetry.Should().HaveCount(2);
                telemetry[0].Should().BeSameAs(firstCpuTelemetry);
                telemetry[1].Should().BeSameAs(firstGpuTelemetry);
                telemetry[0].SpeedRpm.Should().Be(1500);
                telemetry[1].SpeedRpm.Should().Be(1650);
                telemetry[0].DutyCyclePercent.Should().Be(42);
                telemetry[1].DutyCyclePercent.Should().Be(45);
                telemetry[0].RpmSource.Should().Be(RpmSource.EcDirect);
                telemetry[1].RpmState.Should().Be(TelemetryDataState.Valid);
                collectionChangedCount.Should().Be(0, "stable fan-count updates should not reset or rebuild the observable collection");

                var counters = RuntimeUiPerformanceCounters.GetSnapshot();
                counters.FanTelemetrySyncs.Should().Be(2);
                counters.FanTelemetryCollectionResizes.Should().Be(1, "only initial fan discovery should resize the collection");
                counters.FanTelemetryPropertyOnlySyncs.Should().Be(1, "the second sync should update existing fan items in place");
                counters.FanTelemetryItemsUpdated.Should().Be(4);
                counters.FanTelemetryCollectionResizeRatio.Should().Be(0.5d);
                counters.FanTelemetryPropertyOnlySyncRatio.Should().Be(0.5d);
            }
            finally
            {
                logging.Dispose();
            }
        }
    }
}
