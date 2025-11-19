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

        public ThermalChart()
        {
            InitializeComponent();
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

            var cpuPolyline = new Polyline { Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            var gpuPolyline = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(31, 195, 255)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 2, 2 } };

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
    }
}
