using System.CommandLine;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Real-time monitoring, matching OmenCore.Linux's `monitor` command in shape (redraw in place,
/// Ctrl+C to exit, --interval). Reuses CliContext's already-constructed HardwareBringup rather
/// than re-probing hardware every tick - only the actual per-tick reads (fan RPM, temperature)
/// happen inside the loop.
///
/// Examples:
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

        command.SetHandler(async interval =>
        {
            await HandleAsync(interval);
        }, intervalOption);

        return command;
    }

    private static async Task HandleAsync(int interval)
    {
        var ctx = CliContext.Create();
        var thermalProvider = new ThermalSensorProvider(ctx.Bringup.WmiBiosMonitor);

        Console.CursorVisible = false;
        Console.Clear();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Console.SetCursorPosition(0, 0);
                Draw(ctx, thermalProvider);
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit via Ctrl+C.
        }
        finally
        {
            Console.CursorVisible = true;
            Console.WriteLine();
            Console.WriteLine("Monitoring stopped.");
        }
    }

    private static void Draw(CliContext ctx, ThermalSensorProvider thermalProvider)
    {
        var now = DateTime.Now;
        var temps = thermalProvider.ReadTemperatures().ToList();
        var cpuTemp = temps.FirstOrDefault(t => t.Sensor == "CPU Package")?.Celsius;
        var gpuTemp = temps.FirstOrDefault(t => t.Sensor == "GPU")?.Celsius;

        var fanController = ctx.Bringup.FanController;
        var fans = fanController.IsAvailable ? fanController.ReadFanSpeeds().ToList() : new List<FanTelemetry>();

        Console.WriteLine("=== OmenCore Monitor ===".PadRight(60) + $"{now:HH:mm:ss}  [Ctrl+C to exit]");
        Console.WriteLine(new string('-', 60));
        Console.WriteLine();
        Console.WriteLine("Temperatures");
        WriteTempLine("  CPU", cpuTemp);
        WriteTempLine("  GPU", gpuTemp);
        Console.WriteLine();
        Console.WriteLine("Fans");
        if (fans.Count > 0)
        {
            foreach (var fan in fans)
            {
                Console.WriteLine($"  {fan.Name,-12} {fan.DisplayRpmText,-24} duty {fan.DutyCyclePercent,3}%".PadRight(60));
            }
        }
        else
        {
            Console.WriteLine("  N/A - fan control unavailable".PadRight(60));
        }

        // Pad a couple of trailing lines so a shorter-than-previous frame doesn't leave stale
        // characters behind from the last redraw at this cursor position.
        Console.WriteLine(new string(' ', 60));
        Console.WriteLine(new string(' ', 60));
    }

    private static void WriteTempLine(string label, double? celsius)
    {
        Console.Write($"{label,-6}");
        if (celsius is null or <= 0)
        {
            Console.Write("  N/A".PadRight(54));
            Console.WriteLine();
            return;
        }

        Console.ForegroundColor = celsius switch
        {
            < 50 => ConsoleColor.Green,
            < 70 => ConsoleColor.Yellow,
            < 85 => ConsoleColor.Red,
            _ => ConsoleColor.Magenta,
        };
        Console.Write($"{celsius,5:F1}°C");
        Console.ResetColor();
        Console.WriteLine("".PadRight(48));
    }
}
