using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Pins the event subscription <see cref="ProcessMonitoringService"/> uses for game
    /// launch/exit detection to the extrinsic kernel trace classes.
    ///
    /// <para>Game detection was previously subscribed to the intrinsic
    /// <c>__InstanceCreationEvent</c> / <c>__InstanceDeletionEvent</c> classes over
    /// <c>Win32_Process</c> with <c>WITHIN 1</c>. Those classes have no notification source behind
    /// them: WMI satisfies them by enumerating the entire process table on the WITHIN interval and
    /// diffing consecutive snapshots. Subscribing to both ran that enumeration twice a second for
    /// as long as OmenCore was open, and it ran inside <c>WmiPrvSE.exe</c>, so it never showed up
    /// against OmenCore in Task Manager. Measured on 8D87 (Ryzen AI 9 HX 375, ~380 processes)
    /// against a 0.2% idle baseline: 15.6% of a core, continuously. The trace classes measured
    /// 0.0% while delivering the same notifications.</para>
    ///
    /// <para>These are string assertions rather than a live subscription on purpose — the trace
    /// classes need an elevated caller and a working WMI service, neither of which is guaranteed
    /// on a CI agent. What can regress silently is the query text, so that is what is pinned.</para>
    /// </summary>
    public class ProcessMonitoringEventQueryTests
    {
        [Fact]
        public void Start_Query_Uses_The_Extrinsic_Trace_Class()
        {
            ProcessMonitoringService.ProcessStartEventQuery
                .Should().Be("SELECT * FROM Win32_ProcessStartTrace");
        }

        [Fact]
        public void Stop_Query_Uses_The_Extrinsic_Trace_Class()
        {
            ProcessMonitoringService.ProcessStopEventQuery
                .Should().Be("SELECT * FROM Win32_ProcessStopTrace");
        }

        [Theory]
        [InlineData(ProcessMonitoringService.ProcessStartEventQuery)]
        [InlineData(ProcessMonitoringService.ProcessStopEventQuery)]
        public void Queries_Never_Poll_The_Process_Table(string query)
        {
            // WITHIN is what turns a subscription into a table-diffing poll; the intrinsic class
            // names are what require it. Either one reappearing reintroduces the 15.6% burn.
            query.Should().NotContainEquivalentOf("WITHIN");
            query.Should().NotContainEquivalentOf("__InstanceCreationEvent");
            query.Should().NotContainEquivalentOf("__InstanceDeletionEvent");
            query.Should().NotContainEquivalentOf("Win32_Process ");
        }

        [Theory]
        [InlineData("notepad.exe", "notepad")]
        [InlineData("RocketLeague.exe", "rocketleague")]
        [InlineData("Cyberpunk2077.exe", "cyberpunk2077")]
        public void Trace_ProcessName_Normalizes_The_Same_As_Win32_Process_Name(string traceName, string expected)
        {
            // Win32_ProcessStartTrace.ProcessName carries the same "foo.exe" form that
            // Win32_Process.Name did, so the tracked-name comparison needed no change when the
            // subscription moved. This is the assumption that swap rests on.
            ProcessMonitoringService.NormalizeProcessName(traceName).Should().Be(expected);
        }
    }
}
