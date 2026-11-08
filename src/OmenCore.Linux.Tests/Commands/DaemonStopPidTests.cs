using FluentAssertions;
using OmenCore.Linux.Commands;

namespace OmenCore.Linux.Tests.Commands;

public class DaemonStopPidTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1")]            // init is never ours, whatever its cmdline says
    [InlineData("2147483646")]   // no such process
    public void IsOmenCoreProcess_RejectsAnythingThatIsNotARunningOmenCoreBinary(string pidText)
    {
        DaemonCommand.IsOmenCoreProcess(pidText).Should().BeFalse();
    }
}
