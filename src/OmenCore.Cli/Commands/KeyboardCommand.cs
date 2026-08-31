using System.CommandLine;
using OmenCore.Models;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Keyboard lighting command. Scoped to a single static color across the whole keyboard,
/// matching OmenCore.Linux's `keyboard --color` - effects (wave/breathing/ripple/etc.), per-zone
/// color, and per-key control all exist on KeyboardLightingService already, but picking a CLI
/// surface for all of that is its own scoping exercise, not bundled into this first pass.
///
/// Examples:
///   omencore-cli keyboard --status
///   omencore-cli keyboard --color FF0000
/// </summary>
public static class KeyboardCommand
{
    public static Command Create()
    {
        var command = new Command("keyboard", "Control keyboard backlight color");

        var statusOption = new Option<bool>(
            aliases: new[] { "--status", "-S" },
            description: "Show keyboard lighting availability and active backend");

        var colorOption = new Option<string?>(
            aliases: new[] { "--color", "-c" },
            description: "Set a static color across the whole keyboard (6-digit hex, e.g. FF0000)");

        command.AddOption(statusOption);
        command.AddOption(colorOption);

        command.SetHandler((status, color) =>
        {
            Handle(status, color);
        }, statusOption, colorOption);

        return command;
    }

    private static void Handle(bool status, string? color)
    {
        var ctx = CliContext.Create();
        var kb = ctx.KeyboardLightingService;

        if (!kb.IsAvailable)
        {
            PrintError($"Keyboard lighting unavailable on this system (backend: {kb.BackendType}).");
            return;
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            var hex = color.Trim().TrimStart('#').ToUpperInvariant();
            if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
            {
                PrintError("Color must be a 6-digit hex value (example: FF0000).");
                return;
            }

            // ApplyEffect is void - it logs "not applied" internally on a backend mismatch
            // rather than returning a signal the caller can check, so this reports what was
            // requested, not a confirmed hardware write. Same honesty gap the DPI/RGB write
            // paths had before this cycle's fixes; not closed here to keep this command's
            // first slice to what KeyboardLightingService's existing API actually supports.
            kb.ApplyEffect(LightingEffectType.Static, hex, hex, new[] { "All" }, speed: 0);
            PrintSuccess($"Keyboard color requested: #{hex} via {kb.BackendType}");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== Keyboard Lighting Status ===");
        Console.WriteLine($"  Available:   {kb.IsAvailable}");
        Console.WriteLine($"  Backend:     {kb.BackendType}");
        Console.WriteLine($"  Per-key:     {kb.IsPerKey}");
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
