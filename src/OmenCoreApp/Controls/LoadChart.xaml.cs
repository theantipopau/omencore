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

        public LoadChart()
        {
            InitializeComponent();
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

        private void SamplesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();

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

            var cpuLine = new Polyline
            {
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            var gpuLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(31, 195, 255)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };

            for (var i = 0; i < snapshot.Count; i++)
            {
                var x = width * i / Math.Max(1, snapshot.Count - 1);
                var cpuY = height - (snapshot[i].CpuLoadPercent / 100d) * height;
                var gpuY = height - (snapshot[i].GpuLoadPercent / 100d) * height;
                cpuLine.Points.Add(new Point(x, cpuY));
                gpuLine.Points.Add(new Point(x, gpuY));
            }

            ChartCanvas.Children.Clear();
            ChartCanvas.Children.Add(cpuLine);
            ChartCanvas.Children.Add(gpuLine);
        }

        private void ChartCanvasOnSizeChanged(object sender, SizeChangedEventArgs e) => Render();
    }
}
