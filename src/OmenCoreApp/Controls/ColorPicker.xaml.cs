using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OmenCore.Controls
{
    /// <summary>
    /// A simple color picker control with hue slider and saturation/brightness area.
    /// Built entirely in code to avoid XAML designer issues.
    /// </summary>
    public class ColorPickerControl : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPickerControl),
                new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

        public static readonly DependencyProperty HexValueProperty =
            DependencyProperty.Register(nameof(HexValue), typeof(string), typeof(ColorPickerControl),
                new FrameworkPropertyMetadata("#FF0000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexValueChanged));

        private double _hue = 0;
        private double _saturation = 1;
        private double _brightness = 1;
        private bool _isUpdating;
        
        private readonly Rectangle _colorPreview;
        private readonly Slider _hueSlider;
        private readonly Slider _saturationSlider;
        private readonly Slider _brightnessSlider;

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public string HexValue
        {
            get => (string)GetValue(HexValueProperty);
            set => SetValue(HexValueProperty, value);
        }

        public event EventHandler<Color>? ColorChanged;

        public ColorPickerControl()
        {
            // Build the UI in code
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Color preview
            var previewBorder = new Border
            {
                Width = 70,
                Height = 70,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 16, 0)
            };
            previewBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            
            _colorPreview = new Rectangle { RadiusX = 6, RadiusY = 6, Fill = Brushes.Red };
            previewBorder.Child = _colorPreview;
            Grid.SetColumn(previewBorder, 0);
            grid.Children.Add(previewBorder);

            // Sliders panel
            var slidersPanel = new StackPanel();
            Grid.SetColumn(slidersPanel, 1);

            // Hue slider
            var hueLabel = new TextBlock { Text = "Hue", FontSize = 11, Margin = new Thickness(0, 0, 0, 2) };
            hueLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            slidersPanel.Children.Add(hueLabel);

            _hueSlider = new Slider { Minimum = 0, Maximum = 359, Value = 0 };
            _hueSlider.Background = CreateHueGradient();
            _hueSlider.ValueChanged += OnHueSliderChanged;
            slidersPanel.Children.Add(_hueSlider);

            // Saturation slider
            var satLabel = new TextBlock { Text = "Saturation", FontSize = 11, Margin = new Thickness(0, 8, 0, 2) };
            satLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            slidersPanel.Children.Add(satLabel);

            _saturationSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100 };
            _saturationSlider.ValueChanged += OnSaturationSliderChanged;
            slidersPanel.Children.Add(_saturationSlider);

            // Brightness slider
            var brightLabel = new TextBlock { Text = "Brightness", FontSize = 11, Margin = new Thickness(0, 8, 0, 2) };
            brightLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            slidersPanel.Children.Add(brightLabel);

            _brightnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100 };
            _brightnessSlider.ValueChanged += OnBrightnessSliderChanged;
            slidersPanel.Children.Add(_brightnessSlider);

            // Quick color buttons
            var quickColors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            string[] colors = { "#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00FFFF", "#0080FF", "#FF00FF", "#FFFFFF" };
            foreach (var hex in colors)
            {
                var btn = new Button
                {
                    Width = 22, Height = 22,
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(0),
                    Tag = hex,
                    Content = new Rectangle
                    {
                        Width = 18, Height = 18,
                        RadiusX = 2, RadiusY = 2,
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                    }
                };
                btn.Click += OnQuickColorClick;
                quickColors.Children.Add(btn);
            }
            slidersPanel.Children.Add(quickColors);

            grid.Children.Add(slidersPanel);
            Content = grid;
            
            UpdatePreview();
        }

        private static LinearGradientBrush CreateHueGradient()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 0), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 0), 0.167));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 0), 0.333));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 255), 0.667));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 255), 0.833));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 0, 0), 1));
            brush.Freeze();
            return brush;
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerControl picker && !picker._isUpdating)
            {
                picker._isUpdating = true;
                picker.ColorToHsv((Color)e.NewValue, out picker._hue, out picker._saturation, out picker._brightness);
                picker.UpdateSliders();
                picker.UpdatePreview();
                picker.HexValue = ColorToHex((Color)e.NewValue);
                picker._isUpdating = false;
            }
        }

        private static void OnHexValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerControl picker && !picker._isUpdating)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString((string)e.NewValue);
                    picker.SelectedColor = color;
                }
                catch
                {
                    // Invalid hex value, ignore
                }
            }
        }

        private void OnHueSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            _hue = e.NewValue;
            UpdateColorFromHsv();
        }

        private void OnSaturationSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            _saturation = e.NewValue / 100.0;
            UpdateColorFromHsv();
        }

        private void OnBrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            _brightness = e.NewValue / 100.0;
            UpdateColorFromHsv();
        }

        private void UpdateColorFromHsv()
        {
            _isUpdating = true;
            SelectedColor = HsvToColor(_hue, _saturation, _brightness);
            HexValue = ColorToHex(SelectedColor);
            UpdatePreview();
            ColorChanged?.Invoke(this, SelectedColor);
            _isUpdating = false;
        }

        private void UpdateSliders()
        {
            _hueSlider.Value = _hue;
            _saturationSlider.Value = _saturation * 100;
            _brightnessSlider.Value = _brightness * 100;
        }

        private void UpdatePreview()
        {
            _colorPreview.Fill = new SolidColorBrush(SelectedColor);
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            h = h % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        private void ColorToHsv(Color color, out double h, out double s, out double v)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0)
                h = 0;
            else if (max == r)
                h = 60 * ((g - b) / delta % 6);
            else if (max == g)
                h = 60 * ((b - r) / delta + 2);
            else
                h = 60 * ((r - g) / delta + 4);

            if (h < 0) h += 360;
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private void OnQuickColorClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                HexValue = hex;
            }
        }
    }
}
