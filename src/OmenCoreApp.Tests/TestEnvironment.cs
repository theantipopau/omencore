using System;
using System.Runtime.CompilerServices;

namespace OmenCoreApp.Tests
{
    /// <summary>
    /// Runs once, before any test in this assembly, to switch off the two side effects the suite
    /// would otherwise have on the machine running it.
    ///
    /// Both are opt-outs the app already understands, so nothing test-specific leaks into production
    /// code paths - and setting them here means the thirteen test files that construct a
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
        }
    }
}
