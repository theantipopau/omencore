using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

/// <summary>
/// GitHub #186: OmenCore's Linux GPU telemetry never queried NVML at all, so a real NVIDIA laptop
/// GPU with no hwmon exposure (common with the proprietary driver) reported 0C/unavailable even
/// though nvidia-smi read it correctly. NvmlInterop wraps libnvidia-ml.so.1 via P/Invoke.
///
/// This test host has no NVIDIA driver (it isn't Linux, and even a Linux CI runner is unlikely to
/// have one) - libnvidia-ml.so.1 genuinely won't load here, which is exactly the "gracefully
/// degrade" path this class needs to get right. That's the one thing these tests can verify
/// without real hardware: NVML absence must fail closed (null, not a thrown exception) with a
/// reason string a diagnose command can show, never crash the caller.
///
/// NvmlInterop is a static class with static once-per-process init-attempt caching, so these tests
/// share that state across the whole test run in this process - by design (see the class's own
/// "only try once" comment), and irrelevant to what's actually being asserted here (the same
/// "unavailable, with a reason" outcome holds whichever test happens to run first).
/// </summary>
public class NvmlInteropTests
{
    [Fact]
    public void TryGetPrimaryGpu_ReturnsNull_WhenNvmlLibraryIsUnavailable()
    {
        var snapshot = NvmlInterop.TryGetPrimaryGpu();

        snapshot.Should().BeNull("this test host has no NVIDIA driver, so libnvidia-ml.so.1 cannot load");
    }

    [Fact]
    public void LastFailureReason_IsPopulatedAndDescriptive_AfterFailedAttempt()
    {
        NvmlInterop.TryGetPrimaryGpu();

        NvmlInterop.LastFailureReason.Should().NotBeNullOrWhiteSpace(
            "a failed NVML attempt should leave a diagnosable reason behind, not just a silent null - " +
            "this is what GitHub #186 asked diagnose output to surface");
    }

    [Fact]
    public void TryGetPrimaryGpu_RepeatedCalls_DoNotThrow()
    {
        // Exercises the "only attempt init once per process" cache path on the second+ call.
        var first = NvmlInterop.TryGetPrimaryGpu();
        var second = NvmlInterop.TryGetPrimaryGpu();
        var third = NvmlInterop.TryGetPrimaryGpu();

        first.Should().BeNull();
        second.Should().BeNull();
        third.Should().BeNull();
    }
}
