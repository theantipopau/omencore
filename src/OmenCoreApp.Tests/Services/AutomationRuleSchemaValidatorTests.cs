// Temperature-trigger automation rules (CPU/GPU threshold) shipped this cycle: the backend
// (AutomationService.EvaluateTemperatureTrigger) has existed since v2.3.0, but
// AutomationRuleSchemaValidator.SupportedTriggerTypes only ever exposed Time/Battery/ACPower to
// the UI - Temperature, Process, Idle, and WiFiSSID were implemented but deliberately gated as
// "not shipped yet". This promotes Temperature (the only one of the four with no known
// correctness gap - WiFiSSID's fallback path doesn't actually match SSIDs, and Process/Idle
// weren't reviewed for this pass). No prior test file covered this validator at all.

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
        public void TryValidate_StillRejectsUnshippedTriggerTypes()
        {
            // Process/Idle/WiFiSSID stay gated this pass - regression guard so a future change
            // doesn't accidentally widen SupportedTriggerTypes without deliberate review.
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
