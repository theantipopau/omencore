namespace OmenCore.Avalonia.Services;

/// <summary>
/// Fan curve data point.
/// </summary>
public record FanCurvePoint(int Temperature, int FanSpeed);

/// <summary>
/// Hardware monitoring data.
/// </summary>
public class HardwareStatus
{
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
    public int CpuFanRpm { get; set; }
    public int GpuFanRpm { get; set; }
    public int CpuFanPercent { get; set; }
    public int GpuFanPercent { get; set; }
    public double CpuUsage { get; set; }
    public double GpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double MemoryUsedGb { get; set; }
    public double MemoryTotalGb { get; set; }
    public double PowerConsumption { get; set; }
    public int BatteryPercentage { get; set; }
    public bool IsOnBattery { get; set; }
    public bool IsThrottling { get; set; }
    public string? ThrottlingReason { get; set; }
}

/// <summary>
/// System capability information.
/// </summary>
public class SystemCapabilities
{
    public bool HasKeyboardBacklight { get; set; }
    public bool HasPerKeyRgb { get; set; }
    public bool HasFourZoneRgb { get; set; }
    public bool HasDiscreteGpu { get; set; }
    public bool HasGpuMuxSwitch { get; set; }
    public bool SupportsFanControl { get; set; }
    public bool SupportsPerformanceProfiles { get; set; }
    public string FanControlCapabilityClass { get; set; } = "unsupported-control";
    public string FanControlCapabilityReason { get; set; } = string.Empty;
    public string ModelName { get; set; } = "Unknown";
    public string CpuName { get; set; } = "Unknown";
    public string GpuName { get; set; } = "Unknown";
}

/// <summary>
/// Performance mode options.
/// </summary>
public enum PerformanceMode
{
    Quiet = 0,
    Balanced = 1,
    Performance = 2
}

/// <summary>
/// Service for interacting with hardware on Linux.
/// </summary>
public interface IHardwareService
{
    /// <summary>
    /// Gets the current hardware status.
    /// </summary>
    Task<HardwareStatus> GetStatusAsync();
    
    /// <summary>
    /// Gets system capabilities.
    /// </summary>
    Task<SystemCapabilities> GetCapabilitiesAsync();
    
    /// <summary>
    /// Gets the current performance mode.
    /// </summary>
    Task<PerformanceMode> GetPerformanceModeAsync();
    
    /// <summary>
    /// Sets the performance mode.
    /// </summary>
    Task SetPerformanceModeAsync(PerformanceMode mode);
    
    /// <summary>
    /// Sets the CPU fan speed (0-100%).
    /// </summary>
    Task SetCpuFanSpeedAsync(int percentage);
    
    /// <summary>
    /// Sets the GPU fan speed (0-100%).
    /// </summary>
    Task SetGpuFanSpeedAsync(int percentage);
    
    /// <summary>
    /// Gets the current GPU mode (hybrid/discrete/integrated).
    /// </summary>
    Task<string> GetGpuModeAsync();
    
    /// <summary>
    /// Sets the GPU mode.
    /// </summary>
    Task SetGpuModeAsync(string mode);
    
    /// <summary>
    /// Sets keyboard backlight brightness (0-100).
    /// </summary>
    Task SetKeyboardBrightnessAsync(int brightness);
    
    /// <summary>
    /// Sets keyboard color.
    /// </summary>
    Task SetKeyboardColorAsync(byte r, byte g, byte b);
    
    /// <summary>
    /// Event raised when hardware status changes.
    /// </summary>
    event EventHandler<HardwareStatus>? StatusChanged;
}
