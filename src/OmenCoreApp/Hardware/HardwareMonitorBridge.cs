using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    public interface IHardwareMonitorBridge
    {
        Task<MonitoringSample> ReadSampleAsync(CancellationToken token);
        
        /// <summary>
        /// Request bridge to reinitialize/restart hardware monitoring.
        /// Called when consecutive timeouts indicate hardware monitoring is stuck.
        /// </summary>
        Task<bool> TryRestartAsync();

        /// <summary>
        /// Human-readable monitoring source label for UI display.
        /// </summary>
        string MonitoringSource { get; }
    }

    public class LibreHardwareMonitorBridge : IHardwareMonitorBridge
    {
        private readonly Random _random = new();
        private double _cpuTemp = 55;
        private double _gpuTemp = 60;
        private double _cpuLoad = 35;
        private double _gpuLoad = 40;
        private double _vram = 2048;
        private double _ramUsage = 10;
        private double _fanRpm = 1800;
        private double _ssdTemp = 42;
        private double _diskUsage = 15;

        public string MonitoringSource => "Mock Sensors";

        public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Step(ref _cpuTemp, 35, 95);
            Step(ref _gpuTemp, 35, 90);
            Step(ref _cpuLoad, 5, 100);
            Step(ref _gpuLoad, 5, 100);
            Step(ref _vram, 512, 8192);
            Step(ref _ramUsage, 4, 32);
            Step(ref _fanRpm, 900, 4200);
            Step(ref _ssdTemp, 30, 80);
            Step(ref _diskUsage, 0, 100);

            var cores = new List<double>();
            for (var i = 0; i < 8; i++)
            {
                cores.Add(3500 + _random.NextDouble() * 800);
            }

            var sample = new MonitoringSample
            {
                CpuTemperatureC = Math.Round(_cpuTemp, 1),
                CpuLoadPercent = Math.Round(_cpuLoad, 1),
                CpuCoreClocksMhz = cores,
                GpuTemperatureC = Math.Round(_gpuTemp, 1),
                GpuLoadPercent = Math.Round(_gpuLoad, 1),
                GpuVramUsageMb = Math.Round(_vram, 0),
                RamUsageGb = Math.Round(_ramUsage, 1),
                RamTotalGb = 32,
                FanRpm = Math.Round(_fanRpm, 0),
                SsdTemperatureC = Math.Round(_ssdTemp, 1),
                DiskUsagePercent = Math.Round(_diskUsage, 1)
            };

            return Task.FromResult(sample);
        }

        public Task<bool> TryRestartAsync()
        {
            // Mock bridge doesn't need restart - always succeeds
            return Task.FromResult(true);
        }

        private void Step(ref double value, double min, double max)
        {
            value += (_random.NextDouble() - 0.5) * 5;
            value = Math.Clamp(value, min, max);
        }
    }
}
