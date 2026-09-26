using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

/// <summary>
/// GitHub #214: a dead acpitz zone stuck at +20.0°C was chosen over k10temp purely by sysfs
/// enumeration order. Dedicated CPU drivers must always outrank generic ACPI thermal zones.
/// </summary>
public class LinuxCpuSensorRankTests
{
    [Theory]
    [InlineData("k10temp")]
    [InlineData("coretemp")]
    [InlineData("zenpower")]
    [InlineData("x86_pkg_temp")]
    [InlineData("hp")]
    public void DedicatedAndVendorSensors_OutrankAcpitz(string sensor)
    {
        LinuxHwMonController.GetCpuSensorRank(sensor)
            .Should().BeLessThan(LinuxHwMonController.GetCpuSensorRank("acpitz"));
    }

    [Fact]
    public void DieSensorDrivers_AreTopRank()
    {
        LinuxHwMonController.GetCpuSensorRank("k10temp").Should().Be(0);
        LinuxHwMonController.GetCpuSensorRank("coretemp").Should().Be(0);
    }

    [Fact]
    public void Acpitz_IsLastResort_EvenBehindGenericCpuZones()
    {
        LinuxHwMonController.GetCpuSensorRank("acpitz")
            .Should().BeGreaterThan(LinuxHwMonController.GetCpuSensorRank("cpu-thermal"));
    }

    [Fact]
    public void Ranking_IsCaseAndWhitespaceInsensitive()
    {
        LinuxHwMonController.GetCpuSensorRank(" K10TEMP\n").Should().Be(0);
    }
}
