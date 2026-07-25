using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Covers <c>WmiFanController.IsMaxModeTelemetryHealthy</c>'s drop detection, including the
    /// self-calibrating observed-peak fallback added for boards whose real fan-level ceiling sits
    /// below <c>MaxFanLevel * MaxModeHealthyLevelRatio</c>.
    ///
    /// Field case this fallback exists for (GitHub #153, OMEN 17-ck1xxx / board 8A18): nominal
    /// MaxFanLevel 55 derives a healthy-floor of 50, but the board holds a steady 46-48 while Max
    /// mode is genuinely active. Every maintenance cycle therefore read as "sustained drop" and
    /// re-asserted SetFanMax, logging "External fan reset suspected" every ~20s for minutes while
    /// firmware was already holding max.
    ///
    /// Reflection is used to drive the private health check directly — this codebase's existing
    /// pattern for private-method coverage (see RyzenControlTests, BiosUpdateServiceTests).
    /// </summary>
    public class WmiFanControllerMaxModeHealthTests
    {
        /// <summary>
        /// Minimal IHpWmiBios whose fan-level and RPM readback are settable per test, so a specific
        /// board's telemetry shape can be replayed without real hardware.
        /// </summary>
        private sealed class LevelReadbackFakeWmiBios : IHpWmiBios
        {
            public LevelReadbackFakeWmiBios(int maxFanLevel)
            {
                MaxFanLevel = maxFanLevel;
            }

            public (byte fan1, byte fan2)? LevelReadback { get; set; }
            public (int fan1Rpm, int fan2Rpm)? RpmReadback { get; set; }

            public bool IsAvailable => true;
            public string Status => "LevelReadbackFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V1;
            public int FanCount => 2;
            public int MaxFanLevel { get; }

            public (byte fan1, byte fan2)? GetFanLevel() => LevelReadback;
            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => RpmReadback;

            public bool SetFanMax(bool enabled) => true;
            public bool SetFanLevel(byte fan1, byte fan2) => true;
            public bool SetFanMode(HpWmiBios.FanMode mode) => true;
            public double? GetTemperature() => 45.0;
            public double? GetGpuTemperature() => 50.0;
            public void ExtendFanCountdown() { }
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private static bool InvokeHealthCheck(WmiFanController controller, out string details)
        {
            var method = typeof(WmiFanController).GetMethod(
                "IsMaxModeTelemetryHealthy",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull("IsMaxModeTelemetryHealthy should exist on WmiFanController");

            var args = new object?[] { null };
            var healthy = (bool)method!.Invoke(controller, args)!;
            details = (string)args[0]!;
            return healthy;
        }

        private static void InvokeResetTracking(WmiFanController controller)
        {
            var method = typeof(WmiFanController).GetMethod(
                "ResetMaxModeHealthTracking",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull("ResetMaxModeHealthTracking should exist on WmiFanController");
            method!.Invoke(controller, Array.Empty<object>());
        }

        [Fact]
        public void BoardThatNeverReachesNominalFloor_BecomesHealthy_InsteadOfReassertingForever()
        {
            // Board 8A18 shape: nominal 55 → derived floor 50, but hardware holds a steady 46/48.
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 55)
            {
                LevelReadback = (46, 48)
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            // The first observations are deliberately still strict — a single low read must not be
            // enough to lower the bar, otherwise a genuine drop would be excused immediately.
            InvokeHealthCheck(controller, out _).Should().BeFalse("first observation must stay strict");
            InvokeHealthCheck(controller, out _).Should().BeFalse("second observation must stay strict");

            // Once the nominal floor has proven unreachable across several consecutive reads, the
            // observed peak becomes the reference point and the steady hold reads as healthy.
            var healthy = InvokeHealthCheck(controller, out var details);

            healthy.Should().BeTrue(
                "a board holding a stable 46/48 against an unreachable nominal floor of 50 is holding max, not dropping");
            details.Should().Contain("observed peak 48");
            details.Should().Contain("unreachable");
        }

        [Fact]
        public void BoardThatReachesNominalFloor_KeepsStrictCheck_AndDetectsRealDrop()
        {
            // Nominal 100 → derived floor 90. This board genuinely reaches it.
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 100)
            {
                LevelReadback = (95, 95)
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            InvokeHealthCheck(controller, out var healthyDetails).Should().BeTrue();
            healthyDetails.Should().NotContain("observed peak",
                "a board that reaches the nominal floor must keep the strict check, unchanged");

            // Now simulate the stuck-at-mid case the strict threshold exists to catch. Because this
            // board already proved it can reach the nominal floor, the observed-peak fallback must
            // NOT engage and excuse the drop.
            fake.LevelReadback = (70, 70);
            for (var i = 0; i < 5; i++)
            {
                InvokeHealthCheck(controller, out var dropDetails).Should().BeFalse(
                    "a drop below the nominal floor on a board that can reach it is a real drop");
                dropDetails.Should().NotContain("observed peak");
            }
        }

        [Fact]
        public void GenuineCollapseTowardIdle_IsStillDetected_EvenOnObservedPeakBoards()
        {
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 55)
            {
                LevelReadback = (46, 48)
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            // Establish the observed peak and let the fallback engage.
            for (var i = 0; i < 4; i++)
            {
                InvokeHealthCheck(controller, out _);
            }
            InvokeHealthCheck(controller, out _).Should().BeTrue("peak-trust should be active by now");

            // Something really did reset the fans back toward idle/auto.
            fake.LevelReadback = (10, 8);

            InvokeHealthCheck(controller, out var details).Should().BeFalse(
                "a collapse toward idle must still be caught by the absolute backstop");
            details.Should().Contain("levels=10/8");
        }

        [Fact]
        public void ResetMaxModeHealthTracking_ClearsObservedPeak_SoItCannotLeakIntoNextHold()
        {
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 55)
            {
                LevelReadback = (46, 48)
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            for (var i = 0; i < 4; i++)
            {
                InvokeHealthCheck(controller, out _);
            }
            InvokeHealthCheck(controller, out _).Should().BeTrue("peak-trust should be active before reset");

            // A new Max hold starts: the learned peak must be discarded, restoring the strict check
            // so the new hold is evaluated on its own evidence.
            InvokeResetTracking(controller);

            InvokeHealthCheck(controller, out var details).Should().BeFalse(
                "after reset the strict nominal check must apply again");
            details.Should().NotContain("observed peak");
        }

        [Fact]
        public void TelemetryUnavailable_ReportsHealthy_SoKeepaliveIsPreferredOverForcedReapply()
        {
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 55)
            {
                LevelReadback = null,
                RpmReadback = null
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            InvokeHealthCheck(controller, out var details).Should().BeTrue();
            details.Should().Be("telemetry unavailable");
        }

        [Fact]
        public void RpmTelemetry_IsUsedWhenLevelReadbackIsUnavailable()
        {
            var fake = new LevelReadbackFakeWmiBios(maxFanLevel: 55)
            {
                LevelReadback = null,
                RpmReadback = (4200, 4100)
            };
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            InvokeResetTracking(controller);

            InvokeHealthCheck(controller, out _).Should().BeTrue("4200 RPM is above the 2000 RPM floor");

            fake.RpmReadback = (500, 480);
            InvokeHealthCheck(controller, out _).Should().BeFalse("500 RPM is below the 2000 RPM floor");
        }
    }
}
