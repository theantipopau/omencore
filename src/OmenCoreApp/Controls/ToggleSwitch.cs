using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OmenCore.Controls
{
    /// <summary>
    /// A modern toggle switch control with iOS-style appearance.
    /// </summary>
    public class ToggleSwitch : ToggleButton
    {
        static ToggleSwitch()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ToggleSwitch),
                new FrameworkPropertyMetadata(typeof(ToggleSwitch)));
        }

        #region Dependency Properties

        public static readonly DependencyProperty OnContentProperty =
            DependencyProperty.Register(nameof(OnContent), typeof(object), typeof(ToggleSwitch),
                new PropertyMetadata("On"));

        public static readonly DependencyProperty OffContentProperty =
            DependencyProperty.Register(nameof(OffContent), typeof(object), typeof(ToggleSwitch),
                new PropertyMetadata("Off"));

        public static readonly DependencyProperty OnBackgroundProperty =
            DependencyProperty.Register(nameof(OnBackground), typeof(Brush), typeof(ToggleSwitch),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xC8)))); // Accent cyan

        public static readonly DependencyProperty OffBackgroundProperty =
            DependencyProperty.Register(nameof(OffBackground), typeof(Brush), typeof(ToggleSwitch),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x5C)))); // Surface highlight

        public static readonly DependencyProperty ThumbColorProperty =
            DependencyProperty.Register(nameof(ThumbColor), typeof(Brush), typeof(ToggleSwitch),
                new PropertyMetadata(Brushes.White));

        public static readonly DependencyProperty SwitchWidthProperty =
            DependencyProperty.Register(nameof(SwitchWidth), typeof(double), typeof(ToggleSwitch),
                new PropertyMetadata(50.0));

        public static readonly DependencyProperty SwitchHeightProperty =
            DependencyProperty.Register(nameof(SwitchHeight), typeof(double), typeof(ToggleSwitch),
                new PropertyMetadata(26.0));

        public static readonly DependencyProperty ThumbSizeProperty =
            DependencyProperty.Register(nameof(ThumbSize), typeof(double), typeof(ToggleSwitch),
                new PropertyMetadata(22.0));

        public static readonly DependencyProperty ShowLabelProperty =
            DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(ToggleSwitch),
                new PropertyMetadata(false));

        #endregion

        #region Properties

        /// <summary>
        /// Content to display when toggle is ON.
        /// </summary>
        public object OnContent
        {
            get => GetValue(OnContentProperty);
            set => SetValue(OnContentProperty, value);
        }

        /// <summary>
        /// Content to display when toggle is OFF.
        /// </summary>
        public object OffContent
        {
            get => GetValue(OffContentProperty);
            set => SetValue(OffContentProperty, value);
        }

        /// <summary>
        /// Background color when toggle is ON.
        /// </summary>
        public Brush OnBackground
        {
            get => (Brush)GetValue(OnBackgroundProperty);
            set => SetValue(OnBackgroundProperty, value);
        }

        /// <summary>
        /// Background color when toggle is OFF.
        /// </summary>
        public Brush OffBackground
        {
            get => (Brush)GetValue(OffBackgroundProperty);
            set => SetValue(OffBackgroundProperty, value);
        }

        /// <summary>
        /// Color of the toggle thumb.
        /// </summary>
        public Brush ThumbColor
        {
            get => (Brush)GetValue(ThumbColorProperty);
            set => SetValue(ThumbColorProperty, value);
        }

        /// <summary>
        /// Width of the switch track.
        /// </summary>
        public double SwitchWidth
        {
            get => (double)GetValue(SwitchWidthProperty);
            set => SetValue(SwitchWidthProperty, value);
        }

        /// <summary>
        /// Height of the switch track.
        /// </summary>
        public double SwitchHeight
        {
            get => (double)GetValue(SwitchHeightProperty);
            set => SetValue(SwitchHeightProperty, value);
        }

        /// <summary>
        /// Size of the toggle thumb.
        /// </summary>
        public double ThumbSize
        {
            get => (double)GetValue(ThumbSizeProperty);
            set => SetValue(ThumbSizeProperty, value);
        }

        /// <summary>
        /// Whether to show On/Off labels next to the switch.
        /// </summary>
        public bool ShowLabel
        {
            get => (bool)GetValue(ShowLabelProperty);
            set => SetValue(ShowLabelProperty, value);
        }

        #endregion
    }
}
