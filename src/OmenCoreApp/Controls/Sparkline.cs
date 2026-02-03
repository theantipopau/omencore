using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OmenCore.Controls
{
    /// <summary>
    /// A lightweight sparkline control for showing recent data trends.
    /// </summary>
    public class Sparkline : FrameworkElement
    {
        #region Dependency Properties

        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Sparkline),
                new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(double?), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double?), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowEndDotProperty =
            DependencyProperty.Register(nameof(ShowEndDot), typeof(bool), typeof(Sparkline),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EndDotBrushProperty =
            DependencyProperty.Register(nameof(EndDotBrush), typeof(Brush), typeof(Sparkline),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EndDotRadiusProperty =
            DependencyProperty.Register(nameof(EndDotRadius), typeof(double), typeof(Sparkline),
                new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region Properties

        /// <summary>
        /// The data values to display.
        /// </summary>
        public IEnumerable<double> Values
        {
            get => (IEnumerable<double>)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        /// <summary>
        /// The stroke brush for the line.
        /// </summary>
        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        /// <summary>
        /// The stroke thickness.
        /// </summary>
        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        /// <summary>
        /// Optional fill brush for area under the line.
        /// </summary>
        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        /// <summary>
        /// Minimum value for Y-axis scaling. Auto-calculated if null.
        /// </summary>
        public double? MinValue
        {
            get => (double?)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }

        /// <summary>
        /// Maximum value for Y-axis scaling. Auto-calculated if null.
        /// </summary>
        public double? MaxValue
        {
            get => (double?)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        /// <summary>
        /// Whether to show a dot at the end of the line.
        /// </summary>
        public bool ShowEndDot
        {
            get => (bool)GetValue(ShowEndDotProperty);
            set => SetValue(ShowEndDotProperty, value);
        }

        /// <summary>
        /// The brush for the end dot. Uses Stroke if null.
        /// </summary>
        public Brush EndDotBrush
        {
            get => (Brush)GetValue(EndDotBrushProperty);
            set => SetValue(EndDotBrushProperty, value);
        }

        /// <summary>
        /// The radius of the end dot.
        /// </summary>
        public double EndDotRadius
        {
            get => (double)GetValue(EndDotRadiusProperty);
            set => SetValue(EndDotRadiusProperty, value);
        }

        #endregion

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var values = Values?.ToList();
            if (values == null || values.Count < 2)
                return;

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            // Calculate min/max for scaling
            var min = MinValue ?? values.Min();
            var max = MaxValue ?? values.Max();
            
            // Add padding to prevent flat lines at edges
            var range = max - min;
            if (range < 0.001)
            {
                min -= 5;
                max += 5;
                range = max - min;
            }

            // Calculate points
            var points = new List<Point>();
            var stepX = width / (values.Count - 1);
            
            for (int i = 0; i < values.Count; i++)
            {
                var x = i * stepX;
                var normalizedY = (values[i] - min) / range;
                var y = height - (normalizedY * height); // Invert Y axis
                points.Add(new Point(x, Math.Clamp(y, 0, height)));
            }

            // Draw fill if specified
            if (Fill != null)
            {
                var fillGeometry = new StreamGeometry();
                using (var ctx = fillGeometry.Open())
                {
                    ctx.BeginFigure(new Point(0, height), true, true);
                    foreach (var point in points)
                    {
                        ctx.LineTo(point, true, false);
                    }
                    ctx.LineTo(new Point(width, height), true, false);
                }
                fillGeometry.Freeze();
                dc.DrawGeometry(Fill, null, fillGeometry);
            }

            // Draw line
            var lineGeometry = new StreamGeometry();
            using (var ctx = lineGeometry.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i], true, false);
                }
            }
            lineGeometry.Freeze();
            
            var pen = new Pen(Stroke, StrokeThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            
            dc.DrawGeometry(null, pen, lineGeometry);

            // Draw end dot
            if (ShowEndDot && points.Count > 0)
            {
                var lastPoint = points[points.Count - 1];
                var dotBrush = EndDotBrush ?? Stroke;
                dc.DrawEllipse(dotBrush, null, lastPoint, EndDotRadius, EndDotRadius);
            }
        }
    }
}
