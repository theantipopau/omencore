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
            CheckCpuTemperature(sample.CpuTemperatureC);
            
            // Check GPU temperature
            CheckGpuTemperature(sample.GpuTemperatureC);
            
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

        private void CheckCpuTemperature(double temp)
        {
            if (temp >= CpuCriticalThreshold)
            {
                if (!_cpuCriticalActive && CanShowAlert("cpu_critical"))
                {
                    _notificationService.ShowCriticalTemperature("CPU", temp);
                    _cpuCriticalActive = true;
                    RecordAlert("cpu_critical");
                }
            }
            else if (temp >= CpuWarningThreshold)
            {
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
                _cpuWarningActive = false;
                _cpuCriticalActive = false;
            }
        }

        private void CheckGpuTemperature(double temp)
        {
            if (temp >= GpuCriticalThreshold)
            {
                if (!_gpuCriticalActive && CanShowAlert("gpu_critical"))
                {
                    _notificationService.ShowCriticalTemperature("GPU", temp);
                    _gpuCriticalActive = true;
                    RecordAlert("gpu_critical");
                }
            }
            else if (temp >= GpuWarningThreshold)
            {
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
                _gpuWarningActive = false;
                _gpuCriticalActive = false;
            }
        }

        private void CheckGpuHotspot(double temp)
        {
            if (temp >= GpuHotspotWarningThreshold && CanShowAlert("gpu_hotspot"))
            {
                _notificationService.ShowTemperatureWarning("GPU Hotspot", temp, GpuHotspotWarningThreshold);
                RecordAlert("gpu_hotspot");
            }
        }

        private void CheckSsdTemperature(double temp)
        {
            if (temp >= SsdWarningThreshold && CanShowAlert("ssd_warning"))
            {
                _notificationService.ShowTemperatureWarning("SSD", temp, SsdWarningThreshold);
                RecordAlert("ssd_warning");
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
