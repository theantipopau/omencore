using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OmenCore.Utils;

namespace OmenCore.Services;

/// <summary>
/// Toast notification service for displaying brief mode change notifications.
/// 
/// Shows a non-intrusive toast at the top-center of the screen when:
/// - Fan profile changes
/// - Performance mode changes
/// - Keyboard lighting changes
/// - GPU power settings change
/// 
/// Toasts automatically fade out after a few seconds.
/// </summary>
public class ToastNotificationService : IDisposable
{
    private readonly ConfigurationService _config;
    private readonly LoggingService _logging;
    private Window? _toastWindow;
    private DispatcherTimer? _hideTimer;
    private readonly object _lock = new();
    
    public bool IsEnabled => _config.Config.Osd.ShowModeChangeNotifications;
    
    public ToastNotificationService(ConfigurationService config, LoggingService logging)
    {
        _config = config;
        _logging = logging;
    }
    
    /// <summary>
    /// Show a mode change notification.
    /// </summary>
    /// <param name="title">Mode type (e.g., "Fan Profile")</param>
    /// <param name="value">New value (e.g., "Gaming")</param>
    /// <param name="icon">Optional icon character</param>
    public void ShowModeChange(string title, string value, string? icon = null)
    {
        if (!IsEnabled) return;
        
        DispatcherHelper.RunOnUiThread(() =>
        {
            try
            {
                ShowToastInternal(title, value, icon);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Toast: Failed to show notification: {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// Show a fan profile change notification.
    /// </summary>
    public void ShowFanProfileChange(string profile)
    {
        ShowModeChange("Fan Profile", profile, "🌀");
    }
    
    /// <summary>
    /// Show a performance mode change notification.
    /// </summary>
    public void ShowPerformanceModeChange(string mode)
    {
        ShowModeChange("Performance Mode", mode, "⚡");
    }
    
    /// <summary>
    /// Show a GPU power setting change notification.
    /// </summary>
    public void ShowGpuPowerChange(string setting, string value)
    {
        ShowModeChange($"GPU {setting}", value, "🎮");
    }
    
    /// <summary>
    /// Show a keyboard lighting change notification.
    /// </summary>
    public void ShowKeyboardLightingChange(string setting, string value)
    {
        ShowModeChange($"Keyboard {setting}", value, "💡");
    }
    
    /// <summary>
    /// Show a generic success notification.
    /// </summary>
    public void ShowSuccess(string message)
    {
        ShowModeChange("Success", message, "✓");
    }
    
    /// <summary>
    /// Show a generic error notification.
    /// </summary>
    public void ShowError(string message)
    {
        ShowModeChange("Error", message, "✗");
    }
    
    private void ShowToastInternal(string title, string value, string? icon)
    {
        lock (_lock)
        {
            // Close existing toast
            _hideTimer?.Stop();
            _toastWindow?.Close();
            
            // Create toast window
            _toastWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Width = 280,
                Height = 70,
                ResizeMode = ResizeMode.NoResize
            };
            
            // Position at top-center of primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            _toastWindow.Left = (screenWidth - _toastWindow.Width) / 2;
            _toastWindow.Top = 30;
            
            // Build content
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 16, 16, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 0, 92)), // OMEN red
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 10, 16, 10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.4,
                    BlurRadius = 10,
                    ShadowDepth = 2
                }
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Icon
            if (!string.IsNullOrEmpty(icon))
            {
                var iconText = new TextBlock
                {
                    Text = icon,
                    FontSize = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(iconText, 0);
                grid.Children.Add(iconText);
            }
            
            // Text stack
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                FontFamily = AppFonts.App,
                FontWeight = FontWeights.Normal
            };
            textStack.Children.Add(titleText);
            
            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 16,
                Foreground = Brushes.White,
                FontFamily = AppFonts.App,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            };
            textStack.Children.Add(valueText);
            
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);
            
            border.Child = grid;
            _toastWindow.Content = border;
            
            // Show with fade-in animation
            _toastWindow.Opacity = 0;
            _toastWindow.Show();
            
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            _toastWindow.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            
            // Setup hide timer
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _hideTimer.Tick += (_, _) =>
            {
                HideToast();
            };
            _hideTimer.Start();
        }
    }
    
    private void HideToast()
    {
        lock (_lock)
        {
            _hideTimer?.Stop();
            
            if (_toastWindow == null) return;
            
            // Fade out animation
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) =>
            {
                _toastWindow?.Close();
                _toastWindow = null;
            };
            _toastWindow.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
    
    public void Dispose()
    {
        _hideTimer?.Stop();
        _toastWindow?.Close();
    }
}
