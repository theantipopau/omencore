using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OmenCore.Controls
{
    /// <summary>
    /// A circular gauge control for displaying percentage values (like fan speed, CPU load).
    /// </summary>
    public class CircularGauge : Control
    {
        #region Dependency Properties

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GaugeBrushProperty =
            DependencyProperty.Register(nameof(GaugeBrush), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(230, 0, 46)), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StartAngleProperty =
            DependencyProperty.Register(nameof(StartAngle), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(-135.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SweepAngleProperty =
            DependencyProperty.Register(nameof(SweepAngle), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(270.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowValueProperty =
            DependencyProperty.Register(nameof(ShowValue), typeof(bool), typeof(CircularGauge),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFormatProperty =
            DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(CircularGauge),
                new FrameworkPropertyMetadata("{0:F0}%", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFontSizeProperty =
            DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region Properties

        /// <summary>
        /// Current value of the gauge.
        /// </summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>
        /// Minimum value (typically 0).
        /// </summary>
        public double MinValue
        {
            get => (double)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }

        /// <summary>
        /// Maximum value (typically 100).
        /// </summary>
        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        /// <summary>
        /// Background track brush.
        /// </summary>
        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        /// <summary>
        /// Gauge arc brush.
        /// </summary>
        public Brush GaugeBrush
        {
            get => (Brush)GetValue(GaugeBrushProperty);
            set => SetValue(GaugeBrushProperty, value);
        }

        /// <summary>
        /// Thickness of the gauge arc.
        /// </summary>
        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        /// <summary>
        /// Start angle in degrees (-135 for typical gauge).
        /// </summary>
        public double StartAngle
        {
            get => (double)GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
        }

        /// <summary>
        /// Sweep angle in degrees (270 for typical gauge).
        /// </summary>
        public double SweepAngle
        {
            get => (double)GetValue(SweepAngleProperty);
            set => SetValue(SweepAngleProperty, value);
        }

        /// <summary>
        /// Whether to show the value text in the center.
        /// </summary>
        public bool ShowValue
        {
            get => (bool)GetValue(ShowValueProperty);
            set => SetValue(ShowValueProperty, value);
        }

        /// <summary>
        /// Format string for the value text.
        /// </summary>
        public string ValueFormat
        {
            get => (string)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>
        /// Font size for the value text.
        /// </summary>
        public double ValueFontSize
        {
            get => (double)GetValue(ValueFontSizeProperty);
            set => SetValue(ValueFontSizeProperty, value);
        }

        #endregion

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            var center = new Point(width / 2, height / 2);
            var radius = Math.Min(width, height) / 2 - StrokeThickness / 2;
            if (radius <= 0)
                return;

            // Calculate value percentage
            var range = MaxValue - MinValue;
            var normalizedValue = range > 0 ? Math.Clamp((Value - MinValue) / range, 0, 1) : 0;
            var valueAngle = normalizedValue * SweepAngle;

            // Draw track arc
            DrawArc(dc, center, radius, StartAngle, SweepAngle, TrackBrush, StrokeThickness);

            // Draw value arc
            if (valueAngle > 0.1)
            {
                DrawArc(dc, center, radius, StartAngle, valueAngle, GaugeBrush, StrokeThickness);
            }

            // Draw value text
            if (ShowValue)
            {
                var formattedText = new FormattedText(
                    string.Format(ValueFormat, Value),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    ValueFontSize,
                    Foreground ?? Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                var textOrigin = new Point(
                    center.X - formattedText.Width / 2,
                    center.Y - formattedText.Height / 2);

                dc.DrawText(formattedText, textOrigin);
            }
        }

        private void DrawArc(DrawingContext dc, Point center, double radius, double startAngle, double sweepAngle, Brush brush, double thickness)
        {
            if (sweepAngle >= 360)
            {
                // Full circle
                dc.DrawEllipse(null, new Pen(brush, thickness), center, radius, radius);
                return;
            }

            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));

            var endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            var isLargeArc = sweepAngle > 180;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();

            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();

            dc.DrawGeometry(null, pen, geometry);
        }
    }
}
