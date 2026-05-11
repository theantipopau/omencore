using FluentAssertions;
using OmenCore.Services;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class HotkeyServiceTests
    {
        [Fact]
        public void RegisterHotkey_DoesNotQueueDuplicateAction_WhenWindowHandleIsNotReady()
        {
            using var logging = new LoggingService();
            var service = new HotkeyService(logging);

            var first = service.RegisterHotkey(HotkeyAction.ToggleWindow, ModifierKeys.Control | ModifierKeys.Shift, Key.O);
            var second = service.RegisterHotkey(HotkeyAction.ToggleWindow, ModifierKeys.Control | ModifierKeys.Shift, Key.O);

            first.Should().BeFalse();
            second.Should().BeTrue("duplicate action should be treated as already queued");
            GetPendingCount(service).Should().Be(1);
        }

        [Fact]
        public void RegisterHotkey_RejectsConflictingChord_ForDifferentAction_WhenWindowHandleIsNotReady()
        {
            using var logging = new LoggingService();
            var service = new HotkeyService(logging);

            var first = service.RegisterHotkey(HotkeyAction.ToggleWindow, ModifierKeys.Control | ModifierKeys.Shift, Key.O);
            var second = service.RegisterHotkey(HotkeyAction.ToggleFanMode, ModifierKeys.Control | ModifierKeys.Shift, Key.O);

            first.Should().BeFalse();
            second.Should().BeFalse("same key chord cannot map to two actions");
            GetPendingCount(service).Should().Be(1);
        }

        private static int GetPendingCount(HotkeyService service)
        {
            var field = typeof(HotkeyService).GetField("_pendingHotkeys", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            var pending = field!.GetValue(service) as List<HotkeyBinding>;
            pending.Should().NotBeNull();
            return pending!.Count;
        }
    }
}
