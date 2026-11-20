using System;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// #177: between NVAPI refreshes the BIOS GPU sensor must not overwrite the die reading,
    /// or the GPU temperature flips between the two sources on battery.
    /// </summary>
    public class GpuTempSourceFreshnessTests
    {
        private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Battery = TimeSpan.FromSeconds(10);

        [Fact]
        public void NeverReadFromNvapi_UsesBiosSensor()
        {
            WmiBiosMonitor.NvapiGpuTempIsFresh(DateTime.MinValue, Now, Battery).Should().BeFalse();
        }

        [Theory]
        [InlineData(2, true)]    // between two battery refreshes: keep the die reading
        [InlineData(14, true)]   // refresh due, poll slightly late
        [InlineData(16, false)]  // NVAPI missed a refresh: fall back to the BIOS sensor
        public void OnBattery_DieReadingHeldUntilNextRefreshIsDue(int secondsAgo, bool fresh)
        {
            WmiBiosMonitor.NvapiGpuTempIsFresh(Now.AddSeconds(-secondsAgo), Now, Battery).Should().Be(fresh);
        }

        [Fact]
        public void OnAc_DieReadingGoesStaleQuickly()
        {
            // On AC NVAPI is read every poll, so a reading 6 s old means it stopped answering.
            WmiBiosMonitor.NvapiGpuTempIsFresh(Now.AddSeconds(-6), Now, TimeSpan.Zero).Should().BeFalse();
            WmiBiosMonitor.NvapiGpuTempIsFresh(Now.AddSeconds(-2), Now, TimeSpan.Zero).Should().BeTrue();
        }
    }
}
