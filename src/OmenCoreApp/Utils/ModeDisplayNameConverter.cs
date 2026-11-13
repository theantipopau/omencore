using System;
using System.Globalization;
using System.Windows.Data;
using OmenCore.Models;

namespace OmenCore.Utils
{
    /// <summary>Shows a performance mode by the profile card's name ("Balanced", not the firmware's "Default").</summary>
    public sealed class ModeDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            PerformanceModeNameResolver.ToDisplayName(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
