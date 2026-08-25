using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    /// <summary>
    /// Tests for TrayIconService.BuildFanModeHeaderText — pure formatting logic,
    /// no WPF or hardware dependencies.
    /// </summary>
    public class TrayFanModeHeaderTests
    {
        [Fact]
        public void NoRequest_ShowsCurrentModeOnly()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", null, linked: false);
            header.Should().Be("Fan Mode: Auto");
        }

        [Fact]
        public void PendingRequest_DifferentFromCurrent_ShowsAnnotation()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", "Max", linked: false);
            header.Should().Be("Fan Mode: Auto (requested: Max)");
        }

        [Fact]
        public void PendingRequest_SameAsCurrent_NoAnnotation()
        {
            // Once confirmed, the tray should show no pending annotation even if
            // the pending field hasn't been cleared yet (OrdinalIgnoreCase comparison)
            var header = TrayIconService.BuildFanModeHeaderText("Max", "Max", linked: false);
            header.Should().Be("Fan Mode: Max");
        }

        [Fact]
        public void PendingRequest_SameAsCurrent_CaseInsensitive()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Quiet", "quiet", linked: false);
            header.Should().Be("Fan Mode: Quiet");
        }

        [Fact]
        public void LinkedMode_AppendsSuffix_WhenNoPendingRequest()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Performance", null, linked: true);
            header.Should().Be("Fan Mode: Performance [linked]");
        }

        [Fact]
        public void LinkedMode_AppendsSuffix_WithPendingRequest()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", "Quiet", linked: true);
            header.Should().Be("Fan Mode: Auto [linked] (requested: Quiet)");
        }

        [Fact]
        public void EmptyPendingRequest_TreatedAsNoPending()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", "", linked: false);
            header.Should().Be("Fan Mode: Auto");
        }

        [Fact]
        public void WhitespacePendingRequest_TreatedAsNoPending()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", "   ", linked: false);
            header.Should().Be("Fan Mode: Auto");
        }

        [Fact]
        public void PendingRequest_TrimmedBeforeComparison()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Quiet", "  quiet  ", linked: false);
            header.Should().Be("Fan Mode: Quiet");
        }

        [Fact]
        public void PendingRequest_TrimmedBeforeDisplay()
        {
            var header = TrayIconService.BuildFanModeHeaderText("Auto", "  Max  ", linked: false);
            header.Should().Be("Fan Mode: Auto (requested: Max)");
        }

        [Fact]
        public void EmptyCurrentMode_FallsBackToUnknown()
        {
            var header = TrayIconService.BuildFanModeHeaderText("   ", null, linked: false);
            header.Should().Be("Fan Mode: Unknown");
        }
    }
}
