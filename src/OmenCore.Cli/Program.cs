using System.CommandLine;
using OmenCore.Cli.Commands;

namespace OmenCore.Cli;

/// <summary>
/// OmenCore CLI - command-line control for HP OMEN/Victus laptops on Windows.
///
/// Built on the OmenCore.Core extraction (docs/ROADMAP_v4.3.0.md): status/fan/performance/
/// keyboard/monitor, talking to the same FanService/PerformanceModeService/
/// KeyboardLightingService the WPF app uses via CliContext's bootstrap. config and daemon
/// (continuous curve/hold, matching OmenCore.Linux's `daemon` command) are not implemented yet -
/// see docs/ROADMAP_v4.3.0.md for what's left.
///
/// Requires administrator privileges (see app.manifest) - same reason OmenCoreApp's manifest
/// does: PawnIO EC/MSR access and LibreHardwareMonitor's sensor reads need elevation.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("OmenCore CLI - HP OMEN/Victus laptop control utility (Windows)");

        rootCommand.AddCommand(StatusCommand.Create());
        rootCommand.AddCommand(FanCommand.Create());
        rootCommand.AddCommand(PerformanceCommand.Create());
        rootCommand.AddCommand(KeyboardCommand.Create());
        rootCommand.AddCommand(MonitorCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
