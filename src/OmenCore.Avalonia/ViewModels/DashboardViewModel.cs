using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using OmenCore.Avalonia.Services;
using System.Diagnostics;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Dashboard ViewModel showing system overview.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareService _hardwareService;
    private readonly Stopwatch _sessionStopwatch = Stopwatch.StartNew();
    private readonly DispatcherTimer _uptimeTimer;
    private bool _disposed;

    [ObservableProperty]
    private double _cpuTemperature;

    [ObservableProperty]
    private double _gpuTemperature;

    [ObservableProperty]
    private int _cpuFanRpm;

    [ObservableProperty]
    private int _gpuFanRpm;

    [ObservableProperty]
    private int _cpuFanPercent;

    [ObservableProperty]
    private int _gpuFanPercent;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private double _gpuUsage;

    [ObservableProperty]
    private double _memoryUsage;

    [ObservableProperty]
    private string _memoryUsed = "0 / 0 GB";

    [ObservableProperty]
    private double _powerConsumption;

    [ObservableProperty]
    private int _batteryPercentage = 100;

    [ObservableProperty]
    private bool _isOnBattery;

    [ObservableProperty]
    private string _powerSource = "AC Power";

    [ObservableProperty]
    private string _cpuName = "Loading...";

    [ObservableProperty]
    private string _gpuName = "Loading...";

    [ObservableProperty]
    private string _performanceMode = "Balanced";

    [ObservableProperty]
    private string _fanMode = "Auto";

    [ObservableProperty]
    private string _fanSummary = "-- / -- RPM";

    [ObservableProperty]
    private string _sessionUptime = "0:00:00";

    [ObservableProperty]
    private double _peakCpuTemp;

    [ObservableProperty]
    private double _peakGpuTemp;

    [ObservableProperty]
    private bool _isThrottling;

    [ObservableProperty]
    private string _throttlingSummary = "";

    // Temperature warnings
    public bool IsCpuTemperatureWarning => CpuTemperature >= 80;
    public bool IsGpuTemperatureWarning => GpuTemperature >= 85;
    public bool IsCpuTemperatureCritical => CpuTemperature >= 95;
    public bool IsGpuTemperatureCritical => GpuTemperature >= 95;

    public DashboardViewModel(IHardwareService hardwareService)
    {
        _hardwareService = hardwareService;
        _hardwareService.StatusChanged += OnStatusChanged;
        _uptimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uptimeTimer.Tick += (_, _) =>
        {
            SessionUptime = _sessionStopwatch.Elapsed.ToString(@"h\:mm\:ss");
        };
        
        Initialize();
        StartUptimeTimer();
    }

    private async void Initialize()
    {
        try
        {
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            CpuName = capabilities.CpuName;
            GpuName = capabilities.GpuName;

            var mode = await _hardwareService.GetPerformanceModeAsync();
            PerformanceMode = mode.ToString();

            // Initial status update
            var status = await _hardwareService.GetStatusAsync();
            UpdateStatus(status);
        }
        catch
        {
            // Handle initialization errors gracefully
        }
    }

    private void StartUptimeTimer()
    {
        _uptimeTimer.Start();
    }

    private void OnStatusChanged(object? sender, HardwareStatus status)
    {
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateStatus(status);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed)
                {
                    UpdateStatus(status);
                }
            });
        }
    }

    private void UpdateStatus(HardwareStatus status)
    {
        CpuTemperature = Math.Round(status.CpuTemperature, 1);
        GpuTemperature = Math.Round(status.GpuTemperature, 1);
        CpuFanRpm = status.CpuFanRpm;
        GpuFanRpm = status.GpuFanRpm;
        CpuFanPercent = status.CpuFanPercent;
        GpuFanPercent = status.GpuFanPercent;
        CpuUsage = Math.Round(status.CpuUsage, 1);
        GpuUsage = Math.Round(status.GpuUsage, 1);
        MemoryUsage = Math.Round(status.MemoryUsage, 1);
        MemoryUsed = $"{status.MemoryUsedGb:F1} / {status.MemoryTotalGb:F1} GB";
        PowerConsumption = Math.Round(status.PowerConsumption, 1);
        BatteryPercentage = status.BatteryPercentage;
        IsOnBattery = status.IsOnBattery;
        PowerSource = status.IsOnBattery ? "Battery" : "AC Power";

        // Update peak temps
        if (CpuTemperature > PeakCpuTemp) PeakCpuTemp = CpuTemperature;
        if (GpuTemperature > PeakGpuTemp) PeakGpuTemp = GpuTemperature;

        // Update fan summary
        FanSummary = $"{CpuFanRpm} / {GpuFanRpm} RPM";

        // Check throttling
        IsThrottling = status.IsThrottling;
        if (IsThrottling)
        {
            ThrottlingSummary = status.ThrottlingReason ?? "Thermal throttling detected";
        }

        // Notify temperature warning properties
        OnPropertyChanged(nameof(IsCpuTemperatureWarning));
        OnPropertyChanged(nameof(IsGpuTemperatureWarning));
        OnPropertyChanged(nameof(IsCpuTemperatureCritical));
        OnPropertyChanged(nameof(IsGpuTemperatureCritical));
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
            var status = await _hardwareService.GetStatusAsync();
            UpdateStatus(status);
        }
        catch
        {
            // Ignore refresh errors
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hardwareService.StatusChanged -= OnStatusChanged;
            _uptimeTimer.Stop();
            _disposed = true;
        }
    }
}
