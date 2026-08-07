using System;
using System.Globalization;
using System.IO;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// The gate is what stands between a GPU restart and two dead processes, so the properties worth
    /// pinning are the ones whose failure is silent: an expiry that never fires would leave GPU
    /// telemetry off for the rest of the session, and a gate that fails closed on an unreadable
    /// marker would do the same.
    ///
    /// These share one real file path, so the collection is serialised.
    /// </summary>
    [Collection("GpuRestartGate")]
    public class GpuRestartGateTests : IDisposable
    {
        public GpuRestartGateTests() => Cleanup();

        public void Dispose() => Cleanup();

        private static void Cleanup()
        {
            try { File.Delete(GpuRestartGate.MarkerPath); }
            catch (IOException) { /* Nothing to clean up. */ }
            catch (UnauthorizedAccessException) { /* Nothing to clean up. */ }
            WaitOutTheCache();
        }

        // IsRestarting memoises for 150 ms so the poll loops can ask on every reading. Tests that
        // change the marker have to outlive that, or they assert against the previous answer.
        private static void WaitOutTheCache() => System.Threading.Thread.Sleep(250);

        [Fact]
        public void A_Machine_With_No_Marker_Is_Not_Restarting()
        {
            GpuRestartGate.IsRestarting.Should().BeFalse();
        }

        [Fact]
        public void An_Open_Gate_Reads_As_Restarting_And_A_Disposed_One_Does_Not()
        {
            using (GpuRestartGate.Begin(TimeSpan.FromMinutes(1)))
            {
                WaitOutTheCache();
                GpuRestartGate.IsRestarting.Should().BeTrue();
            }

            WaitOutTheCache();
            GpuRestartGate.IsRestarting.Should().BeFalse(
                because: "disposing is the normal close and must not depend on the expiry");
        }

        [Fact]
        public void An_Expired_Marker_Is_Ignored_And_Removed()
        {
            // The process holding the gate died. Telemetry has to come back on its own - the
            // alternative is GPU monitoring that is off until someone finds a file they have never
            // heard of.
            Directory.CreateDirectory(Path.GetDirectoryName(GpuRestartGate.MarkerPath)!);
            File.WriteAllText(GpuRestartGate.MarkerPath,
                DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture));

            WaitOutTheCache();

            GpuRestartGate.IsRestarting.Should().BeFalse();
            File.Exists(GpuRestartGate.MarkerPath).Should().BeFalse(
                because: "an expired marker that is left behind is re-read on every poll forever");
        }

        [Fact]
        public void A_Marker_That_Cannot_Be_Understood_Fails_Open()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GpuRestartGate.MarkerPath)!);
            File.WriteAllText(GpuRestartGate.MarkerPath, "not a timestamp");

            WaitOutTheCache();

            GpuRestartGate.IsRestarting.Should().BeFalse(
                because: "a file we cannot parse is not evidence that a GPU is being restarted");
        }

        [Fact]
        public void A_Poller_Is_Refused_Entry_While_The_Gate_Is_Open()
        {
            using (GpuRestartGate.Begin(TimeSpan.FromMinutes(1)))
            {
                WaitOutTheCache();

                GpuRestartGate.TryEnterNvml(out var refused).Should().BeFalse();
                refused.Should().BeNull(because: "a refused caller has nothing to dispose");
            }

            WaitOutTheCache();

            GpuRestartGate.TryEnterNvml(out var lease).Should().BeTrue();
            lease.Should().NotBeNull();
            lease!.Dispose();
        }

        [Fact]
        public void A_Restart_Cannot_Start_While_A_Poller_Is_Inside_Nvml()
        {
            // This is the whole point of the handshake, and the property the marker file alone could
            // not give: a poll already inside NVML when the device is pulled faults in the driver and
            // takes the process down, so the restarter has to wait for it rather than time it.
            GpuRestartGate.HandshakeAvailable.Should().BeTrue(
                because: "without it the override falls back to guessing, and this test proves nothing");

            GpuRestartGate.TryEnterNvml(out var inFlight).Should().BeTrue();

            try
            {
                GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromMilliseconds(300))
                    .Should().BeFalse(because: "one slot is held, so the drain cannot complete");
            }
            finally
            {
                inFlight!.Dispose();
            }

            GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromSeconds(5))
                .Should().BeTrue(because: "the poller left, so the device is safe to pull");
        }

        [Fact]
        public void Draining_Returns_Every_Slot_It_Took()
        {
            // A drain that kept a slot would starve the pollers one call at a time, and the symptom -
            // GPU telemetry quietly thinning out over a session - would look like anything but this.
            GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromSeconds(5)).Should().BeTrue();
            GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromSeconds(5)).Should().BeTrue();

            GpuRestartGate.TryEnterNvml(out var lease).Should().BeTrue();
            lease!.Dispose();
        }

        [Fact]
        public void A_Lease_Disposed_Twice_Returns_One_Slot()
        {
            GpuRestartGate.TryEnterNvml(out var lease).Should().BeTrue();
            lease!.Dispose();
            lease.Dispose();

            // If the second dispose had released a second count, the semaphore would now be over its
            // maximum and the next drain would succeed while a poller was still inside.
            GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromSeconds(5)).Should().BeTrue();
            GpuRestartGate.TryEnterNvml(out var again).Should().BeTrue();

            try
            {
                GpuRestartGate.WaitForPollersToLeaveNvml(TimeSpan.FromMilliseconds(300)).Should().BeFalse();
            }
            finally
            {
                again!.Dispose();
            }
        }

        [Fact]
        public void Disposing_Twice_Is_Harmless()
        {
            var gate = GpuRestartGate.Begin(TimeSpan.FromMinutes(1));
            gate.Dispose();

            var second = () => gate.Dispose();
            second.Should().NotThrow();
        }
    }
}
