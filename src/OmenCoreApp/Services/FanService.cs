using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class FanService : IDisposable
    {
        private readonly FanController _fanController;
        private readonly ThermalSensorProvider _thermalProvider;
        private readonly LoggingService _logging;
        private readonly TimeSpan _pollPeriod;
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private readonly ObservableCollection<FanTelemetry> _fanTelemetry = new();
        private CancellationTokenSource? _cts;

        public ReadOnlyObservableCollection<ThermalSample> ThermalSamples { get; }
        public ReadOnlyObservableCollection<FanTelemetry> FanTelemetry { get; }

        public FanService(FanController controller, ThermalSensorProvider thermalProvider, LoggingService logging, int pollMs)
        {
            _fanController = controller;
            _thermalProvider = thermalProvider;
            _logging = logging;
            _pollPeriod = TimeSpan.FromMilliseconds(Math.Max(250, pollMs));
            ThermalSamples = new ReadOnlyObservableCollection<ThermalSample>(_thermalSamples);
            FanTelemetry = new ReadOnlyObservableCollection<FanTelemetry>(_fanTelemetry);
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorLoop(_cts.Token));
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

        public void ApplyPreset(FanPreset preset)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn($"Fan preset '{preset.Name}' skipped; EC bridge unavailable");
                return;
            }
            _fanController.ApplyPreset(preset);
            _logging.Info($"Fan preset '{preset.Name}' pushed to EC");
        }

        public void ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            if (!FanWritesAvailable)
            {
                _logging.Warn("Custom fan curve skipped; EC bridge unavailable");
                return;
            }
            _fanController.ApplyCustomCurve(curve);
            _logging.Info("Custom fan curve pushed to EC");
        }

        public bool FanWritesAvailable => _fanController.IsEcReady;

        private async Task MonitorLoop(CancellationToken token)
        {
            _logging.Info("Fan monitor loop started");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var temps = _thermalProvider.ReadTemperatures().ToList();
                    var sample = new ThermalSample
                    {
                        Timestamp = DateTime.Now,
                        CpuCelsius = temps.FirstOrDefault(t => t.Sensor.Contains("CPU"))?.Celsius ?? temps.FirstOrDefault()?.Celsius ?? 0,
                        GpuCelsius = temps.FirstOrDefault(t => t.Sensor.Contains("GPU"))?.Celsius ?? temps.Skip(1).FirstOrDefault()?.Celsius ?? 0
                    };
                    // Read fan speeds
                    var fanSpeeds = _fanController.ReadFanSpeeds().ToList();

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _thermalSamples.Add(sample);
                        const int window = 120;
                        while (_thermalSamples.Count > window)
                        {
                            _thermalSamples.RemoveAt(0);
                        }

                        // Update fan telemetry
                        _fanTelemetry.Clear();
                        foreach (var fan in fanSpeeds)
                        {
                            _fanTelemetry.Add(fan);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logging.Error("Fan monitor loop error", ex);
                }

                await Task.Delay(_pollPeriod, token);
            }

            _logging.Info("Fan monitor loop stopped");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
