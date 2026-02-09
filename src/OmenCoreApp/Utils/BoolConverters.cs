using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmenCore.Utils
{
    public class BoolToYesNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Support custom TrueValue|FalseValue via ConverterParameter
            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var parts = paramStr.Split('|');
                if (parts.Length == 2)
                {
                    return (value is bool b && b) ? parts[0] : parts[1];
                }
            }
            return (value is bool boolVal && boolVal) ? "Yes" : "No";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? Brushes.Green : Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
    
    /// <summary>
    /// Converter that returns custom strings for true/false values.
    /// Set TrueValue and FalseValue properties in XAML.
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "Yes";
        public string FalseValue { get; set; } = "No";
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? TrueValue : FalseValue;
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}