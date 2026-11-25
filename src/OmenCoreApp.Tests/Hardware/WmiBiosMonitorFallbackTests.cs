using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class WmiBiosMonitorFallbackTests
    {
        [Theory]
        [InlineData(0, 30)]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(3, 120)]
        [InlineData(4, 240)]
        [InlineData(5, 300)]
        [InlineData(8, 300)]
        public void CpuFallbackTimeoutCooldown_UsesCappedBackoff(int timeoutStreak, int expectedSeconds)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "CalculateCpuFallbackReadCooldownSeconds",
                BindingFlags.NonPublic | BindingFlags.Static);

            method.Should().NotBeNull();
            var cooldownSeconds = (int)method!.Invoke(null, new object[] { timeoutStreak })!;

            cooldownSeconds.Should().Be(expectedSeconds);
        }

        [Theory]
        [InlineData(79, 30, true)]
        [InlineData(79, 300, true)]
        [InlineData(79, 301, false)]
        [InlineData(0, 30, false)]
        [InlineData(111, 30, false)]
        public void IsRecentWorkerCpuTemperatureUsable_OnlyAcceptsPlausibleRecentWorkerReadings(
            double cpuTemp,
            int ageSeconds,
            bool expected)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "IsRecentWorkerCpuTemperatureUsable",
                BindingFlags.NonPublic | BindingFlags.Static);

            method.Should().NotBeNull();

            var now = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
            var captured = now.AddSeconds(-ageSeconds);
            var usable = (bool)method!.Invoke(null, new object[] { cpuTemp, captured, now })!;

            usable.Should().Be(expected);
        }

        [Theory]
        [InlineData("OMEN by HP Gaming Laptop 16-n0xxx", true)]
        [InlineData("OMEN by HP Laptop 17-ck1xxx", true)]
        [InlineData("OMEN MAX Gaming Laptop 16t-ah000", true)]
        [InlineData("OMEN Laptop 15-en1xxx", true)] // #274, 88D2
        [InlineData("OMEN by HP Gaming Laptop 16-wf0xxx", false)]
        public void ShouldPreferWorkerCpuTemp_UsesModelScopedOverrideList(string model, bool expected)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "ShouldPreferWorkerCpuTemp",
                BindingFlags.NonPublic | BindingFlags.Static);

            method.Should().NotBeNull();
            var shouldPrefer = (bool)method!.Invoke(null, new object[] { model })!;

            shouldPrefer.Should().Be(expected);
        }

        [Theory]
        [InlineData(true, "LHM Worker Override")]
        [InlineData(false, "LHM Fallback")]
        public void CpuFallbackAuthority_PreservesWorkerOverrideIdentity(bool workerOverride, string expected)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "GetCpuFallbackAuthoritySource",
                BindingFlags.NonPublic | BindingFlags.Static);

            method.Should().NotBeNull();
            method!.Invoke(null, new object[] { workerOverride }).Should().Be(expected);
        }
    }
}
