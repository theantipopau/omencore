using System.CommandLine;
using System.Text.Json;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Status command - shows current system state.
/// 
/// Examples:
///   omencore-cli status
///   omencore-cli status --json
/// </summary>
public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show current system status");
        
        var jsonOption = new Option<bool>(
            aliases: new[] { "--json", "-j" },
            description: "Output in JSON format");
            
        command.AddOption(jsonOption);
        
        command.SetHandler(async (json) =>
        {
            await HandleStatusCommandAsync(json);
        }, jsonOption);
        
        return command;
    }
    
    private static async Task HandleStatusCommandAsync(bool jsonOutput)
    {
        var ec = new LinuxEcController();
        var hwmon = new LinuxHwMonController();
        var keyboard = new LinuxKeyboardController();
        
        var cpuTemp = hwmon.GetCpuTemperature() ?? ec.GetCpuTemperature();
        var gpuTemp = hwmon.GetGpuTemperature() ?? ec.GetGpuTemperature();
        
        var (fan1Rpm, fan2Rpm) = ec.IsAvailable ? ec.GetFanSpeeds() : (0, 0);
        var (fan1Pct, fan2Pct) = ec.IsAvailable ? ec.GetFanSpeedPercent() : (0, 0);
        
        var perfMode = ec.IsAvailable ? ec.GetPerformanceMode() : PerformanceMode.Default;
        var perfModeStr = perfMode switch
        {
            PerformanceMode.Default => "Default",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            PerformanceMode.Cool => "Cool",
            _ => "Unknown"
        };
        
        if (jsonOutput)
        {
            var status = new
            {
                ec_available = ec.IsAvailable,
                keyboard_available = keyboard.IsAvailable,
                temperatures = new
                {
                    cpu = cpuTemp ?? 0,
                    gpu = gpuTemp ?? 0
                },
                fans = new
                {
                    fan1_rpm = fan1Rpm,
                    fan1_percent = fan1Pct,
                    fan2_rpm = fan2Rpm,
                    fan2_percent = fan2Pct
                },
                performance = new
                {
                    mode = perfModeStr.ToLowerInvariant()
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            Console.WriteLine(json);
            return;
        }
        
        // Human-readable output
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              OmenCore Linux - System Status               ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        
        // EC Access - show detailed status
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║  EC Access: ✓ Available ({ec.AccessMethod})                       ║".PadRight(63) + "║");
        }
        else
        {
            Console.WriteLine("║  EC Access: ✗ Unavailable                                  ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  ⚠️  Neither ec_sys nor hp-wmi detected.                   ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  Try one of:                                               ║");
            Console.WriteLine("║    sudo modprobe ec_sys write_support=1                    ║");
            Console.WriteLine("║    sudo modprobe hp-wmi  (for 2023+ models)                ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  Fedora 43+: ec_sys not in kernel, use hp-wmi              ║");
        }
        
        // Temperatures
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  TEMPERATURES                                             ║");
        Console.WriteLine($"║    CPU Temperature: {cpuTemp ?? 0,3}°C                                ║");
        Console.WriteLine($"║    GPU Temperature: {gpuTemp ?? 0,3}°C                                ║");
        
        // Fans
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  FAN SPEEDS                                               ║");
        
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║    Fan 1 (CPU): {fan1Rpm,5} RPM ({fan1Pct,3}%)                        ║");
            Console.WriteLine($"║    Fan 2 (GPU): {fan2Rpm,5} RPM ({fan2Pct,3}%)                        ║");
        }
        else
        {
            Console.WriteLine("║    N/A - EC access required                               ║");
        }
        
        // Performance Mode
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  PERFORMANCE                                              ║");
        
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║    Mode: {perfModeStr,-48} ║");
        }
        else
        {
            Console.WriteLine("║    N/A - EC access required                               ║");
        }
        
        // Keyboard
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  KEYBOARD LIGHTING                                        ║");
        Console.WriteLine($"║    HP WMI: {(keyboard.IsAvailable ? "✓ Available" : "✗ Unavailable"),-45} ║");
        
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        await Task.CompletedTask;
    }
}
