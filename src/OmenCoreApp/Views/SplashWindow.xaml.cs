using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using OmenCore.Services;

namespace OmenCore.Views
{
    /// <summary>
    /// Branded splash screen with startup progress indicators.
    /// Shows during application initialization.
    /// </summary>
    public partial class SplashWindow : Window
    {
        private readonly StartupSequencer? _sequencer;
        
        public SplashWindow()
        {
            InitializeComponent();
            
            // Set version from assembly
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }
        
        public SplashWindow(StartupSequencer sequencer) : this()
        {
            _sequencer = sequencer;
            
            if (_sequencer != null)
            {
                _sequencer.ProgressChanged += OnProgressChanged;
                _sequencer.Completed += OnCompleted;
            }
        }
        
        /// <summary>
        /// Update progress and status text.
        /// </summary>
        public void UpdateProgress(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percent;
                StatusText.Text = status;
            });
        }
        
        /// <summary>
        /// Set status text without changing progress.
        /// </summary>
        public void SetStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }
        
        /// <summary>
        /// Animate progress bar to a value.
        /// </summary>
        public void AnimateProgress(int targetPercent, TimeSpan duration)
        {
            Dispatcher.Invoke(() =>
            {
                var animation = new DoubleAnimation
                {
                    To = targetPercent,
                    Duration = duration,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation);
            });
        }
        
        /// <summary>
        /// Close splash with fade out animation.
        /// </summary>
        public void CloseWithFade()
        {
            Dispatcher.Invoke(() =>
            {
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                fadeOut.Completed += (s, e) => Close();
                BeginAnimation(OpacityProperty, fadeOut);
            });
        }
        
        private void OnProgressChanged(object? sender, StartupProgressEventArgs e)
        {
            UpdateProgress(e.ProgressPercent, e.CurrentTask);
        }
        
        private void OnCompleted(object? sender, StartupCompletedEventArgs e)
        {
            UpdateProgress(100, "Ready!");
        }
        
        protected override void OnClosed(EventArgs e)
        {
            if (_sequencer != null)
            {
                _sequencer.ProgressChanged -= OnProgressChanged;
                _sequencer.Completed -= OnCompleted;
            }
            base.OnClosed(e);
        }
    }
}
