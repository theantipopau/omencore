using System.Text.Json.Serialization;

namespace OmenCore.Linux;

/// <summary>
/// JSON serialization context for AOT/trimming support.
/// Using source generators instead of reflection for JSON serialization.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(Commands.DiagnoseInfo))]
[JsonSerializable(typeof(Commands.LinuxKernelIssueHint))]
[JsonSerializable(typeof(Commands.LinuxServiceDiagnostics))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class LinuxJsonContext : JsonSerializerContext
{
}

/// <summary>
/// System status DTO for JSON serialization.
/// </summary>
public class SystemStatus
{
    public string Version { get; set; } = "";
    public bool EcAvailable { get; set; }
    public bool KeyboardAvailable { get; set; }
    public TemperatureInfo Temperatures { get; set; } = new();
    public FanInfo Fans { get; set; } = new();
    public PerformanceInfo Performance { get; set; } = new();
    public string CapabilityClass { get; set; } = "unsupported-control";
    public string CapabilityReason { get; set; } = string.Empty;
    public LinuxAccessInfo Access { get; set; } = new();
    public string GpuTelemetrySource { get; set; } = "unavailable";
    public string GpuTelemetryPath { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}

public class LinuxAccessInfo
{
    public bool IsRoot { get; set; }
    public string AccessMethod { get; set; } = "none";
    public bool EcIoPathExists { get; set; }
    public bool HpWmiPathExists { get; set; }
    public bool HasHwmonFanAccess { get; set; }
    public bool HasThermalProfilePath { get; set; }
    public bool HasPlatformProfilePath { get; set; }
    public bool HasAcpiPlatformProfilePath { get; set; }
    public bool SupportsManualFanControl { get; set; }
    public bool SupportsProfileControl { get; set; }
    public bool SupportsTelemetry { get; set; }
    public string WriteRequirementHint { get; set; } = string.Empty;
}

public class TemperatureInfo
{
    public int Cpu { get; set; }
    public int Gpu { get; set; }
}

public class FanInfo
{
    public int Fan1Rpm { get; set; }
    public int Fan1Percent { get; set; }
    public int Fan2Rpm { get; set; }
    public int Fan2Percent { get; set; }
}

public class PerformanceInfo
{
    public string Mode { get; set; } = "";
    public bool HoldEnabled { get; set; }
    public int HoldIntervalSeconds { get; set; }
    public int? ThermalPowerLimit { get; set; }
}
