using System;
using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class FanControllerEcWatchdogTests
    {
        private class FakeEcAccess : IEcAccess
        {
            public bool FailWrites { get; set; } = true;
            public int WriteCount { get; private set; } = 0;
            public bool Initialize(string devicePath) => true;
            public bool IsAvailable => true;
            public byte ReadByte(ushort address) => 0x10;
            public void WriteByte(ushort address, byte value)
            {
                WriteCount++;
                if (FailWrites)
                    throw new TimeoutException("EC write timeout (simulated)");
            }
            public void Dispose() { }
        }

        [Fact]
        public void EcWriteWatchdog_Disables_EC_writes_after_consecutive_failures()
        {
            var fakeEc = new FakeEcAccess { FailWrites = true };
            var regs = new Dictionary<string, int>();

            // Use short cooldown for test reliability
            var controller = new FanController(fakeEc, regs, null, null, null, ecWriteDisableCooldownSeconds: 1);

            controller.IsEcReady.Should().BeTrue();

            // Trigger consecutive failing writes (use different percents to avoid deduplication)
            controller.SetImmediatePercent(30); // fail #1
            controller.SetImmediatePercent(40); // fail #2
            controller.SetImmediatePercent(50); // fail #3 => watchdog should engage

            Console.WriteLine($"DEBUG: fakeEc.WriteCount={fakeEc.WriteCount}, EcWriteFailureCount={controller.EcWriteFailureCount}");
            fakeEc.WriteCount.Should().BeGreaterThan(0);
            controller.EcWriteFailureCount.Should().BeGreaterThan(0);
            controller.EcWritesTemporarilyDisabled.Should().BeTrue();
            controller.IsEcReady.Should().BeFalse();

            // Further attempts must be skipped (EC watchdog protects against further writes)
            var failureBefore = controller.EcWriteFailureCount;
            controller.SetImmediatePercent(60);
            controller.EcWriteFailureCount.Should().Be(failureBefore);

            // Allow cooldown to expire and make writes succeed again
            fakeEc.FailWrites = false;
            System.Threading.Thread.Sleep(1100);

            controller.SetImmediatePercent(70);
            controller.EcWritesTemporarilyDisabled.Should().BeFalse();
            controller.EcWriteFailureCount.Should().Be(0);
        }
    }
}
