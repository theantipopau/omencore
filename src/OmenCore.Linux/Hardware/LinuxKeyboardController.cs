namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux HP WMI keyboard lighting controller.
///
/// Uses /sys/devices/platform/hp-wmi/* interface for controlling
/// the 4-zone RGB keyboard on HP OMEN laptops.
/// Per-key RGB models are detected but require USB HID protocol (not yet supported on Linux).
///
/// Some boards (confirmed on OMEN Max 16-ah0xxx, board 8D41, GitHub #151) expose
/// zone control via a separate hp-rgb-lighting platform device instead of hp-wmi,
/// with plain "zoneN" filenames (no "_color" suffix). Reporter confirmed these zones
/// are writable via raw sysfs even though hp-wmi's zoneN_color path doesn't exist on
/// that board.
///
/// Requires hp-wmi kernel module (or hp-rgb-lighting for boards that use it):
///   modprobe hp-wmi
/// </summary>
public class LinuxKeyboardController
{
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
    private const string HP_RGB_LIGHTING_PATH = "/sys/devices/platform/hp-rgb-lighting";
    private const string KEYBOARD_BACKLIGHT_PATH = "/sys/class/leds/hp::kbd_backlight";
    private const string DMI_PRODUCT_NAME_PATH = "/sys/class/dmi/id/product_name";
    
    /// <summary>
    /// Model substrings known to have per-key RGB keyboards.
    /// Sourced from the Windows KeyboardModelDatabase.
    /// </summary>
    private static readonly string[] PerKeyModelPatterns = new[]
    {
        "16-wf0",     // OMEN 16 (2024) per-key
        "16-wf1",     // OMEN 16 (2025) per-key
        "16t-wf0",    // OMEN 16t (2024) per-key
        "16t-wf1",    // OMEN 16t (2025) per-key
        "17-wf0",     // OMEN 17 (2024) per-key
        "17-wf1",     // OMEN 17 (2025) per-key
        "17t-wf0",    // OMEN 17t (2024) per-key
        "16t-ah0",    // OMEN Max 16 (2025) per-key
        "16-ah0",     // OMEN Max 16 (2025) per-key
        "17t-ah0",    // OMEN Max 17 (2025) per-key
        "Transcend 14", // OMEN Transcend 14 per-key
        "Transcend 16", // OMEN Transcend 16 per-key
    };
    
    public bool IsAvailable { get; }
    public bool HasZoneControl { get; }
    public bool IsPerKeyRgb { get; }
    public bool SupportsBrightnessControl => File.Exists(Path.Combine(ResolveBacklightDirectory(), "brightness"));
    public string KeyboardType => IsPerKeyRgb ? "Per-Key RGB" : "4-Zone";
    public int ZoneCount => IsPerKeyRgb ? 0 : 4;

    /// <summary>
    /// Resolves the keyboard-backlight LED class directory, trying "hp::kbd_backlight" first
    /// and falling back to the underscore-variant "hp_omen::kbd_backlight" seen on some
    /// kernel/driver combinations. Falls back to the primary path (whether or not it exists)
    /// so callers always get a non-null path to build Path.Combine calls against.
    /// </summary>
    private static string ResolveBacklightDirectory() =>
        LinuxSysfsPathMap.ResolveKeyboardBacklightDirectory() ?? KEYBOARD_BACKLIGHT_PATH;

    public LinuxKeyboardController()
    {
        IsAvailable = Directory.Exists(HP_WMI_PATH) || Directory.Exists(KEYBOARD_BACKLIGHT_PATH)
            || Directory.Exists(HP_RGB_LIGHTING_PATH)
            || Directory.Exists(LinuxSysfsPathMap.KeyboardBacklightPathAlt);
        HasZoneControl = File.Exists(Path.Combine(HP_WMI_PATH, "keyboard_zones")) || HasRgbLightingZoneFiles()
            || LinuxSysfsPathMap.HasRgbZonesDir;
        IsPerKeyRgb = DetectPerKeyRgb();
    }

    /// <summary>
    /// Some boards expose zone files directly under hp-rgb-lighting with no
    /// separate "keyboard_zones" capability flag - detect by probing for zone0.
    /// </summary>
    private static bool HasRgbLightingZoneFiles()
    {
        return File.Exists(Path.Combine(HP_RGB_LIGHTING_PATH, "zone0"));
    }
    
    /// <summary>
    /// Detect if this model has a per-key RGB keyboard based on DMI product name.
    /// </summary>
    private static bool DetectPerKeyRgb()
    {
        try
        {
            if (!File.Exists(DMI_PRODUCT_NAME_PATH))
                return false;
                
            var productName = File.ReadAllText(DMI_PRODUCT_NAME_PATH).Trim();
            foreach (var pattern in PerKeyModelPatterns)
            {
                if (productName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
    
    /// <summary>
    /// Set color for a specific zone (0-3).
    /// </summary>
    public bool SetZoneColor(int zone, byte r, byte g, byte b)
    {
        if (!IsAvailable || zone < 0 || zone > 3)
            return false;
            
        try
        {
            // HP OMEN keyboard lighting is complex - zones may be controlled via WMI
            // This implementation uses a simplified approach based on available interfaces
            var colorValue = $"{r:X2}{g:X2}{b:X2}";

            // Try HP WMI zone control (if available)
            var zonePath = Path.Combine(HP_WMI_PATH, $"zone{zone}_color");
            if (File.Exists(zonePath))
            {
                File.WriteAllText(zonePath, colorValue);
                return true;
            }

            // Some boards (e.g. board 8D41 / OMEN Max 16-ah0xxx) expose zones via a
            // separate hp-rgb-lighting platform device instead, with a plain "zoneN"
            // filename (no "_color" suffix) - try that path before falling back.
            var rgbLightingZonePath = Path.Combine(HP_RGB_LIGHTING_PATH, $"zone{zone}");
            if (File.Exists(rgbLightingZonePath))
            {
                File.WriteAllText(rgbLightingZonePath, colorValue);
                return true;
            }

            // A third, distinct interface: hp-wmi's own "rgb_zones" directory, using 2-digit
            // zero-padded filenames ("zone00".."zone03" - not the same file as either check
            // above). Source: openomen (GPLv3) - documented sysfs path, reimplemented
            // independently; see CHANGELOG_v4.1.7.md.
            var rgbZonePath = LinuxSysfsPathMap.ResolveRgbZoneFilePath(zone);
            if (rgbZonePath != null)
            {
                File.WriteAllText(rgbZonePath, colorValue);
                return true;
            }

            // Alternative: Use keyboard backlight brightness as a proxy
            // This doesn't support full RGB but provides basic control
            var brightnessPath = Path.Combine(ResolveBacklightDirectory(), "brightness");
            if (File.Exists(brightnessPath))
            {
                // Calculate brightness from RGB (0-255 average)
                var brightness = (r + g + b) / 3;
                File.WriteAllText(brightnessPath, brightness.ToString());
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes all 4 zones' colors concatenated into a single 24-char hex string to
    /// <see cref="LinuxSysfsPathMap.HpWmiKeyboardLedsPath"/> (the legacy single-file 4-zone
    /// interface). Returns false when the file doesn't exist, so callers can fall through.
    /// </summary>
    private static bool TryWriteKeyboardLedsFile(byte r, byte g, byte b)
    {
        if (!LinuxSysfsPathMap.HasKeyboardLedsFile)
        {
            return false;
        }

        try
        {
            var colorValue = $"{r:X2}{g:X2}{b:X2}";
            File.WriteAllText(LinuxSysfsPathMap.HpWmiKeyboardLedsPath, colorValue + colorValue + colorValue + colorValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a 12-byte binary payload (4 zones x RGB) to the "hp_omen::kbd_backlight/zone_colors"
    /// LED class file. Returns false when that file doesn't exist.
    /// </summary>
    private static bool TryWriteZoneColorsBinary(byte r, byte g, byte b)
    {
        var path = Path.Combine(LinuxSysfsPathMap.KeyboardBacklightPathAlt, "zone_colors");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var binaryData = new byte[12];
            for (var zone = 0; zone < 4; zone++)
            {
                binaryData[zone * 3] = r;
                binaryData[zone * 3 + 1] = g;
                binaryData[zone * 3 + 2] = b;
            }

            File.WriteAllBytes(path, binaryData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Set the same color for all zones.
    /// </summary>
    public bool SetAllZonesColor(byte r, byte g, byte b)
    {
        if (!IsAvailable)
            return false;
            
        // Try setting each zone
        bool anySuccess = false;
        for (int i = 0; i < 4; i++)
        {
            if (SetZoneColor(i, r, g, b))
                anySuccess = true;
        }

        // Two more all-4-zones-in-one-write interfaces, tried before falling back to the
        // brightness-only proxy. Source: openomen (GPLv3) - documented sysfs paths/wire
        // formats, reimplemented independently; see CHANGELOG_v4.1.7.md.
        if (!anySuccess && TryWriteKeyboardLedsFile(r, g, b))
        {
            anySuccess = true;
        }

        if (!anySuccess && TryWriteZoneColorsBinary(r, g, b))
        {
            anySuccess = true;
        }

        // If zone control didn't work, try global brightness
        if (!anySuccess)
        {
            return SetBrightness((r + g + b) / 3 * 100 / 255);
        }
        
        return anySuccess;
    }
    
    /// <summary>
    /// Set keyboard backlight brightness (0-100).
    /// </summary>
    public bool SetBrightness(int percent)
    {
        if (!IsAvailable)
            return false;

        try
        {
            var backlightDir = ResolveBacklightDirectory();
            var brightnessPath = Path.Combine(backlightDir, "brightness");
            var maxBrightnessPath = Path.Combine(backlightDir, "max_brightness");

            if (!File.Exists(brightnessPath))
                return false;

            int maxBrightness = 3; // Default for many HP laptops
            if (File.Exists(maxBrightnessPath))
            {
                var maxContent = File.ReadAllText(maxBrightnessPath).Trim();
                int.TryParse(maxContent, out maxBrightness);
                if (maxBrightness == 0) maxBrightness = 3;
            }

            var brightness = Math.Clamp(percent * maxBrightness / 100, 0, maxBrightness);
            File.WriteAllText(brightnessPath, brightness.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetBrightnessUnavailableReason()
    {
        if (!IsAvailable)
        {
            return "HP WMI keyboard interface is not available.";
        }

        var brightnessPath = Path.Combine(ResolveBacklightDirectory(), "brightness");
        if (!File.Exists(brightnessPath))
        {
            return $"Brightness sysfs path not found: {brightnessPath}";
        }

        return "Unknown keyboard brightness error.";
    }
    
    /// <summary>
    /// Turn off keyboard lighting.
    /// </summary>
    public bool TurnOff()
    {
        return SetBrightness(0);
    }
    
    /// <summary>
    /// Get current brightness level (0-100).
    /// </summary>
    public int GetBrightness()
    {
        try
        {
            var backlightDir = ResolveBacklightDirectory();
            var brightnessPath = Path.Combine(backlightDir, "brightness");
            var maxBrightnessPath = Path.Combine(backlightDir, "max_brightness");
            
            if (!File.Exists(brightnessPath))
                return 0;
                
            var content = File.ReadAllText(brightnessPath).Trim();
            if (!int.TryParse(content, out var brightness))
                return 0;
                
            int maxBrightness = 3;
            if (File.Exists(maxBrightnessPath))
            {
                var maxContent = File.ReadAllText(maxBrightnessPath).Trim();
                int.TryParse(maxContent, out maxBrightness);
                if (maxBrightness == 0) maxBrightness = 3;
            }
            
            return brightness * 100 / maxBrightness;
        }
        catch
        {
            return 0;
        }
    }
}
