// Automation-rule trigger types shipped this cycle in two passes: the backend
// (AutomationService.Evaluate*Trigger) has existed since v2.3.0, but
// AutomationRuleSchemaValidator.SupportedTriggerTypes only ever exposed Time/Battery/ACPower to
// the UI - the other four were implemented but deliberately gated as "not shipped yet".
// Pass 1 promoted Temperature and Idle after confirming both were already correct.
// Pass 2 promotes Process and WiFiSSID, after fixing the real bug each one had:
//   - WiFiSSID's WMI SSID lookup ("root\WlanApi") and its own fallback (which didn't check the
//     SSID at all, just whether any wireless interface was up) are both replaced by
//     OmenCore.Utils.WlanSsidHelper, a native wlanapi.dll P/Invoke wrapper - the same API the
//     Windows network flyout itself is built on.
//   - Process's trigger reads ProcessMonitoringService.ActiveProcesses, which only ever contains
//     processes registered via TrackProcess() - previously only GameProfileService called that,
//     for configured Game Profiles. AutomationService.EvaluateRules now also calls TrackProcess()
//     for every enabled Process-trigger rule's executable (see
//     AutomationService.GetProcessTriggerExecutableNames), so a rule for any executable - not
//     just a configured Game Profile - actually gets evaluated now.
// All 7 backend trigger types are shipped as of this pass. No prior test file covered this
// validator at all before Pass 1.

using System.Linq;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class AutomationRuleSchemaValidatorTests
    {
        [Fact]
        public void SupportedTriggerTypes_IncludesTemperature()
        {
            AutomationRuleSchemaValidator.IsSupportedTriggerType(TriggerType.Temperature).Should().BeTrue();
        }

        [Fact]
        public void TryValidate_AcceptsValidTemperatureRule()
        {
            var rule = new AutomationRule
            {
                Name = "Cool down at 85C",
                Trigger = TriggerType.Temperature,
                TriggerData = new TriggerConfig
                {
                    TemperatureThreshold = 85,
                    TemperatureCondition = "Above",
                    TemperatureSensor = "CPU"
                },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeTrue(error);
        }

        [Fact]
        public void TryValidate_AcceptsTemperatureRule_WithNoSensorSpecified()
        {
            // TemperatureSensor is optional - EvaluateTemperatureTrigger defaults a null/empty
            // sensor to "cpu" at runtime, so the validator must not require it explicitly.
            var rule = new AutomationRule
            {
                Name = "Cool down",
                Trigger = TriggerType.Temperature,
                TriggerData = new TriggerConfig { TemperatureThreshold = 90, TemperatureCondition = "Above" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeTrue(error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(111)]
        public void TryValidate_RejectsTemperatureRule_ThresholdOutOfRange(int threshold)
        {
            var rule = new AutomationRule
            {
                Name = "Bad threshold",
                Trigger = TriggerType.Temperature,
                TriggerData = new TriggerConfig { TemperatureThreshold = threshold, TemperatureCondition = "Above" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("threshold");
        }

        [Fact]
        public void TryValidate_RejectsTemperatureRule_MissingThreshold()
        {
            var rule = new AutomationRule
            {
                Name = "No threshold",
                Trigger = TriggerType.Temperature,
                TriggerData = new TriggerConfig { TemperatureCondition = "Above" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
        }

        [Fact]
        public void TryValidate_RejectsTemperatureRule_InvalidCondition()
        {
            var rule = new AutomationRule
            {
                Name = "Bad condition",
                Trigger = TriggerType.Temperature,
                TriggerData = new TriggerConfig { TemperatureThreshold = 85, TemperatureCondition = "Sideways" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("condition");
        }

        [Fact]
        public void SupportedTriggerTypes_IncludesIdle()
        {
            AutomationRuleSchemaValidator.IsSupportedTriggerType(TriggerType.Idle).Should().BeTrue();
        }

        [Fact]
        public void TryValidate_AcceptsValidIdleRule()
        {
            var rule = new AutomationRule
            {
                Name = "Idle for 15 minutes",
                Trigger = TriggerType.Idle,
                TriggerData = new TriggerConfig { IdleMinutes = 15 },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Quiet" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeTrue(error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1000)]
        public void TryValidate_RejectsIdleRule_MinutesOutOfRange(int minutes)
        {
            var rule = new AutomationRule
            {
                Name = "Bad idle minutes",
                Trigger = TriggerType.Idle,
                TriggerData = new TriggerConfig { IdleMinutes = minutes },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Quiet" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("minutes");
        }

        [Fact]
        public void TryValidate_RejectsIdleRule_MissingMinutes()
        {
            var rule = new AutomationRule
            {
                Name = "No idle minutes",
                Trigger = TriggerType.Idle,
                TriggerData = new TriggerConfig(),
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Quiet" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
        }

        [Fact]
        public void SupportedTriggerTypes_IncludesProcess()
        {
            AutomationRuleSchemaValidator.IsSupportedTriggerType(TriggerType.Process).Should().BeTrue();
        }

        [Fact]
        public void TryValidate_AcceptsValidProcessRule()
        {
            var rule = new AutomationRule
            {
                Name = "Game running",
                Trigger = TriggerType.Process,
                TriggerData = new TriggerConfig { ProcessName = "game.exe" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeTrue(error);
        }

        [Fact]
        public void TryValidate_RejectsProcessRule_MissingProcessName()
        {
            var rule = new AutomationRule
            {
                Name = "No process name",
                Trigger = TriggerType.Process,
                TriggerData = new TriggerConfig(),
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("executable");
        }

        [Fact]
        public void SupportedTriggerTypes_IncludesWiFiSSID()
        {
            AutomationRuleSchemaValidator.IsSupportedTriggerType(TriggerType.WiFiSSID).Should().BeTrue();
        }

        [Fact]
        public void TryValidate_AcceptsValidWiFiRule()
        {
            var rule = new AutomationRule
            {
                Name = "Connected to home network",
                Trigger = TriggerType.WiFiSSID,
                TriggerData = new TriggerConfig { WiFiSSID = "Home-WiFi" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Quiet" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeTrue(error);
        }

        [Fact]
        public void TryValidate_RejectsWiFiRule_MissingSsid()
        {
            var rule = new AutomationRule
            {
                Name = "No SSID",
                Trigger = TriggerType.WiFiSSID,
                TriggerData = new TriggerConfig(),
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Quiet" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("SSID");
        }

        // Regression guard for GetProcessTriggerExecutableNames (AutomationService.EvaluateRules'
        // TrackProcess-registration fix): only enabled Process-trigger rules with a real executable
        // name should be registered, names should be deduplicated case-insensitively, and other
        // trigger types must never leak in.
        [Fact]
        public void GetProcessTriggerExecutableNames_ReturnsDistinctNamesFromEnabledProcessRulesOnly()
        {
            var rules = new[]
            {
                new AutomationRule { Enabled = true, Trigger = TriggerType.Process, TriggerData = new TriggerConfig { ProcessName = "Game.exe" } },
                new AutomationRule { Enabled = true, Trigger = TriggerType.Process, TriggerData = new TriggerConfig { ProcessName = "game.exe" } },
                new AutomationRule { Enabled = false, Trigger = TriggerType.Process, TriggerData = new TriggerConfig { ProcessName = "disabled.exe" } },
                new AutomationRule { Enabled = true, Trigger = TriggerType.Process, TriggerData = new TriggerConfig() },
                new AutomationRule { Enabled = true, Trigger = TriggerType.Temperature, TriggerData = new TriggerConfig { TemperatureThreshold = 85, TemperatureCondition = "Above" } }
            };

            var names = AutomationService.GetProcessTriggerExecutableNames(rules).ToList();

            names.Should().ContainSingle().Which.Should().Be("Game.exe");
        }
    }
}
