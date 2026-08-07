using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Restarts the discrete GPU so its driver reloads without the firmware's adapter verdict.
    ///
    /// On boards that clamp GPU power on an under-rated supply, the clamp is delivered to the NVIDIA
    /// driver as a one-off ACPI notification when the adapter is evaluated. Nothing lets the driver
    /// ask again: on the board this was measured on (8D87, BIOS F.07) the verdict reaches the driver
    /// only through <c>Notify (PEGP, 0xDx)</c> from the EC query handler, and appears in no _DSM the
    /// driver can poll. A driver that has just initialised therefore holds no verdict at all, and
    /// nothing clamps it until the next notification arrives.
    ///
    /// So the whole override is: restart the device. Measured on 8D87 with a 280 W supply against a
    /// 330 W requirement, the enforced limit went 35 W to 80 W and the GPU drew 79.93 W against it
    /// under load - the card sitting on the ceiling, so the figure is real rather than reported.
    /// No EC write of any kind is involved, which is what makes it implementable here: this app
    /// reaches the EC through PawnIO's port allowlist and cannot write the field the clamp lives in.
    ///
    /// THIS IS NOT PERMANENT AND IT IS NOT FREE. The firmware re-evaluates the adapter on its own
    /// schedule, and the next evaluation notifies the freshly started driver exactly as the first one
    /// did. How long that takes has not been measured. Restarting a display device also drops its
    /// outputs for a few seconds and destroys every graphics and compute context on it, which is why
    /// nothing here runs without being asked.
    /// </summary>
    public sealed class AdapterPowerOverrideService
    {
        private readonly LoggingService _logging;

        // Long enough for the driver to unload and for the PCI function to settle before it is asked
        // to come back. Shorter waits were not tested; this is not a tuned number.
        private static readonly TimeSpan SettleAfterDisable = TimeSpan.FromSeconds(3);

        // The device answers well before this on the machine it was measured on - a healthy restart
        // took about 10 s end to end. The ceiling is here so a device that never comes back is
        // reported as such rather than hanging the caller.
        private static readonly TimeSpan EnableTimeout = TimeSpan.FromSeconds(60);

        public AdapterPowerOverrideService(LoggingService logging)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>The outcome of an override attempt, in the terms the panel reports it.</summary>
        public sealed record OverrideResult(
            bool Applied,
            string Message,
            double? EnforcedWattsBefore,
            double? EnforcedWattsAfter)
        {
            /// <summary>
            /// True only when the limit was read both times and actually moved up. A restart that
            /// leaves the limit where it was is a failure worth reporting as one - on a board with no
            /// adapter clamp, or with the clamp already off, there was nothing to discard.
            /// </summary>
            public bool LimitRose => EnforcedWattsBefore is double before
                                  && EnforcedWattsAfter is double after
                                  && after > before + 0.5;
        }

        /// <summary>
        /// Whether this can be attempted at all. Deliberately does not consider the adapter verdict -
        /// that belongs to the caller, which has it already, and a service that silently refuses on a
        /// verdict it read itself is harder to explain than one that answers the question asked.
        /// </summary>
        public bool IsAvailable(out string reason)
        {
            if (!IsElevated())
            {
                reason = "Restarting a device needs OmenCore to be running as Administrator.";
                return false;
            }

            if (FindDiscreteGpu() is null)
            {
                reason = "No NVIDIA discrete GPU was found to restart.";
                return false;
            }

            if (!File.Exists(PnpUtilPath))
            {
                reason = "pnputil.exe was not found, so the device cannot be restarted.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Restart the dGPU and report what the enforced power limit did across it.
        ///
        /// Reads the limit through nvidia-smi, which wakes the GPU and holds it awake for a couple of
        /// minutes afterwards. That is acceptable here and only here: this runs on an explicit button
        /// press, never on a timer. Anything that asks a dGPU about itself on a schedule keeps it out
        /// of RTD3 and costs idle power for the whole session.
        /// </summary>
        public async Task<OverrideResult> ApplyAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAvailable(out var reason))
            {
                _logging.Warn($"Adapter power override refused: {reason}");
                return new OverrideResult(false, reason, null, null);
            }

            var device = FindDiscreteGpu();
            if (device is null)
            {
                // Re-checked because IsAvailable and this run at different moments and a device can
                // leave between them.
                return new OverrideResult(false, "No NVIDIA discrete GPU was found to restart.", null, null);
            }

            var before = ReadEnforcedPowerLimitWatts();
            _logging.Info($"Adapter power override: restarting {device.Name} ({device.InstanceId}); " +
                          $"enforced limit before = {Describe(before)}");

            try
            {
                var disable = await RunPnpUtilAsync("/disable-device", device.InstanceId, cancellationToken)
                    .ConfigureAwait(false);
                if (!disable.Success)
                {
                    _logging.Error($"Adapter power override: disable failed - {disable.Output}");
                    return new OverrideResult(false,
                        "Could not disable the GPU, so nothing was changed. " + FirstLine(disable.Output),
                        before, null);
                }

                await Task.Delay(SettleAfterDisable, cancellationToken).ConfigureAwait(false);

                // From here the device is DOWN. Every return path below must try to bring it back,
                // including the cancellation one - leaving a machine with its dGPU disabled because a
                // dialog was closed is a far worse outcome than any limit this could have won.
                var enable = await RunPnpUtilAsync("/enable-device", device.InstanceId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!enable.Success)
                {
                    _logging.Error($"Adapter power override: enable failed - {enable.Output}");
                    return new OverrideResult(false,
                        "The GPU was disabled but could not be re-enabled. Reboot to recover it. " +
                        FirstLine(enable.Output),
                        before, null);
                }

                var returned = await WaitForDeviceAsync(device.InstanceId, EnableTimeout).ConfigureAwait(false);
                if (!returned)
                {
                    return new OverrideResult(false,
                        "The GPU was re-enabled but has not reported itself as started. Give it a " +
                        "moment, or reboot if it does not come back.",
                        before, null);
                }

                var after = ReadEnforcedPowerLimitWatts();
                _logging.Info($"Adapter power override: enforced limit after = {Describe(after)}");

                return new OverrideResult(true, BuildMessage(before, after), before, after);
            }
            catch (OperationCanceledException)
            {
                // Cancellation can only have landed before the disable completed - the enable is not
                // cancellable - so the device is still up.
                return new OverrideResult(false, "Cancelled before the GPU was restarted.", before, null);
            }
            catch (Exception ex)
            {
                _logging.Error("Adapter power override failed", ex);
                return new OverrideResult(false, $"The override failed: {ex.Message}", before, null);
            }
        }

        private static string BuildMessage(double? before, double? after)
        {
            if (before is double b && after is double a)
            {
                if (a > b + 0.5)
                {
                    return $"The GPU restarted and its power limit went from {b:0.#} W to {a:0.#} W. " +
                           "This lasts until the firmware next re-evaluates the adapter, which it does " +
                           "on its own schedule - it is not a permanent change.";
                }

                return $"The GPU restarted, but its power limit is unchanged at {a:0.#} W. " +
                       "Either this board does not clamp GPU power on an under-rated supply, or the " +
                       "limit was not being clamped at the time.";
            }

            return "The GPU restarted. The power limit could not be read, so whether it changed is " +
                   "unknown - nvidia-smi was not available.";
        }

        private static string Describe(double? watts) =>
            watts is double w ? w.ToString("0.##", CultureInfo.InvariantCulture) + " W" : "unreadable";

        private static string FirstLine(string text) =>
            text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        // ── device discovery ─────────────────────────────────────────────────────────────────────

        /// <summary>A dGPU as the PnP layer sees it.</summary>
        public sealed record DiscreteGpu(string InstanceId, string Name);

        /// <summary>
        /// The NVIDIA display device, by PCI vendor ID rather than by name. Names vary by driver and
        /// by locale; the vendor ID does not.
        /// </summary>
        public DiscreteGpu? FindDiscreteGpu()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, Status FROM Win32_PnPEntity WHERE PNPClass = 'Display'");

                foreach (ManagementObject device in searcher.Get())
                {
                    var id = device["DeviceID"] as string;
                    if (string.IsNullOrEmpty(id)) continue;

                    // VEN_10DE is NVIDIA. Matching the instance path rather than the friendly name
                    // also excludes the render-only entries that carry no PCI path.
                    if (id.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    return new DiscreteGpu(id, device["Name"] as string ?? "NVIDIA GPU");
                }
            }
            catch (Exception ex)
            {
                _logging.Debug($"Discrete GPU lookup failed: {ex.Message}");
            }

            return null;
        }

        private async Task<bool> WaitForDeviceAsync(string instanceId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var escaped = instanceId.Replace("\\", "\\\\").Replace("'", "\\'");
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT Status FROM Win32_PnPEntity WHERE DeviceID = '{escaped}'");

                    foreach (ManagementObject device in searcher.Get())
                    {
                        // "OK" is what a started device reports. A device that is present but stopped
                        // reports "Error" or "Degraded", so presence alone is not the test.
                        if ((device["Status"] as string) == "OK") return true;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Debug($"Waiting for {instanceId}: {ex.Message}");
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            return false;
        }

        // ── pnputil ──────────────────────────────────────────────────────────────────────────────

        private static string PnpUtilPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");

        private sealed record ProcessResult(bool Success, string Output);

        /// <summary>
        /// pnputil rather than SetupAPI. /disable-device and /enable-device have shipped in-box since
        /// Windows 10 2004, they take the instance path directly, and the alternative is a few hundred
        /// lines of SetupDiSetClassInstallParams P/Invoke to reach the same call. On an older build
        /// pnputil rejects the verb and this reports that, which is the honest failure.
        /// </summary>
        private async Task<ProcessResult> RunPnpUtilAsync(string verb, string instanceId, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PnpUtilPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(verb);
            psi.ArgumentList.Add(instanceId);

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();

                var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                var output = (stdout + "\n" + stderr).Trim();
                _logging.Debug($"pnputil {verb} -> exit {process.ExitCode}: {output}");

                return new ProcessResult(process.ExitCode == 0, output);
            }
            catch (Exception ex)
            {
                return new ProcessResult(false, ex.Message);
            }
        }

        // ── nvidia-smi ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The enforced limit in watts, or null when nvidia-smi is absent or unhelpful.
        ///
        /// This is the number the clamp moves, and it is a REQUESTED limit rather than a measurement -
        /// a GPU can report a raised limit while delivering nothing. Reported here because it is the
        /// only figure available without running a load, and the caller says so.
        /// </summary>
        public double? ReadEnforcedPowerLimitWatts()
        {
            foreach (var exe in NvidiaSmiCandidates())
            {
                if (!File.Exists(exe)) continue;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add("--query-gpu=enforced.power.limit");
                    psi.ArgumentList.Add("--format=csv,noheader,nounits");

                    using var process = Process.Start(psi);
                    if (process is null) continue;

                    var text = process.StandardOutput.ReadToEnd();

                    // A parked or wedged GPU can take 30 s to answer a query that normally costs
                    // milliseconds, so this waits generously rather than reporting "no NVIDIA GPU"
                    // for what is actually a slow answer.
                    if (!process.WaitForExit(45_000))
                    {
                        // The process can exit between the timeout expiring and the kill landing,
                        // which throws rather than being a no-op.
                        try { process.Kill(); }
                        catch (Exception killEx) { _logging.Debug($"nvidia-smi kill: {killEx.Message}"); }
                        _logging.Warn("nvidia-smi did not answer within 45 s - the GPU may be stalled.");
                        return null;
                    }

                    var first = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                    if (first is not null &&
                        double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var watts))
                    {
                        return watts;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Debug($"nvidia-smi read via {exe} failed: {ex.Message}");
                }
            }

            return null;
        }

        private static IEnumerable<string> NvidiaSmiCandidates()
        {
            // The driver puts it in System32. The NVSMI folder is where older driver packages left
            // it and costs nothing to check.
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");

            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        }

        private static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                // Not knowing whether we are elevated is answered as "not", which costs a disabled
                // button rather than a device restart that fails halfway.
                return false;
            }
        }
    }
}
