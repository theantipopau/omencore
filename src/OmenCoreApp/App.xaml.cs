using System;
using System.IO.Compression;
using System.Reflection;
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
            var asm = Assembly.GetExecutingAssembly();
            var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
            var asmVer = asm.GetName().Version?.ToString() ?? "unknown";
            Logging.Info($"OmenCore v{fileVer} starting up (Assembly: {asmVer})");

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

            // Wire up to MainViewModel for monitoring updates and tray actions
            var mainViewModel = _serviceProvider?.GetRequiredService<MainViewModel>();
            if (mainViewModel != null)
            {
                // Subscribe to monitoring updates
                if (mainViewModel.Dashboard != null)
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

                // Wire up tray quick actions to MainViewModel
                _trayIconService.FanModeChangeRequested += mode =>
                {
                    mainViewModel.SetFanModeFromTray(mode);
                };

                _trayIconService.PerformanceModeChangeRequested += mode =>
                {
                    mainViewModel.SetPerformanceModeFromTray(mode);
                };

                // Subscribe to MainViewModel mode changes to update tray display
                mainViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.CurrentFanMode))
                    {
                        _trayIconService?.UpdateFanMode(mainViewModel.CurrentFanMode);
                    }
                    else if (e.PropertyName == nameof(MainViewModel.CurrentPerformanceMode))
                    {
                        _trayIconService?.UpdatePerformanceMode(mainViewModel.CurrentPerformanceMode);
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
                "OmenCore requires the WinRing0 driver for fan control and undervolting.\n\n" +
                "The easiest way to install this driver is through LibreHardwareMonitor.\n\n" +
                "Without this driver, these features are disabled:\n" +
                "• Manual fan curve control\n" +
                "• CPU undervolting\n" +
                "• Direct EC register access\n\n" +
                "Click YES to download LibreHardwareMonitor (recommended)\n" +
                "Click NO to continue without these features",
                "Driver Required - OmenCore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            // Mark first run as completed after showing prompt
            Configuration.Config.FirstRunCompleted = true;
            Configuration.Save(Configuration.Config);

            if (result == MessageBoxResult.Yes)
            {
                DownloadAndInstallLibreHardwareMonitor();
            }
        }

        private async void DownloadAndInstallLibreHardwareMonitor()
        {
            const string downloadUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.3/LibreHardwareMonitor-net472.zip";
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenCore_LHM");
            var zipPath = System.IO.Path.Combine(tempDir, "LibreHardwareMonitor.zip");
            var extractPath = System.IO.Path.Combine(tempDir, "LibreHardwareMonitor");

            try
            {
                // Show progress dialog
                Logging.Info("📥 Downloading LibreHardwareMonitor...");

                // Create temp directory
                System.IO.Directory.CreateDirectory(tempDir);

                // Download the ZIP file
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    await System.IO.File.WriteAllBytesAsync(zipPath, bytes);
                }

                Logging.Info("✓ Download complete, extracting...");

                // Extract ZIP
                if (System.IO.Directory.Exists(extractPath))
                    System.IO.Directory.Delete(extractPath, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Find and run LibreHardwareMonitor.exe
                var exePath = System.IO.Path.Combine(extractPath, "LibreHardwareMonitor.exe");
                if (System.IO.File.Exists(exePath))
                {
                    Logging.Info("🚀 Launching LibreHardwareMonitor to install driver...");
                    
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas" // Run as admin to install driver
                    };
                    
                    System.Diagnostics.Process.Start(startInfo);

                    MessageBox.Show(
                        "LibreHardwareMonitor has been downloaded and launched.\n\n" +
                        "IMPORTANT STEPS:\n" +
                        "1. Let it run for a few seconds (this installs the driver)\n" +
                        "2. You can close LibreHardwareMonitor after it opens\n" +
                        "3. Restart OmenCore to enable fan control\n\n" +
                        "⚠️ If Windows Defender blocks it, click 'More info' → 'Run anyway'\n" +
                        "The driver is safe - it's used by many hardware monitoring tools.",
                        "Driver Installation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    throw new System.IO.FileNotFoundException("LibreHardwareMonitor.exe not found in downloaded archive");
                }
            }
            catch (Exception ex)
            {
                Logging.Error("Failed to download/install LibreHardwareMonitor", ex);
                
                // Fallback: Open download page in browser
                var fallbackResult = MessageBox.Show(
                    $"Automatic download failed: {ex.Message}\n\n" +
                    "Would you like to open the download page in your browser instead?",
                    "Download Failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (fallbackResult == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest",
                        UseShellExecute = true
                    });
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
