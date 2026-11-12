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

        /// <summary>
        /// The name to show a user. The firmware and older configs call the middle mode "Default" (or "Normal",
        /// "Auto"), while the profile card and the sidebar call it "Balanced"; showing both on one screen read as two
        /// different states. Built-in aliases map to the card names; anything else is a user's own saved mode and is
        /// shown exactly as they named it (unlike <see cref="Normalize"/>, which folds unknown names into Balanced).
        /// </summary>
        public static string ToDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "--";
            if (IsPerformanceAlias(value)) return "Performance";
            if (IsQuietAlias(value)) return "Quiet";
            if (IsBalancedAlias(value)) return "Balanced";
            return value.Trim();
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