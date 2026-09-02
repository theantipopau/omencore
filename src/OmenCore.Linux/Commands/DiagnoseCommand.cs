using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using OmenCore.Linux.Config;
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
        info.BoardId = await ReadTextAsync("/sys/devices/virtual/dmi/id/board_name")
                 ?? await ReadTextAsync("/sys/class/dmi/id/board_name")
                 ?? "Unknown";

        info.DebugFsMounted = await IsDebugFsMountedAsync();

        // Modules
        info.EcSysModuleLoaded = Directory.Exists("/sys/module/ec_sys");
        info.EcSysWriteSupport = await ReadTextAsync("/sys/module/ec_sys/parameters/write_support");

        info.HpWmiModuleLoaded = Directory.Exists("/sys/module/hp_wmi");
        info.HpWmiForceMultiplex = await ReadTextAsync("/sys/module/hp_wmi/parameters/force_multiplex");

        // Paths
        info.EcIoPathExists = File.Exists(LinuxSysfsPathMap.EcIoPath);

        info.HpWmiPathExists = Directory.Exists(LinuxSysfsPathMap.HpWmiRoot);
        info.HpWmiThermalProfileExists = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiThermalProfilePaths);
        info.HpWmiPlatformProfileExists = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.PlatformProfilePaths);
        info.HpWmiThermalProfileChoicesExists = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiThermalProfileChoicePaths);
        info.HpWmiPlatformProfileChoicesExists = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiPlatformProfileChoicePaths);
        info.HpWmiFanAlwaysOnExists = File.Exists("/sys/devices/platform/hp-wmi/fan_always_on");
        info.HpWmiFan1OutputExists = File.Exists("/sys/devices/platform/hp-wmi/fan1_output");
        info.HpWmiFan2OutputExists = File.Exists("/sys/devices/platform/hp-wmi/fan2_output");
        info.HpWmiFan1TargetExists = LinuxSysfsPathMap.HasHpWmiFanTarget(1);
        info.HpWmiFan2TargetExists = LinuxSysfsPathMap.HasHpWmiFanTarget(2);
        info.HpWmiPwm1EnableExists = LinuxSysfsPathMap.HasHpWmiPwmEnable(1);
        info.HpWmiPwm2EnableExists = LinuxSysfsPathMap.HasHpWmiPwmEnable(2);
        info.HpWmiPwm1Exists = LinuxSysfsPathMap.HasHpWmiPwm(1);
        info.HpWmiPwm2Exists = LinuxSysfsPathMap.HasHpWmiPwm(2);
        info.HpWmiFan1InputExists = LinuxSysfsPathMap.HasHpWmiFanInput(1);
        info.HpWmiFan2InputExists = LinuxSysfsPathMap.HasHpWmiFanInput(2);
        info.HpWmiPwm1Enable = await ReadTextAsync(LinuxSysfsPathMap.ResolveHpWmiPwmEnablePath(1));
        info.HpWmiPwm1 = await ReadTextAsync(LinuxSysfsPathMap.ResolveHpWmiPwmPath(1));

        // ACPI platform_profile (kernel 5.18+, used by 2025+ models)
        info.AcpiPlatformProfileExists = File.Exists(LinuxSysfsPathMap.AcpiPlatformProfilePath);
        if (info.AcpiPlatformProfileExists)
        {
            info.AcpiPlatformProfile = await ReadTextAsync(LinuxSysfsPathMap.AcpiPlatformProfilePath);
            info.AcpiPlatformProfileChoices = await ReadTextAsync(LinuxSysfsPathMap.AcpiPlatformProfileChoicesPath);
        }

        info.HpWmiPlatformProfile = await ReadFirstExistingTextAsync(LinuxSysfsPathMap.PlatformProfilePaths);
        info.HpWmiPlatformProfileChoices = await ReadFirstExistingTextAsync(LinuxSysfsPathMap.HpWmiPlatformProfileChoicePaths);
        info.HpWmiThermalProfile = await ReadFirstExistingTextAsync(LinuxSysfsPathMap.HpWmiThermalProfilePaths);
        info.HpWmiThermalProfileChoices = await ReadFirstExistingTextAsync(LinuxSysfsPathMap.HpWmiThermalProfileChoicePaths);

        var ec = new LinuxEcController();
        var hwmon = new LinuxHwMonController();
        var gpuReading = LinuxTelemetryResolver.GetGpuTemperature(ec, hwmon);
        info.Service = await CollectServiceDiagnosticsAsync();
        info.KernelIssueHints = await CollectKernelIssueHintsAsync();

        var effectiveConfig = OmenCoreConfig.Load();
        var configReport = OmenCoreConfig.LastLoadReport;
        info.ConfigSchemaVersion = effectiveConfig.SchemaVersion;
        info.ConfigLoadedPaths = configReport.LoadedPaths.ToList();
        info.ConfigMigrationWarnings = configReport.Warnings.ToList();
        if (info.ConfigMigrationWarnings.Count > 0)
        {
            info.Notes.Add($"Config migration/parser warnings: {info.ConfigMigrationWarnings.Count}");
        }

        // Detection (use current controller logic)
        info.DetectedAccessMethod = ec.AccessMethod;
        info.EcControllerAvailable = ec.IsAvailable;
        info.IsUnsafeEcModel = ec.IsUnsafeEcModel;
        info.HasHwmonFanAccess = ec.HasHwmonFanAccess;
        info.HpWmiModuleSource = ec.HpWmiModuleSource;
        info.HpWmiModuleLooksDkms = ec.HpWmiModuleLooksDkms;
        info.HpWmiDkmsCompatibleFanBackend = ec.HasHpWmiDkmsCompatibleFanBackend;
        info.HpWmiCompatibilityLabel = ec.HpWmiCompatibilityLabel;
        info.GpuTelemetrySource = gpuReading?.Source ?? "unavailable";
        info.GpuTelemetryPath = gpuReading?.Path ?? string.Empty;

        var capabilityAssessment = LinuxCapabilityClassifier.Assess(
            info.IsRoot,
            ec.HasEcAccess,
            info.HpWmiPathExists,
            info.HpWmiThermalProfileExists,
            info.HpWmiPlatformProfileExists,
            info.AcpiPlatformProfileExists,
            info.HpWmiFan1OutputExists,
            info.HpWmiFan2OutputExists,
            info.HpWmiFan1TargetExists,
            info.HpWmiFan2TargetExists,
            info.HasHwmonFanAccess,
            info.EcIoPathExists || info.HpWmiPathExists,
            info.IsUnsafeEcModel,
            info.Model,
            info.BoardId);
        info.CapabilityClass = capabilityAssessment.CapabilityKey;
        info.CapabilityReason = capabilityAssessment.Reason;
        info.SupportsManualFanControl = capabilityAssessment.SupportsManualFanControl;
        info.SupportsProfileControl = capabilityAssessment.SupportsProfileControl;
        info.SupportsTelemetry = capabilityAssessment.SupportsTelemetry;
        
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

        if (info.HpWmiPathExists && !info.HpWmiThermalProfileExists && !info.HpWmiPlatformProfileExists)
        {
            if (info.AcpiPlatformProfileExists)
            {
                info.Notes.Add("hp-wmi directory exists but thermal_profile not found. Using ACPI platform_profile instead.");
            }
            else
            {
                info.Notes.Add("hp-wmi directory exists but thermal_profile not found; your kernel/firmware may not expose OMEN controls.");
                info.Recommendations.Add("Try a newer kernel (6.5+ recommended for 2023+ OMEN) and ensure hp-wmi is loaded: sudo modprobe hp-wmi");
                info.Recommendations.Add("If your firmware supports it, test kernel parameter hp_wmi.force_multiplex=1 and reboot.");
                info.Recommendations.Add("Arch-family optional path: test hp-omen-gaming-wmi-dkms (AUR). It often automates hp-wmi profile-path setup, but remains board-dependent and may not fix profile exposure on all models.");
            }
        }

        if (info.HpWmiPwm1EnableExists && info.HpWmiPwm1Exists)
        {
            info.Notes.Add("hp-wmi hwmon manual PWM duty is available. OmenCore can use pwm_enable=1 plus pwm1 duty for --speed/custom-curve writes, pwm_enable=2 for auto, and pwm_enable=0 for max fan.");
        }

        if (info.HpWmiDkmsCompatibleFanBackend)
        {
            info.Notes.Add(info.HpWmiModuleLooksDkms
                ? "hp-omen-gaming-wmi-dkms compatible backend detected: hp_wmi is exposing standard hwmon PWM/fan files, so OmenCore can use the same sysfs path without a separate branch."
                : "Upstream hp-wmi/hwmon fan backend detected: this uses the same sysfs shape expected from hp-omen-gaming-wmi-dkms compatibility mode.");
        }
        else if (info.HpWmiModuleLooksDkms)
        {
            info.Notes.Add("A DKMS hp_wmi module appears to be installed/loaded, but writable hp-wmi hwmon fan files were not detected. This usually means the current board is not enabled by the DKMS module or the module did not bind to this firmware path.");
            info.Recommendations.Add("Check DKMS support for this exact board ID and include `dkms status`, `modinfo hp_wmi`, and the hp-wmi/hwmon tree in Linux support reports.");
        }

        if (string.Equals(info.BoardId, "8E35", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(info.BoardId, "8D24", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(info.BoardId, "8D26", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add($"Board {info.BoardId} detected (OMEN 16 ap0xxx family): prefer upstream hp-wmi/hwmon/platform_profile interfaces and avoid legacy EC writes.");
            if (!info.HpWmiPwm1EnableExists && !info.HpWmiThermalProfileExists && !info.AcpiPlatformProfileExists)
            {
                info.Recommendations.Add("This board likely needs a newer hp-wmi kernel path. Attach diagnose output, hp-wmi tree, and ACPI tables when asking upstream hp-wmi maintainers for board support.");
            }
        }

        if (info.HpWmiPathExists && string.IsNullOrWhiteSpace(info.HpWmiForceMultiplex))
        {
            info.Recommendations.Add("If hp-wmi profile paths are missing or the dGPU power state is desynced, test kernel parameter hp_wmi.force_multiplex=1, reboot, then rerun diagnose.");
        }

        if (string.Equals(info.BoardId, "8C58", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(info.BoardId, "8E41", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add($"Board {info.BoardId} detected (OMEN Transcend 14 family): legacy EC writes are often firmware-reverted.");
            info.Notes.Add("Prefer hp-wmi/acpi interfaces. If profile paths are missing, this is likely a kernel/firmware exposure gap, not a user permissions issue.");
            info.Recommendations.Add("Use latest Fedora kernel available (6.19+) and rerun: omencore-cli diagnose --report");
            info.Recommendations.Add("If fan*_target exists under hp-wmi/hwmon, test manual writes there first; otherwise use profile-based fallback only.");
            info.Recommendations.Add("For Transcend 14-fb1xxx (8E41), collect and attach full diagnose output plus ACPI dump when requesting kernel hp-wmi support updates.");
        }

        if (string.Equals(info.BoardId, "8D41", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add("Board 8D41 detected (OMEN Max 16-ah0xxx): Linux GPU TGP may stay capped near base power if hp-wmi does not send the GPU thermal modes / Dynamic Boost unlock that Windows OGH sends.");
            info.Recommendations.Add("If RTX 5080/5070-class TGP remains capped around 80W, attach `sudo omencore-cli diagnose --report`, `journalctl -u nvidia-powerd -b --no-pager`, and an ACPI dump to GitHub issue #123 or the upstream hp-wmi thread.");
        }

        if (string.Equals(info.BoardId, "8787", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add("Board 8787 detected (OMEN 15-en0xxx / Ryzen + RTX 2060 generation). Windows support uses a conservative legacy profile; Linux support depends on the kernel exposing ec_sys or hp-wmi control paths on this unit.");
            info.Notes.Add("Board 8787 not appearing in third-party hp-omen fan-daemon board lists does not by itself mean OmenCore profile control is unsupported; OmenCore uses whichever kernel path is actually exposed (ACPI profile, hp-wmi, hwmon, or legacy EC).");
            if (info.EcIoPathExists)
            {
                info.Notes.Add("Legacy EC io is present for board 8787. Prefer OmenCore CLI fan/profile commands over raw register writes so safety checks and auto-restore remain active.");
            }
            else
            {
                info.Recommendations.Add("For board 8787 fan control, test the legacy EC path first: `sudo modprobe ec_sys write_support=1`, mount debugfs if needed, then rerun `sudo omencore-cli diagnose --report`.");
            }

            if (!info.HpWmiFan1InputExists && !info.HpWmiFan2InputExists)
            {
                info.Notes.Add("Board 8787 RPM readback is still field-unverified; fan commands may work even when Linux RPM telemetry is unavailable.");
            }

            if (info.AcpiPlatformProfileExists)
            {
                info.Notes.Add("ACPI platform_profile is present on this board, so performance profile switching can still work even when hp-wmi hwmon fan files are absent.");
            }

            info.Recommendations.Add("When requesting Linux 8787 support, attach `sudo omencore-cli diagnose --report`, `sudo dmidecode -t system -t baseboard`, hp-wmi tree, hwmon tree, and the exact RTX 2060 driver/kernel version.");
        }

        if (string.Equals(info.BoardId, "8C30", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add("Board 8C30 detected (Victus 15-fb1xxx / Ryzen 5 7535HS generation). If the GUI reports performance control unavailable, this usually means the current kernel did not expose hp-wmi/platform_profile controls for this board.");
            info.Notes.Add("Treat this as profile-control triage until a diagnose bundle shows writable profile or EC paths; do not assume the disabled GUI card is a successful backend apply failure.");
            info.Recommendations.Add("For board 8C30 Linux reports, attach `sudo omencore-cli diagnose --report`, `sudo dmidecode -t system -t baseboard -t bios`, `find /sys/devices/platform/hp-wmi -maxdepth 4 -type f -print 2>/dev/null`, `find /sys/class/hwmon -maxdepth 3 -type f -name 'fan*' -o -name 'pwm*' -o -name 'temp*_input' 2>/dev/null`, and `journalctl -k -b --no-pager | grep -iE 'hp_wmi|platform_profile|wmi_bus|WQ00'`.");
        }

        if (string.Equals(info.BoardId, "8C77", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add("Board 8C77 detected (OMEN 16-wf1xxx / Insyde BIOS commonly reported). On some units, hp-wmi profile controls may appear available but still fail to deliver expected sustained power behavior.");
            info.Recommendations.Add("For 8C77 performance/profile gaps, capture `sudo omencore-cli diagnose --report`, `journalctl -k -b --no-pager | grep -iE 'hp_wmi|wmi_bus|WQ00|platform_profile'`, and `sudo dmidecode -t system -t baseboard -t bios`.");
            info.Recommendations.Add("If logs show WQ00 firmware-method warnings, treat this as a firmware/kernel integration gap and attach ACPI tables (`sudo acpidump`) for upstream hp-wmi triage.");
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

        if (gpuReading == null)
        {
            // NvmlInterop.LastFailureReason is populated as a side effect of the
            // LinuxTelemetryResolver.GetGpuTemperature call above (it tries NVML first) - surfacing
            // it here directly answers GitHub #186's own request: "if NVML loading is attempted and
            // fails, surfacing the error... would make this class of report much easier to triage."
            var nvmlNote = NvmlInterop.LastFailureReason;
            info.Notes.Add(nvmlNote != null
                ? $"GPU telemetry fallback chain exhausted: NVML unavailable ({nvmlNote}); hwmon, thermal-zone, and EC temperature sources are also unreadable."
                : "GPU telemetry fallback chain exhausted: no NVML, hwmon, thermal-zone, or EC temperature source is currently readable.");
        }
        else if (!string.Equals(gpuReading.Source, "nvml", StringComparison.Ordinal) &&
                 !string.Equals(gpuReading.Source, "hwmon", StringComparison.Ordinal))
        {
            info.Notes.Add($"GPU telemetry is running on fallback source '{gpuReading.Source}' via {gpuReading.Path}.");
        }

        info.Notes.Add($"Capability classification: {info.CapabilityClass}.");
        info.Notes.Add(info.CapabilityReason);

        foreach (var hint in info.KernelIssueHints)
        {
            info.Notes.Add($"{hint.Severity}: {hint.Summary}");
            if (!string.IsNullOrWhiteSpace(hint.Recommendation))
            {
                info.Recommendations.Add(hint.Recommendation);
            }
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

        if (!info.Service.SystemdAvailable)
        {
            info.Notes.Add("systemd was not detected; daemon service commands may not apply on this distribution/session.");
        }
        else if (!info.Service.UnitInstalled)
        {
            info.Recommendations.Add("Install or refresh the Linux daemon service: sudo omencore-cli daemon --install");
        }
        else if (!string.Equals(info.Service.ActiveState, "active", StringComparison.OrdinalIgnoreCase))
        {
            info.Notes.Add($"OmenCore daemon service is installed but not active (state: {info.Service.ActiveState}).");
        }

        if (!info.Service.SystemConfigExists)
        {
            info.Recommendations.Add("Create the system daemon config with: sudo omencore-cli daemon --install");
        }

        if (info.Service.UnitInstalled && !info.Service.BundleExtractDirExists)
        {
            info.Notes.Add($"{info.Service.BundleExtractDir} is missing; the current generated service creates it at startup, but rerun `sudo omencore-cli daemon --install` if an older unit fails before startup.");
        }

        return info;
    }

    private static async Task<LinuxServiceDiagnostics> CollectServiceDiagnosticsAsync()
    {
        var service = new LinuxServiceDiagnostics
        {
            SystemdAvailable = Directory.Exists("/run/systemd/system") ||
                               File.Exists("/usr/bin/systemctl") ||
                               File.Exists("/bin/systemctl"),
            UnitInstalled = File.Exists(LinuxServiceDiagnostics.DefaultUnitPath),
            SystemConfigExists = File.Exists(OmenCoreConfig.SystemConfigPath),
            UserConfigExists = File.Exists(OmenCoreConfig.DefaultConfigPath),
            BundleExtractDirExists = Directory.Exists(LinuxServiceDiagnostics.DefaultBundleExtractDir)
        };

        if (service.SystemdAvailable && service.UnitInstalled)
        {
            service.ActiveState = await TryRunCommandAllowFailureAsync("systemctl", "is-active omencore.service", timeoutMs: 1500)
                                  ?? "unknown";
        }

        return service;
    }

    private static async Task<List<LinuxKernelIssueHint>> CollectKernelIssueHintsAsync()
    {
        var kernelLog = await ReadRecentKernelLogAsync();
        var nvidiaPowerdLog = await TryRunCommandAsync("journalctl", "-u nvidia-powerd -b -n 120 --no-pager --output=short-iso", timeoutMs: 1800);
        return AnalyzeKernelLog(string.Join('\n', kernelLog, nvidiaPowerdLog ?? string.Empty));
    }

    internal static List<LinuxKernelIssueHint> AnalyzeKernelLog(string kernelLog)
    {
        var hints = new List<LinuxKernelIssueHint>();
        if (string.IsNullOrWhiteSpace(kernelLog))
        {
            return hints;
        }

        var lines = kernelLog
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var nvidiaSbiosLines = lines
            .Where(line =>
                line.Contains("NVRM:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("PlatformRequestHandler", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("SBIOS", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("NV_ERR_INVALID_DATA", StringComparison.OrdinalIgnoreCase)))
            .Take(4)
            .ToList();

        if (nvidiaSbiosLines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "nvidia-sbios-platform-request",
                Severity = "Warning",
                Summary = "NVIDIA driver is reporting invalid ACPI/SBIOS platform-request data.",
                Evidence = string.Join(" | ", nvidiaSbiosLines),
                Recommendation = "For GPU idle-after-poweroff or platform-power-mode bugs, attach `journalctl -k -b`, `sudo omencore-cli diagnose --report`, and an optional `sudo acpidump` to the issue. This points toward firmware/kernel/NVIDIA ACPI integration rather than a normal hp-wmi permission problem."
            });
        }

        var nvidiaPowerdBoostLines = lines
            .Where(line =>
                line.Contains("nvidia-powerd", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Dynamic Boost", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("disable", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        if (nvidiaPowerdBoostLines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "nvidia-powerd-dynamic-boost-disabled",
                Severity = "Warning",
                Summary = "nvidia-powerd reports that firmware/SBIOS disabled Dynamic Boost control.",
                Evidence = string.Join(" | ", nvidiaPowerdBoostLines),
                Recommendation = "On OMEN Max 16 8D41 / RTX 50-series reports, this usually means the Linux hp-wmi path applied the thermal profile but did not unlock GPU thermal modes / PPAB. `nvidia-smi -pl` is expected to be blocked on these laptop GPUs; collect `journalctl -u nvidia-powerd -b`, `journalctl -k -b`, and ACPI tables for kernel-side hp-wmi follow-up."
            });
        }

        var nvidiaGpsDsmLines = lines
            .Where(line =>
                (line.Contains("GPS", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("_DSM", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("D3cold", StringComparison.OrdinalIgnoreCase)) &&
                (line.Contains("NVRM", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("ACPI", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("nvidia", StringComparison.OrdinalIgnoreCase)))
            .Take(4)
            .ToList();

        if (nvidiaGpsDsmLines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "nvidia-gps-acpi-dsm-d3cold",
                Severity = "Warning",
                Summary = "Kernel logs mention NVIDIA/ACPI DSM or D3cold power-state problems.",
                Evidence = string.Join(" | ", nvidiaGpsDsmLines),
                Recommendation = "For OMEN 16 RTX 50-series shutdown drain reports, collect `journalctl -k -b`, `cat /sys/bus/pci/devices/*/power/runtime_status` for NVIDIA devices, `sudo omencore-cli diagnose --report`, and ACPI tables. Treat this as firmware/kernel/NVIDIA integration evidence rather than a normal OmenCore fan-control bug."
            });
        }

        var i2cGroupLines = lines
            .Where(line =>
                line.Contains("Failed to resolve group 'i2c'", StringComparison.OrdinalIgnoreCase) ||
                (line.Contains("99-i2c.rules", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains("Unknown group", StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .ToList();

        var hpWmiWq00Lines = lines
            .Where(line =>
                line.Contains("WQ00", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("Firmware Bug", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("query control method not found", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("wmi_bus", StringComparison.OrdinalIgnoreCase)))
            .Take(4)
            .ToList();

        var hpWmiWmaaAbortLines = lines
            .Where(line =>
                (line.Contains("WMAA", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("WHCM", StringComparison.OrdinalIgnoreCase)) &&
                (line.Contains("AE_ABORT", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Method parse/execution failed", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("CreateField", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Abort", StringComparison.OrdinalIgnoreCase)))
            .Take(4)
            .ToList();

        if (hpWmiWmaaAbortLines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "hp-wmi-wmaa-whcm-abort",
                Severity = "Warning",
                Summary = "Kernel logs show HP WMI ACPI WMAA/WHCM aborts; WMI-backed controls may be degraded even when sysfs paths exist.",
                Evidence = string.Join(" | ", hpWmiWmaaAbortLines),
                Recommendation = "Treat fan profile, keyboard RGB, and HP WMI battery status as degraded until an RPM/readback check proves hardware effect. Attach `journalctl -k -b`, `sudo omencore-cli diagnose --report`, hp-wmi/hwmon listings, and ACPI tables for upstream hp-wmi triage."
            });
        }

        if (hpWmiWq00Lines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "hp-wmi-wq00-missing-method",
                Severity = "Warning",
                Summary = "Kernel reports missing WQ00 query method in WMI firmware path.",
                Evidence = string.Join(" | ", hpWmiWq00Lines),
                Recommendation = "This often indicates a firmware/kernel hp-wmi integration gap on some OMEN Insyde boards. Do not assume board-list support guarantees working profile power control; attach diagnose output, full kernel log, and ACPI tables to upstream hp-wmi reports."
            });
        }

        if (i2cGroupLines.Count > 0)
        {
            hints.Add(new LinuxKernelIssueHint
            {
                Id = "missing-i2c-group-udev-rule",
                Severity = "Info",
                Summary = "A udev rule references the `i2c` group, but that group does not exist.",
                Evidence = string.Join(" | ", i2cGroupLines),
                Recommendation = "Create the expected group (`sudo groupadd -r i2c`) or remove/update `/etc/udev/rules.d/99-i2c.rules`, then reload udev rules if you rely on i2c device access."
            });
        }

        return hints;
    }

    private static async Task<string> ReadRecentKernelLogAsync()
    {
        var journal = await TryRunCommandAsync("journalctl", "-k -b -n 300 --no-pager --output=short-iso", timeoutMs: 2500);
        if (!string.IsNullOrWhiteSpace(journal))
        {
            return journal;
        }

        return await TryRunCommandAsync("dmesg", "--color=never", timeoutMs: 2500) ?? string.Empty;
    }

    private static async Task<string?> TryRunCommandAsync(string fileName, string arguments, int timeoutMs)
    {
        Process? process = null;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return process.ExitCode == 0 ? await outputTask : null;
        }
        catch (Exception)
        {
            try
            {
                if (process?.HasExited == false)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Best effort only; diagnose must never fail because journal sampling failed.
            }

            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<string?> TryRunCommandAllowFailureAsync(string fileName, string arguments, int timeoutMs)
    {
        Process? process = null;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            return (await outputTask).Trim();
        }
        catch (Exception)
        {
            try
            {
                if (process?.HasExited == false)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Best effort only; service detection should not break diagnose.
            }

            return null;
        }
        finally
        {
            process?.Dispose();
        }
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

    private static async Task<string?> ReadTextAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

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

    private static async Task<string?> ReadFirstExistingTextAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var value = await ReadTextAsync(path);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void PrintHumanReadable(DiagnoseInfo info)
    {
        if (UseAsciiDiagnoseOutput())
        {
            PrintHumanReadableAscii(info);
            return;
        }
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
        Console.WriteLine($"║  Board ID:  {info.BoardId,-76}║");
        Console.WriteLine(midBorder);
        Console.WriteLine($"║  Root:      {(info.IsRoot ? "✓" : "✗"),-76}║");
        Console.WriteLine($"║  debugfs:   {(info.DebugFsMounted ? "✓ mounted" : "✗ not mounted"),-76}║");
        Console.WriteLine($"║  ec_io:     {(info.EcIoPathExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  ec_sys:    {(info.EcSysModuleLoaded ? "✓ loaded" : "✗ not loaded"),-76}║");
        Console.WriteLine($"║  ec_sys ws: {(string.IsNullOrWhiteSpace(info.EcSysWriteSupport) ? "(n/a)" : info.EcSysWriteSupport),-76}║");
        Console.WriteLine($"║  hp_wmi:    {(info.HpWmiModuleLoaded ? "✓ loaded" : "✗ not loaded"),-76}║");
        Console.WriteLine($"║  hp-wmi dir:{(info.HpWmiPathExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  thermal:   {(info.HpWmiThermalProfileExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  wmi_prof:  {(info.HpWmiPlatformProfileExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  therm_ch:  {(info.HpWmiThermalProfileChoicesExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  wmi_ch:    {(info.HpWmiPlatformProfileChoicesExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan1_out:  {(info.HpWmiFan1OutputExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan2_out:  {(info.HpWmiFan2OutputExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan1_tgt:  {(info.HpWmiFan1TargetExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  fan2_tgt:  {(info.HpWmiFan2TargetExists ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  acpi_prof: {(info.AcpiPlatformProfileExists ? $"✓ ({info.AcpiPlatformProfile ?? "?"})" : "✗ missing"),-76}║");
        Console.WriteLine($"║  hwmon_fan: {(info.HasHwmonFanAccess ? "✓ present" : "✗ missing"),-76}║");
        Console.WriteLine($"║  wmi_back:  {Truncate(info.HpWmiCompatibilityLabel, 76),-76}║");
        Console.WriteLine($"║  wmi_mod:   {Truncate(info.HpWmiModuleSource, 76),-76}║");
        Console.WriteLine(midBorder);
        Console.WriteLine($"║  Capability:{Truncate(info.CapabilityClass, 76),-76}║");
        Console.WriteLine($"║  Config Sch:{info.ConfigSchemaVersion,-76}║");
        Console.WriteLine($"║  GPU Telem.: {Truncate($"{info.GpuTelemetrySource} {info.GpuTelemetryPath}".Trim(), 76),-76}║");
        Console.WriteLine($"║  Detected:  {info.DetectedAccessMethod,-76}║");
        Console.WriteLine($"║  Available: {(info.EcControllerAvailable ? "✓" : "✗"),-76}║");
        Console.WriteLine($"Service: systemd={(info.Service.SystemdAvailable ? "present" : "missing")}, unit={(info.Service.UnitInstalled ? info.Service.ActiveState : "not installed")}, system_config={(info.Service.SystemConfigExists ? "present" : "missing")}");

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

        if (info.KernelIssueHints.Count > 0)
        {
            Console.WriteLine(midBorder);
            Console.WriteLine($"║  {"Kernel Hints:",-86}║");
            foreach (var hint in info.KernelIssueHints.Take(4))
            {
                foreach (var line in WrapText($"{hint.Severity}: {hint.Summary}", innerWidth - 7))
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

    private static bool UseAsciiDiagnoseOutput() => true;

    private static void PrintHumanReadableAscii(DiagnoseInfo info)
    {
        const int innerWidth = 88;
        var border = "+" + new string('-', innerWidth) + "+";

        static string State(bool value, string positive = "present", string negative = "missing") =>
            value ? $"OK {positive}" : $"NO {negative}";

        static string Pad(string value, int width) =>
            value.Length > width ? value[..Math.Max(0, width - 1)] + "." : value.PadRight(width);

        static string Shorten(string value, int max) =>
            value.Length <= max ? value : (max <= 3 ? value[..max] : value[..(max - 3)] + "...");

        static void WriteLine(string label, string value)
        {
            Console.WriteLine($"|  {label,-11}{Pad(value, 75)}|");
        }

        Console.WriteLine();
        Console.WriteLine(border);
        Console.WriteLine($"|{"OmenCore Linux - Diagnose",56}{"",-32}|");
        Console.WriteLine(border);
        WriteLine("Version:", info.Version);
        WriteLine("Runtime:", info.Runtime);
        WriteLine("OS:", info.OsPrettyName);
        WriteLine("Kernel:", info.KernelRelease);
        WriteLine("Model:", info.Model);
        WriteLine("Board ID:", info.BoardId);
        Console.WriteLine(border);
        WriteLine("Root:", info.IsRoot ? "OK" : "NO");
        WriteLine("debugfs:", State(info.DebugFsMounted, "mounted", "not mounted"));
        WriteLine("ec_io:", State(info.EcIoPathExists));
        WriteLine("ec_sys:", State(info.EcSysModuleLoaded, "loaded", "not loaded"));
        WriteLine("ec_sys ws:", string.IsNullOrWhiteSpace(info.EcSysWriteSupport) ? "(n/a)" : info.EcSysWriteSupport);
        WriteLine("hp_wmi:", State(info.HpWmiModuleLoaded, "loaded", "not loaded"));
        WriteLine("hp-wmi:", State(info.HpWmiPathExists));
        WriteLine("thermal:", State(info.HpWmiThermalProfileExists));
        WriteLine("wmi_prof:", State(info.HpWmiPlatformProfileExists));
        WriteLine("therm_ch:", State(info.HpWmiThermalProfileChoicesExists));
        WriteLine("wmi_ch:", State(info.HpWmiPlatformProfileChoicesExists));
        WriteLine("fan1_out:", State(info.HpWmiFan1OutputExists));
        WriteLine("fan2_out:", State(info.HpWmiFan2OutputExists));
        WriteLine("fan1_tgt:", State(info.HpWmiFan1TargetExists));
        WriteLine("fan2_tgt:", State(info.HpWmiFan2TargetExists));
        WriteLine("acpi_prof:", info.AcpiPlatformProfileExists ? $"OK ({info.AcpiPlatformProfile ?? "?"})" : "NO missing");
        WriteLine("hwmon_fan:", State(info.HasHwmonFanAccess));
        WriteLine("wmi_back:", Shorten(info.HpWmiCompatibilityLabel, 75));
        WriteLine("wmi_mod:", Shorten(info.HpWmiModuleSource, 75));
        Console.WriteLine(border);
        WriteLine("Capability:", Shorten(info.CapabilityClass, 75));
        WriteLine("Config Sch:", info.ConfigSchemaVersion.ToString());
        WriteLine("GPU Telem:", Shorten($"{info.GpuTelemetrySource} {info.GpuTelemetryPath}".Trim(), 75));
        WriteLine("Detected:", info.DetectedAccessMethod);
        WriteLine("Available:", info.EcControllerAvailable ? "OK" : "NO");
        Console.WriteLine($"Service: systemd={(info.Service.SystemdAvailable ? "present" : "missing")}, unit={(info.Service.UnitInstalled ? info.Service.ActiveState : "not installed")}, system_config={(info.Service.SystemConfigExists ? "present" : "missing")}");

        if (info.IsUnsafeEcModel)
        {
            WriteLine("EC Safety:", "WARN blocked (new model)");
        }

        if (info.Notes.Count > 0)
        {
            Console.WriteLine(border);
            WriteLine("Notes:", string.Empty);
            foreach (var note in info.Notes.Take(6))
            {
                foreach (var line in WrapText(note, innerWidth - 7))
                {
                    Console.WriteLine($"|   - {Pad(line, innerWidth - 5)}|");
                }
            }
        }

        if (info.KernelIssueHints.Count > 0)
        {
            Console.WriteLine(border);
            WriteLine("Kernel:", "Hints");
            foreach (var hint in info.KernelIssueHints.Take(4))
            {
                foreach (var line in WrapText($"{hint.Severity}: {hint.Summary}", innerWidth - 7))
                {
                    Console.WriteLine($"|   - {Pad(line, innerWidth - 5)}|");
                }
            }
        }

        if (info.Recommendations.Count > 0)
        {
            Console.WriteLine(border);
            WriteLine("Next:", "Steps");
            foreach (var rec in info.Recommendations.Take(6))
            {
                foreach (var line in WrapText(rec, innerWidth - 7))
                {
                    Console.WriteLine($"|   - {Pad(line, innerWidth - 5)}|");
                }
            }
        }

        Console.WriteLine(border);
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
        Console.WriteLine($"- **Board ID:** {info.BoardId}");
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
        Console.WriteLine($"| Fan 1 Target Control (`fan1_target`) | {(info.HpWmiFan1TargetExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| Fan 2 Target Control (`fan2_target`) | {(info.HpWmiFan2TargetExists ? "✓ Present" : "✗ Missing")} |");
        Console.WriteLine($"| HP-WMI pwm1_enable | {(info.HpWmiPwm1EnableExists ? $"Present ({info.HpWmiPwm1Enable ?? "?"})" : "Missing")} |");
        Console.WriteLine($"| HP-WMI pwm1 duty | {(info.HpWmiPwm1Exists ? $"Present ({info.HpWmiPwm1 ?? "?"})" : "Missing")} |");
        Console.WriteLine($"| HP-WMI fan inputs | {((info.HpWmiFan1InputExists || info.HpWmiFan2InputExists) ? "Present" : "Missing")} |");
        Console.WriteLine($"| hp_wmi.force_multiplex | {(string.IsNullOrWhiteSpace(info.HpWmiForceMultiplex) ? "N/A" : info.HpWmiForceMultiplex)} |");
        Console.WriteLine($"| HP-WMI compatibility backend | {info.HpWmiCompatibilityLabel} |");
        Console.WriteLine($"| HP-WMI module source | {info.HpWmiModuleSource} |");
        Console.WriteLine($"| HP-WMI DKMS detected | {(info.HpWmiModuleLooksDkms ? "Yes" : "No")} |");
        Console.WriteLine();
        Console.WriteLine("## Service / Packaging Diagnostics");
        Console.WriteLine();
        Console.WriteLine($"- **systemd Available:** {(info.Service.SystemdAvailable ? "Yes" : "No")}");
        Console.WriteLine($"- **OmenCore Unit:** {(info.Service.UnitInstalled ? info.Service.ActiveState : "Not installed")}");
        Console.WriteLine($"- **System Config:** {(info.Service.SystemConfigExists ? "Present" : "Missing")} (`{info.Service.SystemConfigPath}`)");
        Console.WriteLine($"- **User Config:** {(info.Service.UserConfigExists ? "Present" : "Missing")} (`{info.Service.UserConfigPath}`)");
        Console.WriteLine($"- **Bundle Extract Dir:** {(info.Service.BundleExtractDirExists ? "Present" : "Missing")} (`{info.Service.BundleExtractDir}`)");
        Console.WriteLine($"- **Config Schema Version:** {info.ConfigSchemaVersion}");
        if (info.ConfigLoadedPaths.Count > 0)
        {
            Console.WriteLine($"- **Config Load Paths:** {string.Join(", ", info.ConfigLoadedPaths)}");
        }
        Console.WriteLine();
        Console.WriteLine($"**Capability Classification:** `{info.CapabilityClass}`");
        Console.WriteLine();
        Console.WriteLine($"**Capability Reason:** {info.CapabilityReason}");
        Console.WriteLine();
        Console.WriteLine($"**GPU Telemetry Source:** `{info.GpuTelemetrySource}`");
        Console.WriteLine();
        Console.WriteLine($"**GPU Telemetry Path:** `{info.GpuTelemetryPath}`");
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

        if (info.KernelIssueHints.Count > 0)
        {
            Console.WriteLine("## Kernel / Firmware Hints");
            Console.WriteLine();
            foreach (var hint in info.KernelIssueHints)
            {
                Console.WriteLine($"- **{hint.Id} ({hint.Severity})**: {hint.Summary}");
                if (!string.IsNullOrWhiteSpace(hint.Evidence))
                {
                    Console.WriteLine($"  - Evidence: `{hint.Evidence}`");
                }
                if (!string.IsNullOrWhiteSpace(hint.Recommendation))
                {
                    Console.WriteLine($"  - Recommendation: {hint.Recommendation}");
                }
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
    public string BoardId { get; set; } = "";

    public bool DebugFsMounted { get; set; }

    public bool EcSysModuleLoaded { get; set; }
    public string? EcSysWriteSupport { get; set; }
    public bool EcIoPathExists { get; set; }

    public bool HpWmiModuleLoaded { get; set; }
    public string? HpWmiForceMultiplex { get; set; }
    public bool HpWmiPathExists { get; set; }
    public bool HpWmiThermalProfileExists { get; set; }
    public bool HpWmiPlatformProfileExists { get; set; }
    public bool HpWmiThermalProfileChoicesExists { get; set; }
    public bool HpWmiPlatformProfileChoicesExists { get; set; }
    public bool HpWmiFanAlwaysOnExists { get; set; }
    public bool HpWmiFan1OutputExists { get; set; }
    public bool HpWmiFan2OutputExists { get; set; }
    public bool HpWmiFan1TargetExists { get; set; }
    public bool HpWmiFan2TargetExists { get; set; }
    public bool HpWmiPwm1EnableExists { get; set; }
    public bool HpWmiPwm2EnableExists { get; set; }
    public bool HpWmiPwm1Exists { get; set; }
    public bool HpWmiPwm2Exists { get; set; }
    public bool HpWmiFan1InputExists { get; set; }
    public bool HpWmiFan2InputExists { get; set; }
    public string? HpWmiPwm1Enable { get; set; }
    public string? HpWmiPwm1 { get; set; }
    public string? HpWmiThermalProfile { get; set; }
    public string? HpWmiThermalProfileChoices { get; set; }
    public string? HpWmiPlatformProfile { get; set; }
    public string? HpWmiPlatformProfileChoices { get; set; }

    public string DetectedAccessMethod { get; set; } = "none";
    public bool EcControllerAvailable { get; set; }
    public bool IsUnsafeEcModel { get; set; }
    public bool HasHwmonFanAccess { get; set; }
    public bool HpWmiDkmsCompatibleFanBackend { get; set; }
    public bool HpWmiModuleLooksDkms { get; set; }
    public string HpWmiModuleSource { get; set; } = string.Empty;
    public string HpWmiCompatibilityLabel { get; set; } = string.Empty;
    public Dictionary<string, object>? EcDiagnostics { get; set; }
    public string CapabilityClass { get; set; } = "unsupported-control";
    public string CapabilityReason { get; set; } = string.Empty;
    public bool SupportsManualFanControl { get; set; }
    public bool SupportsProfileControl { get; set; }
    public bool SupportsTelemetry { get; set; }
    public string GpuTelemetrySource { get; set; } = "unavailable";
    public string GpuTelemetryPath { get; set; } = string.Empty;
    public LinuxServiceDiagnostics Service { get; set; } = new();

    public int ConfigSchemaVersion { get; set; }
    public List<string> ConfigLoadedPaths { get; set; } = new();
    public List<string> ConfigMigrationWarnings { get; set; } = new();

    // ACPI platform_profile (2025+ models)
    public bool AcpiPlatformProfileExists { get; set; }
    public string? AcpiPlatformProfile { get; set; }
    public string? AcpiPlatformProfileChoices { get; set; }

    public List<LinuxKernelIssueHint> KernelIssueHints { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class LinuxServiceDiagnostics
{
    public const string DefaultUnitPath = "/etc/systemd/system/omencore.service";
    public const string DefaultBundleExtractDir = "/var/tmp/omencore";

    public bool SystemdAvailable { get; set; }
    public bool UnitInstalled { get; set; }
    public string UnitPath { get; set; } = DefaultUnitPath;
    public string ActiveState { get; set; } = "not-installed";
    public bool SystemConfigExists { get; set; }
    public string SystemConfigPath { get; set; } = OmenCoreConfig.SystemConfigPath;
    public bool UserConfigExists { get; set; }
    public string UserConfigPath { get; set; } = OmenCoreConfig.DefaultConfigPath;
    public bool BundleExtractDirExists { get; set; }
    public string BundleExtractDir { get; set; } = DefaultBundleExtractDir;
}

public class LinuxKernelIssueHint
{
    public string Id { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Summary { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}
