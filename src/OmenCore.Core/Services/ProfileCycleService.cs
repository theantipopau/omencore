using System;

namespace OmenCore.Services
{
    /// <summary>
    /// Resolves deterministic hotkey profile cycles without depending on UI state.
    /// </summary>
    public static class ProfileCycleService
    {
        private static readonly string[] StandardProfiles = { "Balanced", "Performance", "Quiet" };
        private static readonly string[] ProfilesWithCustom = { "Balanced", "Performance", "Quiet", "Custom" };
        private static readonly string[] FanModes = { "Auto", "Gaming", "Extreme", "Custom", "Quiet" };

        public static string ResolveNextPerformanceProfile(string? currentProfile, bool hasCustomCurve)
        {
            var profiles = hasCustomCurve ? ProfilesWithCustom : StandardProfiles;
            var current = string.IsNullOrWhiteSpace(currentProfile) ? "Balanced" : currentProfile;

            var currentIndex = Array.FindIndex(profiles,
                p => p.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = current.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                    ? profiles.Length - 1
                    : 0;
            }

            return profiles[(currentIndex + 1 + profiles.Length) % profiles.Length];
        }

        public static string ResolveNextFanMode(
            string? currentCycleMode,
            bool hasCustomCurve,
            string? customTargetName,
            out string targetMode)
        {
            var current = string.IsNullOrWhiteSpace(currentCycleMode) ? "Auto" : currentCycleMode;
            var currentIndex = Array.FindIndex(FanModes,
                mode => mode.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            for (var offset = 1; offset <= FanModes.Length; offset++)
            {
                var candidate = FanModes[(currentIndex + offset) % FanModes.Length];
                if (!candidate.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                {
                    targetMode = candidate;
                    return candidate;
                }

                if (hasCustomCurve)
                {
                    targetMode = string.IsNullOrWhiteSpace(customTargetName) ? "Custom" : customTargetName;
                    return "Custom";
                }
            }

            targetMode = "Auto";
            return "Auto";
        }
    }
}
