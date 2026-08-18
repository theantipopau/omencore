using System;
using System.Windows;

namespace OmenCore.Utils
{
    /// <summary>
    /// Whether OmenCore should suppress its own motion (view transitions, state-change easing,
    /// anything non-essential). Combines the Windows-wide "Show animations" preference
    /// (<see cref="SystemParameters.ClientAreaAnimation"/>, read via the standard
    /// SPI_GETCLIENTAREAANIMATION mechanism - the same value VisualEffectsOptimizer's "Best
    /// Performance" system tweak already writes to, so running that optimization also quiets
    /// this app's own motion for free) with an explicit in-app override, so a user who wants
    /// OmenCore's own transitions off without touching Windows-wide settings can do that too.
    ///
    /// Every animation added under Pillar 2.2 (docs/ROADMAP_v4.2.0.md) must check
    /// <see cref="ShouldReduceMotion"/> before running - this class exists so that check has one
    /// obvious place to live rather than every future animation call site re-deriving it.
    /// </summary>
    public static class MotionPreference
    {
        // Overridable for tests; production code always goes through the real WPF property.
        // Internal rather than private so tests can substitute a deterministic value instead of
        // depending on whatever the test-runner machine's own Windows animation setting happens
        // to be.
        internal static Func<bool> OsAnimationsEnabledOverride = () => SystemParameters.ClientAreaAnimation;

        public static bool ShouldReduceMotion(bool userReduceMotionOverride)
        {
            if (userReduceMotionOverride)
            {
                return true;
            }

            try
            {
                return !OsAnimationsEnabledOverride();
            }
            catch
            {
                // SystemParameters can throw in edge-case hosts (no display, RDP session
                // mid-teardown). Default to NOT reducing motion rather than let a read failure
                // silently disable animation for everyone.
                return false;
            }
        }
    }
}
