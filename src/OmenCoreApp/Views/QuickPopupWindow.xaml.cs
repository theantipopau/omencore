using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Views
{
    /// <summary>
    /// Quick popup window that appears near the system tray for fast access to common settings.
    /// Shows CPU/GPU temps, fan mode, performance mode, and quick actions.
    /// </summary>
    public partial class QuickPopupWindow : Window
    {
        private readonly DisplayService _displayService;
        private readonly DispatcherTimer _updateTimer;
        private MonitoringSample? _latestSample;
        
        private string _currentFanMode = "Auto";
        private string _currentPerformanceMode = "Balanced";
        private string _monitoringHealth = "Unknown";
        
        /// <summary>
        /// Raised when user requests a fan mode change.
        /// </summary>
        public event Action<string>? FanModeChangeRequested;
        
        /// <summary>
        /// Raised when user requests a performance mode change.
        /// </summary>
        public event Action<string>? PerformanceModeChangeRequested;

        public QuickPopupWindow()
        {
            InitializeComponent();
            
            _displayService = new DisplayService(App.Logging);
            
            // Update display info
            UpdateRefreshRateDisplay();
            
            // Start update timer for temperatures
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += UpdateDisplay;
            _updateTimer.Start();
            
            // Allow dragging the window
            MouseLeftButtonDown += (s, e) => 
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }

        /// <summary>
        /// Position the popup near the system tray area.
        /// </summary>
        public void PositionNearTray()
        {
            // Get the working area (excludes taskbar)
            var workArea = SystemParameters.WorkArea;
            
            // Position in bottom-right corner, above taskbar
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 12;

            // Resume timer when showing
            if (!_updateTimer.IsEnabled)
                _updateTimer.Start();
        }

        /// <summary>
        /// Update monitoring data from the main app.
        /// </summary>
        public void UpdateMonitoringSample(MonitoringSample sample)
        {
            _latestSample = sample;
        }

        /// <summary>
        /// Update the current fan mode display.
        /// </summary>
        public void UpdateFanMode(string mode)
        {
            _currentFanMode = mode;
            UpdateFanModeButtons();
        }

        /// <summary>
        /// Update the current performance mode display.
        /// </summary>
        public void UpdatePerformanceMode(string mode)
        {
            _currentPerformanceMode = mode;
            UpdatePerformanceModeButtons();
        }

        public void UpdateMonitoringHealth(string health)
        {
            _monitoringHealth = string.IsNullOrWhiteSpace(health) ? "Unknown" : health;
            HealthStatusText.Text = $"Monitoring: {_monitoringHealth}";
            HealthStatusText.Foreground = _monitoringHealth switch
            {
                "Healthy" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xC8, 0xC8)),  // Teal
                "Degraded" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00)), // Amber
                "Stale" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x00, 0x5C)),    // Red
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF))           // Grey
            };
        }

        private void UpdateDisplay(object? sender, EventArgs e)
        {
            if (_latestSample == null) return;
            
            // Update temperature displays
            CpuTempText.Text = _latestSample.CpuTemperatureC.ToString("0");
            GpuTempText.Text = _latestSample.GpuTemperatureC.ToString("0");
            CpuLoadText.Text = $"{_latestSample.CpuLoadPercent:0}%";
            GpuLoadText.Text = $"{_latestSample.GpuLoadPercent:0}%";
            HealthStatusText.Text = $"Monitoring: {_monitoringHealth}";
        }

        private void UpdateFanModeButtons()
        {
            // Reset all buttons to default style
            FanAutoBtn.Style = (Style)FindResource("ModeButtonStyle");
            FanMaxBtn.Style = (Style)FindResource("ModeButtonStyle");
            FanQuietBtn.Style = (Style)FindResource("ModeButtonStyle");
            
            // Highlight active button
            var activeStyle = (Style)FindResource("ActiveModeButtonStyle");
            switch (_currentFanMode.ToLower())
            {
                case "auto":
                case "default":
                    FanAutoBtn.Style = activeStyle;
                    break;
                case "max":
                case "maximum":
                    FanMaxBtn.Style = activeStyle;
                    break;
                case "quiet":
                case "silent":
                    FanQuietBtn.Style = activeStyle;
                    break;
            }
        }

        private void UpdatePerformanceModeButtons()
        {
            // Reset all buttons to default style
            PerfBalancedBtn.Style = (Style)FindResource("ModeButtonStyle");
            PerfPerformanceBtn.Style = (Style)FindResource("ModeButtonStyle");
            PerfQuietBtn.Style = (Style)FindResource("ModeButtonStyle");
            
            // Highlight active button
            var activeStyle = (Style)FindResource("ActiveModeButtonStyle");
            switch (_currentPerformanceMode.ToLower())
            {
                case "balanced":
                case "default":
                    PerfBalancedBtn.Style = activeStyle;
                    break;
                case "performance":
                case "high":
                    PerfPerformanceBtn.Style = activeStyle;
                    break;
                case "quiet":
                case "powersaver":
                    PerfQuietBtn.Style = activeStyle;
                    break;
            }
        }

        private void UpdateRefreshRateDisplay()
        {
            int currentRate = _displayService.GetCurrentRefreshRate();
            RefreshRateText.Text = currentRate > 0 ? $"{currentRate}Hz" : "N/A";
        }

        private void FanMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                FanModeChangeRequested?.Invoke(mode);
                UpdateFanMode(mode);
            }
        }

        private void PerformanceMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                PerformanceModeChangeRequested?.Invoke(mode);
                UpdatePerformanceMode(mode);
            }
        }

        private void DisplayOff_Click(object sender, RoutedEventArgs e)
        {
            // Close popup first, then turn off display
            Hide();
            _displayService.TurnOffDisplay();
        }

        private void RefreshRate_Click(object sender, RoutedEventArgs e)
        {
            int newRate = _displayService.ToggleRefreshRate();
            if (newRate > 0)
            {
                RefreshRateText.Text = $"{newRate}Hz";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _updateTimer.Stop();
            Hide();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Auto-hide when clicking outside
            _updateTimer.Stop();
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateDisplay;
            // DisplayService doesn't need disposal
            base.OnClosed(e);
        }
    }
}
