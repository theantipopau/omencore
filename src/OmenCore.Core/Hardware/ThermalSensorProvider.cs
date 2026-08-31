using System;
using System.Collections.Generic;
using System.Threading;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    public class ThermalSensorProvider
    {
        private readonly LibreHardwareMonitorImpl? _bridge;
        private readonly WmiBiosMonitor? _wmiBiosMonitor;
        private readonly HpWmiBios? _wmiBios;
        private const int LastGoodTemperatureReuseSeconds = 15;
        private double _lastGoodCpuTemp;
        private double _lastGoodGpuTemp;
        private DateTime _lastGoodSampleUtc = DateTime.MinValue;
        
        /// <summary>
        /// Create ThermalSensorProvider with LibreHardwareMonitorImpl for full monitoring
        /// </summary>
        public ThermalSensorProvider(LibreHardwareMonitorImpl bridge)
        {
            _bridge = bridge;
        }
        
        /// <summary>
        /// Create ThermalSensorProvider with IHardwareMonitorBridge interface.
        /// Uses the bridge's own cached readings (which include ACPI thermal zone
        /// and NVAPI enrichment) rather than creating a separate raw HpWmiBios.
        /// </summary>
        public ThermalSensorProvider(IHardwareMonitorBridge bridge)
        {
            _bridge = bridge as LibreHardwareMonitorImpl;
            if (_bridge == null)
            {
                // Prefer WmiBiosMonitor which has ACPI-enhanced temperatures
                _wmiBiosMonitor = bridge as WmiBiosMonitor;
                if (_wmiBiosMonitor == null)
                {
                    // Last resort: raw WMI BIOS (integer-only temps)
                    _wmiBios = new HpWmiBios(null);
                }
            }
        }

        public IEnumerable<TemperatureReading> ReadTemperatures()
        {
            var list = new List<TemperatureReading>();

            double cpuTemp = 0;
            double gpuTemp = 0;
            
            // Try LibreHardwareMonitor first
            if (_bridge != null)
            {
                cpuTemp = _bridge.GetCpuTemperature();
                gpuTemp = _bridge.GetGpuTemperature();
            }
            // Use WmiBiosMonitor's ACPI-enhanced cached readings
            else if (_wmiBiosMonitor != null)
            {
                try
                {
                    // Read sample asynchronously but avoid blocking the UI thread indefinitely.
                    // Some callers (OSD, UI controls) may call this on the UI thread — block at most 250ms there.
                    var readTask = _wmiBiosMonitor.ReadSampleAsync(CancellationToken.None);

                    bool onUiThread = OmenCore.Utils.UiThreadMarshaller.IsOnUiThread();
                    if (onUiThread)
                    {
                        // Bound the wait on UI thread to keep UI responsive.
                        // Use WaitAsync timeout rather than Wait()+Result to avoid sync-over-async deadlock patterns.
                        try
                        {
                            var sample = readTask
                                .WaitAsync(TimeSpan.FromMilliseconds(250))
                                .GetAwaiter()
                                .GetResult();
                            cpuTemp = sample.CpuTemperatureC;
                            gpuTemp = sample.GpuTemperatureC;
                        }
                        catch (TimeoutException)
                        {
                            // Timed out on UI thread — return cached/empty values to avoid freeze and log for diagnostics
                            try { AppHost.Logging?.Warn("[ThermalSensorProvider] UI-thread ReadSampleAsync timed out (250ms)"); } catch { }
                            TryReuseLastGoodTemperatures(ref cpuTemp, ref gpuTemp);
                        }
                    }
                    else
                    {
                        // Non-UI callers (background threads) may block until the sample is ready
                        var sample = readTask.GetAwaiter().GetResult();
                        cpuTemp = sample.CpuTemperatureC;
                        gpuTemp = sample.GpuTemperatureC;
                    }
                }
                catch (Exception ex)
                {
                    // If ReadSampleAsync fails, temps stay at 0 — log for diagnostics
                    try { AppHost.Logging?.Debug($"[ThermalSensorProvider] ReadSampleAsync failed: {ex.Message}"); } catch { }
                    TryReuseLastGoodTemperatures(ref cpuTemp, ref gpuTemp);
                }
            }
            // Last resort: raw WMI BIOS (integer-only, no ACPI overlay)
            else if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                var temps = _wmiBios.GetBothTemperatures();
                if (temps.HasValue)
                {
                    var (cpu, gpu) = temps.Value;
                    cpuTemp = cpu;
                    gpuTemp = gpu;
                }
            }

            RememberLastGoodTemperatures(cpuTemp, gpuTemp);

            if (cpuTemp > 0)
            {
                list.Add(new TemperatureReading { Sensor = "CPU Package", Celsius = cpuTemp });
            }

            if (gpuTemp > 0)
            {
                list.Add(new TemperatureReading { Sensor = "GPU", Celsius = gpuTemp });
            }

            // Fallback if no data available
            if (list.Count == 0)
            {
                list.Add(new TemperatureReading { Sensor = "CPU", Celsius = 0 });
                list.Add(new TemperatureReading { Sensor = "GPU", Celsius = 0 });
            }

            return list;
        }

        private void RememberLastGoodTemperatures(double cpuTemp, double gpuTemp)
        {
            if (cpuTemp > 0)
            {
                _lastGoodCpuTemp = cpuTemp;
                _lastGoodSampleUtc = DateTime.UtcNow;
            }

            if (gpuTemp > 0)
            {
                _lastGoodGpuTemp = gpuTemp;
                _lastGoodSampleUtc = DateTime.UtcNow;
            }
        }

        private void TryReuseLastGoodTemperatures(ref double cpuTemp, ref double gpuTemp)
        {
            if (_lastGoodSampleUtc == DateTime.MinValue)
            {
                return;
            }

            var ageSeconds = (DateTime.UtcNow - _lastGoodSampleUtc).TotalSeconds;
            if (ageSeconds > LastGoodTemperatureReuseSeconds)
            {
                return;
            }

            if (cpuTemp <= 0 && _lastGoodCpuTemp > 0)
            {
                cpuTemp = _lastGoodCpuTemp;
            }

            if (gpuTemp <= 0 && _lastGoodGpuTemp > 0)
            {
                gpuTemp = _lastGoodGpuTemp;
            }
        }
    }
}
