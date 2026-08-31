using System.CommandLine;
using System.Text.Json;
using OmenCore.Models;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Status command - shows current system state. Mirrors OmenCore.Linux's `status` command in
/// shape (human-readable box by default, --json for scripting) but reads through OmenCore.Core's
/// HardwareBringup/FanController/PerformanceModeService instead of Linux sysfs paths.
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

        command.SetHandler(json =>
        {
            Handle(json);
        }, jsonOption);

        return command;
    }

    private static void Handle(bool jsonOutput)
    {
        var ctx = CliContext.Create();
        var bringup = ctx.Bringup;
        var fanController = bringup.FanController;
        var fanReadings = fanController.IsAvailable ? fanController.ReadFanSpeeds().ToList() : new List<FanTelemetry>();
        var currentMode = ctx.PerformanceModeService.GetCurrentMode();

        if (jsonOutput)
        {
            var status = new
            {
                version = typeof(StatusCommand).Assembly.GetName().Version?.ToString() ?? "unknown",
                model = new
                {
                    productId = bringup.Capabilities.ProductId,
                    boardId = bringup.Capabilities.BoardId,
                    modelName = bringup.Capabilities.ModelName,
                    isKnownModel = bringup.Capabilities.IsKnownModel,
                },
                ecAccess = new
                {
                    available = bringup.EcAccess?.IsAvailable ?? false,
                    backend = bringup.EcBackend,
                },
                fanController = new
                {
                    available = fanController.IsAvailable,
                    backend = fanController.Backend,
                    status = fanController.Status,
                },
                fans = fanReadings.Select(f => new { name = f.Name, rpm = f.SpeedRpm, dutyPercent = f.DutyCyclePercent, source = f.RpmSourceDisplay }),
                performanceMode = currentMode,
                capabilityWarning = bringup.CapabilityWarning,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== OmenCore CLI - System Status ===");
        Console.WriteLine();
        Console.WriteLine($"  Model:        {(string.IsNullOrEmpty(bringup.Capabilities.ModelName) ? "(unknown)" : bringup.Capabilities.ModelName)}");
        Console.WriteLine($"  Product ID:   {bringup.Capabilities.ProductId}");
        Console.WriteLine($"  Board ID:     {bringup.Capabilities.BoardId}");
        Console.WriteLine($"  Known model:  {(bringup.Capabilities.IsKnownModel ? "yes (database entry)" : "no (conservative defaults)")}");
        if (!string.IsNullOrEmpty(bringup.CapabilityWarning))
        {
            Console.WriteLine($"  Warning:      {bringup.CapabilityWarning}");
        }
        Console.WriteLine();
        Console.WriteLine($"  EC access:    {(bringup.EcAccess?.IsAvailable == true ? "available" : "unavailable")} ({bringup.EcBackend})");
        Console.WriteLine($"  Fan control:  {(fanController.IsAvailable ? "available" : "unavailable")} ({fanController.Backend}) - {fanController.Status}");
        Console.WriteLine();

        if (fanReadings.Count > 0)
        {
            Console.WriteLine("  Fans:");
            foreach (var fan in fanReadings)
            {
                Console.WriteLine($"    {fan.Name,-12} {fan.DisplayRpmText,-24} duty {fan.DutyCyclePercent,3}%  [{fan.RpmSourceDisplay}]");
            }
        }
        else
        {
            Console.WriteLine("  Fans:         N/A - fan control unavailable");
        }

        Console.WriteLine();
        Console.WriteLine($"  Performance mode: {currentMode ?? "(none applied yet this session)"}");
        Console.WriteLine();
    }
}
