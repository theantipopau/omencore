using System;

namespace OmenCore.Models
{
    /// <summary>
    /// Canonical performance-mode aliases used across hotkeys, tray UI, and automation.
    /// </summary>
    public static class PerformanceModeNameResolver
    {
        public static bool IsBalancedAlias(string? value)
        {
            return EqualsAny(value, "balanced", "default", "normal", "auto");
        }

        public static bool IsPerformanceAlias(string? value)
        {
            return EqualsAny(value, "performance", "turbo", "boost", "extreme");
        }

        public static bool IsQuietAlias(string? value)
        {
            return EqualsAny(value, "quiet", "silent", "powersaver", "power saver");
        }

        public static string Normalize(string? value)
        {
            if (IsPerformanceAlias(value))
            {
                return "Performance";
            }

            if (IsQuietAlias(value))
            {
                return "Quiet";
            }

            return "Balanced";
        }

        public static bool AreEquivalent(string? left, string? right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualsAny(string? value, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var alias in aliases)
            {
                if (string.Equals(value.Trim(), alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}