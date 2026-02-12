using System;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
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
        
        // Track remote session state to prevent window activation during RDP
        private static bool _isInRemoteSession;
        private static DateTime _lastSessionUnlock = DateTime.MinValue;
        private const int SessionUnlockGracePeriodMs = 2000;  // Ignore activations for 2s after unlock

        public static LoggingService Logging { get; } = new();
        public static ConfigurationService Configuration { get; } = new();
        public static IServiceProvider? ServiceProvider => ((App)Current)._serviceProvider;
        
        /// <summary>
        /// Returns true if a remote session change recently occurred that should suppress window activation.
        /// </summary>
        public static bool ShouldSuppressWindowActivation =>
            _isInRemoteSession || (DateTime.Now - _lastSessionUnlock).TotalMilliseconds < SessionUnlockGracePeriodMs;
        
        /// <summary>
        /// Tray icon service instance for external access (e.g., from SettingsViewModel).
        /// </summary>
        public static TrayIconService? TrayIcon { get; private set; }

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
                // Another instance is running - try to bring it to front
                BringExistingInstanceToFront();
                Shutdown();
                return;
            }
            
            base.OnStartup(e);
            Logging.Initialize();
            
            // Subscribe to session switch events to prevent window activation during RDP
            SystemEvents.SessionSwitch += OnSessionSwitch;
            
            // Apply log level from configuration
            Logging.Level = Configuration.Config.LogLevel;
            
            var asm = Assembly.GetExecutingAssembly();
            var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
            var asmVer = asm.GetName().Version?.ToString() ?? "unknown";
            Logging.Info($"OmenCore v{fileVer} starting up (Assembly: {asmVer})");
            
            // Log command line arguments for debugging
            if (e.Args.Length > 0)
            {
                Logging.Info($"Command line arguments: {string.Join(" ", e.Args)}");
            }

            // Check for desktop systems and warn (experimental desktop support since v2.8.0)
            if (IsOmenDesktop())
            {
                Logging.Warn("OMEN Desktop PC detected - desktop support is experimental");
                var result = MessageBox.Show(
                    "⚠️ OMEN Desktop PC Detected ⚠️\n\n" +
                    "OmenCore has experimental support for OMEN Desktop systems (25L, 30L, 35L, 40L, 45L).\n\n" +
                    "Desktop fan control uses different hardware interfaces than laptops. " +
                    "Some features may not work correctly:\n\n" +
                    "• Fan speed control may be limited or unavailable\n" +
                    "• Temperature monitoring should work normally\n" +
                    "• RGB control is supported\n\n" +
                    "If you experience any issues, please report them on GitHub or Discord.\n\n" +
                    "Do you want to continue?",
                    "OMEN Desktop - Experimental Support",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                {
                    Logging.Info("User chose not to continue on desktop system");
                    Shutdown();
                    return;
                }
                
                Logging.Info("User chose to continue on desktop system - experimental mode");
            }

            // Check for WinRing0 driver availability
            CheckDriverStatus();

            // Configure dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Initialize system tray
            InitializeTrayIcon();

            // Create main window with DI
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            
            // Check if we should start minimized to tray
            // Priority: command line flag > config setting
            bool hasMinimizedFlag = e.Args.Contains("--minimized") || e.Args.Contains("-m") || e.Args.Contains("/minimized");
            bool startMinimized = hasMinimizedFlag || (Configuration.Config.Monitoring?.StartMinimized ?? false);
            
            if (startMinimized)
            {
                // Start minimized to tray - don't show window but initialize it
                Logging.Info("Starting minimized to system tray");
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                // Initialize the window (triggers Loaded event for hotkey registration) but keep it hidden
                mainWindow.Show();
                mainWindow.Hide();
            }
            else
            {
                // Normal startup - show window
                mainWindow.Show();
            }
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

        /// <summary>
        /// Detect if this is an OMEN Desktop PC (NOT laptop).
        /// Desktop systems use different thermal management and fan control is incompatible.
        /// Returns true ONLY for confirmed HP OMEN desktops, not generic desktops.
        /// </summary>
        private bool IsOmenDesktop()
        {
            try
            {
                bool isHpSystem = false;
                string model = "";
                
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_ComputerSystem");
                
                foreach (var obj in searcher.Get())
                {
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    model = obj["Model"]?.ToString() ?? "";
                    
                    // Check for HP manufacturer
                    if (manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase) ||
                        manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase))
                    {
                        isHpSystem = true;
                        
                        // Check for known OMEN desktop models by name
                        if (model.Contains("25L", StringComparison.OrdinalIgnoreCase) ||
                            model.Contains("30L", StringComparison.OrdinalIgnoreCase) ||
                            model.Contains("35L", StringComparison.OrdinalIgnoreCase) ||
                            model.Contains("40L", StringComparison.OrdinalIgnoreCase) ||
                            model.Contains("45L", StringComparison.OrdinalIgnoreCase) ||
                            model.Contains("Obelisk", StringComparison.OrdinalIgnoreCase))
                        {
                            Logging.Info($"OMEN Desktop detected by model: {manufacturer} {model}");
                            return true;
                        }
                    }
                }
                
                // Only check chassis type for HP systems with OMEN in the model name
                // This prevents false positives on non-HP desktops or HP laptops
                if (isHpSystem && model.Contains("OMEN", StringComparison.OrdinalIgnoreCase))
                {
                    using var chassisSearcher = new System.Management.ManagementObjectSearcher(
                        "SELECT ChassisTypes FROM Win32_SystemEnclosure");
                    
                    foreach (var obj in chassisSearcher.Get())
                    {
                        if (obj["ChassisTypes"] is ushort[] chassisTypes && chassisTypes.Length > 0)
                        {
                            var chassis = chassisTypes[0];
                            // Desktop chassis types: 3=Desktop, 4=LowProfileDesktop, 5=PizzaBox, 
                            // 6=MiniTower, 7=Tower, 13=AllInOne, 15=SpaceSaving
                            if (chassis == 3 || chassis == 4 || chassis == 5 || chassis == 6 || 
                                chassis == 7 || chassis == 13 || chassis == 15)
                            {
                                Logging.Info($"OMEN Desktop detected by chassis type: {chassis} (model: {model})");
                                return true;
                            }
                        }
                    }
                }
                
                // Not an OMEN desktop - allow the app to continue
                // Non-HP desktops will still work (monitoring mode if fan control fails)
                Logging.Info($"Desktop check passed: not an OMEN desktop (HP={isHpSystem}, Model={model})");
            }
            catch (Exception ex)
            {
                Logging.Warn($"Desktop detection failed: {ex.Message}");
            }
            
            return false;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                IconSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/OmenCore.ico")),
                ToolTipText = "OmenCore - Gaming Laptop Control\n\nLeft-click: Quick Popup\nDouble-click: Open Dashboard\nRight-click: Menu"
            };
            
            // Retry tray icon visibility after boot (Windows sometimes fails to show icons during login)
            _ = EnsureTrayIconVisibleAsync();

            var configService = _serviceProvider?.GetService<ConfigurationService>();
            _trayIconService = new TrayIconService(_trayIcon, ForceShowMainWindow, () => Shutdown(), configService);
            TrayIcon = _trayIconService; // Expose for static access (e.g., SettingsViewModel)
            _trayIcon.TrayLeftMouseUp += (s, e) => _trayIconService?.ShowQuickPopup(); // Quick popup like G-Helper
            _trayIcon.TrayLeftMouseDown += (s, e) => { }; // Handle double-click below
            _trayIcon.TrayMouseDoubleClick += (s, e) => ForceShowMainWindow(); // Full window on double-click

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
                
                _trayIconService.QuickProfileChangeRequested += profile =>
                {
                    mainViewModel.ApplyQuickProfileFromTray(profile);
                };
                
                // v2.7.0: GPU Power and Keyboard backlight quick actions
                _trayIconService.GpuPowerChangeRequested += level =>
                {
                    mainViewModel.SetGpuPowerFromTray(level);
                };
                
                _trayIconService.KeyboardBacklightChangeRequested += level =>
                {
                    mainViewModel.SetKeyboardBacklightFromTray(level);
                };
                
                _trayIconService.KeyboardBacklightToggleRequested += () =>
                {
                    mainViewModel.ToggleKeyboardBacklightFromTray();
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
                    else if (e.PropertyName == nameof(MainViewModel.CurrentGpuPowerLevel))
                    {
                        _trayIconService?.SetGpuPowerLevel(mainViewModel.CurrentGpuPowerLevel);
                    }
                    else if (e.PropertyName == nameof(MainViewModel.CurrentKeyboardBrightness))
                    {
                        _trayIconService?.SetKeyboardBrightness(mainViewModel.CurrentKeyboardBrightness);
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
        
        /// <summary>
        /// Retry tray icon visibility after boot.
        /// Windows sometimes fails to show tray icons created before Explorer is fully ready.
        /// </summary>
        private async Task EnsureTrayIconVisibleAsync()
        {
            // Wait a bit for Windows shell to be ready
            await Task.Delay(3000);
            
            // Force icon visibility refresh by toggling visibility
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_trayIcon != null)
                    {
                        // Toggle visibility to force Windows to re-register the icon
                        var currentVisibility = _trayIcon.Visibility;
                        _trayIcon.Visibility = System.Windows.Visibility.Collapsed;
                        _trayIcon.Visibility = currentVisibility;
                        
                        // Also refresh the icon source to force re-render
                        var icon = _trayIcon.IconSource;
                        _trayIcon.IconSource = null;
                        _trayIcon.IconSource = icon;
                    }
                });
                
                // Additional retry after longer delay for slow boot scenarios
                await Task.Delay(5000);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visibility = System.Windows.Visibility.Collapsed;
                        _trayIcon.Visibility = System.Windows.Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                Logging.Warn($"Tray icon refresh failed: {ex.Message}");
            }
        }

        private void ShowMainWindow()
        {
            // Suppress window activation during remote session changes (e.g., RDP)
            // This prevents the window from popping up when user connects to another PC via RDP
            if (ShouldSuppressWindowActivation)
            {
                Logging.Debug("Window activation suppressed (remote session state change)");
                return;
            }
            
            ActivateMainWindow();
        }
        
        /// <summary>
        /// Force-show the main window, bypassing remote session suppression.
        /// Used for explicit user actions (tray double-click, context menu "Open Dashboard").
        /// </summary>
        private void ForceShowMainWindow()
        {
            ActivateMainWindow();
        }
        
        private void ActivateMainWindow()
        {
            var mainWindow = MainWindow;
            if (mainWindow != null)
            {
                // Restore taskbar visibility when showing from tray
                mainWindow.ShowInTaskbar = true;
                
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();
                mainWindow.Focus();
            }
        }
        
        /// <summary>
        /// Handle Windows session state changes (RDP connect/disconnect, lock/unlock, etc.)
        /// This prevents the window from being activated when the user starts an RDP session.
        /// </summary>
        private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.RemoteConnect:
                    _isInRemoteSession = true;
                    Logging.Debug($"Session switch: {e.Reason} - suppressing window activation");
                    break;
                    
                case SessionSwitchReason.RemoteDisconnect:
                    _isInRemoteSession = false;
                    _lastSessionUnlock = DateTime.Now;
                    Logging.Debug($"Session switch: {e.Reason} - grace period started");
                    break;
                    
                case SessionSwitchReason.SessionLock:
                    _isInRemoteSession = true;
                    Logging.Debug($"Session switch: {e.Reason} - suppressing window activation");
                    break;
                    
                case SessionSwitchReason.SessionUnlock:
                    _isInRemoteSession = false;
                    _lastSessionUnlock = DateTime.Now;
                    Logging.Debug($"Session switch: {e.Reason} - grace period started");
                    break;
                    
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.ConsoleDisconnect:
                    // Console connect/disconnect also happen during RDP - suppress briefly
                    _lastSessionUnlock = DateTime.Now;
                    Logging.Debug($"Session switch: {e.Reason} - grace period started");
                    break;
                    
                default:
                    Logging.Debug($"Session switch: {e.Reason}");
                    break;
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
        
        /// <summary>
        /// Attempts to bring an existing instance of OmenCore to the front.
        /// Uses window enumeration to find and activate the existing window.
        /// </summary>
        private static void BringExistingInstanceToFront()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName))
                {
                    if (process.Id != currentProcess.Id && process.MainWindowHandle != IntPtr.Zero)
                    {
                        // Found another instance with a window - bring it to front
                        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                        
                        // If minimized, restore it
                        if (NativeMethods.IsIconic(process.MainWindowHandle))
                        {
                            NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
                        }
                        return;
                    }
                }
                
                // No window found - the other instance might be minimized to tray
                // Post a message to show the window (using registered window message)
                var hwnd = NativeMethods.FindWindow(null, "OmenCore");
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                }
            }
            catch
            {
                // Silently fail - the message box fallback was already removed
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Restore fans to default/Windows auto control before shutdown
            // This ensures fans return to BIOS/Windows default behavior instead of staying at last manual setting
            try
            {
                Logging.Info("OmenCore shutting down (restoring fans to auto control)...");
                
                // Explicitly restore fan auto control before disposing services
                var fanService = _serviceProvider?.GetService(typeof(FanService)) as FanService;
                fanService?.ApplyAutoMode();
            }
            catch (Exception ex)
            {
                Logging.Debug($"Error restoring fan auto control during shutdown: {ex.Message}");
            }

            // Unsubscribe from session switch events
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            
            _trayIconService?.Dispose();
            _trayIcon?.Dispose();
            
            // Dispose MainViewModel to properly clean up services and restore fan control
            try
            {
                var mainViewModel = _serviceProvider?.GetService<MainViewModel>();
                mainViewModel?.Dispose();
            }
            catch { }
            
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
            
            Logging.Info("OmenCore shutdown complete");
            Logging.Dispose();
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint FILE_SHARE_READ = 0x00000001;
            public const uint FILE_SHARE_WRITE = 0x00000002;
            public const uint OPEN_EXISTING = 3;
            public const int SW_RESTORE = 9;

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);
            
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
            
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);
            
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logging.Error("Unhandled UI thread exception", e.Exception);
            e.Handled = true;
            ShowFatalDialog(e.Exception, false);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                // Special handling for NVML/GPU driver crashes
                bool isNvmlCrash = ex is AccessViolationException || 
                                   ex.StackTrace?.Contains("NvidiaML") == true ||
                                   ex.StackTrace?.Contains("nvml.dll") == true ||
                                   ex.StackTrace?.Contains("NvidiaGpu") == true;
                
                if (isNvmlCrash)
                {
                    Logging.Error("NVIDIA NVML driver crash detected! This is a known driver issue during high GPU load. Try updating your NVIDIA drivers.", ex);
                }
                else
                {
                    Logging.Error("Unhandled AppDomain exception", ex);
                }
                
                ShowFatalDialog(ex, isNvmlCrash);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // Log all unobserved task exceptions
            Logging.Error("Unobserved task exception", e.Exception);
            
            // Mark as observed to prevent crash
            e.SetObserved();
            
            // Only show fatal dialog for truly fatal errors, not connection failures
            var innerException = e.Exception?.InnerException;
            if (innerException is System.Net.Sockets.SocketException ||
                innerException is System.IO.IOException ||
                innerException is System.TimeoutException ||
                innerException is OperationCanceledException ||
                innerException is TaskCanceledException)
            {
                // These are non-fatal connection/IO errors - just log them
                Logging.Warn($"Non-fatal async error (suppressed): {innerException.Message}");
                return;
            }
            
            // Show dialog for other serious errors
            if (e.Exception != null)
                ShowFatalDialog(e.Exception, false);
        }

        private static void ShowFatalDialog(Exception ex, bool isNvmlCrash = false)
        {
            // Ensure we're on the UI thread
            if (Current?.Dispatcher.CheckAccess() == false)
            {
                Current.Dispatcher.Invoke(() => ShowFatalDialog(ex, isNvmlCrash));
                return;
            }
            
            string message;
            if (isNvmlCrash)
            {
                message = $"OmenCore crashed due to an NVIDIA driver issue.\n\n" +
                          $"This is a known issue with NVML (NVIDIA Management Library) during high GPU load " +
                          $"such as gaming or benchmarks.\n\n" +
                          $"Recommendation: Update your NVIDIA drivers to the latest version.\n\n" +
                          $"Error: {ex.Message}";
            }
            else
            {
                message = $"OmenCore hit an unexpected error:\n{ex.Message}\n\nSee %LOCALAPPDATA%\\OmenCore for full logs.";
            }
            
            MessageBox.Show(message, "OmenCore Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            Current?.Shutdown();
        }
    }
}
