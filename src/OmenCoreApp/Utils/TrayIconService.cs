using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using OmenCore.Models;
using OmenCore.Services;

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
        private MenuItem? _cpuTempMenuItem;
        private MenuItem? _gpuTempMenuItem;
        private MenuItem? _fanModeMenuItem;
        private MenuItem? _performanceModeMenuItem;
        private MenuItem? _stayOnTopMenuItem;
        private MonitoringSample? _latestSample;
        private string _currentFanMode = "Auto";
        private string _currentPerformanceMode = "Balanced";
        private bool _disposed;

        public event Action<string>? FanModeChangeRequested;
        public event Action<string>? PerformanceModeChangeRequested;

        public TrayIconService(TaskbarIcon trayIcon, Action showMainWindow, Action shutdownApp)
        {
            _trayIcon = trayIcon;
            _showMainWindow = showMainWindow;
            _shutdownApp = shutdownApp;
            _displayService = new DisplayService(App.Logging);

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
            var contextMenu = new ContextMenu();

            // Modern dark theme colors
            var surfaceDark = new SolidColorBrush(Color.FromRgb(15, 17, 28)); // Darker base
            var surfaceMedium = new SolidColorBrush(Color.FromRgb(21, 25, 43)); // Card background
            var accentPrimary = new SolidColorBrush(Color.FromRgb(255, 0, 92)); // OMEN Red/Pink
            var accentSecondary = new SolidColorBrush(Color.FromRgb(0, 200, 200)); // Cyan accent
            var borderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 90)); // Subtle border
            var hoverBrush = new SolidColorBrush(Color.FromRgb(40, 45, 65)); // Hover state
            var textPrimary = new SolidColorBrush(Color.FromRgb(240, 240, 245)); // Bright white
            var textSecondary = new SolidColorBrush(Color.FromRgb(160, 165, 180)); // Muted text
            
            // Gradient background for modern look
            var gradientBg = new LinearGradientBrush(
                Color.FromRgb(18, 20, 35),
                Color.FromRgb(25, 28, 48),
                new Point(0, 0),
                new Point(0, 1));
            
            contextMenu.Background = gradientBg;
            contextMenu.Foreground = textPrimary;
            contextMenu.BorderBrush = borderBrush;
            contextMenu.BorderThickness = new Thickness(1);
            contextMenu.Padding = new Thickness(4);

            // Override system colors for proper dark theme in submenus
            contextMenu.Resources.Add(SystemColors.MenuBarBrushKey, surfaceDark);
            contextMenu.Resources.Add(SystemColors.MenuBrushKey, surfaceDark);
            contextMenu.Resources.Add(SystemColors.MenuTextBrushKey, textPrimary);
            contextMenu.Resources.Add(SystemColors.HighlightBrushKey, hoverBrush);
            contextMenu.Resources.Add(SystemColors.HighlightTextBrushKey, Brushes.White);
            contextMenu.Resources.Add(SystemColors.MenuHighlightBrushKey, hoverBrush);
            
            // Hide the icon column (the white strip on the left)
            contextMenu.Resources.Add(SystemColors.ControlBrushKey, surfaceDark);
            contextMenu.Resources.Add(SystemColors.ControlLightBrushKey, surfaceDark);
            contextMenu.Resources.Add(SystemColors.ControlLightLightBrushKey, surfaceDark);
            contextMenu.Resources.Add(SystemColors.WindowBrushKey, surfaceDark);
            contextMenu.Resources.Add(MenuItem.SeparatorStyleKey, CreateSeparatorStyle(borderBrush));
            
            // Create a style to hide the icon column completely
            var menuItemStyle = CreateDarkMenuItemStyleWithNoIconColumn(surfaceDark, hoverBrush);
            contextMenu.Resources.Add(typeof(MenuItem), menuItemStyle);

            // Create styles
            var darkMenuItemStyle = CreateDarkMenuItemStyle();
            var disabledStyle = CreateDisabledMenuItemStyle();

            // ═══ HEADER ═══
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            headerPanel.Children.Add(new TextBlock { Text = "🎮", FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            headerPanel.Children.Add(new TextBlock { Text = "OmenCore", FontWeight = FontWeights.Bold, FontSize = 13, Foreground = accentPrimary, VerticalAlignment = VerticalAlignment.Center });
            headerPanel.Children.Add(new TextBlock { Text = " v1.2.0", FontSize = 11, Foreground = textSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 0, 0) });
            
            var headerItem = new MenuItem
            {
                Header = headerPanel,
                IsEnabled = false,
                IsHitTestVisible = false,
                Padding = new Thickness(8, 6, 8, 6)
            };
            contextMenu.Items.Add(headerItem);

            // Styled separator
            contextMenu.Items.Add(CreateStyledSeparator(borderBrush));

            // ═══ MONITORING SECTION ═══
            _cpuTempMenuItem = new MenuItem
            {
                Header = CreateMonitoringItem("🔥", "CPU", "--°C", "--%", Color.FromRgb(255, 100, 100)),
                IsEnabled = false,
                IsHitTestVisible = false,
                Padding = new Thickness(8, 4, 8, 4)
            };
            contextMenu.Items.Add(_cpuTempMenuItem);

            _gpuTempMenuItem = new MenuItem
            {
                Header = CreateMonitoringItem("🎯", "GPU", "--°C", "--%", Color.FromRgb(100, 200, 255)),
                IsEnabled = false,
                IsHitTestVisible = false,
                Padding = new Thickness(8, 4, 8, 4)
            };
            contextMenu.Items.Add(_gpuTempMenuItem);

            contextMenu.Items.Add(CreateStyledSeparator(borderBrush));

            // ═══ CONTROL SECTION ═══
            _fanModeMenuItem = new MenuItem
            {
                Header = CreateControlItem("🌀", "Fan Mode", "Auto", accentSecondary),
                Foreground = textPrimary,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };
            
            var fanAuto = CreateSubMenuItem("⚡", "Auto", "Automatic fan control", darkMenuItemStyle);
            fanAuto.Click += (s, e) => SetFanMode("Auto");
            var fanMax = CreateSubMenuItem("🔥", "Max Cooling", "Maximum fan speed", darkMenuItemStyle);
            fanMax.Click += (s, e) => SetFanMode("Max");
            var fanQuiet = CreateSubMenuItem("🤫", "Quiet", "Silent operation", darkMenuItemStyle);
            fanQuiet.Click += (s, e) => SetFanMode("Quiet");
            
            _fanModeMenuItem.Items.Add(fanAuto);
            _fanModeMenuItem.Items.Add(fanMax);
            _fanModeMenuItem.Items.Add(fanQuiet);
            contextMenu.Items.Add(_fanModeMenuItem);

            _performanceModeMenuItem = new MenuItem
            {
                Header = CreateControlItem("⚡", "Performance", "Balanced", accentPrimary),
                Foreground = textPrimary,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };
            
            var perfBalanced = CreateSubMenuItem("⚖️", "Balanced", "Balance power & performance", darkMenuItemStyle);
            perfBalanced.Click += (s, e) => SetPerformanceMode("Balanced");
            var perfPerformance = CreateSubMenuItem("🚀", "Performance", "Maximum performance", darkMenuItemStyle);
            perfPerformance.Click += (s, e) => SetPerformanceMode("Performance");
            var perfQuiet = CreateSubMenuItem("🔋", "Quiet", "Power saving mode", darkMenuItemStyle);
            perfQuiet.Click += (s, e) => SetPerformanceMode("Quiet");
            
            _performanceModeMenuItem.Items.Add(perfBalanced);
            _performanceModeMenuItem.Items.Add(perfPerformance);
            _performanceModeMenuItem.Items.Add(perfQuiet);
            contextMenu.Items.Add(_performanceModeMenuItem);

            // ═══ DISPLAY SECTION ═══
            var displayMenuItem = new MenuItem
            {
                Header = CreateControlItem("🖥️", "Display", GetRefreshRateDisplay(), accentSecondary),
                Foreground = textPrimary,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };

            var refreshHigh = CreateSubMenuItem("⚡", "High Refresh Rate", "Switch to max refresh rate", darkMenuItemStyle);
            refreshHigh.Click += (s, e) => SetHighRefreshRate();
            var refreshLow = CreateSubMenuItem("🔋", "Power Saving", "Lower refresh rate to save power", darkMenuItemStyle);
            refreshLow.Click += (s, e) => SetLowRefreshRate();
            var refreshToggle = CreateSubMenuItem("🔄", "Toggle Refresh Rate", "Switch between high/low", darkMenuItemStyle);
            refreshToggle.Click += (s, e) => ToggleRefreshRate();
            
            displayMenuItem.Items.Add(refreshHigh);
            displayMenuItem.Items.Add(refreshLow);
            displayMenuItem.Items.Add(refreshToggle);
            displayMenuItem.Items.Add(CreateStyledSeparator(borderBrush));
            
            var displayOff = CreateSubMenuItem("🌙", "Turn Off Display", "Screen off, system continues", darkMenuItemStyle);
            displayOff.Click += (s, e) => TurnOffDisplay();
            displayMenuItem.Items.Add(displayOff);
            
            contextMenu.Items.Add(displayMenuItem);

            contextMenu.Items.Add(CreateStyledSeparator(borderBrush));

            // ═══ ACTIONS SECTION ═══
            var showItem = new MenuItem
            {
                Header = CreateActionItem("📺", "Open Dashboard"),
                Foreground = textPrimary,
                FontWeight = FontWeights.SemiBold,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };
            showItem.Click += (s, e) => _showMainWindow();
            contextMenu.Items.Add(showItem);
            
            // Stay on Top toggle
            _stayOnTopMenuItem = new MenuItem
            {
                Header = CreateActionItem(App.Configuration.Config.StayOnTop ? "📌" : "📍", 
                    App.Configuration.Config.StayOnTop ? "Stay on Top ✓" : "Stay on Top"),
                Foreground = textSecondary,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };
            _stayOnTopMenuItem.Click += (s, e) => ToggleStayOnTop();
            contextMenu.Items.Add(_stayOnTopMenuItem);

            var exitItem = new MenuItem
            {
                Header = CreateActionItem("❌", "Exit"),
                Foreground = textSecondary,
                Style = darkMenuItemStyle,
                Padding = new Thickness(8, 6, 8, 6)
            };
            exitItem.Click += (s, e) => _shutdownApp();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
        }

        private static Separator CreateStyledSeparator(Brush borderBrush)
        {
            return new Separator
            {
                Margin = new Thickness(8, 4, 8, 4),
                Background = borderBrush,
                Height = 1
            };
        }

        private static StackPanel CreateMonitoringItem(string icon, string label, string temp, string load, Color accentColor)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = label + ":", FontSize = 12, Width = 32, Foreground = new SolidColorBrush(Color.FromRgb(160, 165, 180)), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = temp, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(accentColor), MinWidth = 45, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = $"({load})", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(130, 135, 150)), VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private static StackPanel CreateControlItem(string icon, string label, string value, Brush accentBrush)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = label + ":", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 215)), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = " " + value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = accentBrush, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = " ▸", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(100, 105, 120)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            return panel;
        }

        private static StackPanel CreateActionItem(string icon, string label)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private static MenuItem CreateSubMenuItem(string icon, string label, string description, Style style)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            
            var mainRow = new StackPanel { Orientation = Orientation.Horizontal };
            mainRow.Children.Add(new TextBlock { Text = icon, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            mainRow.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(mainRow);
            
            panel.Children.Add(new TextBlock 
            { 
                Text = description, 
                FontSize = 10, 
                Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 140)),
                Margin = new Thickness(17, 1, 0, 0)
            });
            
            return new MenuItem
            {
                Header = panel,
                Style = style,
                Padding = new Thickness(8, 4, 16, 4)
            };
        }

        private static Style CreateDisabledMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            var darkBg = new SolidColorBrush(Color.FromRgb(21, 25, 43));
            style.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(MenuItem.ForegroundProperty, Brushes.White));
            return style;
        }

        public void UpdateMonitoringSample(MonitoringSample sample)
        {
            _latestSample = sample;
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
                
                _trayIcon.ToolTipText = $"🎮 OmenCore v1.2.0\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"🔥 CPU: {cpuTemp:F0}°C @ {cpuLoad:F0}%\n" +
                                       $"🎯 GPU: {gpuTemp:F0}°C @ {gpuLoad:F0}%\n" +
                                       $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)\n" +
                                       $"🌀 Fan: {_currentFanMode} | ⚡ {_currentPerformanceMode}\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"Left-click to open dashboard";

                // Update context menu items with styled panels
                if (_cpuTempMenuItem != null)
                {
                    _cpuTempMenuItem.Header = CreateMonitoringItem("🔥", "CPU", $"{cpuTemp:F0}°C", $"{cpuLoad:F0}%", Color.FromRgb(255, 100, 100));
                }

                if (_gpuTempMenuItem != null)
                {
                    _gpuTempMenuItem.Header = CreateMonitoringItem("🎯", "GPU", $"{gpuTemp:F0}°C", $"{gpuLoad:F0}%", Color.FromRgb(100, 200, 255));
                }

                // Update tray icon with max temperature badge (shows highest of CPU/GPU)
                var maxTemp = Math.Max(cpuTemp, gpuTemp);
                var badge = CreateTempIcon(maxTemp);
                if (badge != null)
                {
                    _trayIcon.IconSource = badge;
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
                _fanModeMenuItem.Header = CreateControlItem("🌀", "Fan Mode", mode, new SolidColorBrush(Color.FromRgb(0, 200, 200)));
            }
            FanModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Fan mode changed from tray: {mode}");
        }

        private void SetPerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            if (_performanceModeMenuItem != null)
            {
                _performanceModeMenuItem.Header = CreateControlItem("⚡", "Performance", mode, new SolidColorBrush(Color.FromRgb(255, 0, 92)));
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
            }
        }

        private void SetLowRefreshRate()
        {
            if (_displayService.SetLowRefreshRate())
            {
                App.Logging.Info("✓ Switched to low refresh rate from tray");
            }
        }

        private void ToggleRefreshRate()
        {
            var newRate = _displayService.ToggleRefreshRate();
            if (newRate > 0)
            {
                App.Logging.Info($"✓ Toggled refresh rate to {newRate}Hz from tray");
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
                _stayOnTopMenuItem.Header = CreateActionItem(newValue ? "📌" : "📍", 
                    newValue ? "Stay on Top ✓" : "Stay on Top");
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
                    _fanModeMenuItem.Header = CreateControlItem("🌀", "Fan Mode", mode, new SolidColorBrush(Color.FromRgb(0, 200, 200)));
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
                    _performanceModeMenuItem.Header = CreateControlItem("⚡", "Performance", mode, new SolidColorBrush(Color.FromRgb(255, 0, 92)));
                }
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= UpdateTrayDisplay;
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
        /// Colors: Blue (<60°C), Green (60-70°C), Orange (70-80°C), Red (>80°C)
        /// </summary>
        private ImageSource? CreateTempIcon(double temp)
        {
            const int size = 32;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                // Temperature-based background color
                Color bgColor;
                if (temp < 60)
                    bgColor = Color.FromRgb(0, 100, 200);      // Blue - Cool
                else if (temp < 70)
                    bgColor = Color.FromRgb(0, 180, 80);       // Green - Normal
                else if (temp < 80)
                    bgColor = Color.FromRgb(255, 140, 0);      // Orange - Warm
                else if (temp < 90)
                    bgColor = Color.FromRgb(255, 60, 60);      // Red - Hot
                else
                    bgColor = Color.FromRgb(200, 0, 100);      // Magenta - Critical

                // Draw colored circular background
                var background = new SolidColorBrush(bgColor);
                dc.DrawEllipse(background, null, new Point(size / 2.0, size / 2.0), size / 2.0 - 1, size / 2.0 - 1);

                // Draw temperature text
                var text = temp.ToString("F0");
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    temp >= 100 ? 11 : 13, // Smaller font for 3-digit temps
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

        private Style CreateDarkMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            
            // Set dark background for the submenu popup
            var darkBg = new SolidColorBrush(Color.FromRgb(21, 25, 43));
            var hoverBg = new SolidColorBrush(Color.FromRgb(47, 52, 72));
            var borderBrush = new SolidColorBrush(Color.FromRgb(47, 52, 72));
            
            style.Setters.Add(new Setter(MenuItem.BackgroundProperty, darkBg));
            style.Setters.Add(new Setter(MenuItem.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(MenuItem.BorderBrushProperty, borderBrush));
            
            // Create trigger for hover state
            var hoverTrigger = new Trigger
            {
                Property = MenuItem.IsHighlightedProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, hoverBg));
            style.Triggers.Add(hoverTrigger);
            
            return style;
        }
        
        private static Style CreateDarkMenuItemStyleWithNoIconColumn(Brush darkBg, Brush hoverBg)
        {
            var style = new Style(typeof(MenuItem));
            
            style.Setters.Add(new Setter(MenuItem.BackgroundProperty, darkBg));
            style.Setters.Add(new Setter(MenuItem.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(MenuItem.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(MenuItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 4, 8, 4)));
            
            // Override the icon column by using UsesItemContainerTemplate
            // This approach uses margins to hide the gutter
            style.Setters.Add(new Setter(MenuItem.MarginProperty, new Thickness(-28, 0, 0, 0)));
            style.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(36, 6, 8, 6)));
            
            var hoverTrigger = new Trigger
            {
                Property = MenuItem.IsHighlightedProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, hoverBg));
            style.Triggers.Add(hoverTrigger);
            
            return style;
        }
        
        private static Style CreateSeparatorStyle(Brush borderBrush)
        {
            var style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(Separator.BackgroundProperty, borderBrush));
            style.Setters.Add(new Setter(Separator.MarginProperty, new Thickness(0, 4, 0, 4)));
            style.Setters.Add(new Setter(Separator.HeightProperty, 1.0));
            return style;
        }
    }
}