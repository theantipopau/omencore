// Temperature- and Idle-trigger automation rules shipped this cycle: the backend
// (AutomationService.EvaluateTemperatureTrigger/EvaluateIdleTrigger) has existed since v2.3.0,
// but AutomationRuleSchemaValidator.SupportedTriggerTypes only ever exposed Time/Battery/ACPower
// to the UI - Temperature, Process, Idle, and WiFiSSID were implemented but deliberately gated as
// "not shipped yet". Promoted Temperature and Idle after reviewing all four for correctness gaps;
// WiFiSSID stays gated (its fallback path doesn't actually match SSIDs) and so does Process (its
// trigger only ever sees processes separately tracked via a configured Game Profile - see the
// dedicated comment on the regression test below). No prior test file covered this validator at
// all before this cycle.

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

        // GitHub issue-free finding: EvaluateProcessTrigger reads ProcessMonitoringService.ActiveProcesses,
        // which only ever contains processes explicitly registered via TrackProcess() - and that's only
        // ever called from GameProfileService for configured Game Profiles. A Process-trigger automation
        // rule for any executable that isn't ALSO a configured Game Profile would silently never fire.
        // Left gated until AutomationService tracks Process-trigger rule executables too (or does its own
        // independent enumeration) - promoting it as-is would ship a rule that looks configured but never
        // actually evaluates true.
        [Fact]
        public void TryValidate_StillRejectsUnshippedTriggerTypes()
        {
            // Process/WiFiSSID stay gated - regression guard so a future change doesn't accidentally
            // widen SupportedTriggerTypes without deliberate review of each type's own correctness.
            var rule = new AutomationRule
            {
                Name = "Process trigger",
                Trigger = TriggerType.Process,
                TriggerData = new TriggerConfig { ProcessName = "game.exe" },
                Actions = { new RuleAction { Type = ActionType.SetFanPreset, Parameter = "Max" } }
            };

            AutomationRuleSchemaValidator.TryValidate(rule, out var error).Should().BeFalse();
            error.Should().Contain("not shipped yet");
        }
    }
}
