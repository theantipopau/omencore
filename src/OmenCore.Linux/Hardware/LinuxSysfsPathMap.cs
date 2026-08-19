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
    /// Keyboard-backlight LED class registered by the out-of-tree <c>omen-rgb-keyboard</c> DKMS
    /// driver (github.com/OmenLinux/omen-rgb-keyboard, GPLv3), which names its LED device
    /// "omen::kbd_backlight" rather than the in-tree hp-wmi driver's "hp::kbd_backlight".
    /// That driver is intended to be used *instead of* hp_wmi (its install instructions
    /// blacklist hp_wmi, since both drive the same WMI interface and conflict), so on a machine
    /// running it none of the hp-wmi paths above exist at all. Path/name only - no code from
    /// that project is used here.
    /// </summary>
    public const string KeyboardBacklightPathOmenRgb = "/sys/class/leds/omen::kbd_backlight";

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
    /// The same "rgb_zones/zoneNN" directory layout as <see cref="HpWmiRgbZonesDir"/>, but
    /// registered under the out-of-tree <c>omen-rgb-keyboard</c> DKMS driver's own platform
    /// device rather than hp-wmi's. Its README documents this exact interface
    /// (<c>rgb_zones/zone00</c>..<c>zone03</c>, plus <c>all</c> and <c>brightness</c>, each
    /// written as a plain 6-hex-char string) and its tested-hardware list includes boards this
    /// project already tracks from field reports - 16-wf0xxx (`8BCA`) and 16-wd0xxx (`8BA9`).
    /// Because that driver requires blacklisting hp_wmi, a machine running it exposes none of
    /// the hp-wmi paths, so without this candidate OmenCore's Linux keyboard control finds
    /// nothing at all there. Documented sysfs path and wire format only - no code adopted (that
    /// project is GPLv3; this project is MIT).
    /// </summary>
    public const string OmenRgbKeyboardRgbZonesDir = "/sys/devices/platform/omen-rgb-keyboard/rgb_zones";

    /// <summary>
    /// All known "rgb_zones" style 4-zone directories, in probe order: the hp-wmi-hosted one
    /// first (in-tree driver, the common case), then the out-of-tree omen-rgb-keyboard driver's.
    /// </summary>
    public static readonly string[] RgbZonesDirs =
    {
        HpWmiRgbZonesDir,
        OmenRgbKeyboardRgbZonesDir
    };

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
        foreach (var candidate in KeyboardBacklightDirs)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Keyboard-backlight LED class directories, in probe order: in-tree hp-wmi naming first,
    /// then the underscore variant, then the out-of-tree omen-rgb-keyboard driver's.
    /// </summary>
    public static readonly string[] KeyboardBacklightDirs =
    {
        KeyboardBacklightPath,
        KeyboardBacklightPathAlt,
        KeyboardBacklightPathOmenRgb
    };

    /// <summary>
    /// Resolves the file for one of the 4 zone files ("zone00".."zone03") in whichever
    /// <see cref="RgbZonesDirs"/> directory exists on this system, or null if none do.
    /// </summary>
    public static string? ResolveRgbZoneFilePath(int zoneIndex)
    {
        foreach (var dir in RgbZonesDirs)
        {
            var candidate = Path.Combine(dir, $"zone{zoneIndex:D2}");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the "all zones at once" file in whichever <see cref="RgbZonesDirs"/> directory
    /// exists, or null if none do. Writing one 6-hex-char color here sets every zone in a single
    /// write instead of four - fewer WMI round-trips for the common "set the whole keyboard to
    /// one color" case.
    /// </summary>
    public static string? ResolveRgbZonesAllFilePath()
    {
        foreach (var dir in RgbZonesDirs)
        {
            var candidate = Path.Combine(dir, "all");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool HasRgbZonesDir => RgbZonesDirs.Any(Directory.Exists);

    public static bool HasKeyboardLedsFile => File.Exists(HpWmiKeyboardLedsPath);
}
