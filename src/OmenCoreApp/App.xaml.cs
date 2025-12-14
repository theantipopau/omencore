using System;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
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
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = "OmenCore_SingleInstance_Mutex";
        
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
            // Check for single instance - prevent multiple copies running
            if (!AcquireSingleInstance())
            {
                MessageBox.Show(
                    "OmenCore is already running.\n\nLook for the OmenCore icon in your system tray (notification area).",
                    "OmenCore Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
            
            base.OnStartup(e);
            Logging.Initialize();
            
            // Apply log level from configuration
            Logging.Level = Configuration.Config.LogLevel;
            
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
                // Try multiple device paths for different WinRing0 builds
                var devicePaths = new[] { "\\\\.\\WinRing0_1_2_0", "\\\\.\\WinRing0_1_2", "\\\\.\\WinRing0" };
                var winRing0Detected = false;

                foreach (var devicePath in devicePaths)
                {
                    var handle = NativeMethods.CreateFile(
                        devicePath,
                        NativeMethods.GENERIC_READ,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        NativeMethods.OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (!handle.IsInvalid && !handle.IsClosed)
                    {
                        winRing0Detected = true;
                        handle.Close();
                        break;
                    }

                    handle.Close();
                }

                if (!winRing0Detected)
                {
                    var secureBootEnabled = IsSecureBootEnabled();
                    var memoryIntegrityEnabled = IsMemoryIntegrityEnabled();

                    Logging.Warn("⚠️ WinRing0 driver not detected - some features may be unavailable");
                    Logging.Info("💡 Fan control may still work via WMI/OGH without WinRing0; MSR-based undervolting/TCC and direct EC access require a driver backend.");

                    if (secureBootEnabled || memoryIntegrityEnabled)
                    {
                        Logging.Info("💡 Windows security features may block WinRing0. Consider PawnIO (Secure Boot compatible) from https://pawnio.eu/");
                    }
                    else
                    {
                        Logging.Info("💡 To use WinRing0-dependent features: install/run LibreHardwareMonitor as Administrator or see docs/WINRING0_SETUP.md");
                    }

                    // Prompt user only on first startup if driver missing
                    if (!Configuration.Config.FirstRunCompleted)
                    {
                        Dispatcher.Invoke(() => PromptDriverInstallation(secureBootEnabled, memoryIntegrityEnabled));
                    }
                }
                else
                {
                    Logging.Info("✓ WinRing0 driver detected - WinRing0-dependent features available");
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

                // Initial sync: force access to SystemControl/Dashboard to load saved modes,
                // then sync to tray immediately AFTER subscriptions are set up
                var _ = mainViewModel.Dashboard; // Trigger lazy load
                var __ = mainViewModel.SystemControl; // Trigger lazy load
                
                // Now sync to tray with actual values
                _trayIconService?.UpdateFanMode(mainViewModel.CurrentFanMode);
                _trayIconService?.UpdatePerformanceMode(mainViewModel.CurrentPerformanceMode);
            }
            
            // Wire up Stay on Top toggle
            if (_trayIconService != null)
            {
                _trayIconService.StayOnTopChanged += (stayOnTop) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow != null)
                        {
                            MainWindow.Topmost = stayOnTop;
                        }
                    });
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

        private void PromptDriverInstallation(bool secureBootEnabled, bool memoryIntegrityEnabled)
        {
            var recommendPawnIo = secureBootEnabled || memoryIntegrityEnabled;
            var recommendedBackend = recommendPawnIo
                ? "PawnIO (recommended on Secure Boot/Memory Integrity systems)"
                : "LibreHardwareMonitor (provides WinRing0)";

            var result = MessageBox.Show(
                "Some hardware-control features require a driver backend.\n\n" +
                "Depending on your system, you can use:\n" +
                "• PawnIO (Secure Boot compatible)\n" +
                "• WinRing0 (often provided by LibreHardwareMonitor)\n\n" +
                "Without a supported driver backend, these features may be disabled:\n" +
                "• Direct EC fan control (some models)\n" +
                "• CPU undervolting and TCC offset (Intel MSR)\n\n" +
                $"Recommended: {recommendedBackend}\n\n" +
                (recommendPawnIo
                    ? "Click YES to open PawnIO download page\n"
                    : "Click YES to download LibreHardwareMonitor now\n") +
                "Click NO to continue without driver-dependent features",
                "Driver Required - OmenCore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            // Mark first run as completed after showing prompt
            Configuration.Config.FirstRunCompleted = true;
            Configuration.Save(Configuration.Config);

            if (result == MessageBoxResult.Yes)
            {
                if (recommendPawnIo)
                {
                    OpenPawnIODownloadPage();
                }
                else
                {
                    DownloadAndInstallLibreHardwareMonitor();
                }
            }
        }

        private static void OpenPawnIODownloadPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://pawnio.eu/",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore
            }
        }

        private static bool IsSecureBootEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                var value = key?.GetValue("UEFISecureBootEnabled");
                return value != null && Convert.ToInt32(value) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMemoryIntegrityEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                var value = key?.GetValue("Enabled");
                return value != null && Convert.ToInt32(value) == 1;
            }
            catch
            {
                return false;
            }
        }

        private async void DownloadAndInstallLibreHardwareMonitor()
        {
            const string downloadUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.3/LibreHardwareMonitor-net472.zip";
            // Use a unique temp folder with timestamp to avoid file-in-use conflicts
            var uniqueId = DateTime.Now.Ticks.ToString();
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OmenCore_LHM_{uniqueId}");
            var zipPath = System.IO.Path.Combine(tempDir, "LibreHardwareMonitor.zip");
            var extractPath = System.IO.Path.Combine(tempDir, "LibreHardwareMonitor");

            try
            {
                // Show progress dialog
                Logging.Info("📥 Downloading LibreHardwareMonitor...");

                // Create temp directory (unique, so no conflicts)
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

                // Extract ZIP (no need to delete - unique folder)
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

        /// <summary>
        /// Try to acquire a mutex to ensure only one instance of OmenCore runs at a time.
        /// </summary>
        private static bool AcquireSingleInstance()
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
                if (!createdNew)
                {
                    // Another instance is already running
                    _singleInstanceMutex?.Dispose();
                    _singleInstanceMutex = null;
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                // If mutex creation fails, allow the app to run anyway
                return true;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _trayIcon?.Dispose();
            
            // Release single instance mutex
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                }
                catch { }
            }
            
            Logging.Info("OmenCore shutting down");
            Logging.Dispose();
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            public const uint OPEN_EXISTING = 3;

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
