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
        private MenuItem? _cpuTempMenuItem;
        private MenuItem? _gpuTempMenuItem;
        private MenuItem? _fanModeMenuItem;
        private MenuItem? _performanceModeMenuItem;
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

            // Set dark background with light text
            var darkBg = new SolidColorBrush(Color.FromRgb(21, 25, 43)); // SurfaceMediumBrush
            var borderBrush = new SolidColorBrush(Color.FromRgb(47, 52, 72)); // BorderBrush
            
            contextMenu.Background = darkBg;
            contextMenu.Foreground = Brushes.White;
            contextMenu.BorderBrush = borderBrush;
            contextMenu.BorderThickness = new Thickness(1);

            // Add resources to style submenu popups (removes white icon gutter)
            contextMenu.Resources.Add(SystemColors.MenuBarBrushKey, darkBg);
            contextMenu.Resources.Add(SystemColors.MenuBrushKey, darkBg);
            contextMenu.Resources.Add(SystemColors.MenuTextBrushKey, Brushes.White);
            contextMenu.Resources.Add(SystemColors.HighlightBrushKey, new SolidColorBrush(Color.FromRgb(47, 52, 72)));
            contextMenu.Resources.Add(SystemColors.HighlightTextBrushKey, Brushes.White);
            contextMenu.Resources.Add(SystemColors.MenuHighlightBrushKey, new SolidColorBrush(Color.FromRgb(47, 52, 72)));

            // Status header
            var headerItem = new MenuItem
            {
                Header = "🎮 OmenCore Status",
                IsEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 92)), // AccentBrush
                FontWeight = FontWeights.Bold
            };
            contextMenu.Items.Add(headerItem);

            contextMenu.Items.Add(new Separator());

            // CPU Temperature
            _cpuTempMenuItem = new MenuItem
            {
                Header = "🔥 CPU: --°C (--%)  ",
                IsEnabled = false,
                Foreground = Brushes.White
            };
            contextMenu.Items.Add(_cpuTempMenuItem);

            // GPU Temperature
            _gpuTempMenuItem = new MenuItem
            {
                Header = "🎯 GPU: --°C (--%)  ",
                IsEnabled = false,
                Foreground = Brushes.White
            };
            contextMenu.Items.Add(_gpuTempMenuItem);

            contextMenu.Items.Add(new Separator());

            // Create dark style for submenu items
            var darkMenuItemStyle = CreateDarkMenuItemStyle();

            // Fan Mode submenu
            _fanModeMenuItem = new MenuItem
            {
                Header = "🌀 Fan: Auto",
                Foreground = Brushes.White,
                Style = darkMenuItemStyle
            };
            
            var fanAuto = new MenuItem { Header = "Auto", Foreground = Brushes.White, Style = darkMenuItemStyle };
            fanAuto.Click += (s, e) => SetFanMode("Auto");
            var fanMax = new MenuItem { Header = "Max Cooling", Foreground = Brushes.White, Style = darkMenuItemStyle };
            fanMax.Click += (s, e) => SetFanMode("Max");
            var fanQuiet = new MenuItem { Header = "Quiet", Foreground = Brushes.White, Style = darkMenuItemStyle };
            fanQuiet.Click += (s, e) => SetFanMode("Quiet");
            
            _fanModeMenuItem.Items.Add(fanAuto);
            _fanModeMenuItem.Items.Add(fanMax);
            _fanModeMenuItem.Items.Add(fanQuiet);
            contextMenu.Items.Add(_fanModeMenuItem);

            // Performance Mode submenu
            _performanceModeMenuItem = new MenuItem
            {
                Header = "⚡ Performance: Balanced",
                Foreground = Brushes.White,
                Style = darkMenuItemStyle
            };
            
            var perfBalanced = new MenuItem { Header = "Balanced", Foreground = Brushes.White, Style = darkMenuItemStyle };
            perfBalanced.Click += (s, e) => SetPerformanceMode("Balanced");
            var perfPerformance = new MenuItem { Header = "Performance", Foreground = Brushes.White, Style = darkMenuItemStyle };
            perfPerformance.Click += (s, e) => SetPerformanceMode("Performance");
            var perfQuiet = new MenuItem { Header = "Quiet", Foreground = Brushes.White, Style = darkMenuItemStyle };
            perfQuiet.Click += (s, e) => SetPerformanceMode("Quiet");
            
            _performanceModeMenuItem.Items.Add(perfBalanced);
            _performanceModeMenuItem.Items.Add(perfPerformance);
            _performanceModeMenuItem.Items.Add(perfQuiet);
            contextMenu.Items.Add(_performanceModeMenuItem);

            contextMenu.Items.Add(new Separator());

            // Show Window
            var showItem = new MenuItem
            {
                Header = "📺 Show OmenCore",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            showItem.Click += (s, e) => _showMainWindow();
            contextMenu.Items.Add(showItem);

            // Exit
            var exitItem = new MenuItem
            {
                Header = "❌ Exit",
                Foreground = Brushes.White
            };
            exitItem.Click += (s, e) => _shutdownApp();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
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
                
                _trayIcon.ToolTipText = $"🎮 OmenCore v1.0.0.8\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"🔥 CPU: {cpuTemp:F0}°C @ {cpuLoad:F0}%\n" +
                                       $"🎯 GPU: {gpuTemp:F0}°C @ {gpuLoad:F0}%\n" +
                                       $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)\n" +
                                       $"🌀 Fan: {_currentFanMode} | ⚡ {_currentPerformanceMode}\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"Left-click to open dashboard";

                // Update context menu items
                if (_cpuTempMenuItem != null)
                {
                    _cpuTempMenuItem.Header = $"🔥 CPU: {cpuTemp:F0}°C ({cpuLoad:F0}%)  ";
                }

                if (_gpuTempMenuItem != null)
                {
                    _gpuTempMenuItem.Header = $"🎯 GPU: {gpuTemp:F0}°C ({gpuLoad:F0}%)  ";
                }

                // Update tray icon with CPU temperature badge
                var badge = CreateCpuTempIcon(cpuTemp);
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
                _fanModeMenuItem.Header = $"🌀 Fan: {mode}";
            }
            FanModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Fan mode changed from tray: {mode}");
        }

        private void SetPerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            if (_performanceModeMenuItem != null)
            {
                _performanceModeMenuItem.Header = $"⚡ Performance: {mode}";
            }
            PerformanceModeChangeRequested?.Invoke(mode);
            App.Logging.Info($"Performance mode changed from tray: {mode}");
        }

        public void UpdateFanMode(string mode)
        {
            _currentFanMode = mode;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_fanModeMenuItem != null)
                {
                    _fanModeMenuItem.Header = $"🌀 Fan: {mode}";
                }
            });
        }

        public void UpdatePerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_performanceModeMenuItem != null)
                {
                    _performanceModeMenuItem.Header = $"⚡ Performance: {mode}";
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
    }
}
