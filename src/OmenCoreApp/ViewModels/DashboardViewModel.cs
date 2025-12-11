using System.Collections.ObjectModel;
using System.Linq;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly HardwareMonitoringService _monitoringService;
        private readonly ObservableCollection<ThermalSample> _thermalSamples = new();
        private MonitoringSample? _latestMonitoringSample;
        private bool _monitoringLowOverhead;
        private string _currentPerformanceMode = "Auto";
        private string _currentFanMode = "Auto";

        public ReadOnlyObservableCollection<MonitoringSample> MonitoringSamples => _monitoringService.Samples;
        public ObservableCollection<ThermalSample> ThermalSamples => _thermalSamples;
        
        public string CurrentPerformanceMode
        {
            get => _currentPerformanceMode;
            set
            {
                if (_currentPerformanceMode != value)
                {
                    _currentPerformanceMode = value;
                    OnPropertyChanged();
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
                }
            }
        }

        public MonitoringSample? LatestMonitoringSample
        {
            get => _latestMonitoringSample;
            private set
            {
                _latestMonitoringSample = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuSummary));
                OnPropertyChanged(nameof(GpuSummary));
                OnPropertyChanged(nameof(MemorySummary));
                OnPropertyChanged(nameof(StorageSummary));
                OnPropertyChanged(nameof(BatterySummary));
                OnPropertyChanged(nameof(CpuClockSummary));
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
                    _monitoringService.SetLowOverheadMode(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MonitoringGraphsVisible));
                }
            }
        }

        public bool MonitoringGraphsVisible => !MonitoringLowOverheadMode;

        public string CpuSummary => LatestMonitoringSample == null 
            ? "CPU telemetry unavailable" 
            : LatestMonitoringSample.CpuPowerWatts > 0 
                ? $"{LatestMonitoringSample.CpuTemperatureC:F0}°C • {LatestMonitoringSample.CpuLoadPercent:F0}% • {LatestMonitoringSample.CpuPowerWatts:F0}W"
                : $"{LatestMonitoringSample.CpuTemperatureC:F0}°C • {LatestMonitoringSample.CpuLoadPercent:F0}% load";
        public string GpuSummary => LatestMonitoringSample == null ? "GPU telemetry unavailable" : $"{LatestMonitoringSample.GpuTemperatureC:F0}°C • {LatestMonitoringSample.GpuLoadPercent:F0}% load • {LatestMonitoringSample.GpuVramUsageMb:F0} MB VRAM";
        public string MemorySummary => LatestMonitoringSample == null ? "Memory telemetry unavailable" : $"{LatestMonitoringSample.RamUsageGb:F1} / {LatestMonitoringSample.RamTotalGb:F0} GB";
        public string StorageSummary => LatestMonitoringSample == null ? "Storage telemetry unavailable" : $"SSD {LatestMonitoringSample.SsdTemperatureC:F0}°C • {LatestMonitoringSample.DiskUsagePercent:F0}% active";
        public string BatterySummary => LatestMonitoringSample == null ? "Battery unavailable" 
            : LatestMonitoringSample.IsOnAcPower 
                ? $"{LatestMonitoringSample.BatteryChargePercent:F0}% • AC Power" 
                : $"{LatestMonitoringSample.BatteryChargePercent:F0}% • {LatestMonitoringSample.BatteryTimeRemaining}";
        public string CpuClockSummary => LatestMonitoringSample == null || LatestMonitoringSample.CpuCoreClocksMhz.Count == 0
            ? "Per-core clocks unavailable"
            : string.Join(", ", LatestMonitoringSample.CpuCoreClocksMhz.Select((c, i) => $"C{i + 1}:{c:F0}MHz"));

        public DashboardViewModel(HardwareMonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
            _monitoringService.SampleUpdated += OnSampleUpdated;
        }

        private void OnSampleUpdated(object? sender, MonitoringSample sample)
        {
            LatestMonitoringSample = sample;
            
            // Marshal to UI thread for ObservableCollection updates
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Convert to ThermalSample for temperature charts
                _thermalSamples.Add(new ThermalSample
                {
                    Timestamp = sample.Timestamp,
                    CpuCelsius = sample.CpuTemperatureC,
                    GpuCelsius = sample.GpuTemperatureC
                });
                
                // Keep only last 60 samples (1 minute at 1 sample/second)
                while (_thermalSamples.Count > 60)
                {
                    _thermalSamples.RemoveAt(0);
                }
            });
        }
    }
}
