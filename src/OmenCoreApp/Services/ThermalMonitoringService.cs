using System;
using System.Collections.Generic;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for monitoring thermal thresholds and triggering alerts.
    /// </summary>
    public class ThermalMonitoringService
    {
        private readonly LoggingService _logging;
        private readonly NotificationService _notificationService;
        
        // Threshold settings
        public double CpuWarningThreshold { get; set; } = 85;
        public double CpuCriticalThreshold { get; set; } = 95;
        public double GpuWarningThreshold { get; set; } = 85;
        public double GpuCriticalThreshold { get; set; } = 95;
        public double GpuHotspotWarningThreshold { get; set; } = 100;
        public double SsdWarningThreshold { get; set; } = 70;
        
        // Cooldown to prevent notification spam
        private readonly Dictionary<string, DateTime> _lastAlertTime = new();
        private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(5);

        // Require short persistence above threshold to ignore one-off sensor spikes.
        private const int ConsecutiveReadingsForAlert = 2;
        private int _cpuWarningConsecutive;
        private int _cpuCriticalConsecutive;
        private int _gpuWarningConsecutive;
        private int _gpuCriticalConsecutive;
        private int _gpuHotspotConsecutive;
        private int _ssdWarningConsecutive;

        // Sanity limits to suppress bogus telemetry (for example 107-108C sensor glitches).
        private const double MaxReasonableCpuTempC = 105;
        private const double MaxReasonableGpuTempC = 105;
        private const double MaxReasonableGpuHotspotTempC = 115;
        private const double MaxReasonableSsdTempC = 95;
        
        // State tracking
        private bool _cpuWarningActive;
        private bool _cpuCriticalActive;
        private bool _gpuWarningActive;
        private bool _gpuCriticalActive;
        
        public bool IsEnabled { get; set; } = true;

        public ThermalMonitoringService(LoggingService logging, NotificationService notificationService)
        {
            _logging = logging;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Process a monitoring sample and check thermal thresholds
        /// </summary>
        public void ProcessSample(MonitoringSample sample)
        {
            if (!IsEnabled) return;

            // Check CPU temperature
            CheckCpuTemperature(sample.CpuTemperatureC, sample.CpuTemperatureState);
            
            // Check GPU temperature
            CheckGpuTemperature(sample.GpuTemperatureC, sample.GpuTemperatureState);
            
            // Check GPU hotspot if available
            if (sample.GpuHotspotTemperatureC > 0)
            {
                CheckGpuHotspot(sample.GpuHotspotTemperatureC);
            }
            
            // Check SSD temperature
            if (sample.SsdTemperatureC > 0)
            {
                CheckSsdTemperature(sample.SsdTemperatureC);
            }
        }

        private static bool IsUsableState(TelemetryDataState state)
        {
            return state == TelemetryDataState.Valid || state == TelemetryDataState.Unknown;
        }

        private static bool IsPlausibleTemp(double temp, double maxTempC)
        {
            return !double.IsNaN(temp) && !double.IsInfinity(temp) && temp > 0 && temp <= maxTempC;
        }

        private void CheckCpuTemperature(double temp, TelemetryDataState state)
        {
            if (!IsUsableState(state) || !IsPlausibleTemp(temp, MaxReasonableCpuTempC))
            {
                _cpuWarningConsecutive = 0;
                _cpuCriticalConsecutive = 0;
                return;
            }

            if (temp >= CpuCriticalThreshold)
            {
                _cpuCriticalConsecutive++;
                _cpuWarningConsecutive = 0;
                if (_cpuCriticalConsecutive < ConsecutiveReadingsForAlert)
                {
                    return;
                }

                if (!_cpuCriticalActive && CanShowAlert("cpu_critical"))
                {
                    _notificationService.ShowCriticalTemperature("CPU", temp);
                    _cpuCriticalActive = true;
                    RecordAlert("cpu_critical");
                }
            }
            else if (temp >= CpuWarningThreshold)
            {
                _cpuWarningConsecutive++;
                _cpuCriticalConsecutive = 0;
                if (_cpuWarningConsecutive < ConsecutiveReadingsForAlert)
                {
                    return;
                }

                _cpuCriticalActive = false;
                if (!_cpuWarningActive && CanShowAlert("cpu_warning"))
                {
                    _notificationService.ShowTemperatureWarning("CPU", temp, CpuWarningThreshold);
                    _cpuWarningActive = true;
                    RecordAlert("cpu_warning");
                }
            }
            else
            {
                // Temperature is normal - reset alerts
                if (_cpuWarningActive || _cpuCriticalActive)
                {
                    _logging.Info($"CPU temperature returned to normal: {temp:F0}°C");
                }
                _cpuWarningConsecutive = 0;
                _cpuCriticalConsecutive = 0;
                _cpuWarningActive = false;
                _cpuCriticalActive = false;
            }
        }

        private void CheckGpuTemperature(double temp, TelemetryDataState state)
        {
            if (!IsUsableState(state) || !IsPlausibleTemp(temp, MaxReasonableGpuTempC))
            {
                _gpuWarningConsecutive = 0;
                _gpuCriticalConsecutive = 0;
                return;
            }

            if (temp >= GpuCriticalThreshold)
            {
                _gpuCriticalConsecutive++;
                _gpuWarningConsecutive = 0;
                if (_gpuCriticalConsecutive < ConsecutiveReadingsForAlert)
                {
                    return;
                }

                if (!_gpuCriticalActive && CanShowAlert("gpu_critical"))
                {
                    _notificationService.ShowCriticalTemperature("GPU", temp);
                    _gpuCriticalActive = true;
                    RecordAlert("gpu_critical");
                }
            }
            else if (temp >= GpuWarningThreshold)
            {
                _gpuWarningConsecutive++;
                _gpuCriticalConsecutive = 0;
                if (_gpuWarningConsecutive < ConsecutiveReadingsForAlert)
                {
                    return;
                }

                _gpuCriticalActive = false;
                if (!_gpuWarningActive && CanShowAlert("gpu_warning"))
                {
                    _notificationService.ShowTemperatureWarning("GPU", temp, GpuWarningThreshold);
                    _gpuWarningActive = true;
                    RecordAlert("gpu_warning");
                }
            }
            else
            {
                if (_gpuWarningActive || _gpuCriticalActive)
                {
                    _logging.Info($"GPU temperature returned to normal: {temp:F0}°C");
                }
                _gpuWarningConsecutive = 0;
                _gpuCriticalConsecutive = 0;
                _gpuWarningActive = false;
                _gpuCriticalActive = false;
            }
        }

        private void CheckGpuHotspot(double temp)
        {
            if (!IsPlausibleTemp(temp, MaxReasonableGpuHotspotTempC))
            {
                _gpuHotspotConsecutive = 0;
                return;
            }

            if (temp >= GpuHotspotWarningThreshold)
            {
                _gpuHotspotConsecutive++;
                if (_gpuHotspotConsecutive >= ConsecutiveReadingsForAlert && CanShowAlert("gpu_hotspot"))
                {
                    _notificationService.ShowTemperatureWarning("GPU Hotspot", temp, GpuHotspotWarningThreshold);
                    RecordAlert("gpu_hotspot");
                }
            }
            else
            {
                _gpuHotspotConsecutive = 0;
            }
        }

        private void CheckSsdTemperature(double temp)
        {
            if (!IsPlausibleTemp(temp, MaxReasonableSsdTempC))
            {
                _ssdWarningConsecutive = 0;
                return;
            }

            if (temp >= SsdWarningThreshold)
            {
                _ssdWarningConsecutive++;
                if (_ssdWarningConsecutive >= ConsecutiveReadingsForAlert && CanShowAlert("ssd_warning"))
                {
                    _notificationService.ShowTemperatureWarning("SSD", temp, SsdWarningThreshold);
                    RecordAlert("ssd_warning");
                }
            }
            else
            {
                _ssdWarningConsecutive = 0;
            }
        }

        private bool CanShowAlert(string alertKey)
        {
            if (!_lastAlertTime.TryGetValue(alertKey, out var lastTime))
            {
                return true;
            }
            
            return DateTime.Now - lastTime > _alertCooldown;
        }

        private void RecordAlert(string alertKey)
        {
            _lastAlertTime[alertKey] = DateTime.Now;
        }

        /// <summary>
        /// Reset all alert cooldowns
        /// </summary>
        public void ResetAlerts()
        {
            _lastAlertTime.Clear();
            _cpuWarningActive = false;
            _cpuCriticalActive = false;
            _gpuWarningActive = false;
            _gpuCriticalActive = false;
            _cpuWarningConsecutive = 0;
            _cpuCriticalConsecutive = 0;
            _gpuWarningConsecutive = 0;
            _gpuCriticalConsecutive = 0;
            _gpuHotspotConsecutive = 0;
            _ssdWarningConsecutive = 0;
            _logging.Info("Thermal alerts reset");
        }

        /// <summary>
        /// Update threshold settings
        /// </summary>
        public void UpdateThresholds(double cpuWarn, double cpuCrit, double gpuWarn, double gpuCrit)
        {
            CpuWarningThreshold = cpuWarn;
            CpuCriticalThreshold = cpuCrit;
            GpuWarningThreshold = gpuWarn;
            GpuCriticalThreshold = gpuCrit;
            _logging.Info($"Thermal thresholds updated: CPU {cpuWarn}/{cpuCrit}°C, GPU {gpuWarn}/{gpuCrit}°C");
        }
    }
}
