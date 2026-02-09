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
        
        /// <summary>
        /// Override the auto-detected maximum fan level (0 = auto-detect).
        /// Classic HP OMEN models use 0-55 (krpm), but some models support higher levels
        /// (e.g., OMEN 16-xd0xxx maxes at level 63 = 6300 RPM).
        /// Set this if your fans don't reach full speed. Typical values: 55, 63, 100.
        /// The BIOS will clamp to the actual hardware maximum if the value is too high.
        /// </summary>
        public int MaxFanLevelOverride { get; set; } = 0;
        public OsdSettings Osd { get; set; } = new();
        public BatterySettings Battery { get; set; } = new();
        public AmbientLightingSettings AmbientLighting { get; set; } = new();
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
        /// GPU overclocking settings (NVAPI-based core/memory clock offsets and power limit).
        /// </summary>
        public GpuOcSettings? GpuOc { get; set; }
        
        /// <summary>
        /// Saved GPU overclocking profiles for quick switching.
        /// </summary>
        public List<GpuOcProfile> GpuOcProfiles { get; set; } = new();
        
        /// <summary>
        /// Name of the last applied GPU OC profile (for UI selection).
        /// </summary>
        public string? LastGpuOcProfileName { get; set; }
        
        /// <summary>
        /// AMD Ryzen power and temperature limits (STAPM, Tctl).
        /// </summary>
        public AmdPowerLimits? AmdPowerLimits { get; set; }
        
        /// <summary>
        /// Per-game automatic profile configurations.
        /// </summary>
        public List<GameProfile> GameProfiles { get; set; } = new();
        
        /// <summary>
        /// Automation rules for conditional profile switching.
        /// </summary>
        public List<AutomationRule> AutomationRules { get; set; } = new();
        
        /// <summary>
        /// Last applied TCC (Thermal Control Circuit) offset in degrees C.
        /// Re-applied on startup to maintain CPU temperature limits.
        /// 0 = no limit (full TjMax), higher values = lower temp limit.
        /// </summary>
        public int? LastTccOffset { get; set; }
        
        /// <summary>
        /// Last applied CPU PL1 (sustained power limit) in watts.
        /// </summary>
        public int? LastCpuPl1Watts { get; set; }
        
        /// <summary>
        /// Last applied CPU PL2 (burst power limit) in watts.
        /// </summary>
        public int? LastCpuPl2Watts { get; set; }
        
        /// <summary>
        /// Last applied fan preset name for restoration on startup.
        /// </summary>
        public string? LastFanPresetName { get; set; }
        
        /// <summary>
        /// Custom fan curve points for unified fan control (non-independent mode).
        /// Each point maps a temperature (°C) to a fan percentage.
        /// </summary>
        public List<FanCurvePoint>? CustomFanCurve { get; set; }
        
        /// <summary>
        /// Whether independent CPU/GPU fan curves are enabled.
        /// When enabled, CPU and GPU fans are controlled separately based on their respective temperatures.
        /// </summary>
        public bool IndependentFanCurvesEnabled { get; set; } = false;
        
        /// <summary>
        /// Custom CPU fan curve points (used when IndependentFanCurvesEnabled is true).
        /// Each point maps a temperature (°C) to a fan percentage.
        /// </summary>
        public List<FanCurvePoint>? CpuFanCurve { get; set; }
        
        /// <summary>
        /// Custom GPU fan curve points (used when IndependentFanCurvesEnabled is true).
        /// Each point maps a temperature (°C) to a fan percentage.
        /// </summary>
        public List<FanCurvePoint>? GpuFanCurve { get; set; }
        
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
        /// EXPERIMENTAL: Enable exclusive EC access diagnostic mode.
        /// When enabled, EC providers will acquire exclusive mutex access and hold it,
        /// collecting diagnostic snapshots when other applications try to access EC registers.
        /// This helps diagnose EC register contention issues but may interfere with other apps.
        /// WARNING: This is for diagnostic purposes only and may cause system instability.
        /// </summary>
        public bool ExclusiveEcAccessDiagnosticsEnabled { get; set; } = false;
        
        /// <summary>
        /// Preferred keyboard lighting backend: "Auto" (default), "WmiBios", "Wmi", "Ec".
        /// Auto will use WMI BIOS > WMI > EC priority.
        /// Setting a specific backend forces that backend if available.
        /// </summary>
        public string PreferredKeyboardBackend { get; set; } = "Auto";
        
        /// <summary>
        /// Invert the order of RGB zones (right-to-left instead of left-to-right).
        /// Needed for OMEN Max 16 light bar which has inverted zone order.
        /// Zone mapping when inverted: Zone1=Right, Zone2=Middle-Right, Zone3=Middle-Left, Zone4=Left
        /// </summary>
        public bool InvertRgbZoneOrder { get; set; } = false;
        
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
        
        /// <summary>
        /// Whether the keyboard backlight was ON when user last used OmenCore.
        /// Used to restore backlight state on startup.
        /// </summary>
        public bool BacklightWasEnabled { get; set; } = true;
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
        public double DeadZone { get; set; } = 4.0;
        
        /// <summary>Ramp-up delay in seconds before increasing fan speed.</summary>
        public double RampUpDelay { get; set; } = 1.0;
        
        /// <summary>Ramp-down delay in seconds before decreasing fan speed.</summary>
        public double RampDownDelay { get; set; } = 5.0;
        
        /// <summary>Enable thermal protection override - forces max fans when temps exceed threshold, even in Auto mode.</summary>
        public bool ThermalProtectionEnabled { get; set; } = true;
        
        /// <summary>
        /// Temperature threshold in °C for thermal protection to activate (start ramping fans).
        /// Default: 90°C. Advanced users can lower to 80-85°C for more aggressive cooling.
        /// v2.8.0: Raised from 80°C — laptops routinely hit 80-85°C under gaming load.
        /// Range: 75-95°C. Values outside this range will be clamped.
        /// </summary>
        public double ThermalProtectionThreshold { get; set; } = 90.0;
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
        
        /// <summary>Show package power (CPU+GPU combined wattage)</summary>
        public bool ShowPackagePower { get; set; } = false;
        
        /// <summary>Show GPU hotspot (junction) temperature</summary>
        public bool ShowGpuHotspot { get; set; } = false;
        
        /// <summary>Show toast notifications when mode changes (fan profile, performance mode, etc.)</summary>
        public bool ShowModeChangeNotifications { get; set; } = true;
        
        /// <summary>Use RTSS (RivaTuner) for accurate FPS data when available</summary>
        public bool UseRtssForFps { get; set; } = true;
        
        /// <summary>Layout orientation: Vertical or Horizontal</summary>
        public string Layout { get; set; } = "Vertical";
        
        /// <summary>Show network upload speed in Mbps</summary>
        public bool ShowNetworkUpload { get; set; } = false;
        
        /// <summary>Show network download speed in Mbps</summary>
        public bool ShowNetworkDownload { get; set; } = false;
        
        /// <summary>Show battery percentage and charge status</summary>
        public bool ShowBattery { get; set; } = false;
        
        /// <summary>Show CPU clock speed (average across cores)</summary>
        public bool ShowCpuClock { get; set; } = false;
        
        /// <summary>Show GPU clock speed</summary>
        public bool ShowGpuClock { get; set; } = false;
    }
    
    /// <summary>
    /// Battery care settings.
    /// </summary>
    public class BatterySettings
    {
        /// <summary>Enable charge limit for battery longevity</summary>
        public bool ChargeLimitEnabled { get; set; } = false;
        
        /// <summary>Charge threshold percentage (60-100%, default 80%)</summary>
        public int ChargeThresholdPercent { get; set; } = 80;
        
        /// <summary>Show battery health warnings</summary>
        public bool ShowHealthWarnings { get; set; } = true;
    }
    
    /// <summary>
    /// Ambient lighting (screen color sampling) settings.
    /// </summary>
    public class AmbientLightingSettings
    {
        /// <summary>Enable ambient lighting feature</summary>
        public bool Enabled { get; set; } = false;
        
        /// <summary>Update interval in milliseconds (16-500, default 33 = ~30 FPS)</summary>
        public int UpdateIntervalMs { get; set; } = 33;
        
        /// <summary>Saturation boost (0.5-2.0, default 1.2)</summary>
        public float SaturationBoost { get; set; } = 1.2f;
        
        /// <summary>Brightness multiplier (0.0-1.0, default 1.0)</summary>
        public float Brightness { get; set; } = 1.0f;
        
        /// <summary>Number of frames to smooth colors over (1-10, default 3)</summary>
        public int SmoothingFrames { get; set; } = 3;
        
        /// <summary>Apply ambient colors to keyboard</summary>
        public bool ApplyToKeyboard { get; set; } = true;
        
        /// <summary>Apply ambient colors to peripherals (mouse, headset)</summary>
        public bool ApplyToPeripherals { get; set; } = true;
    }
    
    /// <summary>
    /// GPU overclocking settings (NVAPI-based).
    /// </summary>
    public class GpuOcSettings
    {
        /// <summary>GPU core clock offset in MHz (-500 to +300)</summary>
        public int CoreClockOffsetMHz { get; set; } = 0;
        
        /// <summary>GPU memory clock offset in MHz (-500 to +1500)</summary>
        public int MemoryClockOffsetMHz { get; set; } = 0;
        
        /// <summary>Power limit percentage (50-125, 100 = default TDP)</summary>
        public int PowerLimitPercent { get; set; } = 100;
        
        /// <summary>GPU voltage offset in mV (optional, not all GPUs support)</summary>
        public int? VoltageOffsetMv { get; set; } = null;
        
        /// <summary>Reapply OC settings on application startup</summary>
        public bool ApplyOnStartup { get; set; } = false;
    }
    
    /// <summary>
    /// GPU overclocking profile for save/load functionality.
    /// </summary>
    public class GpuOcProfile
    {
        /// <summary>Profile name (user-defined)</summary>
        public string Name { get; set; } = "Default";
        
        /// <summary>GPU core clock offset in MHz</summary>
        public int CoreClockOffsetMHz { get; set; } = 0;
        
        /// <summary>GPU memory clock offset in MHz</summary>
        public int MemoryClockOffsetMHz { get; set; } = 0;
        
        /// <summary>Power limit percentage</summary>
        public int PowerLimitPercent { get; set; } = 100;
        
        /// <summary>Optional description or notes</summary>
        public string? Description { get; set; }
        
        /// <summary>When this profile was created</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>When this profile was last modified</summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// AMD Ryzen power and temperature limits.
    /// </summary>
    public class AmdPowerLimits
    {
        /// <summary>STAPM (Sustained Power) limit in Watts (15-54W typical range)</summary>
        public uint StapmLimitWatts { get; set; } = 25;
        
        /// <summary>CPU temperature limit in Celsius (75-105°C range)</summary>
        public uint TempLimitC { get; set; } = 95;
    }

    /// <summary>
    /// Automation rule for conditional system behavior.
    /// Triggers when specified conditions are met, applies actions automatically.
    /// </summary>
    public class AutomationRule
    {
        /// <summary>Unique identifier for this rule</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>Display name for the rule (e.g., "Silent Mode at Night")</summary>
        public string Name { get; set; } = "";
        
        /// <summary>Optional description</summary>
        public string? Description { get; set; }
        
        /// <summary>Is this rule enabled?</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Rule priority (lower number = higher priority). Default: 50</summary>
        public int Priority { get; set; } = 50;
        
        /// <summary>Trigger type</summary>
        public TriggerType Trigger { get; set; }
        
        /// <summary>Trigger configuration (JSON-serialized based on trigger type)</summary>
        public TriggerConfig TriggerData { get; set; } = new();
        
        /// <summary>Actions to execute when triggered</summary>
        public List<RuleAction> Actions { get; set; } = new();
        
        /// <summary>When was this rule created</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        /// <summary>When was this rule last triggered</summary>
        public DateTime? LastTriggeredAt { get; set; }
        
        /// <summary>How many times this rule has triggered</summary>
        public int TriggerCount { get; set; }
    }

    /// <summary>
    /// Automation trigger types
    /// </summary>
    public enum TriggerType
    {
        /// <summary>Time-based trigger (specific time or time range)</summary>
        Time,
        
        /// <summary>Battery percentage threshold</summary>
        Battery,
        
        /// <summary>AC power connected/disconnected</summary>
        ACPower,
        
        /// <summary>Temperature threshold (CPU or GPU)</summary>
        Temperature,
        
        /// <summary>Specific process running</summary>
        Process,
        
        /// <summary>System idle for duration</summary>
        Idle,
        
        /// <summary>WiFi SSID connected (location-based)</summary>
        WiFiSSID
    }

    /// <summary>
    /// Trigger configuration data (type-specific)
    /// </summary>
    public class TriggerConfig
    {
        // Time trigger
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public List<DayOfWeek>? Days { get; set; }
        
        // Battery trigger
        public int? BatteryThreshold { get; set; }
        public string? BatteryCondition { get; set; } // "Below", "Above"
        
        // ACPower trigger
        public bool? ACConnected { get; set; }
        
        // Temperature trigger
        public string? TemperatureSensor { get; set; } // "CPU", "GPU"
        public int? TemperatureThreshold { get; set; }
        public string? TemperatureCondition { get; set; } // "Above", "Below"
        
        // Process trigger
        public string? ProcessName { get; set; }
        
        // Idle trigger
        public int? IdleMinutes { get; set; }
        
        // WiFi trigger
        public string? WiFiSSID { get; set; }
    }

    /// <summary>
    /// Action to execute when rule triggers
    /// </summary>
    public class RuleAction
    {
        /// <summary>Action type</summary>
        public ActionType Type { get; set; }
        
        /// <summary>Action-specific parameter (e.g., preset name, mode name)</summary>
        public string? Parameter { get; set; }
        
        /// <summary>Optional numeric parameter (e.g., power limit watts)</summary>
        public int? NumericParameter { get; set; }
    }

    /// <summary>
    /// Automation action types
    /// </summary>
    public enum ActionType
    {
        /// <summary>Apply fan preset by name</summary>
        SetFanPreset,
        
        /// <summary>Set performance mode (Balanced/Performance/Silent)</summary>
        SetPerformanceMode,
        
        /// <summary>Apply GPU OC profile by name</summary>
        SetGpuOcProfile,
        
        /// <summary>Set power limit in watts</summary>
        SetPowerLimit,
        
        /// <summary>Set AMD STAPM limit</summary>
        SetAmdStapmLimit,
        
        /// <summary>Set AMD temperature limit</summary>
        SetAmdTempLimit,
        
        /// <summary>Enable/disable keyboard lighting</summary>
        SetKeyboardLighting,
        
        /// <summary>Show notification message</summary>
        ShowNotification
    }
}

