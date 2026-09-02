using System.CommandLine;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Monitor command - real-time monitoring of system stats.
/// 
/// Example:
///   omencore-cli monitor
///   omencore-cli monitor --interval 500
/// </summary>
public static class MonitorCommand
{
    public static Command Create()
    {
        var command = new Command("monitor", "Real-time system monitoring (press Ctrl+C to exit)");
        
        var intervalOption = new Option<int>(
            aliases: new[] { "--interval", "-i" },
            getDefaultValue: () => 1000,
            description: "Update interval in milliseconds");
        
        command.AddOption(intervalOption);
        
        command.SetHandler(async (interval) =>
        {
            await HandleMonitorCommandAsync(interval);
        }, intervalOption);
        
        return command;
    }
    
    private static async Task HandleMonitorCommandAsync(int interval)
    {
        var ec = new LinuxEcController();
        var hwmon = new LinuxHwMonController();
        
        Console.CursorVisible = false;
        Console.Clear();
        
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Console.SetCursorPosition(0, 0);
                PrintMonitorDisplay(ec, hwmon);
                
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit
        }
        finally
        {
            Console.CursorVisible = true;
            Console.WriteLine("\n\nMonitoring stopped.");
        }
    }
    
    private static void PrintMonitorDisplay(LinuxEcController ec, LinuxHwMonController hwmon)
    {
        var now = DateTime.Now;
        
        // Get data. Routed through LinuxTelemetryResolver rather than this command's own
        // hwmon-then-EC chain (the previous shape here) - that ad-hoc chain never queried NVML at
        // all, which is exactly the GPU-telemetry gap GitHub #186 reported (0C/unavailable on a
        // real NVIDIA laptop GPU that nvidia-smi read correctly). The resolver already has NVML
        // wired in as its first-priority source.
        var cpuReading = LinuxTelemetryResolver.GetCpuTemperature(ec, hwmon);
        var gpuReading = LinuxTelemetryResolver.GetGpuTemperature(ec, hwmon);
        var cpuTemp = cpuReading?.Temperature;
        var gpuTemp = gpuReading?.Temperature;
        var nvmlGpu = NvmlInterop.TryGetPrimaryGpu();
        var (fan1Rpm, fan2Rpm) = ec.IsAvailable ? ec.GetFanSpeeds() : (0, 0);
        var (fan1Pct, fan2Pct) = ec.IsAvailable ? ec.GetFanSpeedPercent() : (0, 0);
        
        // Temperature bar
        var cpuBar = GetProgressBar(cpuTemp ?? 0, 100, 20);
        var gpuBar = GetProgressBar(gpuTemp ?? 0, 100, 20);
        var fan1Bar = GetProgressBar(fan1Pct, 100, 20);
        var fan2Bar = GetProgressBar(fan2Pct, 100, 20);
        var gpuPowerText = nvmlGpu?.PowerWatts.HasValue == true ? $"{nvmlGpu.PowerWatts.Value:F0}W" : "  -";
        var gpuUsageText = nvmlGpu?.UtilizationPercent.HasValue == true ? $"{nvmlGpu.UtilizationPercent.Value,3}%" : " -  ";

        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   OmenCore Linux Monitor            {now:HH:mm:ss}   [Ctrl+C to exit]   ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║   TEMPERATURES                                                ║");
        Console.WriteLine($"║   CPU: {GetTempColorCode(cpuTemp ?? 0)}{cpuTemp,3}°C\u001b[0m [{cpuBar}]                    ║");
        Console.WriteLine($"║   GPU: {GetTempColorCode(gpuTemp ?? 0)}{gpuTemp,3}°C\u001b[0m [{gpuBar}]  {gpuUsageText} {gpuPowerText}  ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║   FAN SPEEDS                                                  ║");
        Console.WriteLine($"║   Fan 1 (CPU): {fan1Rpm,5} RPM  {fan1Pct,3}% [{fan1Bar}]       ║");
        Console.WriteLine($"║   Fan 2 (GPU): {fan2Rpm,5} RPM  {fan2Pct,3}% [{fan2Bar}]       ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    }
    
    private static string GetProgressBar(int value, int max, int width)
    {
        var filled = (int)((double)value / max * width);
        filled = Math.Clamp(filled, 0, width);
        
        var color = value switch
        {
            < 40 => "\u001b[32m",  // Green
            < 70 => "\u001b[33m",  // Yellow
            _ => "\u001b[31m"       // Red
        };
        
        return $"{color}{new string('█', filled)}\u001b[90m{new string('░', width - filled)}\u001b[0m";
    }
    
    private static string GetTempColorCode(int temp)
    {
        return temp switch
        {
            < 50 => "\u001b[32m",  // Green
            < 70 => "\u001b[33m",  // Yellow
            < 85 => "\u001b[31m",  // Red
            _ => "\u001b[35m"       // Magenta (critical)
        };
    }
}
