using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OmenCore.Models;

namespace OmenCore.Controls
{
    public partial class FanCurveChart : UserControl
    {
        private ObservableCollection<FanCurvePoint>? _points;

        // DashboardViewModel.UpdateFanCurvePoints() adds/removes one point at a time on
        // essentially every telemetry sample (>=1/sec), and RebuildHistoricalTelemetryBuffers()
        // replays up to 50 samples one at a time on every Dashboard tab switch - each of those
        // is a separate CollectionChanged event, and OnPointsChanged previously called
        // UpdateChart() (a full ChartCanvas.Children.Clear() + rebuild) for every single one.
        // Same throttle-and-coalesce pattern already used by LoadChart/GpuVcChart.
        private DateTime _lastRender = DateTime.MinValue;
        private const int RenderThrottleMs = 100;
        private bool _renderPending;

        public FanCurveChart()
        {
            InitializeComponent();
        }

        public ObservableCollection<FanCurvePoint>? Points
        {
            get => _points;
            set
            {
                if (_points != null)
                {
                    _points.CollectionChanged -= OnPointsChanged;
                }

                _points = value;

                if (_points != null)
                {
                    _points.CollectionChanged += OnPointsChanged;
                }

                UpdateChart();
            }
        }

        private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
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
                        _lastRender = DateTime.UtcNow;
                        UpdateChart();
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                return;
            }

            _lastRender = now;
            UpdateChart();
        }

        private void UpdateChart()
        {
            ChartCanvas.Children.Clear();

            if (_points == null || _points.Count == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            EmptyStateText.Visibility = Visibility.Collapsed;

            // Get data range
            double minTemp = _points.Min(p => p.TemperatureC);
            double maxTemp = _points.Max(p => p.TemperatureC);
            double minSpeed = _points.Min(p => p.FanSpeedRpm);
            double maxSpeed = _points.Max(p => p.FanSpeedRpm);

            // Add some padding
            var tempRange = Math.Max(maxTemp - minTemp, 10);
            var speedRange = Math.Max(maxSpeed - minSpeed, 500);

            minTemp = Math.Max(0, minTemp - tempRange * 0.1);
            maxTemp += tempRange * 0.1;
            minSpeed = Math.Max(0, minSpeed - speedRange * 0.1);
            maxSpeed += speedRange * 0.1;

            // Draw grid lines
            DrawGrid(minTemp, maxTemp, minSpeed, maxSpeed);

            // Draw data points and line
            DrawDataPoints(minTemp, maxTemp, minSpeed, maxSpeed);
        }

        private void DrawGrid(double minTemp, double maxTemp, double minSpeed, double maxSpeed)
        {
            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Horizontal grid lines (temperature)
            for (int i = 0; i <= 5; i++)
            {
                var temp = minTemp + (maxTemp - minTemp) * i / 5;
                var y = height - (temp - minTemp) / (maxTemp - minTemp) * height;

                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }

            // Vertical grid lines (fan speed)
            for (int i = 0; i <= 5; i++)
            {
                var speed = minSpeed + (maxSpeed - minSpeed) * i / 5;
                var x = (speed - minSpeed) / (maxSpeed - minSpeed) * width;

                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }
        }

        private void DrawDataPoints(double minTemp, double maxTemp, double minSpeed, double maxSpeed)
        {
            if (_points == null) return;

            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Sort points by temperature for proper line drawing
            var sortedPoints = _points.OrderBy(p => p.TemperatureC).ToList();

            Point? previousPoint = null;

            foreach (var point in sortedPoints)
            {
                var x = (int)(point.FanSpeedRpm - minSpeed) / (maxSpeed - minSpeed) * width;
                var y = (int)(height - (point.TemperatureC - minTemp) / (maxTemp - minTemp) * height);

                // Draw line to previous point
                if (previousPoint.HasValue)
                {
                    var line = new Line
                    {
                        X1 = previousPoint.Value.X,
                        Y1 = previousPoint.Value.Y,
                        X2 = x,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromRgb(135, 206, 235)), // Sky blue
                        StrokeThickness = 2
                    };
                    ChartCanvas.Children.Add(line);
                }

                // Draw data point
                var ellipse = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(135, 206, 235)),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1
                };

                Canvas.SetLeft(ellipse, x - 3);
                Canvas.SetTop(ellipse, y - 3);
                ChartCanvas.Children.Add(ellipse);

                previousPoint = new Point(x, y);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateChart();
        }
    }
}