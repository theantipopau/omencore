using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmenCore.Avalonia.Services;
using OmenCore.Avalonia.ViewModels;
using OmenCore.Avalonia.Views;
using System;

namespace OmenCore.Avalonia;

/// <summary>
/// Main application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }
    
    public override void Initialize()
    {
        Program.WriteStartupTrace("app.initialize.start");
        AvaloniaXamlLoader.Load(this);
        Program.WriteStartupTrace("app.initialize.loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Program.WriteStartupTrace("app.framework-init.start");

        // Configure dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            Program.WriteStartupTrace("app.main-window.created");
        }

        base.OnFrameworkInitializationCompleted();
        Program.WriteStartupTrace("app.framework-init.completed");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
        });
        
        // Services
        services.AddSingleton<IHardwareService, LinuxHardwareService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IFanCurveService, FanCurveService>();
        
        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<FanControlViewModel>();
        services.AddTransient<SystemControlViewModel>();
        services.AddTransient<SettingsViewModel>();
    }
}
