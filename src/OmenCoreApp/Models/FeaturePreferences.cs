namespace OmenCore.Models
{
    /// <summary>
    /// Feature toggles to enable/disable optional OmenCore modules.
    /// This allows reducing background presence by disabling unused features.
    /// </summary>
    public class FeaturePreferences
    {
        /// <summary>
        /// Enable Corsair iCUE device integration (keyboards, mice).
        /// Disable if you don't have Corsair peripherals.
        /// Default: false (user must enable if they have Corsair devices)
        /// </summary>
        public bool CorsairIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// Enable Logitech G HUB device integration.
        /// Disable if you don't have Logitech G peripherals.
        /// Default: false (user must enable if they have Logitech devices)
        /// </summary>
        public bool LogitechIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// Enable Razer Chroma device integration.
        /// Disable if you don't have Razer peripherals.
        /// Default: false (user must enable if they have Razer devices)
        /// </summary>
        public bool RazerIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// Enable automatic game profile switching.
        /// When enabled, OmenCore detects running games and applies corresponding profiles.
        /// </summary>
        public bool GameProfilesEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable keyboard backlighting control.
        /// Disable if keyboard lighting doesn't work on your model.
        /// </summary>
        public bool KeyboardLightingEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable temperature monitoring and display.
        /// Disable to reduce background CPU usage.
        /// </summary>
        public bool MonitoringEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable fan curve functionality (continuous fan speed adjustment).
        /// When disabled, fans run in BIOS-controlled mode only.
        /// </summary>
        public bool FanCurvesEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable tray icon with live temperature display.
        /// Disable for minimal background presence.
        /// </summary>
        public bool TrayIconEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable power source monitoring for automatic profile switching.
        /// Switches profiles when AC/Battery power changes.
        /// </summary>
        public bool PowerAutomationEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable LibreHardwareMonitor for detailed sensor readings.
        /// Disable if sensor data is inaccurate or causing issues.
        /// </summary>
        public bool LibreHardwareMonitorEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable GPU switching controls (Hybrid/Discrete/Optimus).
        /// Disable if GPU switching doesn't work on your model.
        /// </summary>
        public bool GpuSwitchingEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable undervolt controls.
        /// Disable if undervolting is not supported on your CPU.
        /// </summary>
        public bool UndervoltEnabled { get; set; } = true;
        
        /// <summary>
        /// Enable OMEN key interception.
        /// When enabled, pressing the OMEN key shows OmenCore instead of HP OMEN Gaming Hub.
        /// </summary>
        public bool OmenKeyInterceptionEnabled { get; set; } = true;
        
        /// <summary>
        /// Action to perform when OMEN key is pressed.
        /// Options: "ToggleOmenCore", "CyclePerformance", "CycleFanMode", "ToggleMaxCooling", "LaunchExternalApp", "DoNothing"
        /// </summary>
        public string OmenKeyAction { get; set; } = "ToggleOmenCore";
        
        /// <summary>
        /// Show CPU temperature on the system tray icon.
        /// When disabled, shows the static OmenCore logo instead.
        /// </summary>
        public bool TrayTempDisplayEnabled { get; set; } = true;
        
        /// <summary>
        /// Suppress hotkeys and OMEN key during Remote Desktop (RDP) sessions.
        /// Prevents OmenCore from stealing focus or responding to keyboard events during remote sessions.
        /// Default: true (suppress during RDP).
        /// </summary>
        public bool SuppressHotkeysInRdp { get; set; } = true;
        
        /// <summary>
        /// Force PawnIO-only backend mode.
        /// When enabled, OmenCore disables features that require HP WMI BIOS or HP services,
        /// only using PawnIO for temperature monitoring, fan curves (via EC), and undervolting.
        /// Useful for systems where HP services are not installed or causing issues.
        /// Default: false (auto-detect available backends).
        /// </summary>
        public bool PawnIOOnlyMode { get; set; } = false;

        /// <summary>
        /// Enable startup safe-mode guardrails.
        /// If monitoring becomes degraded/stale shortly after startup, OmenCore temporarily blocks
        /// hardware write actions (fan/performance tray writes) to avoid lockups while telemetry stabilizes.
        /// </summary>
        public bool StartupSafeModeGuardEnabled { get; set; } = true;

        /// <summary>
        /// Startup guard window in seconds.
        /// Safe mode can only auto-activate within this startup period.
        /// </summary>
        public int StartupSafeModeWindowSeconds { get; set; } = 180;

        /// <summary>
        /// Consecutive monitoring timeout threshold before startup safe mode activates.
        /// </summary>
        public int StartupSafeModeTimeoutThreshold { get; set; } = 2;

        /// <summary>
        /// Experimental: cycle performance profile when firmware emits Fn+P-like WMI event.
        /// Disabled by default because event IDs vary across models/BIOS versions.
        /// </summary>
        public bool EnableFirmwareFnPProfileCycle { get; set; } = false;
    }
}
