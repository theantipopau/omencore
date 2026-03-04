using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class FanControllerEcRateTests
    {
        /// <summary>
        /// Fake EC provider that fails writes if more than a configured number
        /// occur within a sliding time window. This simulates a hardware rate limit
        /// where the EC rejects too-frequent access.
        /// </summary>
        private class RateLimitedEc : IEcAccess
        {
            private readonly Queue<DateTime> _timestamps = new();
            public int MaxWritesPerWindow { get; set; } = 10;
            public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

            public bool Initialize(string devicePath) => true;
            public bool IsAvailable => true;
            public byte ReadByte(ushort address) => 0x00;

            public int WriteCount { get; private set; }

            public void WriteByte(ushort address, byte value)
            {
                var now = DateTime.UtcNow;
                _timestamps.Enqueue(now);
                WriteCount++;

                // drop old timestamps
                while (_timestamps.Count > 0 && now - _timestamps.Peek() > Window)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count > MaxWritesPerWindow)
                {
                    throw new TimeoutException("EC write rate limit exceeded (simulated)");
                }
            }

            public void Dispose() { }
        }

        [Fact]
        public void EcWriteRateSimulator_TriggersWatchdogUnderStress()
        {
            var fakeEc = new RateLimitedEc { MaxWritesPerWindow = 5, Window = TimeSpan.FromMilliseconds(100) };
            var regs = new Dictionary<string, int>();

            var controller = new FanController(fakeEc, regs, null, null, null, ecWriteDisableCooldownSeconds: 1);
            controller.IsEcReady.Should().BeTrue();

            // hammer the controller with rapid writes
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    controller.SetImmediatePercent(i);
                }
                catch
                {
                    // ignore exceptions - watchdog will eventually disable writes
                }
            }

            controller.EcWriteFailureCount.Should().BeGreaterThan(0);
            controller.EcWritesTemporarilyDisabled.Should().BeTrue();

            // allow cooldown
            Thread.Sleep(1100);
            // remove rate limit
            fakeEc.MaxWritesPerWindow = int.MaxValue;

            controller.SetImmediatePercent(42);
            controller.EcWritesTemporarilyDisabled.Should().BeFalse();
            controller.EcWriteFailureCount.Should().Be(0);
        }
    }
}