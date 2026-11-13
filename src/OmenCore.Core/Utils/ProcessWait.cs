using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OmenCore.Utils
{
    /// <summary>
    /// Bounded waits for child processes whose output is captured.
    ///
    /// Every timeout in the app used to be decorative: the code called <c>ReadToEnd()</c> and then
    /// <c>WaitForExit(ms)</c>. ReadToEnd only returns once the child closes the pipe, so a wedged
    /// powershell.exe / schtasks / fsutil blocked before the wait was ever reached - the deadline
    /// could not fire, and a cleanup pass sat on the hung child forever. GitHub #245 found the same
    /// pattern on the Linux side and fixed it there by reading first and waiting second.
    ///
    /// Start the read, then wait with a bound; the read completes on its own and only the wait
    /// enforces the deadline. On timeout the process tree is killed so the caller is not left with
    /// an orphan holding the pipe.
    /// </summary>
    public static class ProcessWait
    {
        /// <summary>
        /// Waits up to <paramref name="timeoutMs"/> for the process to exit, killing its tree if it
        /// does not. Returns false on timeout - callers must not read <see cref="Process.ExitCode"/>
        /// in that case (it throws until the process has exited).
        /// </summary>
        public static bool TryWait(Process process, int timeoutMs)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            try
            {
                if (process.WaitForExit(timeoutMs))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Handle lost or the process never started: a failed wait, not an app crash.
            }

            Kill(process);
            return false;
        }

        public static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Best effort: already gone, or the handle is invalid.
            }
        }

        /// <summary>
        /// Completes a read that was started before <see cref="TryWait"/>. Returns empty text when
        /// the child was killed mid-read instead of throwing on the broken pipe.
        /// </summary>
        public static string Complete(Task<string>? read)
        {
            if (read == null) return string.Empty;

            try
            {
                return read.GetAwaiter().GetResult() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
