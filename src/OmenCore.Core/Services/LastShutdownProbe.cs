using System;
using System.Diagnostics.Eventing.Reader;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Asks Windows how the previous session ended.
    ///
    /// Reads the OS's own record rather than keeping a dirty flag of our own. A flag written at
    /// shutdown and cleared at startup cannot tell a bugcheck from OmenCore being killed in Task
    /// Manager, or from an update replacing it mid-run - and treating those as crashes would revoke
    /// an overclock's startup authorization every time the user closed the app the wrong way.
    /// Kernel-Boot 20 carries exactly the distinction that matters: whether the machine went down
    /// without shutting down.
    /// </summary>
    public sealed class LastShutdownProbe
    {
        private readonly LoggingService? _logging;

        // Kernel-Boot 20: "The last shutdown's success status was <bool>. The last boot's success
        // status was <bool>." Property 0 is the shutdown status, which is false after a bugcheck or
        // a hard power loss and true after a normal shutdown.
        private const int KernelBootLastShutdownStatusEventId = 20;
        private const string KernelBootProvider = "Microsoft-Windows-Kernel-Boot";

        public LastShutdownProbe(LoggingService? logging = null) => _logging = logging;

        /// <summary>
        /// How the last session ended, or <see cref="LastShutdownState.Unknown"/> when the log could
        /// not be read or carried no such record. Unknown is not a failure to report loudly: a
        /// machine with a trimmed or unreadable System log is an ordinary machine.
        /// </summary>
        public LastShutdownState Read()
        {
            try
            {
                var query = new EventLogQuery(
                    "System",
                    PathType.LogName,
                    $"*[System[Provider[@Name='{KernelBootProvider}'] and EventID={KernelBootLastShutdownStatusEventId}]]")
                {
                    ReverseDirection = true // newest first; we want the boot we are in
                };

                using var reader = new EventLogReader(query);
                using var record = reader.ReadEvent();

                if (record?.Properties is not { Count: > 0 } properties)
                {
                    _logging?.Info("Last-shutdown state: no Kernel-Boot 20 record found; treating as unknown");
                    return LastShutdownState.Unknown;
                }

                if (properties[0].Value is not bool cleanShutdown)
                {
                    _logging?.Info("Last-shutdown state: Kernel-Boot 20 carried no boolean status; treating as unknown");
                    return LastShutdownState.Unknown;
                }

                var state = cleanShutdown ? LastShutdownState.Clean : LastShutdownState.Unclean;
                _logging?.Info($"Last-shutdown state: {state} (Kernel-Boot 20 reported success={cleanShutdown})");
                return state;
            }
            catch (Exception ex)
            {
                // Reading the System log can fail on a locked-down machine, and that is not a reason
                // to withhold a setting the user confirmed.
                _logging?.Info($"Last-shutdown state could not be read ({ex.GetType().Name}: {ex.Message}); treating as unknown");
                return LastShutdownState.Unknown;
            }
        }
    }
}
