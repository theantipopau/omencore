namespace OmenCore.Models
{
    /// <summary>
    /// Coordinates tuning startup recovery at AppConfig scope.
    /// Applies safe reset decisions for both CPU undervolt and GPU OC sections.
    /// </summary>
    public static class TuningStartupRecoveryCoordinator
    {
        public static TuningStartupRecoveryOutcome Recover(AppConfig config)
        {
            var outcome = new TuningStartupRecoveryOutcome();

            config.Undervolt ??= new UndervoltPreferences();
            if (TuningStartupRecoveryGuard.ShouldSafeReset(config.Undervolt))
            {
                TuningStartupRecoveryGuard.ApplySafeReset(config.Undervolt);
                outcome.CpuUndervoltReset = true;
            }

            if (config.GpuOc != null && TuningStartupRecoveryGuard.ShouldSafeReset(config.GpuOc))
            {
                TuningStartupRecoveryGuard.ApplySafeReset(config.GpuOc);
                outcome.GpuOcReset = true;
            }

            outcome.ConfigChanged = outcome.CpuUndervoltReset || outcome.GpuOcReset;
            return outcome;
        }
    }

    public sealed class TuningStartupRecoveryOutcome
    {
        public bool CpuUndervoltReset { get; set; }
        public bool GpuOcReset { get; set; }
        public bool ConfigChanged { get; set; }
    }
}
