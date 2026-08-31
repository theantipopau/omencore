namespace OmenCore.Models
{
    /// <summary>
    /// Centralized startup-recovery policy for tuning settings.
    /// If a profile was left in an unconfirmed test state, startup performs a safe reset.
    /// </summary>
    public static class TuningStartupRecoveryGuard
    {
        public static bool ShouldSafeReset(UndervoltPreferences prefs)
        {
            return prefs.PendingTestApply || prefs.StartupPendingConfirmation;
        }

        public static bool ShouldSafeReset(GpuOcSettings settings)
        {
            return settings.PendingTestApply || settings.StartupPendingConfirmation;
        }

        public static void ApplySafeReset(UndervoltPreferences prefs)
        {
            prefs.DefaultOffset ??= new UndervoltOffset();
            prefs.DefaultOffset.CoreMv = 0;
            prefs.DefaultOffset.CacheMv = 0;
            prefs.PerCoreOffsetsMv = null;
            prefs.EnablePerCoreUndervolt = false;
            prefs.ApplyOnStartup = false;
            prefs.PendingTestApply = false;
            prefs.StartupPendingConfirmation = false;
            prefs.LastStartupHadUnconfirmedState = true;
        }

        public static void ApplySafeReset(GpuOcSettings settings)
        {
            settings.CoreClockOffsetMHz = 0;
            settings.MemoryClockOffsetMHz = 0;
            settings.PowerLimitPercent = 100;
            settings.VoltageOffsetMv = 0;
            settings.ApplyOnStartup = false;
            settings.PendingTestApply = false;
            settings.StartupPendingConfirmation = false;
            settings.LastStartupHadUnconfirmedState = true;
        }
    }
}
