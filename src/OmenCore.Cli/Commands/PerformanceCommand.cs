using System.CommandLine;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Performance mode command.
///
/// Examples:
///   omencore-cli performance --status
///   omencore-cli performance --mode balanced
/// </summary>
public static class PerformanceCommand
{
    public static Command Create()
    {
        var command = new Command("performance", "Control performance mode");
        command.AddAlias("perf");

        var statusOption = new Option<bool>(
            aliases: new[] { "--status", "-S" },
            description: "Show current performance mode");

        var modeOption = new Option<string?>(
            aliases: new[] { "--mode", "-m" },
            description: "Performance mode name from config - run --status to see what's configured");

        command.AddOption(statusOption);
        command.AddOption(modeOption);

        command.SetHandler((status, mode) =>
        {
            Handle(status, mode);
        }, statusOption, modeOption);

        return command;
    }

    private static void Handle(bool status, string? mode)
    {
        var ctx = CliContext.Create();
        var service = ctx.PerformanceModeService;

        if (!string.IsNullOrWhiteSpace(mode))
        {
            var target = ctx.Config.PerformanceModes.FirstOrDefault(m => string.Equals(m.Name, mode, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                PrintError($"No performance mode named '{mode}' in config.");
                var names = string.Join(", ", ctx.Config.PerformanceModes.Select(m => m.Name));
                Console.WriteLine(string.IsNullOrEmpty(names) ? "  (no performance modes configured)" : $"  Available: {names}");
                return;
            }

            service.Apply(target);
            PrintSuccess($"Performance mode applied: {target.Name}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== Performance Status ===");
        Console.WriteLine($"  Current mode (this session): {service.GetCurrentMode() ?? "(none applied yet)"}");
        Console.WriteLine($"  Fan/performance linked: {(service.LinkFanToPerformanceMode ? "yes" : "no")}");
        Console.WriteLine();
    }

    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"OK: {message}");
        Console.ResetColor();
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {message}");
        Console.ResetColor();
    }
}
