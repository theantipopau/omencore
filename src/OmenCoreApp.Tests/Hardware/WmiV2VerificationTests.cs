using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class WmiV2VerificationTests
    {
        private class FakeWmiBios : IHpWmiBios
        {
            private bool _maxEnabled = false;
            public bool IsAvailable => true;
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
            details.Should().Contain("No RPMs available");
        }
    }
}
