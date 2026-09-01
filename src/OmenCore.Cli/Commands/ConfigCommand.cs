using System.CommandLine;
using OmenCore.Models;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Configuration get/set/show. Deliberately does NOT cover all of AppConfig - it's a much
/// larger, more organically-grown object than OmenCore.Linux's TOML schema (60+ top-level
/// properties plus several nested settings objects), and a 1:1 key mapping over all of it would
/// be its own multi-day scoping exercise, not something to guess at for a first pass. Instead
/// this exposes the handful of settings that are actually useful to read/toggle from a script:
/// polling interval, log verbosity, diagnostics/telemetry opt-in, fan/performance linking, and
/// the Quiet Safety Monitor's threshold - the same kind of curated (not exhaustive) subset
/// Linux's own config command exposes, not a literal field-for-field mirror.
///
/// Bypasses CliContext deliberately - a config read/write has no reason to pay for
/// HardwareBringup's NVAPI/PawnIO/WMI probing, so this talks to AppHost.Configuration directly.
///
/// Examples:
///   omencore-cli config --show
///   omencore-cli config --get fan.link_to_performance
///   omencore-cli config --set safety.quiet_safety_temp_c=85
/// </summary>
public static class ConfigCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Show or change a small set of OmenCore settings");

        var showOption = new Option<bool>(
            aliases: new[] { "--show", "-s" },
            description: "Show the current values of the supported keys");

        var getOption = new Option<string?>(
            aliases: new[] { "--get" },
            description: "Get a single config value by key");

        var setOption = new Option<string?>(
            aliases: new[] { "--set" },
            description: "Set a config value (key=value)");

        command.AddOption(showOption);
        command.AddOption(getOption);
        command.AddOption(setOption);

        command.SetHandler((show, get, set) =>
        {
            Handle(show, get, set);
        }, showOption, getOption, setOption);

        return command;
    }

    private static void Handle(bool show, string? get, string? set)
    {
        var config = AppHost.Configuration.Load();

        if (!string.IsNullOrWhiteSpace(set))
        {
            var parts = set.Split('=', 2);
            if (parts.Length != 2)
            {
                PrintError("Invalid format. Use --set key=value");
                return;
            }

            if (!TrySetValue(config, parts[0].Trim().ToLowerInvariant(), parts[1].Trim(), out var error))
            {
                PrintError(error ?? "Unsupported key. Run --show to see supported keys.");
                return;
            }

            AppHost.Configuration.Save(config);
            PrintSuccess($"Set {parts[0].Trim().ToLowerInvariant()} = {parts[1].Trim()}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(get))
        {
            var value = GetValue(config, get.Trim().ToLowerInvariant());
            Console.WriteLine(value ?? "(unknown key - run --show to see supported keys)");
            return;
        }

        ShowConfig(config);
    }

    private static void ShowConfig(AppConfig config)
    {
        Console.WriteLine();
        Console.WriteLine("=== OmenCore Configuration (supported keys only) ===");
        Console.WriteLine();
        Console.WriteLine($"  general.monitoring_interval_ms   = {config.MonitoringIntervalMs}");
        Console.WriteLine($"  general.log_level                = {config.LogLevel}");
        Console.WriteLine($"  general.enable_diagnostics        = {config.EnableDiagnostics}");
        Console.WriteLine($"  general.telemetry_enabled         = {config.TelemetryEnabled}");
        Console.WriteLine($"  fan.link_to_performance           = {config.LinkFanToPerformanceMode}");
        Console.WriteLine($"  safety.quiet_safety_enabled       = {config.QuietSafety.Enabled}");
        Console.WriteLine($"  safety.quiet_safety_temp_c        = {config.QuietSafety.SafetyOnTempC}");
        Console.WriteLine();
        Console.WriteLine("This is a curated subset, not the full config file. See docs/ROADMAP_v4.3.0.md.");
        Console.WriteLine();
    }

    private static string? GetValue(AppConfig config, string key) => key switch
    {
        "general.monitoring_interval_ms" => config.MonitoringIntervalMs.ToString(),
        "general.log_level" => config.LogLevel.ToString(),
        "general.enable_diagnostics" => config.EnableDiagnostics.ToString(),
        "general.telemetry_enabled" => config.TelemetryEnabled.ToString(),
        "fan.link_to_performance" => config.LinkFanToPerformanceMode.ToString(),
        "safety.quiet_safety_enabled" => config.QuietSafety.Enabled.ToString(),
        "safety.quiet_safety_temp_c" => config.QuietSafety.SafetyOnTempC.ToString(),
        _ => null,
    };

    private static bool TrySetValue(AppConfig config, string key, string rawValue, out string? error)
    {
        error = null;

        switch (key)
        {
            case "general.monitoring_interval_ms":
                return TrySetInt(rawValue, 250, 60000, v => config.MonitoringIntervalMs = v, out error);
            case "general.log_level":
                if (!Enum.TryParse<OmenCore.Services.LogLevel>(rawValue, ignoreCase: true, out var level))
                {
                    error = "Expected one of: Error, Warning, Info, Debug.";
                    return false;
                }
                config.LogLevel = level;
                return true;
            case "general.enable_diagnostics":
                return TrySetBool(rawValue, v => config.EnableDiagnostics = v, out error);
            case "general.telemetry_enabled":
                return TrySetBool(rawValue, v => config.TelemetryEnabled = v, out error);
            case "fan.link_to_performance":
                return TrySetBool(rawValue, v => config.LinkFanToPerformanceMode = v, out error);
            case "safety.quiet_safety_enabled":
                return TrySetBool(rawValue, v => config.QuietSafety.Enabled = v, out error);
            case "safety.quiet_safety_temp_c":
                return TrySetDouble(rawValue, 70, 100, v => config.QuietSafety.SafetyOnTempC = v, out error);
            default:
                error = "Unknown key. Run --show to see supported keys.";
                return false;
        }
    }

    private static bool TrySetBool(string raw, Action<bool> setter, out string? error)
    {
        if (!bool.TryParse(raw, out var value))
        {
            error = "Expected true or false.";
            return false;
        }
        setter(value);
        error = null;
        return true;
    }

    private static bool TrySetInt(string raw, int min, int max, Action<int> setter, out string? error)
    {
        if (!int.TryParse(raw, out var value))
        {
            error = "Expected an integer value.";
            return false;
        }
        if (value < min || value > max)
        {
            error = $"Value must be in range {min}-{max}.";
            return false;
        }
        setter(value);
        error = null;
        return true;
    }

    private static bool TrySetDouble(string raw, double min, double max, Action<double> setter, out string? error)
    {
        if (!double.TryParse(raw, out var value))
        {
            error = "Expected a numeric value.";
            return false;
        }
        if (value < min || value > max)
        {
            error = $"Value must be in range {min}-{max}.";
            return false;
        }
        setter(value);
        error = null;
        return true;
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
