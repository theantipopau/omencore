using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Diagnose command - prints Linux environment and hardware interface detection details.
/// </summary>
public static class DiagnoseCommand
{
    public static Command Create()
    {
        var command = new Command("diagnose", "Print Linux hardware access diagnostics");

        var jsonOption = new Option<bool>(
            aliases: new[] { "--json", "-j" },
            description: "Output in JSON format");

        command.AddOption(jsonOption);

        command.SetHandler(async (json) =>
        {
            await HandleDiagnoseAsync(json);
        }, jsonOption);

        return command;
    }

    private static async Task HandleDiagnoseAsync(bool jsonOutput)
    {
        var info = await CollectAsync();

        if (jsonOutput)
        {
            // Use source-generated context for trimming-friendly output.
            var json = JsonSerializer.Serialize(info, LinuxJsonContext.Default.DiagnoseInfo);
            Console.WriteLine(json);
            return;
        }

        PrintHumanReadable(info);
    }

    private static async Task<DiagnoseInfo> CollectAsync()
    {
        var info = new DiagnoseInfo
        {
            Version = Program.Version,
            Runtime = $".NET {Environment.Version}",
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            IsRoot = false
        };

        if (!info.IsLinux)
        {
            info.Notes.Add("Not running on Linux; sysfs checks skipped.");
            return info;
        }

        info.IsRoot = LinuxEcController.CheckRootAccess();

        info.OsPrettyName = await ReadOsPrettyNameAsync() ?? "Unknown";
        info.KernelRelease = await ReadTextAsync("/proc/sys/kernel/osrelease") ?? "Unknown";
        info.Model = await ReadTextAsync("/sys/devices/virtual/dmi/id/product_name")
                     ?? await ReadTextAsync("/sys/class/dmi/id/product_name")
                     ?? "Unknown";

        info.DebugFsMounted = await IsDebugFsMountedAsync();

        // Modules
        info.EcSysModuleLoaded = Directory.Exists("/sys/module/ec_sys");
        info.EcSysWriteSupport = await ReadTextAsync("/sys/module/ec_sys/parameters/write_support");

        info.HpWmiModuleLoaded = Directory.Exists("/sys/module/hp_wmi");

        // Paths
        info.EcIoPathExists = File.Exists("/sys/kernel/debug/ec/ec0/io");

        info.HpWmiPathExists = Directory.Exists("/sys/devices/platform/hp-wmi");
        info.HpWmiThermalProfileExists = File.Exists("/sys/devices/platform/hp-wmi/thermal_profile");
        info.HpWmiFanAlwaysOnExists = File.Exists("/sys/devices/platform/hp-wmi/fan_always_on");
        info.HpWmiFan1OutputExists = File.Exists("/sys/devices/platform/hp-wmi/fan1_output");
        info.HpWmiFan2OutputExists = File.Exists("/sys/devices/platform/hp-wmi/fan2_output");

        // Detection (use current controller logic)
        var ec = new LinuxEcController();
        info.DetectedAccessMethod = ec.AccessMethod;
        info.EcControllerAvailable = ec.IsAvailable;

        // Recommendations
        if (!info.IsRoot)
        {
            info.Recommendations.Add("Run with sudo for fan/performance control (EC/HP-WMI writes require root).");
        }

        if (!info.DebugFsMounted)
        {
            info.Recommendations.Add("Mount debugfs: sudo mount -t debugfs debugfs /sys/kernel/debug");
        }

        if (!info.EcIoPathExists)
        {
            if (!info.EcSysModuleLoaded)
            {
                info.Recommendations.Add("Load ec_sys (older models): sudo modprobe ec_sys write_support=1");
            }
            else if (info.EcSysWriteSupport?.Trim() != "1")
            {
                info.Recommendations.Add("Reload ec_sys with write support: sudo modprobe -r ec_sys; sudo modprobe ec_sys write_support=1");
            }
        }

        if (info.HpWmiPathExists && !info.HpWmiThermalProfileExists)
        {
            info.Notes.Add("hp-wmi directory exists but thermal_profile not found; your kernel/firmware may not expose OMEN controls.");
            info.Recommendations.Add("Try a newer kernel (6.5+ recommended for 2023+ OMEN) and ensure hp-wmi is loaded: sudo modprobe hp-wmi");
        }

        if (info.DetectedAccessMethod == "none")
        {
            info.Recommendations.Add("If you have a 2023+ OMEN (wf0000 / 13th gen HX), try hp-wmi: sudo modprobe hp-wmi");
            info.Recommendations.Add("If you have an older OMEN, try ec_sys: sudo modprobe ec_sys write_support=1");
        }

        if (info.DetectedAccessMethod == "hp-wmi" && !info.HpWmiFan1OutputExists && !info.HpWmiFan2OutputExists)
        {
            info.Notes.Add("hp-wmi detected, but fan output controls are not present; fan control may be limited to thermal_profile only.");
        }

        return info;
    }

    private static async Task<bool> IsDebugFsMountedAsync()
    {
        try
        {
            var mounts = await File.ReadAllTextAsync("/proc/mounts");
            return mounts.Split('\n').Any(line => line.Contains(" /sys/kernel/debug ") && line.Contains(" debugfs "));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> ReadOsPrettyNameAsync()
    {
        try
        {
            if (!File.Exists("/etc/os-release"))
                return null;

            var lines = await File.ReadAllLinesAsync("/etc/os-release");
            foreach (var line in lines)
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.OrdinalIgnoreCase))
                    return line.Replace("PRETTY_NAME=", "").Trim().Trim('"');
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private static async Task<string?> ReadTextAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return (await File.ReadAllTextAsync(path)).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static void PrintHumanReadable(DiagnoseInfo info)
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                 OmenCore Linux - Diagnose                ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Version:   {info.Version,-50}║");
        Console.WriteLine($"║  Runtime:   {info.Runtime,-50}║");
        Console.WriteLine($"║  OS:        {info.OsPrettyName,-50}║");
        Console.WriteLine($"║  Kernel:    {info.KernelRelease,-50}║");
        Console.WriteLine($"║  Model:     {info.Model,-50}║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Root:      {(info.IsRoot ? "✓" : "✗"),-50}║");
        Console.WriteLine($"║  debugfs:   {(info.DebugFsMounted ? "✓ mounted" : "✗ not mounted"),-50}║");
        Console.WriteLine($"║  ec_io:     {(info.EcIoPathExists ? "✓ present" : "✗ missing"),-50}║");
        Console.WriteLine($"║  ec_sys:    {(info.EcSysModuleLoaded ? "✓ loaded" : "✗ not loaded"),-50}║");
        Console.WriteLine($"║  ec_sys ws: {(string.IsNullOrWhiteSpace(info.EcSysWriteSupport) ? "(n/a)" : info.EcSysWriteSupport),-50}║");
        Console.WriteLine($"║  hp_wmi:    {(info.HpWmiModuleLoaded ? "✓ loaded" : "✗ not loaded"),-50}║");
        Console.WriteLine($"║  hp-wmi dir:{(info.HpWmiPathExists ? "✓ present" : "✗ missing"),-50}║");
        Console.WriteLine($"║  thermal:   {(info.HpWmiThermalProfileExists ? "✓ present" : "✗ missing"),-50}║");
        Console.WriteLine($"║  fan1_out:  {(info.HpWmiFan1OutputExists ? "✓ present" : "✗ missing"),-50}║");
        Console.WriteLine($"║  fan2_out:  {(info.HpWmiFan2OutputExists ? "✓ present" : "✗ missing"),-50}║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Detected:  {info.DetectedAccessMethod,-50}║");
        Console.WriteLine($"║  Available: {(info.EcControllerAvailable ? "✓" : "✗"),-50}║");

        if (info.Notes.Count > 0)
        {
            Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Notes:                                                   ║");
            foreach (var note in info.Notes.Take(6))
            {
                Console.WriteLine($"║   - {Truncate(note, 57),-57}║");
            }
        }

        if (info.Recommendations.Count > 0)
        {
            Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Next Steps:                                              ║");
            foreach (var rec in info.Recommendations.Take(6))
            {
                Console.WriteLine($"║   - {Truncate(rec, 57),-57}║");
            }
        }

        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value[..(max - 1)] + "…";
    }
}

public class DiagnoseInfo
{
    public string Version { get; set; } = "";
    public string Runtime { get; set; } = "";
    public bool IsLinux { get; set; }
    public bool IsRoot { get; set; }

    public string OsPrettyName { get; set; } = "";
    public string KernelRelease { get; set; } = "";
    public string Model { get; set; } = "";

    public bool DebugFsMounted { get; set; }

    public bool EcSysModuleLoaded { get; set; }
    public string? EcSysWriteSupport { get; set; }
    public bool EcIoPathExists { get; set; }

    public bool HpWmiModuleLoaded { get; set; }
    public bool HpWmiPathExists { get; set; }
    public bool HpWmiThermalProfileExists { get; set; }
    public bool HpWmiFanAlwaysOnExists { get; set; }
    public bool HpWmiFan1OutputExists { get; set; }
    public bool HpWmiFan2OutputExists { get; set; }

    public string DetectedAccessMethod { get; set; } = "none";
    public bool EcControllerAvailable { get; set; }

    public List<string> Notes { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}
