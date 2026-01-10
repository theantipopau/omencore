using System.Text.Json.Serialization;

namespace OmenCore.Linux;

/// <summary>
/// JSON serialization context for AOT/trimming support.
/// Using source generators instead of reflection for JSON serialization.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SystemStatus))]
[JsonSerializable(typeof(Commands.DiagnoseInfo))]
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
    public long Timestamp { get; set; }
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
}
