using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OmenCore.Models;

namespace OmenCore.Utils
{
    /// <summary>
    /// Compact, pasteable encoding for a fan curve - a "share code" string a user can drop
    /// directly into a Discord message, forum post, or GitHub comment, rather than attaching a
    /// file. OmenCore already has file-based curve sharing (a saved <see cref="FanPreset"/>
    /// already carries its <see cref="FanPreset.Curve"/> through the existing
    /// FanControlViewModel Import/Export-Presets commands) - this exists for the case a file
    /// attachment is the wrong tool: a one-line string that survives being pasted into a chat
    /// box. Idea prompted by looking at a comparable community project (OmenXHub, MIT licensed)
    /// that has an equivalent "share code" concept for its own RPM-based curves; this is an
    /// independent implementation using OmenCore's own curve model (percent-based, not RPM) and
    /// format, not ported code.
    /// </summary>
    public static class FanCurveShareCode
    {
        private const string Prefix = "OCFC1:";

        /// <summary>
        /// Encodes a curve as a share code. Points are sorted by temperature first, since an
        /// unsorted curve isn't valid input for interpolation on the receiving end either.
        /// Returns null if there are fewer than 2 points - a "curve" of 0 or 1 points can't be
        /// interpolated and isn't worth sharing.
        /// </summary>
        public static string? Generate(IEnumerable<FanCurvePoint> points, string curveName)
        {
            if (points == null)
            {
                return null;
            }

            var sorted = points.OrderBy(p => p.TemperatureC).ToList();
            if (sorted.Count < 2)
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(curveName) ? "Custom Curve" : curveName.Replace('|', '-').Trim();
            var serializedPoints = string.Join(";", sorted.Select(p =>
                $"{p.TemperatureC.ToString(CultureInfo.InvariantCulture)},{p.FanPercent.ToString(CultureInfo.InvariantCulture)}"));
            var payload = $"{name}|{serializedPoints}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decodes a share code back into curve points and a name. Returns false for anything
        /// that isn't a well-formed, valid curve - malformed input must never partially apply.
        /// </summary>
        public static bool TryParse(string? code, out List<FanCurvePoint> points, out string name)
        {
            points = new List<FanCurvePoint>();
            name = string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var trimmed = code.Trim();
            if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(Prefix.Length);
            }

            string payload;
            try
            {
                var bytes = Convert.FromBase64String(trimmed);
                payload = Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return false;
            }

            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex < 0)
            {
                return false;
            }

            var candidateName = payload.Substring(0, separatorIndex);
            var serializedPoints = payload.Substring(separatorIndex + 1);

            var parsedPoints = new List<FanCurvePoint>();
            foreach (var segment in serializedPoints.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var temp) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                {
                    return false;
                }

                parsedPoints.Add(new FanCurvePoint { TemperatureC = temp, FanPercent = percent });
            }

            if (!IsValidCurve(parsedPoints))
            {
                return false;
            }

            points = parsedPoints.OrderBy(p => p.TemperatureC).ToList();
            name = candidateName;
            return true;
        }

        /// <summary>
        /// A curve is valid to import if it has at least 2 points, every temperature is unique
        /// (interpolation is undefined for duplicate keys), and every fan percent is within the
        /// range the rest of the app already assumes (0-100). Deliberately does not clamp or
        /// silently "fix" an out-of-range value - a bad share code should be rejected outright,
        /// not partially applied with the caller unaware something was altered.
        /// </summary>
        private static bool IsValidCurve(List<FanCurvePoint> points)
        {
            if (points.Count < 2)
            {
                return false;
            }

            if (points.Select(p => p.TemperatureC).Distinct().Count() != points.Count)
            {
                return false;
            }

            return points.All(p => p.FanPercent >= 0 && p.FanPercent <= 100);
        }
    }
}
