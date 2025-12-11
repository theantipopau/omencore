using System;
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
        /// Currently active tracked processes.
        /// </summary>
        public Dictionary<string, ProcessInfo> ActiveProcesses { get; } = new();

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

            _trackedProcesses.Add(executableName.ToLowerInvariant());
            _logging.Info($"Now tracking process: {executableName}");
        }

        /// <summary>
        /// Remove a process from tracking.
        /// </summary>
        public void UntrackProcess(string executableName)
        {
            if (string.IsNullOrEmpty(executableName))
                return;

            _trackedProcesses.Remove(executableName.ToLowerInvariant());
            _logging.Info($"Stopped tracking process: {executableName}");
        }

        /// <summary>
        /// Clear all tracked processes.
        /// </summary>
        public void ClearTrackedProcesses()
        {
            _trackedProcesses.Clear();
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
                var runningProcesses = Process.GetProcesses()
                    .Where(p => _trackedProcesses.Contains(p.ProcessName.ToLowerInvariant()))
                    .ToDictionary(p => p.ProcessName.ToLowerInvariant(), p => p);

                // Detect new processes (launches)
                foreach (var kvp in runningProcesses)
                {
                    if (!ActiveProcesses.ContainsKey(kvp.Key))
                    {
                        var info = new ProcessInfo
                        {
                            ProcessName = kvp.Value.ProcessName,
                            ProcessId = kvp.Value.Id,
                            ExecutablePath = GetExecutablePath(kvp.Value),
                            StartTime = kvp.Value.StartTime,
                            WindowTitle = GetMainWindowTitle(kvp.Value)
                        };

                        ActiveProcesses[kvp.Key] = info;
                        _logging.Info($"Detected game launch: {info.ProcessName} (PID: {info.ProcessId})");
                        ProcessDetected?.Invoke(this, new ProcessDetectedEventArgs(info));
                    }
                }

                // Detect exited processes
                var exitedProcesses = ActiveProcesses.Keys
                    .Where(k => !runningProcesses.ContainsKey(k))
                    .ToList();

                foreach (var exited in exitedProcesses)
                {
                    var info = ActiveProcesses[exited];
                    var runtime = DateTime.Now - info.StartTime;
                    ActiveProcesses.Remove(exited);

                    _logging.Info($"Detected game exit: {info.ProcessName} (Runtime: {runtime:hh\\:mm\\:ss})");
                    ProcessExited?.Invoke(this, new ProcessExitedEventArgs(info, runtime));
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Error scanning processes", ex);
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
