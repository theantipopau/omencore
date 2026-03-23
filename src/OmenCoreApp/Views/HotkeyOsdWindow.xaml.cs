using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Forms;
using OmenCore.Models;

namespace OmenCore.Views
{
    /// <summary>
    /// On-Screen Display window that shows mode changes when hotkeys are pressed.
    /// Appears briefly in the corner of the screen and fades out automatically.
    /// </summary>
    public partial class HotkeyOsdWindow : Window
    {
        private readonly DispatcherTimer _dismissTimer;
        private bool _isAnimatingOut;
        private DateTime _lastShowUtc = DateTime.MinValue;
        private const int MinReshowMs = 120;
        private int _dismissDurationMs = 2200;
        private bool _compactMode;
        private string _accentMode = "Auto";
        private double _scaleFactor = 1.0;

        public HotkeyOsdWindow()
        {
            InitializeComponent();
            
            // Set up auto-dismiss timer
            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };
            _dismissTimer.Tick += DismissTimer_Tick;
        }

        /// <summary>
        /// Show the OSD with the specified mode information
        /// </summary>
        public void ShowMode(string category, string modeName, string? hotkeyDescription = null)
        {
            // Avoid animation spam and visual jitter when multiple hotkeys fire
            // in the same short burst; just refresh content if OSD is already visible.
            var now = DateTime.UtcNow;
            if (IsVisible && (now - _lastShowUtc).TotalMilliseconds < MinReshowMs)
            {
                ModeCategory.Text = category;
                ModeName.Text = modeName;
                ModeIcon.Text = GetModeIcon(category, modeName);
                TriggerText.Text = hotkeyDescription ?? "via Hotkey";
                UpdateAccentColor(modeName);
                _dismissTimer.Stop();
                _dismissTimer.Interval = TimeSpan.FromMilliseconds(_dismissDurationMs);
                _dismissTimer.Start();
                StartProgressAnimation();
                _lastShowUtc = now;
                return;
            }

            // Cancel any existing animations
            _isAnimatingOut = false;
            _dismissTimer.Stop();
            
            // Update content
            ModeCategory.Text = category;
            ModeName.Text = modeName;
            ModeIcon.Text = GetModeIcon(category, modeName);
            TriggerText.Text = hotkeyDescription ?? "via Hotkey";
            
            // Update accent color based on mode
            UpdateAccentColor(modeName);
            _lastShowUtc = now;
            
            // Show the window first (required for accurate size measurement)
            Opacity = 0;
            Show();
            
            // Use Dispatcher to position after layout is complete
            // This ensures ActualWidth/ActualHeight are properly calculated
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                PositionWindow();
                AnimateIn();
                _dismissTimer.Interval = TimeSpan.FromMilliseconds(_dismissDurationMs);
                _dismissTimer.Start();
                StartProgressAnimation();
            });
        }

        public void ApplySettings(OsdSettings? settings)
        {
            if (settings == null)
            {
                return;
            }

            _dismissDurationMs = Math.Clamp(settings.HotkeyToastDurationMs, 800, 6000);
            _compactMode = settings.HotkeyToastCompact;
            _accentMode = string.IsNullOrWhiteSpace(settings.HotkeyToastAccent) ? "Auto" : settings.HotkeyToastAccent;
            _scaleFactor = Math.Clamp(settings.HotkeyToastScalePercent, 80, 140) / 100.0;

            OsdBorder.LayoutTransform = new ScaleTransform(_scaleFactor, _scaleFactor);

            if (_compactMode)
            {
                OsdBorder.Padding = new Thickness(16, 12, 16, 12);
                ModeBadge.Width = 44;
                ModeBadge.Height = 44;
                ModeBadge.CornerRadius = new CornerRadius(22);
                ModeIcon.FontSize = 12;
                ModeName.FontSize = 18;
                ModeCategory.Visibility = Visibility.Collapsed;
                TriggerText.Visibility = Visibility.Collapsed;
            }
            else
            {
                OsdBorder.Padding = new Thickness(24, 16, 24, 16);
                ModeBadge.Width = 56;
                ModeBadge.Height = 56;
                ModeBadge.CornerRadius = new CornerRadius(28);
                ModeIcon.FontSize = 14;
                ModeName.FontSize = 22;
                ModeCategory.Visibility = Visibility.Visible;
                TriggerText.Visibility = Visibility.Visible;
            }
        }

        private void PositionWindow()
        {
            // Place on the monitor where the user currently is (cursor screen),
            // then clamp to that monitor's working area (excludes taskbar).
            var cursor = System.Windows.Forms.Cursor.Position;
            var screen = Screen.FromPoint(cursor);
            var workAreaRect = screen.WorkingArea;
            var workArea = new Rect(workAreaRect.Left, workAreaRect.Top, workAreaRect.Width, workAreaRect.Height);
            
            // Update layout to get actual size
            UpdateLayout();
            
            // Get actual dimensions (fallback to reasonable defaults if not yet measured)
            var width = ActualWidth > 0 ? ActualWidth : 300;
            var height = ActualHeight > 0 ? ActualHeight : 100;
            
            // Position in bottom-right corner with padding
            const double padding = 24;
            Left = workArea.Right - width - padding;
            Top = workArea.Bottom - height - padding;
            
            // Ensure window is within screen bounds (safety check)
            if (Left < workArea.Left) Left = workArea.Left + padding;
            if (Top < workArea.Top) Top = workArea.Top + padding;
        }

        private string GetModeIcon(string category, string modeName)
        {
            var lowerMode = modeName.ToLower();

            // Use deterministic text labels instead of emoji glyphs.
            // Emoji rendering can vary by font/locale and may clip in transparent overlays.
            var icon = category.ToLower() switch
            {
                "fan mode" => lowerMode switch
                {
                    "performance" or "max" or "turbo" => "PERF",
                    "quiet" or "silent" => "QUIET",
                    "balanced" or "auto" => "BAL",
                    _ => "FAN"
                },
                "performance" => lowerMode switch
                {
                    "performance" => "PWR",
                    "balanced" => "BAL",
                    "quiet" or "power saver" => "ECO",
                    _ => "SYS"
                },
                "boost" => "BST",
                _ => "OSD"
            };

            return icon;
        }

        private void UpdateAccentColor(string modeName)
        {
            var autoColor = modeName.ToLower() switch
            {
                "performance" or "boost" or "max" or "turbo" => Color.FromRgb(0xFF, 0x6B, 0x35), // Orange
                "quiet" or "silent" => Color.FromRgb(0x4E, 0xCD, 0xC4), // Teal
                "balanced" or "auto" => Color.FromRgb(0x00, 0xD4, 0xFF), // Cyan
                _ => Color.FromRgb(0x00, 0xD4, 0xFF) // Default cyan
            };

            var color = _accentMode.ToLowerInvariant() switch
            {
                "cyan" => Color.FromRgb(0x00, 0xD4, 0xFF),
                "orange" => Color.FromRgb(0xFF, 0x8A, 0x3D),
                "teal" => Color.FromRgb(0x4E, 0xCD, 0xC4),
                "magenta" => Color.FromRgb(0xFF, 0x4F, 0xC3),
                _ => autoColor
            };
            
            var brush = new SolidColorBrush(color);
            OsdBorder.BorderBrush = brush;
            ProgressFill.Background = brush;
        }

        private void AnimateIn()
        {
            // Fade in and slide up
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            var transform = EnsureTranslateTransform();
            transform.X = 0;
            transform.Y = 20;
            
            BeginAnimation(OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }

        private void AnimateOut(Action? onComplete = null)
        {
            if (_isAnimatingOut) return;
            _isAnimatingOut = true;
            ProgressScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            
            // Fade out and slide down
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                Hide();
                _isAnimatingOut = false;
                onComplete?.Invoke();
            };
            
            var transform = EnsureTranslateTransform();
            var slideOut = new DoubleAnimation(0, 20, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            
            OsdBorder.RenderTransform = transform;
            BeginAnimation(OpacityProperty, fadeOut);
            transform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        private void DismissTimer_Tick(object? sender, EventArgs e)
        {
            _dismissTimer.Stop();
            AnimateOut();
        }

        /// <summary>
        /// Dismiss the OSD immediately (with animation)
        /// </summary>
        public void Dismiss()
        {
            _dismissTimer.Stop();
            AnimateOut();
        }

        private void StartProgressAnimation()
        {
            ProgressScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ProgressScaleTransform.ScaleX = 1;
            ProgressScaleTransform.ScaleY = 1;

            var countdown = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(_dismissDurationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ProgressScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, countdown);
        }

        private TranslateTransform EnsureTranslateTransform()
        {
            if (OsdBorder.RenderTransform is TranslateTransform tt)
            {
                return tt;
            }

            if (OsdBorder.RenderTransform is TransformGroup group)
            {
                foreach (var child in group.Children)
                {
                    if (child is TranslateTransform gtt)
                    {
                        return gtt;
                    }
                }

                var created = new TranslateTransform();
                group.Children.Add(created);
                return created;
            }

            var transform = new TranslateTransform();
            OsdBorder.RenderTransform = transform;
            return transform;
        }
    }
}
