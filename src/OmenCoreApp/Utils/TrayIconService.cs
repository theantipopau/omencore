using System;
using System.Globalization;
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
        private QuickPopupWindow? _quickPopup;
        private MenuItem? _cpuTempMenuItem;
        private MenuItem? _gpuTempMenuItem;
        private MenuItem? _fanModeMenuItem;
        private MenuItem? _performanceModeMenuItem;
        private MenuItem? _stayOnTopMenuItem;
        private MenuItem? _displayMenuItem;  // For updating refresh rate display
        private MonitoringSample? _latestSample;
        private string _currentFanMode = "Auto";
        private string _currentPerformanceMode = "Balanced";
        private bool _disposed;
        private readonly ConfigurationService? _configService;

        public event Action<string>? FanModeChangeRequested;
        public event Action<string>? PerformanceModeChangeRequested;
        public event Action<string>? QuickProfileChangeRequested;
        
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

        private void InitializeContextMenu()
        {
            // Use our fully custom DarkContextMenu - no white margins, no icon gutter
            var contextMenu = new DarkContextMenu();

            // ═══ HEADER ═══
            contextMenu.Items.Add(DarkContextMenu.CreateHeader("🎮", "OmenCore", "v1.5.0"));
            contextMenu.Items.Add(new Separator());

            // ═══ MONITORING SECTION ═══
            _cpuTempMenuItem = DarkContextMenu.CreateMonitoringItem("🔥", "CPU", "--°C", "(--%)", Color.FromRgb(255, 100, 100));
            contextMenu.Items.Add(_cpuTempMenuItem);

            _gpuTempMenuItem = DarkContextMenu.CreateMonitoringItem("🎯", "GPU", "--°C", "(--%)", Color.FromRgb(100, 200, 255));
            contextMenu.Items.Add(_gpuTempMenuItem);

            contextMenu.Items.Add(new Separator());

            // ═══ QUICK PROFILES ═══
            var quickProfileMenuItem = DarkContextMenu.CreateControlItem("🎮", "Quick Profiles", "Balanced", DarkContextMenu.GetAccentPrimary());
            
            var profilePerformance = DarkContextMenu.CreateSubMenuItem("🚀", "Performance", "Max power + Max cooling");
            profilePerformance.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Performance");
            var profileBalanced = DarkContextMenu.CreateSubMenuItem("⚖️", "Balanced", "Default power + Auto fans");
            profileBalanced.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Balanced");
            var profileQuiet = DarkContextMenu.CreateSubMenuItem("🤫", "Quiet", "Power saver + Silent fans");
            profileQuiet.Click += (s, e) => QuickProfileChangeRequested?.Invoke("Quiet");
            
            quickProfileMenuItem.Items.Add(profilePerformance);
            quickProfileMenuItem.Items.Add(profileBalanced);
            quickProfileMenuItem.Items.Add(profileQuiet);
            contextMenu.Items.Add(quickProfileMenuItem);

            contextMenu.Items.Add(new Separator());

            // ═══ FAN MODE ═══
            _fanModeMenuItem = DarkContextMenu.CreateControlItem("🌀", "Fan Mode", "Auto", DarkContextMenu.GetAccentSecondary());
            
            var fanAuto = DarkContextMenu.CreateSubMenuItem("⚡", "Auto", "Automatic fan control");
            fanAuto.Click += (s, e) => SetFanMode("Auto");
            var fanMax = DarkContextMenu.CreateSubMenuItem("🔥", "Max Cooling", "Maximum fan speed");
            fanMax.Click += (s, e) => SetFanMode("Max");
            var fanQuiet = DarkContextMenu.CreateSubMenuItem("🤫", "Quiet", "Silent operation");
            fanQuiet.Click += (s, e) => SetFanMode("Quiet");
            
            _fanModeMenuItem.Items.Add(fanAuto);
            _fanModeMenuItem.Items.Add(fanMax);
            _fanModeMenuItem.Items.Add(fanQuiet);
            contextMenu.Items.Add(_fanModeMenuItem);

            // ═══ PERFORMANCE MODE ═══
            _performanceModeMenuItem = DarkContextMenu.CreateControlItem("⚡", "Performance", "Balanced", DarkContextMenu.GetAccentPrimary());
            
            var perfBalanced = DarkContextMenu.CreateSubMenuItem("⚖️", "Balanced", "Balance power & performance");
            perfBalanced.Click += (s, e) => SetPerformanceMode("Balanced");
            var perfPerformance = DarkContextMenu.CreateSubMenuItem("🚀", "Performance", "Maximum performance");
            perfPerformance.Click += (s, e) => SetPerformanceMode("Performance");
            var perfQuiet = DarkContextMenu.CreateSubMenuItem("🔋", "Quiet", "Power saving mode");
            perfQuiet.Click += (s, e) => SetPerformanceMode("Quiet");
            
            _performanceModeMenuItem.Items.Add(perfBalanced);
            _performanceModeMenuItem.Items.Add(perfPerformance);
            _performanceModeMenuItem.Items.Add(perfQuiet);
            contextMenu.Items.Add(_performanceModeMenuItem);

            // ═══ DISPLAY ═══
            _displayMenuItem = DarkContextMenu.CreateControlItem("🖥️", "Display", GetRefreshRateDisplay(), DarkContextMenu.GetAccentSecondary());

            var refreshHigh = DarkContextMenu.CreateSubMenuItem("⚡", "High Refresh Rate", "Switch to max refresh rate");
            refreshHigh.Click += (s, e) => SetHighRefreshRate();
            var refreshLow = DarkContextMenu.CreateSubMenuItem("🔋", "Power Saving", "Lower refresh rate to save power");
            refreshLow.Click += (s, e) => SetLowRefreshRate();
            var refreshToggle = DarkContextMenu.CreateSubMenuItem("🔄", "Toggle Refresh Rate", "Switch between high/low");
            refreshToggle.Click += (s, e) => ToggleRefreshRate();
            
            _displayMenuItem.Items.Add(refreshHigh);
            _displayMenuItem.Items.Add(refreshLow);
            _displayMenuItem.Items.Add(refreshToggle);
            _displayMenuItem.Items.Add(new Separator());
            
            var displayOff = DarkContextMenu.CreateSubMenuItem("🌙", "Turn Off Display", "Screen off, system continues");
            displayOff.Click += (s, e) => TurnOffDisplay();
            _displayMenuItem.Items.Add(displayOff);
            
            contextMenu.Items.Add(_displayMenuItem);

            contextMenu.Items.Add(new Separator());

            // ═══ ACTIONS ═══
            var showItem = DarkContextMenu.CreateActionItem("📺", "Open Dashboard", isPrimary: true);
            showItem.Click += (s, e) => _showMainWindow();
            contextMenu.Items.Add(showItem);
            
            _stayOnTopMenuItem = DarkContextMenu.CreateActionItem(
                App.Configuration.Config.StayOnTop ? "📌" : "📍", 
                App.Configuration.Config.StayOnTop ? "Stay on Top ✓" : "Stay on Top");
            _stayOnTopMenuItem.Click += (s, e) => ToggleStayOnTop();
            contextMenu.Items.Add(_stayOnTopMenuItem);

            var exitItem = DarkContextMenu.CreateActionItem("❌", "Exit");
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

            try
            {
                var cpuTemp = _latestSample.CpuTemperatureC;
                var gpuTemp = _latestSample.GpuTemperatureC;
                var cpuLoad = _latestSample.CpuLoadPercent;
                var gpuLoad = _latestSample.GpuLoadPercent;

                // Update tooltip with enhanced system info
                var memUsedGb = _latestSample.RamUsageGb;
                var memTotalGb = _latestSample.RamTotalGb;
                var memPercent = memTotalGb > 0 ? (memUsedGb * 100.0 / memTotalGb) : 0;
                
                _trayIcon.ToolTipText = $"🎮 OmenCore v1.5.0\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"🔥 CPU: {cpuTemp:F0}°C @ {cpuLoad:F0}%\n" +
                                       $"🎯 GPU: {gpuTemp:F0}°C @ {gpuLoad:F0}%\n" +
                                       $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)\n" +
                                       $"🌀 Fan: {_currentFanMode} | ⚡ {_currentPerformanceMode}\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"Left-click to open dashboard";

                // Update context menu items using DarkContextMenu helper
                if (_cpuTempMenuItem != null)
                {
                    var newCpuItem = DarkContextMenu.CreateMonitoringItem("🔥", "CPU", $"{cpuTemp:F0}°C", $"({cpuLoad:F0}%)", Color.FromRgb(255, 100, 100));
                    _cpuTempMenuItem.Header = newCpuItem.Header;
                }

                if (_gpuTempMenuItem != null)
                {
                    var newGpuItem = DarkContextMenu.CreateMonitoringItem("🎯", "GPU", $"{gpuTemp:F0}°C", $"({gpuLoad:F0}%)", Color.FromRgb(100, 200, 255));
                    _gpuTempMenuItem.Header = newGpuItem.Header;
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
        }

        private void SetFanMode(string mode)
        {
            _currentFanMode = mode;
            if (_fanModeMenuItem != null)
            {
                var newItem = DarkContextMenu.CreateControlItem("🌀", "Fan Mode", mode, DarkContextMenu.GetAccentSecondary());
                _fanModeMenuItem.Header = newItem.Header;
            }
            FanModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Fan mode changed from tray: {mode}");
        }

        private void SetPerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            if (_performanceModeMenuItem != null)
            {
                var newItem = DarkContextMenu.CreateControlItem("⚡", "Performance", mode, DarkContextMenu.GetAccentPrimary());
                _performanceModeMenuItem.Header = newItem.Header;
            }
            PerformanceModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Performance mode changed from tray: {mode}");
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
                var newItem = DarkContextMenu.CreateControlItem("🖥️", "Display", GetRefreshRateDisplay(), DarkContextMenu.GetAccentSecondary());
                _displayMenuItem.Header = newItem.Header;
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
                var newItem = DarkContextMenu.CreateActionItem(newValue ? "📌" : "📍", 
                    newValue ? "Stay on Top ✓" : "Stay on Top");
                _stayOnTopMenuItem.Header = newItem.Header;
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
                    var newItem = DarkContextMenu.CreateControlItem("🌀", "Fan Mode", mode, DarkContextMenu.GetAccentSecondary());
                    _fanModeMenuItem.Header = newItem.Header;
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
                    var newItem = DarkContextMenu.CreateControlItem("⚡", "Performance", mode, DarkContextMenu.GetAccentPrimary());
                    _performanceModeMenuItem.Header = newItem.Header;
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

        [Obsolete("Use CreateTempIcon instead")]
        private ImageSource? CreateCpuTempIcon(double cpuTemp)
        {
            const int size = 32;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                var background = new LinearGradientBrush(
                    Color.FromRgb(8, 10, 20),
                    Color.FromRgb(21, 25, 43),
                    new Point(0, 0),
                    new Point(1, 1));
                dc.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 6, 6);

                var accent = new SolidColorBrush(Color.FromRgb(255, 0, 92));
                dc.DrawEllipse(accent, null, new Point(size / 2.0, size / 2.0), size / 2.1, size / 2.1);

                var text = cpuTemp.ToString("F0");
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    14,
                    Brushes.White,
                    1.25);

                var origin = new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2 + 1);
                dc.DrawText(formatted, origin);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}