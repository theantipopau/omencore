using System.CommandLine;
using System.Text.Json;
using OmenCore.Linux.Config;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Commands;

/// <summary>
/// Status command - shows current system state.
/// 
/// Examples:
///   omencore-cli status
///   omencore-cli status --json
/// </summary>
public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show current system status");
        
        var jsonOption = new Option<bool>(
            aliases: new[] { "--json", "-j" },
            description: "Output in JSON format");
            
        command.AddOption(jsonOption);
        
        command.SetHandler(async (json) =>
        {
            await HandleStatusCommandAsync(json);
        }, jsonOption);
        
        return command;
    }
    
    private static async Task HandleStatusCommandAsync(bool jsonOutput)
    {
        var ec = new LinuxEcController();
        var hwmon = new LinuxHwMonController();
        var keyboard = new LinuxKeyboardController();
        var config = OmenCoreConfig.Load();
        var isRoot = LinuxEcController.CheckRootAccess();
        var ecIoPathExists = File.Exists(LinuxSysfsPathMap.EcIoPath);
        var hpWmiPathExists = Directory.Exists(LinuxSysfsPathMap.HpWmiRoot);
        var hasThermalProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiThermalProfilePaths);
        var hasPlatformProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.PlatformProfilePaths);
        var hasAcpiPlatformProfilePath = File.Exists(LinuxSysfsPathMap.AcpiPlatformProfilePath);

        var cpuReading = LinuxTelemetryResolver.GetCpuTemperature(ec, hwmon);
        var gpuReading = LinuxTelemetryResolver.GetGpuTemperature(ec, hwmon);
        var cpuTemp = cpuReading?.Temperature;
        var gpuTemp = gpuReading?.Temperature;
        // Separate from GetGpuTemperature's own NVML call above - name/power/utilization aren't
        // part of LinuxTemperatureReading's shape (temperature-only, shared with hwmon/EC sources).
        // A second NVML query per invocation is cheap and irrelevant for a one-shot CLI command.
        var nvmlGpu = NvmlInterop.TryGetPrimaryGpu();
        
        var (fan1Rpm, fan2Rpm) = ec.IsAvailable ? ec.GetFanSpeeds() : (0, 0);
        var (fan1Pct, fan2Pct) = ec.IsAvailable ? ec.GetFanSpeedPercent() : (0, 0);
        var capabilityAssessment = LinuxCapabilityClassifier.Assess(
            isRoot,
            ec.HasEcAccess,
            hpWmiPathExists,
            hasThermalProfilePath,
            hasPlatformProfilePath,
            hasAcpiPlatformProfilePath,
            File.Exists("/sys/devices/platform/hp-wmi/fan1_output"),
            File.Exists("/sys/devices/platform/hp-wmi/fan2_output"),
            LinuxSysfsPathMap.HasHpWmiFanTarget(1),
            LinuxSysfsPathMap.HasHpWmiFanTarget(2),
            ec.HasHwmonFanAccess,
            ecIoPathExists || hpWmiPathExists,
            ec.IsUnsafeEcModel,
            ec.DetectedModel,
            ec.DetectedBoardId);
        
        var perfMode = ec.IsAvailable ? ec.GetPerformanceMode() : PerformanceMode.Default;
        var perfModeStr = perfMode switch
        {
            PerformanceMode.Default => "Default",
            PerformanceMode.Balanced => "Balanced",
            PerformanceMode.Performance => "Performance",
            PerformanceMode.Cool => "Cool",
            _ => "Unknown"
        };
        
        if (jsonOutput)
        {
            var status = new SystemStatus
            {
                Version = Program.Version,
                EcAvailable = ec.IsAvailable,
                KeyboardAvailable = keyboard.IsAvailable,
                Temperatures = new TemperatureInfo
                {
                    Cpu = cpuTemp ?? 0,
                    Gpu = gpuTemp ?? 0
                },
                Fans = new FanInfo
                {
                    Fan1Rpm = fan1Rpm,
                    Fan1Percent = fan1Pct,
                    Fan2Rpm = fan2Rpm,
                    Fan2Percent = fan2Pct
                },
                Performance = new PerformanceInfo
                {
                    Mode = perfModeStr.ToLowerInvariant(),
                    HoldEnabled = config.Performance.HoldEnabled,
                    HoldIntervalSeconds = config.Performance.HoldIntervalSeconds,
                    ThermalPowerLimit = config.Performance.ThermalPowerLimit
                },
                CapabilityClass = capabilityAssessment.CapabilityKey,
                CapabilityReason = capabilityAssessment.Reason,
                Access = new LinuxAccessInfo
                {
                    IsRoot = isRoot,
                    AccessMethod = ec.AccessMethod,
                    EcIoPathExists = ecIoPathExists,
                    HpWmiPathExists = hpWmiPathExists,
                    HasHwmonFanAccess = ec.HasHwmonFanAccess,
                    HasHpWmiDkmsCompatibleFanBackend = ec.HasHpWmiDkmsCompatibleFanBackend,
                    HpWmiModuleLooksDkms = ec.HpWmiModuleLooksDkms,
                    HpWmiModuleSource = ec.HpWmiModuleSource,
                    HpWmiCompatibilityLabel = ec.HpWmiCompatibilityLabel,
                    HasThermalProfilePath = hasThermalProfilePath,
                    HasPlatformProfilePath = hasPlatformProfilePath,
                    HasAcpiPlatformProfilePath = hasAcpiPlatformProfilePath,
                    SupportsManualFanControl = capabilityAssessment.SupportsManualFanControl,
                    SupportsProfileControl = capabilityAssessment.SupportsProfileControl,
                    SupportsTelemetry = capabilityAssessment.SupportsTelemetry,
                    WriteRequirementHint = isRoot
                        ? "root privileges detected; writable interfaces depend on kernel/firmware-exposed paths"
                        : "run with sudo for write-capable fan/performance control"
                },
                GpuTelemetrySource = gpuReading?.Source ?? "unavailable",
                GpuTelemetryPath = gpuReading?.Path ?? string.Empty,
                Gpu = new GpuInfo
                {
                    Name = nvmlGpu?.Name ?? string.Empty,
                    PowerWatts = nvmlGpu?.PowerWatts,
                    UtilizationPercent = nvmlGpu?.UtilizationPercent
                },
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            var json = JsonSerializer.Serialize(status, LinuxJsonContext.Default.SystemStatus);
            Console.WriteLine(json);
            return;
        }
        
        // Human-readable output
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              OmenCore Linux - System Status               ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        
        // EC Access - show detailed status
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║  EC Access: ✓ Available ({ec.AccessMethod})                       ║".PadRight(63) + "║");
        }
        else
        {
            Console.WriteLine("║  EC Access: ✗ Unavailable                                  ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  🔧 TROUBLESHOOTING STEPS:                                 ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  1. Check kernel modules:                                  ║");
            Console.WriteLine("║     sudo modprobe ec_sys write_support=1                   ║");
            Console.WriteLine("║     sudo modprobe hp-wmi                                   ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  2. Verify hardware access:                                ║");
            Console.WriteLine("║     ls -la /sys/kernel/debug/ec/ec0/io                     ║");
            Console.WriteLine("║     ls -la /sys/devices/platform/hp-wmi/                   ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  3. Check kernel version & distribution:                   ║");
            Console.WriteLine("║     uname -a                                               ║");
            Console.WriteLine("║     cat /etc/os-release                                     ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  4. For Fedora 43+ / RHEL 10+:                             ║");
            Console.WriteLine("║     ec_sys removed - use hp-wmi only                       ║");
            Console.WriteLine("║                                                            ║");
            Console.WriteLine("║  📖 See: https://github.com/theantipopau/omencore/wiki/Linux");
        }
        
        // Temperatures
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  CAPABILITY: {capabilityAssessment.CapabilityKey,-44} ║");
        Console.WriteLine($"║  {Truncate(capabilityAssessment.Reason, 57),-57}║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  ACCESS CONTEXT                                           ║");
        Console.WriteLine($"║    Root: {(isRoot ? "yes" : "no"),-49} ║");
        Console.WriteLine($"║    Access Method: {Truncate(ec.AccessMethod, 39),-39} ║");
        Console.WriteLine($"║    EC io path: {(ecIoPathExists ? "present" : "missing"),-42} ║");
        Console.WriteLine($"║    hp-wmi path: {(hpWmiPathExists ? "present" : "missing"),-41} ║");
        Console.WriteLine($"║    hwmon fan control: {(ec.HasHwmonFanAccess ? "present" : "missing"),-35} ║");
        Console.WriteLine($"║    hp-wmi backend: {Truncate(ec.HpWmiCompatibilityLabel, 38),-38} ║");
        Console.WriteLine($"║    thermal/platform profile path: {Truncate($"{(hasThermalProfilePath ? "thermal" : "-")}/{(hasPlatformProfilePath ? "platform" : "-")}/{(hasAcpiPlatformProfilePath ? "acpi" : "-")}", 25),-25} ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  TEMPERATURES                                             ║");
        Console.WriteLine($"║    CPU Temperature: {cpuTemp ?? 0,3}°C                                ║");
        Console.WriteLine($"║    GPU Temperature: {gpuTemp ?? 0,3}°C                                ║");
        Console.WriteLine($"║    GPU Telemetry:  {Truncate(gpuReading == null ? "unavailable" : $"{gpuReading.Source} ({gpuReading.Path})", 36),-36}║");
        if (nvmlGpu != null)
        {
            Console.WriteLine($"║    GPU Name:       {Truncate(string.IsNullOrEmpty(nvmlGpu.Name) ? "unknown" : nvmlGpu.Name, 36),-36}║");
            Console.WriteLine($"║    GPU Power:      {(nvmlGpu.PowerWatts.HasValue ? $"{nvmlGpu.PowerWatts.Value:F1} W" : "unavailable"),-36}║");
            Console.WriteLine($"║    GPU Usage:      {(nvmlGpu.UtilizationPercent.HasValue ? $"{nvmlGpu.UtilizationPercent.Value}%" : "unavailable"),-36}║");
        }
        
        // Fans
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  FAN SPEEDS                                               ║");
        
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║    Fan 1 (CPU): {fan1Rpm,5} RPM ({fan1Pct,3}%)                        ║");
            Console.WriteLine($"║    Fan 2 (GPU): {fan2Rpm,5} RPM ({fan2Pct,3}%)                        ║");
        }
        else
        {
            Console.WriteLine("║    N/A - EC access required                               ║");
        }
        
        // Performance Mode
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  PERFORMANCE                                              ║");
        
        if (ec.IsAvailable)
        {
            Console.WriteLine($"║    Mode: {perfModeStr,-48} ║");
            Console.WriteLine($"║    Hold Enabled: {(config.Performance.HoldEnabled ? "yes" : "no"),-40} ║");
            Console.WriteLine($"║    Hold Interval: {config.Performance.HoldIntervalSeconds,3}s                            ║");
        }
        else
        {
            Console.WriteLine("║    N/A - EC access required                               ║");
        }
        
        // Keyboard
        Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  KEYBOARD LIGHTING                                        ║");
        Console.WriteLine($"║    HP WMI: {(keyboard.IsAvailable ? "✓ Available" : "✗ Unavailable"),-45} ║");
        
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        await Task.CompletedTask;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)] + "…";
    }
}
