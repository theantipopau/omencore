using System.CommandLine;
using OmenCore.Cli.Commands;

namespace OmenCore.Cli;

/// <summary>
/// OmenCore CLI - command-line control for HP OMEN/Victus laptops on Windows.
///
/// Built on the OmenCore.Core extraction (docs/ROADMAP_v4.3.0.md): status/fan/performance/
/// keyboard/monitor/config/daemon, talking to the same FanService/PerformanceModeService/
/// KeyboardLightingService/ConfigurationService the WPF app uses via CliContext's bootstrap
/// (config bypasses CliContext - see ConfigCommand's own doc comment). daemon is foreground-only
/// - no Windows Service or Scheduled Task self-installation, a deliberately separate decision
/// from adding the command itself; see DaemonCommand's own doc comment. `diagnose` is not
/// implemented - see docs/ROADMAP_v4.3.0.md for what's left.
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
        rootCommand.AddCommand(ConfigCommand.Create());
        rootCommand.AddCommand(DaemonCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
