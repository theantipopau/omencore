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
    private const int MinPlausibleTemperatureC = 1;
    private const int MaxPlausibleTemperatureC = 125;

    public static LinuxTemperatureReading? GetCpuTemperature(LinuxEcController ec, LinuxHwMonController hwmon)
    {
        return FilterPlausible(hwmon.GetCpuTemperatureReading()) ?? CreateEcReading(ec.GetCpuTemperature(), CpuEcPath);
    }

    public static LinuxTemperatureReading? GetGpuTemperature(LinuxEcController ec, LinuxHwMonController hwmon)
    {
        // NVML first: it's what nvidia-smi itself reads from, and it's authoritative for NVIDIA
        // GPUs regardless of whether the proprietary driver happens to register a hwmon device on
        // this system (it often doesn't - see GitHub #186, where hwmon and EC both came back empty
        // for a real RTX 5080 laptop GPU that nvidia-smi read correctly the whole time).
        return CreateNvmlReading() ?? FilterPlausible(hwmon.GetGpuTemperatureReading()) ?? CreateEcReading(ec.GetGpuTemperature(), GpuEcPath);
    }

    private static LinuxTemperatureReading? CreateNvmlReading()
    {
        var snapshot = NvmlInterop.TryGetPrimaryGpu();
        if (snapshot?.TemperatureC is not int temperature || !IsPlausibleTemperature(temperature))
        {
            return null;
        }

        return new LinuxTemperatureReading
        {
            Temperature = temperature,
            Source = "nvml",
            Path = "nvml:0"
        };
    }

    private static LinuxTemperatureReading? CreateEcReading(int? temperature, string path)
    {
        if (!temperature.HasValue || !IsPlausibleTemperature(temperature.Value))
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

    private static LinuxTemperatureReading? FilterPlausible(LinuxTemperatureReading? reading)
    {
        return reading != null && IsPlausibleTemperature(reading.Temperature)
            ? reading
            : null;
    }

    private static bool IsPlausibleTemperature(int temperature)
    {
        return temperature >= MinPlausibleTemperatureC && temperature <= MaxPlausibleTemperatureC;
    }
}
