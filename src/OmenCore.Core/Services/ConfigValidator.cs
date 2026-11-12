using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OmenCore.Models;

namespace OmenCore.Services
{
    public enum ConfigIssueSeverity
    {
        Warning,
        Error
    }

    /// <summary>
    /// One problem found in a configuration file.
    /// </summary>
    /// <param name="Path">The JSON path as the user sees it, e.g. <c>quietSafety.safetyOnTempC</c>.</param>
    /// <param name="RepairedOnLoad">
    /// True when <see cref="ConfigurationService.ValidateAndRepair"/> silently fixes this on load, so
    /// the user understands why a value they set does not stick.
    /// </param>
    public sealed record ConfigIssue(
        string Path,
        string Message,
        ConfigIssueSeverity Severity,
        bool RepairedOnLoad);

    /// <summary>
    /// Checks a configuration object against the ranges this app documents and enforces (roadmap
    /// I.10, definition-of-done item 14).
    ///
    /// This exists because the loader is forgiving by design: <c>ConfigurationService.ValidateAndRepair</c>
    /// resets a handful of out-of-range fields with a log line the user never sees. That is the right
    /// default for starting up, and the wrong answer to "why did my setting not stick?" - so the same
    /// rules are stated here as diagnostics, with the repair marked, and exposed as
    /// <c>omencore-cli config --validate</c>.
    ///
    /// Pure and side-effect free: it reads a deserialized <see cref="AppConfig"/> and returns findings.
    /// Every rule cites the range the rest of the app already asserts (the documented property
    /// comments, the loader's clamps, or the validator used by the setting's own service), so this
    /// file is a report about existing behaviour, not a second opinion that can drift from it.
    /// </summary>
    public static class ConfigValidator
    {
        private static readonly string[] MemoryCleanProfiles =
            { "Aggressive", "Balanced", "Conservative", "OffPeakOnly", "Manual" };

        /// <summary>Languages the app actually ships. Unknown values fall back to "auto".</summary>
        private static readonly string[] Languages = { "auto", "en", "ru", "tr" };

        public static IReadOnlyList<ConfigIssue> Validate(AppConfig? config)
        {
            var issues = new List<ConfigIssue>();

            if (config is null)
            {
                issues.Add(new ConfigIssue("(root)",
                    "The file does not deserialize to a configuration object; loading it would replace it with defaults.",
                    ConfigIssueSeverity.Error, RepairedOnLoad: true));
                return issues;
            }

            // Loader-enforced (ConfigurationService.ValidateAndRepair)
            if (config.MonitoringIntervalMs < 500 || config.MonitoringIntervalMs > 10000)
            {
                issues.Add(new ConfigIssue("monitoringIntervalMs",
                    $"{config.MonitoringIntervalMs} ms is outside 500-10000 ms; the loader resets it to 1000.",
                    ConfigIssueSeverity.Error, RepairedOnLoad: true));
            }

            if (config.FanTransition is null)
            {
                issues.Add(new ConfigIssue("fanTransition", "Missing; defaults are used.",
                    ConfigIssueSeverity.Warning, RepairedOnLoad: true));
            }
            else
            {
                if (config.FanTransition.SmoothingDurationMs < 0)
                {
                    issues.Add(new ConfigIssue("fanTransition.smoothingDurationMs",
                        $"{config.FanTransition.SmoothingDurationMs} is negative; the loader resets it to 1000.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: true));
                }

                if (config.FanTransition.SmoothingStepMs <= 0)
                {
                    issues.Add(new ConfigIssue("fanTransition.smoothingStepMs",
                        $"{config.FanTransition.SmoothingStepMs} is not positive; the loader resets it to 200.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: true));
                }
            }

            // Battery care: the firmware accepts 60-100 %, matching the setting's own documented range
            if (config.Battery is { } battery && (battery.ChargeThresholdPercent < 60 || battery.ChargeThresholdPercent > 100))
            {
                issues.Add(new ConfigIssue("battery.chargeThresholdPercent",
                    $"{battery.ChargeThresholdPercent} % is outside 60-100 %; Battery Care would be asked to hold an unreachable level.",
                    ConfigIssueSeverity.Error, RepairedOnLoad: false));
            }

            // Fan hysteresis and thermal protection
            if (config.FanHysteresis is { } hysteresis)
            {
                if (hysteresis.DeadZone < 0)
                {
                    issues.Add(new ConfigIssue("fanHysteresis.deadZone",
                        $"{hysteresis.DeadZone} °C is negative; a dead zone must not be.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (hysteresis.RampUpDelay < 0 || hysteresis.RampDownDelay < 0)
                {
                    issues.Add(new ConfigIssue("fanHysteresis.rampDelay",
                        "Ramp delays must not be negative (ramp up " +
                        $"{hysteresis.RampUpDelay} s, ramp down {hysteresis.RampDownDelay} s).",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (hysteresis.ThermalProtectionThreshold is < 75 or > 95)
                {
                    issues.Add(new ConfigIssue("fanHysteresis.thermalProtectionThreshold",
                        $"{hysteresis.ThermalProtectionThreshold} °C is outside the documented 75-95 °C range.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (hysteresis.ThermalEmergencyThreshold is < 90 or > 99)
                {
                    issues.Add(new ConfigIssue("fanHysteresis.thermalEmergencyThreshold",
                        $"{hysteresis.ThermalEmergencyThreshold} °C is outside the documented 90-99 °C range.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }
                else if (hysteresis.ThermalEmergencyThreshold < hysteresis.ThermalProtectionThreshold + 2)
                {
                    issues.Add(new ConfigIssue("fanHysteresis.thermalEmergencyThreshold",
                        $"The emergency threshold ({hysteresis.ThermalEmergencyThreshold} °C) must sit at least 2 °C above " +
                        $"the protection threshold ({hysteresis.ThermalProtectionThreshold} °C), or the two fire together.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }
            }

            // Quiet safety: both ranges, and off strictly below on
            if (config.QuietSafety is { } quiet)
            {
                if (quiet.SafetyOnTempC is < 70 or > 100)
                {
                    issues.Add(new ConfigIssue("quietSafety.safetyOnTempC",
                        $"{quiet.SafetyOnTempC} °C is outside the documented 70-100 °C range.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (quiet.SafetyOffTempC is < 50 or > 90)
                {
                    issues.Add(new ConfigIssue("quietSafety.safetyOffTempC",
                        $"{quiet.SafetyOffTempC} °C is outside the documented 50-90 °C range.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (quiet.Enabled && quiet.SafetyOffTempC >= quiet.SafetyOnTempC)
                {
                    issues.Add(new ConfigIssue("quietSafety.safetyOffTempC",
                        $"The release temperature ({quiet.SafetyOffTempC} °C) must be below the trigger " +
                        $"({quiet.SafetyOnTempC} °C); otherwise the override never releases.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }
            }

            // Memory optimizer
            if (config.MemoryIntervalCleanMinutes is < 1 or > 120)
            {
                issues.Add(new ConfigIssue("memoryIntervalCleanMinutes",
                    $"{config.MemoryIntervalCleanMinutes} min is outside 1-120 min.",
                    ConfigIssueSeverity.Error, RepairedOnLoad: false));
            }

            if (config.MemoryAutoCleanThreshold is < 50 or > 95)
            {
                issues.Add(new ConfigIssue("memoryAutoCleanThreshold",
                    $"{config.MemoryAutoCleanThreshold} % is outside 50-95 %.",
                    ConfigIssueSeverity.Error, RepairedOnLoad: false));
            }

            if (!MemoryCleanProfiles.Contains(config.MemoryAutoCleanProfile, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ConfigIssue("memoryAutoCleanProfile",
                    $"\"{config.MemoryAutoCleanProfile}\" is not one of {string.Join(", ", MemoryCleanProfiles)}; " +
                    "the memory optimizer falls back to Balanced.",
                    ConfigIssueSeverity.Warning, RepairedOnLoad: true));
            }

            // OSD
            if (config.Osd is { } osd)
            {
                if (osd.Opacity is < 0 or > 1)
                {
                    issues.Add(new ConfigIssue("osd.opacity",
                        $"{osd.Opacity} is outside 0-1 (a transparent or invisible overlay).",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (osd.HotkeyToastScalePercent is < 80 or > 140)
                {
                    issues.Add(new ConfigIssue("osd.hotkeyToastScalePercent",
                        $"{osd.HotkeyToastScalePercent} % is outside 80-140 %.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }

                if (osd.HotkeyToastDurationMs is < 800 or > 6000)
                {
                    issues.Add(new ConfigIssue("osd.hotkeyToastDurationMs",
                        $"{osd.HotkeyToastDurationMs} ms is outside 800-6000 ms.",
                        ConfigIssueSeverity.Error, RepairedOnLoad: false));
                }
            }

            // Fan hardware override: 0 means auto-detect, otherwise a plausible level
            if (config.MaxFanLevelOverride != 0 && config.MaxFanLevelOverride is < 20 or > 100)
            {
                issues.Add(new ConfigIssue("maxFanLevelOverride",
                    $"{config.MaxFanLevelOverride} is neither 0 (auto-detect) nor a level between 20 and 100.",
                    ConfigIssueSeverity.Warning, RepairedOnLoad: false));
            }

            // UI language
            if (!Languages.Contains(config.Language?.Trim().ToLowerInvariant() ?? "auto", StringComparer.Ordinal))
            {
                issues.Add(new ConfigIssue("language",
                    $"\"{config.Language}\" is not a shipped language ({string.Join(", ", Languages)}); " +
                    "the interface falls back to the system language.",
                    ConfigIssueSeverity.Warning, RepairedOnLoad: true));
            }

            // Schedules
            if (config.ScheduleRules is { } schedules)
            {
                for (int i = 0; i < schedules.Count; i++)
                {
                    var rule = schedules[i];
                    if (!TimeSpan.TryParseExact(rule.TriggerTime, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                    {
                        issues.Add(new ConfigIssue($"scheduleRules[{i}].triggerTime",
                            $"\"{rule.TriggerTime}\" is not a 24-hour HH:mm time.",
                            ConfigIssueSeverity.Error, RepairedOnLoad: false));
                    }
                }
            }

            // Profiles: duplicate names make selection ambiguous, which the UI cannot show
            foreach (var duplicate in config.PerformanceModes
                         .Where(mode => !string.IsNullOrWhiteSpace(mode.Name))
                         .GroupBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                issues.Add(new ConfigIssue("performanceModes",
                    $"{duplicate.Count()} performance modes are named \"{duplicate.Key}\"; only one can be selected.",
                    ConfigIssueSeverity.Warning, RepairedOnLoad: false));
            }

            return issues;
        }

        public static bool HasErrors(IReadOnlyList<ConfigIssue> issues) =>
            issues.Any(issue => issue.Severity == ConfigIssueSeverity.Error);
    }
}
