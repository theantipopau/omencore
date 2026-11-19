using System.Linq;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class OmenKeyActionOptionsTests
    {
        [Fact]
        public void EveryListedAction_IsOneTheKeyServiceUnderstands()
        {
            foreach (var option in SettingsViewModel.OmenKeyActionChoices)
            {
                // ParseAction falls back to ShowQuickPopup for unknown strings, so only that key may map to it.
                var parsed = OmenKeyService.ParseAction(option.Key);
                if (option.Key != "ShowQuickPopup")
                    parsed.Should().NotBe(OmenKeyAction.ShowQuickPopup, option.Key);
                option.Value.Should().NotBe(option.Key, "the list shows a readable name, not the identifier");
            }
        }

        [Theory]
        [InlineData("ToggleFanMode", "CycleFanMode")]
        [InlineData("TogglePerformanceMode", "CyclePerformance")]
        [InlineData(null, "ShowQuickPopup")]
        [InlineData("ToggleMaxCooling", "ToggleMaxCooling")]
        public void SavedAliases_StillSelectAListedEntry(string? saved, string expected)
        {
            var normalized = SettingsViewModel.NormalizeOmenKeyAction(saved);
            normalized.Should().Be(expected);
            SettingsViewModel.OmenKeyActionChoices.Select(o => o.Key).Should().Contain(normalized);
        }
    }
}
