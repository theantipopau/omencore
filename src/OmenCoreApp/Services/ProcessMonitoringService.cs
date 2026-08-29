using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Timers;
using Timer = System.Timers.Timer;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Monitors running processes for game detection and profile switching.
    /// </summary>
    public class ProcessMonitoringService : IDisposable
    {
        private const double FastPollIntervalMs = 2000;
        private const double IdlePollIntervalMs = 10000;

        // When WMI event-based detection is active, polling only needs to run as an
        // infrequent reconciliation pass (catches anything a dropped/missed WMI event
        // would otherwise leave stale) rather than the primary detection mechanism.
        private const double ReconciliationPollIntervalMs = 20000;

        // Give a freshly-launched process a moment to create its main window before we
        // read WindowTitle and fire ProcessDetected, so GameProfile.WindowTitleContains
        // disambiguation isn't starved by racing the process's own startup. The old
        // ~2s poll interval gave this some slack for free; event-based detection can
        // fire within milliseconds of process creation, well before a window exists.
        private const double WindowTitleSettleDelayMs = 1500;

        // Extrinsic, push-delivered process notifications. These must never be swapped back to the
        // intrinsic __InstanceCreationEvent/__InstanceDeletionEvent classes over Win32_Process: those
        // have no notification source, so WMI services them by re-enumerating the whole process table
        // on the WITHIN interval, inside WmiPrvSE where the cost is invisible to us. See
        // TryStartWmiEventing for the measurements. ProcessMonitoringEventQueryTests pins this.
        internal const string ProcessStartEventQuery = "SELECT * FROM Win32_ProcessStartTrace";
        internal const string ProcessStopEventQuery = "SELECT * FROM Win32_ProcessStopTrace";

        private readonly LoggingService _logging;
        private readonly Timer _pollTimer;
        private readonly HashSet<string> _trackedProcesses = new();
        private readonly object _trackedLock = new();
        private bool _isMonitoring;
        private ManagementEventWatcher? _creationWatcher;
        private ManagementEventWatcher? _deletionWatcher;
        private volatile bool _wmiEventingActive;

        internal static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            name = name.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[0..^4];
            }

            return name.ToLowerInvariant();
        }

        /// <summary>
        /// Fired when a tracked process is detected (game launch).
        /// </summary>
        public event EventHandler<ProcessDetectedEventArgs>? ProcessDetected;

        /// <summary>
        /// Fired when a tracked process exits (game closed).
        /// </summary>
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

        /// <summary>
        /// Currently active tracked processes (keyed by Process ID for multi-instance support).
        /// </summary>
        public ConcurrentDictionary<int, ProcessInfo> ActiveProcesses { get; } = new();

        /// <summary>
        /// Whether process polling is currently active.
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Whether WMI event-based detection (rather than polling) is currently the
        /// primary detection path. False means <see cref="StartMonitoring"/> fell back
        /// to polling-only, e.g. because WMI eventing isn't available in this environment.
        /// </summary>
        public bool IsEventDrivenDetectionActive => _wmiEventingActive;

        /// <summary>
        /// Number of normalized executable names currently tracked.
        /// </summary>
        public int TrackedProcessCount
        {
            get
            {
                lock (_trackedLock)
                {
                    return _trackedProcesses.Count;
                }
            }
        }

        public ProcessMonitoringService(LoggingService logging)
        {
            _logging = logging;
            _pollTimer = new Timer(FastPollIntervalMs);
            _pollTimer.Elapsed += OnPollTimer;
        }

        /// <summary>
        /// Add a process executable name to track (e.g., "RocketLeague.exe").
        /// </summary>
        public void TrackProcess(string executableName)
        {
            if (string.IsNullOrEmpty(executableName))
                return;

            lock (_trackedLock)
            {
                var normalized = NormalizeProcessName(executableName);
                if (!string.IsNullOrEmpty(normalized))
                {
                    _trackedProcesses.Add(normalized);
                }
            }
            _logging.Info($"Now tracking process: {executableName}");
        }

        /// <summary>
        /// Remove a process from tracking.
        /// </summary>
        public void UntrackProcess(string executableName)
        {
            if (string.IsNullOrEmpty(executableName))
                return;

            lock (_trackedLock)
            {
                _trackedProcesses.Remove(NormalizeProcessName(executableName));
            }
            _logging.Info($"Stopped tracking process: {executableName}");
        }

        /// <summary>
        /// Update polling interval based on activity. Slows down when idle to reduce CPU usage.
        /// </summary>
        private void UpdatePollingInterval()
        {
            if (_wmiEventingActive)
            {
                // Event-based detection is primary here; keep the poll as an infrequent
                // reconciliation pass rather than racing it back up to 2s just because a
                // game is running (that would defeat the point of offloading to WMI events).
                if (Math.Abs(_pollTimer.Interval - ReconciliationPollIntervalMs) > 100)
                {
                    _pollTimer.Interval = ReconciliationPollIntervalMs;
                }
                return;
            }

            // Fallback path: fast polling (2s) when games are running, slow (10s) when idle
            var newInterval = ActiveProcesses.Count > 0 ? FastPollIntervalMs : IdlePollIntervalMs;

            if (Math.Abs(_pollTimer.Interval - newInterval) > 100) // Only update if changed significantly
            {
                _pollTimer.Interval = newInterval;
                _logging.Info($"Process monitoring interval adjusted to {newInterval}ms (active processes: {ActiveProcesses.Count})");
            }
        }
        
        /// <summary>
        /// Clear all tracked processes.
        /// </summary>
        public void ClearTrackedProcesses()
        {
            lock (_trackedLock)
            {
                _trackedProcesses.Clear();
            }
            _logging.Info("Cleared all tracked processes");
        }

        /// <summary>
        /// Start monitoring processes.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            TryStartWmiEventing();

            _pollTimer.Interval = _wmiEventingActive ? ReconciliationPollIntervalMs : FastPollIntervalMs;
            _pollTimer.Start();
            BackgroundTimerRegistry.Register(
                "ProcessMonitor",
                "ProcessMonitoringService",
                _wmiEventingActive
                    ? "Reconciliation poll backing up WMI event-based game process detection"
                    : "Polls running processes to detect game launches and workload switches",
                (int)_pollTimer.Interval,
                BackgroundTimerTier.Optional);
            _logging.Info($"Process monitoring started ({_trackedProcesses.Count} tracked, event-based={_wmiEventingActive})");

            // Initial scan
            ScanProcesses();
        }

        /// <summary>
        /// Stop monitoring processes.
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;
            _pollTimer.Stop();
            StopWmiEventing();
            BackgroundTimerRegistry.Unregister("ProcessMonitor");
            _logging.Info("Process monitoring stopped");
        }

        private void OnPollTimer(object? sender, ElapsedEventArgs e)
        {
            ScanProcesses();
        }

        /// <summary>
        /// Try to start push-based process creation/deletion notifications.
        /// Falls back silently to polling-only if the subscription isn't available in this
        /// environment (restrictive WMI ACLs, WMI service unavailable, sandboxed host, etc.).
        /// </summary>
        /// <remarks>
        /// <para><b>Why the trace classes and not <c>__InstanceCreationEvent</c>.</b> The intrinsic
        /// event classes have no notification source behind them. WMI satisfies
        /// <c>__InstanceCreationEvent ... WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'</c> by
        /// enumerating the whole process table on that interval and diffing consecutive snapshots.
        /// Subscribing to creation and deletion therefore ran that full enumeration twice a second
        /// for as long as OmenCore was open — and it ran inside <c>WmiPrvSE.exe</c>, so it never
        /// appeared against our own process in Task Manager.</para>
        ///
        /// <para>Measured on 8D87 (Ryzen AI 9 HX 375, ~380 processes) against a 0.2% idle baseline:
        /// the two intrinsic subscriptions cost <b>15.6% of a core, continuously</b>.
        /// <c>Win32_ProcessStartTrace</c> and <c>Win32_ProcessStopTrace</c> are extrinsic events
        /// pushed from the kernel process trace provider — nothing polls — and measured <b>0.0%</b>
        /// on the same machine while delivering the same notifications.</para>
        ///
        /// <para>The trace classes need an elevated caller, which <c>app.manifest</c> already
        /// guarantees (<c>requireAdministrator</c>). If the subscription fails anyway we drop to
        /// polling only, and deliberately do not fall back to an intrinsic subscription at a longer
        /// <c>WITHIN</c>: <see cref="ScanProcesses"/> polls via <see cref="Process.GetProcesses"/>,
        /// a native call that is cheaper at 2s than the intrinsic subscription was at 1s.</para>
        /// </remarks>
        private void TryStartWmiEventing()
        {
            try
            {
                _creationWatcher = new ManagementEventWatcher(
                    new WqlEventQuery(ProcessStartEventQuery));
                _creationWatcher.EventArrived += OnProcessCreationEvent;
                _creationWatcher.Start();

                _deletionWatcher = new ManagementEventWatcher(
                    new WqlEventQuery(ProcessStopEventQuery));
                _deletionWatcher.EventArrived += OnProcessDeletionEvent;
                _deletionWatcher.Start();

                _wmiEventingActive = true;
                _logging.Info("Event-based process detection active (Win32_ProcessStartTrace/Win32_ProcessStopTrace)");
            }
            catch (Exception ex)
            {
                _wmiEventingActive = false;
                StopWmiEventing();
                _logging.Warn($"Push-based process detection unavailable, falling back to polling only: {ex.Message}");
            }
        }

        private void StopWmiEventing()
        {
            if (_creationWatcher != null)
            {
                try { _creationWatcher.Stop(); } catch { /* best-effort teardown */ }
                try { _creationWatcher.Dispose(); } catch { /* best-effort teardown */ }
                _creationWatcher = null;
            }

            if (_deletionWatcher != null)
            {
                try { _deletionWatcher.Stop(); } catch { /* best-effort teardown */ }
                try { _deletionWatcher.Dispose(); } catch { /* best-effort teardown */ }
                _deletionWatcher = null;
            }

            _wmiEventingActive = false;
        }

        private void OnProcessCreationEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                // Win32_ProcessStartTrace exposes the fields directly; there is no TargetInstance
                // wrapper as there was on the intrinsic classes. ProcessName carries the same
                // "foo.exe" form Win32_Process.Name did, so NormalizeProcessName is unchanged.
                var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
                var name = e.NewEvent["ProcessName"]?.ToString() ?? string.Empty;

                HashSet<string> trackedCopy;
                lock (_trackedLock)
                {
                    trackedCopy = new HashSet<string>(_trackedProcesses);
                }

                if (!trackedCopy.Contains(NormalizeProcessName(name)) || ActiveProcesses.ContainsKey(pid))
                {
                    return;
                }

                var settleTimer = new Timer(WindowTitleSettleDelayMs) { AutoReset = false };
                settleTimer.Elapsed += (_, _) =>
                {
                    settleTimer.Dispose();
                    CompleteEventDetection(pid);
                };
                settleTimer.Start();
            }
            catch (Exception ex)
            {
                _logging.Error("Error handling process-start event", ex);
            }
        }

        private void CompleteEventDetection(int pid)
        {
            if (ActiveProcesses.ContainsKey(pid))
                return;

            try
            {
                using var process = Process.GetProcessById(pid);
                var info = new ProcessInfo
                {
                    ProcessName = process.ProcessName,
                    ProcessId = pid,
                    ExecutablePath = GetExecutablePath(process),
                    StartTime = GetStartTimeSafe(process),
                    WindowTitle = GetMainWindowTitle(process)
                };

                if (ActiveProcesses.TryAdd(pid, info))
                {
                    _logging.Info($"Detected game launch (event): {info.ProcessName} (PID: {pid})");
                    ProcessDetected?.Invoke(this, new ProcessDetectedEventArgs(info));
                }
            }
            catch (ArgumentException)
            {
                // Process already exited before we could follow up — nothing to detect.
            }
            catch (Exception ex)
            {
                _logging.Error($"Error completing event-based detection for PID {pid}", ex);
            }
        }

        private void OnProcessDeletionEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);

                if (ActiveProcesses.TryRemove(pid, out var info))
                {
                    var runtime = DateTime.Now - info.StartTime;
                    _logging.Info($"Detected game exit (event): {info.ProcessName} (PID: {pid}, Runtime: {runtime:hh\\:mm\\:ss})");
                    ProcessExited?.Invoke(this, new ProcessExitedEventArgs(info, runtime));
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Error handling process-stop event", ex);
            }
        }

        private void ScanProcesses()
        {
            try
            {
                // Build dictionary of running tracked processes by PID
                HashSet<string> trackedCopy;
                lock (_trackedLock)
                {
                    trackedCopy = new HashSet<string>(_trackedProcesses);
                }

                var allProcesses = Process.GetProcesses();
                var runningProcesses = new Dictionary<int, Process>();

                foreach (var p in allProcesses)
                {
                    try
                    {
                        if (trackedCopy.Contains(NormalizeProcessName(p.ProcessName)))
                        {
                            runningProcesses[p.Id] = p;
                        }
                        else
                        {
                            p.Dispose();
                        }
                    }
                    catch
                    {
                        // Handle potential access denied or race conditions
                        p.Dispose();
                    }
                }

                // Detect new processes (launches) - keyed by Process ID to support multiple instances
                foreach (var kvp in runningProcesses)
                {
                    var pid = kvp.Key;
                    var process = kvp.Value;

                    if (!ActiveProcesses.ContainsKey(pid))
                    {
                        var info = new ProcessInfo
                        {
                            ProcessName = process.ProcessName,
                            ProcessId = pid,
                            ExecutablePath = GetExecutablePath(process),
                            StartTime = GetStartTimeSafe(process),
                            WindowTitle = GetMainWindowTitle(process)
                        };

                        ActiveProcesses[pid] = info;
                        _logging.Info($"Detected game launch: {info.ProcessName} (PID: {pid})");
                        ProcessDetected?.Invoke(this, new ProcessDetectedEventArgs(info));
                    }
                }

                // Detect exited processes
                var exitedPids = ActiveProcesses.Keys
                    .Where(pid => !runningProcesses.ContainsKey(pid))
                    .ToList();

                foreach (var pid in exitedPids)
                {
                    if (ActiveProcesses.TryRemove(pid, out var info))
                    {
                        var runtime = DateTime.Now - info.StartTime;
                        _logging.Info($"Detected game exit: {info.ProcessName} (PID: {pid}, Runtime: {runtime:hh\\:mm\\:ss})");
                        ProcessExited?.Invoke(this, new ProcessExitedEventArgs(info, runtime));
                    }
                }
                
                // Update polling interval based on activity
                UpdatePollingInterval();
            }
            catch (Exception ex)
            {
                _logging.Error("Error scanning processes", ex);
            }
        }

        /// <summary>
        /// Safely get process start time (can throw if process exits during access).
        /// </summary>
        private DateTime GetStartTimeSafe(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.Now; // Fallback to current time if process exited
            }
        }

        private string GetExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // Access denied for some processes (e.g., elevated)
                return string.Empty;
            }
        }

        private string GetMainWindowTitle(Process process)
        {
            try
            {
                return process.MainWindowTitle ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Get detailed info for a specific process using WMI (slower but more data).
        /// </summary>
        public ProcessInfo? GetDetailedProcessInfo(int processId)
        {
            try
            {
                var query = $"SELECT * FROM Win32_Process WHERE ProcessId = {processId}";
                using var searcher = new ManagementObjectSearcher(query);
                using var results = searcher.Get();

                foreach (ManagementObject obj in results)
                {
                    return new ProcessInfo
                    {
                        ProcessName = obj["Name"]?.ToString() ?? string.Empty,
                        ProcessId = processId,
                        ExecutablePath = obj["ExecutablePath"]?.ToString() ?? string.Empty,
                        CommandLine = obj["CommandLine"]?.ToString() ?? string.Empty,
                        StartTime = ManagementDateTimeConverter.ToDateTime(obj["CreationDate"]?.ToString() ?? string.Empty)
                    };
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to get detailed process info for PID {processId}", ex);
            }

            return null;
        }

        public void Dispose()
        {
            StopMonitoring();
            _pollTimer?.Dispose();
        }
    }

    /// <summary>
    /// Process information snapshot.
    /// </summary>
    public class ProcessInfo
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }

    public class ProcessDetectedEventArgs : EventArgs
    {
        public ProcessInfo ProcessInfo { get; }

        public ProcessDetectedEventArgs(ProcessInfo processInfo)
        {
            ProcessInfo = processInfo;
        }
    }

    public class ProcessExitedEventArgs : EventArgs
    {
        public ProcessInfo ProcessInfo { get; }
        public TimeSpan Runtime { get; }

        public ProcessExitedEventArgs(ProcessInfo processInfo, TimeSpan runtime)
        {
            ProcessInfo = processInfo;
            Runtime = runtime;
        }
    }
}
