using System.Collections.Generic;
using OmenCore.Corsair;
using OmenCore.Services;

namespace OmenCore.Models
{
    public class AppConfig
    {
        public string EcDevicePath { get; set; } = @"\\.\WinRing0_1_2";
        public int MonitoringIntervalMs { get; set; } = 1000;
        public List<FanPreset> FanPresets { get; set; } = new();
        public List<PerformanceMode> PerformanceModes { get; set; } = new();
        public List<ServiceToggle> SystemToggles { get; set; } = new();
        public List<LightingProfile> LightingProfiles { get; set; } = new();
        public List<CorsairLightingPreset> CorsairLightingPresets { get; set; } = new();
        public List<CorsairDpiStage> DefaultCorsairDpi { get; set; } = new();
        public List<MacroProfile> MacroProfiles { get; set; } = new();
        public Dictionary<string, int> EcFanRegisterMap { get; set; } = new();
        public UndervoltPreferences Undervolt { get; set; } = new();
        public MonitoringPreferences Monitoring { get; set; } = new();
        public UpdatePreferences Updates { get; set; } = new();
        public bool FirstRunCompleted { get; set; } = false;
        
        /// <summary>
        /// Enable detailed diagnostics logging at startup (OGH commands, WMI status, etc.)
        /// </summary>
        public bool EnableDiagnostics { get; set; } = false;
        
        /// <summary>
        /// Logging verbosity level. Options: Error, Warning, Info, Debug.
        /// Default is Info. Set to Warning for less verbose logs.
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Info;
        
        /// <summary>
        /// Keep the main window always on top of other windows.
        /// </summary>
        public bool StayOnTop { get; set; } = false;
        
        /// <summary>
        /// Last applied performance mode name (e.g., "Balanced", "Performance", "Quiet")
        /// Restored on startup.
        /// </summary>
        public string? LastPerformanceModeName { get; set; }
        
        /// <summary>
        /// Last applied GPU Power Boost level ("Minimum", "Medium", "Maximum").
        /// Note: GPU power settings may reset after sleep/reboot on some OMEN models due to BIOS behavior.
        /// </summary>
        public string? LastGpuPowerBoostLevel { get; set; }
        
        /// <summary>
        /// Last applied fan preset name for restoration on startup.
        /// </summary>
        public string? LastFanPresetName { get; set; }
        
        /// <summary>
        /// Power automation settings for AC/Battery profile switching.
        /// </summary>
        public PowerAutomationSettings? PowerAutomation { get; set; }
    }
    
    /// <summary>
    /// Settings for automatic profile switching based on power source.
    /// </summary>
    public class PowerAutomationSettings
    {
        public bool Enabled { get; set; } = false;
        
        // AC Power settings
        public string AcFanPreset { get; set; } = "Auto";
        public string AcPerformanceMode { get; set; } = "Balanced";
        public string AcGpuMode { get; set; } = "Hybrid";
        
        // Battery settings
        public string BatteryFanPreset { get; set; } = "Quiet";
        public string BatteryPerformanceMode { get; set; } = "Silent";
        public string BatteryGpuMode { get; set; } = "Eco";
    }
}
