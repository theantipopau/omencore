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

        // How long to wait for every GPU poller to acknowledge that it is out of NVML. Each holds a
        // slot only for the duration of one call, so this is reached only if a poller is wedged
        // inside the driver - in which case pulling the device would kill it, and refusing is right.
        private static readonly TimeSpan QuiesceTimeout = TimeSpan.FromSeconds(15);

        // Used only when the handshake is unavailable, which leaves the marker file and a stopwatch.
        // One poll interval plus margin: a guess, and the code says so where it uses it.
        private static readonly TimeSpan BlindQuiesceWait = TimeSpan.FromSeconds(3);

        // The gate's own expiry, in case this process dies mid-restart. Generous on purpose: an
        // expiry that fires while the GPU is still coming up puts the poll loops back on a stale NVML
        // handle, which is the crash the gate exists to prevent.
        private static readonly TimeSpan GateMaxDuration = TimeSpan.FromMinutes(3);

        // pnputil returns as soon as the enable is queued; the driver takes seconds longer to load and
        // NVML re-enumerates later still. Asking nvidia-smi too early gets "No devices were found",
        // which reads exactly like "nvidia-smi is not installed" if you believe the first answer.
        private static readonly TimeSpan PowerLimitReadTimeout = TimeSpan.FromSeconds(40);

        // Answers "is the dGPU parked" out of the PnP manager's bookkeeping, without touching the
        // device. Used to decide whether reading the power limit is nearly free or costs a wake.
        private readonly Hardware.GpuPowerStateProbe _powerStateProbe;

        public AdapterPowerOverrideService(LoggingService logging)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _powerStateProbe = new Hardware.GpuPowerStateProbe(msg => _logging.Debug(msg));
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

            // Everything below happens with GPU telemetry suspended in this process and in the
            // hardware worker. Both of them hold an NVML device handle that the restart invalidates,
            // and NVML faults rather than failing when called through a stale one - an
            // AccessViolationException, which .NET does not let managed code catch. The first run of
            // this override killed both processes that way, in the same second, in the poll that
            // landed while the GPU was coming back.
            using var gate = Hardware.GpuRestartGate.Begin(
                GateMaxDuration, msg => _logging.Info($"Adapter power override: {msg}"));

            try
            {
                // The gate stops a poller *starting* an NVML call. This waits for the ones already
                // running to come out, which is the half that cannot be done with a timer: a call
                // inside the driver when the device disappears is not recoverable at any speed.
                var quiesced = await Task.Run(
                    () => Hardware.GpuRestartGate.WaitForPollersToLeaveNvml(
                        QuiesceTimeout, msg => _logging.Info($"Adapter power override: {msg}")),
                    cancellationToken).ConfigureAwait(false);

                if (!quiesced)
                {
                    // Either a poller is wedged inside NVML, or there is no handshake to be had. The
                    // first is a reason to refuse: restarting the device under it is the crash this
                    // whole mechanism exists to prevent, and no wait makes it safe.
                    if (Hardware.GpuRestartGate.HandshakeAvailable)
                    {
                        return new OverrideResult(false,
                            "GPU telemetry did not stand down, so the GPU was not restarted. Something " +
                            "is still reading the card. Close other monitoring tools and try again.",
                            before, null);
                    }

                    _logging.Warn("Adapter power override: no NVML handshake — falling back to a timed wait");
                    await Task.Delay(BlindQuiesceWait, cancellationToken).ConfigureAwait(false);
                }

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

                var after = await WaitForEnforcedPowerLimitAsync(PowerLimitReadTimeout).ConfigureAwait(false);
                _logging.Info($"Adapter power override: enforced limit after = {Describe(after)}");

                return new OverrideResult(true, BuildMessage(before, after, NvidiaSmiIsInstalled()), before, after);
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

        /// <param name="nvidiaSmiInstalled">
        /// Separates the two ways a reading goes missing. They look identical from a null and are not
        /// the same news: one means install nothing and expect nothing, the other means the GPU did
        /// not answer and the restart may still have worked. The first version of this reported both
        /// as "nvidia-smi was not available" on a machine that had nvidia-smi in System32.
        /// </param>
        private static string BuildMessage(double? before, double? after, bool nvidiaSmiInstalled)
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

            if (!nvidiaSmiInstalled)
            {
                return "The GPU restarted. Whether the power limit changed is unknown - nvidia-smi is " +
                       "not installed, and it is the only thing here that can read the limit. It ships " +
                       "with the NVIDIA driver.";
            }

            var half = before is double known
                ? $"The limit before the restart was {known:0.#} W. "
                : string.Empty;

            return "The GPU restarted. " + half + "nvidia-smi did not report a limit afterwards, so " +
                   "whether it changed is unknown - the driver may still have been loading. Check again " +
                   "in a moment.";
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

        /// <summary>
        /// Waits for the device to report itself started.
        ///
        /// Two consecutive readings, because the first one lies: pnputil returns as soon as the enable
        /// is queued, and Win32_PnPEntity can still be answering with the status it held before the
        /// device was touched. Believing that single reading made the whole restart appear to finish
        /// in under four seconds, and the power limit was then read off a driver that had not loaded.
        /// </summary>
        private async Task<bool> WaitForDeviceAsync(string instanceId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var consecutiveOk = 0;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500).ConfigureAwait(false);

                try
                {
                    var escaped = instanceId.Replace("\\", "\\\\").Replace("'", "\\'");
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT Status FROM Win32_PnPEntity WHERE DeviceID = '{escaped}'");

                    var ok = false;
                    foreach (ManagementObject device in searcher.Get())
                    {
                        // "OK" is what a started device reports. A device that is present but stopped
                        // reports "Error" or "Degraded", so presence alone is not the test.
                        if ((device["Status"] as string) == "OK") ok = true;
                    }

                    consecutiveOk = ok ? consecutiveOk + 1 : 0;
                    if (consecutiveOk >= 2) return true;
                }
                catch (Exception ex)
                {
                    _logging.Debug($"Waiting for {instanceId}: {ex.Message}");
                    consecutiveOk = 0;
                }
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
        /// The pair of limits the clamp separates.
        ///
        /// <paramref name="EnforcedWatts"/> is what the GPU will actually hold itself to and is the
        /// number the clamp moves. <paramref name="DefaultWatts"/> is the card's own nominal limit,
        /// which the clamp leaves alone - measured on board 8D87 as 35 W enforced against an 80 W
        /// default while clamped, and 80 W against 80 W once the driver restarts without the verdict.
        /// The gap between them is therefore the whole visible evidence that something outside the
        /// card is holding it down.
        ///
        /// Both are REQUESTED limits rather than measurements: a GPU can report a raised limit while
        /// delivering nothing. They are what is available without running a load, and callers say so.
        /// </summary>
        public sealed record PowerLimits(double? EnforcedWatts, double? DefaultWatts)
        {
            /// <summary>
            /// True when the card is being held below its own default. Half a watt of slack because
            /// these are floating-point readings of the same nominal number, not because a real
            /// clamp is ever that small - the measured one is 45 W.
            /// </summary>
            public bool EnforcedIsBelowDefault =>
                EnforcedWatts is double enforced
                && DefaultWatts is double standard
                && enforced < standard - 0.5;
        }

        /// <summary>
        /// The enforced limit in watts, or null when nvidia-smi is absent or unhelpful.
        /// </summary>
        public double? ReadEnforcedPowerLimitWatts() => ReadPowerLimitsWatts().EnforcedWatts;

        /// <summary>
        /// Both limits, in one nvidia-smi call.
        ///
        /// One call rather than two on purpose: each one wakes a parked dGPU and holds it awake for
        /// the driver's inactivity timeout, so asking twice for two fields of the same answer would
        /// double a cost that is already the reason this is never on a timer.
        /// </summary>
        public PowerLimits ReadPowerLimitsWatts()
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
                    psi.ArgumentList.Add("--query-gpu=enforced.power.limit,power.default_limit");
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
                        return new PowerLimits(null, null);
                    }

                    var limits = ParsePowerLimits(text);
                    if (limits.EnforcedWatts is not null) return limits;
                }
                catch (Exception ex)
                {
                    _logging.Debug($"nvidia-smi read via {exe} failed: {ex.Message}");
                }
            }

            return new PowerLimits(null, null);
        }

        /// <summary>
        /// Decodes one row of "enforced, default" out of csv,noheader,nounits output.
        ///
        /// Each field is decoded independently because they fail independently: nvidia-smi answers
        /// "[N/A]" or "[Not Supported]" per field, and a card that will not report its default limit
        /// still reports the enforced one. Losing the enforced reading over the other field being
        /// absent would throw away the only number this feature is about.
        /// </summary>
        private static PowerLimits ParsePowerLimits(string text)
        {
            var row = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (row is null) return new PowerLimits(null, null);

            var fields = row.Split(',');

            static double? Field(string[] fields, int index) =>
                index < fields.Length &&
                double.TryParse(fields[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                out var watts)
                    ? watts
                    : null;

            return new PowerLimits(Field(fields, 0), Field(fields, 1));
        }

        /// <summary>
        /// Whether the dGPU is parked in D3 right now.
        ///
        /// Costs nothing and wakes nothing - it is read from the PnP manager's bookkeeping. Callers
        /// use it to decide whether reading the power limit is nearly free or is worth asking about
        /// first, because nvidia-smi answers by waking the card and keeping it awake afterwards.
        ///
        /// False when the state cannot be determined, matching the probe: an unknown state is not a
        /// reason to refuse a reading the user can see the cost of.
        /// </summary>
        public bool DiscreteGpuIsParked() => _powerStateProbe.IsAsleep();

        /// <summary>
        /// Whether nvidia-smi is on disk at all. Public because the panel has to distinguish "no
        /// reading yet" from "no way to take one" before it offers a button that would do nothing.
        /// </summary>
        public bool CanReadPowerLimit => NvidiaSmiIsInstalled();

        /// <summary>
        /// Retries until the GPU answers or the deadline passes.
        ///
        /// A device that has just been re-enabled reports itself started well before NVML will
        /// enumerate it, and nvidia-smi answers "No devices were found" in the gap. That is not the
        /// same as no reading being available, and taking the first null as final is what produced a
        /// report of "nvidia-smi was not available" on a machine where it was installed and working.
        /// </summary>
        private async Task<double?> WaitForEnforcedPowerLimitAsync(TimeSpan timeout)
        {
            // Nothing to wait for if the tool is not there. Retrying a missing file for forty seconds
            // would hold GPU telemetry suspended for the whole of it, to learn what one File.Exists
            // already said.
            if (!NvidiaSmiIsInstalled()) return null;

            var deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                var watts = ReadEnforcedPowerLimitWatts();
                if (watts is not null) return watts;

                if (DateTime.UtcNow >= deadline) return null;
                await Task.Delay(2000).ConfigureAwait(false);
            }
        }

        /// <summary>Whether nvidia-smi is on disk at all, as opposed to present but unable to answer.</summary>
        private static bool NvidiaSmiIsInstalled() => NvidiaSmiCandidates().Any(File.Exists);

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
