// Area C — Fan safety-clamping determinism tests (no-hardware session 2026-04-16)
// These tests verify the ApplySafetyBoundsClamping logic in FanService via reflection.
// All temperature thresholds must be deterministic and must never allow fan levels
// below the safety floor for their respective temperature band.
// No physical OMEN hardware required.

using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class FanSafetyClampingTests
    {
        private static double InvokeApplySafetyBoundsClamping(FanService service, double fanPercent, double temperatureC)
        {
            var method = typeof(FanService).GetMethod(
                "ApplySafetyBoundsClamping",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method.Should().NotBeNull("ApplySafetyBoundsClamping must exist as a private instance method");

            return (double)method!.Invoke(service, new object[] { fanPercent, temperatureC })!;
        }

        private static FanService CreateFanService()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var notificationService = new NotificationService(logging);
            var controller = new MinimalFanController();
            var hwImpl = new LibreHardwareMonitorImpl();
            var thermalProvider = new ThermalSensorProvider(hwImpl);
            return new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
        }

        // ─── Below 80°C — user curve trusted entirely ────────────────────────

        [Theory]
        [InlineData(0.0, 79.9)]
        [InlineData(10.0, 50.0)]
        [InlineData(30.0, 65.0)]
        [InlineData(100.0, 79.9)]
        public void BelowEighty_FanPercentUnchanged(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);
            result.Should().Be(fanPercent,
                $"at {tempC}°C the safety clamp must not intervene; user curve value {fanPercent}% must be respected");
        }

        // ─── 80–84.9°C — minimum 40% ─────────────────────────────────────────

        [Theory]
        [InlineData(10.0, 80.0)]
        [InlineData(39.9, 84.0)]
        public void EightyDegrees_ClampedToFortyPercent(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);
            result.Should().BeGreaterThanOrEqualTo(40.0,
                $"at {tempC}°C fan must be at least 40%; requested {fanPercent}%");
        }

        [Fact]
        public void EightyDegrees_HighFanPercent_NotClamped()
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, 60.0, 82.0);
            result.Should().Be(60.0, "60% at 82°C is already above the 40% floor; must not be changed");
        }

        // ─── 85–89.9°C — minimum 60% ─────────────────────────────────────────

        [Theory]
        [InlineData(10.0, 85.0)]
        [InlineData(59.9, 89.0)]
        public void EightyFiveDegrees_ClampedToSixtyPercent(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);
            result.Should().BeGreaterThanOrEqualTo(60.0,
                $"at {tempC}°C fan must be at least 60%; requested {fanPercent}%");
        }

        // ─── 90–94.9°C — minimum 80% ─────────────────────────────────────────

        [Theory]
        [InlineData(10.0, 90.0)]
        [InlineData(79.9, 94.0)]
        public void NinetyDegrees_ClampedToEightyPercent(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);
            result.Should().BeGreaterThanOrEqualTo(80.0,
                $"at {tempC}°C fan must be at least 80%; requested {fanPercent}%");
        }

        // ─── 95°C+ — always 100% (emergency override) ────────────────────────

        [Theory]
        [InlineData(0.0, 95.0)]
        [InlineData(50.0, 100.0)]
        [InlineData(99.9, 95.0)]
        public void NinetyFiveDegrees_ForcedToHundredPercent(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);
            result.Should().Be(100.0,
                $"at {tempC}°C the emergency clamp must force fans to 100%");
        }

        [Fact]
        public void NinetyFiveDegrees_AlreadyHundred_StaysHundred()
        {
            var svc = CreateFanService();
            var result = InvokeApplySafetyBoundsClamping(svc, 100.0, 95.0);
            result.Should().Be(100.0);
        }

        // ─── Monotonicity: higher temperatures never produce lower floors ─────

        [Fact]
        public void SafetyFloor_IsMonotonicallyNonDecreasing_WithTemperature()
        {
            // The safety floor must not decrease as temperature increases.
            // This guards against future threshold regressions.
            var svc = CreateFanService();
            double previousFloor = 0;

            for (int tempInt = 0; tempInt <= 100; tempInt++)
            {
                double temp = tempInt;
                // Use a 0% desired fan to measure the effective floor at each temperature
                double floor = InvokeApplySafetyBoundsClamping(svc, 0.0, temp);
                floor.Should().BeGreaterThanOrEqualTo(previousFloor,
                    $"safety floor at {temp}°C ({floor}%) must not be less than floor at {temp - 1}°C ({previousFloor}%)");
                previousFloor = floor;
            }
        }

        // ─── Minimal stub controller ──────────────────────────────────────────

        private sealed class MinimalFanController : IFanController
        {
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
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public void ApplyMaxCooling() { }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = "stub"; return false; }
        }
    }
}
