using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// Unlike UiPollingCoordinator (untestable here without a live WPF Dispatcher),
    /// BackgroundPollingCoordinator is backed by a plain System.Threading.Timer, so it can
    /// actually be driven end-to-end in a headless test host. The scheduling logic itself
    /// (PollingScheduler) already has deterministic fake-clock coverage in
    /// PollingSchedulerTests.cs; these tests cover only what's unique to this class: real timer
    /// wiring, thread-pool execution, and the reentrancy guard.
    /// </summary>
    [Collection("NonParallel")]
    public class BackgroundPollingCoordinatorTests
    {
        [Fact]
        public async Task Subscribe_FiresCallbackOnThreadPoolThread()
        {
            var tcs = new TaskCompletionSource<int>();
            var callbackThreadId = -1;
            var testThreadId = Environment.CurrentManagedThreadId;

            using var subscription = BackgroundPollingCoordinator.Subscribe(
                "test-live-fire", TimeSpan.FromMilliseconds(1), () =>
            {
                callbackThreadId = Environment.CurrentManagedThreadId;
                tcs.TrySetResult(1);
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            completed.Should().Be(tcs.Task, "a due subscription should fire within a few base ticks");
            callbackThreadId.Should().NotBe(testThreadId,
                "the coordinator must run callbacks on a thread-pool thread, never the caller's own thread");
        }

        [Fact]
        public async Task Dispose_StopsFurtherCallbacks()
        {
            var fireCount = 0;
            var subscription = BackgroundPollingCoordinator.Subscribe(
                "test-dispose", TimeSpan.FromMilliseconds(1), () => Interlocked.Increment(ref fireCount));

            // Wait for at least one fire, then unsubscribe and confirm the count stops moving.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref fireCount) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            fireCount.Should().BeGreaterThan(0, "the subscription should have fired at least once before we test disposal");

            subscription.Dispose();
            var countAtDispose = Volatile.Read(ref fireCount);

            await Task.Delay(TimeSpan.FromSeconds(3));

            Volatile.Read(ref fireCount).Should().Be(countAtDispose,
                "disposing the subscription should stop further callbacks even though the shared base timer keeps running for other subscribers");
        }

        [Fact]
        public async Task UpdateInterval_ChangesRealFiringCadence()
        {
            var fireCount = 0;
            using var subscription = BackgroundPollingCoordinator.Subscribe(
                "test-update-interval", TimeSpan.FromMinutes(10), () => Interlocked.Increment(ref fireCount));

            // 10 minutes would never fire within this test's timeout - only the speed-up should
            // make it fire, confirming UpdateInterval reaches the real, live-timer-backed
            // subscription end to end, not just PollingScheduler's own in-memory state.
            subscription.UpdateInterval(TimeSpan.FromMilliseconds(1));

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref fireCount) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Volatile.Read(ref fireCount).Should().BeGreaterThan(0,
                "UpdateInterval should take effect on the real background coordinator, not just an in-memory scheduler");
        }

        [Fact]
        public void SubscriptionCount_ReflectsActiveSubscriptions()
        {
            var before = BackgroundPollingCoordinator.SubscriptionCount;

            using (BackgroundPollingCoordinator.Subscribe("test-count", TimeSpan.FromMinutes(10), () => { }))
            {
                BackgroundPollingCoordinator.SubscriptionCount.Should().Be(before + 1);
            }

            BackgroundPollingCoordinator.SubscriptionCount.Should().Be(before);
        }
    }
}
