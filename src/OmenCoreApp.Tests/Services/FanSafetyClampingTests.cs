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

        // ─── ThermalProtectionEnabled = false: clamp must not intervene at all ─
        // Field report (SprinkSponk-adjacent, boards 8D87/88F7): request to be able to disable
        // the "thermal emergency forces max fan" behavior entirely, including for custom curves.

        [Theory]
        [InlineData(0.0, 95.0)]
        [InlineData(0.0, 99.9)]
        [InlineData(0.0, 90.0)]
        public void ThermalProtectionDisabled_ClampDoesNotIntervene_EvenAtEmergencyTemps(double fanPercent, double tempC)
        {
            var svc = CreateFanService();
            svc.SetHysteresis(new FanHysteresisSettings { ThermalProtectionEnabled = false });

            var result = InvokeApplySafetyBoundsClamping(svc, fanPercent, tempC);

            result.Should().Be(fanPercent,
                $"with thermal protection disabled, the safety clamp must never override the curve's own value, even at {tempC}°C");
        }

        [Fact]
        public void ThermalProtectionReEnabled_ClampInterveneAgain()
        {
            var svc = CreateFanService();
            svc.SetHysteresis(new FanHysteresisSettings { ThermalProtectionEnabled = false });
            svc.SetHysteresis(new FanHysteresisSettings { ThermalProtectionEnabled = true });

            var result = InvokeApplySafetyBoundsClamping(svc, 0.0, 95.0);

            result.Should().Be(100.0, "re-enabling thermal protection must restore the emergency clamp");
        }

        // ─── Configurable emergency threshold ───────────────────────────────────

        [Fact]
        public void CustomEmergencyThreshold_ClampTriggersAtConfiguredTemp_NotDefault()
        {
            var svc = CreateFanService();
            // Lower ramp threshold so the emergency threshold (92) still clears the +2°C margin.
            svc.SetHysteresis(new FanHysteresisSettings
            {
                ThermalProtectionEnabled = true,
                ThermalProtectionThreshold = 85.0,
                ThermalEmergencyThreshold = 92.0
            });

            // Below the custom threshold: curve trusted (91°C is above the old hardcoded 90°C
            // "critical" floor, so this only proves the true emergency force-100 doesn't fire yet).
            var belowEmergency = InvokeApplySafetyBoundsClamping(svc, 82.0, 91.9);
            belowEmergency.Should().Be(82.0, "82% at 91.9°C is already above the 80% critical floor and below the custom 92°C emergency point");

            // At/above the custom threshold: forced to 100%, not gated on the old hardcoded 95°C.
            var atEmergency = InvokeApplySafetyBoundsClamping(svc, 50.0, 92.0);
            atEmergency.Should().Be(100.0, "a custom 92°C emergency threshold must force 100% at 92°C, well below the old hardcoded 95°C");
        }

        [Fact]
        public void EmergencyThreshold_ClampedToConfiguredRange()
        {
            var svc = CreateFanService();
            // Way outside the documented 90-99°C range on both ends.
            svc.SetHysteresis(new FanHysteresisSettings
            {
                ThermalProtectionEnabled = true,
                ThermalProtectionThreshold = 75.0,
                ThermalEmergencyThreshold = 40.0 // should clamp up to 90 (the floor), not stay at 40
            });

            // If the raw 40°C value leaked through unclamped, this would already be "emergency."
            // It must not be — the effective threshold should sit at or above the 90°C floor.
            var result = InvokeApplySafetyBoundsClamping(svc, 30.0, 89.0);
            result.Should().BeLessThan(100.0, "an out-of-range emergency threshold (40°C) must be clamped up to the 90-99°C floor, not applied literally");
        }

        [Fact]
        public void EmergencyThreshold_KeptAboveRampThreshold_WhenConfiguredBelowIt()
        {
            var svc = CreateFanService();
            // Emergency (91) configured below ramp threshold (95) — must be bumped up, not left inverted.
            svc.SetHysteresis(new FanHysteresisSettings
            {
                ThermalProtectionEnabled = true,
                ThermalProtectionThreshold = 95.0,
                ThermalEmergencyThreshold = 91.0
            });

            // 96°C is above the configured ramp threshold (95) but would also be above the raw
            // (un-bumped) emergency value of 91. The critical 90°C floor already forces >=80% by
            // this point regardless, so use fan=100 with a value the critical floor wouldn't touch
            // to isolate the emergency tier: at exactly the bumped threshold (97 = 95+2), it must
            // force 100%; just below it (96.9), the emergency tier must not have fired yet even
            // though it's above the naive/un-bumped 91°C value.
            var justBelowBumpedThreshold = InvokeApplySafetyBoundsClamping(svc, 80.0, 96.9);
            justBelowBumpedThreshold.Should().Be(80.0,
                "96.9°C is below the bumped emergency threshold (97 = ramp 95 + 2°C margin); only the 80% critical floor should apply, not a forced 100%");

            var atBumpedThreshold = InvokeApplySafetyBoundsClamping(svc, 50.0, 97.0);
            atBumpedThreshold.Should().Be(100.0,
                "97°C is at the bumped emergency threshold (ramp 95°C + 2°C margin) and must force 100%");
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
