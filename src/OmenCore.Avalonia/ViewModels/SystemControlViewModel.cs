using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// System control ViewModel for performance modes, GPU switching, and keyboard lighting.
/// </summary>
public partial class SystemControlViewModel : ObservableObject
{
    private readonly IHardwareService _hardwareService;
    private bool _suppressPerformanceModeSelectionChange;
    private string _performanceProfileReason = "Performance mode control is unavailable on this Linux board/kernel path.";
    private bool _canSetKeyboardBrightness = true;
    private string _keyboardBrightnessReason = "Keyboard brightness control is unavailable on this Linux board/kernel path.";

    // Performance Mode
    [ObservableProperty]
    private int _selectedPerformanceModeIndex;

    [ObservableProperty]
    private string _currentPerformanceMode = "Balanced";

    [ObservableProperty]
    private bool _isPerformanceModeChanging;

    [ObservableProperty]
    private bool _canSetPerformanceMode = true;

    // GPU Mode
    [ObservableProperty]
    private string _currentGpuMode = "hybrid";

    [ObservableProperty]
    private bool _isGpuModeChanging;

    [ObservableProperty]
    private bool _hasGpuMuxSwitch;

    // Keyboard Lighting
    [ObservableProperty]
    private bool _hasKeyboardBacklight;

    [ObservableProperty]
    private int _keyboardBrightness = 100;

    [ObservableProperty]
    private byte _keyboardRed = 0;

    [ObservableProperty]
    private byte _keyboardGreen = 191;

    [ObservableProperty]
    private byte _keyboardBlue = 255;

    [ObservableProperty]
    private bool _hasFourZoneRgb;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusDetails = string.Empty;

    public string[] PerformanceModes { get; } = { "Quiet", "Balanced", "Performance" };
    public string[] GpuModes { get; } = { "Hybrid", "Discrete", "Integrated" };

    public SystemControlViewModel(IHardwareService hardwareService)
    {
        _hardwareService = hardwareService;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            ApplyCapabilities(capabilities);

            if (!CanSetPerformanceMode)
            {
                SetStatus("Performance profile control is unavailable.", BuildLinuxCapabilityHelpDetails());
            }

            var mode = await _hardwareService.GetPerformanceModeAsync();
            SetSelectedPerformanceModeIndex(mode);
            CurrentPerformanceMode = GetPerformanceModeName(mode);

            CurrentGpuMode = await _hardwareService.GetGpuModeAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Initialization error: {ex.Message}";
        }
    }

    partial void OnSelectedPerformanceModeIndexChanged(int value)
    {
        if (_suppressPerformanceModeSelectionChange)
        {
            return;
        }

        if (!TryGetPerformanceModeFromIndex(value, out var mode))
        {
            SetStatus($"Unknown performance mode index: {value}");
            return;
        }

        _ = SetPerformanceModeByIndexAsync(mode);
    }

    [RelayCommand]
    private async Task SetPerformanceMode(string modeName)
    {
        if (IsPerformanceModeChanging)
            return;

        if (!TryParsePerformanceModeName(modeName, out var mode))
        {
            SetStatus($"Unsupported performance mode: {modeName}");
            return;
        }

        await SetPerformanceModeByIndexAsync(mode);
    }

    private async Task SetPerformanceModeByIndexAsync(PerformanceMode mode)
    {
        if (IsPerformanceModeChanging)
            return;

        if (!CanSetPerformanceMode)
        {
            await RefreshCapabilitiesIfNeededAsync();
            if (!CanSetPerformanceMode)
            {
                SetStatus("Performance profile control is still unavailable.", BuildLinuxCapabilityHelpDetails());
            return;
            }
        }

        try
        {
            IsPerformanceModeChanging = true;
            SetStatus($"Setting performance mode to {mode}...");
            await _hardwareService.SetPerformanceModeAsync(mode);
            CurrentPerformanceMode = GetPerformanceModeName(mode);
            SetSelectedPerformanceModeIndex(mode);
            SetStatus($"Performance mode set to {mode}");
        }
        catch (Exception ex)
        {
            SetStatus("Failed to set performance mode.", ex.Message);
        }
        finally
        {
            IsPerformanceModeChanging = false;
        }
    }

    [RelayCommand]
    private async Task RefreshCapabilities()
    {
        await RefreshCapabilitiesIfNeededAsync();

        if (CanSetPerformanceMode)
        {
            SetStatus("Linux control capabilities refreshed. Performance profiles are available.");
        }
        else
        {
            SetStatus("Performance profile control is still unavailable.", BuildLinuxCapabilityHelpDetails());
        }
    }

    [RelayCommand]
    private void ShowLinuxDiagnosticsHelp()
    {
        SetStatus("Collect Linux diagnostics for support.",
            "Run these commands:\n" +
            "sudo omencore-cli diagnose --report\n" +
            "sudo dmidecode -t system -t baseboard -t bios\n" +
            "find /sys/devices/platform/hp-wmi -maxdepth 4 -type f -print 2>/dev/null\n" +
            "find /sys/class/hwmon -maxdepth 3 -type f -name 'fan*' -o -name 'pwm*' -o -name 'temp*_input' 2>/dev/null");
    }

    private async Task RefreshCapabilitiesIfNeededAsync()
    {
        try
        {
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            ApplyCapabilities(capabilities);
            if (CanSetPerformanceMode)
            {
                StatusDetails = string.Empty;
            }
        }
        catch
        {
            // Keep prior capability state; caller will surface existing status reason.
        }
    }

    private void ApplyCapabilities(SystemCapabilities capabilities)
    {
        HasKeyboardBacklight = capabilities.HasKeyboardBacklight;
        HasFourZoneRgb = capabilities.HasFourZoneRgb;
        HasGpuMuxSwitch = capabilities.HasGpuMuxSwitch;
        CanSetPerformanceMode = capabilities.SupportsPerformanceProfiles;
        _performanceProfileReason = string.IsNullOrWhiteSpace(capabilities.PerformanceProfileReason)
            ? "Performance mode control is unavailable on this Linux board/kernel path."
            : capabilities.PerformanceProfileReason;

        _canSetKeyboardBrightness = capabilities.SupportsKeyboardBrightness;
        _keyboardBrightnessReason = string.IsNullOrWhiteSpace(capabilities.KeyboardBrightnessReason)
            ? "Keyboard brightness control is unavailable on this Linux board/kernel path."
            : capabilities.KeyboardBrightnessReason;
    }

    private string BuildLinuxCapabilityHelpDetails()
    {
        return _performanceProfileReason + "\n\n" +
               "Try:\n" +
               "- sudo modprobe hp-wmi\n" +
               "- (legacy boards) sudo modprobe ec_sys write_support=1\n" +
               "- Click 'Re-detect Linux Controls' in this page\n" +
               "- If still unavailable, click 'Show Linux Diagnostic Commands'";
    }

    [RelayCommand]
    private async Task SetGpuMode(string mode)
    {
        if (IsGpuModeChanging)
            return;

        try
        {
            IsGpuModeChanging = true;
            SetStatus($"Switching GPU to {mode} mode...");
            await _hardwareService.SetGpuModeAsync(mode);
            CurrentGpuMode = mode;
            SetStatus($"GPU mode changed to {mode}. A reboot may be required.");
        }
        catch (NotSupportedException ex)
        {
            // Distinct from a transient failure: this operation is not offered on this
            // platform/board at all, not a command that failed to execute.
            SetStatus("GPU mode switching isn't available here.", ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("GPU switch failed.", ex.Message);
        }
        finally
        {
            IsGpuModeChanging = false;
        }
    }

    partial void OnKeyboardBrightnessChanged(int value)
    {
        _ = ApplyKeyboardBrightnessAsync();
    }

    private async Task ApplyKeyboardBrightnessAsync()
    {
        if (!_canSetKeyboardBrightness)
        {
            SetStatus("Keyboard brightness control is unavailable.", _keyboardBrightnessReason);
            return;
        }

        try
        {
            await _hardwareService.SetKeyboardBrightnessAsync(KeyboardBrightness);
        }
        catch (NotSupportedException ex)
        {
            // Distinct from a transient failure: this board/kernel path doesn't expose
            // brightness control at all, not a command that failed to execute.
            SetStatus("Keyboard brightness isn't available on this board.", ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("Keyboard brightness update failed.", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyKeyboardColor()
    {
        try
        {
            await _hardwareService.SetKeyboardColorAsync(KeyboardRed, KeyboardGreen, KeyboardBlue);
            SetStatus($"Keyboard color set to RGB({KeyboardRed}, {KeyboardGreen}, {KeyboardBlue})");
        }
        catch (Exception ex)
        {
            SetStatus("Keyboard color update failed.", ex.Message);
        }
    }

    private void SetStatus(string message, string? details = null)
    {
        StatusMessage = message;
        StatusDetails = details ?? string.Empty;
    }

    [RelayCommand]
    private void SetPresetColor(string colorName)
    {
        (KeyboardRed, KeyboardGreen, KeyboardBlue) = colorName.ToLower() switch
        {
            "blue" => ((byte)0, (byte)191, (byte)255),
            "red" => ((byte)227, (byte)24, (byte)55),
            "green" => ((byte)57, (byte)255, (byte)20),
            "purple" => ((byte)157, (byte)78, (byte)221),
            "orange" => ((byte)255, (byte)107, (byte)53),
            "white" => ((byte)255, (byte)255, (byte)255),
            "cyan" => ((byte)0, (byte)255, (byte)255),
            "yellow" => ((byte)255, (byte)255, (byte)0),
            _ => ((byte)0, (byte)191, (byte)255)
        };

        _ = ApplyKeyboardColor();
    }

    private static bool TryParsePerformanceModeName(string modeName, out PerformanceMode mode)
    {
        switch (modeName.Trim())
        {
            case "Quiet":
                mode = PerformanceMode.Quiet;
                return true;
            case "Balanced":
                mode = PerformanceMode.Balanced;
                return true;
            case "Performance":
                mode = PerformanceMode.Performance;
                return true;
            default:
                mode = PerformanceMode.Balanced;
                return false;
        }
    }

    private static bool TryGetPerformanceModeFromIndex(int index, out PerformanceMode mode)
    {
        switch (index)
        {
            case 0:
                mode = PerformanceMode.Quiet;
                return true;
            case 1:
                mode = PerformanceMode.Balanced;
                return true;
            case 2:
                mode = PerformanceMode.Performance;
                return true;
            default:
                mode = PerformanceMode.Balanced;
                return false;
        }
    }

    private static int GetPerformanceModeIndex(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Quiet => 0,
            PerformanceMode.Balanced => 1,
            PerformanceMode.Performance => 2,
            _ => 1
        };
    }

    private static string GetPerformanceModeName(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Quiet => "Quiet",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            _ => "Balanced"
        };
    }

    private void SetSelectedPerformanceModeIndex(PerformanceMode mode)
    {
        var index = GetPerformanceModeIndex(mode);
        if (SelectedPerformanceModeIndex == index)
        {
            return;
        }

        _suppressPerformanceModeSelectionChange = true;
        try
        {
            SelectedPerformanceModeIndex = index;
        }
        finally
        {
            _suppressPerformanceModeSelectionChange = false;
        }
    }
}
