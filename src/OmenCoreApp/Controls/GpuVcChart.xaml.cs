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
    public partial class GpuVcChart : UserControl
    {
        public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
            nameof(Samples), typeof(IEnumerable<MonitoringSample>), typeof(GpuVcChart),
            new PropertyMetadata(null, OnSamplesChanged));

        public IEnumerable<MonitoringSample>? Samples
        {
            get => (IEnumerable<MonitoringSample>?)GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        private double _dpiScale = 1.0;
        private DateTime _lastRender = DateTime.MinValue;
        private const int RenderThrottleMs = 50; // 20 FPS for smooth animation
        private bool _renderPending;
        private Polyline? _voltageLine;
        private Polyline? _currentLine;

        public GpuVcChart()
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
            if (d is GpuVcChart chart)
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
                _voltageLine = null;
                _currentLine = null;
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

            // Only clear and redraw gridlines if canvas was empty
            if (ChartCanvas.Children.Count == 0)
            {
                DrawGridlines(width, height);
            }

            // Apply DPI-aware stroke thickness
            var strokeThickness = Math.Max(2.0, 1.5 * _dpiScale);

            // Reuse polylines for better performance
            if (_voltageLine == null)
            {
                _voltageLine = new Polyline
                {
                    Stroke = (Brush)FindResource("AccentBrush"),
                    StrokeThickness = strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    CacheMode = new BitmapCache()
                };
                ChartCanvas.Children.Add(_voltageLine);
            }
            else
            {
                _voltageLine.StrokeThickness = strokeThickness;
            }

            if (_currentLine == null)
            {
                _currentLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                    StrokeThickness = strokeThickness,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    CacheMode = new BitmapCache()
                };
                ChartCanvas.Children.Add(_currentLine);
            }
            else
            {
                _currentLine.StrokeThickness = strokeThickness;
            }

            // Clear existing points and add new ones
            _voltageLine.Points.Clear();
            _currentLine.Points.Clear();

            // Find voltage/current ranges for scaling
            var voltages = snapshot.Where(s => s.GpuVoltageV > 0).Select(s => s.GpuVoltageV).ToList();
            var currents = snapshot.Where(s => s.GpuCurrentA > 0).Select(s => s.GpuCurrentA).ToList();

            if (voltages.Count == 0 || currents.Count == 0)
            {
                return; // No data to display
            }

            var minVoltage = voltages.Min();
            var maxVoltage = voltages.Max();
            var minCurrent = currents.Min();
            var maxCurrent = currents.Max();

            // Use combined range for Y-axis scaling (voltage in volts, current in amps)
            var voltageRange = maxVoltage - minVoltage;
            var currentRange = maxCurrent - minCurrent;

            for (var i = 0; i < snapshot.Count; i++)
            {
                var x = width * i / Math.Max(1, snapshot.Count - 1);

                // Scale voltage (0-1.5V typical range)
                var voltageY = height - ((snapshot[i].GpuVoltageV - minVoltage) / Math.Max(voltageRange, 0.1)) * height;

                // Scale current (0-50A typical range)
                var currentY = height - ((snapshot[i].GpuCurrentA - minCurrent) / Math.Max(currentRange, 1.0)) * height;

                if (snapshot[i].GpuVoltageV > 0)
                    _voltageLine.Points.Add(new Point(x, voltageY));

                if (snapshot[i].GpuCurrentA > 0)
                    _currentLine.Points.Add(new Point(x, currentY));
            }
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