using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using OmenCore.Linux.Commands;

namespace OmenCore.Linux;

/// <summary>
/// OmenCore Linux CLI - Command-line utility for controlling HP OMEN laptops.
/// 
/// Usage:
///   omencore-cli fan --profile auto|silent|gaming|max
///   omencore-cli fan --speed 50%
///   omencore-cli fan --curve "40:20,50:30,60:50,80:80,90:100"
///   omencore-cli perf --mode balanced|performance
///   omencore-cli keyboard --color FF0000
///   omencore-cli keyboard --zone 0 --color 00FF00
///   omencore-cli status [--json]
///   omencore-cli monitor [--interval 1000]
///   omencore-cli config --show|--set key=value
///   omencore-cli daemon --start|--stop|--status
/// 
/// Requirements:
///   - Linux kernel with ec_sys module (write_support=1)
///   - HP WMI module for keyboard lighting
///   - Root privileges for EC access
/// </summary>
class Program
{
    public const string Version = "2.0.1-beta";
    public const string BuildDate = "2025-01";
    
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
        ".config", "omencore", "config.json");

    static async Task<int> Main(string[] args)
    {
        // Handle --version before anything else
        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-V"))
        {
            PrintVersion();
            return 0;
        }

        var rootCommand = new RootCommand("OmenCore Linux CLI - HP OMEN laptop control utility");
        
        // NOTE: Do not add a --version option here; System.CommandLine may add one internally,
        // and we already handle --version/-V early to print the banner.
        
        // Add commands
        rootCommand.AddCommand(FanCommand.Create());
        rootCommand.AddCommand(PerformanceCommand.Create());
        rootCommand.AddCommand(KeyboardCommand.Create());
        rootCommand.AddCommand(StatusCommand.Create());
        rootCommand.AddCommand(MonitorCommand.Create());
        rootCommand.AddCommand(ConfigCommand.Create());
        rootCommand.AddCommand(DaemonCommand.Create());
        rootCommand.AddCommand(CreateBatteryCommand());
        
        // Add global options
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");
        rootCommand.AddGlobalOption(verboseOption);
        
        var jsonOption = new Option<bool>(
            aliases: new[] { "--json", "-j" },
            description: "Output in JSON format for scripting");
        rootCommand.AddGlobalOption(jsonOption);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    private static void PrintVersion()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   OmenCore Linux CLI v{Version,-30}  ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║   Build:    {BuildDate,-40}  ║");
        Console.WriteLine($"║   Runtime:  .NET {Environment.Version,-36}  ║");
        Console.WriteLine($"║   OS:       {GetOsInfo(),-40}  ║");
        Console.WriteLine("║                                                       ║");
        Console.WriteLine("║   GitHub:   github.com/omencore/omencore              ║");
        Console.WriteLine("║   License:  MIT                                       ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
    
    private static string GetOsInfo()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                foreach (var line in lines)
                {
                    if (line.StartsWith("PRETTY_NAME="))
                    {
                        return line.Replace("PRETTY_NAME=", "").Trim('"');
                    }
                }
            }
        }
        catch { }
        
        return Environment.OSVersion.ToString();
    }
    
    /// <summary>
    /// Battery command - manages battery-aware fan profiles and power settings.
    /// </summary>
    private static Command CreateBatteryCommand()
    {
        var command = new Command("battery", "Battery status and power management");
        
        var statusSubCommand = new Command("status", "Show battery status");
        statusSubCommand.SetHandler(async () =>
        {
            await ShowBatteryStatusAsync();
        });
        
        var profileSubCommand = new Command("profile", "Set battery profile (affects fans when on battery)");
        var profileNameArg = new Argument<string>("profile", "Profile name: quiet, balanced, performance");
        profileSubCommand.AddArgument(profileNameArg);
        profileSubCommand.SetHandler(async (profileName) =>
        {
            await SetBatteryProfileAsync(profileName);
        }, profileNameArg);
        
        var chargeThresholdCommand = new Command("threshold", "Set battery charge threshold (0 = disabled)");
        var thresholdArg = new Argument<int>("percent", "Stop charging at this percentage (60-100, 0 = disabled)");
        chargeThresholdCommand.AddArgument(thresholdArg);
        chargeThresholdCommand.SetHandler(async (threshold) =>
        {
            await SetChargeThresholdAsync(threshold);
        }, thresholdArg);
        
        command.AddCommand(statusSubCommand);
        command.AddCommand(profileSubCommand);
        command.AddCommand(chargeThresholdCommand);
        
        return command;
    }
    
    private static async Task ShowBatteryStatusAsync()
    {
        const string batteryPath = "/sys/class/power_supply/BAT0";
        const string acPath = "/sys/class/power_supply/AC0";
        
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║               Battery Status                         ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        
        int capacity = 0;
        string status = "Unknown";
        int energyNow = 0;
        int energyFull = 0;
        int powerNow = 0;
        bool onAc = false;
        
        try
        {
            // Read capacity
            var capacityFile = Path.Combine(batteryPath, "capacity");
            if (File.Exists(capacityFile))
                capacity = int.Parse(await File.ReadAllTextAsync(capacityFile));
            
            // Read status
            var statusFile = Path.Combine(batteryPath, "status");
            if (File.Exists(statusFile))
                status = (await File.ReadAllTextAsync(statusFile)).Trim();
            
            // Read energy values
            var energyNowFile = Path.Combine(batteryPath, "energy_now");
            if (File.Exists(energyNowFile))
                energyNow = int.Parse(await File.ReadAllTextAsync(energyNowFile));
            
            var energyFullFile = Path.Combine(batteryPath, "energy_full");
            if (File.Exists(energyFullFile))
                energyFull = int.Parse(await File.ReadAllTextAsync(energyFullFile));
            
            // Read power draw
            var powerNowFile = Path.Combine(batteryPath, "power_now");
            if (File.Exists(powerNowFile))
                powerNow = int.Parse(await File.ReadAllTextAsync(powerNowFile));
            
            // Check AC adapter
            var acOnlineFile = Path.Combine(acPath, "online");
            if (File.Exists(acOnlineFile))
                onAc = (await File.ReadAllTextAsync(acOnlineFile)).Trim() == "1";
        }
        catch { }
        
        var bar = GetProgressBar(capacity, 100, 30);
        var color = capacity > 60 ? ConsoleColor.Green : capacity > 20 ? ConsoleColor.Yellow : ConsoleColor.Red;
        
        Console.Write($"║  Level:     ");
        Console.ForegroundColor = color;
        Console.Write($"{capacity,3}%");
        Console.ResetColor();
        Console.WriteLine($" [{bar}]    ║");
        
        Console.WriteLine($"║  Status:    {status,-40}  ║");
        Console.WriteLine($"║  Power:     {(onAc ? "AC Adapter" : "Battery"),-40}  ║");
        
        if (powerNow > 0)
        {
            var watts = powerNow / 1000000.0;
            Console.WriteLine($"║  Draw:      {watts:F1} W                                       ║".PadRight(54, ' ') + "  ║");
        }
        
        if (status == "Discharging" && powerNow > 0 && energyNow > 0)
        {
            var hoursRemaining = energyNow / (double)powerNow;
            Console.WriteLine($"║  Remaining: ~{hoursRemaining:F1} hours                                  ║".PadRight(54, ' ') + "  ║");
        }
        
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        await Task.CompletedTask;
    }
    
    private static async Task SetBatteryProfileAsync(string profileName)
    {
        var profile = profileName.ToLower() switch
        {
            "quiet" or "silent" => "quiet",
            "balanced" => "balanced",
            "performance" => "performance",
            _ => null
        };
        
        if (profile == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ Invalid profile. Use: quiet, balanced, or performance");
            Console.ResetColor();
            return;
        }
        
        // Save to config
        ConfigManager.Set("battery.profile", profile);
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Battery profile set to: {profile}");
        Console.ResetColor();
        Console.WriteLine("  The daemon will apply this profile when running on battery.");
        
        await Task.CompletedTask;
    }
    
    private static async Task SetChargeThresholdAsync(int threshold)
    {
        if (threshold != 0 && (threshold < 60 || threshold > 100))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ Threshold must be between 60-100, or 0 to disable");
            Console.ResetColor();
            return;
        }
        
        // Try to set via sysfs (HP laptops with omen-wmi)
        const string thresholdPath = "/sys/devices/platform/hp-wmi/charge_threshold";
        
        if (File.Exists(thresholdPath))
        {
            try
            {
                await File.WriteAllTextAsync(thresholdPath, threshold.ToString());
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Charge threshold set to: {(threshold == 0 ? "disabled" : $"{threshold}%")}");
                Console.ResetColor();
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Root privileges required. Run with sudo.");
                Console.ResetColor();
                return;
            }
        }
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠ Charge threshold not supported on this device");
        Console.ResetColor();
        
        await Task.CompletedTask;
    }
    
    private static string GetProgressBar(int value, int max, int width)
    {
        var filled = (int)((double)value / max * width);
        var empty = width - filled;
        return new string('█', filled) + new string('░', empty);
    }
}

/// <summary>
/// Configuration file management.
/// </summary>
public static class ConfigManager
{
    private static readonly string ConfigDir = Path.GetDirectoryName(Program.ConfigPath)!;
    
    public static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(Program.ConfigPath))
            {
                var json = File.ReadAllText(Program.ConfigPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch { }
        return new();
    }
    
    public static void Save(Dictionary<string, string> config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Program.ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
    
    public static string? Get(string key) => Load().TryGetValue(key, out var val) ? val : null;
    
    public static void Set(string key, string value)
    {
        var config = Load();
        config[key] = value;
        Save(config);
    }
}
