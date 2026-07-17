using System;
using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// Coverage for the pure scheduling logic behind UiPollingCoordinator — the shared
    /// polling coordinator introduced to start consolidating the independently-timed
    /// DispatcherTimers audited in ROADMAP_v4.0.0.md (Tray/OSD/Quick Popup cluster).
    /// Uses a fake clock so timing behavior is deterministic instead of relying on
    /// real elapsed wall-clock time.
    /// </summary>
    public class PollingSchedulerTests
    {
        private sealed class FakeClock
        {
            public DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public DateTime Get() => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        [Fact]
        public void Subscribe_DoesNotFireImmediately()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var fireCount = 0;

            scheduler.Subscribe("test", TimeSpan.FromSeconds(2), () => fireCount++);
            scheduler.Pump();

            fireCount.Should().Be(0, "a fresh subscription's first fire should be at least one interval away, not on the same tick it was created");
        }

        [Fact]
        public void Subscribe_FiresOnceIntervalElapses()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var fireCount = 0;

            scheduler.Subscribe("test", TimeSpan.FromSeconds(2), () => fireCount++);

            clock.Advance(TimeSpan.FromSeconds(2));
            scheduler.Pump();

            fireCount.Should().Be(1);
        }

        [Fact]
        public void Pump_ReschedulesRelativeToFireTime_NotDrifting()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var fireTimes = new List<DateTime>();

            scheduler.Subscribe("test", TimeSpan.FromSeconds(2), () => fireTimes.Add(clock.Now));

            // Simulate a base tick every 500ms, matching UiPollingCoordinator's real cadence.
            for (int i = 0; i < 20; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                scheduler.Pump();
            }

            // Over 10 seconds of 500ms base ticks, a 2s-interval subscriber should fire 5 times
            // (at 2s, 4s, 6s, 8s, 10s) with no drift or missed fires.
            fireTimes.Should().HaveCount(5);
            fireTimes[0].Should().Be(new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc));
            fireTimes[4].Should().Be(new DateTime(2026, 1, 1, 0, 0, 10, DateTimeKind.Utc));
        }

        [Fact]
        public void MultipleSubscribers_WithDifferentCadences_FireIndependently()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var fastCount = 0;
            var slowCount = 0;

            scheduler.Subscribe("fast", TimeSpan.FromSeconds(1), () => fastCount++);
            scheduler.Subscribe("slow", TimeSpan.FromSeconds(5), () => slowCount++);

            for (int i = 0; i < 20; i++) // 10 seconds at 500ms base ticks
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                scheduler.Pump();
            }

            fastCount.Should().Be(10); // every 1s over 10s
            slowCount.Should().Be(2);  // every 5s over 10s
        }

        [Fact]
        public void Dispose_UnsubscribesAndStopsFiring()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var fireCount = 0;

            var subscription = scheduler.Subscribe("test", TimeSpan.FromSeconds(1), () => fireCount++);

            clock.Advance(TimeSpan.FromSeconds(1));
            scheduler.Pump();
            fireCount.Should().Be(1);

            subscription.Dispose();

            clock.Advance(TimeSpan.FromSeconds(5));
            scheduler.Pump();
            fireCount.Should().Be(1, "disposing the subscription should stop further callbacks");
            scheduler.SubscriptionCount.Should().Be(0);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var scheduler = new PollingScheduler();
            var subscription = scheduler.Subscribe("test", TimeSpan.FromSeconds(1), () => { });

            subscription.Dispose();
            var act = () => subscription.Dispose();

            act.Should().NotThrow();
        }

        [Fact]
        public void FaultingSubscriber_DoesNotPreventOthersFromFiring()
        {
            var clock = new FakeClock();
            var scheduler = new PollingScheduler(clock.Get);
            var goodFired = false;
            var reportedErrors = new List<string>();

            scheduler.Subscribe("bad", TimeSpan.FromSeconds(1), () => throw new InvalidOperationException("boom"));
            scheduler.Subscribe("good", TimeSpan.FromSeconds(1), () => goodFired = true);

            clock.Advance(TimeSpan.FromSeconds(1));
            var act = () => scheduler.Pump((name, ex) => reportedErrors.Add(name));

            act.Should().NotThrow("one subscriber throwing must not take down the coordinator or the other subscribers");
            goodFired.Should().BeTrue();
            reportedErrors.Should().ContainSingle().Which.Should().Be("bad");
        }

        [Fact]
        public void Subscribe_RejectsInvalidArguments()
        {
            var scheduler = new PollingScheduler();

            scheduler.Invoking(s => s.Subscribe("", TimeSpan.FromSeconds(1), () => { }))
                .Should().Throw<ArgumentException>();

            scheduler.Invoking(s => s.Subscribe("name", TimeSpan.Zero, () => { }))
                .Should().Throw<ArgumentOutOfRangeException>();

            scheduler.Invoking(s => s.Subscribe("name", TimeSpan.FromSeconds(1), null!))
                .Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Pump_WithNoSubscribers_DoesNothing()
        {
            var scheduler = new PollingScheduler();
            var act = () => scheduler.Pump();

            act.Should().NotThrow();
        }
    }
}
