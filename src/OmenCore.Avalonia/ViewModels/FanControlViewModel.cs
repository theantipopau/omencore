using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;
using System.Collections.ObjectModel;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Fan control ViewModel for custom fan curves.
/// </summary>
public partial class FanControlViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareService _hardwareService;
    private readonly IFanCurveService _fanCurveService;
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
    private bool _isCustomCurveEnabled;

    [ObservableProperty]
    private string _selectedPreset = "Balanced";

    [ObservableProperty]
    private bool _linkFans = true;

    [ObservableProperty]
    private int _hysteresis = 3;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _canEditFanCurve = true;

    [ObservableProperty]
    private bool _showCapabilityWarning;

    [ObservableProperty]
    private string _capabilityWarningMessage = "";

    public bool IsCurveEditorVisible => CanEditFanCurve && IsCustomCurveEnabled;

    public ObservableCollection<string> Presets { get; } = new();
    public ObservableCollection<FanCurvePointViewModel> CpuFanCurve { get; } = new();
    public ObservableCollection<FanCurvePointViewModel> GpuFanCurve { get; } = new();

    public FanControlViewModel(
        IHardwareService hardwareService,
        IFanCurveService fanCurveService)
    {
        _hardwareService = hardwareService;
        _fanCurveService = fanCurveService;
        
        _hardwareService.StatusChanged += OnStatusChanged;
        
        Initialize();
    }

    private void Initialize()
    {
        // Load presets
        foreach (var preset in _fanCurveService.GetPresetNames())
        {
            Presets.Add(preset);
        }

        // Load default curves
        LoadPreset("Balanced");

        _ = InitializeCapabilitiesAsync();
    }

    private async Task InitializeCapabilitiesAsync()
    {
        try
        {
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            CanEditFanCurve = capabilities.SupportsFanControl;

            var capabilityClass = capabilities.FanControlCapabilityClass?.Trim().ToLowerInvariant() ?? "unsupported-control";
            switch (capabilityClass)
            {
                case "profile-only":
                    ShowCapabilityWarning = true;
                    CapabilityWarningMessage = "This Linux system exposes thermal profiles but not direct fan-speed targets. Use System Control performance profiles for cooling behavior.";
                    break;
                case "telemetry-only":
                    ShowCapabilityWarning = true;
                    CapabilityWarningMessage = "Fan telemetry is available, but firmware does not expose writable fan control interfaces on this board/kernel.";
                    break;
                case "unsupported-control":
                    ShowCapabilityWarning = true;
                    CapabilityWarningMessage = "No supported Linux fan control interface was detected for this board/kernel combination.";
                    break;
                default:
                    ShowCapabilityWarning = false;
                    CapabilityWarningMessage = string.Empty;
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowCapabilityWarning = true;
            CapabilityWarningMessage = "Could not detect Linux fan-control capability. Curve controls may be unavailable on this system.";
            System.Diagnostics.Debug.WriteLine($"Failed to initialize fan capability state: {ex.Message}");
        }
    }

    private void OnStatusChanged(object? sender, HardwareStatus status)
    {
        CpuTemperature = Math.Round(status.CpuTemperature, 1);
        GpuTemperature = Math.Round(status.GpuTemperature, 1);
        CpuFanRpm = status.CpuFanRpm;
        GpuFanRpm = status.GpuFanRpm;
        
        // Calculate current fan percentages (estimate based on RPM)
        CpuFanPercent = Math.Min(100, (int)(CpuFanRpm / 60.0));
        GpuFanPercent = Math.Min(100, (int)(GpuFanRpm / 60.0));
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            LoadPreset(value);
            if (CanEditFanCurve)
            {
                _ = ApplyCurve();
            }
        }
    }

    partial void OnIsCustomCurveEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCurveEditorVisible));
    }

    partial void OnCanEditFanCurveChanged(bool value)
    {
        if (!value)
        {
            IsCustomCurveEnabled = false;
        }

        OnPropertyChanged(nameof(IsCurveEditorVisible));
    }

    [RelayCommand]
    private void LoadPreset(string presetName)
    {
        var (cpu, gpu) = _fanCurveService.GetPreset(presetName);
        
        CpuFanCurve.Clear();
        foreach (var point in cpu)
        {
            CpuFanCurve.Add(new FanCurvePointViewModel(point));
        }

        GpuFanCurve.Clear();
        foreach (var point in gpu)
        {
            GpuFanCurve.Add(new FanCurvePointViewModel(point));
        }

        _fanCurveService.SetCpuFanCurve(cpu);
        _fanCurveService.SetGpuFanCurve(gpu);
    }

    [RelayCommand]
    private async Task ApplyCurve()
    {
        if (!CanEditFanCurve)
        {
            StatusMessage = "Manual fan curve control is unavailable on this system.";
            return;
        }

        try
        {
            // Update curves from view models
            _fanCurveService.SetCpuFanCurve(CpuFanCurve.Select(vm => new FanCurvePoint(vm.Temperature, vm.FanSpeed)));
            _fanCurveService.SetGpuFanCurve(GpuFanCurve.Select(vm => new FanCurvePoint(vm.Temperature, vm.FanSpeed)));
            
            await _fanCurveService.ApplyAsync();
            StatusMessage = "Applied once using current CPU/GPU temperatures.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to apply fan curve: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Failed to apply fan curve: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        SelectedPreset = "Balanced";
        LoadPreset("Balanced");
        StatusMessage = "Reset to default fan curve";
    }

    [RelayCommand]
    private async Task SavePreset()
    {
        try
        {
            var baseName = string.IsNullOrWhiteSpace(SelectedPreset) ? "Custom" : SelectedPreset.Trim();
            var presetName = baseName;

            if (Presets.Contains(presetName))
            {
                presetName = $"{baseName}-{DateTime.Now:HHmmss}";
            }

            var cpuCurve = CpuFanCurve.Select(vm => new FanCurvePoint(vm.Temperature, vm.FanSpeed)).ToList();
            var gpuCurve = GpuFanCurve.Select(vm => new FanCurvePoint(vm.Temperature, vm.FanSpeed)).ToList();

            _fanCurveService.SavePreset(presetName, cpuCurve, gpuCurve);

            if (!Presets.Contains(presetName))
            {
                Presets.Add(presetName);
            }

            SelectedPreset = presetName;
            await ApplyCurve();
            StatusMessage = $"Saved preset '{presetName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save preset: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EmergencyStop()
    {
        try
        {
            await _hardwareService.SetCpuFanSpeedAsync(100);
            await _hardwareService.SetGpuFanSpeedAsync(100);
            StatusMessage = "Emergency stop activated - fans set to maximum";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Emergency stop failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddCpuPoint()
    {
        var lastPoint = CpuFanCurve.LastOrDefault();
        var newTemp = lastPoint != null ? Math.Min(100, lastPoint.Temperature + 10) : 40;
        var newSpeed = lastPoint != null ? Math.Min(100, lastPoint.FanSpeed + 10) : 30;
        CpuFanCurve.Add(new FanCurvePointViewModel(new FanCurvePoint(newTemp, newSpeed)));
    }

    [RelayCommand]
    private void AddGpuPoint()
    {
        var lastPoint = GpuFanCurve.LastOrDefault();
        var newTemp = lastPoint != null ? Math.Min(100, lastPoint.Temperature + 10) : 40;
        var newSpeed = lastPoint != null ? Math.Min(100, lastPoint.FanSpeed + 10) : 30;
        GpuFanCurve.Add(new FanCurvePointViewModel(new FanCurvePoint(newTemp, newSpeed)));
    }

    [RelayCommand]
    private void RemoveCpuPoint(FanCurvePointViewModel? point)
    {
        if (point != null && CpuFanCurve.Count > 2)
        {
            CpuFanCurve.Remove(point);
        }
    }

    [RelayCommand]
    private void RemoveGpuPoint(FanCurvePointViewModel? point)
    {
        if (point != null && GpuFanCurve.Count > 2)
        {
            GpuFanCurve.Remove(point);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hardwareService.StatusChanged -= OnStatusChanged;
            _disposed = true;
        }
    }
}

/// <summary>
/// ViewModel for a single fan curve point.
/// </summary>
public partial class FanCurvePointViewModel : ObservableObject
{
    [ObservableProperty]
    private int _temperature;

    [ObservableProperty]
    private int _fanSpeed;

    public FanCurvePointViewModel(FanCurvePoint point)
    {
        Temperature = point.Temperature;
        FanSpeed = point.FanSpeed;
    }
}
