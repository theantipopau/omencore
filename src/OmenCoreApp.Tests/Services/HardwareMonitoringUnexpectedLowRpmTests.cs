using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    // GitHub #143 (Victus 8DCD): Performance fan reportedly collapsed from ~5000 RPM to below
    // 2000 RPM while CPU stayed above 80C for roughly five minutes. This is diagnostic-only
    // instrumentation - it does not change fan control behavior or attempt to fix #143 itself
    // (that needs a field reproduction per this project's evidence-gate rule). These tests pin
    // that the warning fires only for a sustained, well-evidenced anomaly and stays silent
    // otherwise, so it produces signal instead of log noise.
    public class HardwareMonitoringUnexpectedLowRpmTests
    {
        private sealed class StubBridge : IHardwareMonitorBridge
        {
            public string MonitoringSource => "StubBridge";
            public System.Threading.Tasks.Task<MonitoringSample> ReadSampleAsync(System.Threading.CancellationToken token)
                => System.Threading.Tasks.Task.FromResult(new MonitoringSample());
            public System.Threading.Tasks.Task<bool> TryRestartAsync() => System.Threading.Tasks.Task.FromResult(true);
        }

        private static (HardwareMonitoringService svc, MethodInfo method, List<string> logs) CreateHarness()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var logs = new List<string>();
            logging.LogEmitted += s => logs.Add(s);

            var svc = new HardwareMonitoringService(new StubBridge(), logging, new MonitoringPreferences(), new ResumeRecoveryDiagnosticsService());
            var method = typeof(HardwareMonitoringService).GetMethod("CheckForUnexpectedLowRpmAtHighTemp", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            return (svc, method!, logs);
        }

        private static MonitoringSample HighTempLowRpmSample(int cpuRpm = 1500, int gpuRpm = 4000) => new MonitoringSample
        {
            CpuTemperatureC = 85,
            GpuTemperatureC = 60,
            Fan1Rpm = cpuRpm,
            Fan2Rpm = gpuRpm,
            Fan1RpmState = TelemetryDataState.Valid,
            Fan2RpmState = TelemetryDataState.Valid
        };

        [Fact]
        public void Warns_AfterSustainedHighTempLowRpm()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var sample = HighTempLowRpmSample();

            for (var i = 0; i < 5; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Should().Contain(l => l.Contains("CPU fan reads 1500 RPM") && l.Contains("85.0"),
                "5 consecutive readings of high temp + low RPM should trigger the diagnostic warning");
        }

        [Fact]
        public void DoesNotWarn_BeforeSustainedThreshold()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var sample = HighTempLowRpmSample();

            for (var i = 0; i < 4; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Should().NotContain(l => l.Contains("unusually low"),
                "a handful of readings below the sustained-count threshold must not fire the warning - single dips are not evidence of a real problem");
        }

        [Fact]
        public void DoesNotWarn_WhenTemperatureBelowThreshold()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var sample = new MonitoringSample
            {
                CpuTemperatureC = 60, // below the 80C threshold
                Fan1Rpm = 1200,
                Fan1RpmState = TelemetryDataState.Valid
            };

            for (var i = 0; i < 10; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Should().NotContain(l => l.Contains("unusually low"),
                "a quiet fan at a normal temperature is expected behavior, not an anomaly");
        }

        [Fact]
        public void DoesNotWarn_WhenRpmAboveFloor()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var sample = new MonitoringSample
            {
                CpuTemperatureC = 85,
                Fan1Rpm = 4800, // healthy response to a high temperature
                Fan1RpmState = TelemetryDataState.Valid
            };

            for (var i = 0; i < 10; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Should().NotContain(l => l.Contains("unusually low"),
                "a fan correctly spinning up at high temperature must never be flagged");
        }

        [Fact]
        public void DoesNotWarn_WhenRpmStateIsNotValid()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            // Same shape as the failing case, but the RPM reading itself is not trustworthy -
            // e.g. Zero/Stale/Unavailable/Invalid. Warning on untrustworthy data would be noise,
            // not evidence.
            var sample = new MonitoringSample
            {
                CpuTemperatureC = 85,
                Fan1Rpm = 0,
                Fan1RpmState = TelemetryDataState.Unavailable
            };

            for (var i = 0; i < 10; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Should().NotContain(l => l.Contains("unusually low"),
                "an untrustworthy RPM reading (not TelemetryDataState.Valid) must not be treated as evidence either way");
        }

        [Fact]
        public void ResetsConsecutiveCount_WhenConditionClears()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var badSample = HighTempLowRpmSample();
            var goodSample = new MonitoringSample
            {
                CpuTemperatureC = 85,
                Fan1Rpm = 5000,
                Fan1RpmState = TelemetryDataState.Valid
            };

            // 4 bad readings (below the 5-reading sustained threshold)...
            for (var i = 0; i < 4; i++)
            {
                method.Invoke(svc, new object[] { badSample });
            }

            // ...then the fan recovers, which must reset the counter...
            method.Invoke(svc, new object[] { goodSample });

            // ...so 4 more bad readings afterward still should not reach the threshold.
            for (var i = 0; i < 4; i++)
            {
                method.Invoke(svc, new object[] { badSample });
            }

            logs.Should().NotContain(l => l.Contains("unusually low"),
                "a recovery in between must reset the consecutive-reading counter, not let it carry over across the gap");
        }

        [Fact]
        public void WarnsOnlyOnce_PerSustainedAnomalyWindow()
        {
            var harness = CreateHarness();
            using var svc = harness.svc;
            var method = harness.method;
            var logs = harness.logs;
            var sample = HighTempLowRpmSample();

            for (var i = 0; i < 20; i++)
            {
                method.Invoke(svc, new object[] { sample });
            }

            logs.Count(l => l.Contains("unusually low")).Should().Be(1,
                "the warning should fire once per sustained anomaly window, not spam the log on every tick");
        }
    }
}
