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
        private CancellationTokenSource? _cts;
        private bool _lowOverheadMode;
        private MonitoringSample? _lastSample;
        private readonly double _changeThreshold = 0.5; // Minimum change to trigger UI update (degrees/percent)

        public ReadOnlyObservableCollection<MonitoringSample> Samples { get; }
        public event EventHandler<MonitoringSample>? SampleUpdated;

        public HardwareMonitoringService(IHardwareMonitorBridge bridge, LoggingService logging, MonitoringPreferences preferences)
        {
            _bridge = bridge;
            _logging = logging;
            _history = Math.Max(30, preferences.HistoryCount);
            _baseInterval = TimeSpan.FromMilliseconds(Math.Clamp(preferences.PollIntervalMs, 500, 5000));
            _lowOverheadMode = preferences.LowOverheadMode;
            Samples = new ReadOnlyObservableCollection<MonitoringSample>(_samples);
        }

        public bool LowOverheadMode => _lowOverheadMode;

        public void SetLowOverheadMode(bool enabled)
        {
            _lowOverheadMode = enabled;
            _logging.Info($"Hardware monitoring low overhead mode: {enabled}");
        }

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
                try
                {
                    var sample = await _bridge.ReadSampleAsync(token);
                    consecutiveErrors = 0; // Reset error counter on success

                    // Change detection optimization - only update UI if values changed significantly
                    if (ShouldUpdateUI(sample))
                    {
                        if (!_lowOverheadMode)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _samples.Add(sample);
                                while (_samples.Count > _history)
                                {
                                    _samples.RemoveAt(0);
                                }
                            });
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
                    // Adaptive polling - increase interval in low overhead mode
                    var delay = _lowOverheadMode ? _baseInterval.Add(TimeSpan.FromMilliseconds(500)) : _baseInterval;
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
        /// </summary>
        private bool ShouldUpdateUI(MonitoringSample newSample)
        {
            if (_lastSample == null)
                return true;

            // Check if any significant changes occurred
            var cpuTempChange = Math.Abs(newSample.CpuTemperatureC - _lastSample.CpuTemperatureC);
            var gpuTempChange = Math.Abs(newSample.GpuTemperatureC - _lastSample.GpuTemperatureC);
            var cpuLoadChange = Math.Abs(newSample.CpuLoadPercent - _lastSample.CpuLoadPercent);
            var gpuLoadChange = Math.Abs(newSample.GpuLoadPercent - _lastSample.GpuLoadPercent);

            return cpuTempChange >= _changeThreshold ||
                   gpuTempChange >= _changeThreshold ||
                   cpuLoadChange >= _changeThreshold ||
                   gpuLoadChange >= _changeThreshold;
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
