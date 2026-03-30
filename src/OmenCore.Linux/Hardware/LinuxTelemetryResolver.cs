namespace OmenCore.Linux.Hardware;

public sealed class LinuxTemperatureReading
{
    public int Temperature { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public static class LinuxTelemetryResolver
{
    private const string CpuEcPath = "ec:0x57";
    private const string GpuEcPath = "ec:0xB7";

    public static LinuxTemperatureReading? GetCpuTemperature(LinuxEcController ec, LinuxHwMonController hwmon)
    {
        return hwmon.GetCpuTemperatureReading() ?? CreateEcReading(ec.GetCpuTemperature(), CpuEcPath);
    }

    public static LinuxTemperatureReading? GetGpuTemperature(LinuxEcController ec, LinuxHwMonController hwmon)
    {
        return hwmon.GetGpuTemperatureReading() ?? CreateEcReading(ec.GetGpuTemperature(), GpuEcPath);
    }

    private static LinuxTemperatureReading? CreateEcReading(int? temperature, string path)
    {
        if (!temperature.HasValue)
        {
            return null;
        }

        return new LinuxTemperatureReading
        {
            Temperature = temperature.Value,
            Source = "ec",
            Path = path
        };
    }
}