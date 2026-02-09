using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OmenCore.Utils
{
    /// <summary>
    /// Converts an integer to Visibility. Returns Visible when the value is zero, Collapsed otherwise.
    /// Useful for showing "empty state" messages when a collection count is 0.
    /// </summary>
    public class IntZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
