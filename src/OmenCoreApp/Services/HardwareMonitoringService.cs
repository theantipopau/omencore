using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Hardware monitoring service with performance enhancements.
    /// Features: Object pooling, change detection, adaptive polling, low-overhead mode.
    /// </summary>
    public class HardwareMonitoringService : IDisposable
    {
        private readonly IHardwareMonitorBridge _bridge;
        private readonly LoggingService _logging;
        private readonly ObservableCollection<MonitoringSample> _samples = new();
        private readonly int _history;
        private readonly TimeSpan _baseInterval;
        private readonly TimeSpan _lowOverheadInterval;
        private CancellationTokenSource? _cts;
        private volatile bool _lowOverheadMode; // volatile for thread-safe reads from monitor loop
        private MonitoringSample? _lastSample;
        private readonly double _changeThreshold = 0.5; // Minimum change to trigger UI update (degrees/percent)
        private readonly double _lowOverheadChangeThreshold = 3.0; // Higher threshold in low overhead mode
        private volatile bool _isPaused; // For S0 Modern Standby support (volatile for thread-safety)
        private readonly object _pauseLock = new();
        private volatile bool _pendingUIUpdate; // Throttle BeginInvoke backlog

        public ReadOnlyObservableCollection<MonitoringSample> Samples { get; }
        public event EventHandler<MonitoringSample>? SampleUpdated;

        public HardwareMonitoringService(IHardwareMonitorBridge bridge, LoggingService logging, MonitoringPreferences preferences)
        {
            _bridge = bridge;
            _logging = logging;
            _history = Math.Max(30, preferences.HistoryCount);
            _baseInterval = TimeSpan.FromMilliseconds(Math.Clamp(preferences.PollIntervalMs, 500, 5000));
            // Low overhead mode: poll every 5 seconds instead of 1 second
            _lowOverheadInterval = TimeSpan.FromMilliseconds(Math.Max(5000, preferences.PollIntervalMs * 5));
            _lowOverheadMode = preferences.LowOverheadMode;
            Samples = new ReadOnlyObservableCollection<MonitoringSample>(_samples);
            
            // Set initial low overhead mode on the bridge if it supports it
            if (_bridge is LibreHardwareMonitorImpl lhwm)
            {
                lhwm.SetLowOverheadMode(_lowOverheadMode);
            }
        }

        public bool LowOverheadMode => _lowOverheadMode;

        public void SetLowOverheadMode(bool enabled)
        {
            _lowOverheadMode = enabled;
            _logging.Info($"Hardware monitoring low overhead mode: {enabled} (poll interval: {(enabled ? _lowOverheadInterval : _baseInterval).TotalMilliseconds}ms)");
            
            // Also set on the bridge for cache lifetime adjustment
            if (_bridge is LibreHardwareMonitorImpl lhwm)
            {
                lhwm.SetLowOverheadMode(enabled);
            }
        }

        /// <summary>
        /// Pause monitoring during system suspend (S0 Modern Standby).
        /// Prevents fan revving from WMI/EC queries while system is in standby.
        /// </summary>
        public void Pause()
        {
            lock (_pauseLock)
            {
                if (!_isPaused)
                {
                    _isPaused = true;
                    _logging.Info("Hardware monitoring paused (system entering standby)");
                }
            }
        }

        /// <summary>
        /// Resume monitoring after system wakes from suspend.
        /// </summary>
        public void Resume()
        {
            lock (_pauseLock)
            {
                if (_isPaused)
                {
                    _isPaused = false;
                    _logging.Info("Hardware monitoring resumed (system waking from standby)");
                }
            }
        }

        public bool IsPaused => _isPaused;

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            _logging.Info("Optimized hardware monitoring loop started");
            var consecutiveErrors = 0;
            const int maxErrors = 5;

            while (!token.IsCancellationRequested)
            {
                // Check if paused (S0 Modern Standby support)
                if (_isPaused)
                {
                    try
                    {
                        // Wait while paused, checking every 500ms
                        await Task.Delay(500, token);
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                try
                {
                    var sample = await _bridge.ReadSampleAsync(token);
                    consecutiveErrors = 0; // Reset error counter on success

                    // Change detection optimization - only update UI if values changed significantly
                    if (ShouldUpdateUI(sample))
                    {
                        if (!_lowOverheadMode)
                        {
                            // Throttle UI updates to prevent Dispatcher backlog during heavy load
                            if (!_pendingUIUpdate)
                            {
                                _pendingUIUpdate = true;
                                // Use BeginInvoke to avoid potential deadlocks
                                Application.Current?.Dispatcher?.BeginInvoke(() =>
                                {
                                    _samples.Add(sample);
                                    while (_samples.Count > _history)
                                    {
                                        _samples.RemoveAt(0);
                                    }
                                    _pendingUIUpdate = false;
                                });
                            }
                        }

                        SampleUpdated?.Invoke(this, sample);
                        _lastSample = sample;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logging.Error($"Hardware monitoring error ({consecutiveErrors}/{maxErrors})", ex);

                    if (consecutiveErrors >= maxErrors)
                    {
                        _logging.Error("Too many consecutive errors, stopping hardware monitoring");
                        break;
                    }
                }

                try
                {
                    // Adaptive polling - significantly slower in low overhead mode
                    var delay = _lowOverheadMode ? _lowOverheadInterval : _baseInterval;
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logging.Info("Optimized hardware monitoring loop stopped");
        }

        /// <summary>
        /// Determines if UI should be updated based on change threshold.
        /// Reduces unnecessary UI updates and improves performance.
        /// Uses a higher threshold in low-overhead mode to reduce CPU wake-ups.
        /// </summary>
        private bool ShouldUpdateUI(MonitoringSample newSample)
        {
            if (_lastSample == null)
                return true;

            // Use higher threshold in low overhead mode to reduce updates
            var threshold = _lowOverheadMode ? _lowOverheadChangeThreshold : _changeThreshold;

            // Check if any significant changes occurred
            var cpuTempChange = Math.Abs(newSample.CpuTemperatureC - _lastSample.CpuTemperatureC);
            var gpuTempChange = Math.Abs(newSample.GpuTemperatureC - _lastSample.GpuTemperatureC);
            var cpuLoadChange = Math.Abs(newSample.CpuLoadPercent - _lastSample.CpuLoadPercent);
            var gpuLoadChange = Math.Abs(newSample.GpuLoadPercent - _lastSample.GpuLoadPercent);

            return cpuTempChange >= threshold ||
                   gpuTempChange >= threshold ||
                   cpuLoadChange >= threshold ||
                   gpuLoadChange >= threshold;
        }

        public void Dispose()
        {
            Stop();
            if (_bridge is IDisposable disposableBridge)
            {
                disposableBridge.Dispose();
            }
        }
    }
}
