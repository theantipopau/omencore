namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux HP WMI keyboard lighting controller.
/// 
/// Uses /sys/devices/platform/hp-wmi/* interface for controlling
/// the 4-zone RGB keyboard on HP OMEN laptops.
/// Per-key RGB models are detected but require USB HID protocol (not yet supported on Linux).
/// 
/// Requires hp-wmi kernel module:
///   modprobe hp-wmi
/// </summary>
public class LinuxKeyboardController
{
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
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
    public string KeyboardType => IsPerKeyRgb ? "Per-Key RGB" : "4-Zone";
    public int ZoneCount => IsPerKeyRgb ? 0 : 4;
    
    public LinuxKeyboardController()
    {
        IsAvailable = Directory.Exists(HP_WMI_PATH) || Directory.Exists(KEYBOARD_BACKLIGHT_PATH);
        HasZoneControl = File.Exists(Path.Combine(HP_WMI_PATH, "keyboard_zones"));
        IsPerKeyRgb = DetectPerKeyRgb();
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
            
            // Try HP WMI zone control (if available)
            if (HasZoneControl)
            {
                var zonePath = Path.Combine(HP_WMI_PATH, $"zone{zone}_color");
                if (File.Exists(zonePath))
                {
                    var colorValue = $"{r:X2}{g:X2}{b:X2}";
                    File.WriteAllText(zonePath, colorValue);
                    return true;
                }
            }
            
            // Alternative: Use keyboard backlight brightness as a proxy
            // This doesn't support full RGB but provides basic control
            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
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
            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
            var maxBrightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "max_brightness");
            
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
            var brightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "brightness");
            var maxBrightnessPath = Path.Combine(KEYBOARD_BACKLIGHT_PATH, "max_brightness");
            
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
