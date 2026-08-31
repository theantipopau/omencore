using System;
using System.Collections.Generic;

namespace OmenCore.Models
{
    /// <summary>
    /// Applies the persisted state for a safe all-tuning rollback.
    /// Live hardware calls remain in the UI/services; this coordinator keeps the
    /// config contract testable and shared by diagnostics.
    /// </summary>
    public static class TuningRollbackCoordinator
    {
        public const string SafePerformanceMode = "Balanced";
        public const string SafeGpuPowerBoostLevel = "Minimum";
        public const string SafeFanPresetName = "Auto";
        public const int SafeTccOffset = 0;
        public const uint SafeAmdStapmWatts = 25;
        public const uint SafeAmdTempLimitC = 95;

        public static TuningRollbackOutcome ApplySafeRollback(
            AppConfig config,
            int? startupPl1Watts = null,
            int? startupPl2Watts = null)
        {
            ArgumentNullException.ThrowIfNull(config);

            var outcome = new TuningRollbackOutcome();

            if (!string.Equals(config.LastPerformanceModeName, SafePerformanceMode, StringComparison.OrdinalIgnoreCase))
            {
                config.LastPerformanceModeName = SafePerformanceMode;
                outcome.PerformanceModeReset = true;
            }

            if (!string.Equals(config.LastGpuPowerBoostLevel, SafeGpuPowerBoostLevel, StringComparison.OrdinalIgnoreCase))
            {
                config.LastGpuPowerBoostLevel = SafeGpuPowerBoostLevel;
                outcome.GpuPowerBoostReset = true;
            }

            if (!string.Equals(config.LastFanPresetName, SafeFanPresetName, StringComparison.OrdinalIgnoreCase))
            {
                config.LastFanPresetName = SafeFanPresetName;
                outcome.FanPresetReset = true;
            }

            if (config.LastTccOffset != SafeTccOffset)
            {
                config.LastTccOffset = SafeTccOffset;
                outcome.TccOffsetReset = true;
            }

            var targetPl1 = NormalizePositive(startupPl1Watts);
            var targetPl2 = NormalizePositive(startupPl2Watts);
            if (config.LastCpuPl1Watts != targetPl1)
            {
                config.LastCpuPl1Watts = targetPl1;
                outcome.CpuPowerLimitsReset = true;
            }

            if (config.LastCpuPl2Watts != targetPl2)
            {
                config.LastCpuPl2Watts = targetPl2;
                outcome.CpuPowerLimitsReset = true;
            }

            config.Undervolt ??= new UndervoltPreferences();
            if (HasUnsafeUndervoltState(config.Undervolt))
            {
                ApplyManualUndervoltRollback(config.Undervolt);
                outcome.CpuUndervoltReset = true;
            }

            if (config.GpuOc != null && HasUnsafeGpuOcState(config.GpuOc))
            {
                ApplyManualGpuOcRollback(config.GpuOc);
                outcome.GpuOcReset = true;
            }

            if (config.AmdPowerLimits != null &&
                (config.AmdPowerLimits.StapmLimitWatts != SafeAmdStapmWatts ||
                 config.AmdPowerLimits.TempLimitC != SafeAmdTempLimitC))
            {
                config.AmdPowerLimits.StapmLimitWatts = SafeAmdStapmWatts;
                config.AmdPowerLimits.TempLimitC = SafeAmdTempLimitC;
                outcome.AmdPowerLimitsReset = true;
            }

            outcome.ConfigChanged =
                outcome.PerformanceModeReset ||
                outcome.GpuPowerBoostReset ||
                outcome.FanPresetReset ||
                outcome.TccOffsetReset ||
                outcome.CpuPowerLimitsReset ||
                outcome.CpuUndervoltReset ||
                outcome.GpuOcReset ||
                outcome.AmdPowerLimitsReset;

            return outcome;
        }

        private static int? NormalizePositive(int? value) =>
            value.HasValue && value.Value > 0 ? value.Value : null;

        private static bool HasUnsafeUndervoltState(UndervoltPreferences prefs)
        {
            return prefs.DefaultOffset?.CoreMv != 0 ||
                   prefs.DefaultOffset?.CacheMv != 0 ||
                   prefs.PerCoreOffsetsMv != null ||
                   prefs.EnablePerCoreUndervolt ||
                   prefs.ApplyOnStartup ||
                   prefs.PendingTestApply ||
                   prefs.StartupPendingConfirmation ||
                   prefs.LastStartupHadUnconfirmedState;
        }

        private static void ApplyManualUndervoltRollback(UndervoltPreferences prefs)
        {
            prefs.DefaultOffset ??= new UndervoltOffset();
            prefs.DefaultOffset.CoreMv = 0;
            prefs.DefaultOffset.CacheMv = 0;
            prefs.PerCoreOffsetsMv = null;
            prefs.EnablePerCoreUndervolt = false;
            prefs.ApplyOnStartup = false;
            prefs.PendingTestApply = false;
            prefs.StartupPendingConfirmation = false;
            prefs.LastStartupHadUnconfirmedState = false;
        }

        private static bool HasUnsafeGpuOcState(GpuOcSettings settings)
        {
            return settings.CoreClockOffsetMHz != 0 ||
                   settings.MemoryClockOffsetMHz != 0 ||
                   settings.PowerLimitPercent != 100 ||
                   settings.VoltageOffsetMv.GetValueOrDefault() != 0 ||
                   settings.ApplyOnStartup ||
                   settings.PendingTestApply ||
                   settings.StartupPendingConfirmation ||
                   settings.LastStartupHadUnconfirmedState;
        }

        private static void ApplyManualGpuOcRollback(GpuOcSettings settings)
        {
            settings.CoreClockOffsetMHz = 0;
            settings.MemoryClockOffsetMHz = 0;
            settings.PowerLimitPercent = 100;
            settings.VoltageOffsetMv = 0;
            settings.ApplyOnStartup = false;
            settings.PendingTestApply = false;
            settings.StartupPendingConfirmation = false;
            settings.LastStartupHadUnconfirmedState = false;
        }
    }

    public sealed class TuningRollbackOutcome
    {
        public bool PerformanceModeReset { get; set; }
        public bool GpuPowerBoostReset { get; set; }
        public bool FanPresetReset { get; set; }
        public bool TccOffsetReset { get; set; }
        public bool CpuPowerLimitsReset { get; set; }
        public bool CpuUndervoltReset { get; set; }
        public bool GpuOcReset { get; set; }
        public bool AmdPowerLimitsReset { get; set; }
        public bool ConfigChanged { get; set; }

        public IReadOnlyList<string> ChangedAreas
        {
            get
            {
                var areas = new List<string>();
                if (PerformanceModeReset) areas.Add("Performance mode");
                if (GpuPowerBoostReset) areas.Add("GPU power boost");
                if (FanPresetReset) areas.Add("Fan preset");
                if (TccOffsetReset) areas.Add("TCC offset");
                if (CpuPowerLimitsReset) areas.Add("CPU power limits");
                if (CpuUndervoltReset) areas.Add("CPU undervolt");
                if (GpuOcReset) areas.Add("GPU OC");
                if (AmdPowerLimitsReset) areas.Add("AMD power limits");
                return areas;
            }
        }
    }
}
