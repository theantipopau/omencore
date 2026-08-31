using System.CommandLine;
using OmenCore.Hardware;
using OmenCore.Services;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Fan control command. Scoped to the same fixed presets (auto/quiet/balanced/gaming/max) the
/// GUI's quick-preset buttons apply - a custom curve preset would apply once here but not keep
/// re-evaluating temperature after the process exits (that needs FanService.Start()'s background
/// monitor loop running continuously, i.e. a persistent process - out of scope until a `daemon`
/// command exists, matching OmenCore.Linux's shape).
///
/// Examples:
///   omencore-cli fan --status
///   omencore-cli fan --profile quiet
///   omencore-cli fan --profile max
/// </summary>
public static class FanCommand
{
    public static Command Create()
    {
        var command = new Command("fan", "Control fan profile");

        var statusOption = new Option<bool>(
            aliases: new[] { "--status", "-S" },
            description: "Show current fan speeds and active preset");

        var profileOption = new Option<string?>(
            aliases: new[] { "--profile", "-p" },
            description: "Fan preset name from config (e.g. auto, quiet, balanced, gaming, max) - run --status to see what's configured");

        command.AddOption(statusOption);
        command.AddOption(profileOption);

        command.SetHandler((status, profile) =>
        {
            Handle(status, profile);
        }, statusOption, profileOption);

        return command;
    }

    private static void Handle(bool status, string? profile)
    {
        var ctx = CliContext.Create();
        var fanService = ctx.FanService;
        var fanController = ctx.Bringup.FanController;

        if (!fanController.IsAvailable)
        {
            PrintError($"Fan control unavailable: {fanController.Status}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            var preset = ctx.Config.FanPresets.FirstOrDefault(p => string.Equals(p.Name, profile, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                PrintError($"No fan preset named '{profile}' in config.");
                var names = string.Join(", ", ctx.Config.FanPresets.Select(p => p.Name));
                Console.WriteLine(string.IsNullOrEmpty(names) ? "  (no presets configured)" : $"  Available: {names}");
                return;
            }

            if (fanService.ApplyPreset(preset))
            {
                PrintSuccess($"Fan preset applied: {preset.Name}");
            }
            else
            {
                PrintError($"Failed to apply fan preset: {preset.Name} - check logs (%LOCALAPPDATA%\\OmenCore\\logs) for the reason");
            }
            return;
        }

        ShowStatus(fanController, fanService);
    }

    private static void ShowStatus(IFanController fanController, FanService fanService)
    {
        Console.WriteLine();
        Console.WriteLine("=== Fan Status ===");
        Console.WriteLine($"  Backend: {fanController.Backend} - {fanController.Status}");
        Console.WriteLine($"  Active preset (this session): {fanService.ActivePresetName ?? "(none applied yet)"}");
        Console.WriteLine();

        foreach (var fan in fanController.ReadFanSpeeds())
        {
            Console.WriteLine($"  {fan.Name,-12} {fan.DisplayRpmText,-24} duty {fan.DutyCyclePercent,3}%  [{fan.RpmSourceDisplay}]");
        }

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
