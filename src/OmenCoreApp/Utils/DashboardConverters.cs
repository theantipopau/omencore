using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using OmenCore.Models;

namespace OmenCore.Utils
{
    /// <summary>
    /// Extracts the total power value from a power summary string.
    /// Input: "CPU: 25W • GPU: 31W • Total: 56W"
    /// Output: "56W"
    /// </summary>
    public class PowerToTotalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string summary || string.IsNullOrEmpty(summary))
                return "--W";
            
            // Look for "Total: XXW" pattern
            var totalIndex = summary.IndexOf("Total:", StringComparison.OrdinalIgnoreCase);
            if (totalIndex >= 0)
            {
                var afterTotal = summary.Substring(totalIndex + 6).Trim();
                var endIndex = afterTotal.IndexOf('W');
                if (endIndex > 0)
                {
                    return afterTotal.Substring(0, endIndex + 1);
                }
            }
            
            // Fallback: try to extract any number followed by W
            var parts = summary.Split(' ');
            foreach (var part in parts)
            {
                if (part.EndsWith("W") && double.TryParse(part.TrimEnd('W'), out _))
                {
                    return part;
                }
            }
            
            return "--W";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
    
    /// <summary>
    /// Extracts fan RPM info from a fan summary string.
    /// Input: "45°C → CPU: 2500 RPM • GPU: 2800 RPM"
    /// Output: "2500/2800"
    /// </summary>
    public class FanSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string summary || string.IsNullOrEmpty(summary))
                return "-- RPM";
            
            // Extract CPU and GPU RPM values
            int cpuRpm = 0, gpuRpm = 0;
            
            var cpuMatch = System.Text.RegularExpressions.Regex.Match(summary, @"CPU:\s*(\d+)\s*RPM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (cpuMatch.Success)
                int.TryParse(cpuMatch.Groups[1].Value, out cpuRpm);
            
            var gpuMatch = System.Text.RegularExpressions.Regex.Match(summary, @"GPU:\s*(\d+)\s*RPM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (gpuMatch.Success)
                int.TryParse(gpuMatch.Groups[1].Value, out gpuRpm);
            
            if (cpuRpm > 0 || gpuRpm > 0)
            {
                return $"{cpuRpm}/{gpuRpm}";
            }
            
            return "-- RPM";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
    
    /// <summary>
    /// Extracts the latest fan speed from FanCurvePoints collection.
    /// ConverterParameter: "cpu" or "gpu"
    /// </summary>
    public class LatestFanSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not IEnumerable<FanCurvePoint> points)
                return "-- RPM";
            
            var pointsList = points.ToList();
            if (pointsList.Count == 0)
                return "-- RPM";
            
            // Get the most recent point
            var latest = pointsList.LastOrDefault();
            if (latest == null)
                return "-- RPM";
            
            // For now, return the fan speed - in a real implementation,
            // you might want to track CPU/GPU fans separately
            var rpm = (int)latest.FanSpeedRpm;
            return rpm > 0 ? $"{rpm} RPM" : "-- RPM";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
