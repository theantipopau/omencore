using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The validator's job is to say what the loader would fix silently, so these tests pin two
    /// things per rule: that an out-of-range value is reported, and that the repair flag matches what
    /// <c>ConfigurationService.ValidateAndRepair</c> actually does.
    /// </summary>
    public class ConfigValidatorTests
    {
        private static AppConfig Valid() => DefaultConfiguration.Create();

        private static ConfigIssue Single(IReadOnlyList<ConfigIssue> issues, string path)
        {
            var matches = issues.Where(issue => issue.Path == path).ToList();
            matches.Should().HaveCount(1, $"exactly one finding should name '{path}'");
            return matches[0];
        }

        [Fact]
        public void TheDefaultConfiguration_IsClean()
        {
            ConfigValidator.Validate(Valid()).Should().BeEmpty(
                "a freshly created config must not warn about itself");
        }

        [Fact]
        public void NullConfig_IsAnError()
        {
            var issues = ConfigValidator.Validate(null);

            issues.Should().ContainSingle();
            issues[0].Severity.Should().Be(ConfigIssueSeverity.Error);
            ConfigValidator.HasErrors(issues).Should().BeTrue();
        }

        [Fact]
        public void MonitoringIntervalOutOfRange_IsAnErrorTheLoaderRepairs()
        {
            var config = Valid();
            config.MonitoringIntervalMs = 100;

            var issue = Single(ConfigValidator.Validate(config), "monitoringIntervalMs");

            issue.Severity.Should().Be(ConfigIssueSeverity.Error);
            issue.RepairedOnLoad.Should().BeTrue("ValidateAndRepair resets it to 1000");
        }

        [Fact]
        public void BatteryThresholdBelowTheFirmwareRange_IsAnError()
        {
            var config = Valid();
            config.Battery.ChargeThresholdPercent = 50;

            var issue = Single(ConfigValidator.Validate(config), "battery.chargeThresholdPercent");

            issue.Severity.Should().Be(ConfigIssueSeverity.Error);
            issue.RepairedOnLoad.Should().BeFalse();
        }

        [Fact]
        public void EmergencyThresholdTooCloseToProtection_IsAnError()
        {
            var config = Valid();
            config.FanHysteresis.ThermalProtectionThreshold = 90;
            config.FanHysteresis.ThermalEmergencyThreshold = 91;

            Single(ConfigValidator.Validate(config), "fanHysteresis.thermalEmergencyThreshold")
                .Severity.Should().Be(ConfigIssueSeverity.Error);
        }

        [Fact]
        public void QuietSafetyReleaseAboveTrigger_IsAnErrorOnlyWhenEnabled()
        {
            var config = Valid();
            config.QuietSafety.Enabled = true;
            config.QuietSafety.SafetyOnTempC = 80;
            config.QuietSafety.SafetyOffTempC = 85;

            Single(ConfigValidator.Validate(config), "quietSafety.safetyOffTempC")
                .Severity.Should().Be(ConfigIssueSeverity.Error);

            config.QuietSafety.Enabled = false;
            ConfigValidator.Validate(config).Should().NotContain(
                issue => issue.Path == "quietSafety.safetyOffTempC",
                "the thresholds are inert while the feature is off");
        }

        [Fact]
        public void MemoryThresholdAndInterval_AreChecked()
        {
            var config = Valid();
            config.MemoryAutoCleanThreshold = 20;
            config.MemoryIntervalCleanMinutes = 0;

            var issues = ConfigValidator.Validate(config);

            Single(issues, "memoryAutoCleanThreshold").Severity.Should().Be(ConfigIssueSeverity.Error);
            Single(issues, "memoryIntervalCleanMinutes").Severity.Should().Be(ConfigIssueSeverity.Error);
        }

        [Fact]
        public void UnknownMemoryProfile_IsAWarningTheLoaderRepairs()
        {
            var config = Valid();
            config.MemoryAutoCleanProfile = "Aggressive-ish";

            var issue = Single(ConfigValidator.Validate(config), "memoryAutoCleanProfile");

            issue.Severity.Should().Be(ConfigIssueSeverity.Warning);
            issue.RepairedOnLoad.Should().BeTrue();
        }

        [Fact]
        public void OsdOpacityOutsideZeroToOne_IsAnError()
        {
            var config = Valid();
            config.Osd.Opacity = 1.5;

            Single(ConfigValidator.Validate(config), "osd.opacity")
                .Severity.Should().Be(ConfigIssueSeverity.Error);
        }

        [Fact]
        public void UnshippedLanguage_IsOnlyAWarning()
        {
            var config = Valid();
            config.Language = "de";

            var issue = Single(ConfigValidator.Validate(config), "language");

            issue.Severity.Should().Be(ConfigIssueSeverity.Warning);
            ConfigValidator.HasErrors(ConfigValidator.Validate(config)).Should().BeFalse(
                "an unsupported language falls back rather than breaking the launch");
        }

        [Fact]
        public void ShippedAndDefaultLanguages_AreNotReported()
        {
            foreach (string? language in new[] { null, "auto", "en", "ru", "tr", " RU " })
            {
                var config = Valid();
                config.Language = language!;

                ConfigValidator.Validate(config).Should().NotContain(issue => issue.Path == "language",
                    $"'{language}' is a supported setting");
            }
        }

        [Fact]
        public void MalformedScheduleTime_IsAnError()
        {
            var config = Valid();
            config.ScheduleRules = new List<ScheduleRule>
            {
                new() { RuleName = "Night", TriggerTime = "25:00" }
            };

            Single(ConfigValidator.Validate(config), "scheduleRules[0].triggerTime")
                .Severity.Should().Be(ConfigIssueSeverity.Error);
        }

        [Fact]
        public void DuplicatePerformanceModeNames_AreAWarning()
        {
            var config = Valid();
            config.PerformanceModes = new List<PerformanceMode>
            {
                new() { Name = "Balanced" },
                new() { Name = "balanced" }
            };

            Single(ConfigValidator.Validate(config), "performanceModes")
                .Severity.Should().Be(ConfigIssueSeverity.Warning);
        }
    }
}
