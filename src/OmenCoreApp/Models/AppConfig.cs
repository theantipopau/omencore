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
        public FeaturePreferences Features { get; set; } = new();
        public FanHysteresisSettings FanHysteresis { get; set; } = new();
        public OsdSettings Osd { get; set; } = new();
        public BatterySettings Battery { get; set; } = new();
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
        
        // OMEN Key Interception settings
        /// <summary>Enable OMEN key interception to prevent OGH from launching</summary>
        public bool OmenKeyEnabled { get; set; } = false;
        
        /// <summary>Whether to block the key (true) or let it pass through (false)</summary>
        public bool OmenKeyIntercept { get; set; } = true;
        
        /// <summary>Action to perform when OMEN key is pressed</summary>
        public string OmenKeyAction { get; set; } = "ToggleOmenCore";
        
        /// <summary>Path to external app when OmenKeyAction is LaunchExternalApp</summary>
        public string? OmenKeyExternalApp { get; set; }
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
    
    /// <summary>
    /// Fan hysteresis settings to prevent fan speed oscillation.
    /// </summary>
    public class FanHysteresisSettings
    {
        /// <summary>Enable hysteresis (dead-zone) to prevent fan oscillation</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Temperature dead-zone in °C. Fans won't change unless temp moves beyond this threshold.</summary>
        public double DeadZone { get; set; } = 3.0;
        
        /// <summary>Ramp-up delay in seconds before increasing fan speed.</summary>
        public double RampUpDelay { get; set; } = 0.5;
        
        /// <summary>Ramp-down delay in seconds before decreasing fan speed.</summary>
        public double RampDownDelay { get; set; } = 3.0;
        
        /// <summary>Enable thermal protection override - forces max fans when temps exceed 90°C, even in Auto mode.</summary>
        public bool ThermalProtectionEnabled { get; set; } = true;
    }
    
    /// <summary>
    /// In-game OSD overlay settings.
    /// </summary>
    public class OsdSettings
    {
        /// <summary>Master toggle - when disabled, no OSD process runs at all</summary>
        public bool Enabled { get; set; } = false;
        
        /// <summary>Toggle hotkey (e.g., F12)</summary>
        public string ToggleHotkey { get; set; } = "F12";
        
        /// <summary>Position: TopLeft, TopRight, BottomLeft, BottomRight</summary>
        public string Position { get; set; } = "TopLeft";
        
        /// <summary>Opacity 0.0-1.0</summary>
        public double Opacity { get; set; } = 0.85;
        
        /// <summary>Show CPU temperature</summary>
        public bool ShowCpuTemp { get; set; } = true;
        
        /// <summary>Show GPU temperature</summary>
        public bool ShowGpuTemp { get; set; } = true;
        
        /// <summary>Show CPU load</summary>
        public bool ShowCpuLoad { get; set; } = true;
        
        /// <summary>Show GPU load</summary>
        public bool ShowGpuLoad { get; set; } = true;
        
        /// <summary>Show fan speeds</summary>
        public bool ShowFanSpeed { get; set; } = true;
        
        /// <summary>Show RAM usage</summary>
        public bool ShowRamUsage { get; set; } = false;
        
        /// <summary>Show current FPS (requires integration)</summary>
        public bool ShowFps { get; set; } = false;
    }
    
    /// <summary>
    /// Battery care settings.
    /// </summary>
    public class BatterySettings
    {
        /// <summary>Enable 80% charge limit for battery longevity</summary>
        public bool ChargeLimitEnabled { get; set; } = false;
        
        /// <summary>Show battery health warnings</summary>
        public bool ShowHealthWarnings { get; set; } = true;
    }
}
