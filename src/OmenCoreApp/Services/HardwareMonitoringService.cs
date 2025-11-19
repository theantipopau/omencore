using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class HardwareMonitoringService : IDisposable
    {
        private readonly IHardwareMonitorBridge _bridge;
        private readonly LoggingService _logging;
        private readonly ObservableCollection<MonitoringSample> _samples = new();
        private readonly int _history;
        private readonly TimeSpan _interval;
        private CancellationTokenSource? _cts;
        private bool _lowOverheadMode;

        public ReadOnlyObservableCollection<MonitoringSample> Samples { get; }
        public event EventHandler<MonitoringSample>? SampleUpdated;

        public HardwareMonitoringService(IHardwareMonitorBridge bridge, LoggingService logging, MonitoringPreferences preferences)
        {
            _bridge = bridge;
            _logging = logging;
            _history = Math.Max(30, preferences.HistoryCount);
            _interval = TimeSpan.FromMilliseconds(Math.Clamp(preferences.PollIntervalMs, 500, 5000));
            _lowOverheadMode = preferences.LowOverheadMode;
            Samples = new ReadOnlyObservableCollection<MonitoringSample>(_samples);
        }

        public bool LowOverheadMode => _lowOverheadMode;

        public void SetLowOverheadMode(bool enabled)
        {
            _lowOverheadMode = enabled;
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
            _logging.Info("Hardware monitoring loop started");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var sample = await _bridge.ReadSampleAsync(token);
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
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error("Hardware monitoring loop error", ex);
                }

                try
                {
                    await Task.Delay(_interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            _logging.Info("Hardware monitoring loop stopped");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
