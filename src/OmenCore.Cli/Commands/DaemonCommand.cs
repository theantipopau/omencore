using System.CommandLine;
using System.Diagnostics;

namespace OmenCore.Cli.Commands;

/// <summary>
/// Foreground curve/hold daemon. Deliberately narrower than OmenCore.Linux's `daemon` command,
/// which also manages a real systemd service (--install/--start/--stop/--uninstall, unit-file
/// generation, PID files). Windows has no equivalent this CLI builds here: the two real options
/// are a genuine Windows Service (its own hosting model - ServiceBase or
/// Microsoft.Extensions.Hosting.WindowsServices, a separate entry point, Service Control Manager
/// interaction) or a Scheduled Task at logon (the pattern OmenCoreApp.exe's own GUI "Start with
/// Windows" setting already uses - SettingsViewModel.SetStartWithWindows, schtasks with a
/// LogonTrigger + HighestAvailable RunLevel). Either is a real, separate design decision - most
/// importantly, whether a CLI daemon should be able to install itself to run unattended at every
/// boot at all, competing with the GUI app for the same fan hardware - not something to default
/// into while adding one command. This first pass only builds the foreground run mode, which
/// needs no such decision: it does nothing until explicitly invoked, and nothing survives it
/// exiting.
///
/// Unlike every other command in this CLI, `daemon` DOES call FanService.Dispose() on exit -
/// see the doc comment on RunAsync for why that's correct here specifically.
///
/// Examples:
///   omencore-cli daemon --profile gaming
///   omencore-cli daemon --status
/// </summary>
public static class DaemonCommand
{
    public static Command Create()
    {
        var command = new Command("daemon", "Run a fan preset in the foreground, with continuous curve/hold support, until Ctrl+C");

        var profileOption = new Option<string?>(
            aliases: new[] { "--profile", "-p" },
            description: "Fan preset name to run under continuous daemon supervision (required to actually run)");

        var statusOption = new Option<bool>(
            aliases: new[] { "--status", "-S" },
            description: "Show whether it's currently safe to run the daemon (e.g. is the GUI app already running)");

        command.AddOption(profileOption);
        command.AddOption(statusOption);

        command.SetHandler(async (profile, status) =>
        {
            await HandleAsync(profile, status);
        }, profileOption, statusOption);

        return command;
    }

    private static async Task HandleAsync(string? profile, bool status)
    {
        if (status || string.IsNullOrWhiteSpace(profile))
        {
            ShowStatus();
            if (string.IsNullOrWhiteSpace(profile) && !status)
            {
                PrintError("--profile <name> is required to run the daemon. See --status for available presets.");
            }
            return;
        }

        await RunAsync(profile);
    }

    private static void ShowStatus()
    {
        var guiRunning = Process.GetProcessesByName("OmenCore").Length > 0;

        Console.WriteLine();
        Console.WriteLine("=== Daemon Status ===");
        Console.WriteLine();
        if (guiRunning)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  WARNING: OmenCore.exe (the GUI app) is currently running.");
            Console.WriteLine("  Running the daemon at the same time means two processes will both");
            Console.WriteLine("  try to own fan control - close the GUI app first, or expect the");
            Console.WriteLine("  daemon's curve to be overridden by whatever the GUI last applied.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("  OmenCore.exe (the GUI app) is not currently running - safe to start the daemon.");
        }

        Console.WriteLine();
        // Config only, deliberately not a full CliContext.Create() - listing preset names has
        // no reason to pay for HardwareBringup's NVAPI/PawnIO/WMI probing.
        var config = AppHost.Configuration.Load();
        var names = config.FanPresets.Select(p => p.Name).ToList();
        Console.WriteLine(names.Count > 0
            ? $"  Available presets: {string.Join(", ", names)}"
            : "  (no fan presets configured)");
        Console.WriteLine();
        Console.WriteLine("  Run:    omencore-cli daemon --profile <name>");
        Console.WriteLine("  Stop:   Ctrl+C (restores BIOS auto fan control before exiting)");
        Console.WriteLine();
        Console.WriteLine("  No install/start/stop/service-management support in this build - this");
        Console.WriteLine("  only runs in the foreground for as long as this process is alive.");
        Console.WriteLine();
    }

    /// <summary>
    /// Every other command in this CLI deliberately never calls FanService.Dispose(), because a
    /// one-shot "fan --profile quiet" is supposed to leave the fan on quiet after the process
    /// exits. A curve or hold preset is different in kind, not just degree: it only stays correct
    /// while something is continuously re-evaluating temperature against it (FanService.Start()'s
    /// MonitorLoop, which this command actually runs, unlike the one-shot commands). Once this
    /// process exits, nothing is left driving that loop - the fan would freeze at whatever RPM it
    /// last computed, which is worse than restoring BIOS auto control cleanly. So this command
    /// disposes on exit; none of the others should.
    /// </summary>
    private static async Task RunAsync(string profile)
    {
        var ctx = CliContext.Create();
        var preset = ctx.Config.FanPresets.FirstOrDefault(p => string.Equals(p.Name, profile, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            PrintError($"No fan preset named '{profile}' in config.");
            var names = string.Join(", ", ctx.Config.FanPresets.Select(p => p.Name));
            Console.WriteLine(string.IsNullOrEmpty(names) ? "  (no presets configured)" : $"  Available: {names}");
            return;
        }

        if (!ctx.Bringup.FanController.IsAvailable)
        {
            PrintError($"Fan control unavailable: {ctx.Bringup.FanController.Status}");
            return;
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        ctx.FanService.Start();
        if (!ctx.FanService.ApplyPreset(preset))
        {
            PrintError($"Failed to apply fan preset: {preset.Name} - check logs (%LOCALAPPDATA%\\OmenCore\\logs) for the reason");
            ctx.FanService.Dispose();
            return;
        }

        PrintSuccess($"Daemon running preset '{preset.Name}'. Press Ctrl+C to stop.");

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(5000, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit via Ctrl+C.
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Stopping daemon - restoring BIOS auto fan control...");
            ctx.FanService.Dispose();
            Console.WriteLine("Stopped.");
        }
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
