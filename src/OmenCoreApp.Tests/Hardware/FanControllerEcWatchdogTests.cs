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

    /// <summary>
    /// Validates STEP-02: AbandonedMutexException from EC reads is treated as contention
    /// (same path as TimeoutException), not rethrown and not logged as a generic read failure.
    /// Regression guard: this test would have failed before STEP-02 on non-English Windows
    /// because the old ex.Message.Contains("mutex") check depended on English exception text.
    /// </summary>
    public class FanControllerEcContentionClassificationTests
    {
        private class AbandonedMutexEcAccess : IEcAccess
        {
            public bool Initialize(string devicePath) => true;
            public bool IsAvailable => true;
            public byte ReadByte(ushort address) =>
                throw new AbandonedMutexException("EC mutex abandoned by previous holder (simulated)");
            public void WriteByte(ushort address, byte value) { }
            public void Dispose() { }
        }

        [Fact]
        public void ReadActualFanRpm_AbandonedMutexException_DoesNotPropagate()
        {
            // AbandonedMutexException must be caught, not rethrown.
            PawnIOEcAccess.EcContentionWarningLogged = false;
            var controller = new FanController(new AbandonedMutexEcAccess(), new Dictionary<string, int>(), null);

            var act = () => controller.ReadActualFanRpmPublic();

            act.Should().NotThrow("AbandonedMutexException from an EC read must be handled as contention, not propagated");
            PawnIOEcAccess.EcContentionWarningLogged = false; // cleanup shared state
        }

        [Fact]
        public void ReadActualFanRpm_AbandonedMutexException_SetsContentionFlag()
        {
            // When AbandonedMutexException occurs, the EC contention warning should be logged
            // (EcContentionWarningLogged = true), not the generic read-failure path.
            PawnIOEcAccess.EcContentionWarningLogged = false;
            var controller = new FanController(new AbandonedMutexEcAccess(), new Dictionary<string, int>(), null);

            var (fan1, fan2) = controller.ReadActualFanRpmPublic();

            fan1.Should().Be(0);
            fan2.Should().Be(0);
            PawnIOEcAccess.EcContentionWarningLogged.Should().BeTrue(
                "AbandonedMutexException must be classified as EC contention and set the contention-warning flag");
            PawnIOEcAccess.EcContentionWarningLogged = false; // cleanup shared state
        }
    }
}
