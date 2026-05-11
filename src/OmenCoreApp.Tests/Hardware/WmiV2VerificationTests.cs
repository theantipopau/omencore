using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
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

        [Fact]
        public void SetFanSpeeds_DualMax_UsesCeilingFallback_WhenSetFanMaxFails()
        {
            var fake = new MaxFallbackLevelFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetFanSpeeds(100, 100).Should().BeTrue();
            fake.LastSetFanLevel.Should().NotBeNull();
            fake.LastSetFanLevel!.Value.fan1.Should().Be((byte)100);
            fake.LastSetFanLevel!.Value.fan2.Should().Be((byte)100);
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

        private class MaxFallbackLevelFakeWmiBios : IHpWmiBios
        {
            public (byte fan1, byte fan2)? LastSetFanLevel { get; private set; }

            public bool IsAvailable => true;
            public string Status => "MaxFallbackLevelFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => (4200, 4100);
            public (byte fan1, byte fan2)? GetFanLevel() => LastSetFanLevel ?? (0, 0);

            public bool SetFanMax(bool enabled) => false;

            public bool SetFanLevel(byte fan1, byte fan2)
            {
                LastSetFanLevel = (fan1, fan2);
                return true;
            }

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
            controller.LastMaxModeExternalResetUtc.Should().NotBeNull();
            controller.LastMaxModeExternalResetDetails.Should().Contain("Max telemetry dropped");
            controller.LastMaxModeExternalResetDetails.Should().Contain("another controller");

            var history = controller.GetCommandHistory();
            history.Should().Contain(entry =>
                entry.Command == "SetFanMax(true)" &&
                entry.Success &&
                entry.Error != null &&
                entry.Error.Contains("Max telemetry dropped"));
        }

        [Fact]
        public void CountdownExtensionCallback_MaxMode_TelemetryUnavailable_PeriodicallyReassertsMax()
        {
            var fake = new TelemetryUnavailableMaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetMaxFanSpeed(true).Should().BeTrue();
            controller.StopCountdownExtension();

            var maxCallsBefore = fake.SetFanMaxTrueCalls;

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanMaxTrueCalls.Should().Be(maxCallsBefore + 1,
                "missing telemetry should trigger bounded compatibility reassert after repeated maintenance cycles");

            controller.LastMaxModeExternalResetUtc.Should().NotBeNull();
            controller.LastMaxModeExternalResetDetails.Should().Contain("telemetry unavailable");
        }

        [Fact]
        public void CountdownExtensionCallback_ManualMode_ThrottlesRapidReapplyWrites()
        {
            var fake = new ManualMaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetFanSpeed(42).Should().BeTrue();
            controller.StopCountdownExtension();
            fake.SetFanLevelCalls = 0;
            fake.ExtendCountdownCalls = 0;

            SetPrivateField(controller, "_lastManualModeReapplyUtc", System.DateTime.MinValue);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanLevelCalls.Should().Be(1);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanLevelCalls.Should().Be(1,
                "rapid keepalive ticks should not keep re-writing the same manual level every interval");
            fake.ExtendCountdownCalls.Should().BeGreaterThan(0);
        }

        [Fact]
        public void CountdownExtensionCallback_PresetMode_ThrottlesRapidReapplyWrites()
        {
            var fake = new PresetMaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetPerformanceMode("Performance").Should().BeTrue();
            controller.StopCountdownExtension();
            fake.SetFanModeCalls = 0;
            fake.ExtendCountdownCalls = 0;

            SetPrivateField(controller, "_lastPresetModeReapplyUtc", System.DateTime.MinValue);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanModeCalls.Should().Be(1);

            InvokeCountdownExtensionCallback(controller);
            fake.SetFanModeCalls.Should().Be(1,
                "preset keepalive should not spam SetFanMode on every countdown tick");
            fake.ExtendCountdownCalls.Should().BeGreaterThan(0);
        }

        [Fact]
        public void CountdownExtensionCallback_PresetMode_YieldsWhileDiagnosticModeActive()
        {
            var fake = new PresetMaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetPerformanceMode("Performance").Should().BeTrue();
            controller.StopCountdownExtension();
            fake.SetFanModeCalls = 0;
            fake.ExtendCountdownCalls = 0;
            SetPrivateField(controller, "_lastPresetModeReapplyUtc", System.DateTime.MinValue);

            SetPrivateStaticField(typeof(FanService), "_globalDiagnosticModeCount", 1);
            try
            {
                InvokeCountdownExtensionCallback(controller, resetDiagnosticState: false);
            }
            finally
            {
                SetPrivateStaticField(typeof(FanService), "_globalDiagnosticModeCount", 0);
            }

            fake.SetFanModeCalls.Should().Be(0,
                "diagnostic sessions should temporarily own the fan state without preset keepalive reassertion");
            fake.ExtendCountdownCalls.Should().BeGreaterThan(0);
        }

        [Fact]
        public void CountdownExtensionCallback_PerformanceMode_DoesNotReuseStaleManualFanLevel()
        {
            var fake = new PresetMaintenanceFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetFanSpeed(42).Should().BeTrue();
            controller.SetPerformanceMode("Performance").Should().BeTrue();
            controller.StopCountdownExtension();

            fake.SetFanLevelCalls = 0;
            fake.SetFanModeCalls = 0;
            SetPrivateField(controller, "_lastPresetModeReapplyUtc", System.DateTime.MinValue);

            InvokeCountdownExtensionCallback(controller);

            fake.SetFanModeCalls.Should().Be(1,
                "performance keepalive should reassert fan mode policy");
            fake.SetFanLevelCalls.Should().Be(0,
                "stale manual percentage must not force manual SetFanLevel reapply after switching to performance mode");
        }

        [Fact]
        public void RestoreAutoControl_V1_DoesNotWriteZeroFanLevel()
        {
            var fake = new V1AutoHandoffFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetMaxFanSpeed(true).Should().BeTrue();
            controller.RestoreAutoControl().Should().BeTrue();

            fake.SetFanLevelCalls.Should().NotContain(call => call.fan1 == 0 && call.fan2 == 0,
                "V1 auto handoff must not force a transient 0 RPM dip");
        }

        [Fact]
        public void ApplyPreset_Auto_V1_DoesNotWriteZeroFanLevel()
        {
            var fake = new V1AutoHandoffFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.ApplyPreset(new FanPreset { Name = "Auto", Mode = OmenCore.Models.FanMode.Auto }).Should().BeTrue();

            fake.SetFanLevelCalls.Should().NotContain(call => call.fan1 == 0 && call.fan2 == 0,
                "Auto preset should rely on SetFanMode(Default), not an explicit SetFanLevel(0,0)");
        }

        [Fact]
        public void ResetEcToDefaults_V1_DoesNotWriteZeroFanLevel()
        {
            var fake = new V1AutoHandoffFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.ResetEcToDefaults().Should().BeTrue();

            fake.SetFanLevelCalls.Should().Contain(call => call.fan1 == 20 && call.fan2 == 20,
                "V1 EC reset should use a nonzero transition hint while returning control to BIOS");
            fake.SetFanLevelCalls.Should().NotContain(call => call.fan1 == 0 && call.fan2 == 0,
                "EC reset must not reintroduce the transient 0 RPM handoff risk");
        }

        [Fact]
        public void ApplyPreset_CurvePayload_PreservesCurrentPerformancePolicy()
        {
            var fake = new ModeCaptureFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetPerformanceMode("Performance").Should().BeTrue();
            fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Performance);

            var preset = new FanPreset
            {
                Name = "Quiet",
                Mode = OmenCore.Models.FanMode.Manual,
                Curve = new List<FanCurvePoint>
                {
                    new() { TemperatureC = 40, FanPercent = 35 },
                    new() { TemperatureC = 70, FanPercent = 70 }
                }
            };

            controller.ApplyPreset(preset).Should().BeTrue();
            fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Performance,
                "curve-based fan presets should not change the active thermal policy mode");
        }

        [Fact]
        public void ApplyPreset_CurvePayload_DoesNotForcePerformanceAliasMode()
        {
            var fake = new ModeCaptureFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.SetPerformanceMode("Default").Should().BeTrue();
            fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Default);

            var preset = new FanPreset
            {
                Name = "Extreme",
                Mode = OmenCore.Models.FanMode.Performance,
                Curve = new List<FanCurvePoint>
                {
                    new() { TemperatureC = 40, FanPercent = 45 },
                    new() { TemperatureC = 70, FanPercent = 100 }
                }
            };

            controller.ApplyPreset(preset).Should().BeTrue();
            fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Default,
                "curve presets should preserve current policy even when preset metadata uses Performance");
        }

        [Fact]
        public void ApplyPreset_AutoCurvePayload_PreservesCurrentPolicyMode()
        {
            var fake = new ModeCaptureFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            try
            {
                controller.SetPerformanceMode("Performance").Should().BeTrue();
                fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Performance);

                var preset = new FanPreset
                {
                    Name = "Auto",
                    Mode = OmenCore.Models.FanMode.Auto,
                    Curve = new List<FanCurvePoint>
                    {
                        new() { TemperatureC = 40, FanPercent = 35 },
                        new() { TemperatureC = 80, FanPercent = 80 }
                    }
                };

                controller.ApplyPreset(preset).Should().BeTrue();
                fake.LastSetFanMode.Should().Be(HpWmiBios.FanMode.Performance,
                    "Auto presets with explicit curve payload should preserve active thermal policy mode");
                controller.CountdownExtensionEnabled.Should().BeTrue(
                    "preserved non-default thermal policies still need WMI hold maintenance even when the preset label is Auto");
            }
            finally
            {
                controller.StopCountdownExtension();
            }
        }

        [Theory]
        [InlineData(HpWmiBios.GpuPowerLevel.Minimum, 0, 0)]
        [InlineData(HpWmiBios.GpuPowerLevel.Medium, 1, 0)]
        [InlineData(HpWmiBios.GpuPowerLevel.Maximum, 1, 1)]
        [InlineData(HpWmiBios.GpuPowerLevel.Extended3, 1, 2)]
        public void BuildGpuPowerPayload_UsesDistinctTgpAndPpabBytes(
            HpWmiBios.GpuPowerLevel level,
            byte expectedCustomTgp,
            byte expectedPpab)
        {
            var payload = HpWmiBios.BuildGpuPowerPayload(level);

            payload.customTgp.Should().Be(expectedCustomTgp);
            payload.ppab.Should().Be(expectedPpab);
        }

        [Theory]
        [InlineData(HpWmiBios.GpuPowerLevel.Minimum, false, 0, true)]
        [InlineData(HpWmiBios.GpuPowerLevel.Medium, true, 0, true)]
        [InlineData(HpWmiBios.GpuPowerLevel.Maximum, true, 1, true)]
        [InlineData(HpWmiBios.GpuPowerLevel.Extended3, true, 2, true)]
        [InlineData(HpWmiBios.GpuPowerLevel.Maximum, true, 0, false)]
        [InlineData(HpWmiBios.GpuPowerLevel.Extended3, true, 1, false)]
        public void IsGpuPowerReadbackMatch_DetectsIgnoredOrDowngradedWrites(
            HpWmiBios.GpuPowerLevel level,
            bool customTgp,
            int ppabLevel,
            bool expected)
        {
            HpWmiBios.IsGpuPowerReadbackMatch(level, customTgp, ppabLevel)
                .Should().Be(expected);
        }

        [Theory]
        [InlineData(50, 55, 27)]
        [InlineData(100, 55, 100)]
        [InlineData(100, 63, 100)]
        public void MapFanPercentToWmiLevel_UsesProtocolCeilingForFullSpeed(int percent, int maxFanLevel, int expectedLevel)
        {
            WmiFanController.MapFanPercentToWmiLevel(percent, maxFanLevel)
                .Should().Be((byte)expectedLevel);
        }

        [Fact]
        public void ApplyCustomCurve_FullSpeed_UsesProtocolCeilingInsteadOfClassicMaxLevel()
        {
            var fake = new V1AutoHandoffFakeWmiBios();
            var controller = new WmiFanController(null, null, 0, injectedWmiBios: fake);

            controller.ApplyCustomCurve(new[]
            {
                new FanCurvePoint { TemperatureC = 30, FanPercent = 100 }
            }).Should().BeTrue();

            fake.SetFanLevelCalls.Should().Contain(call => call.fan1 == 100 && call.fan2 == 100,
                "custom curve 100% should use the same protocol ceiling as Max mode so BIOS can clamp to the real hardware maximum");
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

        private class ModeCaptureFakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled;

            public HpWmiBios.FanMode LastSetFanMode { get; private set; } = HpWmiBios.FanMode.Default;

            public bool IsAvailable => true;
            public string Status => "ModeCaptureFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => _maxEnabled ? (4200, 4100) : (900, 850);
            public (byte fan1, byte fan2)? GetFanLevel() => _maxEnabled ? ((byte)100, (byte)100) : ((byte)35, (byte)35);

            public bool SetFanMax(bool enabled)
            {
                _maxEnabled = enabled;
                return true;
            }

            public bool SetFanLevel(byte fan1, byte fan2) => true;

            public bool SetFanMode(HpWmiBios.FanMode mode)
            {
                LastSetFanMode = mode;
                _maxEnabled = false;
                return true;
            }

            public double? GetTemperature() => 50.0;
            public double? GetGpuTemperature() => 52.0;
            public void ExtendFanCountdown() { }
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private class V1AutoHandoffFakeWmiBios : IHpWmiBios
        {
            public List<(byte fan1, byte fan2)> SetFanLevelCalls { get; } = new();
            private bool _maxEnabled;

            public bool IsAvailable => true;
            public string Status => "V1AutoHandoffFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V1;
            public int FanCount => 2;
            public int MaxFanLevel => 55;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => _maxEnabled ? (4200, 4100) : (600, 580);
            public (byte fan1, byte fan2)? GetFanLevel() => _maxEnabled ? ((byte)55, (byte)55) : ((byte)10, (byte)10);
            public bool SetFanMax(bool enabled) { _maxEnabled = enabled; return true; }
            public bool SetFanLevel(byte fan1, byte fan2) { SetFanLevelCalls.Add((fan1, fan2)); return true; }
            public bool SetFanMode(HpWmiBios.FanMode mode) { _maxEnabled = false; return true; }

            public double? GetTemperature() => 45.0;
            public double? GetGpuTemperature() => 45.0;
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

        private class TelemetryUnavailableMaintenanceFakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled;
            public int SetFanMaxTrueCalls { get; private set; }

            public bool IsAvailable => true;
            public string Status => "TelemetryUnavailableMaintenanceFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => null;
            public (byte fan1, byte fan2)? GetFanLevel() => null;

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
            public double? GetGpuTemperature() => 45.0;
            public void ExtendFanCountdown() { }
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private class ManualMaintenanceFakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled;
            public int SetFanLevelCalls { get; set; }
            public int ExtendCountdownCalls { get; set; }

            public bool IsAvailable => true;
            public string Status => "ManualMaintenanceFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => _maxEnabled ? (4200, 4100) : (1900, 1800);
            public (byte fan1, byte fan2)? GetFanLevel() => _maxEnabled ? ((byte)100, (byte)100) : ((byte)42, (byte)42);

            public bool SetFanMax(bool enabled)
            {
                _maxEnabled = enabled;
                return true;
            }

            public bool SetFanLevel(byte fan1, byte fan2)
            {
                SetFanLevelCalls++;
                return true;
            }

            public bool SetFanMode(HpWmiBios.FanMode mode) => true;
            public double? GetTemperature() => 55.0;
            public double? GetGpuTemperature() => 52.0;
            public void ExtendFanCountdown() => ExtendCountdownCalls++;
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private class PresetMaintenanceFakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled;
            public int SetFanLevelCalls { get; set; }
            public int SetFanModeCalls { get; set; }
            public int ExtendCountdownCalls { get; set; }

            public bool IsAvailable => true;
            public string Status => "PresetMaintenanceFake";
            public HpWmiBios.ThermalPolicyVersion ThermalPolicy => HpWmiBios.ThermalPolicyVersion.V2;
            public int FanCount => 2;
            public int MaxFanLevel => 100;

            public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect() => _maxEnabled ? (4300, 4200) : (1800, 1700);
            public (byte fan1, byte fan2)? GetFanLevel() => _maxEnabled ? ((byte)100, (byte)100) : ((byte)40, (byte)40);

            public bool SetFanMax(bool enabled)
            {
                _maxEnabled = enabled;
                return true;
            }

            public bool SetFanLevel(byte fan1, byte fan2)
            {
                SetFanLevelCalls++;
                return true;
            }

            public bool SetFanMode(HpWmiBios.FanMode mode)
            {
                SetFanModeCalls++;
                _maxEnabled = false;
                return true;
            }

            public double? GetTemperature() => 52.0;
            public double? GetGpuTemperature() => 50.0;
            public void ExtendFanCountdown() => ExtendCountdownCalls++;
            public (bool customTgp, bool ppab, int dState)? GetGpuPower() => null;
            public bool SetGpuPower(HpWmiBios.GpuPowerLevel level) => true;
            public HpWmiBios.GpuMode? GetGpuMode() => null;
            public void Dispose() { }
        }

        private static void InvokeCountdownExtensionCallback(WmiFanController controller, bool resetDiagnosticState = true)
        {
            if (resetDiagnosticState)
            {
                SetPrivateStaticField(typeof(FanService), "_globalDiagnosticModeCount", 0);
            }

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

        private static void SetPrivateStaticField(System.Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            field.Should().NotBeNull();
            field!.SetValue(null, value);
        }
    }
}
