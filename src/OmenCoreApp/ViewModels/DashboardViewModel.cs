using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class DashboardViewModel : ViewModelBase, IDisposable
    {
        /// <summary>Maximum number of thermal samples to keep (30 minutes at 1 sample/second).</summary>
        private const int MaxThermalSampleHistory = 1800;
        private static readonly TimeSpan UiProjectionMinInterval = TimeSpan.FromMilliseconds(1000);
        private const double UiProjectionTempDelta = 0.5;
        private const double UiProjectionLoadDelta = 2.0;
        private const double UiProjectionPowerDelta = 1.0;
        private const int UiProjectionFanRpmDelta = 120;
        private const string CelsiusSuffix = "\u00B0C";
        private const string MissingTemperatureDisplay = "--\u00B0C";
        private const double MaxPlausibleDashboardTempC = 105;
        private const double SuspectBiosSentinelTempC = 100;
        private const double SuspectBiosSentinelDeltaC = 25;

        /// <summary>Time range options in minutes for the graph time-range selector.</summary>
        public static readonly int[] TimeRangeOptions = { 1, 5, 15, 30 };
        
        private readonly HardwareMonitoringService _monitoringService;
        private readonly FanService? _fanService;
        private readonly RuntimePollingCoordinator? _pollingCoordinator;
        private readonly HashSet<FanTelemetry> _subscribedFanTelemetry = new();
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private readonly ObservableCollection<ThermalSample> _filteredThermalSamples = new();
        private int _timeRangeMinutes = 5;
        private MonitoringSample? _latestMonitoringSample;
        private bool _monitoringLowOverhead;
        private string _currentPerformanceMode = "Auto";
        private string _currentFanMode = "Auto";
        private string _modeLinkStatus = "Decoupled fan/perf";
        private bool _disposed;
        private volatile bool _pendingUIUpdate; // Throttle BeginInvoke backlog
        private volatile bool _pendingFanTelemetryUIUpdate;
        private bool _telemetryProjectionEnabled = true;
        private readonly object _sampleUpdateLock = new();
        private MonitoringSample? _queuedSample;
        private MonitoringSample? _lastUiProjectedSample;
        private DateTime _lastUiProjectionUtc = DateTime.MinValue;
        
        // Session tracking (v2.2)
        private readonly DateTime _sessionStartTime = DateTime.Now;
        private double _peakCpuTemp;
        private double _peakGpuTemp;
        private DispatcherTimer? _uptimeTimer;

        public ReadOnlyObservableCollection<MonitoringSample> MonitoringSamples => _monitoringService.Samples;
        public ObservableCollection<ThermalSample> ThermalSamples => _thermalSamples;

        /// <summary>Filtered subset of ThermalSamples matching the selected time range.</summary>
        public ObservableCollection<ThermalSample> FilteredThermalSamples => _filteredThermalSamples;

        /// <summary>Current time-range window in minutes (1, 5, 15 or 30).</summary>
        public int TimeRangeMinutes
        {
            get => _timeRangeMinutes;
            set
            {
                if (_timeRangeMinutes != value)
                {
                    _timeRangeMinutes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTimeRange1m));
                    OnPropertyChanged(nameof(IsTimeRange5m));
                    OnPropertyChanged(nameof(IsTimeRange15m));
                    OnPropertyChanged(nameof(IsTimeRange30m));
                    RebuildFilteredSamples();
                }
            }
        }

        public bool IsTimeRange1m  { get => _timeRangeMinutes == 1;  set { if (value) TimeRangeMinutes = 1;  } }
        public bool IsTimeRange5m  { get => _timeRangeMinutes == 5;  set { if (value) TimeRangeMinutes = 5;  } }
        public bool IsTimeRange15m { get => _timeRangeMinutes == 15; set { if (value) TimeRangeMinutes = 15; } }
        public bool IsTimeRange30m { get => _timeRangeMinutes == 30; set { if (value) TimeRangeMinutes = 30; } }

        private void RebuildFilteredSamples()
        {
            _filteredThermalSamples.Clear();
            var cutoff = DateTime.Now - TimeSpan.FromMinutes(_timeRangeMinutes);
            foreach (var s in _thermalSamples)
            {
                if (s.Timestamp >= cutoff)
                    _filteredThermalSamples.Add(s);
            }
        }

        private void AddFilteredSample(ThermalSample sample)
        {
            var cutoff = DateTime.Now - TimeSpan.FromMinutes(_timeRangeMinutes);
            while (_filteredThermalSamples.Count > 0 && _filteredThermalSamples[0].Timestamp < cutoff)
            {
                _filteredThermalSamples.RemoveAt(0);
            }

            if (sample.Timestamp >= cutoff)
            {
                _filteredThermalSamples.Add(sample);
            }

            while (_filteredThermalSamples.Count > MaxThermalSampleHistory)
            {
                _filteredThermalSamples.RemoveAt(0);
            }
        }
        
        /// <summary>
        /// Whether there is historical chart data available.
        /// Used to show/hide empty state panels (v2.7.0).
        /// </summary>
        public bool HasHistoricalData => _monitoringService.HasHistoricalData();
        
        /// <summary>
        /// Whether there is live sensor data available.
        /// </summary>
        public bool HasLiveData => _latestMonitoringSample != null;
        
        public string CurrentPerformanceMode
        {
            get => _currentPerformanceMode;
            set
            {
                if (_currentPerformanceMode != value)
                {
                    _currentPerformanceMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PerformanceVisualState));
                }
            }
        }
        
        public string CurrentFanMode
        {
            get => _currentFanMode;
            set
            {
                if (_currentFanMode != value)
                {
                    _currentFanMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FanVisualState));
                }
            }
        }

        public string ModeLinkStatus
        {
            get => _modeLinkStatus;
            set
            {
                if (_modeLinkStatus != value)
                {
                    _modeLinkStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public MonitoringSample? LatestMonitoringSample
        {
            get => _latestMonitoringSample;
            private set
            {
                _latestMonitoringSample = value;
                
                // Track peak temperatures (v2.2)
                if (value != null)
                {
                    if (value.CpuTemperatureC > _peakCpuTemp)
                    {
                        _peakCpuTemp = value.CpuTemperatureC;
                        OnPropertyChanged(nameof(PeakCpuTemp));
                    }
                    if (value.GpuTemperatureC > _peakGpuTemp)
                    {
                        _peakGpuTemp = value.GpuTemperatureC;
                        OnPropertyChanged(nameof(PeakGpuTemp));
                    }
                }
                
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuTemperature));
                OnPropertyChanged(nameof(GpuTemperature));
                OnPropertyChanged(nameof(CpuTempDisplay));
                OnPropertyChanged(nameof(GpuTempDisplay));
                OnPropertyChanged(nameof(CpuTempChipDisplay));
                OnPropertyChanged(nameof(GpuTempChipDisplay));
                OnPropertyChanged(nameof(SsdTempDisplay));
                OnPropertyChanged(nameof(IsCpuTempAvailable));
                OnPropertyChanged(nameof(IsGpuTempAvailable));
                OnPropertyChanged(nameof(CpuSummary));
                OnPropertyChanged(nameof(GpuSummary));
                OnPropertyChanged(nameof(MemorySummary));
                OnPropertyChanged(nameof(StorageSummary));
                OnPropertyChanged(nameof(CpuSummaryDisplay));
                OnPropertyChanged(nameof(GpuSummaryDisplay));
                OnPropertyChanged(nameof(StorageSummaryDisplay));
                OnPropertyChanged(nameof(BatterySummary));
                OnPropertyChanged(nameof(CpuClockSummary));
                OnPropertyChanged(nameof(ThrottlingSummary));
                OnPropertyChanged(nameof(IsThrottling));
                OnPropertyChanged(nameof(IsSsdDataAvailable));
                OnPropertyChanged(nameof(FanSummary));
                OnPropertyChanged(nameof(FanCurveSummary));
                OnPropertyChanged(nameof(FanCurveChipDisplay));
                OnPropertyChanged(nameof(CpuFanDisplay));
                OnPropertyChanged(nameof(GpuFanDisplay));
                OnPropertyChanged(nameof(FanVisualState));
                OnPropertyChanged(nameof(PowerVisualState));
                OnPropertyChanged(nameof(PowerTotalDisplay));
                OnPropertyChanged(nameof(HasAmdGpuTelemetryQuarantineWarning));
                OnPropertyChanged(nameof(AmdGpuTelemetryQuarantineWarningText));
            }
        }

        public bool MonitoringLowOverheadMode
        {
            get => _monitoringLowOverhead;
            set
            {
                if (_monitoringLowOverhead != value)
                {
                    _monitoringLowOverhead = value;
                    if (_pollingCoordinator != null)
                    {
                        _pollingCoordinator.SetLowOverheadMode(value);
                    }
                    else
                    {
                        _monitoringService.SetLowOverheadMode(value);
                    }
                    if (value)
                    {
                        ClearHistoricalTelemetryBuffers();
                    }
                    else
                    {
                        RebuildHistoricalTelemetryBuffers();
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MonitoringGraphsVisible));
                }
            }
        }

        public bool MonitoringGraphsVisible => !MonitoringLowOverheadMode;

        public void SetTelemetryProjectionEnabled(bool enabled)
        {
            MonitoringSample? resumeSample = null;
            var changed = false;

            lock (_sampleUpdateLock)
            {
                if (_telemetryProjectionEnabled == enabled)
                {
                    return;
                }

                _telemetryProjectionEnabled = enabled;
                changed = true;
                   // v3.6.2: Track dormancy activation for field validation
                   if (!enabled)
                   {
                       RuntimeUiPerformanceCounters.RecordDashboardDormancyActivation();
                   }

                if (enabled && _queuedSample != null && !_pendingUIUpdate)
                {
                    _pendingUIUpdate = true;
                    resumeSample = _queuedSample;
                }
            }

            if (changed)
            {
                SetUptimeTimerEnabled(enabled);
            }

            if (resumeSample != null)
            {
                ScheduleUiProjection();
            }
        }

        private void ClearHistoricalTelemetryBuffers()
        {
            _thermalSamples.Clear();
            _filteredThermalSamples.Clear();
            _fanCurvePoints.Clear();
            OnPropertyChanged(nameof(RecentCpuTemps));
            OnPropertyChanged(nameof(RecentGpuTemps));
            OnPropertyChanged(nameof(HasSparklineData));
            OnPropertyChanged(nameof(HasHistoricalData));
            OnPropertyChanged(nameof(FanCurveSummary));
        }

        private void RebuildHistoricalTelemetryBuffers()
        {
            _thermalSamples.Clear();
            _filteredThermalSamples.Clear();
            _fanCurvePoints.Clear();

            foreach (var sample in _monitoringService.Samples.TakeLast(MaxThermalSampleHistory))
            {
                var thermalSample = new ThermalSample
                {
                    Timestamp = sample.Timestamp,
                    CpuCelsius = sample.CpuTemperatureC,
                    GpuCelsius = sample.GpuTemperatureC
                };

                _thermalSamples.Add(thermalSample);
                AddFilteredSample(thermalSample);
                UpdateFanCurvePoints(sample);
            }

            OnPropertyChanged(nameof(RecentCpuTemps));
            OnPropertyChanged(nameof(RecentGpuTemps));
            OnPropertyChanged(nameof(HasSparklineData));
            OnPropertyChanged(nameof(HasHistoricalData));
            OnPropertyChanged(nameof(FanCurveSummary));
        }

        #region Sparkline Data
        
        /// <summary>
        /// Recent CPU temperatures for sparkline display (last 20 samples).
        /// </summary>
        public System.Collections.Generic.IEnumerable<double> RecentCpuTemps => 
            _thermalSamples.TakeLast(20).Select(s => s.CpuCelsius);
        
        /// <summary>
        /// Recent GPU temperatures for sparkline display (last 20 samples).
        /// </summary>
        public System.Collections.Generic.IEnumerable<double> RecentGpuTemps => 
            _thermalSamples.TakeLast(20).Select(s => s.GpuCelsius);
        
        /// <summary>
        /// Recent RAM usage percentages for sparkline display.
        /// </summary>
        public System.Collections.Generic.IEnumerable<double> RecentRamUsage => 
            _monitoringService.Samples.TakeLast(20).Select(s => s.RamUsagePercent);
        
        /// <summary>
        /// Whether there's enough data for sparklines (at least 3 samples).
        /// </summary>
        public bool HasSparklineData => _thermalSamples.Count >= 3;
        
        #endregion

        /// <summary>
        /// Current CPU temperature for sidebar display binding.
        /// Returns 0 if no sample available (UI will show fallback).
        /// </summary>
        public double CpuTemperature => LatestMonitoringSample?.CpuTemperatureC ?? 0;
        
        /// <summary>
        /// Current GPU temperature for sidebar display binding.
        /// Returns 0 if no sample available (UI will show fallback).
        /// </summary>
        public double GpuTemperature => LatestMonitoringSample?.GpuTemperatureC ?? 0;
        
        /// <summary>
        /// Formatted CPU temperature string. Shows "—°C" when sensor data is unavailable (0°C).
        /// </summary>
        public string CpuTempDisplay => LatestMonitoringSample?.CpuTemperatureState switch
        {
            TelemetryDataState.Unavailable => "--°C",
            TelemetryDataState.Stale => CpuTemperature > 0 ? $"{CpuTemperature:F0}°C*" : "--°C",
            TelemetryDataState.Invalid => "--°C",
            _ => CpuTemperature > 0 ? $"{CpuTemperature:F0}°C" : "—°C"
        };
        
        /// <summary>
        /// Formatted GPU temperature string. Shows "—°C" when sensor data is unavailable (0°C).
        /// </summary>
        public string GpuTempDisplay => LatestMonitoringSample?.GpuTemperatureState switch
        {
            TelemetryDataState.Inactive => GpuTemperature > 0 ? $"{GpuTemperature:F0}°C" : "--°C",
            TelemetryDataState.Unavailable => "--°C",
            TelemetryDataState.Stale => GpuTemperature > 0 ? $"{GpuTemperature:F0}°C*" : "--°C",
            TelemetryDataState.Invalid => "--°C",
            _ => GpuTemperature > 0 ? $"{GpuTemperature:F0}°C" : "—°C"
        };
        
        /// <summary>
        /// Whether CPU temperature sensor data is available (non-zero reading).
        /// </summary>
        public bool IsCpuTempAvailable => CpuTemperature > 0;
        
        /// <summary>
        /// Whether GPU temperature sensor data is available (non-zero reading).
        /// </summary>
        public bool IsGpuTempAvailable => GpuTemperature > 0;

        public string CpuTempChipDisplay => FormatTemperatureForState(
            CpuTemperature,
            LatestMonitoringSample?.CpuTemperatureState ?? TelemetryDataState.Unknown);

        public string GpuTempChipDisplay => LatestMonitoringSample?.GpuTemperatureState == TelemetryDataState.Inactive
            ? "Idle"
            : FormatTemperatureForState(
                GpuTemperature,
                LatestMonitoringSample?.GpuTemperatureState ?? TelemetryDataState.Unknown);

        public string SsdTempDisplay => FormatTemperature(LatestMonitoringSample?.SsdTemperatureC ?? 0);

        public string CpuSummary => LatestMonitoringSample == null 
            ? "CPU telemetry unavailable" 
            : LatestMonitoringSample.CpuPowerWatts > 0 
                ? $"{(LatestMonitoringSample.CpuTemperatureC > 0 ? $"{LatestMonitoringSample.CpuTemperatureC:F0}°C" : "—°C")} • {LatestMonitoringSample.CpuLoadPercent:F0}% • {LatestMonitoringSample.CpuPowerWatts:F0}W"
                : $"{(LatestMonitoringSample.CpuTemperatureC > 0 ? $"{LatestMonitoringSample.CpuTemperatureC:F0}°C" : "—°C")} • {LatestMonitoringSample.CpuLoadPercent:F0}% load";
        public string GpuSummary => LatestMonitoringSample == null
            ? "GPU telemetry unavailable"
            : LatestMonitoringSample.GpuTemperatureState == TelemetryDataState.Inactive
                ? $"dGPU idle • {LatestMonitoringSample.GpuLoadPercent:F0}% load{(LatestMonitoringSample.GpuVramUsageMb > 0 ? $" • {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM" : string.Empty)}"
                : $"{(LatestMonitoringSample.GpuTemperatureC > 0 ? $"{LatestMonitoringSample.GpuTemperatureC:F0}°C" : "—°C")} • {LatestMonitoringSample.GpuLoadPercent:F0}% load{(LatestMonitoringSample.GpuVramUsageMb > 0 ? $" • {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM" : string.Empty)}";
        public string MemorySummary => LatestMonitoringSample == null ? "Memory telemetry unavailable" : $"{LatestMonitoringSample.RamUsageGb:F1} / {LatestMonitoringSample.RamTotalGb:F0} GB";
        public string StorageSummary => LatestMonitoringSample == null ? "Storage telemetry unavailable" 
            : LatestMonitoringSample.SsdTemperatureC > 0 
                ? $"SSD {LatestMonitoringSample.SsdTemperatureC:F0}°C • {LatestMonitoringSample.DiskUsagePercent:F0}% active"
                : $"SSD —°C • {LatestMonitoringSample.DiskUsagePercent:F0}% active";
        public string BatterySummary => LatestMonitoringSample == null ? "Battery unavailable" 
            : LatestMonitoringSample.IsOnAcPower 
                ? $"{LatestMonitoringSample.BatteryChargePercent:F0}% • AC Power" 
                : $"{LatestMonitoringSample.BatteryChargePercent:F0}% • {LatestMonitoringSample.BatteryTimeRemaining}";
        public string CpuSummaryDisplay => LatestMonitoringSample == null
            ? "CPU telemetry unavailable"
            : LatestMonitoringSample.CpuPowerWatts > 0
                ? $"{FormatTemperature(LatestMonitoringSample.CpuTemperatureC)} | {LatestMonitoringSample.CpuLoadPercent:F0}% | {LatestMonitoringSample.CpuPowerWatts:F0}W"
                : $"{FormatTemperature(LatestMonitoringSample.CpuTemperatureC)} | {LatestMonitoringSample.CpuLoadPercent:F0}% load";

        public string GpuSummaryDisplay => LatestMonitoringSample == null
            ? "GPU telemetry unavailable"
            : LatestMonitoringSample.GpuTemperatureState == TelemetryDataState.Inactive
                ? $"dGPU idle | {LatestMonitoringSample.GpuLoadPercent:F0}% load{(LatestMonitoringSample.GpuVramUsageMb > 0 ? $" | {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM" : string.Empty)}"
                : $"{FormatTemperature(LatestMonitoringSample.GpuTemperatureC)} | {LatestMonitoringSample.GpuLoadPercent:F0}% load{(LatestMonitoringSample.GpuVramUsageMb > 0 ? $" | {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM" : string.Empty)}";

        public string StorageSummaryDisplay => LatestMonitoringSample == null
            ? "Storage telemetry unavailable"
            : $"SSD {FormatTemperature(LatestMonitoringSample.SsdTemperatureC)} | {LatestMonitoringSample.DiskUsagePercent:F0}% active";

        private static string FormatTemperature(double temperature) =>
            temperature > 0 ? $"{temperature:F0}{CelsiusSuffix}" : MissingTemperatureDisplay;

        private static string FormatTemperatureForState(double temperature, TelemetryDataState state) =>
            state switch
            {
                TelemetryDataState.Unavailable => MissingTemperatureDisplay,
                TelemetryDataState.Invalid => MissingTemperatureDisplay,
                TelemetryDataState.Stale => temperature > 0 ? $"{temperature:F0}{CelsiusSuffix}*" : MissingTemperatureDisplay,
                _ => FormatTemperature(temperature)
            };

        public string CpuClockSummary => LatestMonitoringSample == null || LatestMonitoringSample.CpuCoreClocksMhz.Count == 0
            ? "Per-core clocks unavailable"
            : string.Join(", ", LatestMonitoringSample.CpuCoreClocksMhz.Select((c, i) => $"C{i + 1}:{c:F0}MHz"));
        
        /// <summary>
        /// Whether the system is currently throttling (thermal or power limited).
        /// </summary>
        public bool IsThrottling => LatestMonitoringSample?.IsThrottling ?? false;
        
        /// <summary>
        /// Human-readable throttling status for display.
        /// </summary>
        public string ThrottlingSummary => LatestMonitoringSample == null 
            ? "Unknown" 
            : LatestMonitoringSample.ThrottlingStatus;
        
        /// <summary>
        /// Whether SSD sensor data is available (non-zero temperature).
        /// Used to hide Storage card when LibreHardwareMonitor can't read SMART data.
        /// </summary>
        public bool IsSsdDataAvailable => LatestMonitoringSample?.IsSsdDataAvailable ?? false;
        
        // Power consumption and efficiency properties
        public string PowerConsumptionSummary => LatestMonitoringSample == null 
            ? "Power telemetry unavailable"
            : LatestMonitoringSample.GpuTemperatureState == TelemetryDataState.Inactive
                ? $"CPU: {LatestMonitoringSample.CpuPowerWatts:F0}W • GPU: inactive (Optimus)"
            : LatestMonitoringSample.CpuPowerWatts > 0 || LatestMonitoringSample.GpuPowerWatts > 0
                ? $"CPU: {LatestMonitoringSample.CpuPowerWatts:F0}W • GPU: {LatestMonitoringSample.GpuPowerWatts:F0}W • Total: {LatestMonitoringSample.CpuPowerWatts + LatestMonitoringSample.GpuPowerWatts:F0}W"
                : "Power sensors unavailable";

        public string PowerTotalDisplay
        {
            get
            {
                if (LatestMonitoringSample == null)
                    return "--W";

                var cpuWatts = Math.Max(0, LatestMonitoringSample.CpuPowerWatts);
                var gpuWatts = LatestMonitoringSample.GpuTemperatureState == TelemetryDataState.Inactive
                    ? 0
                    : Math.Max(0, LatestMonitoringSample.GpuPowerWatts);
                var totalWatts = cpuWatts + gpuWatts;

                return totalWatts > 0 ? $"{totalWatts:F0}W" : "--W";
            }
        }
        
        public string PowerEfficiencySummary
        {
            get
            {
                if (LatestMonitoringSample == null) return "Efficiency data unavailable";
                
                var totalPower = LatestMonitoringSample.CpuPowerWatts + LatestMonitoringSample.GpuPowerWatts;
                if (totalPower <= 0) return "Power data required for efficiency";
                
                // Calculate performance per watt (rough estimate)
                var cpuPerf = LatestMonitoringSample.CpuLoadPercent * LatestMonitoringSample.CpuCoreClocksMhz.Count;
                var gpuPerf = LatestMonitoringSample.GpuLoadPercent;
                var totalPerf = cpuPerf + gpuPerf;
                
                if (totalPerf <= 0) return "Performance data required";
                
                var efficiency = totalPerf / totalPower;
                return $"{efficiency:F1} perf/W • {totalPower:F0}W total";
            }
        }
        
        // Capacity-health summary. Charge-limit targets (for example 80%) are separate
        // from battery wear/capacity health, so do not infer this from charge percent.
        public string BatteryHealthSummary
        {
            get
            {
                if (LatestMonitoringSample == null || LatestMonitoringSample.BatteryChargePercent <= 0)
                    return "Battery capacity health unavailable";
                
                return "Capacity health unavailable";
            }
        }
        
        // Fan curve visualization data
        private readonly ObservableCollection<FanCurvePoint> _fanCurvePoints = new();
        public ObservableCollection<FanCurvePoint> FanCurvePoints => _fanCurvePoints;
        
        public string FanCurveSummary
        {
            get
            {
                if (_fanService?.FanTelemetry == null || _fanService.FanTelemetry.Count == 0)
                    return "Fan curve unavailable";
                
                var avgTemp = LatestMonitoringSample is { } sample
                    ? GetDashboardThermalProjectionTemps(sample).DefaultIfEmpty(0).Average()
                    : 0;
                
                var cpuFan = CpuFanDisplay;
                var gpuFan = GpuFanDisplay;
                
                return $"{avgTemp:F0}{CelsiusSuffix} -> CPU: {cpuFan} | GPU: {gpuFan}";
            }
        }

        public string FanCurveChipDisplay
        {
            get
            {
                if (_fanService?.FanTelemetry == null || _fanService.FanTelemetry.Count == 0)
                    return "-- RPM";

                var cpuFan = _fanService.FanTelemetry.Count > 0 ? FormatFanRpmForChip(_fanService.FanTelemetry[0]) : "--";
                var gpuFan = _fanService.FanTelemetry.Count > 1 ? FormatFanRpmForChip(_fanService.FanTelemetry[1]) : "--";

                return string.Equals(cpuFan, "--", StringComparison.Ordinal) && string.Equals(gpuFan, "--", StringComparison.Ordinal)
                    ? "-- RPM"
                    : $"{cpuFan}/{gpuFan}";
            }
        }
        
        // Session tracking properties (v2.2)
        public string SessionUptime
        {
            get
            {
                var elapsed = DateTime.Now - _sessionStartTime;
                if (elapsed.TotalHours >= 1)
                    return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
                return $"{elapsed.Minutes}m {elapsed.Seconds}s";
            }
        }
        
        public double PeakCpuTemp => _peakCpuTemp;
        public double PeakGpuTemp => _peakGpuTemp;
        
        public string FanSummary
        {
            get
            {
                var telemetry = _fanService?.FanTelemetry;
                if (telemetry == null || telemetry.Count == 0)
                    return "-- RPM";
                
                var fan1 = CpuFanDisplay;
                var fan2 = GpuFanDisplay;
                return $"CPU: {fan1} • GPU: {fan2}";
            }
        }

        public string CpuFanDisplay => _fanService?.FanTelemetry.Count > 0
            ? FormatFanRpmForSummary(_fanService.FanTelemetry[0])
            : "--";

        public string GpuFanDisplay => _fanService?.FanTelemetry.Count > 1
            ? FormatFanRpmForSummary(_fanService.FanTelemetry[1])
            : "--";

        private static string FormatFanRpmForSummary(FanTelemetry telemetry)
        {
            if (telemetry.RpmState == TelemetryDataState.Unavailable)
            {
                return "RPM unavailable (fan responding)";
            }

            return $"{telemetry.SpeedRpm} RPM";
        }

        private static string FormatFanRpmForChip(FanTelemetry telemetry)
        {
            if (telemetry.RpmState == TelemetryDataState.Unavailable)
            {
                return "n/a";
            }

            return telemetry.SpeedRpm > 0 ? telemetry.SpeedRpm.ToString() : "--";
        }

        public ICommand RefreshCommand { get; }

        public DashboardViewModel(
            HardwareMonitoringService monitoringService,
            FanService? fanService = null,
            RuntimePollingCoordinator? pollingCoordinator = null)
        {
            _monitoringService = monitoringService;
            _fanService = fanService;
            _pollingCoordinator = pollingCoordinator;
            RefreshCommand = new RelayCommand(_ => RefreshDashboardDisplay());
            _monitoringService.SampleUpdated += OnSampleUpdated;
            _monitoringService.HealthStatusChanged += OnHealthStatusChanged;
            SubscribeFanTelemetry();
            
            // Start uptime timer for session tracking (v2.2)
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uptimeTimer.Tick += (s, e) => 
            {
                if (!_telemetryProjectionEnabled)
                {
                    return;
                }

                OnPropertyChanged(nameof(SessionUptime));
                OnPropertyChanged(nameof(LastSampleAge));
            };
            _uptimeTimer.Start();
        }

        private void RefreshDashboardDisplay()
        {
            if (!MonitoringLowOverheadMode)
            {
                RebuildHistoricalTelemetryBuffers();
            }

            OnPropertyChanged(nameof(LatestMonitoringSample));
            OnPropertyChanged(nameof(CpuTempDisplay));
            OnPropertyChanged(nameof(GpuTempDisplay));
            OnPropertyChanged(nameof(CpuTempChipDisplay));
            OnPropertyChanged(nameof(GpuTempChipDisplay));
            OnPropertyChanged(nameof(SsdTempDisplay));
            OnPropertyChanged(nameof(CpuSummary));
            OnPropertyChanged(nameof(GpuSummary));
            OnPropertyChanged(nameof(CpuSummaryDisplay));
            OnPropertyChanged(nameof(GpuSummaryDisplay));
            OnPropertyChanged(nameof(StorageSummaryDisplay));
            OnPropertyChanged(nameof(PowerConsumptionSummary));
            OnPropertyChanged(nameof(PowerTotalDisplay));
            OnPropertyChanged(nameof(FanSummary));
            OnPropertyChanged(nameof(FanCurveSummary));
            OnPropertyChanged(nameof(FanCurveChipDisplay));
            OnPropertyChanged(nameof(CpuFanDisplay));
            OnPropertyChanged(nameof(GpuFanDisplay));
            OnPropertyChanged(nameof(MonitoringSourceText));
            OnPropertyChanged(nameof(LastSampleAge));
        }

        private void SubscribeFanTelemetry()
        {
            if (_fanService?.FanTelemetry == null)
            {
                return;
            }

            if (_fanService.FanTelemetry is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged += OnFanTelemetryCollectionChanged;
            }

            foreach (var fan in _fanService.FanTelemetry)
            {
                SubscribeFanTelemetryItem(fan);
            }
        }

        private void UnsubscribeFanTelemetry()
        {
            if (_fanService?.FanTelemetry is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged -= OnFanTelemetryCollectionChanged;
            }

            foreach (var fan in _subscribedFanTelemetry.ToArray())
            {
                fan.PropertyChanged -= OnFanTelemetryPropertyChanged;
            }

            _subscribedFanTelemetry.Clear();
        }

        private void OnFanTelemetryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var fan in _subscribedFanTelemetry.ToArray())
                {
                    fan.PropertyChanged -= OnFanTelemetryPropertyChanged;
                }

                _subscribedFanTelemetry.Clear();
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<FanTelemetry>())
                {
                    if (_subscribedFanTelemetry.Remove(item))
                    {
                        item.PropertyChanged -= OnFanTelemetryPropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<FanTelemetry>())
                {
                    SubscribeFanTelemetryItem(item);
                }
            }

            ScheduleFanTelemetryUiRefresh();
        }

        private void SubscribeFanTelemetryItem(FanTelemetry telemetry)
        {
            if (_subscribedFanTelemetry.Add(telemetry))
            {
                telemetry.PropertyChanged += OnFanTelemetryPropertyChanged;
            }
        }

        private void OnFanTelemetryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var propertyName = e.PropertyName;
            if (string.IsNullOrEmpty(propertyName)
                || propertyName == nameof(FanTelemetry.SpeedRpm)
                || propertyName == nameof(FanTelemetry.RpmState)
                || propertyName == nameof(FanTelemetry.DisplayRpmText)
                || propertyName == nameof(FanTelemetry.Name))
            {
                ScheduleFanTelemetryUiRefresh();
            }
        }

        private void ScheduleFanTelemetryUiRefresh()
        {
            if (_disposed || _pendingFanTelemetryUIUpdate)
            {
                return;
            }

            _pendingFanTelemetryUIUpdate = true;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _pendingFanTelemetryUIUpdate = false;
                RaiseFanTelemetryDisplayProperties();
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                _pendingFanTelemetryUIUpdate = false;
                if (_disposed)
                {
                    return;
                }

                RaiseFanTelemetryDisplayProperties();
            }), DispatcherPriority.Background);
        }

        private void RaiseFanTelemetryDisplayProperties()
        {
            OnPropertyChanged(nameof(FanSummary));
            OnPropertyChanged(nameof(FanCurveSummary));
            OnPropertyChanged(nameof(FanCurveChipDisplay));
            OnPropertyChanged(nameof(CpuFanDisplay));
            OnPropertyChanged(nameof(GpuFanDisplay));
            OnPropertyChanged(nameof(FanVisualState));
        }

        private void SetUptimeTimerEnabled(bool enabled)
        {
            if (_uptimeTimer == null)
            {
                return;
            }

            if (enabled)
            {
                if (!_uptimeTimer.IsEnabled)
                {
            // Timer start deferred to SetUptimeTimerEnabled when dashboard becomes active
                }
            }
            else if (_uptimeTimer.IsEnabled)
            {
                _uptimeTimer.Stop();
            }
        }
        
        // Monitoring health status properties (v2.7.0)
        /// <summary>
        /// Current monitoring health status.
        /// </summary>
        public MonitoringHealthStatus MonitoringHealthStatus => _monitoringService.HealthStatus;
        
        /// <summary>
        /// Human-readable monitoring health status string.
        /// </summary>
        public string MonitoringHealthStatusText => _monitoringService.HealthStatus switch
        {
            MonitoringHealthStatus.Healthy => "✓ Healthy",
            MonitoringHealthStatus.Degraded => "⚠ Degraded",
            MonitoringHealthStatus.Stale => "⛔ Stale",
            _ => "? Unknown"
        };

        public string MonitoringVisualState => _monitoringService.HealthStatus switch
        {
            MonitoringHealthStatus.Healthy => "confirmed",
            MonitoringHealthStatus.Degraded => "degraded",
            MonitoringHealthStatus.Stale => "blocked",
            _ => "degraded"
        };

        public string MonitoringHealthLabel => _monitoringService.HealthStatus switch
        {
            MonitoringHealthStatus.Healthy => "Healthy",
            MonitoringHealthStatus.Degraded => "Degraded",
            MonitoringHealthStatus.Stale => "Stale",
            _ => "Unknown"
        };

        public string FanVisualState => _fanService?.FanTelemetry.Count > 0 ? "confirmed" : "degraded";

        public string PerformanceVisualState => string.Equals(CurrentPerformanceMode, "Auto", StringComparison.OrdinalIgnoreCase)
            ? "degraded"
            : "confirmed";

        public string PowerVisualState => LatestMonitoringSample == null ? "degraded" : "confirmed";

        /// <summary>
        /// Monitoring source label for UI display.
        /// </summary>
        public string MonitoringSourceText => _monitoringService.MonitoringSource;
        
        /// <summary>
        /// Color for health status indicator.
        /// </summary>
        public string MonitoringHealthColor => _monitoringService.HealthStatus switch
        {
            MonitoringHealthStatus.Healthy => "#2ECC71",  // Green
            MonitoringHealthStatus.Degraded => "#F1C40F", // Yellow
            MonitoringHealthStatus.Stale => "#E74C3C",    // Red
            _ => "#95A5A6"                                 // Gray
        };

        public bool IsTelemetryStale => _monitoringService.HealthStatus == MonitoringHealthStatus.Stale ||
                        _monitoringService.HealthStatus == MonitoringHealthStatus.Degraded;

        public bool HasAmdGpuTelemetryQuarantineWarning =>
            LatestMonitoringSample?.IsAmdGpuTelemetryQuarantined == true;

        public string AmdGpuTelemetryQuarantineWarningText
        {
            get
            {
                var sample = LatestMonitoringSample;
                if (sample?.IsAmdGpuTelemetryQuarantined != true)
                {
                    return string.Empty;
                }

                var reason = string.IsNullOrWhiteSpace(sample.AmdGpuTelemetryQuarantineReason)
                    ? string.Empty
                    : $" Reason: {sample.AmdGpuTelemetryQuarantineReason}";

                return "AMD GPU telemetry is temporarily quarantined after a driver/library fault. " +
                       "CPU, fan, memory, and system telemetry continue normally." + reason;
            }
        }

        public string TelemetryStateBannerText => _monitoringService.HealthStatus switch
        {
            MonitoringHealthStatus.Stale => "Telemetry stale: sensor data is delayed or frozen. OmenCore is attempting automatic recovery.",
            MonitoringHealthStatus.Degraded => "Telemetry degraded: data quality reduced. Monitoring recovery is in progress.",
            _ => string.Empty
        };
        
        /// <summary>
        /// Time since last successful sensor reading.
        /// </summary>
        public string LastSampleAge
        {
            get
            {
                var age = _monitoringService.LastSampleAge;
                if (age == TimeSpan.MaxValue) return "No data";
                if (age.TotalSeconds < 2) return "Just now";
                if (age.TotalSeconds < 60) return $"{age.TotalSeconds:F0}s ago";
                return $"{age.TotalMinutes:F0}m ago";
            }
        }
        
        private void OnHealthStatusChanged(object? sender, MonitoringHealthStatus status)
        {
            if (!_telemetryProjectionEnabled)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (!_telemetryProjectionEnabled)
                {
                    return;
                }

                OnPropertyChanged(nameof(MonitoringHealthStatus));
                OnPropertyChanged(nameof(MonitoringHealthStatusText));
                OnPropertyChanged(nameof(MonitoringVisualState));
                OnPropertyChanged(nameof(MonitoringHealthLabel));
                OnPropertyChanged(nameof(MonitoringHealthColor));
                OnPropertyChanged(nameof(MonitoringSourceText));
                OnPropertyChanged(nameof(IsTelemetryStale));
                OnPropertyChanged(nameof(TelemetryStateBannerText));
            });
        }

        private void OnSampleUpdated(object? sender, MonitoringSample sample)
        {
            RuntimeUiPerformanceCounters.RecordDashboardSampleReceived();

            lock (_sampleUpdateLock)
            {
                _queuedSample = sample;
                if (!_telemetryProjectionEnabled)
                {
                    RuntimeUiPerformanceCounters.RecordDashboardSampleSkipped();
                       // v3.6.2: Track samples queued during dormancy (latest-sample replacements)
                       RuntimeUiPerformanceCounters.RecordDashboardDormancySampleProjected();
                       RuntimeUiPerformanceCounters.RecordLatestSampleReplacement();
                    return;
                }

                if (_pendingUIUpdate)
                {
                    RuntimeUiPerformanceCounters.RecordDashboardSampleSkipped();
                    RuntimeUiPerformanceCounters.RecordLatestSampleReplacement();
                    return;
                }

                _pendingUIUpdate = true;
            }

            ScheduleUiProjection();
        }

        private void ScheduleUiProjection()
        {
            RuntimeUiPerformanceCounters.RecordDashboardDispatcherPost();
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ProcessQueuedUiProjection();
                return;
            }

            dispatcher.BeginInvoke(new Action(ProcessQueuedUiProjection), DispatcherPriority.Background);
        }

        private void ProcessQueuedUiProjection()
        {
            bool requeue = false;
            try
            {
                MonitoringSample? latest;
                lock (_sampleUpdateLock)
                {
                    if (!_telemetryProjectionEnabled)
                    {
                        _pendingUIUpdate = false;
                        return;
                    }

                    latest = _queuedSample;
                    _queuedSample = null;
                }

                if (latest != null && ShouldProjectSampleToUi(latest))
                {
                    RuntimeUiPerformanceCounters.RecordDashboardSampleProjected();
                    _lastUiProjectedSample = latest;
                    _lastUiProjectionUtc = DateTime.UtcNow;

                    LatestMonitoringSample = latest;

                    // Convert to ThermalSample for temperature charts
                    var thermalSample = new ThermalSample
                    {
                        Timestamp = latest.Timestamp,
                        CpuCelsius = latest.CpuTemperatureC,
                        GpuCelsius = latest.GpuTemperatureC
                    };
                    if (!MonitoringLowOverheadMode)
                    {
                        _thermalSamples.Add(thermalSample);

                        // Trim to max history size - remove excess items in one pass
                        var excessCount = _thermalSamples.Count - MaxThermalSampleHistory;
                        for (int i = 0; i < excessCount; i++)
                        {
                            _thermalSamples.RemoveAt(0);
                        }

                        AddFilteredSample(thermalSample);

                        // Update fan curve points for visualization
                        UpdateFanCurvePoints(latest);

                        // Update sparkline data properties only when graph history is active.
                        OnPropertyChanged(nameof(RecentCpuTemps));
                        OnPropertyChanged(nameof(RecentGpuTemps));
                        OnPropertyChanged(nameof(HasSparklineData));
                        OnPropertyChanged(nameof(HasHistoricalData));
                        OnPropertyChanged(nameof(FanCurveSummary));
                        OnPropertyChanged(nameof(RecentRamUsage));
                    }

                    // Notify coalesced derived summaries
                    OnPropertyChanged(nameof(PowerConsumptionSummary));
                    OnPropertyChanged(nameof(PowerEfficiencySummary));
                    OnPropertyChanged(nameof(BatteryHealthSummary));
                    OnPropertyChanged(nameof(HasLiveData));
                    OnPropertyChanged(nameof(MonitoringSourceText));
                }
                else if (latest != null)
                {
                    RuntimeUiPerformanceCounters.RecordDashboardSampleSkipped();
                }

                lock (_sampleUpdateLock)
                {
                    requeue = _queuedSample != null;
                    if (!requeue)
                    {
                        _pendingUIUpdate = false;
                    }
                }
            }
            finally
            {
                // Process at most one sample per dispatch turn to avoid monopolizing
                // the UI thread under telemetry bursts.
                if (requeue)
                {
                    RuntimeUiPerformanceCounters.RecordDashboardProjectionRequeue();
                    ScheduleUiProjection();
                }
            }
        }

        private bool ShouldProjectSampleToUi(MonitoringSample sample)
        {
            if (_lastUiProjectedSample == null)
            {
                return true;
            }

            var elapsed = DateTime.UtcNow - _lastUiProjectionUtc;
            if (elapsed >= UiProjectionMinInterval)
            {
                return true;
            }

            var previous = _lastUiProjectedSample;
            var cpuTempChange = Math.Abs(sample.CpuTemperatureC - previous.CpuTemperatureC);
            var gpuTempChange = Math.Abs(sample.GpuTemperatureC - previous.GpuTemperatureC);
            var cpuLoadChange = Math.Abs(sample.CpuLoadPercent - previous.CpuLoadPercent);
            var gpuLoadChange = Math.Abs(sample.GpuLoadPercent - previous.GpuLoadPercent);
            var cpuPowerChange = Math.Abs(sample.CpuPowerWatts - previous.CpuPowerWatts);
            var gpuPowerChange = Math.Abs(sample.GpuPowerWatts - previous.GpuPowerWatts);
            var fan1RpmChange = Math.Abs(sample.Fan1Rpm - previous.Fan1Rpm);
            var fan2RpmChange = Math.Abs(sample.Fan2Rpm - previous.Fan2Rpm);
            var amdGpuQuarantineStateChanged =
                sample.IsAmdGpuTelemetryQuarantined != previous.IsAmdGpuTelemetryQuarantined ||
                !string.Equals(sample.AmdGpuTelemetryQuarantineReason, previous.AmdGpuTelemetryQuarantineReason, StringComparison.Ordinal);

            return cpuTempChange >= UiProjectionTempDelta
                   || gpuTempChange >= UiProjectionTempDelta
                   || cpuLoadChange >= UiProjectionLoadDelta
                   || gpuLoadChange >= UiProjectionLoadDelta
                   || cpuPowerChange >= UiProjectionPowerDelta
                   || gpuPowerChange >= UiProjectionPowerDelta
                   || fan1RpmChange >= UiProjectionFanRpmDelta
                   || fan2RpmChange >= UiProjectionFanRpmDelta
                   || amdGpuQuarantineStateChanged;
        }
        
        private void UpdateFanCurvePoints(MonitoringSample sample)
        {
            if (_fanService?.FanTelemetry == null || _fanService.FanTelemetry.Count == 0)
                return;
            
            var usableTemps = GetDashboardThermalProjectionTemps(sample).ToList();
            if (usableTemps.Count == 0)
            {
                return;
            }

            var avgTemp = (int)Math.Round(usableTemps.Average());
            
            // Get current fan speeds
            var cpuFanRpm = _fanService.FanTelemetry.Count > 0 ? _fanService.FanTelemetry[0].SpeedRpm : 0;
            var gpuFanRpm = _fanService.FanTelemetry.Count > 1 ? _fanService.FanTelemetry[1].SpeedRpm : 0;
            var avgFanRpm = (cpuFanRpm + gpuFanRpm) / 2;
            
            // Add current point to fan curve (limit to last 50 points for visualization)
            _fanCurvePoints.Add(new FanCurvePoint
            {
                TemperatureC = avgTemp,
                FanSpeedRpm = avgFanRpm,
                Timestamp = sample.Timestamp
            });
            
            // Keep only recent points
            while (_fanCurvePoints.Count > 50)
            {
                _fanCurvePoints.RemoveAt(0);
            }
        }

        private static IEnumerable<double> GetDashboardThermalProjectionTemps(MonitoringSample sample)
        {
            var cpuTemp = GetUsableDashboardTemperature(sample.CpuTemperatureC, sample.CpuTemperatureState);
            var gpuTemp = GetUsableDashboardTemperature(sample.GpuTemperatureC, sample.GpuTemperatureState);

            if (IsLikelyBrokenBiosSentinel(cpuTemp, gpuTemp))
            {
                cpuTemp = null;
            }

            if (IsLikelyBrokenBiosSentinel(gpuTemp, cpuTemp))
            {
                gpuTemp = null;
            }

            if (cpuTemp.HasValue)
            {
                yield return cpuTemp.Value;
            }

            if (gpuTemp.HasValue)
            {
                yield return gpuTemp.Value;
            }
        }

        private static double? GetUsableDashboardTemperature(double temp, TelemetryDataState state)
        {
            if (state is TelemetryDataState.Invalid or TelemetryDataState.Unavailable or TelemetryDataState.Inactive or TelemetryDataState.Stale)
            {
                return null;
            }

            if (double.IsNaN(temp) || double.IsInfinity(temp) || temp <= 0 || temp > MaxPlausibleDashboardTempC)
            {
                return null;
            }

            return temp;
        }

        private static bool IsLikelyBrokenBiosSentinel(double? candidate, double? paired)
        {
            if (!candidate.HasValue || !paired.HasValue)
            {
                return false;
            }

            return Math.Abs(candidate.Value - SuspectBiosSentinelTempC) < 0.1
                   && paired.Value <= 80
                   && candidate.Value - paired.Value >= SuspectBiosSentinelDeltaC;
        }
        
        /// <summary>
        /// Dispose resources and unsubscribe from events.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// Protected dispose implementation.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                _monitoringService.SampleUpdated -= OnSampleUpdated;
                _monitoringService.HealthStatusChanged -= OnHealthStatusChanged;
                UnsubscribeFanTelemetry();
                _uptimeTimer?.Stop();
                _uptimeTimer = null;
            }
            
            _disposed = true;
        }
    }
}
