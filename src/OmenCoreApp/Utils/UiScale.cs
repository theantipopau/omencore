using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Utils
{
    /// <summary>
    /// Display scale for the shell (roadmap WP-33, the font-scale item deferred from WP-45).
    ///
    /// This is a layout zoom, not a font-size change: every view sets its own explicit
    /// <c>FontSize</c>, so neither an inherited size nor a style setter would reach them. Scaling
    /// the content host's layout transform grows text, spacing and controls together, which is what
    /// a user asking for "bigger" actually wants, and it is the same unit the nav rail already
    /// shrinks itself with.
    ///
    /// The steps are fixed rather than a free number so a stored value always maps to something
    /// the Settings combo box can show, and so 100 % is exactly the identity transform.
    /// </summary>
    public static class UiScale
    {
        public const int Default = 100;

        public static IReadOnlyList<int> Steps { get; } = new[] { 80, 90, 100, 110, 125, 140, 150 };

        /// <summary>
        /// Maps any stored value to the nearest supported step. A config edited by hand to 133
        /// becomes 140; nonsense becomes 100 rather than throwing on startup.
        /// </summary>
        public static int Normalize(int percent)
        {
            if (percent <= 0)
            {
                return Default;
            }

            if (Steps.Contains(percent))
            {
                return percent;
            }

            return Steps.OrderBy(step => System.Math.Abs(step - percent)).First();
        }

        /// <summary>Layout transform factor for a (already normalized) percentage.</summary>
        public static double Factor(int percent) => Normalize(percent) / 100.0;

        /// <summary>
        /// Whether the page host has to scroll as a whole.
        ///
        /// Only at a scale other than 100 %: a scrolling host measures its child with infinite
        /// height, which silently turns every <c>*</c> row on a page into <c>Auto</c> - the chart
        /// that used to fill the window sizes to its content instead, and the page grows to fit.
        /// That is acceptable when the user has deliberately zoomed and expects to scroll; it is not
        /// acceptable as the default layout, so the unzoomed case keeps the constrained measurement
        /// every page was written against.
        /// </summary>
        public static bool PageScrollsAsOne(int percent) => Normalize(percent) != Default;

        /// <summary>The next step up, or the current value at the ceiling.</summary>
        public static int Next(int percent)
        {
            int current = Normalize(percent);
            int index = Steps.ToList().IndexOf(current);
            return index >= Steps.Count - 1 ? current : Steps[index + 1];
        }

        /// <summary>The next step down, or the current value at the floor.</summary>
        public static int Previous(int percent)
        {
            int current = Normalize(percent);
            int index = Steps.ToList().IndexOf(current);
            return index <= 0 ? current : Steps[index - 1];
        }
    }
}
