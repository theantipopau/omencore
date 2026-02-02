using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Hardware monitoring service with performance enhancements and dashboard support.
    /// Features: Object pooling, change detection, adaptive polling, low-overhead mode, power monitoring, alerts.
    /// </summary>
    public class HardwareMonitoringService : IHardwareMonitoringService, IDisposable
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

        // New fields for dashboard functionality
        private readonly List<HardwareMetrics> _metricsHistory = new();
        private readonly List<SystemAlert> _activeAlerts = new();
        private readonly object _dashboardLock = new();
        private HardwareMetrics? _lastMetrics;
        private const int ReadSampleTimeoutMs = 10000; // 10 second timeout for sample reads
        private int _consecutiveTimeouts = 0; // Track consecutive timeouts for diagnostics
        
        // Monitoring health tracking (v2.7.0)
        private DateTime _lastSuccessfulSampleTime = DateTime.MinValue;
        private volatile MonitoringHealthStatus _healthStatus = MonitoringHealthStatus.Unknown;
        private const int StaleSampleThresholdSeconds = 15;  // Mark stale after 15s without update
        private const int DegradedTimeoutThreshold = 2;      // Mark degraded after 2 consecutive timeouts

        public ReadOnlyObservableCollection<MonitoringSample> Samples { get; }
        public event EventHandler<MonitoringSample>? SampleUpdated;
        
        /// <summary>
        /// Event fired when monitoring health status changes.
        /// </summary>
        public event EventHandler<MonitoringHealthStatus>? HealthStatusChanged;

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
        
        /// <summary>
        /// Current monitoring health status (Healthy, Degraded, or Stale).
        /// </summary>
        public MonitoringHealthStatus HealthStatus => _healthStatus;
        
        /// <summary>
        /// Time since last successful sensor reading.
        /// </summary>
        public TimeSpan LastSampleAge => _lastSuccessfulSampleTime == DateTime.MinValue 
            ? TimeSpan.MaxValue 
            : DateTime.Now - _lastSuccessfulSampleTime;
        
        /// <summary>
        /// Number of consecutive timeouts (useful for diagnostics).
        /// </summary>
        public int ConsecutiveTimeouts => _consecutiveTimeouts;

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
        /// Set MSR access for enhanced throttling detection on LibreHardwareMonitor.
        /// </summary>
        public void SetMsrAccess(IMsrAccess? msrAccess)
        {
            if (_bridge is LibreHardwareMonitorImpl lhwm)
            {
                lhwm.SetMsrAccess(msrAccess);
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
            int iteration = 0;
            var lastHeartbeat = DateTime.Now;
            const int HeartbeatIntervalSeconds = 60; // Log heartbeat every minute

            while (!token.IsCancellationRequested)
            {
                iteration++;
                
                // Periodic heartbeat to confirm loop is still running
                if ((DateTime.Now - lastHeartbeat).TotalSeconds >= HeartbeatIntervalSeconds)
                {
                    _logging.Info($"[MonitorLoop] ❤️ Heartbeat: iteration {iteration}, errors={consecutiveErrors}, timeouts={_consecutiveTimeouts}");
                    lastHeartbeat = DateTime.Now;
                }
                
                _logging.Debug($"[MonitorLoop] Iteration {iteration} starting...");
                
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
                    _logging.Debug($"[MonitorLoop] Iteration {iteration}: Reading sample from bridge...");
                    
                    // Add timeout to prevent infinite hangs in hardware monitoring
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    readCts.CancelAfter(ReadSampleTimeoutMs);
                    
                    MonitoringSample? sample;
                    try
                    {
                        sample = await _bridge.ReadSampleAsync(readCts.Token);
                        _consecutiveTimeouts = 0; // Reset on successful read
                        _lastSuccessfulSampleTime = DateTime.Now;
                        UpdateHealthStatus(MonitoringHealthStatus.Healthy);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Timeout occurred (not overall cancellation)
                        _consecutiveTimeouts++;
                        _logging.Warn($"[MonitorLoop] ReadSampleAsync timed out after {ReadSampleTimeoutMs}ms (consecutive: {_consecutiveTimeouts})");
                        
                        // Update health status based on consecutive timeouts
                        if (_consecutiveTimeouts >= DegradedTimeoutThreshold)
                        {
                            UpdateHealthStatus(MonitoringHealthStatus.Degraded);
                            _logging.Warn("[MonitorLoop] Multiple consecutive timeouts - hardware monitoring may be stuck");
                        }
                        
                        // Continue to next iteration instead of hanging
                        continue;
                    }
                    
                    consecutiveErrors = 0; // Reset error counter on success

                    _logging.Info($"MonitorLoop: Got sample - CPU: {sample.CpuTemperatureC}°C, GPU: {sample.GpuTemperatureC}°C, CPULoad: {sample.CpuLoadPercent}%, GPULoad: {sample.GpuLoadPercent}%, RAM: {sample.RamUsageGb}GB");

                    // Change detection optimization - only update UI if values changed significantly
                    var shouldUpdate = ShouldUpdateUI(sample);
                    _logging.Info($"MonitorLoop: ShouldUpdateUI={shouldUpdate}, lastSample null={_lastSample == null}");
                    
                    if (shouldUpdate)
                    {
                        _logging.Info("MonitorLoop: ShouldUpdateUI returned true, updating dashboard metrics");

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

                        // Update dashboard metrics for the new monitoring dashboard
                        UpdateDashboardMetrics(sample);

                        SampleUpdated?.Invoke(this, sample);
                        _lastSample = sample;
                    }
                    else
                    {
                        _logging.Info("MonitorLoop: ShouldUpdateUI returned false, skipping update");
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

        // IHardwareMonitoringService implementation
        public async Task<HardwareMetrics> GetCurrentMetricsAsync()
        {
            lock (_dashboardLock)
            {
                if (_lastMetrics == null)
                {
                    _logging.Info("GetCurrentMetricsAsync: _lastMetrics is null, returning default");
                    return new HardwareMetrics { Timestamp = DateTime.Now };
                }
                
                _logging.Info($"GetCurrentMetricsAsync: Returning metrics - CPU: {_lastMetrics.CpuTemperature}°C, GPU: {_lastMetrics.GpuTemperature}°C, Power: {_lastMetrics.PowerConsumption}W");
                return _lastMetrics;
            }
        }

        public async Task<IEnumerable<SystemAlert>> GetActiveAlertsAsync()
        {
            lock (_dashboardLock)
            {
                return _activeAlerts.ToList();
            }
        }

        public async Task<IEnumerable<HistoricalDataPoint>> GetHistoricalDataAsync(ChartType chartType, TimeSpan timeRange)
        {
            var cutoffTime = DateTime.Now - timeRange;

            lock (_dashboardLock)
            {
                var realData = _metricsHistory
                    .Where(m => m.Timestamp >= cutoffTime)
                    .Select(m => new HistoricalDataPoint
                    {
                        Timestamp = m.Timestamp,
                        Value = GetValueForChartType(m, chartType),
                        Label = GetLabelForChartType(chartType)
                    })
                    .ToList();

                // v2.7.0: Return empty list instead of synthetic data to show proper empty state
                // Synthetic data can mask telemetry failures and confuse users
                return realData;
            }
        }

        /// <summary>
        /// Check if there is historical data available for charts.
        /// Use this to decide whether to show empty state UI.
        /// </summary>
        public bool HasHistoricalData()
        {
            lock (_dashboardLock)
            {
                return _metricsHistory.Count > 0;
            }
        }
        
        /// <summary>
        /// Check if there is historical data for a specific time range.
        /// </summary>
        public bool HasHistoricalData(TimeSpan timeRange)
        {
            lock (_dashboardLock)
            {
                var cutoffTime = DateTime.Now - timeRange;
                return _metricsHistory.Any(m => m.Timestamp >= cutoffTime);
            }
        }

        // REMOVED: GenerateSampleData method - replaced with empty state pattern
        // Synthetic data masks telemetry failures and confuses users

        public Task<IEnumerable<HardwareSensorReading>> GetAllSensorDataAsync()
        {
            var sample = _lastSample;
            if (sample == null)
            {
                return Task.FromResult<IEnumerable<HardwareSensorReading>>(Array.Empty<HardwareSensorReading>());
            }

            var readings = new List<HardwareSensorReading>
            {
                new()
                {
                    Name = "CPU Temperature",
                    Type = "Temperature",
                    Value = sample.CpuTemperatureC,
                    Unit = "°C",
                    MinValue = sample.CpuTemperatureC,
                    MaxValue = sample.CpuTemperatureC
                },
                new()
                {
                    Name = "GPU Temperature",
                    Type = "Temperature",
                    Value = sample.GpuTemperatureC,
                    Unit = "°C",
                    MinValue = sample.GpuTemperatureC,
                    MaxValue = sample.GpuTemperatureC
                },
                new()
                {
                    Name = "CPU Load",
                    Type = "Load",
                    Value = sample.CpuLoadPercent,
                    Unit = "%",
                    MinValue = 0,
                    MaxValue = 100
                },
                new()
                {
                    Name = "GPU Load",
                    Type = "Load",
                    Value = sample.GpuLoadPercent,
                    Unit = "%",
                    MinValue = 0,
                    MaxValue = 100
                },
                new()
                {
                    Name = "System Fan Speed",
                    Type = "Speed",
                    Value = sample.FanRpm,
                    Unit = "RPM",
                    MinValue = 0,
                    MaxValue = sample.FanRpm
                },
                new()
                {
                    Name = "Battery",
                    Type = "Charge",
                    Value = sample.BatteryChargePercent,
                    Unit = "%",
                    MinValue = 0,
                    MaxValue = 100
                }
            };

            return Task.FromResult<IEnumerable<HardwareSensorReading>>(readings);
        }

        public async Task<string> ExportMonitoringDataAsync()
        {
            var exportData = new
            {
                ExportTime = DateTime.Now,
                CurrentMetrics = _lastMetrics,
                HistoricalData = _metricsHistory.TakeLast(100), // Last 100 data points
                ActiveAlerts = _activeAlerts
            };

            return System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public async Task StartMonitoringAsync()
        {
            if (_cts != null) return;
            Start();
        }

        public async Task StopMonitoringAsync()
        {
            Stop();
        }

        public bool IsMonitoring => _cts != null;

        private void UpdateDashboardMetrics(MonitoringSample sample)
        {
            var metrics = new HardwareMetrics
            {
                Timestamp = DateTime.Now,
                CpuTemperature = sample.CpuTemperatureC,
                GpuTemperature = sample.GpuTemperatureC,
                // Note: Power consumption, battery health, and other metrics would need
                // additional hardware sensors or calculations based on available data
                PowerConsumption = CalculateEstimatedPowerConsumption(sample),
                BatteryHealthPercentage = 100, // Placeholder - would need battery sensor
                BatteryCycles = 0, // Placeholder
                EstimatedBatteryLifeYears = 3.0, // Placeholder
                PowerEfficiency = CalculatePowerEfficiency(sample),
                FanEfficiency = 70.0 // Placeholder - would need fan speed data
            };

            _logging.Info($"UpdateDashboardMetrics: Created metrics - CPU: {metrics.CpuTemperature}°C, GPU: {metrics.GpuTemperature}°C, Power: {metrics.PowerConsumption}W");

            // Calculate trend
            if (_metricsHistory.Count >= 2)
            {
                var recentMetrics = _metricsHistory.TakeLast(5).ToList();
                var avgPower = recentMetrics.Average(m => m.PowerConsumption);
                metrics.PowerConsumptionTrend = metrics.PowerConsumption - avgPower;
            }

            lock (_dashboardLock)
            {
                _lastMetrics = metrics;
                _metricsHistory.Add(metrics);

                // Keep only last 24 hours of data
                var cutoffTime = DateTime.Now.AddHours(-24);
                _metricsHistory.RemoveAll(m => m.Timestamp < cutoffTime);

                // Check for alerts
                CheckForAlerts(metrics);
            }

            _logging.Info("UpdateDashboardMetrics: _lastMetrics updated successfully");
        }

        private double CalculateEstimatedPowerConsumption(MonitoringSample sample)
        {
            // Rough estimation based on CPU/GPU load and temperatures
            // In a real implementation, this would use actual power sensors
            double basePower = 30; // Base system power
            double cpuPower = (sample.CpuLoadPercent / 100.0) * 45; // CPU power contribution
            double gpuPower = (sample.GpuLoadPercent / 100.0) * 60; // GPU power contribution
            double tempMultiplier = 1 + ((sample.CpuTemperatureC + sample.GpuTemperatureC) / 2 - 50) * 0.005; // Temperature efficiency loss

            return (basePower + cpuPower + gpuPower) * tempMultiplier;
        }

        private double CalculatePowerEfficiency(MonitoringSample sample)
        {
            // Simplified efficiency calculation
            double loadFactor = (sample.CpuLoadPercent + sample.GpuLoadPercent) / 200.0; // Average load 0-1
            double tempEfficiency = Math.Max(0.5, 1 - ((sample.CpuTemperatureC + sample.GpuTemperatureC) / 2 - 50) / 100.0);

            return Math.Min(100, (loadFactor * tempEfficiency * 100));
        }

        private void CheckForAlerts(HardwareMetrics metrics)
        {
            _activeAlerts.Clear();

            // Temperature alerts
            if (metrics.CpuTemperature > 90)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "🔥",
                    Title = "High CPU Temperature",
                    Message = $"CPU temperature is {metrics.CpuTemperature:F1}°C. Consider improving cooling.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Critical
                });
            }
            else if (metrics.CpuTemperature > 80)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "⚠️",
                    Title = "Elevated CPU Temperature",
                    Message = $"CPU temperature is {metrics.CpuTemperature:F1}°C.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Warning
                });
            }

            if (metrics.GpuTemperature > 85)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "🔥",
                    Title = "High GPU Temperature",
                    Message = $"GPU temperature is {metrics.GpuTemperature:F1}°C. Consider improving cooling.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Critical
                });
            }
            else if (metrics.GpuTemperature > 75)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "⚠️",
                    Title = "Elevated GPU Temperature",
                    Message = $"GPU temperature is {metrics.GpuTemperature:F1}°C.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Warning
                });
            }

            // Power consumption alerts
            if (metrics.PowerConsumption > 120)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "⚡",
                    Title = "High Power Consumption",
                    Message = $"System is drawing {metrics.PowerConsumption:F1}W. Consider power management settings.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Warning
                });
            }

            // Battery health alerts
            if (metrics.BatteryHealthPercentage < 70)
            {
                _activeAlerts.Add(new SystemAlert
                {
                    Icon = "🔋",
                    Title = "Battery Health Warning",
                    Message = $"Battery health is at {metrics.BatteryHealthPercentage:F1}%. Consider battery calibration.",
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = AlertSeverity.Warning
                });
            }
        }

        private double GetValueForChartType(HardwareMetrics metrics, ChartType chartType)
        {
            return chartType switch
            {
                ChartType.PowerConsumption => metrics.PowerConsumption,
                ChartType.BatteryHealth => metrics.BatteryHealthPercentage,
                ChartType.Temperature => (metrics.CpuTemperature + metrics.GpuTemperature) / 2,
                ChartType.FanSpeeds => metrics.FanEfficiency, // Placeholder - would be actual fan speed
                _ => 0
            };
        }

        private string GetLabelForChartType(ChartType chartType)
        {
            return chartType switch
            {
                ChartType.PowerConsumption => "Watts",
                ChartType.BatteryHealth => "Percentage",
                ChartType.Temperature => "°C",
                ChartType.FanSpeeds => "RPM",
                _ => ""
            };
        }

        public void Dispose()
        {
            Stop();
            if (_bridge is IDisposable disposableBridge)
            {
                disposableBridge.Dispose();
            }
        }
        
        /// <summary>
        /// Update the monitoring health status and fire event if changed.
        /// Also checks for stale data based on last sample time.
        /// </summary>
        private void UpdateHealthStatus(MonitoringHealthStatus newStatus)
        {
            // Check for stale data (takes precedence over other statuses)
            if (_lastSuccessfulSampleTime != DateTime.MinValue && 
                (DateTime.Now - _lastSuccessfulSampleTime).TotalSeconds > StaleSampleThresholdSeconds)
            {
                newStatus = MonitoringHealthStatus.Stale;
            }
            
            if (_healthStatus != newStatus)
            {
                var oldStatus = _healthStatus;
                _healthStatus = newStatus;
                _logging.Info($"Monitoring health status changed: {oldStatus} → {newStatus}");
                
                try
                {
                    HealthStatusChanged?.Invoke(this, newStatus);
                }
                catch (Exception ex)
                {
                    _logging.Error($"Error invoking HealthStatusChanged: {ex.Message}", ex);
                }
            }
        }
    }
    
    /// <summary>
    /// Monitoring health status for the hardware monitoring service.
    /// </summary>
    public enum MonitoringHealthStatus
    {
        /// <summary>Status not yet determined.</summary>
        Unknown,
        
        /// <summary>Monitoring is working normally with fresh data.</summary>
        Healthy,
        
        /// <summary>Experiencing timeouts or partial failures but still functional.</summary>
        Degraded,
        
        /// <summary>Data is stale - no successful reads for extended period.</summary>
        Stale
    }

    public class HardwareSensorReading
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Value { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public string Unit { get; set; } = string.Empty;
    }
}
