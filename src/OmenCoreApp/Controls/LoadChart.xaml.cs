using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OmenCore.Models;

namespace OmenCore.Controls
{
    public partial class LoadChart : UserControl
    {
        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IEnumerable<MonitoringSample>), typeof(LoadChart),
            new PropertyMetadata(null, OnSamplesChanged));

        public IEnumerable<MonitoringSample>? Samples
        {
            get => (IEnumerable<MonitoringSample>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        private double _dpiScale = 1.0;
        private DateTime _lastRender = DateTime.MinValue;
        private const int RenderThrottleMs = 100; // Throttle to 10 FPS max
        private bool _renderPending;

        public LoadChart()
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
            if (d is LoadChart chart)
            {
                if (e.OldValue is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= chart.SamplesChanged;
                }
                if (e.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += chart.SamplesChanged;
                }
                chart.Render();
            }
        }

        private void SamplesChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
                        Render();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                return;
            }
            
            _lastRender = now;
            Render();
        }

        private void Render()
        {
            if (ChartCanvas == null)
            {
                return;
            }

            var snapshot = Samples?.ToList() ?? new List<MonitoringSample>();
            if (snapshot.Count < 2)
            {
                ChartCanvas.Children.Clear();
                return;
            }

            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                ChartCanvas.SizeChanged -= ChartCanvasOnSizeChanged;
                ChartCanvas.SizeChanged += ChartCanvasOnSizeChanged;
                return;
            }

            ChartCanvas.Children.Clear();
            DrawGridlines(width, height);

            // Apply DPI-aware stroke thickness
            var strokeThickness = Math.Max(2.0, 1.5 * _dpiScale);

            var cpuLine = new Polyline
            {
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = strokeThickness,
                StrokeLineJoin = PenLineJoin.Round,
                CacheMode = new BitmapCache() // Enable visual caching for better performance
            };
            var gpuLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(31, 195, 255)),
                StrokeThickness = strokeThickness,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                CacheMode = new BitmapCache()
            };

            for (var i = 0; i < snapshot.Count; i++)
            {
                var x = width * i / Math.Max(1, snapshot.Count - 1);
                var cpuY = height - (snapshot[i].CpuLoadPercent / 100d) * height;
                var gpuY = height - (snapshot[i].GpuLoadPercent / 100d) * height;
                cpuLine.Points.Add(new Point(x, cpuY));
                gpuLine.Points.Add(new Point(x, gpuY));
            }

            ChartCanvas.Children.Add(cpuLine);
            ChartCanvas.Children.Add(gpuLine);
        }

        private void ChartCanvasOnSizeChanged(object sender, SizeChangedEventArgs e) => Render();

        private void DrawGridlines(double width, double height)
        {
            const int horizontalSegments = 4;
            const int verticalSegments = 6;

            var baseBrush = (TryFindResource("BorderBrush") as SolidColorBrush)?.Clone() ?? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            baseBrush.Opacity = 0.25;

            for (var i = 1; i < horizontalSegments; i++)
            {
                var y = height * i / horizontalSegments;
                ChartCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = baseBrush,
                    StrokeDashArray = new DoubleCollection { 2, 6 },
                    StrokeThickness = 1
                });
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
                    Stroke = baseBrush,
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
                Stroke = baseBrush,
                StrokeThickness = 1
            });
        }
    }
}
