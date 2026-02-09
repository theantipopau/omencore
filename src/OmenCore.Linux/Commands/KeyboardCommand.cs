using System.CommandLine;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Keyboard lighting control command.
/// 
/// Examples:
///   omencore-cli keyboard --color FF0000
///   omencore-cli keyboard --zone 0 --color 00FF00
///   omencore-cli keyboard --off
/// </summary>
public static class KeyboardCommand
{
    public static Command Create()
    {
        var command = new Command("keyboard", "Control keyboard RGB lighting");
        
        var colorOption = new Option<string?>(
            aliases: new[] { "--color", "-c" },
            description: "Set color in hex format (e.g., FF0000 for red)");
            
        var zoneOption = new Option<int?>(
            aliases: new[] { "--zone", "-z" },
            description: "Zone number (0-3). If not specified, sets all zones.");
            
        var brightnessOption = new Option<int?>(
            aliases: new[] { "--brightness", "-b" },
            description: "Brightness level (0-100)");
            
        var offOption = new Option<bool>(
            name: "--off",
            description: "Turn off keyboard lighting");
        
        command.AddOption(colorOption);
        command.AddOption(zoneOption);
        command.AddOption(brightnessOption);
        command.AddOption(offOption);
        
        command.SetHandler(async (color, zone, brightness, off) =>
        {
            await HandleKeyboardCommandAsync(color, zone, brightness, off);
        }, colorOption, zoneOption, brightnessOption, offOption);
        
        return command;
    }
    
    private static async Task HandleKeyboardCommandAsync(string? color, int? zone, int? brightness, bool off)
    {
        var keyboard = new LinuxKeyboardController();
        
        if (!keyboard.IsAvailable)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: HP WMI keyboard interface not available.");
            Console.WriteLine("  Ensure hp-wmi module is loaded: modprobe hp-wmi");
            Console.ResetColor();
            return;
        }
        
        // Handle off
        if (off)
        {
            if (keyboard.TurnOff())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Keyboard lighting turned off");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Failed to turn off keyboard lighting");
                Console.ResetColor();
            }
            return;
        }
        
        // Handle color
        if (!string.IsNullOrEmpty(color))
        {
            // Parse hex color
            var hexColor = color.TrimStart('#');
            if (hexColor.Length != 6)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Invalid color format. Use hex format: FF0000");
                Console.ResetColor();
                return;
            }
            
            if (!byte.TryParse(hexColor.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
                !byte.TryParse(hexColor.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
                !byte.TryParse(hexColor.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Invalid color format. Use hex format: FF0000");
                Console.ResetColor();
                return;
            }
            
            bool success;
            if (zone.HasValue)
            {
                var zoneIndex = Math.Clamp(zone.Value, 0, 3);
                success = keyboard.SetZoneColor(zoneIndex, r, g, b);
                if (success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Zone {zoneIndex} color set to: #{hexColor}");
                    Console.ResetColor();
                }
            }
            else
            {
                success = keyboard.SetAllZonesColor(r, g, b);
                if (success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ All zones color set to: #{hexColor}");
                    Console.ResetColor();
                }
            }
            
            if (!success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Failed to set keyboard color");
                Console.ResetColor();
            }
            return;
        }
        
        // Handle brightness
        if (brightness.HasValue)
        {
            var level = Math.Clamp(brightness.Value, 0, 100);
            if (keyboard.SetBrightness(level))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Keyboard brightness set to: {level}%");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Failed to set keyboard brightness");
                Console.ResetColor();
            }
            return;
        }
        
        // No options - show current status
        ShowKeyboardStatus(keyboard);
        await Task.CompletedTask;
    }
    
    private static void ShowKeyboardStatus(LinuxKeyboardController keyboard)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║          Keyboard RGB Status                ║");
        Console.WriteLine("╠══════════════════════════════════════════════╣");
        Console.WriteLine($"║  Available: {(keyboard.IsAvailable ? "Yes" : "No"),-32} ║");
        Console.WriteLine($"║  Backend: HP WMI                             ║");
        Console.WriteLine($"║  Type: {keyboard.KeyboardType,-37} ║");
        if (keyboard.IsPerKeyRgb)
        {
            Console.WriteLine($"║  Per-Key: USB HID (not yet on Linux)         ║");
            Console.WriteLine($"║  4-Zone Fallback: {(keyboard.HasZoneControl ? "Available" : "Unavailable"),-26} ║");
        }
        else
        {
            Console.WriteLine($"║  Zones: 4 (WASD, Left, Right, Far)           ║");
        }
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
