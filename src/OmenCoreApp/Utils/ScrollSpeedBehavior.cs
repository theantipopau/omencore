using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OmenCore.Utils
{
    /// <summary>
    /// Attached behavior for smooth animated scrolling in ScrollViewer.
    /// Supports both traditional mouse wheel and precision touchpad scrolling.
    /// Usage: utils:ScrollSpeedBehavior.ScrollSpeedMultiplier="1.2"
    /// </summary>
    public static class ScrollSpeedBehavior
    {
        private static double _targetOffset;
        private static bool _isAnimating;
        private static DateTime _lastScrollTime = DateTime.MinValue;
        private static int _accumulatedDelta;
        private const int TouchpadThreshold = 50; // Detect touchpad by small deltas
        private const int AccumulationTimeMs = 50; // Time window to accumulate touchpad deltas
        
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
                var now = DateTime.Now;
                var timeSinceLastScroll = (now - _lastScrollTime).TotalMilliseconds;
                
                // Detect precision touchpad scrolling (small deltas in rapid succession)
                bool isTouchpad = Math.Abs(e.Delta) < TouchpadThreshold;
                
                double scrollAmount;
                
                if (isTouchpad)
                {
                    // For touchpad: accumulate small deltas and use direct scrolling for smoothness
                    if (timeSinceLastScroll > AccumulationTimeMs)
                    {
                        _accumulatedDelta = 0;
                    }
                    
                    _accumulatedDelta += e.Delta;
                    
                    // Direct scroll for touchpad - WPF handles smoothness natively
                    scrollAmount = -(_accumulatedDelta / 120.0) * 40 * multiplier;
                    
                    // Apply directly without animation for touchpad (smoother)
                    var newOffset = Math.Max(0, Math.Min(
                        scrollViewer.ScrollableHeight,
                        scrollViewer.VerticalOffset + (e.Delta / -120.0) * 40 * multiplier));
                    
                    scrollViewer.ScrollToVerticalOffset(newOffset);
                    _accumulatedDelta = 0;
                }
                else
                {
                    // For mouse wheel: use animated scrolling
                    // e.Delta is typically 120 per notch, we want about 80-100 pixels per notch
                    scrollAmount = (e.Delta / 120.0) * 80 * multiplier;
                    
                    // Initialize target offset if not animating or starting fresh
                    if (!_isAnimating || timeSinceLastScroll > 300)
                    {
                        _targetOffset = scrollViewer.VerticalOffset;
                    }
                    
                    // Calculate new target (clamped to valid range)
                    _targetOffset = Math.Max(0, Math.Min(
                        scrollViewer.ScrollableHeight,
                        _targetOffset - scrollAmount));
                    
                    // Animate to the target
                    AnimateScroll(scrollViewer, _targetOffset);
                }
                
                _lastScrollTime = now;
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
