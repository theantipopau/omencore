using System.CommandLine;
using System.Diagnostics;
using OmenCore.Linux.Config;
using OmenCore.Linux.Daemon;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Daemon management command for running OmenCore as a background service.
///
/// Examples:
///   omencore-cli daemon --run              (run daemon in foreground)
///   omencore-cli daemon --start            (start via systemd)
///   omencore-cli daemon --stop             (stop via systemd)
///   omencore-cli daemon --status           (show status)
///   omencore-cli daemon --install          (install systemd service)
///   omencore-cli daemon --uninstall        (remove systemd service)
///   omencore-cli daemon --generate-service (print service file)
///   omencore-cli daemon --generate-config  (print default config)
/// </summary>
public static class DaemonCommand
{
    private const string ServiceName = "omencore";
    private const string PidFile = "/var/run/omencore.pid";
    private const string SystemConfigDir = "/etc/omencore";

    private static readonly string SystemdServicePath = $"/etc/systemd/system/{ServiceName}.service";

    public static Command Create()
    {
        var command = new Command("daemon", "Manage OmenCore background daemon");

        var runOption = new Option<bool>(
            aliases: new[] { "--run", "-r" },
            description: "Run daemon in foreground (use this in systemd service)");

        var startOption = new Option<bool>(
            aliases: new[] { "--start" },
            description: "Start the daemon via systemd");

        var stopOption = new Option<bool>(
            aliases: new[] { "--stop" },
            description: "Stop the daemon via systemd");

        var statusOption = new Option<bool>(
            aliases: new[] { "--status" },
            description: "Check daemon status");

        var installOption = new Option<bool>(
            aliases: new[] { "--install" },
            description: "Install systemd service");

        var uninstallOption = new Option<bool>(
            aliases: new[] { "--uninstall" },
            description: "Uninstall systemd service");

        var generateServiceOption = new Option<bool>(
            aliases: new[] { "--generate-service" },
            description: "Print systemd service file to stdout");

        var generateConfigOption = new Option<bool>(
            aliases: new[] { "--generate-config" },
            description: "Print default TOML configuration to stdout");

        var configOption = new Option<string?>(
            aliases: new[] { "--config", "-c" },
            description: "Path to TOML configuration file");

        command.AddOption(runOption);
        command.AddOption(startOption);
        command.AddOption(stopOption);
        command.AddOption(statusOption);
        command.AddOption(installOption);
        command.AddOption(uninstallOption);
        command.AddOption(generateServiceOption);
        command.AddOption(generateConfigOption);
        command.AddOption(configOption);

        command.SetHandler(async (context) =>
        {
            var run = context.ParseResult.GetValueForOption(runOption);
            var start = context.ParseResult.GetValueForOption(startOption);
            var stop = context.ParseResult.GetValueForOption(stopOption);
            var install = context.ParseResult.GetValueForOption(installOption);
            var uninstall = context.ParseResult.GetValueForOption(uninstallOption);
            var generateService = context.ParseResult.GetValueForOption(generateServiceOption);
            var generateConfig = context.ParseResult.GetValueForOption(generateConfigOption);
            var config = context.ParseResult.GetValueForOption(configOption);

            await HandleDaemonCommandAsync(run, start, stop, install, uninstall, generateService, generateConfig, config);
        });

        return command;
    }

    private static async Task HandleDaemonCommandAsync(
        bool run,
        bool start,
        bool stop,
        bool install,
        bool uninstall,
        bool generateService,
        bool generateConfig,
        string? configPath)
    {
        if (generateConfig)
        {
            Console.WriteLine(OmenCoreConfig.GenerateDefaultToml());
            return;
        }

        if (generateService)
        {
            PrintSystemdService();
            return;
        }

        if (install)
        {
            await InstallServiceAsync();
            return;
        }

        if (uninstall)
        {
            await UninstallServiceAsync();
            return;
        }

        if (run)
        {
            await RunDaemonAsync(configPath);
            return;
        }

        if (start)
        {
            await StartDaemonAsync();
            return;
        }

        if (stop)
        {
            await StopDaemonAsync();
            return;
        }

        await ShowStatusAsync(configPath);
    }

    private static async Task RunDaemonAsync(string? configPath)
    {
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            WriteColor(ConsoleColor.Red, "Error: Root privileges required to run daemon");
            return;
        }

        // Self-heal older service files that predate the extraction directory setting.
        var extractDir = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if (string.IsNullOrEmpty(extractDir))
        {
            extractDir = "/var/tmp/omencore";
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", extractDir);
            WriteColor(ConsoleColor.Yellow, $"Warning: DOTNET_BUNDLE_EXTRACT_BASE_DIR not set; using {extractDir}");
            Console.WriteLine("To fix permanently: sudo omencore-cli daemon --install");
        }

        try
        {
            Directory.CreateDirectory(extractDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to create {extractDir}: {ex.Message}");
        }

        var config = OmenCoreConfig.Load(configPath);

        using var daemon = new OmenCoreDaemon(config);
        await daemon.RunAsync();
    }

    private static void PrintSystemdService()
    {
        Console.Write(BuildSystemdService(GetCurrentExecutablePath()));
    }

    private static string GetCurrentExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ?? "/usr/local/bin/omencore-cli";
    }

    private static string BuildSystemdService(string exePath)
    {
        return $@"[Unit]
Description=OmenCore HP OMEN Laptop Control Daemon
Documentation=https://github.com/theantipopau/omencore
After=network.target

[Service]
Type=simple
ExecStartPre=-/usr/bin/mkdir -p /var/tmp/omencore
ExecStart={exePath} daemon --run
ExecReload=/bin/kill -HUP $MAINPID
Restart=on-failure
RestartSec=5
User=root
Environment=HOME=/root
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/tmp/omencore

# Security hardening
ProtectSystem=strict
ProtectHome=read-only
ReadWritePaths=/var/run /var/log /sys/kernel/debug/ec /var/tmp/omencore
NoNewPrivileges=false
PrivateTmp=true

[Install]
WantedBy=multi-user.target
";
    }

    private static async Task InstallServiceAsync()
    {
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            WriteColor(ConsoleColor.Red, "Error: Root privileges required to install service");
            return;
        }

        try
        {
            await File.WriteAllTextAsync(SystemdServicePath, BuildSystemdService(GetCurrentExecutablePath()));

            if (!Directory.Exists(SystemConfigDir))
            {
                Directory.CreateDirectory(SystemConfigDir);
                await File.WriteAllTextAsync(OmenCoreConfig.SystemConfigPath, OmenCoreConfig.GenerateDefaultToml());
                Console.WriteLine($"Created default configuration at {OmenCoreConfig.SystemConfigPath}");
            }

            await RunCommandAsync("systemctl", "daemon-reload");
            await RunCommandAsync("systemctl", "enable omencore.service");

            WriteColor(ConsoleColor.Green, "OmenCore systemd service installed.");
            Console.WriteLine($"Configuration: {OmenCoreConfig.SystemConfigPath}");
            Console.WriteLine($"Service file:  {SystemdServicePath}");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  sudo omencore-cli daemon --start");
            Console.WriteLine("  sudo omencore-cli daemon --status");
            Console.WriteLine("  journalctl -u omencore -f");
        }
        catch (Exception ex)
        {
            WriteColor(ConsoleColor.Red, $"Error installing service: {ex.Message}");
        }
    }

    private static async Task UninstallServiceAsync()
    {
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            WriteColor(ConsoleColor.Red, "Error: Root privileges required to uninstall service");
            return;
        }

        try
        {
            await RunCommandAsync("systemctl", "stop omencore.service");
            await RunCommandAsync("systemctl", "disable omencore.service");

            if (File.Exists(SystemdServicePath))
            {
                File.Delete(SystemdServicePath);
            }

            await RunCommandAsync("systemctl", "daemon-reload");

            WriteColor(ConsoleColor.Green, "Systemd service uninstalled");
            Console.WriteLine();
            Console.WriteLine($"Note: configuration at {OmenCoreConfig.SystemConfigPath} was preserved.");
            Console.WriteLine("Delete it manually if no longer needed.");
        }
        catch (Exception ex)
        {
            WriteColor(ConsoleColor.Red, $"Error uninstalling service: {ex.Message}");
        }
    }

    private static async Task StartDaemonAsync()
    {
        if (!File.Exists(SystemdServicePath))
        {
            WriteColor(ConsoleColor.Yellow, "Systemd service not installed.");
            Console.WriteLine();
            Console.WriteLine("Install with: sudo omencore-cli daemon --install");
            Console.WriteLine("Or run directly: sudo omencore-cli daemon --run");
            return;
        }

        await RunCommandAsync("systemctl", "start omencore.service");
        WriteColor(ConsoleColor.Green, "Service started via systemd");
        Console.WriteLine();
        Console.WriteLine("Check status: sudo omencore-cli daemon --status");
        Console.WriteLine("View logs:    journalctl -u omencore -f");
    }

    private static async Task StopDaemonAsync()
    {
        if (File.Exists(SystemdServicePath))
        {
            await RunCommandAsync("systemctl", "stop omencore.service");
            WriteColor(ConsoleColor.Green, "Service stopped");
            return;
        }

        if (File.Exists(PidFile))
        {
            var pid = await File.ReadAllTextAsync(PidFile);
            await RunCommandAsync("kill", pid.Trim());
            File.Delete(PidFile);
            WriteColor(ConsoleColor.Green, "Daemon stopped");
            return;
        }

        WriteColor(ConsoleColor.Yellow, "No running daemon found");
    }

    private static async Task ShowStatusAsync(string? configPath)
    {
        var serviceInstalled = File.Exists(SystemdServicePath);
        var serviceActive = serviceInstalled ? await TryGetSystemdActiveAsync() : null;
        var userConfig = File.Exists(OmenCoreConfig.DefaultConfigPath);
        var systemConfig = File.Exists(OmenCoreConfig.SystemConfigPath);
        var config = OmenCoreConfig.Load(configPath);

        Console.WriteLine();
        Console.WriteLine("OmenCore Linux - Daemon Status");
        Console.WriteLine("--------------------------------");
        Console.WriteLine($"Systemd service : {(serviceInstalled ? "Installed" : "Not installed")}");

        if (serviceInstalled)
        {
            Console.WriteLine($"Service status  : {FormatServiceStatus(serviceActive)}");
        }

        Console.WriteLine($"User config     : {(userConfig ? "Found" : "Not found")} ({OmenCoreConfig.DefaultConfigPath})");
        Console.WriteLine($"System config   : {(systemConfig ? "Found" : "Not found")} ({OmenCoreConfig.SystemConfigPath})");
        Console.WriteLine();
        Console.WriteLine("Performance hold");
        Console.WriteLine($"Mode            : {config.Performance.Mode}");
        Console.WriteLine($"Hold enabled    : {FormatEnabled(config.Performance.HoldEnabled)}");
        Console.WriteLine($"Hold interval   : {config.Performance.HoldIntervalSeconds}s");
        Console.WriteLine($"Power limit     : {FormatPowerLimit(config.Performance.ThermalPowerLimit)}");
        Console.WriteLine();
        Console.WriteLine("Commands");
        Console.WriteLine("Install service : sudo omencore-cli daemon --install");
        Console.WriteLine("Start service   : sudo omencore-cli daemon --start");
        Console.WriteLine("Stop service    : sudo omencore-cli daemon --stop");
        Console.WriteLine("View logs       : journalctl -u omencore -f");
        Console.WriteLine("Print service   : omencore-cli daemon --generate-service");
        Console.WriteLine("Print config    : omencore-cli daemon --generate-config");
        Console.WriteLine();
    }

    private static string FormatServiceStatus(bool? serviceActive)
    {
        return serviceActive switch
        {
            true => "Running",
            false => "Stopped",
            null => "Unknown"
        };
    }

    private static string FormatEnabled(bool enabled) => enabled ? "Enabled" : "Disabled";

    private static string FormatPowerLimit(int? thermalPowerLimit)
    {
        return thermalPowerLimit.HasValue ? thermalPowerLimit.Value.ToString() : "Not configured";
    }

    private static async Task<bool?> TryGetSystemdActiveAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = "is-active omencore.service",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return string.Equals(output.Trim(), "active", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task RunCommandAsync(string command, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }

    private static void WriteColor(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
