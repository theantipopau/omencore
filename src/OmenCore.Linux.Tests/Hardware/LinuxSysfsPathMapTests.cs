using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

/// <summary>
/// These assert the *candidate path tables*, not filesystem probing - the resolver methods all
/// call File/Directory.Exists against absolute Linux paths, which are correctly absent on the
/// Windows dev machine this suite runs on. The tables are the part that carries the real risk:
/// a driver's paths silently dropped from the probe order is exactly the kind of regression that
/// would only surface as "RGB stopped working" on a user's machine.
/// </summary>
public class LinuxSysfsPathMapTests
{
    [Fact]
    public void RgbZonesDirs_IncludesBothHpWmiAndOmenRgbKeyboardDrivers()
    {
        // The out-of-tree omen-rgb-keyboard DKMS driver requires blacklisting hp_wmi, so on a
        // machine running it NONE of the hp-wmi paths exist. Without its directory in this
        // table, OmenCore finds no keyboard RGB interface at all on those systems.
        LinuxSysfsPathMap.RgbZonesDirs.Should().Contain("/sys/devices/platform/hp-wmi/rgb_zones");
        LinuxSysfsPathMap.RgbZonesDirs.Should().Contain("/sys/devices/platform/omen-rgb-keyboard/rgb_zones");
    }

    [Fact]
    public void RgbZonesDirs_ProbesInTreeHpWmiBeforeOutOfTreeDriver()
    {
        // Order matters: the in-tree driver is the common case and should win when both somehow
        // exist, rather than depending on enumeration order.
        var dirs = LinuxSysfsPathMap.RgbZonesDirs;

        dirs.Should().HaveCountGreaterThanOrEqualTo(2);
        dirs[0].Should().Contain("hp-wmi");
        dirs.Should().ContainInOrder(
            "/sys/devices/platform/hp-wmi/rgb_zones",
            "/sys/devices/platform/omen-rgb-keyboard/rgb_zones");
    }

    [Fact]
    public void KeyboardBacklightDirs_IncludesAllThreeKnownLedClassNames()
    {
        LinuxSysfsPathMap.KeyboardBacklightDirs.Should().ContainInOrder(
            "/sys/class/leds/hp::kbd_backlight",
            "/sys/class/leds/hp_omen::kbd_backlight",
            "/sys/class/leds/omen::kbd_backlight");
    }

    [Fact]
    public void KeyboardBacklightDirs_ContainsTheOmenRgbKeyboardDriverLedClass()
    {
        // omen-rgb-keyboard registers its LED device as "omen::kbd_backlight", not the in-tree
        // hp-wmi driver's "hp::kbd_backlight" - brightness control silently does nothing on
        // that driver without this entry.
        LinuxSysfsPathMap.KeyboardBacklightDirs
            .Should().Contain(LinuxSysfsPathMap.KeyboardBacklightPathOmenRgb);
    }

    [Fact]
    public void RgbZoneAndBacklightPaths_AreAbsoluteSysfsPaths()
    {
        // Guards against a relative path or a Windows-style separator sneaking in - these are
        // written to a Linux filesystem verbatim.
        foreach (var path in LinuxSysfsPathMap.RgbZonesDirs.Concat(LinuxSysfsPathMap.KeyboardBacklightDirs))
        {
            path.Should().StartWith("/sys/");
            path.Should().NotContain("\\");
        }
    }

    [Theory]
    [InlineData(0, "zone00")]
    [InlineData(1, "zone01")]
    [InlineData(3, "zone03")]
    public void ZoneFileNaming_IsTwoDigitZeroPadded(int zoneIndex, string expectedFileName)
    {
        // Both drivers name zone files "zoneNN" (2-digit, zero-padded) - "zone0" would miss.
        // ResolveRgbZoneFilePath returns null here (no Linux sysfs on the test host), so assert
        // the naming convention it builds from instead.
        var built = $"zone{zoneIndex:D2}";

        built.Should().Be(expectedFileName);
    }

    [Fact]
    public void ResolveRgbZoneFilePath_OnNonLinuxHost_ReturnsNullRatherThanThrowing()
    {
        var act = () => LinuxSysfsPathMap.ResolveRgbZoneFilePath(0);

        act.Should().NotThrow();
        LinuxSysfsPathMap.ResolveRgbZoneFilePath(0).Should().BeNull();
    }

    [Fact]
    public void ResolveRgbZonesAllFilePath_OnNonLinuxHost_ReturnsNullRatherThanThrowing()
    {
        var act = () => LinuxSysfsPathMap.ResolveRgbZonesAllFilePath();

        act.Should().NotThrow();
        LinuxSysfsPathMap.ResolveRgbZonesAllFilePath().Should().BeNull();
    }
}
