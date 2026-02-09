using System.CommandLine;
using System.IO;
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

        var reportOption = new Option<bool>(
            aliases: new[] { "--report", "-r" },
            description: "Generate GitHub issue report template");

        var exportOption = new Option<string?>(
            aliases: new[] { "--export" },
            description: "Write diagnostics to file (json)",
            getDefaultValue: () => null);

        command.AddOption(jsonOption);
        command.AddOption(reportOption);
        command.AddOption(exportOption);

        command.SetHandler(async (json, report, exportPath) =>
        {
            await HandleDiagnoseAsync(json, report, exportPath);
        }, jsonOption, reportOption, exportOption);

        return command;
    }

    private static async Task HandleDiagnoseAsync(bool jsonOutput, bool generateReport, string? exportPath)
    {
        var info = await CollectAsync();

        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            await ExportAsync(info, exportPath);
            Console.WriteLine($"Diagnostics written to {exportPath}");
            return;
        }

        if (generateReport)
        {
            PrintGitHubIssueReport(info);
            return;
        }

        if (jsonOutput)
        {
            // Use source-generated context for trimming-friendly output.
            var json = JsonSerializer.Serialize(info, LinuxJsonContext.Default.DiagnoseInfo);
            Console.WriteLine(json);
            return;
        }

        PrintHumanReadable(info);
    }

    private static async Task ExportAsync(DiagnoseInfo info, string exportPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(info, LinuxJsonContext.Default.DiagnoseInfo);
            var directory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(exportPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write diagnostics: {ex.Message}");
        }
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

        // ACPI platform_profile (kernel 5.18+, used by 2025+ models)
        info.AcpiPlatformProfileExists = File.Exists("/sys/firmware/acpi/platform_profile");
        if (info.AcpiPlatformProfileExists)
        {
            info.AcpiPlatformProfile = await ReadTextAsync("/sys/firmware/acpi/platform_profile");
            info.AcpiPlatformProfileChoices = await ReadTextAsync("/sys/firmware/acpi/platform_profile_choices");
        }

        // Detection (use current controller logic)
        var ec = new LinuxEcController();
        info.DetectedAccessMethod = ec.AccessMethod;
        info.EcControllerAvailable = ec.IsAvailable;
        info.IsUnsafeEcModel = ec.IsUnsafeEcModel;
        info.HasHwmonFanAccess = ec.HasHwmonFanAccess;
        
        // Add detailed diagnostics from controller
        var ecDiagnostics = ec.GetDiagnostics();
        info.EcDiagnostics = ecDiagnostics;

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
            if (info.AcpiPlatformProfileExists)
            {
                info.Notes.Add("hp-wmi directory exists but thermal_profile not found. Using ACPI platform_profile instead.");
            }
            else
            {
                info.Notes.Add("hp-wmi directory exists but thermal_profile not found; your kernel/firmware may not expose OMEN controls.");
                info.Recommendations.Add("Try a newer kernel (6.5+ recommended for 2023+ OMEN) and ensure hp-wmi is loaded: sudo modprobe hp-wmi");
            }
        }
        
        if (ec.IsUnsafeEcModel)
        {
            info.Notes.Add($"⚠ Model '{ec.DetectedModel}' has an unmapped EC register layout. Direct EC access is blocked for safety.");
            info.Notes.Add("This model uses ACPI platform_profile and hp-wmi hwmon for fan control instead of legacy EC registers.");
            if (ec.HasHwmonFanAccess)
                info.Notes.Add("✓ hwmon fan control interface detected and available.");
            if (info.AcpiPlatformProfileExists)
                info.Notes.Add($"✓ ACPI platform profile available: {info.AcpiPlatformProfile} (choices: {info.AcpiPlatformProfileChoices})");
        }

        if (info.DetectedAccessMethod == "none")
        {
            info.Recommendations.Add("If you have a 2023+ OMEN (wf0000 / 13th gen HX), try hp-wmi: sudo modprobe hp-wmi");
            info.Recommendations.Add("If you have an older OMEN, try ec_sys: sudo modprobe ec_sys write_support=1");
            info.Recommendations.Add("Check kernel version: uname -a (6.5+ recommended for newer models)");
            info.Recommendations.Add("For Fedora 43+/RHEL 10+: ec_sys removed from kernel, use hp-wmi only");
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
        // Box width: 90 total (╔ + 88 inner + ╗)
        const int innerWidth = 88;
        const string topBorder    = "╔════════════════════════════════════════════════════════════════════════════════════════════╗";
        const string midBorder    = "╠════════════════════════════════════════════════════════════════════════════════════════════╣";
        const string bottomBorder = "╚════════════════════════════════════════════════════════════════════════════════════════════╝";
        
        Console.WriteLine();
        Console.WriteLine(topBorder);
        Console.WriteLine($"║{"OmenCore Linux - Diagnose",56}{"",-32}║");
        Console.WriteLine(midBorder);
        Console.WriteLine($"║  Version:   {info.Version,-76}║");
        Console.WriteLine($"║  Runtime:   {info.Runtime,-76}║");
        Console.WriteLine($"║  OS:        {info.OsPrettyName,-76}║");
        Console.WriteLine($"║  Kernel:    {info.KernelRelease,-76}║");
        Console.WriteLine($"║  Model:     {info.Model,-76}║");
        Console.WriteLine(midBorder);
        Console.WriteLine($"║  Root:      {(info.IsRoot ? "✓" : "✗"),-76}║");
        Console.WriteLine($"║  debugfs:   {(info.DebugFsMounted ? "✓ mounted" : "✗ not mounted"),-76}║");
        Console.WriteLine($"║  ec_io:     {(info.EcIoPathExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  ec_sys:    {(info.EcSysModuleLoaded ? "✓ loaded" : "✗ not loaded"),-76}║");
        Console.WriteLine($"║  ec_sys ws: {(string.IsNullOrWhiteSpace(info.EcSysWriteSupport) ? "(n/a)" : info.EcSysWriteSupport),-76}║");
        Console.WriteLine($"║  hp_wmi:    {(info.HpWmiModuleLoaded ? "✓ loaded" : "✗ not loaded"),-76}║");
        Console.WriteLine($"║  hp-wmi dir:{(info.HpWmiPathExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  thermal:   {(info.HpWmiThermalProfileExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan1_out:  {(info.HpWmiFan1OutputExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan2_out:  {(info.HpWmiFan2OutputExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  acpi_prof: {(info.AcpiPlatformProfileExists ? $"✓ ({info.AcpiPlatformProfile ?? "?"})" : "✗ missing"),-76}║");
        Console.WriteLine($"║  hwmon_fan: {(info.HasHwmonFanAccess ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine(midBorder);
        Console.WriteLine($"║  Detected:  {info.DetectedAccessMethod,-76}║");
        Console.WriteLine($"║  Available: {(info.EcControllerAvailable ? "✓" : "✗"),-76}║");
        if (info.IsUnsafeEcModel)
            Console.WriteLine($"║  EC Safety: {"⚠ Blocked (new model)",-76}║");

        if (info.Notes.Count > 0)
        {
            Console.WriteLine(midBorder);
            Console.WriteLine($"║  {"Notes:",-86}║");
            foreach (var note in info.Notes.Take(6))
            {
                foreach (var line in WrapText(note, innerWidth - 7)) // 7 = "║   - " + "║"
                {
                    Console.WriteLine($"║   - {line,-(innerWidth - 5)}║");
                }
            }
        }

        if (info.Recommendations.Count > 0)
        {
            Console.WriteLine(midBorder);
            Console.WriteLine($"║  {"Next Steps:",-86}║");
            foreach (var rec in info.Recommendations.Take(6))
            {
                foreach (var line in WrapText(rec, innerWidth - 7))
                {
                    Console.WriteLine($"║   - {line,-(innerWidth - 5)}║");
                }
            }
        }

        Console.WriteLine(bottomBorder);
        Console.WriteLine();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value[..(max - 1)] + "…";
    }
    
    /// <summary>
    /// Wrap text at word boundaries to fit inside the box.
    /// First line includes "- " prefix, continuations use "  " indent.
    /// </summary>
    private static IEnumerable<string> WrapText(string text, int maxWidth)
    {
        if (text.Length <= maxWidth)
        {
            yield return text;
            yield break;
        }
        
        int pos = 0;
        bool first = true;
        while (pos < text.Length)
        {
            int available = first ? maxWidth : maxWidth - 2; // continuation lines indented
            int len = Math.Min(available, text.Length - pos);
            if (pos + len < text.Length)
            {
                int lastSpace = text.LastIndexOf(' ', pos + len - 1, Math.Min(len, len));
                if (lastSpace > pos)
                    len = lastSpace - pos;
            }
            var segment = text.Substring(pos, len).TrimEnd();
            if (!first)
                segment = "  " + segment; // indent continuation lines
            yield return segment;
            pos += len;
            while (pos < text.Length && text[pos] == ' ') pos++;
            first = false;
        }
    }

    private static void PrintGitHubIssueReport(DiagnoseInfo info)
    {
        Console.WriteLine();
        Console.WriteLine("<!-- Copy everything below this line and paste into your GitHub issue -->");
        Console.WriteLine();
        Console.WriteLine("## System Information");
        Console.WriteLine();
        Console.WriteLine($"- **OmenCore Version:** {info.Version}");
        Console.WriteLine($"- **Runtime:** {info.Runtime}");
        Console.WriteLine($"- **OS:** {info.OsPrettyName}");
        Console.WriteLine($"- **Kernel:** {info.KernelRelease}");
        Console.WriteLine($"- **Model:** {info.Model}");
        Console.WriteLine();
        Console.WriteLine("## Hardware Access Diagnostics");
        Console.WriteLine();
        Console.WriteLine("| Component | Status |");
        Console.WriteLine("|-----------|--------|");
        Console.WriteLine($"| Root Access | {(info.IsRoot ? "✓ Yes" : "✗ No")} |");
        Console.WriteLine($"| debugfs Mounted | {(info.DebugFsMounted ? "✓ Yes" : "✗ No")} |");
        Console.WriteLine($"| `ec_sys` Module | {(info.EcSysModuleLoaded ? "✓ Loaded" : "✗ Not Loaded")} |");
        Console.WriteLine($"| `ec_sys` Write Support | {(string.IsNullOrWhiteSpace(info.EcSysWriteSupport) ? "N/A" : info.EcSysWriteSupport)} |");
        Console.WriteLine($"| EC I/O Path (`/sys/kernel/debug/ec/ec0/io`) | {(info.EcIoPathExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| `hp-wmi` Module | {(info.HpWmiModuleLoaded ? "✓ Loaded" : "✗ Not Loaded")} |");
        Console.WriteLine($"| HP-WMI Directory | {(info.HpWmiPathExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| Thermal Profile Control | {(info.HpWmiThermalProfileExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| Fan 1 Output Control | {(info.HpWmiFan1OutputExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| Fan 2 Output Control | {(info.HpWmiFan2OutputExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine();
        Console.WriteLine($"**Detected Access Method:** `{info.DetectedAccessMethod}`");
        Console.WriteLine();
        Console.WriteLine($"**Controller Available:** {(info.EcControllerAvailable ? "✓ Yes" : "✗ No")}");
        Console.WriteLine();

        if (info.Notes.Count > 0)
        {
            Console.WriteLine("## Notes");
            Console.WriteLine();
            foreach (var note in info.Notes)
            {
                Console.WriteLine($"- {note}");
            }
            Console.WriteLine();
        }

        if (info.Recommendations.Count > 0)
        {
            Console.WriteLine("## Recommended Steps");
            Console.WriteLine();
            foreach (var rec in info.Recommendations)
            {
                Console.WriteLine($"1. {rec}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("## Issue Description");
        Console.WriteLine();
        Console.WriteLine("<!-- Describe what you're experiencing here -->");
        Console.WriteLine();
        Console.WriteLine("### Expected Behavior");
        Console.WriteLine();
        Console.WriteLine("<!-- What should happen? -->");
        Console.WriteLine();
        Console.WriteLine("### Actual Behavior");
        Console.WriteLine();
        Console.WriteLine("<!-- What actually happens? -->");
        Console.WriteLine();
        Console.WriteLine("### Steps to Reproduce");
        Console.WriteLine();
        Console.WriteLine("1. ");
        Console.WriteLine("2. ");
        Console.WriteLine("3. ");
        Console.WriteLine();
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
    public bool IsUnsafeEcModel { get; set; }
    public bool HasHwmonFanAccess { get; set; }
    public Dictionary<string, object>? EcDiagnostics { get; set; }
    
    // ACPI platform_profile (2025+ models)
    public bool AcpiPlatformProfileExists { get; set; }
    public string? AcpiPlatformProfile { get; set; }
    public string? AcpiPlatformProfileChoices { get; set; }

    public List<string> Notes { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}
