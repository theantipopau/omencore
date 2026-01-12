using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Settings ViewModel for application configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _darkTheme = true;

    [ObservableProperty]
    private int _pollingInterval = 1000;

    [ObservableProperty]
    private bool _autoApplyProfile = true;

    [ObservableProperty]
    private string _defaultPerformanceMode = "Balanced";

    [ObservableProperty]
    private bool _startWithSystem;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _version = "2.3.1";
    
    [ObservableProperty]
    private bool _batteryAwareFans = true;
    
    [ObservableProperty]
    private int _batterySpeedReduction = 20;

    public string[] PerformanceModes { get; } = { "Quiet", "Balanced", "Performance" };
    public int[] PollingIntervals { get; } = { 500, 1000, 2000, 5000 };
    public int[] BatteryReductionOptions { get; } = { 10, 15, 20, 25, 30 };

    public SettingsViewModel(IConfigurationService configService)
    {
        _configService = configService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        StartMinimized = _configService.Get<bool>("start_minimized");
        DarkTheme = _configService.Get<bool>("dark_theme");
        PollingInterval = _configService.Get<int>("polling_interval_ms");
        AutoApplyProfile = _configService.Get<bool>("auto_apply_profile");
        DefaultPerformanceMode = _configService.Get<string>("default_performance_mode") ?? "Balanced";
        StartWithSystem = _configService.Get<bool>("start_with_system");
        ShowNotifications = _configService.Get<bool>("show_notifications");
        BatteryAwareFans = _configService.Get<bool>("battery_aware_fans");
        BatterySpeedReduction = _configService.Get<int>("battery_speed_reduction");
        
        if (PollingInterval == 0)
            PollingInterval = 1000;
        if (BatterySpeedReduction == 0)
            BatterySpeedReduction = 20;
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            _configService.Set("start_minimized", StartMinimized);
            _configService.Set("dark_theme", DarkTheme);
            _configService.Set("polling_interval_ms", PollingInterval);
            _configService.Set("auto_apply_profile", AutoApplyProfile);
            _configService.Set("default_performance_mode", DefaultPerformanceMode);
            _configService.Set("start_with_system", StartWithSystem);
            _configService.Set("show_notifications", ShowNotifications);
            _configService.Set("battery_aware_fans", BatteryAwareFans);
            _configService.Set("battery_speed_reduction", BatterySpeedReduction);
            
            await _configService.SaveAsync();
            StatusMessage = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        StartMinimized = false;
        DarkTheme = true;
        PollingInterval = 1000;
        AutoApplyProfile = true;
        DefaultPerformanceMode = "Balanced";
        StartWithSystem = false;
        ShowNotifications = true;
        BatteryAwareFans = true;
        BatterySpeedReduction = 20;
        
        _ = SaveSettings();
        StatusMessage = "Settings reset to defaults";
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(configDir))
        {
            configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        var omenConfigDir = Path.Combine(configDir, "omencore");

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = omenConfigDir,
                UseShellExecute = true
            });
        }
        catch
        {
            StatusMessage = $"Config folder: {omenConfigDir}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        // Reload settings to discard any unsaved changes
        LoadSettings();
        StatusMessage = "Changes discarded";
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Jeyloh/OmenCore",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }
}
