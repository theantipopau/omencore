using System;
using System.Collections.Generic;
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
        private string? _curvePresetName;
        private string _monitoringHealth = "Unknown";
        private bool _isFanPerformanceLinked;
        private List<DisplayTarget> _displayTargets = new();
        private int _activeDisplayTargetIndex;
        
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

            RefreshDisplayTargets();
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
        /// Update the Curve button tooltip with the active preset name so users know
        /// which saved fan curve will be applied when they click Curve.
        /// </summary>
        public void UpdateCurvePresetName(string? presetName)
        {
            _curvePresetName = presetName;
            var tooltip = string.IsNullOrWhiteSpace(presetName)
                ? "Apply saved fan curve"
                : $"Curve: {presetName}";
            FanCustomBtn.ToolTip = tooltip;
            UpdatePerformanceModeTooltips();
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

        public void UpdateLinkedMode(bool linked)
        {
            _isFanPerformanceLinked = linked;
            UpdateLinkModeText();
            LinkModeText.Foreground = linked
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xC8, 0xC8))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
            LinkModeText.ToolTip = linked
                ? "Linked mode is enabled. Performance changes may also rewrite fan policy."
                : "Decoupled mode is enabled. Performance changes leave your current fan preset or curve untouched.";
            UpdatePerformanceModeTooltips();
        }

        private void UpdateDisplay(object? sender, EventArgs e)
        {
            if (_latestSample == null) return;
            
            // Update temperature displays (show — when sensor data unavailable)
            CpuTempText.Text = _latestSample.CpuTemperatureC > 0 ? _latestSample.CpuTemperatureC.ToString("0") : "—";
            GpuTempText.Text = _latestSample.GpuTemperatureC > 0 ? _latestSample.GpuTemperatureC.ToString("0") : "—";
            CpuLoadText.Text = $"{_latestSample.CpuLoadPercent:0}%";
            GpuLoadText.Text = $"{_latestSample.GpuLoadPercent:0}%";
            HealthStatusText.Text = $"Monitoring: {_monitoringHealth}";
            UpdateLinkModeText();
        }

        private void UpdateLinkModeText()
        {
            LinkModeText.Text = _isFanPerformanceLinked
                ? "Fan/Perf: Linked"
                : "Fan/Perf: Decoupled - fan stays";
        }

        private void UpdatePerformanceModeTooltips()
        {
            var preservedFanText = string.IsNullOrWhiteSpace(_curvePresetName)
                ? "your current fan preset"
                : $"the active curve preset '{_curvePresetName}'";

            var tooltip = _isFanPerformanceLinked
                ? "Apply this performance mode. Linked mode can also update fan policy."
                : $"Apply this performance mode. Decoupled mode keeps {preservedFanText} active.";

            PerfQuietBtn.ToolTip = tooltip;
            PerfBalancedBtn.ToolTip = tooltip;
            PerfPerformanceBtn.ToolTip = tooltip;
        }

        private void UpdateFanModeButtons()
        {
            // Reset all buttons to default style
            FanAutoBtn.Style = (Style)FindResource("ModeButtonStyle");
            FanMaxBtn.Style = (Style)FindResource("ModeButtonStyle");
            FanQuietBtn.Style = (Style)FindResource("ModeButtonStyle");
            FanCustomBtn.Style = (Style)FindResource("ModeButtonStyle");
            
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
                default:
                    FanCustomBtn.Style = activeStyle;
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
            var target = GetActiveDisplayTarget();
            int currentRate = _displayService.GetCurrentRefreshRate(target?.DeviceName);

            if (target == null)
            {
                RefreshRateText.Text = currentRate > 0 ? $"{currentRate}Hz" : "N/A";
                RefreshRateBtn.ToolTip = "Toggle refresh rate";
                return;
            }

            var targetLabel = $"D{_activeDisplayTargetIndex + 1}";
            RefreshRateText.Text = currentRate > 0 ? $"{targetLabel} {currentRate}Hz" : $"{targetLabel} N/A";
            RefreshRateBtn.ToolTip = $"Toggle refresh rate for {target.FriendlyName}. Click again to move to the next display.";
        }

        private void RefreshDisplayTargets()
        {
            _displayTargets = _displayService.GetDisplayTargets();
            if (_displayTargets.Count == 0)
            {
                _activeDisplayTargetIndex = 0;
                return;
            }

            var primaryIndex = _displayTargets.FindIndex(target => target.IsPrimary);
            _activeDisplayTargetIndex = primaryIndex >= 0 ? primaryIndex : 0;
        }

        private DisplayTarget? GetActiveDisplayTarget()
        {
            if (_displayTargets.Count == 0)
            {
                return null;
            }

            if (_activeDisplayTargetIndex < 0 || _activeDisplayTargetIndex >= _displayTargets.Count)
            {
                _activeDisplayTargetIndex = 0;
            }

            return _displayTargets[_activeDisplayTargetIndex];
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
            var target = GetActiveDisplayTarget();
            int newRate = _displayService.ToggleRefreshRate(target?.DeviceName);
            if (newRate > 0)
            {
                if (_displayTargets.Count > 1)
                {
                    _activeDisplayTargetIndex = (_activeDisplayTargetIndex + 1) % _displayTargets.Count;
                }

                UpdateRefreshRateDisplay();
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
