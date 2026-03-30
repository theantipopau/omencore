using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using OmenCore.Controls;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Views;

namespace OmenCore.Utils
{
    /// <summary>
    /// Manages the system tray icon with live temperature monitoring and quick actions.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private readonly DispatcherTimer _updateTimer;
        private readonly Action _showMainWindow;
        private readonly Action _shutdownApp;
        private readonly ImageSource? _baseIconSource;
        private readonly DisplayService _displayService;
        private readonly string _appVersion;
        private QuickPopupWindow? _quickPopup;
        private MenuItem? _cpuTempMenuItem;
        private MenuItem? _gpuTempMenuItem;
        private MenuItem? _fanModeMenuItem;
        private MenuItem? _performanceModeMenuItem;
        private MenuItem? _stayOnTopMenuItem;
        private MenuItem? _displayMenuItem;  // For updating refresh rate display
        private MenuItem? _monitoringHealthMenuItem;
        private MonitoringSample? _latestSample;
        private string _currentFanMode = "Auto";
        private string _currentPerformanceMode = "Balanced";
        private bool _linkFanToPerformanceMode;
        private string _monitoringHealth = "Unknown";
        private bool _disposed;
        private readonly ConfigurationService? _configService;
        
        // v2.6.1: Track fan menu items for checkmarks
        private MenuItem? _fanAutoMenuItem;
        private MenuItem? _fanMaxMenuItem;
        private MenuItem? _fanQuietMenuItem;
        private MenuItem? _perfBalancedMenuItem;
        private MenuItem? _perfPerformanceMenuItem;
        private MenuItem? _perfQuietMenuItem;
        
        // v2.7.0: GPU Power and Keyboard backlight menu items
        private MenuItem? _gpuPowerMenuItem;
        private MenuItem? _gpuPowerMinMenuItem;
        private MenuItem? _gpuPowerMedMenuItem;
        private MenuItem? _gpuPowerMaxMenuItem;
        private MenuItem? _keyboardBacklightMenuItem;
        private string _currentGpuPowerLevel = "Medium";
        private int _currentKeyboardBrightness = 3;

        // v3.0.2: Additional live status menu items
        private MenuItem? _ramMenuItem;
        private MenuItem? _fanStatusMenuItem;
        private MenuItem? _batteryMenuItem;
        
        // Throttling to prevent flicker during system events (brightness keys, etc.)
        // Use Interlocked for thread-safe access from timer callback
        private int _isUpdatingIcon = 0; // 0 = false, 1 = true (for Interlocked)
        private long _lastIconUpdateTicks = 0;
        private const int MinIconUpdateIntervalMs = 500;

        public event Action<string>? FanModeChangeRequested;
        public event Action<string>? PerformanceModeChangeRequested;
        public event Action<string>? QuickProfileChangeRequested;
        public event Action<string>? GpuPowerChangeRequested;
        public event Action<int>? KeyboardBacklightChangeRequested;
        public event Action? KeyboardBacklightToggleRequested;
        public event Action? CheckForUpdatesRequested;
        
        /// <summary>
        /// Forces immediate refresh of the tray icon (e.g., when temp display setting changes).
        /// </summary>
        public void RefreshTrayIcon()
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                UpdateTrayDisplay(null, EventArgs.Empty);
            });
        }

        public TrayIconService(TaskbarIcon trayIcon, Action showMainWindow, Action shutdownApp, ConfigurationService? configService = null)
        {
            _trayIcon = trayIcon;
            _showMainWindow = showMainWindow;
            _shutdownApp = shutdownApp;
            _displayService = new DisplayService(App.Logging);
            _configService = configService;
            _appVersion = LoadAppVersion();

            _baseIconSource = LoadBaseIcon();
            _trayIcon.IconSource = _baseIconSource;

            InitializeContextMenu();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _updateTimer.Tick += UpdateTrayDisplay;
            _updateTimer.Start();

            UpdateTrayDisplay(null, EventArgs.Empty);
        }

        private static string LoadAppVersion()
        {
            try
            {
                var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");
                if (File.Exists(versionFile))
                {
                    foreach (var line in File.ReadLines(versionFile))
                    {
                        var version = line.Trim();
                        if (!string.IsNullOrEmpty(version))
                            return version;
                    }
                }
            }
            catch { }
            
            // Fallback to assembly version
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return asm != null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "3.2.0";
        }

        private void InitializeContextMenu()
        {
            // Use the fully templated dark menu to avoid default system gutters/white edges.
            var contextMenu = new DarkContextMenu();
            contextMenu.MinWidth = 320;
            contextMenu.Padding = new Thickness(0, 4, 0, 4);
            var menuItemStyle = contextMenu.Resources[typeof(MenuItem)] as Style ?? new Style(typeof(MenuItem));

            // ═══ HEADER ═══
            var headerItem = new MenuItem { Header = $"🎮  OmenCore  v{_appVersion}", IsEnabled = false, FontWeight = FontWeights.Bold, FontSize = 13 };
            contextMenu.Items.Add(headerItem);
            contextMenu.Items.Add(new Separator());

            // ═══ LIVE STATUS ═══
            _cpuTempMenuItem = new MenuItem { Header = "🔥 CPU: --°C · --%", IsEnabled = false };
            _cpuTempMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            contextMenu.Items.Add(_cpuTempMenuItem);

            _gpuTempMenuItem = new MenuItem { Header = "🎯 GPU: --°C · --%", IsEnabled = false };
            _gpuTempMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            contextMenu.Items.Add(_gpuTempMenuItem);

            _monitoringHealthMenuItem = new MenuItem { Header = "📈 Monitor: Unknown", IsEnabled = false };
            _monitoringHealthMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            contextMenu.Items.Add(_monitoringHealthMenuItem);

            _ramMenuItem = new MenuItem { Header = "💾 RAM: —/— GB", IsEnabled = false };
            _ramMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            contextMenu.Items.Add(_ramMenuItem);

            _fanStatusMenuItem = new MenuItem { Header = "🌀 Fan: Auto · —", IsEnabled = false };
            _fanStatusMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            contextMenu.Items.Add(_fanStatusMenuItem);

            _batteryMenuItem = new MenuItem { Header = "🔋 Battery: —", IsEnabled = false };
            _batteryMenuItem.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            _batteryMenuItem.Visibility = Visibility.Collapsed;
            contextMenu.Items.Add(_batteryMenuItem);

            contextMenu.Items.Add(new Separator());

            // ═══ QUICK PROFILES (Combined Fan + Performance) ═══
            var quickProfileMenuItem = new MenuItem { Header = "⚡ Quick Profile ▶" };

            var profilePerformance = new MenuItem { Header = "🚀 Performance — Max cooling + Performance mode" };
            profilePerformance.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Performance");
            var profileBalanced = new MenuItem { Header = "⚖️ Balanced — Auto cooling + Balanced mode" };
            profileBalanced.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Balanced");
            var profileQuiet = new MenuItem { Header = "🤫 Quiet — Quiet fans + Power saving" };
            profileQuiet.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Quiet");

            quickProfileMenuItem.Items.Add(profilePerformance);
            quickProfileMenuItem.Items.Add(profileBalanced);
            quickProfileMenuItem.Items.Add(profileQuiet);

            // Ensure submenu items use our dark MenuItem style
            quickProfileMenuItem.ItemContainerStyle = menuItemStyle;
            quickProfileMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    quickProfileMenuItem.ApplyTemplate();
                    var popup = quickProfileMenuItem.Template.FindName("PART_Popup", quickProfileMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Submenu style error: {ex.Message}"); }
            };

            contextMenu.Items.Add(quickProfileMenuItem);

            // ═══ ADVANCED CONTROLS ═══
            var advancedMenuItem = new MenuItem { Header = "🔧 Advanced ▶" };

            // Fan submenu
            _fanModeMenuItem = new MenuItem { Header = "🌀 Fan Control ▶" };
            
            _fanAutoMenuItem = new MenuItem { Header = "✓ ⚡ Auto — System controlled" };
            _fanAutoMenuItem.Click += (s, e) => SetFanMode("Auto");
            _fanMaxMenuItem = new MenuItem { Header = "   🔥 Max — Maximum cooling" };
            _fanMaxMenuItem.Click += (s, e) => SetFanMode("Max");
            _fanQuietMenuItem = new MenuItem { Header = "   🤫 Quiet — Reduced noise" };
            _fanQuietMenuItem.Click += (s, e) => SetFanMode("Quiet");
            
            _fanModeMenuItem.Items.Add(_fanAutoMenuItem);
            _fanModeMenuItem.Items.Add(_fanMaxMenuItem);
            _fanModeMenuItem.Items.Add(_fanQuietMenuItem);
            _fanModeMenuItem.ItemContainerStyle = menuItemStyle;
            _fanModeMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    _fanModeMenuItem.ApplyTemplate();
                    var popup = _fanModeMenuItem.Template.FindName("PART_Popup", _fanModeMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Fan submenu style error: {ex.Message}"); }
            };
            advancedMenuItem.Items.Add(_fanModeMenuItem);

            // Performance submenu
            _performanceModeMenuItem = new MenuItem { Header = "⚡ Power Profile ▶" };
            
            _perfQuietMenuItem = new MenuItem { Header = "   🔋 Power Saver — Battery life" };
            _perfQuietMenuItem.Click += (s, e) => SetPerformanceMode("Quiet");
            _perfBalancedMenuItem = new MenuItem { Header = "✓ ⚖️ Balanced — Default" };
            _perfBalancedMenuItem.Click += (s, e) => SetPerformanceMode("Balanced");
            _perfPerformanceMenuItem = new MenuItem { Header = "   🚀 Performance — Max power" };
            _perfPerformanceMenuItem.Click += (s, e) => SetPerformanceMode("Performance");
            
            _performanceModeMenuItem.Items.Add(_perfQuietMenuItem);
            _performanceModeMenuItem.Items.Add(_perfBalancedMenuItem);
            _performanceModeMenuItem.Items.Add(_perfPerformanceMenuItem);
            _performanceModeMenuItem.ItemContainerStyle = menuItemStyle;
            _performanceModeMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    _performanceModeMenuItem.ApplyTemplate();
                    var popup = _performanceModeMenuItem.Template.FindName("PART_Popup", _performanceModeMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Performance submenu style error: {ex.Message}"); }
            };
            advancedMenuItem.Items.Add(_performanceModeMenuItem);

            // Display submenu
            _displayMenuItem = new MenuItem { Header = "🖥️ Display ▶" };

            var refreshHigh = new MenuItem { Header = "⚡ High Refresh — Gaming mode" };
            refreshHigh.Click += (s, e) => SetHighRefreshRate();
            var refreshLow = new MenuItem { Header = "🔋 Low Refresh — Save power" };
            refreshLow.Click += (s, e) => SetLowRefreshRate();
            var refreshToggle = new MenuItem { Header = "🔄 Toggle Refresh Rate" };
            refreshToggle.Click += (s, e) => ToggleRefreshRate();
            
            _displayMenuItem.Items.Add(refreshHigh);
            _displayMenuItem.Items.Add(refreshLow);
            _displayMenuItem.Items.Add(refreshToggle);
            _displayMenuItem.Items.Add(new Separator());

            var displayOff = new MenuItem { Header = "🌙 Turn Off Display" };
            displayOff.Click += (s, e) => TurnOffDisplay();
            _displayMenuItem.Items.Add(displayOff);

            _displayMenuItem.ItemContainerStyle = menuItemStyle;
            _displayMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    _displayMenuItem.ApplyTemplate();
                    var popup = _displayMenuItem.Template.FindName("PART_Popup", _displayMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Display submenu style error: {ex.Message}"); }
            };

            advancedMenuItem.Items.Add(_displayMenuItem);
            
            // GPU Power submenu (v2.7.0)
            _gpuPowerMenuItem = new MenuItem { Header = "⚡ GPU Power ▶" };
            
            _gpuPowerMinMenuItem = new MenuItem { Header = "   🔋 Minimum — Base TGP, best battery" };
            _gpuPowerMinMenuItem.Click += (s, e) => RequestGpuPowerChange("Minimum");
            _gpuPowerMedMenuItem = new MenuItem { Header = "✓ ⚖️ Medium — Custom TGP" };
            _gpuPowerMedMenuItem.Click += (s, e) => RequestGpuPowerChange("Medium");
            _gpuPowerMaxMenuItem = new MenuItem { Header = "   🔥 Maximum — TGP + Dynamic Boost" };
            _gpuPowerMaxMenuItem.Click += (s, e) => RequestGpuPowerChange("Maximum");
            
            _gpuPowerMenuItem.Items.Add(_gpuPowerMinMenuItem);
            _gpuPowerMenuItem.Items.Add(_gpuPowerMedMenuItem);
            _gpuPowerMenuItem.Items.Add(_gpuPowerMaxMenuItem);
            _gpuPowerMenuItem.ItemContainerStyle = menuItemStyle;
            _gpuPowerMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    _gpuPowerMenuItem.ApplyTemplate();
                    var popup = _gpuPowerMenuItem.Template.FindName("PART_Popup", _gpuPowerMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] GPU Power submenu style error: {ex.Message}"); }
            };
            advancedMenuItem.Items.Add(_gpuPowerMenuItem);
            
            // Keyboard backlight toggle (v2.7.0)
            _keyboardBacklightMenuItem = new MenuItem { Header = "💡 Keyboard Backlight ▶" };
            
            var kbOff = new MenuItem { Header = "   🌑 Off" };
            kbOff.Click += (s, e) => RequestKeyboardBacklight(0);
            var kbLow = new MenuItem { Header = "   🔅 Low" };
            kbLow.Click += (s, e) => RequestKeyboardBacklight(1);
            var kbMed = new MenuItem { Header = "   🔆 Medium" };
            kbMed.Click += (s, e) => RequestKeyboardBacklight(2);
            var kbHigh = new MenuItem { Header = "✓ 💡 High" };
            kbHigh.Click += (s, e) => RequestKeyboardBacklight(3);
            var kbToggle = new MenuItem { Header = "🔄 Toggle" };
            kbToggle.Click += (s, e) => RequestKeyboardBacklightToggle();
            
            _keyboardBacklightMenuItem.Items.Add(kbOff);
            _keyboardBacklightMenuItem.Items.Add(kbLow);
            _keyboardBacklightMenuItem.Items.Add(kbMed);
            _keyboardBacklightMenuItem.Items.Add(kbHigh);
            _keyboardBacklightMenuItem.Items.Add(new Separator());
            _keyboardBacklightMenuItem.Items.Add(kbToggle);
            _keyboardBacklightMenuItem.ItemContainerStyle = menuItemStyle;
            _keyboardBacklightMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    _keyboardBacklightMenuItem.ApplyTemplate();
                    var popup = _keyboardBacklightMenuItem.Template.FindName("PART_Popup", _keyboardBacklightMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Keyboard submenu style error: {ex.Message}"); }
            };
            advancedMenuItem.Items.Add(_keyboardBacklightMenuItem);
            
            advancedMenuItem.ItemContainerStyle = menuItemStyle;
            advancedMenuItem.SubmenuOpened += (s, e) =>
            {
                try
                {
                    advancedMenuItem.ApplyTemplate();
                    var popup = advancedMenuItem.Template.FindName("PART_Popup", advancedMenuItem) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child != null)
                    {
                        if (popup.Child is System.Windows.Controls.Border b)
                        {
                            b.Background = contextMenu.Background;
                            if (b.Child is System.Windows.Controls.Control innerCtrl)
                                innerCtrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                        else if (popup.Child is System.Windows.Controls.Control ctrl)
                        {
                            ctrl.Background = contextMenu.Background;
                            ctrl.Foreground = (Brush)contextMenu.Foreground;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tray] Advanced submenu style error: {ex.Message}"); }
            };
            
            contextMenu.Items.Add(advancedMenuItem);

            contextMenu.Items.Add(new Separator());

            // ═══ ACTIONS ═══
            var showItem = new MenuItem { Header = "📺 Open Dashboard" };
            showItem.Click += (s, e) => _showMainWindow();
            contextMenu.Items.Add(showItem);
            
            var stayOnTopEnabled = App.Configuration.Config.StayOnTop;
            _stayOnTopMenuItem = new MenuItem
            {
                Header = stayOnTopEnabled ? "📌 Stay on Top ✓" : "📍 Stay on Top",
                ToolTip = stayOnTopEnabled
                    ? "Keep OmenCore above other windows (On)"
                    : "Keep OmenCore above other windows (Off)"
            };
            _stayOnTopMenuItem.Click += (s, e) => ToggleStayOnTop();
            contextMenu.Items.Add(_stayOnTopMenuItem);

            contextMenu.Items.Add(new Separator());

            var checkUpdateItem = new MenuItem { Header = "🔄 Check for Updates" };
            checkUpdateItem.Click += (s, e) => CheckForUpdatesRequested?.Invoke();
            contextMenu.Items.Add(checkUpdateItem);

            var exitItem = new MenuItem { Header = "⏻  Exit OmenCore" };
            exitItem.Click += (s, e) => _shutdownApp();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
        }

        public void UpdateMonitoringSample(MonitoringSample sample)
        {
            _latestSample = sample;
            
            // Also update QuickPopup if visible
            if (_quickPopup?.IsVisible == true)
            {
                _quickPopup.UpdateMonitoringSample(sample);
            }
        }

        private void UpdateTrayDisplay(object? sender, EventArgs e)
        {
            if (_disposed || _latestSample == null)
            {
                _trayIcon.ToolTipText = "OmenCore - No Data";
                _trayIcon.IconSource = _baseIconSource;
                return;
            }

            // Throttle updates to prevent flicker during system events (brightness keys, etc.)
            // Use Interlocked for thread-safe check-and-set
            var lastTicks = Interlocked.Read(ref _lastIconUpdateTicks);
            var timeSinceLastUpdate = (DateTime.UtcNow.Ticks - lastTicks) / TimeSpan.TicksPerMillisecond;
            if (Interlocked.CompareExchange(ref _isUpdatingIcon, 1, 0) != 0 || timeSinceLastUpdate < MinIconUpdateIntervalMs)
            {
                return;
            }

            try
            {
                var cpuTemp = _latestSample.CpuTemperatureC;
                var gpuTemp = _latestSample.GpuTemperatureC;
                var cpuLoad = _latestSample.CpuLoadPercent;
                var gpuLoad = _latestSample.GpuLoadPercent;

                // Update tooltip with enhanced system info including fan RPM and GPU power (v2.6.1)
                var memUsedGb = _latestSample.RamUsageGb;
                var memTotalGb = _latestSample.RamTotalGb;
                var memPercent = memTotalGb > 0 ? (memUsedGb * 100.0 / memTotalGb) : 0;
                
                // v2.9.0: Show "—" for zero temps (sensor unavailable)
                var cpuTempStr = cpuTemp > 0 ? $"{cpuTemp:F0}°C" : "—°C";
                var gpuTempStr = gpuTemp > 0 ? $"{gpuTemp:F0}°C" : "—°C";
                
                // v2.9.0: Label fans separately as CPU Fan / GPU Fan
                var fan1Rpm = _latestSample.Fan1Rpm;
                var fan2Rpm = _latestSample.Fan2Rpm;
                var fanLine = fan2Rpm > 0 
                    ? $"🌀 CPU Fan: {fan1Rpm} · GPU Fan: {fan2Rpm} RPM" 
                    : (fan1Rpm > 0 ? $"🌀 Fan: {fan1Rpm} RPM" : "🌀 Fan: —");
                    
                // v2.6.1: Add GPU power display
                var gpuPower = _latestSample.GpuPowerWatts;
                var gpuPowerDisplay = gpuPower > 0 ? $" · {gpuPower:F0}W" : "";
                
                // v2.9.0: Battery/AC status line
                var batteryPercent = _latestSample.BatteryChargePercent;
                var isAc = _latestSample.IsOnAcPower;
                var powerLine = batteryPercent > 0 || isAc
                    ? $"🔋 {batteryPercent:F0}% · {(isAc ? "AC Power" : "Battery")}"
                    : "";
                
                _trayIcon.ToolTipText = $"🎮 OmenCore v{_appVersion}\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"🔥 CPU: {cpuTempStr} @ {cpuLoad:F0}%\n" +
                                       $"🎯 GPU: {gpuTempStr} @ {gpuLoad:F0}%{gpuPowerDisplay}\n" +
                                       $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)\n" +
                                       $"{fanLine} | ⚡ {_currentPerformanceMode}\n" +
                                       $"🔗 Fan/Perf: {(_linkFanToPerformanceMode ? "Linked" : "Decoupled")}\n" +
                                       (powerLine.Length > 0 ? $"{powerLine}\n" : "") +
                                       $"📈 Monitor: {_monitoringHealth}\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"Left-click to open dashboard";

                // Update context menu items using simple header updates
                if (_cpuTempMenuItem != null)
                {
                    _cpuTempMenuItem.Header = $"🔥 CPU: {cpuTempStr} · {cpuLoad:F0}%";
                }

                if (_gpuTempMenuItem != null)
                {
                    _gpuTempMenuItem.Header = $"🎯 GPU: {gpuTempStr} · {gpuLoad:F0}%";
                }

                if (_monitoringHealthMenuItem != null)
                {
                    _monitoringHealthMenuItem.Header = $"📈 Monitor: {_monitoringHealth}";
                }

                if (_ramMenuItem != null)
                {
                    _ramMenuItem.Header = $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)";
                }

                var fanRpmStr = fan2Rpm > 0
                    ? $"CPU {fan1Rpm} · GPU {fan2Rpm} RPM"
                    : (fan1Rpm > 0 ? $"{fan1Rpm} RPM" : "—");
                if (_fanStatusMenuItem != null)
                {
                    _fanStatusMenuItem.Header = $"🌀 Fan: {_currentFanMode} · {fanRpmStr}";
                }

                if (_batteryMenuItem != null)
                {
                    if (batteryPercent > 0 || isAc)
                    {
                        _batteryMenuItem.Header = $"🔋 {batteryPercent:F0}% · {(isAc ? "⚡ Charging" : "On Battery")}";
                        _batteryMenuItem.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _batteryMenuItem.Visibility = Visibility.Collapsed;
                    }
                }

                // Update tray icon with max temperature badge (shows highest of CPU/GPU)
                // But only if tray temp display is enabled in settings
                var showTempOnTray = _configService?.Config?.Features?.TrayTempDisplayEnabled ?? true;
                if (showTempOnTray)
                {
                    var maxTemp = Math.Max(cpuTemp, gpuTemp);
                    var badge = CreateTempIcon(maxTemp);
                    if (badge != null)
                    {
                        _trayIcon.IconSource = badge;
                    }
                }
                else
                {
                    // Show base icon without temperature
                    _trayIcon.IconSource = _baseIconSource;
                }
            }
            catch (Exception ex)
            {
                App.Logging.Warn($"Failed to update tray display: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _lastIconUpdateTicks, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _isUpdatingIcon, 0);
            }
        }

        private void SetFanMode(string mode)
        {
            _currentFanMode = mode;
            if (_fanModeMenuItem != null)
            {
                _fanModeMenuItem.Header = $"🌀 Fan Control ▶ [{mode}]";
            }
            
            // v2.6.1: Update checkmarks on fan menu items
            UpdateFanModeCheckmarks(mode);
            
            FanModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Fan mode changed from tray: {mode}");
        }
        
        /// <summary>
        /// Update checkmarks on fan mode menu items (v2.6.1)
        /// </summary>
        private void UpdateFanModeCheckmarks(string activeMode)
        {
            if (_fanAutoMenuItem != null)
                _fanAutoMenuItem.Header = (activeMode == "Auto" ? "✓" : "  ") + " ⚡ Auto — System controlled";
            if (_fanMaxMenuItem != null)
                _fanMaxMenuItem.Header = (activeMode == "Max" ? "✓" : "  ") + " 🔥 Max — Maximum cooling";
            if (_fanQuietMenuItem != null)
                _fanQuietMenuItem.Header = (activeMode == "Quiet" ? "✓" : "  ") + " 🤫 Quiet — Reduced noise";
        }

        private void SetPerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            if (_performanceModeMenuItem != null)
            {
                _performanceModeMenuItem.Header = $"⚡ Power Profile ▶ [{mode}]";
            }
            
            // v2.6.1: Update checkmarks on performance menu items
            UpdatePerformanceModeCheckmarks(mode);
            
            PerformanceModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Performance mode changed from tray: {mode}");
        }
        
        /// <summary>
        /// Update checkmarks on performance mode menu items (v2.6.1)
        /// </summary>
        private void UpdatePerformanceModeCheckmarks(string activeMode)
        {
            if (_perfBalancedMenuItem != null)
                _perfBalancedMenuItem.Header = (activeMode == "Balanced" ? "✓" : "  ") + " ⚖️ Balanced — Default";
            if (_perfPerformanceMenuItem != null)
                _perfPerformanceMenuItem.Header = (activeMode == "Performance" ? "✓" : "  ") + " 🚀 Performance — Max power";
            if (_perfQuietMenuItem != null)
                _perfQuietMenuItem.Header = (activeMode == "Quiet" ? "✓" : "  ") + " 🔋 Power Saver — Battery life";
        }
        
        /// <summary>
        /// Request GPU power level change (v2.7.0)
        /// </summary>
        private void RequestGpuPowerChange(string level)
        {
            _currentGpuPowerLevel = level;
            UpdateGpuPowerCheckmarks(level);
            GpuPowerChangeRequested?.Invoke(level);
            App.Logging.Info($"GPU power level changed from tray: {level}");
        }
        
        /// <summary>
        /// Update GPU power level checkmarks (v2.7.0)
        /// </summary>
        private void UpdateGpuPowerCheckmarks(string activeLevel)
        {
            if (_gpuPowerMinMenuItem != null)
                _gpuPowerMinMenuItem.Header = (activeLevel == "Minimum" ? "✓" : "  ") + " 🔋 Minimum — Base TGP, best battery";
            if (_gpuPowerMedMenuItem != null)
                _gpuPowerMedMenuItem.Header = (activeLevel == "Medium" ? "✓" : "  ") + " ⚖️ Medium — Custom TGP";
            if (_gpuPowerMaxMenuItem != null)
                _gpuPowerMaxMenuItem.Header = (activeLevel == "Maximum" ? "✓" : "  ") + " 🔥 Maximum — TGP + Dynamic Boost";
        }
        
        /// <summary>
        /// Update GPU power level from external source (v2.7.0)
        /// </summary>
        public void SetGpuPowerLevel(string level)
        {
            _currentGpuPowerLevel = level;
            UpdateGpuPowerCheckmarks(level);
            if (_gpuPowerMenuItem != null)
            {
                _gpuPowerMenuItem.Header = $"⚡ GPU Power ▶ [{level}]";
            }
        }
        
        /// <summary>
        /// Show or hide the GPU Power tray submenu based on whether the feature is supported on this hardware.
        /// Call after SystemControlViewModel.DetectGpuPowerBoost() completes — hides the submenu on HP Victus
        /// and other models where SupportsGpuPowerBoost = false, preventing confusing no-op menu items.
        /// </summary>
        public void SetGpuPowerAvailable(bool available)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_gpuPowerMenuItem != null)
                    _gpuPowerMenuItem.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Request keyboard backlight level change (v2.7.0)
        /// </summary>
        private void RequestKeyboardBacklight(int level)
        {
            _currentKeyboardBrightness = level;
            KeyboardBacklightChangeRequested?.Invoke(level);
            App.Logging.Info($"Keyboard backlight changed from tray: level {level}");
        }
        
        /// <summary>
        /// Request keyboard backlight toggle (v2.7.0)
        /// </summary>
        private void RequestKeyboardBacklightToggle()
        {
            KeyboardBacklightToggleRequested?.Invoke();
            App.Logging.Info("Keyboard backlight toggle requested from tray");
        }
        
        /// <summary>
        /// Update keyboard brightness from external source (v2.7.0)
        /// </summary>
        public void SetKeyboardBrightness(int level)
        {
            _currentKeyboardBrightness = level;
            if (_keyboardBacklightMenuItem != null)
            {
                string levelName = level switch
                {
                    0 => "Off",
                    1 => "Low",
                    2 => "Medium",
                    _ => "High"
                };
                _keyboardBacklightMenuItem.Header = $"💡 Keyboard Backlight ▶ [{levelName}]";
            }
        }

        private string GetRefreshRateDisplay()
        {
            try
            {
                var rate = _displayService.GetCurrentRefreshRate();
                return rate > 0 ? $"{rate}Hz" : "Display";
            }
            catch
            {
                return "Display";
            }
        }

        private void SetHighRefreshRate()
        {
            if (_displayService.SetHighRefreshRate())
            {
                App.Logging.Info("✓ Switched to high refresh rate from tray");
                UpdateRefreshRateMenuItem();
            }
        }

        private void SetLowRefreshRate()
        {
            if (_displayService.SetLowRefreshRate())
            {
                App.Logging.Info("✓ Switched to low refresh rate from tray");
                UpdateRefreshRateMenuItem();
            }
        }

        private void ToggleRefreshRate()
        {
            var newRate = _displayService.ToggleRefreshRate();
            if (newRate > 0)
            {
                App.Logging.Info($"✓ Toggled refresh rate to {newRate}Hz from tray");
                UpdateRefreshRateMenuItem();
            }
        }
        
        /// <summary>
        /// Update the display menu item header to show current refresh rate.
        /// </summary>
        private void UpdateRefreshRateMenuItem()
        {
            if (_displayMenuItem == null) return;
            
            try
            {
                _displayMenuItem.Header = $"🖥️ Display ▶ {GetRefreshRateDisplay()}";
            }
            catch (Exception ex)
            {
                App.Logging.Warn($"Failed to update refresh rate menu item: {ex.Message}");
            }
        }

        private void TurnOffDisplay()
        {
            _displayService.TurnOffDisplay();
        }

        private void ToggleStayOnTop()
        {
            var newValue = !App.Configuration.Config.StayOnTop;
            App.Configuration.Config.StayOnTop = newValue;
            App.Configuration.Save(App.Configuration.Config);
            
            // Update the menu item
            if (_stayOnTopMenuItem != null)
            {
                _stayOnTopMenuItem.Header = newValue ? "📌 Stay on Top ✓" : "📍 Stay on Top";
                _stayOnTopMenuItem.ToolTip = newValue
                    ? "Keep OmenCore above other windows (On)"
                    : "Keep OmenCore above other windows (Off)";
            }
            
            // Notify the main window to update
            StayOnTopChanged?.Invoke(newValue);
            App.Logging.Info($"Stay on Top: {(newValue ? "Enabled" : "Disabled")}");
        }

        public event Action<bool>? StayOnTopChanged;

        public void UpdateFanMode(string mode)
        {
            _currentFanMode = mode;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_fanModeMenuItem != null)
                {
                    var suffix = _linkFanToPerformanceMode ? " [linked]" : string.Empty;
                    _fanModeMenuItem.Header = $"🌀 Fan Mode ▶ {mode}{suffix}";
                }
            });
        }

        public void UpdatePerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_performanceModeMenuItem != null)
                {
                    _performanceModeMenuItem.Header = $"⚡ Performance ▶ {mode}";
                }
            });
        }

        public void UpdateLinkedMode(bool linked)
        {
            _linkFanToPerformanceMode = linked;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_fanModeMenuItem != null)
                {
                    var suffix = linked ? " [linked]" : string.Empty;
                    _fanModeMenuItem.Header = $"🌀 Fan Mode ▶ {_currentFanMode}{suffix}";
                }

                if (_quickPopup != null)
                {
                    _quickPopup.UpdateLinkedMode(linked);
                }
            });
        }

        public void UpdateMonitoringHealth(MonitoringHealthStatus healthStatus)
        {
            _monitoringHealth = healthStatus switch
            {
                MonitoringHealthStatus.Healthy => "Healthy",
                MonitoringHealthStatus.Degraded => "Degraded",
                MonitoringHealthStatus.Stale => "Stale",
                _ => "Unknown"
            };

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_monitoringHealthMenuItem != null)
                {
                    _monitoringHealthMenuItem.Header = $"📈 Monitor: {_monitoringHealth}";
                }

                if (_quickPopup != null)
                {
                    _quickPopup.UpdateMonitoringHealth(_monitoringHealth);
                }
            });
        }

        /// <summary>
        /// Shows or hides the quick popup window near the tray.
        /// </summary>
        public void ToggleQuickPopup()
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_quickPopup == null)
                {
                    _quickPopup = new QuickPopupWindow();
                    _quickPopup.FanModeChangeRequested += mode => FanModeChangeRequested?.Invoke(mode);
                    _quickPopup.PerformanceModeChangeRequested += mode => PerformanceModeChangeRequested?.Invoke(mode);
                    _quickPopup.Closed += (s, e) => _quickPopup = null;
                }

                if (_quickPopup.IsVisible)
                {
                    _quickPopup.Hide();
                }
                else
                {
                    _quickPopup.PositionNearTray();
                    _quickPopup.UpdateFanMode(_currentFanMode);
                    _quickPopup.UpdatePerformanceMode(_currentPerformanceMode);
                    _quickPopup.UpdateLinkedMode(_linkFanToPerformanceMode);
                    _quickPopup.UpdateMonitoringHealth(_monitoringHealth);
                    if (_latestSample != null)
                    {
                        _quickPopup.UpdateMonitoringSample(_latestSample);
                    }
                    _quickPopup.Show();
                    _quickPopup.Activate();
                }
            });
        }

        /// <summary>
        /// Shows the quick popup window near the tray.
        /// </summary>
        public void ShowQuickPopup()
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_quickPopup == null)
                {
                    _quickPopup = new QuickPopupWindow();
                    _quickPopup.FanModeChangeRequested += mode => FanModeChangeRequested?.Invoke(mode);
                    _quickPopup.PerformanceModeChangeRequested += mode => PerformanceModeChangeRequested?.Invoke(mode);
                    _quickPopup.Closed += (s, e) => _quickPopup = null;
                }

                _quickPopup.PositionNearTray();
                _quickPopup.UpdateFanMode(_currentFanMode);
                _quickPopup.UpdatePerformanceMode(_currentPerformanceMode);
                _quickPopup.UpdateLinkedMode(_linkFanToPerformanceMode);
                _quickPopup.UpdateMonitoringHealth(_monitoringHealth);
                if (_latestSample != null)
                {
                    _quickPopup.UpdateMonitoringSample(_latestSample);
                }
                _quickPopup.Show();
                _quickPopup.Activate();
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= UpdateTrayDisplay;
                _quickPopup?.Close();
                _disposed = true;
            }
        }

        private ImageSource? LoadBaseIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/OmenCore.ico", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a dynamic tray icon showing temperature with color-coded background.
        /// Colors optimized for gaming laptops where higher temps are normal:
        /// - Blue: < 50°C (idle/cool)
        /// - Green: 50-65°C (light load)
        /// - Yellow: 65-75°C (moderate load) 
        /// - Orange: 75-85°C (gaming/heavy load - normal for laptops)
        /// - Red: 85-95°C (very hot but within spec for gaming laptops)
        /// - Magenta: > 95°C (critical - approaching thermal throttle)
        /// </summary>
        private ImageSource? CreateTempIcon(double temp)
        {
            // System tray icons are rendered at 16x16 or 32x32 depending on DPI
            // We create at 32x32 for best quality at high DPI
            const int size = 32;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                // Temperature-based background color (adjusted for gaming laptops)
                Color bgColor;
                if (temp < 50)
                    bgColor = Color.FromRgb(0, 120, 220);      // Blue - Cool/Idle
                else if (temp < 65)
                    bgColor = Color.FromRgb(0, 180, 80);       // Green - Light load
                else if (temp < 75)
                    bgColor = Color.FromRgb(220, 200, 0);      // Yellow - Moderate load
                else if (temp < 85)
                    bgColor = Color.FromRgb(255, 140, 0);      // Orange - Gaming/Heavy (normal for laptops)
                else if (temp < 95)
                    bgColor = Color.FromRgb(255, 60, 60);      // Red - Very hot
                else
                    bgColor = Color.FromRgb(200, 0, 100);      // Magenta - Critical

                // Draw colored square background (fills more of the tray space)
                var background = new SolidColorBrush(bgColor);
                dc.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 4, 4);

                // Draw temperature text - use largest font that fits
                var text = temp.ToString("F0");
                var fontSize = temp >= 100 ? 13 : 16; // Adjusted for 32x32 icon
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal),
                    fontSize,
                    Brushes.White,
                    1.25);

                // Center the text
                var origin = new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2);
                dc.DrawText(formatted, origin);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}