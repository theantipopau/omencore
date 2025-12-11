using OmenCore.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OmenCore.Controls
{
    public partial class ThermalChart : UserControl
    {
        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IEnumerable<ThermalSample>), typeof(ThermalChart),
            new PropertyMetadata(null, OnSamplesChanged));

        public IEnumerable<ThermalSample>? Samples
        {
            get => (IEnumerable<ThermalSample>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        private double _dpiScale = 1.0;
        private DateTime _lastRender = DateTime.MinValue;
        private const int RenderThrottleMs = 100; // Throttle to 10 FPS max
        private bool _renderPending;

        public ThermalChart()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateDpiScale();
        }

        private void UpdateDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScale = Math.Max(1.0, dpiX);
            }
        }

        private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ThermalChart chart)
            {
                if (e.OldValue is INotifyCollectionChanged old)
                {
                    old.CollectionChanged -= chart.SamplesChanged;
                }
                if (e.NewValue is INotifyCollectionChanged @new)
                {
                    @new.CollectionChanged += chart.SamplesChanged;
                }
                chart.RenderChart();
            }
        }

        private void SamplesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Throttle rendering to reduce CPU usage
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRender).TotalMilliseconds;
            
            if (elapsed < RenderThrottleMs)
            {
                if (!_renderPending)
                {
                    _renderPending = true;
                    Dispatcher.InvokeAsync(() =>
                    {
                        _renderPending = false;
                        RenderChart();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                return;
            }
            
            _lastRender = now;
            RenderChart();
        }

        private void RenderChart()
        {
            ChartCanvas.Children.Clear();
            var samplesList = Samples?.ToList() ?? new List<ThermalSample>();
            if (samplesList.Count < 2)
            {
                return;
            }

            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                ChartCanvas.SizeChanged -= ChartCanvas_SizeChanged;
                ChartCanvas.SizeChanged += ChartCanvas_SizeChanged;
                return;
            }

            double maxTemp = 100;
            foreach (var sample in samplesList)
            {
                maxTemp = Math.Max(maxTemp, Math.Max(sample.CpuCelsius, sample.GpuCelsius));
            }

            DrawGridlines(width, height, maxTemp);

            // Apply DPI-aware stroke thickness (minimum 2px, scales up on high-DPI displays)
            var strokeThickness = Math.Max(2.0, 1.5 * _dpiScale);
            
            var cpuPolyline = new Polyline 
            { 
                Stroke = (Brush)FindResource("AccentBrush"), 
                StrokeThickness = strokeThickness, 
                StrokeLineJoin = PenLineJoin.Round,
                CacheMode = new BitmapCache() // Enable visual caching for better performance
            };
            var gpuPolyline = new Polyline 
            { 
                Stroke = new SolidColorBrush(Color.FromRgb(31, 195, 255)), 
                StrokeThickness = strokeThickness, 
                StrokeDashArray = new DoubleCollection { 2, 2 },
                CacheMode = new BitmapCache()
            };

            for (int i = 0; i < samplesList.Count; i++)
            {
                var sample = samplesList[i];
                var x = width * i / Math.Max(1, samplesList.Count - 1);
                var cpuY = height - (sample.CpuCelsius / maxTemp) * height;
                var gpuY = height - (sample.GpuCelsius / maxTemp) * height;
                cpuPolyline.Points.Add(new Point(x, cpuY));
                gpuPolyline.Points.Add(new Point(x, gpuY));
            }

            ChartCanvas.Children.Add(cpuPolyline);
            ChartCanvas.Children.Add(gpuPolyline);
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderChart();
        }

        private void DrawGridlines(double width, double height, double maxTemp)
        {
            const int horizontalSegments = 4;
            const int verticalSegments = 6;

            var gridBrush = (TryFindResource("BorderBrush") as SolidColorBrush)?.Clone() ?? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            gridBrush.Opacity = 0.25;
            var textBrush = (Brush)(TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);

            for (var i = 1; i < horizontalSegments; i++)
            {
                var ratio = i / (double)horizontalSegments;
                var y = height * ratio;
                var labelValue = Math.Round(maxTemp * (1 - ratio));

                ChartCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeDashArray = new DoubleCollection { 2, 6 },
                    StrokeThickness = 1
                });

                var label = new TextBlock
                {
                    Text = $"{labelValue:0}°",
                    Foreground = textBrush,
                    FontSize = 10
                };
                Canvas.SetLeft(label, 4);
                Canvas.SetTop(label, Math.Max(0, y - 10));
                Panel.SetZIndex(label, 5);
                ChartCanvas.Children.Add(label);
            }

            for (var i = 1; i < verticalSegments; i++)
            {
                var x = width * i / verticalSegments;
                ChartCanvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = gridBrush,
                    StrokeDashArray = new DoubleCollection { 2, 6 },
                    StrokeThickness = 1
                });
            }

            ChartCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = height,
                Y2 = height,
                Stroke = gridBrush,
                StrokeThickness = 1
            });
        }
    }
}
