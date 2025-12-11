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
        private MenuItem? _performanceModeMenuItem;
        private MonitoringSample? _latestSample;
        private bool _disposed;

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
            contextMenu.Background = new SolidColorBrush(Color.FromRgb(21, 25, 43)); // SurfaceMediumBrush
            contextMenu.Foreground = Brushes.White;
            contextMenu.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 52, 72)); // BorderBrush
            contextMenu.BorderThickness = new Thickness(1);

            // Status header
            var headerItem = new MenuItem
            {
                Header = "OmenCore Status",
                IsEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 92)), // AccentBrush
                FontWeight = FontWeights.Bold
            };
            contextMenu.Items.Add(headerItem);

            contextMenu.Items.Add(new Separator());

            // CPU Temperature
            _cpuTempMenuItem = new MenuItem
            {
                Header = "CPU: --°C (--%)  ",
                IsEnabled = false,
                Foreground = Brushes.White
            };
            contextMenu.Items.Add(_cpuTempMenuItem);

            // GPU Temperature
            _gpuTempMenuItem = new MenuItem
            {
                Header = "GPU: --°C (--%)  ",
                IsEnabled = false,
                Foreground = Brushes.White
            };
            contextMenu.Items.Add(_gpuTempMenuItem);

            contextMenu.Items.Add(new Separator());

            // Performance Mode
            _performanceModeMenuItem = new MenuItem
            {
                Header = "⚡ Performance Mode",
                Foreground = Brushes.White
            };
            _performanceModeMenuItem.Click += (s, e) => TogglePerformanceMode();
            contextMenu.Items.Add(_performanceModeMenuItem);

            contextMenu.Items.Add(new Separator());

            // Show Window
            var showItem = new MenuItem
            {
                Header = "Show OmenCore",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            showItem.Click += (s, e) => _showMainWindow();
            contextMenu.Items.Add(showItem);

            // Exit
            var exitItem = new MenuItem
            {
                Header = "Exit",
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
                
                _trayIcon.ToolTipText = $"🎮 OmenCore v1.0.0.5\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"🔥 CPU: {cpuTemp:F0}°C @ {cpuLoad:F0}%\n" +
                                       $"🎯 GPU: {gpuTemp:F0}°C @ {gpuLoad:F0}%\n" +
                                       $"💾 RAM: {memUsedGb:F1}/{memTotalGb:F1} GB ({memPercent:F0}%)\n" +
                                       $"━━━━━━━━━━━━━━━━━━\n" +
                                       $"Left-click to open dashboard";

                // Update context menu items
                if (_cpuTempMenuItem != null)
                {
                    _cpuTempMenuItem.Header = $"CPU: {cpuTemp:F0}°C ({cpuLoad:F0}%)  ";
                }

                if (_gpuTempMenuItem != null)
                {
                    _gpuTempMenuItem.Header = $"GPU: {gpuTemp:F0}°C ({gpuLoad:F0}%)  ";
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

        private void TogglePerformanceMode()
        {
            // This will be wired up when services are more accessible
            // For now, it opens the main window to the HP Omen tab
            _showMainWindow();
            App.Logging.Info("Performance mode toggle requested from tray");
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
    }
}
