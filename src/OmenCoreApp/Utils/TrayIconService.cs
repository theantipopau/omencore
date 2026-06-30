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
using OmenCore.Services.Diagnostics;
using OmenCore.Views;

namespace OmenCore.Utils
{
    /// <summary>
    /// Manages the system tray icon with live temperature monitoring and quick actions.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private const string TrayRefreshTimerRegistryName = "TrayIconRefresh";
        private const int TrayRefreshIntervalMs = 2000;
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
        private string? _pendingFanModeRequest;
        private string _currentPerformanceMode = "Balanced";
        private string? _curvePresetName;
        private bool _linkFanToPerformanceMode;
        private string _monitoringHealth = "Unknown";
        private bool _disposed;
        private readonly ConfigurationService? _configService;
        private bool? _lastRegisteredTrayTempDisplayEnabled;
        // Cached previous values for change detection to avoid unnecessary UI work
        private double _lastCpuTempC = double.NaN;
        private double _lastGpuTempC = double.NaN;
        private double _lastCpuLoadPercent = double.NaN;
        private double _lastGpuLoadPercent = double.NaN;
        private double _lastMemUsedGb = double.NaN;
        private double _lastMemTotalGb = double.NaN;
        private int _lastFan1Rpm = -1;
        private int _lastFan2Rpm = -1;
        private double _lastBatteryPercent = double.NaN;
        private bool _lastIsAc = false;
        private string _lastCurrentPerformanceMode = string.Empty;
        private string _lastCurrentFanMode = string.Empty;
        private string _lastMonitoringHealth = string.Empty;
        private bool _lastLinkFanToPerformanceMode = false;
        private bool _lastShowTempOnTray = false;
        private string? _lastTooltipText;
        private int? _lastRenderedBadgeTemperature;
        private bool? _lastRenderedTrayTempDisplayEnabled;
        
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
                UpdateTrayRefreshTimerDescription(force: true);
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
            _appVersion = AppVersionProvider.GetVersionString();

            _baseIconSource = LoadBaseIcon();
            _trayIcon.IconSource = _baseIconSource;

            InitializeContextMenu();

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TrayRefreshIntervalMs)
            };
            _updateTimer.Tick += UpdateTrayDisplay;
            _updateTimer.Start();
            RegisterTrayRefreshTimer();

            UpdateTrayDisplay(null, EventArgs.Empty);
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

            var profilePerformance = new MenuItem { Header = "🚀 Performance — Gaming cooling + Performance mode" };
            profilePerformance.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Performance");
            var profileBalanced = new MenuItem { Header = "⚖️ Balanced — Auto cooling + Balanced mode" };
            profileBalanced.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Balanced");
            var profileQuiet = new MenuItem { Header = "🤫 Quiet — Quiet fans + Power saving" };
            profileQuiet.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Quiet");
            var profileCustom = new MenuItem { Header = "⚙️ Custom — User-defined fans + power settings" };
            profileCustom.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Custom");

            quickProfileMenuItem.Items.Add(profilePerformance);
            quickProfileMenuItem.Items.Add(profileBalanced);
            quickProfileMenuItem.Items.Add(profileQuiet);
            quickProfileMenuItem.Items.Add(profileCustom);

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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Fan submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Performance submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Display submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] GPU Power submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Keyboard submenu style error: {ex.Message}"); }
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
                catch (Exception ex) { App.Logging.Warn($"[Tray] Advanced submenu style error: {ex.Message}"); }
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

        public void UpdateMonitoringSample(MonitoringSample? sample)
        {
            if (sample == null)
            {
                return;
            }

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
                const string noDataTooltip = "OmenCore - No Data";
                if (!string.Equals(_lastTooltipText, noDataTooltip, StringComparison.Ordinal))
                {
                    _trayIcon.ToolTipText = noDataTooltip;
                    _lastTooltipText = noDataTooltip;
                }

                if (!ReferenceEquals(_trayIcon.IconSource, _baseIconSource))
                {
                    _trayIcon.IconSource = _baseIconSource;
                }

                _lastRenderedBadgeTemperature = null;
                _lastRenderedTrayTempDisplayEnabled = false;
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

                var memUsedGb = _latestSample.RamUsageGb;
                var memTotalGb = _latestSample.RamTotalGb;
                var memPercent = memTotalGb > 0 ? (memUsedGb * 100.0 / memTotalGb) : 0;
                
                // Retrieve additional sample values needed for change detection.
                var fan1Rpm = _latestSample.Fan1Rpm;
                var fan2Rpm = _latestSample.Fan2Rpm;
                var gpuPower = _latestSample.GpuPowerWatts;
                var batteryPercent = _latestSample.BatteryChargePercent;
                var isAc = _latestSample.IsOnAcPower;

                // Determine whether the tray display needs updating based on any changed values.
                var showTempOnTray = _configService?.Config?.Features?.TrayTempDisplayEnabled ?? true;

                // Detect changes
                bool valuesChanged = false;
                if (!double.Equals(cpuTemp, _lastCpuTempC)) valuesChanged = true;
                if (!double.Equals(gpuTemp, _lastGpuTempC)) valuesChanged = true;
                if (!double.Equals(cpuLoad, _lastCpuLoadPercent)) valuesChanged = true;
                if (!double.Equals(gpuLoad, _lastGpuLoadPercent)) valuesChanged = true;
                if (!double.Equals(memUsedGb, _lastMemUsedGb)) valuesChanged = true;
                if (!double.Equals(memTotalGb, _lastMemTotalGb)) valuesChanged = true;
                if (fan1Rpm != _lastFan1Rpm) valuesChanged = true;
                if (fan2Rpm != _lastFan2Rpm) valuesChanged = true;
                if (!double.Equals(batteryPercent, _lastBatteryPercent)) valuesChanged = true;
                if (isAc != _lastIsAc) valuesChanged = true;
                if (_currentPerformanceMode != _lastCurrentPerformanceMode) valuesChanged = true;
                if (_currentFanMode != _lastCurrentFanMode) valuesChanged = true;
                if (_monitoringHealth != _lastMonitoringHealth) valuesChanged = true;
                if (_linkFanToPerformanceMode != _lastLinkFanToPerformanceMode) valuesChanged = true;
                if (showTempOnTray != _lastShowTempOnTray) valuesChanged = true;

                if (!valuesChanged)
                {
                    // No visible changes; skip UI update.
                    return;
                }

                // Build display strings after confirming changes.
                var cpuTempStr = cpuTemp > 0 ? $"{cpuTemp:F0}°C" : "—°C";
                var gpuTempStr = gpuTemp > 0 ? $"{gpuTemp:F0}°C" : "—°C";

                var fanLine = fan2Rpm > 0 
                    ? $"🌀 CPU Fan: {fan1Rpm} · GPU Fan: {fan2Rpm} RPM" 
                    : (fan1Rpm > 0 ? $"🌀 Fan: {fan1Rpm} RPM" : "🌀 Fan: —");

                var gpuPowerDisplay = gpuPower > 0 ? $" · {gpuPower:F0}W" : "";

                var powerLine = batteryPercent > 0 || isAc
                    ? $"🔋 {batteryPercent:F0}% · {(isAc ? "AC Power" : "Battery")}"
                    : "";

                var toolTipText = $"🎮 OmenCore v{_appVersion}\n" +
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
                if (!string.Equals(_lastTooltipText, toolTipText, StringComparison.Ordinal))
                {
                    _trayIcon.ToolTipText = toolTipText;
                    _lastTooltipText = toolTipText;
                }

                SetMenuHeaderIfChanged(_cpuTempMenuItem, $"🔥 CPU: {cpuTempStr} · {cpuLoad:F0}%");
                SetMenuHeaderIfChanged(_gpuTempMenuItem, $"🎯 GPU: {gpuTempStr} · {gpuLoad:F0}%");
                SetMenuHeaderIfChanged(_monitoringHealthMenuItem, BuildMonitoringHealthHeaderText(_monitoringHealth));
                SetMenuHeaderIfChanged(_ramMenuItem, $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)");

                var fanRpmStr = fan2Rpm > 0
                    ? $"CPU {fan1Rpm} · GPU {fan2Rpm} RPM"
                    : (fan1Rpm > 0 ? $"{fan1Rpm} RPM" : "—");
                SetMenuHeaderIfChanged(_fanStatusMenuItem, $"🌀 Fan: {_currentFanMode} · {fanRpmStr}");

                if (_batteryMenuItem != null)
                {
                    if (batteryPercent > 0 || isAc)
                    {
                        SetMenuHeaderIfChanged(_batteryMenuItem, $"🔋 {batteryPercent:F0}% · {(isAc ? "⚡ Charging" : "On Battery")}");
                        SetMenuVisibilityIfChanged(_batteryMenuItem, Visibility.Visible);
                    }
                    else
                    {
                        SetMenuVisibilityIfChanged(_batteryMenuItem, Visibility.Collapsed);
                    }
                }

                // showTempOnTray already determined earlier
                UpdateTrayRefreshTimerDescription(showTempOnTray);
                if (showTempOnTray)
                {
                    var maxTemp = Math.Max(cpuTemp, gpuTemp);
                    var badgeTemperature = (int)Math.Round(maxTemp);
                    if (_lastRenderedTrayTempDisplayEnabled != true || _lastRenderedBadgeTemperature != badgeTemperature)
                    {
                           // v3.6.2: Cache miss - render state changed, requires re-render
                           RuntimeUiPerformanceCounters.RecordTrayRenderCacheMiss();
                        var badge = CreateTempIcon(maxTemp);
                        if (badge != null)
                        {
                            _trayIcon.IconSource = badge;
                            _lastRenderedBadgeTemperature = badgeTemperature;
                            _lastRenderedTrayTempDisplayEnabled = true;
                        }
                    }
                       else
                       {
                           // v3.6.2: Cache hit - render state unchanged, no re-render needed
                           RuntimeUiPerformanceCounters.RecordTrayRenderCacheHit();
                       }
                }
                else if (_lastRenderedTrayTempDisplayEnabled != false || !ReferenceEquals(_trayIcon.IconSource, _baseIconSource))
                {
                       // v3.6.2: Cache miss - render state changed (disabling or switching icon)
                       RuntimeUiPerformanceCounters.RecordTrayRenderCacheMiss();
                    _trayIcon.IconSource = _baseIconSource;
                    _lastRenderedBadgeTemperature = null;
                    _lastRenderedTrayTempDisplayEnabled = false;
                }
                   else
                   {
                       // v3.6.2: Cache hit - render state unchanged (both disabled)
                // Update cached values for next comparison
                _lastCpuTempC = cpuTemp;
                _lastGpuTempC = gpuTemp;
                _lastCpuLoadPercent = cpuLoad;
                _lastGpuLoadPercent = gpuLoad;
                _lastMemUsedGb = memUsedGb;
                _lastMemTotalGb = memTotalGb;
                _lastFan1Rpm = fan1Rpm;
                _lastFan2Rpm = fan2Rpm;
                _lastBatteryPercent = batteryPercent;
                _lastIsAc = isAc;
                _lastCurrentPerformanceMode = _currentPerformanceMode;
                _lastCurrentFanMode = _currentFanMode;
                _lastMonitoringHealth = _monitoringHealth;
                _lastLinkFanToPerformanceMode = _linkFanToPerformanceMode;
                _lastShowTempOnTray = showTempOnTray;
                        RuntimeUiPerformanceCounters.RecordTrayRenderCacheHit();
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
            _pendingFanModeRequest = mode;
            if (_fanModeMenuItem != null)
            {
                _fanModeMenuItem.Header = BuildFanModeHeader();
            }

            FanModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Fan mode request from tray: requested={mode}, current={_currentFanMode}");
        }
        
        /// <summary>
        /// Update checkmarks on fan mode menu items (v2.6.1)
        /// </summary>
        private void UpdateFanModeCheckmarks(string activeMode)
        {
            var isAuto = FanModeNameResolver.IsAutoAlias(activeMode);
            var isMax = FanModeNameResolver.IsMaxAlias(activeMode);
            var isQuiet = FanModeNameResolver.IsQuietAlias(activeMode);

            if (_fanAutoMenuItem != null)
                _fanAutoMenuItem.Header = (isAuto ? "✓" : "  ") + " ⚡ Auto — System controlled";
            if (_fanMaxMenuItem != null)
                _fanMaxMenuItem.Header = (isMax ? "✓" : "  ") + " 🔥 Max — Maximum cooling";
            if (_fanQuietMenuItem != null)
                _fanQuietMenuItem.Header = (isQuiet ? "✓" : "  ") + " 🤫 Quiet — Reduced noise";
        }

        private void SetPerformanceMode(string mode)
        {
            _currentPerformanceMode = PerformanceModeNameResolver.Normalize(mode);
            if (_performanceModeMenuItem != null)
            {
                _performanceModeMenuItem.Header = BuildPerformanceModeHeaderText(_currentPerformanceMode);
            }
            
            // v2.6.1: Update checkmarks on performance menu items
            UpdatePerformanceModeCheckmarks(_currentPerformanceMode);
            
            PerformanceModeChangeRequested?.Invoke(_currentPerformanceMode);
            App.Logging.Info($"Performance mode changed from tray: {_currentPerformanceMode}");
        }
        
        /// <summary>
        /// Update checkmarks on performance mode menu items (v2.6.1)
        /// </summary>
        private void UpdatePerformanceModeCheckmarks(string activeMode)
        {
            var canonicalMode = PerformanceModeNameResolver.Normalize(activeMode);
            if (_perfBalancedMenuItem != null)
                _perfBalancedMenuItem.Header = (canonicalMode == "Balanced" ? "✓" : "  ") + " ⚖️ Balanced — Default";
            if (_perfPerformanceMenuItem != null)
                _perfPerformanceMenuItem.Header = (canonicalMode == "Performance" ? "✓" : "  ") + " 🚀 Performance — Max power";
            if (_perfQuietMenuItem != null)
                _perfQuietMenuItem.Header = (canonicalMode == "Quiet" ? "✓" : "  ") + " 🔋 Power Saver — Battery life";
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
            var normalizedMode = NormalizeFanModeForDisplay(mode);
            if (string.Equals(_currentFanMode, normalizedMode, StringComparison.Ordinal) && _pendingFanModeRequest == null)
            {
                return;
            }

            _currentFanMode = normalizedMode;
            _pendingFanModeRequest = null;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                UpdateFanModeCheckmarks(normalizedMode);

                if (_fanModeMenuItem != null)
                {
                    _fanModeMenuItem.Header = BuildFanModeHeader();
                }

                _quickPopup?.UpdateFanMode(normalizedMode);
            });
        }

        public void UpdateCurvePresetName(string? presetName)
        {
            if (string.Equals(_curvePresetName, presetName, StringComparison.Ordinal))
            {
                return;
            }

            _curvePresetName = presetName;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                _quickPopup?.UpdateCurvePresetName(presetName);
            });
        }

        public void UpdatePerformanceMode(string mode)
        {
            var normalizedMode = PerformanceModeNameResolver.Normalize(mode);
            if (string.Equals(_currentPerformanceMode, normalizedMode, StringComparison.Ordinal))
            {
                return;
            }

            _currentPerformanceMode = normalizedMode;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_performanceModeMenuItem != null)
                {
                    _performanceModeMenuItem.Header = BuildPerformanceModeHeaderText(_currentPerformanceMode);
                }

                UpdatePerformanceModeCheckmarks(_currentPerformanceMode);

                _quickPopup?.UpdatePerformanceMode(_currentPerformanceMode);
            });
        }

        public void UpdateLinkedMode(bool linked)
        {
            if (_linkFanToPerformanceMode == linked)
            {
                return;
            }

            _linkFanToPerformanceMode = linked;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_fanModeMenuItem != null)
                {
                    _fanModeMenuItem.Header = BuildFanModeHeader();
                }

                if (_quickPopup != null)
                {
                    _quickPopup.UpdateLinkedMode(linked);
                }
            });
        }

        public void UpdateMonitoringHealth(MonitoringHealthStatus healthStatus)
        {
            var normalizedHealth = healthStatus switch
            {
                MonitoringHealthStatus.Healthy => "Healthy",
                MonitoringHealthStatus.Degraded => "Degraded",
                MonitoringHealthStatus.Stale => "Stale",
                _ => "Unknown"
            };

            if (string.Equals(_monitoringHealth, normalizedHealth, StringComparison.Ordinal))
            {
                return;
            }

            _monitoringHealth = normalizedHealth;

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (_monitoringHealthMenuItem != null)
                {
                    _monitoringHealthMenuItem.Header = BuildMonitoringHealthHeaderText(_monitoringHealth);
                }

                if (_quickPopup != null)
                {
                    _quickPopup.UpdateMonitoringHealth(_monitoringHealth);
                }
            });
        }

        private string BuildFanModeHeader() =>
            BuildFanModeHeaderText(_currentFanMode, _pendingFanModeRequest, _linkFanToPerformanceMode);

        private static string NormalizeFanModeForDisplay(string? mode)
        {
            if (FanModeNameResolver.IsMaxAlias(mode))
            {
                return "Max";
            }

            if (FanModeNameResolver.IsQuietAlias(mode))
            {
                return "Quiet";
            }

            if (FanModeNameResolver.IsAutoAlias(mode))
            {
                return "Auto";
            }

            if (FanModeNameResolver.IsCustomAlias(mode))
            {
                return "Custom";
            }

            return string.IsNullOrWhiteSpace(mode) ? "Auto" : mode.Trim();
        }

        /// <summary>
        /// Pure helper — separated so it can be tested without WPF dependencies.
        /// </summary>
        public static string BuildFanModeHeaderText(string currentMode, string? pendingRequest, bool linked)
        {
            var normalizedCurrentMode = string.IsNullOrWhiteSpace(currentMode) ? "Unknown" : currentMode.Trim();
            var normalizedPendingRequest = string.IsNullOrWhiteSpace(pendingRequest) ? null : pendingRequest.Trim();
            var suffix = linked ? " [linked]" : string.Empty;
            if (normalizedPendingRequest != null &&
                !string.Equals(normalizedPendingRequest, normalizedCurrentMode, StringComparison.OrdinalIgnoreCase))
            {
                return $"🌀 Fan Mode ▶ {normalizedCurrentMode}{suffix} (requested: {normalizedPendingRequest})";
            }

            return $"🌀 Fan Mode ▶ {normalizedCurrentMode}{suffix}";
        }

        /// <summary>
        /// Pure helper for tray performance mode header formatting.
        /// </summary>
        public static string BuildPerformanceModeHeaderText(string currentMode)
        {
            var normalizedCurrentMode = string.IsNullOrWhiteSpace(currentMode) ? "Unknown" : currentMode.Trim();
            return $"⚡ Performance ▶ {normalizedCurrentMode}";
        }

        /// <summary>
        /// Pure helper for tray monitoring-health header formatting.
        /// </summary>
        public static string BuildMonitoringHealthHeaderText(string monitoringHealth)
        {
            var normalizedHealth = string.IsNullOrWhiteSpace(monitoringHealth) ? "Unknown" : monitoringHealth.Trim();
            return $"📈 Monitor: {normalizedHealth}";
        }

        private static void SetMenuHeaderIfChanged(MenuItem? menuItem, string header)
        {
            if (menuItem == null)
            {
                return;
            }

            var currentHeader = menuItem.Header?.ToString();
            if (!string.Equals(currentHeader, header, StringComparison.Ordinal))
            {
                menuItem.Header = header;
            }
        }

        private static void SetMenuVisibilityIfChanged(MenuItem? menuItem, Visibility visibility)
        {
            if (menuItem != null && menuItem.Visibility != visibility)
            {
                menuItem.Visibility = visibility;
            }
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
                    _quickPopup.QuickProfileChangeRequested += profile => QuickProfileChangeRequested?.Invoke(profile);
                    _quickPopup.OpenDashboardRequested += () => _showMainWindow();
                    _quickPopup.Closed += (s, e) => _quickPopup = null;
                }

                if (_quickPopup.IsVisible)
                {
                    _quickPopup.Hide();
                }
                else
                {
                    _quickPopup.ConfigureQuickAction(_configService?.Config.QuickAccessAction);
                    _quickPopup.PositionNearTray();
                    _quickPopup.UpdateFanMode(_currentFanMode);
                    _quickPopup.UpdateCurvePresetName(_curvePresetName);
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
                    _quickPopup.QuickProfileChangeRequested += profile => QuickProfileChangeRequested?.Invoke(profile);
                    _quickPopup.OpenDashboardRequested += () => _showMainWindow();
                    _quickPopup.Closed += (s, e) => _quickPopup = null;
                }

                _quickPopup.ConfigureQuickAction(_configService?.Config.QuickAccessAction);
                _quickPopup.PositionNearTray();
                _quickPopup.UpdateFanMode(_currentFanMode);
                _quickPopup.UpdateCurvePresetName(_curvePresetName);
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
                BackgroundTimerRegistry.Unregister(TrayRefreshTimerRegistryName);
                _quickPopup?.Close();
                _disposed = true;
            }
        }

        private void RegisterTrayRefreshTimer()
        {
            var showTempOnTray = _configService?.Config?.Features?.TrayTempDisplayEnabled ?? true;
            _lastRegisteredTrayTempDisplayEnabled = showTempOnTray;
            BackgroundTimerRegistry.Register(
                TrayRefreshTimerRegistryName,
                nameof(TrayIconService),
                BuildTrayRefreshTimerDescription(showTempOnTray),
                TrayRefreshIntervalMs,
                BackgroundTimerTier.Optional);
        }

        private void UpdateTrayRefreshTimerDescription(bool? showTempOnTray = null, bool force = false)
        {
            var enabled = showTempOnTray ?? (_configService?.Config?.Features?.TrayTempDisplayEnabled ?? true);
            if (!force && _lastRegisteredTrayTempDisplayEnabled == enabled)
            {
                return;
            }

            _lastRegisteredTrayTempDisplayEnabled = enabled;
            BackgroundTimerRegistry.UpdateDescription(
                TrayRefreshTimerRegistryName,
                BuildTrayRefreshTimerDescription(enabled));
        }

        private static string BuildTrayRefreshTimerDescription(bool showTempOnTray) =>
            showTempOnTray
                ? "Tray tooltip/menu refresh plus live temperature badge redraw"
                : "Tray tooltip/menu refresh with static icon";

        private ImageSource? LoadBaseIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/OmenCore.ico", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logging.Debug($"Failed to load tray base icon from embedded assets: {ex.Message}");
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
