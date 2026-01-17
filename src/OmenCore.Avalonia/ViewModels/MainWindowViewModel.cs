using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmenCore.Avalonia.Services;
using System.Diagnostics;
using System.Reflection;

namespace OmenCore.Avalonia.ViewModels;

/// <summary>
/// Main window ViewModel handling navigation.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IHardwareService _hardwareService;
    private readonly IConfigurationService _configService;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _currentPage = "Dashboard";

    [ObservableProperty]
    private string _modelName = "HP OMEN";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isConnected = true;

    [ObservableProperty]
    private string _performanceMode = "Balanced";

    [ObservableProperty]
    private string _fanMode = "Auto";

    [ObservableProperty]
    private string _appVersion = "2.5.0";

    // Navigation state
    [ObservableProperty]
    private bool _isDashboardActive = true;

    [ObservableProperty]
    private bool _isFanControlActive;

    [ObservableProperty]
    private bool _isSystemControlActive;

    [ObservableProperty]
    private bool _isSettingsActive;

    public DashboardViewModel DashboardVm { get; }
    public FanControlViewModel FanControlVm { get; }
    public SystemControlViewModel SystemControlVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public MainWindowViewModel(
        IHardwareService hardwareService,
        IConfigurationService configService,
        DashboardViewModel dashboardVm,
        FanControlViewModel fanControlVm,
        SystemControlViewModel systemControlVm,
        SettingsViewModel settingsVm)
    {
        _hardwareService = hardwareService;
        _configService = configService;
        DashboardVm = dashboardVm;
        FanControlVm = fanControlVm;
        SystemControlVm = systemControlVm;
        SettingsVm = settingsVm;

        CurrentView = DashboardVm;
        
        // Get version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            AppVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        }
        
        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            await _configService.LoadAsync();
            var capabilities = await _hardwareService.GetCapabilitiesAsync();
            ModelName = capabilities.ModelName;
            StatusText = "Connected";
            IsConnected = true;

            // Get current modes
            var perfMode = await _hardwareService.GetPerformanceModeAsync();
            PerformanceMode = perfMode.ToString();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsConnected = false;
        }
    }

    private void SetActiveNavigation(string page)
    {
        IsDashboardActive = page == "Dashboard";
        IsFanControlActive = page == "Fan Control";
        IsSystemControlActive = page == "System Control";
        IsSettingsActive = page == "Settings";
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        CurrentView = DashboardVm;
        CurrentPage = "Dashboard";
        SetActiveNavigation("Dashboard");
    }

    [RelayCommand]
    private void NavigateToFanControl()
    {
        CurrentView = FanControlVm;
        CurrentPage = "Fan Control";
        SetActiveNavigation("Fan Control");
    }

    [RelayCommand]
    private void NavigateToSystemControl()
    {
        CurrentView = SystemControlVm;
        CurrentPage = "System Control";
        SetActiveNavigation("System Control");
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = SettingsVm;
        CurrentPage = "Settings";
        SetActiveNavigation("Settings");
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
            StatusText = "Refreshing...";
            var status = await _hardwareService.GetStatusAsync();
            StatusText = "Connected";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        OpenUrl("https://github.com/theantipopau/omencore");
    }
    
    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback for Linux when xdg-open fails
            try
            {
                // Try common Linux browsers directly
                var browsers = new[] { "firefox", "chromium", "google-chrome", "brave-browser", "xdg-open" };
                foreach (var browser in browsers)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = browser,
                            Arguments = url,
                            UseShellExecute = false,
                            RedirectStandardError = true
                        });
                        return;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
