using System;
using System.Runtime.CompilerServices;

namespace OmenCoreApp.Tests
{
    /// <summary>
    /// Runs once, before any test in this assembly, to switch off the side effects the suite would
    /// otherwise have on the machine running it.
    ///
    /// All three are opt-outs the app already understands, so nothing test-specific leaks into
    /// production code paths - and setting them here means the thirteen test files that construct a
    /// NotificationService, and the several that construct a LoggingService, do not each have to
    /// remember. Individual tests may still set these themselves; doing so is idempotent.
    /// </summary>
    internal static class TestEnvironment
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            // Real Windows toasts. ThermalProtectionTests drives the thermal check with a mock 95 C
            // against a real NotificationService, so without this the suite raises genuine emergency
            // over-temperature notifications about a temperature no hardware ever reported.
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_NOTIFICATIONS", "1");

            // Log file contention: helper processes running concurrently can hold the log file open,
            // which surfaces as an IOException mid-run. CI sets this for the same reason.
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG", "1");

            // A real hardware worker process. WmiBiosMonitor prelaunches OmenCore.HardwareWorker.exe,
            // and from a test host that resolves to the one in the developer's own build output. It
            // polls the GPU through NVML on its own timer, it is not a child of any individual test,
            // and its orphan timeout is measured in minutes - so it outlives the test host that
            // caused it and goes on touching the hardware of the machine that ran the suite. That is
            // a real instrument left running: it holds a dGPU out of RTD3, and it competes for the
            // NVML in-flight slots that GpuRestartGate hands out.
            //
            // Anything that genuinely needs a worker starts one deliberately. Nothing should get one
            // as a side effect of constructing a monitor.
            Environment.SetEnvironmentVariable("OMENCORE_DISABLE_LHM", "1");
        }
    }
}
