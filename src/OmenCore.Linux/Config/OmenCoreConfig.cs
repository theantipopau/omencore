using Tomlyn;
using Tomlyn.Model;

namespace OmenCore.Linux.Config;

/// <summary>
/// TOML configuration model for OmenCore Linux daemon.
/// 
/// Example config file (~/.config/omencore/config.toml):
/// 
/// [general]
/// poll_interval_ms = 2000
/// log_level = "info"
/// 
/// [fan]
/// profile = "auto"          # auto, silent, balanced, gaming, max, custom
/// boost = false
/// smooth_transition = true
/// 
/// [fan.curve]               # Only used when profile = "custom"
/// enabled = true
/// points = [
///     { temp = 40, speed = 20 },
///     { temp = 50, speed = 30 },
///     { temp = 60, speed = 50 },
///     { temp = 70, speed 70 },
///     { temp = 80, speed = 85 },
///     { temp = 90, speed = 100 }
/// ]
/// 
/// [performance]
/// mode = "balanced"         # default, balanced, performance, cool
/// 
/// [keyboard]
/// enabled = true
/// color = "FF0000"
/// brightness = 100
/// 
/// [startup]
/// apply_on_boot = true
/// restore_on_exit = true
/// </summary>
public class OmenCoreConfig
{
    public GeneralConfig General { get; set; } = new();
    public FanConfig Fan { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public ThermalConfig Thermal { get; set; } = new();
    public KeyboardConfig Keyboard { get; set; } = new();
    public StartupConfig Startup { get; set; } = new();
    
    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "omencore");
    
    public static string DefaultConfigPath => Path.Combine(DefaultConfigDir, "config.toml");
    public static string SystemConfigPath => "/etc/omencore/config.toml";
    
    /// <summary>
    /// Load configuration from TOML file.
    /// Looks in order: /etc/omencore/config.toml, ~/.config/omencore/config.toml
    /// </summary>
    public static OmenCoreConfig Load(string? customPath = null)
    {
        var paths = new List<string>();
        
        if (!string.IsNullOrEmpty(customPath))
        {
            paths.Add(customPath);
        }
        else
        {
            // System config first, then user config (user overrides system)
            paths.Add(SystemConfigPath);
            paths.Add(DefaultConfigPath);
        }
        
        var config = new OmenCoreConfig();
        
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var toml = File.ReadAllText(path);
                    var parsed = Toml.ToModel<OmenCoreConfig>(toml);
                    config = MergeConfigs(config, parsed);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to parse {path}: {ex.Message}");
                }
            }
        }
        
        return config;
    }
    
    /// <summary>
    /// Save configuration to TOML file.
    /// </summary>
    public void Save(string? customPath = null)
    {
        var path = customPath ?? DefaultConfigPath;
        var dir = Path.GetDirectoryName(path);
        
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        var toml = Toml.FromModel(this);
        File.WriteAllText(path, toml);
    }
    
    /// <summary>
    /// Generate a default configuration file with comments.
    /// </summary>
    public static string GenerateDefaultToml()
    {
        return """
            # OmenCore Linux Configuration
            # Place this file at ~/.config/omencore/config.toml or /etc/omencore/config.toml
            
            [general]
            # Polling interval in milliseconds for daemon mode
            poll_interval_ms = 2000
            # Log level: debug, info, warn, error
            log_level = "info"
            
            [fan]
            # Fan profile: auto, silent, balanced, gaming, max, custom
            profile = "auto"
            # Enable fan boost mode (more aggressive cooling)
            boost = false
            # Smooth fan speed transitions to reduce noise spikes
            smooth_transition = true
            
            # Custom fan curve (only used when profile = "custom")
            [fan.curve]
            enabled = false
            # Hysteresis in degrees - prevents fan speed oscillation
            hysteresis = 3
            # Fan curve points: temperature (°C) -> fan speed (%)
            [[fan.curve.points]]
            temp = 40
            speed = 20
            
            [[fan.curve.points]]
            temp = 50
            speed = 30
            
            [[fan.curve.points]]
            temp = 60
            speed = 50
            
            [[fan.curve.points]]
            temp = 70
            speed = 70
            
            [[fan.curve.points]]
            temp = 80
            speed = 85
            
            [[fan.curve.points]]
            temp = 90
            speed = 100
            
            [performance]
            # Performance mode: default, balanced, performance, cool
            mode = "balanced"
            
            [thermal]
            # Re-apply configured performance mode after CPU thermal cooldown.
            # Some HP OMEN models (e.g. Transcend 14) reset the thermal profile to Balanced
            # when the CPU hits its package temperature limit (~100°C / PROCHOT). Enable this
            # so OmenCore restores your chosen mode (e.g. Performance) once temps normalise.
            restore_performance_after_throttle = false
            # CPU temperature (°C) above which throttling is considered active.
            throttle_temp_c = 95
            # CPU temperature (°C) below which the system is considered cooled-down.
            # Performance mode is re-applied when CPU drops below this threshold.
            restore_temp_c = 80
            
            [keyboard]
            # Enable keyboard lighting control
            enabled = true
            # RGB color in hex (without #)
            color = "FF0000"
            # Brightness 0-100
            brightness = 100
            
            [startup]
            # Apply saved configuration when daemon starts
            apply_on_boot = true
            # Restore previous fan/performance settings when daemon exits
            restore_on_exit = true
            """;
    }
    
    private static OmenCoreConfig MergeConfigs(OmenCoreConfig baseConfig, OmenCoreConfig overlay)
    {
        // Simple merge - overlay wins for non-default values
        return overlay;
    }
}

public class GeneralConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public string LogLevel { get; set; } = "info";
    
    /// <summary>
    /// Low-overhead mode settings for battery and idle operation
    /// </summary>
    public LowOverheadConfig LowOverhead { get; set; } = new();
}

/// <summary>
/// Low-overhead monitoring mode for reduced power consumption (#22)
/// </summary>
public class LowOverheadConfig
{
    /// <summary>
    /// Enable automatic low-overhead mode when on battery
    /// </summary>
    public bool EnableOnBattery { get; set; } = true;
    
    /// <summary>
    /// Poll interval in low-overhead mode (ms) - longer = less CPU usage
    /// </summary>
    public int PollIntervalMs { get; set; } = 5000;
    
    /// <summary>
    /// Disable hwmon scanning in low-overhead mode (use cached paths only)
    /// </summary>
    public bool DisableSensorScanning { get; set; } = true;
    
    /// <summary>
    /// Reduce logging verbosity in low-overhead mode
    /// </summary>
    public bool ReduceLogging { get; set; } = true;
}

public class FanConfig
{
    public string Profile { get; set; } = "auto";
    public bool Boost { get; set; } = false;
    public bool SmoothTransition { get; set; } = true;
    public FanCurveConfig Curve { get; set; } = new();
}

public class FanCurveConfig
{
    public bool Enabled { get; set; } = false;
    public int Hysteresis { get; set; } = 3;
    public List<FanCurvePoint> Points { get; set; } = new()
    {
        new() { Temp = 40, Speed = 20 },
        new() { Temp = 50, Speed = 30 },
        new() { Temp = 60, Speed = 50 },
        new() { Temp = 70, Speed = 70 },
        new() { Temp = 80, Speed = 85 },
        new() { Temp = 90, Speed = 100 }
    };
}

public class FanCurvePoint
{
    public int Temp { get; set; }
    public int Speed { get; set; }
}

public class PerformanceConfig
{
    public string Mode { get; set; } = "balanced";
}

public class KeyboardConfig
{
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "FF0000";
    public int Brightness { get; set; } = 100;
}

public class StartupConfig
{
    public bool ApplyOnBoot { get; set; } = true;
    public bool RestoreOnExit { get; set; } = true;
}

public class ThermalConfig
{
    /// <summary>
    /// Re-apply configured performance mode after the CPU cools down from a thermal throttle event.
    /// Some HP OMEN models reset the thermal profile to Balanced when the CPU hits its package
    /// temperature limit (PROCHOT / ~100°C).  Enable this to restore your chosen mode
    /// (e.g. Performance) automatically once temps fall back below <see cref="RestoreTempC"/>.
    /// </summary>
    public bool RestorePerformanceAfterThrottle { get; set; } = false;

    /// <summary>CPU °C above which the system is considered thermally throttling. Default 95.</summary>
    public int ThrottleTempC { get; set; } = 95;

    /// <summary>
    /// CPU °C below which the system is considered cooled-down and the performance mode will
    /// be re-applied.  Must be lower than <see cref="ThrottleTempC"/>. Default 80.
    /// </summary>
    public int RestoreTempC { get; set; } = 80;
}
