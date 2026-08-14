namespace OmenCore.Linux.Hardware;

/// <summary>
/// Centralized Linux sysfs path normalization for hp-wmi and ACPI capability probing.
/// </summary>
public static class LinuxSysfsPathMap
{
    public const string EcIoPath = "/sys/kernel/debug/ec/ec0/io";
    public const string HpWmiRoot = "/sys/devices/platform/hp-wmi";
    public const string HpWmiHwmonRoot = "/sys/devices/platform/hp-wmi/hwmon";
    public const string AcpiPlatformProfilePath = "/sys/firmware/acpi/platform_profile";
    public const string AcpiPlatformProfileChoicesPath = "/sys/firmware/acpi/platform_profile_choices";
    public const string KeyboardBacklightPath = "/sys/class/leds/hp::kbd_backlight";

    /// <summary>
    /// Alternate keyboard-backlight LED class name seen on some kernel/driver combinations
    /// (underscore "hp_omen::" rather than "hp::"). Neither this nor <see cref="KeyboardBacklightPath"/>
    /// is documented anywhere authoritative; both are corroborated only by community tooling
    /// (openomen), so treat as an additional candidate, not a replacement.
    /// </summary>
    public const string KeyboardBacklightPathAlt = "/sys/class/leds/hp_omen::kbd_backlight";

    /// <summary>
    /// 4-zone RGB keyboard control directory, distinct from both the WMI-driven "zoneN_color"
    /// files (<see cref="HpWmiRoot"/>) and the hp-rgb-lighting platform device's plain "zoneN"
    /// files. Each zone is its own file, named "zone00".."zone03" (2-digit, zero-padded),
    /// written as a plain 6-hex-char string with no "#" prefix. Source: openomen (GPLv3) -
    /// this is a documented sysfs path/wire format, not copied code; see
    /// docs/CHANGELOG_v4.1.7.md for the corroboration trail.
    /// </summary>
    public const string HpWmiRgbZonesDir = "/sys/devices/platform/hp-wmi/rgb_zones";

    /// <summary>
    /// Legacy single-file 4-zone keyboard control: one write of all 4 zones' colors
    /// concatenated as a 24-char hex string (zone0 first). Source: openomen (GPLv3), same
    /// corroboration note as <see cref="HpWmiRgbZonesDir"/>.
    /// </summary>
    public const string HpWmiKeyboardLedsPath = "/sys/devices/platform/hp-wmi/keyboardleds";

    public static readonly string[] ThermalProfilePaths =
    {
        "/sys/firmware/acpi/platform_profile",
        "/sys/devices/platform/hp-wmi/thermal_profile",
        "/sys/devices/platform/hp-wmi/thermal-profile",
        "/sys/devices/platform/hp-wmi/platform_profile",
        "/sys/devices/platform/hp-wmi/platform-profile",
        "/sys/devices/platform/hp-wmi/performance_profile",
        "/sys/devices/platform/hp-wmi/performance-profile"
    };

    public static readonly string[] ThermalProfileChoicePaths =
    {
        "/sys/firmware/acpi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform-profile-choices",
        "/sys/devices/platform/hp-wmi/thermal_profile_choices",
        "/sys/devices/platform/hp-wmi/thermal-profile-choices"
    };

    public static readonly string[] PlatformProfilePaths =
    {
        "/sys/devices/platform/hp-wmi/platform_profile",
        "/sys/devices/platform/hp-wmi/platform-profile"
    };

    public static readonly string[] HpWmiThermalProfilePaths =
    {
        "/sys/devices/platform/hp-wmi/thermal_profile",
        "/sys/devices/platform/hp-wmi/thermal-profile"
    };

    public static readonly string[] HpWmiPerformanceProfilePaths =
    {
        "/sys/devices/platform/hp-wmi/performance_profile",
        "/sys/devices/platform/hp-wmi/performance-profile"
    };

    public static readonly string[] HpWmiProfilePaths =
    {
        "/sys/devices/platform/hp-wmi/thermal_profile",
        "/sys/devices/platform/hp-wmi/thermal-profile",
        "/sys/devices/platform/hp-wmi/platform_profile",
        "/sys/devices/platform/hp-wmi/platform-profile",
        "/sys/devices/platform/hp-wmi/performance_profile",
        "/sys/devices/platform/hp-wmi/performance-profile"
    };

    public static readonly string[] HpWmiPlatformProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform-profile-choices"
    };

    public static readonly string[] HpWmiThermalProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/thermal_profile_choices",
        "/sys/devices/platform/hp-wmi/thermal-profile-choices"
    };

    public static readonly string[] HpWmiPerformanceProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/performance_profile_choices",
        "/sys/devices/platform/hp-wmi/performance-profile-choices"
    };

    public static readonly string[] HpWmiProfileChoicePaths =
    {
        "/sys/devices/platform/hp-wmi/thermal_profile_choices",
        "/sys/devices/platform/hp-wmi/thermal-profile-choices",
        "/sys/devices/platform/hp-wmi/platform_profile_choices",
        "/sys/devices/platform/hp-wmi/platform-profile-choices",
        "/sys/devices/platform/hp-wmi/performance_profile_choices",
        "/sys/devices/platform/hp-wmi/performance-profile-choices"
    };

    public static string? ResolveFirstExistingFile(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? ResolveThermalProfilePath() => ResolveFirstExistingFile(ThermalProfilePaths);

    public static string? ResolveThermalProfileChoicesPath() => ResolveFirstExistingFile(ThermalProfileChoicePaths);

    public static bool AnyPathExists(IEnumerable<string> candidates) => candidates.Any(File.Exists);

    public static IEnumerable<string> EnumerateHpWmiHwmonDirectories()
    {
        if (!Directory.Exists(HpWmiHwmonRoot))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetDirectories(HpWmiHwmonRoot, "hwmon*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string? ResolveHpWmiFanTargetPath(int fanIndex)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"fan{fanIndex}_target");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiFanTarget(int fanIndex) => ResolveHpWmiFanTargetPath(fanIndex) != null;

    public static string? ResolveHpWmiPwmEnablePath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"pwm{index}_enable");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiPwmEnable(int index) => ResolveHpWmiPwmEnablePath(index) != null;

    public static string? ResolveHpWmiPwmPath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"pwm{index}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiPwm(int index) => ResolveHpWmiPwmPath(index) != null;

    public static string? ResolveHpWmiFanInputPath(int index)
    {
        foreach (var hwmonDir in EnumerateHpWmiHwmonDirectories())
        {
            var candidate = Path.Combine(hwmonDir, $"fan{index}_input");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasHpWmiFanInput(int index) => ResolveHpWmiFanInputPath(index) != null;

    /// <summary>
    /// Resolves the keyboard-backlight LED class directory, trying <see cref="KeyboardBacklightPath"/>
    /// first and falling back to the underscore-variant class name
    /// (<see cref="KeyboardBacklightPathAlt"/>) seen on some kernel/driver combinations.
    /// </summary>
    public static string? ResolveKeyboardBacklightDirectory()
    {
        if (Directory.Exists(KeyboardBacklightPath))
        {
            return KeyboardBacklightPath;
        }

        if (Directory.Exists(KeyboardBacklightPathAlt))
        {
            return KeyboardBacklightPathAlt;
        }

        return null;
    }

    /// <summary>
    /// Resolves the file for one of the 4 <see cref="HpWmiRgbZonesDir"/> zone files
    /// ("zone00".."zone03"), or null if the directory or that specific zone file doesn't exist.
    /// </summary>
    public static string? ResolveRgbZoneFilePath(int zoneIndex)
    {
        var candidate = Path.Combine(HpWmiRgbZonesDir, $"zone{zoneIndex:D2}");
        return File.Exists(candidate) ? candidate : null;
    }

    public static bool HasRgbZonesDir => Directory.Exists(HpWmiRgbZonesDir);

    public static bool HasKeyboardLedsFile => File.Exists(HpWmiKeyboardLedsPath);
}
