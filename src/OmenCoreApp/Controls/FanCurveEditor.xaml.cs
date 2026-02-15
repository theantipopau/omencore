using OmenCore.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OmenCore.Controls
{
    /// <summary>
    /// Visual fan curve editor with draggable points.
    /// Allows users to set fan speed percentages at different temperature thresholds.
    /// </summary>
    public partial class FanCurveEditor : UserControl
    {
        #region Constants
        
        // Temperature range (X-axis)
        private const int MinTemperature = 30;
        private const int MaxTemperature = 100;
        
        // Fan speed range (Y-axis)  
        private const int MinFanPercent = 0;
        private const int MaxFanPercent = 100;
        
        // Grid line settings
        private const int TempGridStep = 10;    // Every 10°C
        private const int FanGridStep = 20;     // Every 20%
        
        // Visual settings - increased for easier touch/drag
        private const double PointRadius = 10;
        private const double PointHoverRadius = 13;
        private const double LineThickness = 3.0;
        private const double GridLineThickness = 1;
        
        #endregion

        #region Dependency Properties
        
        public static readonly DependencyProperty CurvePointsProperty = DependencyProperty.Register(
            nameof(CurvePoints),
            typeof(ObservableCollection<FanCurvePoint>),
            typeof(FanCurveEditor),
            new FrameworkPropertyMetadata(null, OnCurvePointsChanged));

        public ObservableCollection<FanCurvePoint>? CurvePoints
        {
            get => (ObservableCollection<FanCurvePoint>?)GetValue(CurvePointsProperty);
            set => SetValue(CurvePointsProperty, value);
        }
        
        public static readonly DependencyProperty CurrentTemperatureProperty = DependencyProperty.Register(
            nameof(CurrentTemperature),
            typeof(double),
            typeof(FanCurveEditor),
            new PropertyMetadata(0.0, OnCurrentTemperatureChanged));

        /// <summary>
        /// Current CPU/GPU temperature - shown as a vertical indicator line.
        /// </summary>
        public double CurrentTemperature
        {
            get => (double)GetValue(CurrentTemperatureProperty);
            set => SetValue(CurrentTemperatureProperty, value);
        }
        
        #endregion

        #region Private Fields
        
        private Ellipse? _draggedPoint;
        private int _draggedPointIndex = -1;
        private bool _isDragging;
        private Point _dragStartOffset;
        private readonly List<Ellipse> _pointEllipses = new();
        private Line? _currentTempLine;
        private bool _suppressRender;  // Prevents re-render during programmatic updates
        private long _lastDragRenderTicks;
        private const int MinDragRenderIntervalMs = 33;
        private const int DragTemperatureSnapStep = 2;
        private const int DragFanPercentSnapStep = 2;
        
        // Drag instrumentation for performance profiling
        private long _dragStartTicks;
        private int _dragFrameCount;
        private long _dragTotalRenderUs;
        private long _dragMaxRenderUs;
        
        #endregion

        #region Constructor
        
        public FanCurveEditor()
        {
            InitializeComponent();
            Loaded += (s, e) => RenderCurve();
            
            // Handle mouse release when cursor leaves the chart area or releases outside a point
            ChartCanvas.MouseLeave += ChartCanvas_MouseLeave;
            ChartCanvas.MouseLeftButtonUp += ChartCanvas_MouseLeftButtonUp;
            ChartCanvas.LostMouseCapture += ChartCanvas_LostMouseCapture;
        }
        
        #endregion
        
        #region Global Mouse Handlers
        
        private void ChartCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // Release drag when mouse leaves chart area
            if (_isDragging)
            {
                ReleaseDrag();
            }
        }
        
        private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Release drag on any mouse up in the canvas
            if (_isDragging)
            {
                ReleaseDrag();
            }
        }
        
        private void ChartCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // Release drag if mouse capture is lost
            if (_isDragging)
            {
                ReleaseDrag();
            }
        }
        
        private void ReleaseDrag()
        {
            // Capture drag instrumentation before resetting state
            long dragFrames = _dragFrameCount;
            long dragTotalUs = _dragTotalRenderUs;
            long dragMaxUs = _dragMaxRenderUs;
            long dragStartTicks = _dragStartTicks;

            if (_draggedPoint != null)
            {
                _draggedPoint.ReleaseMouseCapture();
            }
            _isDragging = false;
            _draggedPoint = null;
            _draggedPointIndex = -1;
            
            HideTooltip();
            
            // Sort points by temperature after drag
            if (CurvePoints != null)
            {
                _suppressRender = true;
                try
                {
                    var sorted = CurvePoints.OrderBy(p => p.TemperatureC).ToList();
                    CurvePoints.Clear();
                    foreach (var p in sorted)
                    {
                        CurvePoints.Add(p);
                    }
                }
                finally
                {
                    _suppressRender = false;
                }
                RenderCurve();
                ValidateCurve();
            }

            // Log drag performance summary
            if (dragFrames > 0)
            {
                double avgUs = (double)dragTotalUs / dragFrames;
                double durationMs = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - dragStartTicks)
                                    / System.Diagnostics.Stopwatch.Frequency * 1000.0;
                System.Diagnostics.Debug.WriteLine(
                    $"[FanCurveEditor] Drag complete: {dragFrames} frames in {durationMs:F0}ms, " +
                    $"render avg={avgUs:F0}µs max={dragMaxUs}µs");
            }
        }
        
        #endregion

        #region Property Changed Handlers
        
        private static void OnCurvePointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FanCurveEditor editor)
            {
                // Unsubscribe from old collection
                if (e.OldValue is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= editor.CurvePoints_CollectionChanged;
                }
                
                // Subscribe to new collection
                if (e.NewValue is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += editor.CurvePoints_CollectionChanged;
                }
                
                editor.RenderCurve();
            }
        }
        
        private static void OnCurrentTemperatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FanCurveEditor editor)
            {
                editor.UpdateCurrentTempIndicator();
            }
        }
        
        private void CurvePoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RenderCurve();
            ValidateCurve(); // Validate after any change
        }
        
        #endregion

        #region Curve Validation
        
        /// <summary>
        /// Validate curve for safety issues and show warnings
        /// </summary>
        private void ValidateCurve()
        {
            if (CurvePoints == null || CurvePoints.Count < 2)
            {
                HideValidationWarning();
                return;
            }

            var sorted = CurvePoints.OrderBy(p => p.TemperatureC).ToList();
            
            // Check for dangerous inverted curves (fan speed decreases as temperature increases)
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var tempDiff = sorted[i + 1].TemperatureC - sorted[i].TemperatureC;
                var fanDiff = sorted[i + 1].FanPercent - sorted[i].FanPercent;
                
                // If temperature increases by 10°C+ but fan speed decreases by 15%+, that's dangerous
                if (tempDiff >= 10 && fanDiff <= -15)
                {
                    ShowValidationWarning($"Dangerous curve: At {sorted[i+1].TemperatureC}°C the fan speed drops to {sorted[i+1].FanPercent}% (was {sorted[i].FanPercent}% at {sorted[i].TemperatureC}°C). This could cause overheating!");
                    return;
                }
            }
            
            // Check if high temperature (85°C+) has low fan speed (< 50%)
            var highTempPoints = sorted.Where(p => p.TemperatureC >= 85).ToList();
            if (highTempPoints.Any(p => p.FanPercent < 50))
            {
                var problematic = highTempPoints.First(p => p.FanPercent < 50);
                ShowValidationWarning($"Safety warning: {problematic.TemperatureC}°C has only {problematic.FanPercent}% fan speed. Recommend at least 70% at high temperatures to prevent throttling.");
                return;
            }
            
            // Check if curve doesn't cover high temps (< 85°C)
            if (sorted[^1].TemperatureC < 85)
            {
                ShowValidationWarning($"Incomplete coverage: Curve only extends to {sorted[^1].TemperatureC}°C. Add points up to 95°C to protect against thermal spikes.");
                return;
            }
            
            HideValidationWarning();
        }
        
        private void ShowValidationWarning(string message)
        {
            ValidationWarningText.Text = message;
            ValidationWarning.Visibility = Visibility.Visible;
        }
        
        private void HideValidationWarning()
        {
            ValidationWarning.Visibility = Visibility.Collapsed;
        }
        
        #endregion

        #region Coordinate Conversion
        
        private Point DataToCanvas(int temperature, int fanPercent)
        {
            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;
            
            // Map temperature to X coordinate
            var x = (temperature - MinTemperature) / (double)(MaxTemperature - MinTemperature) * width;
            
            // Map fan percent to Y coordinate (inverted - 100% at top)
            var y = height - (fanPercent - MinFanPercent) / (double)(MaxFanPercent - MinFanPercent) * height;
            
            return new Point(x, y);
        }
        
        private (int temperature, int fanPercent) CanvasToData(Point canvasPoint)
        {
            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;
            
            // Map X to temperature
            var temperature = (int)Math.Round(canvasPoint.X / width * (MaxTemperature - MinTemperature) + MinTemperature);
            temperature = Math.Clamp(temperature, MinTemperature, MaxTemperature);
            
            // Map Y to fan percent (inverted)
            var fanPercent = (int)Math.Round((1 - canvasPoint.Y / height) * (MaxFanPercent - MinFanPercent) + MinFanPercent);
            fanPercent = Math.Clamp(fanPercent, MinFanPercent, MaxFanPercent);
            
            return (temperature, fanPercent);
        }
        
        #endregion

        #region Rendering
        
        private void RenderCurve()
        {
            // Don't re-render during programmatic updates (prevents slider jitter)
            if (_suppressRender) return;
            
            ChartCanvas.Children.Clear();
            YAxisLabels.Children.Clear();
            XAxisLabels.Children.Clear();
            _pointEllipses.Clear();
            _currentTempLine = null;
            
            var width = ChartCanvas.ActualWidth;
            var height = ChartCanvas.ActualHeight;
            
            if (width <= 0 || height <= 0)
            {
                return;
            }
            
            // Draw grid lines first (behind everything)
            DrawGridLines(width, height);
            
            // Draw axes labels
            DrawAxisLabels(width, height);
            
            var points = CurvePoints?.OrderBy(p => p.TemperatureC).ToList();
            
            if (points == null || points.Count == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }
            
            EmptyStateText.Visibility = Visibility.Collapsed;
            
            // Draw the fan curve line
            DrawCurveLine(points, width, height);
            
            // Draw the fill area under the curve
            DrawCurveFill(points, width, height);
            
            // Draw current temperature indicator
            DrawCurrentTempIndicator(width, height);
            
            // Draw draggable points (on top)
            DrawCurvePoints(points);
        }
        
        private void DrawGridLines(double width, double height)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            
            // Horizontal grid lines (fan speed)
            for (int fan = MinFanPercent; fan <= MaxFanPercent; fan += FanGridStep)
            {
                var y = height - (fan / (double)MaxFanPercent) * height;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = GridLineThickness,
                    StrokeDashArray = new DoubleCollection { 2, 4 }
                };
                ChartCanvas.Children.Add(line);
            }
            
            // Vertical grid lines (temperature)
            for (int temp = MinTemperature; temp <= MaxTemperature; temp += TempGridStep)
            {
                var x = (temp - MinTemperature) / (double)(MaxTemperature - MinTemperature) * width;
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = gridBrush,
                    StrokeThickness = GridLineThickness,
                    StrokeDashArray = new DoubleCollection { 2, 4 }
                };
                ChartCanvas.Children.Add(line);
            }
        }
        
        private void DrawAxisLabels(double width, double height)
        {
            var labelBrush = (Brush)FindResource("TextTertiaryBrush");
            
            // Y-axis labels (fan %)
            for (int fan = MinFanPercent; fan <= MaxFanPercent; fan += FanGridStep)
            {
                var y = height - (fan / (double)MaxFanPercent) * height;
                var label = new TextBlock
                {
                    Text = $"{fan}%",
                    Foreground = labelBrush,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 30,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 7);
                YAxisLabels.Children.Add(label);
            }
            
            // X-axis labels (temperature)
            for (int temp = MinTemperature; temp <= MaxTemperature; temp += TempGridStep)
            {
                var x = (temp - MinTemperature) / (double)(MaxTemperature - MinTemperature) * width;
                var label = new TextBlock
                {
                    Text = $"{temp}°",
                    Foreground = labelBrush,
                    FontSize = 10
                };
                Canvas.SetLeft(label, x - 12);
                Canvas.SetTop(label, 5);
                XAxisLabels.Children.Add(label);
            }
        }
        
        private void DrawCurveLine(List<FanCurvePoint> points, double width, double height)
        {
            if (points.Count < 2) return;
            
            var accentBrush = (Brush)FindResource("AccentBrush");
            var polyline = new Polyline
            {
                Stroke = accentBrush,
                StrokeThickness = LineThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            
            // Extend line to left edge at first point's fan %
            var firstPoint = DataToCanvas(MinTemperature, points[0].FanPercent);
            polyline.Points.Add(firstPoint);
            
            foreach (var point in points)
            {
                var canvasPoint = DataToCanvas(point.TemperatureC, point.FanPercent);
                polyline.Points.Add(canvasPoint);
            }
            
            // Extend line to right edge at last point's fan %
            var lastPoint = DataToCanvas(MaxTemperature, points[^1].FanPercent);
            polyline.Points.Add(lastPoint);
            
            ChartCanvas.Children.Add(polyline);
        }
        
        private void DrawCurveFill(List<FanCurvePoint> points, double width, double height)
        {
            if (points.Count < 1) return;
            
            // Create gradient fill under the curve
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 0, 92), 0));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(10, 255, 0, 92), 1));
            
            var polygon = new Polygon
            {
                Fill = gradientBrush,
                Stroke = null
            };
            
            // Start at bottom-left
            polygon.Points.Add(new Point(0, height));
            
            // Add first point extending to left edge
            polygon.Points.Add(new Point(0, DataToCanvas(MinTemperature, points[0].FanPercent).Y));
            
            // Add all curve points
            foreach (var point in points)
            {
                var canvasPoint = DataToCanvas(point.TemperatureC, point.FanPercent);
                polygon.Points.Add(canvasPoint);
            }
            
            // Extend to right edge at last point's level
            var lastY = DataToCanvas(MaxTemperature, points[^1].FanPercent).Y;
            polygon.Points.Add(new Point(width, lastY));
            
            // Close at bottom-right
            polygon.Points.Add(new Point(width, height));
            
            ChartCanvas.Children.Add(polygon);
        }
        
        private void DrawCurrentTempIndicator(double width, double height)
        {
            if (CurrentTemperature < MinTemperature || CurrentTemperature > MaxTemperature)
                return;
                
            var x = (CurrentTemperature - MinTemperature) / (double)(MaxTemperature - MinTemperature) * width;
            
            // Vertical line at current temperature
            _currentTempLine = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = new SolidColorBrush(Color.FromRgb(31, 195, 255)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Opacity = 0.8
            };
            ChartCanvas.Children.Add(_currentTempLine);
            
            // Temperature label at top
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(31, 195, 255)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Text = $"{CurrentTemperature:F0}°C",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                }
            };
            Canvas.SetLeft(label, x - 18);
            Canvas.SetTop(label, -5);
            ChartCanvas.Children.Add(label);
        }
        
        private void UpdateCurrentTempIndicator()
        {
            // Re-render to update indicator position
            RenderCurve();
        }
        
        private void DrawCurvePoints(List<FanCurvePoint> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var canvasPoint = DataToCanvas(point.TemperatureC, point.FanPercent);
                
                var ellipse = new Ellipse
                {
                    Width = PointRadius * 2,
                    Height = PointRadius * 2,
                    Fill = (Brush)FindResource("AccentBrush"),
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    Tag = i,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 4,
                        Opacity = 0.4,
                        ShadowDepth = 1
                    }
                };
                
                Canvas.SetLeft(ellipse, canvasPoint.X - PointRadius);
                Canvas.SetTop(ellipse, canvasPoint.Y - PointRadius);
                
                ellipse.MouseLeftButtonDown += Point_MouseLeftButtonDown;
                ellipse.MouseMove += Point_MouseMove;
                ellipse.MouseLeftButtonUp += Point_MouseLeftButtonUp;
                ellipse.MouseEnter += Point_MouseEnter;
                ellipse.MouseLeave += Point_MouseLeave;
                ellipse.MouseRightButtonDown += Point_RightClick;
                
                ChartCanvas.Children.Add(ellipse);
                _pointEllipses.Add(ellipse);
            }
        }
        
        #endregion

        #region Mouse Event Handlers
        
        private void ChartCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Add new point when clicking on empty area
            if (_isDragging || CurvePoints == null) return;
            
            var pos = e.GetPosition(ChartCanvas);
            var (temp, fan) = CanvasToData(pos);
            
            // Snap temperature to nearest 5 degrees for cleaner values
            temp = (int)Math.Round(temp / 5.0) * 5;
            temp = Math.Clamp(temp, MinTemperature, MaxTemperature);
            
            // Check if we're too close to an existing point
            foreach (var existing in CurvePoints)
            {
                if (Math.Abs(existing.TemperatureC - temp) < 5)
                {
                    return; // Too close to existing point
                }
            }
            
            // Suppress render during batch update to prevent visual jitter
            _suppressRender = true;
            try
            {
                CurvePoints.Add(new FanCurvePoint { TemperatureC = temp, FanPercent = fan });
                
                // Sort by temperature
                var sorted = CurvePoints.OrderBy(p => p.TemperatureC).ToList();
                CurvePoints.Clear();
                foreach (var p in sorted)
                {
                    CurvePoints.Add(p);
                }
            }
            finally
            {
                _suppressRender = false;
            }
            
            // Single render after batch update is complete
            RenderCurve();
        }
        
        private void Point_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse ellipse && ellipse.Tag is int index)
            {
                _isDragging = true;
                _draggedPoint = ellipse;
                _draggedPointIndex = index;
                _dragStartTicks = DateTime.UtcNow.Ticks;
                _dragFrameCount = 0;
                _dragTotalRenderUs = 0;
                _dragMaxRenderUs = 0;
                
                var mousePos = e.GetPosition(ChartCanvas);
                var ellipsePos = new Point(Canvas.GetLeft(ellipse) + PointRadius, Canvas.GetTop(ellipse) + PointRadius);
                _dragStartOffset = new Point(mousePos.X - ellipsePos.X, mousePos.Y - ellipsePos.Y);
                
                ellipse.CaptureMouse();
                e.Handled = true;
                
                UpdateTooltip(ellipse, true);
            }
        }
        
        private void Point_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggedPoint == null || CurvePoints == null) return;
            
            var pos = e.GetPosition(ChartCanvas);
            var adjustedPos = new Point(pos.X - _dragStartOffset.X, pos.Y - _dragStartOffset.Y);
            
            var (temp, fan) = CanvasToData(adjustedPos);
            
            // Get the sorted points to determine valid temperature range
            var sortedPoints = CurvePoints.OrderBy(p => p.TemperatureC).ToList();
            var currentIndex = sortedPoints.FindIndex(p => 
                p.TemperatureC == CurvePoints[_draggedPointIndex].TemperatureC && 
                p.FanPercent == CurvePoints[_draggedPointIndex].FanPercent);
            
            // Constrain temperature between neighbors (prevent crossing)
            int minTemp = MinTemperature;
            int maxTemp = MaxTemperature;
            
            if (currentIndex > 0)
            {
                minTemp = sortedPoints[currentIndex - 1].TemperatureC + 5;
            }
            if (currentIndex < sortedPoints.Count - 1)
            {
                maxTemp = sortedPoints[currentIndex + 1].TemperatureC - 5;
            }
            
            // Ensure minTemp doesn't exceed maxTemp (edge case when points are too close)
            if (minTemp > maxTemp)
            {
                // Keep the point at its current position if constraints conflict
                minTemp = CurvePoints[_draggedPointIndex].TemperatureC;
                maxTemp = CurvePoints[_draggedPointIndex].TemperatureC;
            }
            
            temp = Math.Clamp(temp, minTemp, maxTemp);

            // Quantize drag updates to reduce high-frequency UI churn while still feeling smooth.
            temp = (int)Math.Round(temp / (double)DragTemperatureSnapStep) * DragTemperatureSnapStep;
            fan = (int)Math.Round(fan / (double)DragFanPercentSnapStep) * DragFanPercentSnapStep;

            var currentPoint = CurvePoints[_draggedPointIndex];
            if (currentPoint.TemperatureC == temp && currentPoint.FanPercent == fan)
            {
                return;
            }
            
            // Update the point
            currentPoint.TemperatureC = temp;
            currentPoint.FanPercent = fan;
            
            // Update visual position
            var newCanvasPoint = DataToCanvas(temp, fan);
            Canvas.SetLeft(_draggedPoint, newCanvasPoint.X - PointRadius);
            Canvas.SetTop(_draggedPoint, newCanvasPoint.Y - PointRadius);
            
            UpdateTooltip(_draggedPoint, true);
            
            var nowTicks = DateTime.UtcNow.Ticks;
            var elapsedMs = (nowTicks - _lastDragRenderTicks) / TimeSpan.TicksPerMillisecond;
            if (elapsedMs >= MinDragRenderIntervalMs)
            {
                // Re-render throttled to avoid lag from full-canvas redraw on every mouse event.
                var renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
                RenderCurve();
                var renderEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                var renderUs = (renderEnd - renderStart) * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
                
                _dragFrameCount++;
                _dragTotalRenderUs += renderUs;
                if (renderUs > _dragMaxRenderUs) _dragMaxRenderUs = renderUs;
                
                _lastDragRenderTicks = nowTicks;
            }
        }
        
        private void Point_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                ReleaseDrag();
                e.Handled = true;
            }
        }
        
        private void Point_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Ellipse ellipse && !_isDragging)
            {
                ellipse.Width = PointHoverRadius * 2;
                ellipse.Height = PointHoverRadius * 2;
                
                // Adjust position to keep centered
                var left = Canvas.GetLeft(ellipse);
                var top = Canvas.GetTop(ellipse);
                Canvas.SetLeft(ellipse, left - (PointHoverRadius - PointRadius));
                Canvas.SetTop(ellipse, top - (PointHoverRadius - PointRadius));
                
                ellipse.Fill = (Brush)FindResource("AccentBlueBrush");
                
                UpdateTooltip(ellipse, true);
            }
        }
        
        private void Point_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Ellipse ellipse && !_isDragging)
            {
                ellipse.Width = PointRadius * 2;
                ellipse.Height = PointRadius * 2;
                
                // Adjust position back
                var left = Canvas.GetLeft(ellipse);
                var top = Canvas.GetTop(ellipse);
                Canvas.SetLeft(ellipse, left + (PointHoverRadius - PointRadius));
                Canvas.SetTop(ellipse, top + (PointHoverRadius - PointRadius));
                
                ellipse.Fill = (Brush)FindResource("AccentBrush");
                
                HideTooltip();
            }
        }
        
        private void Point_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Delete point on right-click
            if (sender is Ellipse ellipse && ellipse.Tag is int index && CurvePoints != null)
            {
                if (CurvePoints.Count > 2) // Keep at least 2 points
                {
                    CurvePoints.RemoveAt(index);
                }
                e.Handled = true;
            }
        }
        
        #endregion

        #region Tooltip
        
        private void UpdateTooltip(Ellipse ellipse, bool show)
        {
            if (ellipse.Tag is int index && CurvePoints != null && index < CurvePoints.Count)
            {
                var point = CurvePoints[index];
                PointTooltipText.Text = $"{point.TemperatureC}°C → {point.FanPercent}%";
                
                var canvasPos = DataToCanvas(point.TemperatureC, point.FanPercent);
                PointTooltip.Margin = new Thickness(canvasPos.X + 55, canvasPos.Y + 5, 0, 0);
                PointTooltip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        
        private void HideTooltip()
        {
            PointTooltip.Visibility = Visibility.Collapsed;
        }
        
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Add a new curve point at the specified temperature and fan percentage.
        /// </summary>
        public void AddPoint(int temperature, int fanPercent)
        {
            if (CurvePoints == null) return;
            
            temperature = Math.Clamp(temperature, MinTemperature, MaxTemperature);
            fanPercent = Math.Clamp(fanPercent, MinFanPercent, MaxFanPercent);
            
            // Check for existing point at this temperature
            var existing = CurvePoints.FirstOrDefault(p => p.TemperatureC == temperature);
            if (existing != null)
            {
                existing.FanPercent = fanPercent;
            }
            else
            {
                CurvePoints.Add(new FanCurvePoint { TemperatureC = temperature, FanPercent = fanPercent });
            }
            
            // Sort by temperature
            var sorted = CurvePoints.OrderBy(p => p.TemperatureC).ToList();
            CurvePoints.Clear();
            foreach (var p in sorted)
            {
                CurvePoints.Add(p);
            }
        }
        
        /// <summary>
        /// Remove the curve point at the specified temperature.
        /// </summary>
        public void RemovePointAt(int temperature)
        {
            if (CurvePoints == null || CurvePoints.Count <= 2) return; // Keep at least 2 points
            
            var point = CurvePoints.FirstOrDefault(p => p.TemperatureC == temperature);
            if (point != null)
            {
                CurvePoints.Remove(point);
            }
        }
        
        /// <summary>
        /// Remove the last added curve point.
        /// </summary>
        public void RemoveLastPoint()
        {
            if (CurvePoints == null || CurvePoints.Count <= 2) return; // Keep at least 2 points
            CurvePoints.RemoveAt(CurvePoints.Count - 1);
        }
        
        #endregion

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderCurve();
        }
    }
}
