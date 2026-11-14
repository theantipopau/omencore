using FluentAssertions;
using OmenCore.Linux.Commands;

namespace OmenCore.Linux.Tests.Commands;

public class NvidiaPowerdHintTests
{
    [Theory]
    [InlineData("inactive")]
    [InlineData("failed")]
    [InlineData(null)]
    public void NvidiaLaptop_WithoutPowerd_GetsTheHint(string? state)
    {
        // GitHub #123: 8D41 sat at 80 W in every profile whenever nvidia-powerd was stopped.
        DiagnoseCommand.NvidiaPowerdHint(true, state).Should().Contain("systemctl enable --now nvidia-powerd");
    }

    [Fact]
    public void RunningPowerd_OrNoNvidiaGpu_GetsNoHint()
    {
        DiagnoseCommand.NvidiaPowerdHint(true, "active\n").Should().BeNull();
        DiagnoseCommand.NvidiaPowerdHint(false, null).Should().BeNull();
    }
}
