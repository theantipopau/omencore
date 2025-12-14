using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OmenCore.Utils
{
    /// <summary>
    /// Attached behavior for smooth animated scrolling in ScrollViewer.
    /// Usage: utils:ScrollSpeedBehavior.ScrollSpeedMultiplier="1.2"
    /// </summary>
    public static class ScrollSpeedBehavior
    {
        private static double _targetOffset;
        private static bool _isAnimating;
        
        public static readonly DependencyProperty ScrollSpeedMultiplierProperty =
            DependencyProperty.RegisterAttached(
                "ScrollSpeedMultiplier",
                typeof(double),
                typeof(ScrollSpeedBehavior),
                new PropertyMetadata(1.0, OnScrollSpeedMultiplierChanged));

        public static double GetScrollSpeedMultiplier(DependencyObject obj)
            => (double)obj.GetValue(ScrollSpeedMultiplierProperty);

        public static void SetScrollSpeedMultiplier(DependencyObject obj, double value)
            => obj.SetValue(ScrollSpeedMultiplierProperty, value);

        private static void OnScrollSpeedMultiplierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && !e.Handled)
            {
                var multiplier = GetScrollSpeedMultiplier(scrollViewer);
                
                // Calculate scroll amount - use a reasonable base amount 
                // e.Delta is typically 120 per notch, we want about 80-100 pixels per notch
                var scrollAmount = (e.Delta / 120.0) * 80 * multiplier;
                
                // Initialize target offset if not animating
                if (!_isAnimating)
                {
                    _targetOffset = scrollViewer.VerticalOffset;
                }
                
                // Calculate new target (clamped to valid range)
                _targetOffset = Math.Max(0, Math.Min(
                    scrollViewer.ScrollableHeight,
                    _targetOffset - scrollAmount));
                
                // Animate to the target
                AnimateScroll(scrollViewer, _targetOffset);
                
                e.Handled = true;
            }
        }
        
        private static void AnimateScroll(ScrollViewer scrollViewer, double targetOffset)
        {
            _isAnimating = true;
            
            // Create smooth animation
            var animation = new DoubleAnimation
            {
                From = scrollViewer.VerticalOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            animation.Completed += (s, e) => _isAnimating = false;
            
            // Create a storyboard to control the animation
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            
            // We can't directly animate VerticalOffset, so use a workaround with CompositionTarget
            var startOffset = scrollViewer.VerticalOffset;
            var delta = targetOffset - startOffset;
            var startTime = DateTime.Now;
            var duration = animation.Duration.TimeSpan;
            
            void OnRendering(object? s, EventArgs args)
            {
                var elapsed = DateTime.Now - startTime;
                if (elapsed >= duration)
                {
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
                    _isAnimating = false;
                    return;
                }
                
                // Apply easing (quadratic ease out)
                var progress = elapsed.TotalMilliseconds / duration.TotalMilliseconds;
                var easedProgress = 1 - Math.Pow(1 - progress, 2); // Ease out quad
                
                var newOffset = startOffset + (delta * easedProgress);
                scrollViewer.ScrollToVerticalOffset(newOffset);
            }
            
            System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        }
    }
}
