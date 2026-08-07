using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OmenCore.Hardware
{
    /// <summary>
    /// A "do not touch the discrete GPU" flag held while the device is being restarted.
    ///
    /// LibreHardwareMonitor reads NVIDIA telemetry through NVML, and the <c>NvmlDevice</c> handle it
    /// caches when the <c>Computer</c> is opened does not survive the device being disabled and
    /// re-enabled. Calling <c>nvmlDeviceGetPowerUsage</c> with the stale handle faults inside the
    /// driver, and .NET surfaces that as an <see cref="AccessViolationException"/> — a corrupted-state
    /// exception that managed code is not allowed to catch, so the process dies where it stands. That
    /// is exactly how both OmenCore.exe and OmenCore.HardwareWorker.exe died on the first run of the
    /// adapter power override: same frame, same second, in the poll that landed while the GPU was
    /// coming back.
    ///
    /// Two processes poll that GPU and they share no memory, so the flag is a file. It carries an
    /// absolute expiry because the alternative failure is worse than the one being fixed: a process
    /// that dies mid-restart would otherwise leave GPU telemetry switched off, silently, until
    /// someone deleted a file they had never heard of.
    ///
    /// The marker alone would only stop calls that have not started yet, which leaves the one that
    /// matters: a poll already inside NVML when the device disappears. So there is a second half.
    /// Every process holds a slot in a named semaphore for the duration of each NVML call, and the
    /// restarter drains every slot before touching the device — which it can only do once the last
    /// in-flight call has returned. See <see cref="TryEnterNvml"/> and
    /// <see cref="WaitForPollersToLeaveNvml"/> for why the two halves compose into a guarantee rather
    /// than a shorter race.
    ///
    /// Deliberately dependency-free, like <see cref="GpuPowerStateProbe"/>, so it links into the
    /// hardware worker — which needs it as much as the app does and references no app assembly.
    /// </summary>
    public static class GpuRestartGate
    {
        /// <summary>
        /// How long a cached answer is reused. Both poll loops ask on every reading, and the point of
        /// the gate is to be cheap enough that nobody is tempted to check it less often.
        /// </summary>
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(150);

        private static readonly object Sync = new();
        private static long _cachedAtTicks = -1;
        private static bool _cachedValue;

        /// <summary>
        /// The marker, beside the logs the worker already writes, so both processes resolve the same
        /// path without being told it. They run as the same user — the app spawns the worker.
        /// </summary>
        public static string MarkerPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmenCore",
            "gpu-restart.marker");

        /// <summary>
        /// True while some process is restarting the discrete GPU. Callers must not read NVML, NVAPI
        /// or anything else that talks to the device until this goes false.
        /// </summary>
        public static bool IsRestarting
        {
            get
            {
                lock (Sync)
                {
                    var now = Environment.TickCount64;
                    if (_cachedAtTicks >= 0 && now - _cachedAtTicks < CacheLifetime.TotalMilliseconds)
                    {
                        return _cachedValue;
                    }

                    _cachedValue = ReadMarker();
                    _cachedAtTicks = now;
                    return _cachedValue;
                }
            }
        }

        /// <summary>
        /// Open the gate. Dispose the result to close it.
        /// </summary>
        /// <param name="maxDuration">
        /// How long the gate stays shut if it is never disposed. Make this comfortably longer than the
        /// restart is expected to take: an expiry that fires early puts the poll loops back on a
        /// device that is still coming up, which is the crash this exists to prevent.
        /// </param>
        /// <param name="log">Optional; called once on open and once on close.</param>
        public static IDisposable Begin(TimeSpan maxDuration, Action<string>? log = null)
        {
            var expiry = DateTimeOffset.UtcNow.Add(maxDuration).ToUnixTimeMilliseconds();

            try
            {
                var path = MarkerPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, expiry.ToString(CultureInfo.InvariantCulture));
                Invalidate();
                log?.Invoke($"GPU restart gate open — telemetry suspended for up to {maxDuration.TotalSeconds:0} s");
            }
            catch (Exception ex)
            {
                // A gate that could not be opened must not stop the caller: it degrades to the
                // behaviour we had before it existed, and the caller says so.
                log?.Invoke($"GPU restart gate could not be opened ({ex.Message}) — telemetry will not be suspended");
            }

            return new Gate(log);
        }

        /// <summary>
        /// Drop the cached answer. Called after the marker is written or removed by this process; the
        /// other process picks the change up within <see cref="CacheLifetime"/>.
        /// </summary>
        private static void Invalidate()
        {
            lock (Sync) { _cachedAtTicks = -1; }
        }

        private static bool ReadMarker()
        {
            try
            {
                var path = MarkerPath;
                if (!File.Exists(path)) return false;

                var text = File.ReadAllText(path);
                if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry))
                {
                    // Unreadable content is treated as expired rather than as "restarting forever".
                    TryDelete(path);
                    return false;
                }

                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiry)
                {
                    TryDelete(path);
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Failing open. A probe that cannot read a file is not evidence that a GPU is being
                // restarted, and suspending all GPU telemetry on that basis would be the worse guess.
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception) { /* Another process may be deleting the same expired marker. */ }
        }

        // ── the in-flight handshake ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Slots in the in-flight semaphore. Two processes poll the GPU today and each has a single
        /// update thread, so this is order-of-magnitude headroom rather than a tuned figure. It has to
        /// be a fixed number because draining the semaphore means acquiring all of them.
        /// </summary>
        private const int NvmlSlots = 16;

        /// <summary>
        /// Session-scoped, not <c>Global\</c>: the app and the worker share a session — the app spawns
        /// the worker — and creating a global kernel object needs a privilege that an unelevated run
        /// does not have. The override refuses to run unelevated, but telemetry does not, and a
        /// telemetry path that throws on an unprivileged machine would be a worse bug than the one
        /// this is here to prevent.
        /// </summary>
        private const string NvmlSemaphoreName = @"Local\OmenCore.Nvml.InFlight";

        private static Semaphore? _nvmlSlotsSemaphore;
        private static bool _nvmlSemaphoreUnavailable;

        /// <summary>
        /// Whether the in-flight handshake exists on this machine. When it does not, a failed
        /// <see cref="WaitForPollersToLeaveNvml"/> means "cannot tell", not "someone is inside" — and
        /// those two deserve different answers from a caller about to pull a device.
        /// </summary>
        public static bool HandshakeAvailable => NvmlSlotsSemaphore != null;

        private static Semaphore? NvmlSlotsSemaphore
        {
            get
            {
                lock (Sync)
                {
                    if (_nvmlSlotsSemaphore != null || _nvmlSemaphoreUnavailable) return _nvmlSlotsSemaphore;

                    try
                    {
                        _nvmlSlotsSemaphore = new Semaphore(NvmlSlots, NvmlSlots, NvmlSemaphoreName);
                    }
                    catch (Exception)
                    {
                        // No handshake available. Everything degrades to the marker file, which is
                        // what this looked like before the handshake existed - narrower, not broken.
                        _nvmlSemaphoreUnavailable = true;
                    }

                    return _nvmlSlotsSemaphore;
                }
            }
        }

        /// <summary>
        /// Take a slot for the duration of one NVML call, or refuse if the GPU is being restarted.
        ///
        /// Callers must dispose the lease in a <c>finally</c>. A leaked slot is not recovered when the
        /// process dies — Windows semaphores have no abandoned state, unlike mutexes — so it costs one
        /// slot for the life of the kernel object. That is why there are sixteen of them.
        ///
        /// The marker is re-read <i>after</i> the slot is taken, and that ordering is the whole proof:
        /// a caller that passes the check while holding a slot cannot have been passed by a restarter,
        /// because the restarter sets the marker before it starts draining.
        /// </summary>
        /// <returns>False when the caller must not touch the GPU at all.</returns>
        public static bool TryEnterNvml(out IDisposable? lease)
        {
            lease = null;

            var semaphore = NvmlSlotsSemaphore;
            if (semaphore == null)
            {
                // Degraded to marker-only. Answer the question the marker can answer.
                return !IsRestarting;
            }

            bool taken;
            try
            {
                taken = semaphore.WaitOne(0);
            }
            catch (Exception)
            {
                return !IsRestarting;
            }

            if (!taken) return false;

            if (IsRestarting)
            {
                Release(semaphore);
                return false;
            }

            lease = new NvmlLease(semaphore);
            return true;
        }

        /// <summary>
        /// Block until no process is inside an NVML call, so the device can be restarted under it.
        ///
        /// Only valid with the gate already open — draining is what makes the *current* calls finish,
        /// and the marker is what stops the next ones starting. Either alone is a race; the pair is
        /// not, because a poller takes its slot before it re-reads the marker.
        ///
        /// The slots are released again as soon as they are all held. Holding them across the restart
        /// would add nothing the marker does not already do, and would leave GPU telemetry locked out
        /// of this session if the restarting process died mid-way — a semaphore count is not returned
        /// when its holder exits, and no expiry can reach it.
        /// </summary>
        /// <returns>
        /// True when every poller was observed out of NVML. False on timeout, or when there is no
        /// handshake available — in which case the caller has the marker and a stopwatch, and should
        /// say so rather than claim the device is safe to pull.
        /// </returns>
        public static bool WaitForPollersToLeaveNvml(TimeSpan timeout, Action<string>? log = null)
        {
            var semaphore = NvmlSlotsSemaphore;
            if (semaphore == null)
            {
                log?.Invoke("No NVML handshake available — falling back to waiting out one poll interval");
                return false;
            }

            var deadline = DateTime.UtcNow + timeout;
            var held = 0;

            try
            {
                while (held < NvmlSlots)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        log?.Invoke($"Timed out waiting for GPU pollers to leave NVML ({held}/{NvmlSlots} slots)");
                        return false;
                    }

                    if (!semaphore.WaitOne(remaining)) continue;
                    held++;
                }

                log?.Invoke("All GPU pollers are out of NVML");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"NVML handshake failed ({ex.Message}) — falling back to waiting out one poll interval");
                return false;
            }
            finally
            {
                for (var i = 0; i < held; i++) Release(semaphore);
            }
        }

        private static void Release(Semaphore semaphore)
        {
            try { semaphore.Release(); }
            catch (SemaphoreFullException) { /* Already at maximum; nothing was leaked. */ }
            catch (ObjectDisposedException) { /* Shutting down. */ }
        }

        private sealed class NvmlLease : IDisposable
        {
            private readonly Semaphore _semaphore;
            private int _released;

            public NvmlLease(Semaphore semaphore) => _semaphore = semaphore;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0) return;
                Release(_semaphore);
            }
        }

        private sealed class Gate : IDisposable
        {
            private readonly Action<string>? _log;
            private int _closed;

            public Gate(Action<string>? log) => _log = log;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;

                TryDelete(MarkerPath);
                Invalidate();
                _log?.Invoke("GPU restart gate closed — telemetry may resume");
            }
        }
    }
}
