using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmenCore.Utils
{
    /// <summary>
    /// Converts temperature values to color brushes based on thresholds.
    /// Green (OK) &lt; 60°C, Yellow (Warm) 60-80°C, Orange (Hot) 80-90°C, Red (Critical) &gt; 90°C
    /// </summary>
    public class TemperatureToColorConverter : IValueConverter
    {
        // Color thresholds
        private static readonly Color CoolColor = Color.FromRgb(76, 175, 80);    // Green
        private static readonly Color WarmColor = Color.FromRgb(255, 193, 7);    // Amber
        private static readonly Color HotColor = Color.FromRgb(255, 152, 0);     // Orange
        private static readonly Color CriticalColor = Color.FromRgb(244, 67, 54); // Red

        private static readonly SolidColorBrush CoolBrush = new(CoolColor);
        private static readonly SolidColorBrush WarmBrush = new(WarmColor);
        private static readonly SolidColorBrush HotBrush = new(HotColor);
        private static readonly SolidColorBrush CriticalBrush = new(CriticalColor);
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(200, 200, 200));

        static TemperatureToColorConverter()
        {
            CoolBrush.Freeze();
            WarmBrush.Freeze();
            HotBrush.Freeze();
            CriticalBrush.Freeze();
            DefaultBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double temp = 0;
            
            if (value is double d)
                temp = d;
            else if (value is float f)
                temp = f;
            else if (value is int i)
                temp = i;
            else
                return DefaultBrush;

            // Use parameter to specify different thresholds (e.g., "gpu" for GPU with higher limits)
            var mode = parameter as string ?? "cpu";
            
            if (mode.Equals("gpu", StringComparison.OrdinalIgnoreCase))
            {
                // GPUs can run hotter
                if (temp < 65) return CoolBrush;
                if (temp < 80) return WarmBrush;
                if (temp < 90) return HotBrush;
                return CriticalBrush;
            }
            else if (mode.Equals("ssd", StringComparison.OrdinalIgnoreCase))
            {
                // SSDs have lower limits
                if (temp < 45) return CoolBrush;
                if (temp < 55) return WarmBrush;
                if (temp < 65) return HotBrush;
                return CriticalBrush;
            }
            else
            {
                // CPU default thresholds
                if (temp < 60) return CoolBrush;
                if (temp < 75) return WarmBrush;
                if (temp < 85) return HotBrush;
                return CriticalBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts load percentage to color based on utilization levels.
    /// Green (Low) &lt; 50%, Yellow (Medium) 50-80%, Orange (High) 80-95%, Red (Critical) &gt; 95%
    /// </summary>
    public class LoadToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush LowBrush = new(Color.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush MediumBrush = new(Color.FromRgb(255, 193, 7));
        private static readonly SolidColorBrush HighBrush = new(Color.FromRgb(255, 152, 0));
        private static readonly SolidColorBrush CriticalBrush = new(Color.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(200, 200, 200));

        static LoadToColorConverter()
        {
            LowBrush.Freeze();
            MediumBrush.Freeze();
            HighBrush.Freeze();
            CriticalBrush.Freeze();
            DefaultBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double load = 0;
            
            if (value is double d)
                load = d;
            else if (value is float f)
                load = f;
            else if (value is int i)
                load = i;
            else
                return DefaultBrush;

            if (load < 50) return LowBrush;
            if (load < 80) return MediumBrush;
            if (load < 95) return HighBrush;
            return CriticalBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
