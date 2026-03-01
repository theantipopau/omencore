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
            var status = context.ParseResult.GetValueForOption(statusOption);
            var install = context.ParseResult.GetValueForOption(installOption);
            var uninstall = context.ParseResult.GetValueForOption(uninstallOption);
            var generateService = context.ParseResult.GetValueForOption(generateServiceOption);
            var generateConfig = context.ParseResult.GetValueForOption(generateConfigOption);
            var config = context.ParseResult.GetValueForOption(configOption);
            
            await HandleDaemonCommandAsync(run, start, stop, status, install, uninstall, generateService, generateConfig, config);
        });
        
        return command;
    }
    
    private static async Task HandleDaemonCommandAsync(
        bool run, bool start, bool stop, bool status, bool install, bool uninstall, 
        bool generateService, bool generateConfig, string? configPath)
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
        
        // Default: show status
        await ShowStatusAsync();
    }
    
    private static async Task RunDaemonAsync(string? configPath)
    {
        // Check root
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Root privileges required to run daemon");
            Console.ResetColor();
            return;
        }
        
        // Self-heal: ensure DOTNET_BUNDLE_EXTRACT_BASE_DIR is set.
        // Older service file installations (pre-v3.0.0) may be missing this env var,
        // causing the .NET single-file runtime to fail to extract itself.
        // Setting it here helps child processes started by the daemon. For the service
        // itself to start cleanly, the user should re-run: sudo omencore-cli daemon --install
        var extractDir = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if (string.IsNullOrEmpty(extractDir))
        {
            extractDir = "/var/tmp/omencore";
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", extractDir);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠  DOTNET_BUNDLE_EXTRACT_BASE_DIR not set — using {extractDir}");
            Console.WriteLine("   To fix permanently: sudo omencore-cli daemon --install");
            Console.ResetColor();
        }
        try { Directory.CreateDirectory(extractDir); } catch { }
        
        // Load configuration
        var config = OmenCoreConfig.Load(configPath);
        
        // Create and run daemon
        using var daemon = new OmenCoreDaemon(config);
        await daemon.RunAsync();
    }
    
    private static void PrintSystemdService()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "/usr/local/bin/omencore-cli";
        
        Console.WriteLine($@"[Unit]
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
WantedBy=multi-user.target");
    }
    
    private static async Task InstallServiceAsync()
    {
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Root privileges required to install service");
            Console.ResetColor();
            return;
        }
        
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "/usr/local/bin/omencore-cli";
            
            var serviceContent = $@"[Unit]
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
WantedBy=multi-user.target";
            
            await File.WriteAllTextAsync(SystemdServicePath, serviceContent);
            
            // Create config directory and default config
            var configDir = "/etc/omencore";
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
                var defaultConfig = OmenCoreConfig.GenerateDefaultToml();
                await File.WriteAllTextAsync(Path.Combine(configDir, "config.toml"), defaultConfig);
                Console.WriteLine($"Created default configuration at {configDir}/config.toml");
            }
            
            // Reload systemd and enable service
            await RunCommandAsync("systemctl", "daemon-reload");
            await RunCommandAsync("systemctl", "enable omencore.service");
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║          ✓ OmenCore systemd service installed            ║");
            Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Configuration: /etc/omencore/config.toml                 ║");
            Console.WriteLine("║  Service file:  /etc/systemd/system/omencore.service      ║");
            Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Commands:                                                ║");
            Console.WriteLine("║    sudo systemctl start omencore   - Start service        ║");
            Console.WriteLine("║    sudo systemctl stop omencore    - Stop service         ║");
            Console.WriteLine("║    sudo systemctl status omencore  - Check status         ║");
            Console.WriteLine("║    journalctl -u omencore -f       - View logs            ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error installing service: {ex.Message}");
            Console.ResetColor();
        }
    }
    
    private static async Task UninstallServiceAsync()
    {
        if (Mono.Unix.Native.Syscall.getuid() != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Root privileges required to uninstall service");
            Console.ResetColor();
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
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Systemd service uninstalled");
            Console.WriteLine();
            Console.WriteLine("Note: Configuration at /etc/omencore/config.toml was preserved.");
            Console.WriteLine("      Delete manually if no longer needed.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error uninstalling service: {ex.Message}");
            Console.ResetColor();
        }
    }
    
    private static async Task StartDaemonAsync()
    {
        if (File.Exists(SystemdServicePath))
        {
            await RunCommandAsync("systemctl", "start omencore.service");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Service started via systemd");
            Console.WriteLine();
            Console.WriteLine("Check status: sudo systemctl status omencore");
            Console.WriteLine("View logs:    journalctl -u omencore -f");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Systemd service not installed.");
            Console.WriteLine();
            Console.WriteLine("Install with: sudo omencore-cli daemon --install");
            Console.WriteLine("Or run directly: sudo omencore-cli daemon --run");
            Console.ResetColor();
        }
    }
    
    private static async Task StopDaemonAsync()
    {
        if (File.Exists(SystemdServicePath))
        {
            await RunCommandAsync("systemctl", "stop omencore.service");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Service stopped");
            Console.ResetColor();
        }
        else
        {
            // Try to find and kill by PID file
            if (File.Exists(PidFile))
            {
                var pid = await File.ReadAllTextAsync(PidFile);
                await RunCommandAsync("kill", pid.Trim());
                File.Delete(PidFile);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Daemon stopped");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No running daemon found");
                Console.ResetColor();
            }
        }
    }
    
    private static async Task ShowStatusAsync()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              OmenCore Linux - Daemon Status               ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        
        var serviceInstalled = File.Exists(SystemdServicePath);
        Console.WriteLine($"║  Systemd service: {(serviceInstalled ? "✓ Installed" : "✗ Not installed"),-38} ║");
        
        if (serviceInstalled)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = "is-active omencore.service",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    var isActive = output.Trim() == "active";
                    Console.WriteLine($"║  Service status:  {(isActive ? "✓ Running" : "✗ Stopped"),-38} ║");
                }
            }
            catch
            {
                Console.WriteLine("║  Service status:  ? Unknown                               ║");
            }
        }
        
        // Check for config files
        var userConfig = File.Exists(OmenCoreConfig.DefaultConfigPath);
        var systemConfig = File.Exists(OmenCoreConfig.SystemConfigPath);
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  User config:   {(userConfig ? "✓ Found" : "✗ Not found"),-40} ║");
        Console.WriteLine($"║  System config: {(systemConfig ? "✓ Found" : "✗ Not found"),-40} ║");
        
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Commands:                                                ║");
        Console.WriteLine("║    daemon --install          Install systemd service      ║");
        Console.WriteLine("║    daemon --start            Start via systemd            ║");
        Console.WriteLine("║    daemon --stop             Stop via systemd             ║");
        Console.WriteLine("║    daemon --run              Run in foreground            ║");
        Console.WriteLine("║    daemon --generate-config  Print default config         ║");
        Console.WriteLine("║    daemon --uninstall        Remove systemd service       ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
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
}
