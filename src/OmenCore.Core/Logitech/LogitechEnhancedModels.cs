using System;
using System.Collections.Generic;

namespace OmenCore.Logitech
{
    /// <summary>
    /// Enhanced Logitech device capabilities with advanced features
    /// </summary>
    public class LogitechDeviceCapabilities
    {
        public bool SupportsDpiAdjustment { get; set; }
        public bool SupportsRgbLighting { get; set; }
        public bool SupportsButtonRemapping { get; set; }
        public bool SupportsOnboardMemory { get; set; }
        public bool SupportsMacros { get; set; }
        public bool SupportsBattery { get; set; }
        public int MaxDpi { get; set; }
        public int MinDpi { get; set; }
        public int DpiStep { get; set; } = 50;
        public int MaxMacroDuration { get; set; } // milliseconds
    }

    /// <summary>
    /// DPI configuration for Logitech devices
    /// </summary>
    public class LogitechDpiConfig
    {
        public int DpiValue { get; set; }
        public string ColorHex { get; set; } = "#FFFFFF";
        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// Button remapping configuration
    /// </summary>
    public class LogitechButtonMapping
    {
        public int ButtonId { get; set; }
        public string ButtonName { get; set; } = string.Empty;
        public LogitechButtonAction Action { get; set; }
        public string ActionParameter { get; set; } = string.Empty; // Key combo, macro name, etc.
    }

    public enum LogitechButtonAction
    {
        Default,
        Disabled,
        KeyCombo,
        Macro,
        MediaControl,
        DpiCycle,
        ProfileSwitch
    }

    /// <summary>
    /// Advanced lighting effect for Logitech RGB devices
    /// </summary>
    public class LogitechLightingEffect
    {
        public LogitechEffectType Type { get; set; }
        public string PrimaryColorHex { get; set; } = "#E6002E";
        public string SecondaryColorHex { get; set; } = "#1FC3FF";
        public int Speed { get; set; } = 100; // 0-200, 100 = normal
        public int Brightness { get; set; } = 100; // 0-100
        public List<string> ColorCycle { get; set; } = new List<string>();
    }

    public enum LogitechEffectType
    {
        Static,
        Breathing,
        ColorCycle,
        Wave,
        Ripple,
        ScreenSampler,
        AudioVisualizer
    }

    /// <summary>
    /// Device profile supporting multiple configurations
    /// </summary>
    public class LogitechDeviceProfile
    {
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Default Profile";
        public List<LogitechDpiConfig> DpiStages { get; set; } = new List<LogitechDpiConfig>();
        public List<LogitechButtonMapping> ButtonMappings { get; set; } = new List<LogitechButtonMapping>();
        public LogitechLightingEffect? LightingEffect { get; set; }
        public int PollingRate { get; set; } = 1000; // Hz
        public bool IsActive { get; set; }
    }
}
