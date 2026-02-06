using System.CommandLine;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Fan control command.
/// 
/// Examples:
///   omencore-cli fan --status            # Show current fan status
///   omencore-cli fan --profile auto
///   omencore-cli fan --speed 80
///   omencore-cli fan --curve "40:20,50:30,60:50,80:80,90:100"
///   omencore-cli fan --battery-aware    # Auto-adjusts profile based on power source
/// </summary>
public static class FanCommand
{
    public static Command Create()
    {
        var command = new Command("fan", "Control fan speed and profiles");
        
        // Options
        var statusOption = new Option<bool>(
            aliases: new[] { "--status", "-S" },
            description: "Show current fan speeds and status");
        
        var profileOption = new Option<string?>(
            aliases: new[] { "--profile", "-p" },
            description: "Fan profile: auto, silent, balanced, gaming, max");
            
        var speedOption = new Option<int?>(
            aliases: new[] { "--speed", "-s" },
            description: "Manual fan speed percentage (0-100)");
            
        var curveOption = new Option<string?>(
            aliases: new[] { "--curve", "-c" },
            description: "Custom fan curve: temp:speed pairs (e.g., '40:20,50:30,60:50,80:80,90:100')");
            
        var fan1Option = new Option<int?>(
            name: "--fan1",
            description: "Set Fan 1 (CPU) speed in RPM");
            
        var fan2Option = new Option<int?>(
            name: "--fan2",
            description: "Set Fan 2 (GPU) speed in RPM");
            
        var boostOption = new Option<bool?>(
            aliases: new[] { "--boost", "-b" },
            description: "Enable/disable fan boost mode");
            
        var batteryAwareOption = new Option<bool>(
            aliases: new[] { "--battery-aware", "-B" },
            description: "Auto-switch to quiet profile on battery power");
        
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Show detailed fan information");
        
        command.AddOption(statusOption);
        command.AddOption(profileOption);
        command.AddOption(speedOption);
        command.AddOption(curveOption);
        command.AddOption(fan1Option);
        command.AddOption(fan2Option);
        command.AddOption(boostOption);
        command.AddOption(batteryAwareOption);
        command.AddOption(verboseOption);
        
        // Use InvocationContext to handle >8 options (System.CommandLine limitation)
        command.SetHandler(async (context) =>
        {
            var status = context.ParseResult.GetValueForOption(statusOption);
            var profile = context.ParseResult.GetValueForOption(profileOption);
            var speed = context.ParseResult.GetValueForOption(speedOption);
            var curve = context.ParseResult.GetValueForOption(curveOption);
            var fan1 = context.ParseResult.GetValueForOption(fan1Option);
            var fan2 = context.ParseResult.GetValueForOption(fan2Option);
            var boost = context.ParseResult.GetValueForOption(boostOption);
            var batteryAware = context.ParseResult.GetValueForOption(batteryAwareOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            
            await HandleFanCommandAsync(status, profile, speed, curve, fan1, fan2, boost, batteryAware, verbose);
        });
        
        return command;
    }
    
    private static async Task HandleFanCommandAsync(
        bool status, string? profile, int? speed, string? curve, 
        int? fan1, int? fan2, bool? boost, bool batteryAware, bool verbose)
    {
        // Check root
        if (!LinuxEcController.CheckRootAccess())
        {
            PrintError("Root privileges required. Run with sudo.");
            return;
        }
        
        var ec = new LinuxEcController();
        
        // Check EC access
        if (!ec.IsAvailable)
        {
            PrintError("Cannot access fan control interface.");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nTry one of these methods:");
            Console.WriteLine("  1. EC access (older OMEN models):  sudo modprobe ec_sys write_support=1");
            Console.WriteLine("  2. HP-WMI (newer OMEN 2023+):      sudo modprobe hp-wmi");
            Console.ResetColor();
            return;
        }
        
        // Show which access method is being used
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Using access method: {ec.AccessMethod}");
        Console.ResetColor();
        
        // Handle explicit --status flag first
        if (status)
        {
            ShowFanStatus(ec, verbose);
            await Task.CompletedTask;
            return;
        }
        
        // Handle battery-aware mode
        if (batteryAware)
        {
            var isOnBattery = await IsOnBatteryAsync();
            var batteryProfile = isOnBattery ? "silent" : (profile ?? "balanced");
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"ℹ Battery-aware mode: {(isOnBattery ? "On Battery → Quiet" : "Plugged In → Normal")}");
            Console.ResetColor();
            
            profile = batteryProfile;
        }
        
        // Handle profile
        if (!string.IsNullOrEmpty(profile))
        {
            var success = profile.ToLower() switch
            {
                "auto" => ec.SetFanProfile(FanProfile.Auto),
                "silent" => ec.SetFanProfile(FanProfile.Silent),
                "balanced" => ec.SetFanProfile(FanProfile.Balanced),
                "gaming" => ec.SetFanProfile(FanProfile.Gaming),
                "max" => ec.SetFanProfile(FanProfile.Max),
                _ => false
            };
            
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Fan profile set to: {profile}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Failed to set fan profile: {profile}");
                Console.ResetColor();
            }
            return;
        }
        
        // Handle speed
        if (speed.HasValue)
        {
            var pct = Math.Clamp(speed.Value, 0, 100);
            
            // For 2025+ models with hwmon, use pwm_enable instead of direct EC writes
            if (ec.IsUnsafeEcModel || ec.HasHwmonFanAccess)
            {
                bool success;
                if (pct >= 100)
                {
                    // Max speed: pwm_enable=0 (full speed)
                    success = ec.SetHwmonPwmEnable(0);
                    if (success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Fan speed set to MAX (pwm_enable=0, full speed)");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("  Note: This is a temporary boost. Fans will return to BIOS control after ~1 minute.");
                        Console.WriteLine("  Use '--profile performance' for sustained high performance.");
                        Console.ResetColor();
                    }
                }
                else if (pct <= 0)
                {
                    // Auto (BIOS control): pwm_enable=2
                    success = ec.SetHwmonPwmEnable(2);
                    if (success)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ Fan control returned to BIOS (auto mode)");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // Intermediate speeds: use ACPI profile as closest match
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"⚠ Your model ({ec.DetectedModel}) does not support precise fan speed control.");
                    Console.WriteLine($"  The OMEN Max 2025 uses BIOS-managed fan profiles instead of direct speed control.");
                    Console.ResetColor();
                    
                    // Map percentage to closest profile
                    if (pct <= 30)
                        success = ec.SetFanProfile(FanProfile.Silent);
                    else if (pct <= 60)
                        success = ec.SetFanProfile(FanProfile.Balanced);
                    else
                        success = ec.SetFanProfile(FanProfile.Gaming);
                    
                    if (success)
                    {
                        var mappedProfile = pct <= 30 ? "Silent (low-power)" : pct <= 60 ? "Balanced" : "Gaming (performance)";
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✓ Applied closest profile: {mappedProfile}");
                        Console.ResetColor();
                    }
                }
                
                if (!success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ Failed to set fan speed. Try: --profile auto|balanced|performance|max");
                    Console.ResetColor();
                }
                return;
            }
            
            if (ec.SetFanSpeedPercent(pct))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Fan speed set to: {pct}%");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Failed to set fan speed");
                Console.ResetColor();
            }
            return;
        }
        
        // Handle individual fan RPM
        if (fan1.HasValue || fan2.HasValue)
        {
            if (fan1.HasValue)
            {
                var rpm = (byte)(fan1.Value / 100);
                if (ec.SetFan1Speed(rpm))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Fan 1 speed set to: {fan1.Value} RPM");
                    Console.ResetColor();
                }
            }
            
            if (fan2.HasValue)
            {
                var rpm = (byte)(fan2.Value / 100);
                if (ec.SetFan2Speed(rpm))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Fan 2 speed set to: {fan2.Value} RPM");
                    Console.ResetColor();
                }
            }
            return;
        }
        
        // Handle boost
        if (boost.HasValue)
        {
            if (ec.SetFanBoost(boost.Value))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Fan boost {(boost.Value ? "enabled" : "disabled")}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Failed to set fan boost");
                Console.ResetColor();
            }
            return;
        }
        
        // No options - show current status
        ShowFanStatus(ec, verbose);
        await Task.CompletedTask;
    }
    
    private static async Task<bool> IsOnBatteryAsync()
    {
        const string acPath = "/sys/class/power_supply/AC0/online";
        const string acPathAlt = "/sys/class/power_supply/ACAD/online";
        
        try
        {
            string path = File.Exists(acPath) ? acPath : acPathAlt;
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path);
                return content.Trim() == "0";
            }
        }
        catch { }
        
        return false; // Assume plugged in if can't detect
    }
    
    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {message}");
        Console.ResetColor();
    }
    
    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Error: {message}");
        Console.ResetColor();
    }
    
    private static void PrintHint(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Hint: {message}");
        Console.ResetColor();
    }
    
    private static void ShowFanStatus(LinuxEcController ec, bool verbose = false)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║               Fan Status                     ║");
        Console.WriteLine("╠══════════════════════════════════════════════╣");
        
        var (fan1Speed, fan2Speed) = ec.GetFanSpeeds();
        
        // For hwmon/ACPI models, show different info
        if (ec.HasHwmonFanAccess || ec.IsUnsafeEcModel)
        {
            Console.WriteLine($"║  Fan 1 (CPU):   {fan1Speed,5} RPM                ║");
            Console.WriteLine($"║  Fan 2 (GPU):   {fan2Speed,5} RPM                ║");
            
            // Show current profile
            var acpiProfile = ec.GetAcpiProfile();
            if (acpiProfile != null)
            {
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                Console.WriteLine($"║  Profile:       {acpiProfile,-27} ║");
            }
            
            var pwmEnable = ec.GetHwmonPwmEnable();
            if (pwmEnable.HasValue)
            {
                var pwmMode = pwmEnable.Value switch
                {
                    0 => "Full Speed",
                    1 => "Manual",
                    2 => "Auto (BIOS)",
                    3 => "Fan Off (!)",
                    _ => $"Unknown ({pwmEnable.Value})"
                };
                Console.WriteLine($"║  Fan Mode:      {pwmMode,-27} ║");
            }
        }
        else
        {
            var (fan1Pct, fan2Pct) = ec.GetFanSpeedPercent();
            Console.WriteLine($"║  Fan 1 (CPU):   {fan1Speed,5} RPM  ({fan1Pct,3}%)       ║");
            Console.WriteLine($"║  Fan 2 (GPU):   {fan2Speed,5} RPM  ({fan2Pct,3}%)       ║");
        }
        
        if (verbose)
        {
            Console.WriteLine("╠══════════════════════════════════════════════╣");
            Console.WriteLine($"║  Access Method: {ec.AccessMethod,-27} ║");
            if (ec.DetectedModel != null)
                Console.WriteLine($"║  Model:         {ec.DetectedModel.PadRight(27).Substring(0, 27)} ║");
            if (ec.IsUnsafeEcModel)
                Console.WriteLine($"║  EC Access:     {"Blocked (safety)",-27} ║");
            
            // Try to get temperatures
            var cpuTemp = ec.GetCpuTemperature();
            var gpuTemp = ec.GetGpuTemperature();
            if (cpuTemp > 0 || gpuTemp > 0)
            {
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                if (cpuTemp > 0)
                    Console.WriteLine($"║  CPU Temp:      {cpuTemp,5}°C                    ║");
                if (gpuTemp > 0)
                    Console.WriteLine($"║  GPU Temp:      {gpuTemp,5}°C                    ║");
            }
            
            // Show available ACPI profiles
            if (ec.HasAcpiProfileAccess)
            {
                var choices = ec.GetAcpiProfileChoices();
                if (choices.Length > 0)
                {
                    Console.WriteLine("╠══════════════════════════════════════════════╣");
                    Console.WriteLine($"║  Available:     {string.Join(", ", choices).PadRight(27).Substring(0, 27)} ║");
                }
            }
        }
        
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
        
        if (!verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Tip: Use --verbose for more details, --profile to set mode");
            Console.ResetColor();
        }
    }
}
