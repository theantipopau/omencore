using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using OmenCore.Services;
using OmenCore.Utils;
using OmenCore.ViewModels;
using OmenCore.Views;

namespace OmenCore
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;
        private TaskbarIcon? _trayIcon;
        private TrayIconService? _trayIconService;

        public static LoggingService Logging { get; } = new();
        public static ConfigurationService Configuration { get; } = new();

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Logging.Initialize();
            var version = typeof(App).Assembly.GetName().Version;
            Logging.Info($"OmenCore v1.0.0.5 starting up (Assembly: {version?.ToString(3) ?? "Unknown"})");

            // Check for WinRing0 driver availability
            CheckDriverStatus();

            // Configure dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Initialize system tray
            InitializeTrayIcon();

            // Create and show main window with DI
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void CheckDriverStatus()
        {
            try
            {
                var devicePath = "\\\\.\\WinRing0_1_2";
                var handle = NativeMethods.CreateFile(
                    devicePath,
                    0, // GENERIC_READ
                    0,
                    IntPtr.Zero,
                    3, // OPEN_EXISTING
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid || handle.IsClosed)
                {
                    Logging.Warn("⚠️ WinRing0 driver not detected - fan control and undervolt will be disabled");
                    Logging.Info("💡 To enable fan control: Install LibreHardwareMonitor or see docs/WINRING0_SETUP.md");
                    
                    // Prompt user only on first startup if driver missing
                    if (!Configuration.Config.FirstRunCompleted)
                    {
                        Dispatcher.Invoke(() => PromptDriverInstallation());
                    }
                }
                else
                {
                    Logging.Info("✓ WinRing0 driver detected - full hardware control available");
                    handle.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.Warn($"Driver check failed: {ex.Message}");
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                IconSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/OmenCore.ico")),
                ToolTipText = "OmenCore - Gaming Laptop Control"
            };

            _trayIconService = new TrayIconService(_trayIcon, ShowMainWindow, () => Shutdown());
            _trayIcon.TrayLeftMouseUp += (s, e) => ShowMainWindow();

            // Wire up to MainViewModel for monitoring updates
            var mainViewModel = _serviceProvider?.GetRequiredService<MainViewModel>();
            if (mainViewModel?.Dashboard != null)
            {
                mainViewModel.Dashboard.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DashboardViewModel.LatestMonitoringSample))
                    {
                        var sample = mainViewModel.Dashboard.LatestMonitoringSample;
                        if (sample != null)
                        {
                            _trayIconService?.UpdateMonitoringSample(sample);
                        }
                    }
                };
            }
        }

        private void ShowMainWindow()
        {
            var mainWindow = MainWindow;
            if (mainWindow != null)
            {
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();
            }
        }

        private void PromptDriverInstallation()
        {
            var result = MessageBox.Show(
                "OmenCore requires the WinRing0 driver for advanced hardware control.\n\n" +
                "⚠️ IMPORTANT: Windows Defender may flag WinRing0 as a virus.\n" +
                "This is a FALSE POSITIVE - the driver is safe when using trusted sources.\n\n" +
                "Without this driver, the following features are disabled:\n" +
                "• Manual fan curve control\n" +
                "• CPU voltage offset (undervolting)\n" +
                "• Direct EC register access\n\n" +
                "The easiest option is to install LibreHardwareMonitor which bundles this driver.\n\n" +
                "Would you like to view the setup guide?",
                "Driver Not Found - OmenCore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            // Mark first run as completed after showing prompt
            Configuration.Config.FirstRunCompleted = true;
            Configuration.Save(Configuration.Config);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Try bundled docs first, then installed location, finally fallback to online
                    var installDocPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "WINRING0_SETUP.md");
                    var devDocPath = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\docs\WINRING0_SETUP.md"));
                    
                    if (System.IO.File.Exists(installDocPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = installDocPath,
                            UseShellExecute = true
                        });
                        Logging.Info("📄 Opened installed WINRING0_SETUP.md");
                    }
                    else if (System.IO.File.Exists(devDocPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = devDocPath,
                            UseShellExecute = true
                        });
                        Logging.Info("📄 Opened dev WINRING0_SETUP.md");
                    }
                    else
                    {
                        // Fallback: Open online documentation
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://github.com/theantipopau/omencore/blob/main/docs/WINRING0_SETUP.md",
                            UseShellExecute = true
                        });
                        Logging.Info("🌐 Opened online WINRING0_SETUP.md");
                    }
                }
                catch (Exception ex)
                {
                    Logging.Error("Failed to open driver setup documentation", ex);
                    MessageBox.Show(
                        "Could not open setup guide.\n\n" +
                        "Please install LibreHardwareMonitor from:\n" +
                        "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases\n\n" +
                        "Or see docs/WINRING0_SETUP.md in the OmenCore installation folder.",
                        "Setup Guide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register ViewModels (MainViewModel creates all services internally for now)
            services.AddSingleton<MainViewModel>();

            // Register Views
            services.AddTransient<MainWindow>();
            
            // TODO: Future refactoring - register all services here and inject into ViewModels
            // This would require breaking down MainViewModel's constructor to accept dependencies
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _trayIcon?.Dispose();
            Logging.Info("OmenCore shutting down");
            Logging.Dispose();
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logging.Error("Unhandled UI thread exception", e.Exception);
            e.Handled = true;
            ShowFatalDialog(e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logging.Error("Unhandled AppDomain exception", ex);
                ShowFatalDialog(ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logging.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
            ShowFatalDialog(e.Exception);
        }

        private static void ShowFatalDialog(Exception ex)
        {
            MessageBox.Show($"OmenCore hit an unexpected error:\n{ex.Message}\n\nSee %LOCALAPPDATA%\\OmenCore for full logs.", "OmenCore Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown();
        }
    }
}
