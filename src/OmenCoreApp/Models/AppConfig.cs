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
        
        /// <summary>
        /// Named DPI profiles saved by the user for quick apply across devices.
        /// </summary>
        public List<OmenCore.Corsair.CorsairDpiProfile> CorsairDpiProfiles { get; set; } = new();
        public List<MacroProfile> MacroProfiles { get; set; } = new();
        public Dictionary<string, int> EcFanRegisterMap { get; set; } = new();
        public UndervoltPreferences Undervolt { get; set; } = new();
        public MonitoringPreferences Monitoring { get; set; } = new();
        public UpdatePreferences Updates { get; set; } = new();
        public FeaturePreferences Features { get; set; } = new();
        public FanHysteresisSettings FanHysteresis { get; set; } = new();
        /// <summary>
        /// Fan transition / smoothing settings for ramping and immediate apply behavior.
        /// </summary>
        public FanTransitionSettings FanTransition { get; set; } = new();
        public OsdSettings Osd { get; set; } = new();
        public BatterySettings Battery { get; set; } = new();
        public bool FirstRunCompleted { get; set; } = false;

        /// <summary>
        /// If true, the Corsair service will NOT fall back to the iCUE SDK when direct HID access fails.
        /// This allows OmenCore to operate without iCUE but may result in reduced device support on some systems.
        /// Default: false (iCUE fallback allowed).
        /// </summary>
        public bool CorsairDisableIcueFallback { get; set; } = false;

        /// <summary>
        /// Telemetry: anonymous, aggregated counts of PID successes/failures for HID writes.
        /// Opt-in only; default is false.
        /// </summary>
        public bool TelemetryEnabled { get; set; } = false;
        
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
        /// Last applied TCC (Thermal Control Circuit) offset in degrees C.
        /// Re-applied on startup to maintain CPU temperature limits.
        /// 0 = no limit (full TjMax), higher values = lower temp limit.
        /// </summary>
        public int? LastTccOffset { get; set; }
        
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
        public bool OmenKeyEnabled { get; set; } = true;
        
        /// <summary>Whether to block the key (true) or let it pass through (false)</summary>
        public bool OmenKeyIntercept { get; set; } = true;
        
        /// <summary>Action to perform when OMEN key is pressed</summary>
        public string OmenKeyAction { get; set; } = "ToggleOmenCore";
        
        /// <summary>Path to external app when OmenKeyAction is LaunchExternalApp</summary>
        public string? OmenKeyExternalApp { get; set; }
        
        /// <summary>
        /// EXPERIMENTAL: Enable direct EC writes for keyboard RGB control.
        /// WARNING: This feature is DISABLED by default because EC keyboard registers vary by model.
        /// Writing to wrong EC addresses can cause hard system crashes requiring forced restart.
        /// Only enable this if WMI keyboard lighting doesn't work and you accept the risk.
        /// </summary>
        public bool ExperimentalEcKeyboardEnabled { get; set; } = false;
        
        /// <summary>
        /// Preferred keyboard lighting backend: "Auto" (default), "WmiBios", "Wmi", "Ec".
        /// Auto will use WMI BIOS > WMI > EC priority.
        /// Setting a specific backend forces that backend if available.
        /// </summary>
        public string PreferredKeyboardBackend { get; set; } = "Auto";
        
        /// <summary>
        /// Saved keyboard zone colors (4 zones). Applied on startup.
        /// </summary>
        public KeyboardLightingSettings KeyboardLighting { get; set; } = new();
    }
    
    /// <summary>
    /// Settings for HP OMEN keyboard 4-zone RGB lighting.
    /// </summary>
    public class KeyboardLightingSettings
    {
        /// <summary>Zone 1 (Left) color in hex format (e.g., "#E6002E")</summary>
        public string Zone1Color { get; set; } = "#E6002E";
        
        /// <summary>Zone 2 (Middle-Left) color in hex format</summary>
        public string Zone2Color { get; set; } = "#E6002E";
        
        /// <summary>Zone 3 (Middle-Right) color in hex format</summary>
        public string Zone3Color { get; set; } = "#E6002E";
        
        /// <summary>Zone 4 (Right) color in hex format</summary>
        public string Zone4Color { get; set; } = "#E6002E";
        
        /// <summary>Whether to apply keyboard colors on startup</summary>
        public bool ApplyOnStartup { get; set; } = true;
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
    /// Settings for fan transition smoothing and immediate-apply behavior.
    /// </summary>
    public class FanTransitionSettings
    {
        /// <summary>Enable smoothing (ramp fans gradually) when applying a new target.</summary>
        public bool EnableSmoothing { get; set; } = true;

        /// <summary>Total smoothing duration in milliseconds when ramping between fan percentages.</summary>
        public int SmoothingDurationMs { get; set; } = 1000;

        /// <summary>Step interval in milliseconds for incremental ramp steps.</summary>
        public int SmoothingStepMs { get; set; } = 200;

        /// <summary>
        /// When true, user-initiated apply actions (Save/Apply preset) will force immediate application
        /// rather than waiting for smoothing / hysteresis.
        /// </summary>
        public bool ApplyImmediatelyOnUserAction { get; set; } = false;
    }
    
    /// <summary>
    /// In-game OSD overlay settings.
    /// </summary>
    public class OsdSettings
    {
        /// <summary>Master toggle - when disabled, no OSD process runs at all</summary>
        public bool Enabled { get; set; } = false;
        
        /// <summary>Toggle hotkey (e.g., Ctrl+Shift+F12)</summary>
        public string ToggleHotkey { get; set; } = "Ctrl+Shift+F12";
        
        /// <summary>Position: TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight</summary>
        public string Position { get; set; } = "TopRight";
        
        /// <summary>Opacity 0.0-1.0 (lower = more transparent)</summary>
        public double Opacity { get; set; } = 0.6;
        
        /// <summary>Show CPU temperature</summary>
        public bool ShowCpuTemp { get; set; } = true;
        
        /// <summary>Show GPU temperature</summary>
        public bool ShowGpuTemp { get; set; } = true;
        
        /// <summary>Show CPU load percentage</summary>
        public bool ShowCpuLoad { get; set; } = true;
        
        /// <summary>Show GPU load percentage</summary>
        public bool ShowGpuLoad { get; set; } = true;
        
        /// <summary>Show fan speeds</summary>
        public bool ShowFanSpeed { get; set; } = true;
        
        /// <summary>Show RAM usage</summary>
        public bool ShowRamUsage { get; set; } = false;
        
        /// <summary>Show current fan/performance mode</summary>
        public bool ShowCurrentMode { get; set; } = true;
        
        /// <summary>Show current FPS (estimated from GPU metrics)</summary>
        public bool ShowFps { get; set; } = false;
        
        /// <summary>Show current fan mode (Auto, Performance, Silent, Max)</summary>
        public bool ShowFanMode { get; set; } = true;
        
        /// <summary>Show performance mode (Balanced, Performance, Silent)</summary>
        public bool ShowPerformanceMode { get; set; } = false;
        
        /// <summary>Show frametime in milliseconds (inverse of FPS)</summary>
        public bool ShowFrametime { get; set; } = false;
        
        /// <summary>Show current clock time</summary>
        public bool ShowTime { get; set; } = false;
        
        /// <summary>Show GPU power consumption in watts</summary>
        public bool ShowGpuPower { get; set; } = false;
        
        /// <summary>Show CPU power consumption in watts</summary>
        public bool ShowCpuPower { get; set; } = false;
        
        /// <summary>Show network latency (ping to common servers)</summary>
        public bool ShowNetworkLatency { get; set; } = false;
        
        /// <summary>Show GPU memory usage</summary>
        public bool ShowVramUsage { get; set; } = false;
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
