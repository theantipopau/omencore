using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Covers <c>WmiFanController.SetPerformanceMode(string)</c>'s release of the BIOS Max-fan
    /// latch when Max mode was active before the mode switch.
    ///
    /// Field case (SAINTOP, HP Victus 15 fa2082wm / board 8DCD): Performance Mode → Maximum Fans
    /// → switching back to Balanced (either from the General tab or via Custom/Auto) left the fans
    /// stuck at maximum speed. Root cause: <c>SetPerformanceMode</c> called
    /// <c>_wmiBios.SetFanMode(...)</c> and, on success, cleared <c>_isMaxModeActive</c> without ever
    /// calling <c>_wmiBios.SetFanMax(false)</c> — <c>SetFanMode</c> and <c>SetFanMax</c> are two
    /// independently-required BIOS commands (see <c>ResetFromMaxMode</c>'s own Step 1/Step 2). The
    /// stale-false flag then caused the next <c>RestoreAutoControl()</c> to skip its own reset
    /// entirely, since <c>ResetFromMaxMode()</c> only sends <c>SetFanMax(false)</c> when
    /// <c>_isMaxModeActive</c> is true. Only fully closing the app (which unconditionally resets
    /// EC/WMI state on shutdown) released the latch.
    /// </summary>
    public class WmiFanControllerPerformanceModeMaxReleaseTests
    {
        private sealed class CallTrackingFakeWmiBios : IHpWmiBios
        {
            public List<bool> SetFanMaxCalls { get; } = new();

            public bool IsAvailable => true;
            public string Status => "CallTrackingFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V1;
            public int FanCount => 2;
            public int MaxFanLevel => 55;

            public (byte fan1, byte fan2)? GetFanLevel() => null;
            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => null;

            public bool SetFanMax(bool enabled)
            {
                SetFanMaxCalls.Add(enabled);
                return true;
            }

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

        [Fact]
        public void SwitchingAwayFromMaxMode_ReleasesTheFanMaxLatch()
        {
            var fake = new CallTrackingFakeWmiBios();
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetMaxFanSpeed(true).Should().BeTrue();
            fake.SetFanMaxCalls.Should().Equal(new[] { true });

            controller.SetPerformanceMode("Balanced").Should().BeTrue();

            fake.SetFanMaxCalls.Should().Equal(new[] { true, false },
                "switching performance mode away from an active Max hold must release the BIOS SetFanMax latch, not just clear the in-memory flag");
            controller.IsManualControlActive.Should().BeFalse();
        }

        [Fact]
        public void SwitchingModeWithoutMaxModeActive_DoesNotSendRedundantSetFanMax()
        {
            var fake = new CallTrackingFakeWmiBios();
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetPerformanceMode("Balanced").Should().BeTrue();

            fake.SetFanMaxCalls.Should().BeEmpty(
                "no Max hold was active, so no SetFanMax call — redundant or not — should be sent on this transition");
        }

        [Fact]
        public void ReleasedMaxLatch_LetsRestoreAutoControlSkipItsOwnRedundantReset()
        {
            var fake = new CallTrackingFakeWmiBios();
            using var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetMaxFanSpeed(true).Should().BeTrue();
            controller.SetPerformanceMode("Balanced").Should().BeTrue();
            fake.SetFanMaxCalls.Should().Equal(new[] { true, false });

            // The latch is already released and in-memory state already reflects that, so
            // RestoreAutoControl's own gate correctly finds nothing left to do.
            controller.RestoreAutoControl().Should().BeTrue();
            fake.SetFanMaxCalls.Should().Equal(new[] { true, false },
                "RestoreAutoControl must not need to send another SetFanMax now that SetPerformanceMode already released it");
        }
    }
}
