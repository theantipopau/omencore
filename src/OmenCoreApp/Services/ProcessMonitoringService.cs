using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Timers;
using Timer = System.Timers.Timer;

namespace OmenCore.Services
{
    /// <summary>
    /// Monitors running processes for game detection and profile switching.
    /// </summary>
    public class ProcessMonitoringService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly Timer _pollTimer;
        private readonly HashSet<string> _trackedProcesses = new();
        private readonly object _trackedLock = new();
        private bool _isMonitoring;

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

        public ProcessMonitoringService(LoggingService logging)
        {
            _logging = logging;
            _pollTimer = new Timer(2000); // Poll every 2 seconds
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
                _trackedProcesses.Add(executableName.ToLowerInvariant());
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
                _trackedProcesses.Remove(executableName.ToLowerInvariant());
            }
            _logging.Info($"Stopped tracking process: {executableName}");
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
            _pollTimer.Start();
            _logging.Info($"Process monitoring started ({_trackedProcesses.Count} tracked)");

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
            _logging.Info("Process monitoring stopped");
        }

        private void OnPollTimer(object? sender, ElapsedEventArgs e)
        {
            ScanProcesses();
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

                var runningProcesses = Process.GetProcesses()
                    .Where(p => trackedCopy.Contains(p.ProcessName.ToLowerInvariant()))
                    .ToDictionary(p => p.Id, p => p);

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
