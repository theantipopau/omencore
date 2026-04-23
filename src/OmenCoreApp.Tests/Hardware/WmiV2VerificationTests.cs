using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class WmiV2VerificationTests
    {
        private class FakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled = false;
            public bool IsAvailable => true;
            public string Status => "FakeWmi";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect()
            {
                if (_maxEnabled)
                    return (4200, 4100);
                return (400, 380);
            }

            public (byte fan1, byte fan2)? GetFanLevel()
            {
                return (10, 10);
            }

            public bool SetFanMax(bool enabled)
            {
                _maxEnabled = enabled;
                return true;
            }

            public bool SetFanLevel(byte fan1, byte fan2)
            {
                // emulate success
                return true;
            }

            public bool SetFanMode(HpWmiBios.FanMode mode)
            {
                return true;
            }

            // Additional IHpWmiBios members (stubs) added to satisfy interface changes
            public double? GetTemperature() => 45.0;
            public double? GetGpuTemperature() => 50.0;
            public void ExtendFanCountdown() { /* no-op for tests */ }
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { /* no-op */ }
        }

        [Fact]
        public void VerifyMaxApplied_With_ThermalPolicyV2_Succeeds_When_RPM_Increases()
        {
            var fake = new FakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var wrapper = new OmenCore.Hardware.WmiFanControllerWrapper(controller);

            // simulate user enabling max
            wrapper.SetMaxFanSpeed(true).Should().BeTrue();

            // verification should detect high RPM via GetFanRpmDirect
            wrapper.VerifyMaxApplied(out var details).Should().BeTrue();
            details.Should().Contain("ReadFanSpeeds");
        }

        [Fact]
        public void VerifyMaxApplied_Fails_When_RPM_Does_Not_Increase()
        {
            var fake = new FakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var wrapper = new OmenCore.Hardware.WmiFanControllerWrapper(controller);

            // Ensure fake never flips to high RPM (do not call SetFanMax)
            // wrapper.SetMaxFanSpeed(false) no-op

            // verification should return false because RPMs are low
            wrapper.VerifyMaxApplied(out var details).Should().BeFalse();
            details.Should().Contain("ReadFanSpeeds");
        }

        [Fact]
        public void ApplyPreset_Max_RollsBack_When_SetFanMax_Succeeds_But_Ineffective()
        {
            // Fake that accepts SetFanMax(true) but provides no RPM or level change
            var fake = new NoEffectFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var preset = new OmenCore.Models.FanPreset { Name = "Max" };

            controller.ApplyPreset(preset).Should().BeFalse();
            // Ensure the BIOS was told to clear Max after verification failed
            fake.SetFanMaxCalls.Should().Contain(new[] { true, false });
            controller.IsManualControlActive.Should().BeFalse();
        }

        private class NoEffectFakeWmiBios : IHpWmiBios
        {
            public List<bool> SetFanMaxCalls { get; } = new List<bool>();
            public bool IsAvailable => true;
            public string Status => "NoEffectFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => (400, 380); // never increases
            public (byte fan1, byte fan2)? GetFanLevel() => (10, 10); // no change
            public bool SetFanMax(bool enabled) { SetFanMaxCalls.Add(enabled); return true; }
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
        public void ReadFanSpeeds_Returns_Rpms_For_ThermalPolicyV2()
        {
            var fake = new FakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var wrapper = new OmenCore.Hardware.WmiFanControllerWrapper(controller);

            var speeds = wrapper.ReadFanSpeeds().ToList();
            speeds.Should().HaveCountGreaterThan(0);
            speeds[0].SpeedRpm.Should().Be(400);

            // enabling max should change the RPMs from the fake implementation
            wrapper.SetMaxFanSpeed(true).Should().BeTrue();
            var speedsAfter = wrapper.ReadFanSpeeds().ToList();
            speedsAfter[0].SpeedRpm.Should().Be(4200);
        }

        [Fact]
        public void ApplyPreset_Max_Verified_By_Rpm()
        {
            var fake = new FakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var preset = new FanPreset { Name = "Max" };

            controller.ApplyPreset(preset).Should().BeTrue();
            controller.IsManualControlActive.Should().BeTrue();
            controller.VerifyFailCount.Should().Be(0);
        }

        [Fact]
        public void ApplyPreset_Max_Verified_By_LevelFallback_When_RpmUnavailable()
        {
            // Fake: no RPM readback but fan level reports a high value -> should verify via level fallback
            var fake = new FallbackFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);
            var preset = new FanPreset { Name = "Max" };

            controller.ApplyPreset(preset).Should().BeTrue();
            controller.IsManualControlActive.Should().BeTrue();
        }

        [Fact]
        public void CountdownExtensionCallback_MaxMode_UsesKeepalive_WhenTelemetryHealthy()
        {
            var fake = new MaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.ApplyPreset(new FanPreset { Name = "Max" }).Should().BeTrue();
            controller.StopCountdownExtension();

            var maxCallsBefore = fake.SetFanMaxTrueCalls;
            InvokeCountdownExtensionCallback(controller);

            fake.ExtendCountdownCalls.Should().Be(1);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore);
        }

        [Fact]
        public void CountdownExtensionCallback_MaxMode_ReappliesAfterSustainedDrop()
        {
            var fake = new MaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.ApplyPreset(new FanPreset { Name = "Max" }).Should().BeTrue();
            controller.StopCountdownExtension();

            var maxCallsBefore = fake.SetFanMaxTrueCalls;
            fake.ForceLowTelemetry = true;

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore);

            SetPrivateField(controller, "_lastMaxModeMaintenanceUtc", System.DateTime.MinValue);
            InvokeCountdownExtensionCallback(controller);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore + 1);
        }

        // Fake implementation to simulate V2 BIOS that does not expose RPM but reports fan levels.
        private class FallbackFakeWmiBios : IHpWmiBios
        {
            public bool IsAvailable => true;
            public string Status => "FallbackFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => null; // RPM not available
            public (byte fan1, byte fan2)? GetFanLevel() => (45, 42); // high level -> indicates spin-up
            public bool SetFanMax(bool enabled) => true;
            public bool SetFanLevel(byte fan1, byte fan2) => true;
            public bool SetFanMode(HpWmiBios.FanMode mode) => true;

            public double? GetTemperature() => 50.0;
            public double? GetGpuTemperature() => 50.0;
            public void ExtendFanCountdown() { }
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private class MaintenanceFakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled;
            public bool ForceLowTelemetry { get; set; }
            public int SetFanMaxTrueCalls { get; private set; }
            public int ExtendCountdownCalls { get; private set; }

            public bool IsAvailable => true;
            public string Status => "MaintenanceFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect()
            {
                if (!_maxEnabled)
                {
                    return (500, 480);
                }

                return ForceLowTelemetry ? (900, 880) : (4300, 4200);
            }

            public (byte fan1, byte fan2)? GetFanLevel()
            {
                if (!_maxEnabled)
                {
                    return (8, 8);
                }

                return ForceLowTelemetry ? ((byte)15, (byte)14) : ((byte)92, (byte)90);
            }

            public bool SetFanMax(bool enabled)
            {
                _maxEnabled = enabled;
                if (enabled)
                {
                    SetFanMaxTrueCalls++;
                }

                return true;
            }

            public bool SetFanLevel(byte fan1, byte fan2) => true;
            public bool SetFanMode(HpWmiBios.FanMode mode) => true;
            public double? GetTemperature() => 45.0;
            public double? GetGpuTemperature() => 50.0;
            public void ExtendFanCountdown() => ExtendCountdownCalls++;
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private static void InvokeCountdownExtensionCallback(WmiFanController controller)
        {
            SetPrivateField(controller, "_lastMaxModeMaintenanceUtc", System.DateTime.MinValue);
            var callback = typeof(WmiFanController).GetMethod("CountdownExtensionCallback", BindingFlags.NonPublic | BindingFlags.Instance);
            callback.Should().NotBeNull();
            callback!.Invoke(controller, new object?[] { null });
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull();
            field!.SetValue(instance, value);
        }
    }
}
