using System.Runtime.InteropServices;
using OmenCore.Linux.Hardware;

namespace OmenCore.Avalonia.Services;

/// <summary>
/// Linux implementation of hardware service using sysfs and ACPI interfaces.
/// </summary>
public class LinuxHardwareService : IHardwareService, IDisposable
{
    private readonly System.Timers.Timer _pollingTimer;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private HardwareStatus _lastStatus = new();
    private SystemCapabilities? _capabilities;
    private bool _disposed;
    private bool _pollingInProgress;
    private PerformanceMode? _lastFanFallbackMode;

    // HP OMEN specific paths
    private const string HP_WMI_PATH = LinuxSysfsPathMap.HpWmiRoot;
    private const string HWMON_BASE = "/sys/class/hwmon";
    private const string POWER_SUPPLY = "/sys/class/power_supply";
    private const string HP_WMI_FAN1_OUTPUT = "/sys/devices/platform/hp-wmi/fan1_output";
    private const string HP_WMI_FAN2_OUTPUT = "/sys/devices/platform/hp-wmi/fan2_output";
    private const string HP_WMI_FAN_ALWAYS_ON = "/sys/devices/platform/hp-wmi/fan_always_on";
    private static readonly string[] PowerProfilesCtlCandidates =
    {
        "/usr/bin/powerprofilesctl",
        "/bin/powerprofilesctl"
    };
    
    private string? _resolvedThermalPath; // Cached resolved path
    
    public event EventHandler<HardwareStatus>? StatusChanged;

    public LinuxHardwareService()
    {
        _pollingTimer = new System.Timers.Timer(2500)
        {
            AutoReset = false
        };
        _pollingTimer.Elapsed += async (s, e) =>
        {
            await PollHardwareAsync();
            if (!_disposed)
            {
                _pollingTimer.Start();
            }
        };
        _pollingTimer.Start();
    }

    private async Task PollHardwareAsync()
    {
        if (_pollingInProgress || _disposed)
        {
            return;
        }

        _pollingInProgress = true;
        try
        {
            var status = await GetStatusAsync();
            if (HasStatusChanged(_lastStatus, status))
            {
                _lastStatus = status;
                StatusChanged?.Invoke(this, status);
            }
        }
        catch
        {
            // Ignore polling errors
        }
        finally
        {
            _pollingInProgress = false;
        }
    }

    private static bool HasStatusChanged(HardwareStatus old, HardwareStatus current)
    {
        // GpuUsage/PowerConsumption were dead fields on Linux before the NVML fix above (always
        // 0, so comparing them here was moot) - now that they carry real data on NVIDIA systems,
        // leaving them out of this comparison would mean the dashboard's StatusChanged-driven
        // update misses a GPU load/power change that happens without temperature or fan RPM also
        // moving enough to cross their own thresholds.
        return Math.Abs(old.CpuTemperature - current.CpuTemperature) > 1 ||
               Math.Abs(old.GpuTemperature - current.GpuTemperature) > 1 ||
               old.CpuFanRpm != current.CpuFanRpm ||
               old.GpuFanRpm != current.GpuFanRpm ||
               Math.Abs(old.GpuUsage - current.GpuUsage) > 2 ||
               Math.Abs(old.PowerConsumption - current.PowerConsumption) > 1;
    }

    public async Task<HardwareStatus> GetStatusAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Return mock data for testing on Windows
            return GetMockStatus();
        }

        return await ExecuteWithIoLockAsync(async () =>
        {
            var status = new HardwareStatus();

            // Read CPU temperature from hwmon
            status.CpuTemperature = await ReadTemperatureAsync("coretemp") / 1000.0;

            // GitHub #186: GPU temperature/usage/power all read as 0/unavailable on a real NVIDIA
            // laptop GPU, because this whole service never queried NVML - only hwmon, which the
            // proprietary NVIDIA driver typically doesn't register a device for (unlike
            // amdgpu/nouveau). NVML first, since it's authoritative for NVIDIA GPUs regardless of
            // hwmon exposure; falls through to the existing hwmon-only path unchanged for AMD/Intel
            // GPUs or when NVML itself isn't available.
            var nvmlGpu = NvmlInterop.TryGetPrimaryGpu();
            status.GpuTemperature = nvmlGpu?.TemperatureC is int nvmlTempC
                ? nvmlTempC
                : await ReadGpuTemperatureAsync() / 1000.0;
            if (nvmlGpu?.UtilizationPercent is int nvmlUtil)
            {
                status.GpuUsage = nvmlUtil;
            }
            if (nvmlGpu?.PowerWatts is double nvmlWatts)
            {
                status.PowerConsumption = nvmlWatts;
            }

            // Read fan speeds
            status.CpuFanRpm = await ReadFanRpmAsync("cpu");
            status.GpuFanRpm = await ReadFanRpmAsync("gpu");

            // Read CPU/memory usage from /proc
            status.CpuUsage = await ReadCpuUsageAsync();
            var (memPercentage, memUsedGb, memTotalGb) = await ReadMemoryUsageAsync();
            status.MemoryUsage = memPercentage;
            status.MemoryUsedGb = memUsedGb;
            status.MemoryTotalGb = memTotalGb;

            // Read battery status
            (status.BatteryPercentage, status.IsOnBattery) = await ReadBatteryStatusAsync();

            return status;
        });
    }

    private static HardwareStatus GetMockStatus()
    {
        var rng = new Random();
        var cpuFanPercent = 30 + rng.Next(0, 40);
        var gpuFanPercent = 25 + rng.Next(0, 45);
        var memUsed = 8.0 + rng.NextDouble() * 12;
        var memTotal = 32.0;
        return new HardwareStatus
        {
            CpuTemperature = 45 + rng.Next(0, 20),
            GpuTemperature = 40 + rng.Next(0, 25),
            CpuFanRpm = 2000 + rng.Next(0, 1000),
            GpuFanRpm = 2500 + rng.Next(0, 1500),
            CpuFanPercent = cpuFanPercent,
            GpuFanPercent = gpuFanPercent,
            CpuUsage = 10 + rng.Next(0, 50),
            GpuUsage = 5 + rng.Next(0, 60),
            MemoryUsage = (memUsed / memTotal) * 100,
            MemoryUsedGb = memUsed,
            MemoryTotalGb = memTotal,
            PowerConsumption = 25 + rng.Next(0, 50),
            BatteryPercentage = 75 + rng.Next(-20, 25),
            IsOnBattery = false,
            IsThrottling = false,
            ThrottlingReason = null
        };
    }

    public async Task<SystemCapabilities> GetCapabilitiesAsync()
    {
        // Recompute each call so runtime kernel/module changes (e.g., hp-wmi/ec_sys load)
        // are reflected without forcing an app restart.
        _capabilities = new SystemCapabilities();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Mock capabilities for testing
            return new SystemCapabilities
            {
                HasKeyboardBacklight = true,
                HasFourZoneRgb = true,
                HasDiscreteGpu = true,
                HasGpuMuxSwitch = true,
                SupportsFanControl = true,
                SupportsFanSurface = true,
                SupportsPerformanceProfiles = true,
                SupportsKeyboardBrightness = true,
                FanControlCapabilityClass = "full-control",
                FanControlCapabilityReason = "Mock environment reports full control.",
                ModelName = "HP OMEN 16 (Mock)",
                CpuName = "AMD Ryzen 9 7945HX",
                GpuName = "NVIDIA GeForce RTX 4070"
            };
        }

        // Check for HP OMEN thermal/profile interfaces using centralized path normalization.
        _resolvedThermalPath = ResolveThermalProfilePath();
        bool hasDirectFanControl = File.Exists(HP_WMI_FAN1_OUTPUT) ||
                       File.Exists(HP_WMI_FAN2_OUTPUT) ||
                       ResolveHwmonFanTargetPath(1) != null ||
                       ResolveHwmonFanTargetPath(2) != null;
        var hasHpWmiThermalProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.HpWmiThermalProfilePaths);
        var hasHpWmiPlatformProfilePath = LinuxSysfsPathMap.AnyPathExists(LinuxSysfsPathMap.PlatformProfilePaths);

        var capabilityAssessment = LinuxCapabilityClassifier.Assess(
            CheckRootAccess(),
            File.Exists(LinuxSysfsPathMap.EcIoPath),
            Directory.Exists(HP_WMI_PATH),
            hasHpWmiThermalProfilePath,
            hasHpWmiPlatformProfilePath,
            File.Exists(LinuxSysfsPathMap.AcpiPlatformProfilePath),
            File.Exists(HP_WMI_FAN1_OUTPUT),
            File.Exists(HP_WMI_FAN2_OUTPUT),
            ResolveHwmonFanTargetPath(1) != null,
            ResolveHwmonFanTargetPath(2) != null,
            ResolveHwmonPwmEnablePath(1) != null || ResolveHwmonPwmEnablePath(2) != null,
            Directory.Exists(HWMON_BASE) || Directory.Exists(HP_WMI_PATH),
            IsUnsafeEcModel(),
            await ReadDmiStringAsync("product_name"),
            await ReadDmiStringAsync("board_name"));
        _capabilities.SupportsFanControl = capabilityAssessment.SupportsManualFanControl;
        _capabilities.SupportsFanSurface = capabilityAssessment.SupportsManualFanControl || capabilityAssessment.SupportsProfileControl || capabilityAssessment.SupportsTelemetry;
        _capabilities.SupportsPerformanceProfiles = capabilityAssessment.SupportsProfileControl || ResolvePowerProfilesCtlPath() != null;
        _capabilities.PerformanceProfileReason = _capabilities.SupportsPerformanceProfiles
            ? string.Empty
            : "No writable platform_profile/thermal_profile interface was detected.";
        _capabilities.FanControlCapabilityClass = capabilityAssessment.CapabilityKey;
        _capabilities.FanControlCapabilityReason = capabilityAssessment.Reason;
        
        // Check keyboard backlight and writable brightness endpoint. Resolves either the
        // "hp::kbd_backlight" or "hp_omen::kbd_backlight" LED class - see ResolveBacklightDirectory.
        var backlightDir = ResolveBacklightDirectory();
        _capabilities.HasKeyboardBacklight = backlightDir != null;
        var keyboardBrightnessPath = Path.Combine(backlightDir ?? LinuxSysfsPathMap.KeyboardBacklightPath, "brightness");
        _capabilities.SupportsKeyboardBrightness = _capabilities.HasKeyboardBacklight && File.Exists(keyboardBrightnessPath);
        _capabilities.KeyboardBrightnessReason = _capabilities.SupportsKeyboardBrightness
            ? string.Empty
            : "Keyboard brightness sysfs path was not detected on this kernel/board.";

        // Detect RGB capabilities from common HP OMEN LED interfaces.
        _capabilities.HasFourZoneRgb = DetectFourZoneRgbSupport();
        _capabilities.HasPerKeyRgb = DetectPerKeyRgbSupport();
        
        // Check for discrete GPU
        _capabilities.HasDiscreteGpu = await HasDiscreteGpuAsync();
        
        // Read model name from DMI
        _capabilities.ModelName = await ReadDmiStringAsync("product_name") ?? "Unknown HP OMEN";
        
        // Read CPU name from /proc/cpuinfo
        _capabilities.CpuName = await ReadCpuNameAsync();
        
        // Read GPU name
        _capabilities.GpuName = await ReadGpuNameAsync();

        return _capabilities;
    }

    private static bool DetectFourZoneRgbSupport()
    {
        if (File.Exists("/sys/class/leds/hp::kbd_backlight/color") ||
            File.Exists("/sys/class/leds/hp::kbd_backlight/multi_intensity"))
        {
            return true;
        }

        // Additional 4-zone backends seen on some kernel/driver combinations, not covered by
        // the "hp::kbd_backlight" checks above or the generic LED-name scan below (the
        // "hp_omen::kbd_backlight" LED class name doesn't contain "zone"/"multicolor", and
        // its zone_colors file isn't multi_intensity/multi_index). Source: openomen (GPLv3) -
        // documented sysfs paths, reimplemented independently; see CHANGELOG_v4.1.7.md.
        if (LinuxSysfsPathMap.HasRgbZonesDir ||
            LinuxSysfsPathMap.HasKeyboardLedsFile ||
            File.Exists(Path.Combine(LinuxSysfsPathMap.KeyboardBacklightPathAlt, "zone_colors")))
        {
            return true;
        }

        try
        {
            if (!Directory.Exists("/sys/class/leds"))
            {
                return false;
            }

            foreach (var ledPath in Directory.EnumerateDirectories("/sys/class/leds", "*", SearchOption.TopDirectoryOnly))
            {
                var ledName = Path.GetFileName(ledPath);
                if (string.IsNullOrWhiteSpace(ledName))
                {
                    continue;
                }

                if (ledName.Contains("zone", StringComparison.OrdinalIgnoreCase) ||
                    ledName.Contains("multicolor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (File.Exists(Path.Combine(ledPath, "multi_intensity")) ||
                    File.Exists(Path.Combine(ledPath, "multi_index")))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectPerKeyRgbSupport()
    {
        try
        {
            if (!Directory.Exists("/sys/class/leds"))
            {
                return false;
            }

            return Directory.EnumerateDirectories("/sys/class/leds", "hp::*key*", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateDirectories("/sys/class/leds", "*kbd*perkey*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Find the first existing thermal profile sysfs path.
    /// </summary>
    private static string? ResolveThermalProfilePath()
    {
        return LinuxSysfsPathMap.ResolveThermalProfilePath();
    }

    private static string? ResolveHwmonPwmEnablePath(int index)
    {
        return LinuxSysfsPathMap.ResolveHpWmiPwmEnablePath(index);
    }

    private static bool CheckRootAccess()
    {
        try
        {
            return geteuid() == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnsafeEcModel()
    {
        try
        {
            var modelName = ReadDmiString("product_name");
            var boardName = ReadDmiString("board_name");
            if (modelName?.Contains("transcend 14", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (boardName != null &&
                (string.Equals(boardName, "8C58", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(boardName, "8E41", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string? ReadDmiString(string name)
    {
        var paths = new[]
        {
            $"/sys/devices/virtual/dmi/id/{name}",
            $"/sys/class/dmi/id/{name}"
        };

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    /// <summary>
    /// Convert kernel platform_profile string to OmenCore PerformanceMode.
    /// The kernel interface uses: "low-power", "cool", "quiet", "balanced", "balanced-performance", "performance".
    /// </summary>
    private static PerformanceMode ParsePlatformProfile(string profile)
    {
        return profile.Trim().ToLower() switch
        {
            "low-power" or "cool" or "quiet" => PerformanceMode.Quiet,
            "balanced" => PerformanceMode.Balanced,
            "balanced-performance" => PerformanceMode.Performance,
            "performance" => PerformanceMode.Performance,
            _ => PerformanceMode.Balanced
        };
    }

    /// <summary>
    /// Convert OmenCore PerformanceMode to kernel platform_profile string.
    /// Uses the standard kernel values from /sys/firmware/acpi/platform_profile_choices.
    /// </summary>
    private async Task<string> GetKernelProfileStringAsync(PerformanceMode mode)
    {
        // Read available choices from the kernel to use exact supported values
        var choices = await ReadAvailableProfileChoicesAsync();
        
        return mode switch
        {
            PerformanceMode.Quiet => 
                choices.Contains("low-power") ? "low-power" :
                choices.Contains("quiet") ? "quiet" :
                choices.Contains("cool") ? "cool" : "low-power",
            PerformanceMode.Balanced => "balanced",
            PerformanceMode.Performance =>
                choices.Contains("performance") ? "performance" :
                choices.Contains("balanced-performance") ? "balanced-performance" :
                "performance",
            _ => "balanced"
        };
    }

    /// <summary>
    /// Read available profile choices from the kernel.
    /// </summary>
    private async Task<HashSet<string>> ReadAvailableProfileChoicesAsync()
    {
        var choices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var choicesPath in LinuxSysfsPathMap.ThermalProfileChoicePaths)
            {
                if (!File.Exists(choicesPath))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(choicesPath);
                foreach (var choice in content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    choices.Add(choice.Trim());

                if (choices.Count > 0)
                {
                    break;
                }
            }
        }
        catch { }
        
        // Fallback defaults if we couldn't read choices
        if (choices.Count == 0)
        {
            choices.Add("low-power");
            choices.Add("balanced");
            choices.Add("performance");
        }
        
        return choices;
    }

    public async Task<PerformanceMode> GetPerformanceModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PerformanceMode.Balanced;

        try
        {
            var thermalPath = _resolvedThermalPath ?? ResolveThermalProfilePath();
            if (thermalPath != null)
            {
                var profile = await File.ReadAllTextAsync(thermalPath);
                return ParsePlatformProfile(profile);
            }
        }
        catch { }

        var fallbackMode = await TryGetPowerProfilesCtlModeAsync();
        if (fallbackMode.HasValue)
        {
            return fallbackMode.Value;
        }

        return PerformanceMode.Balanced;
    }

    public async Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(() => SetPerformanceModeCoreAsync(mode));
    }

    private async Task SetPerformanceModeCoreAsync(PerformanceMode mode)
    {
        var thermalPath = _resolvedThermalPath ?? ResolveThermalProfilePath();
        if (thermalPath == null)
        {
            if (await TrySetPerformanceModeViaPowerProfilesCtlAsync(mode))
            {
                return;
            }

            var boardId = await ReadFirstExistingTextAsync(new[]
            {
                "/sys/class/dmi/id/board_name",
                "/sys/devices/virtual/dmi/id/board_name"
            }) ?? "unknown";

            throw new InvalidOperationException(
                $"No thermal profile interface found (board {boardId}). If hp-wmi is loaded but platform_profile/thermal_profile are missing, run 'omencore-cli diagnose --report' to capture model-specific sysfs capabilities.");
        }

        var profile = await GetKernelProfileStringAsync(mode);

        // Strategy 1: Direct sysfs write (works when running as root)
        try
        {
            // Use File.Open with FileMode.Open to avoid Create semantics that sysfs rejects
            await using var fs = new FileStream(thermalPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            var bytes = System.Text.Encoding.UTF8.GetBytes(profile);
            await fs.WriteAsync(bytes);
            return; // Success
        }
        catch (UnauthorizedAccessException)
        {
            // Not running with permission to write this sysfs control.
        }
        catch (IOException)
        {
            // sysfs write failed.
        }

        if (await TrySetPerformanceModeViaPowerProfilesCtlAsync(mode))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not write to {thermalPath}. Start OmenCore with the required permissions or configure a distro policy rule for this sysfs path.");
    }

    private static string? ResolvePowerProfilesCtlPath()
    {
        foreach (var candidate in PowerProfilesCtlCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static PerformanceMode ParsePowerProfilesCtlMode(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "power-saver" => PerformanceMode.Quiet,
            "performance" => PerformanceMode.Performance,
            _ => PerformanceMode.Balanced
        };
    }

    private static string ToPowerProfilesCtlMode(PerformanceMode mode)
    {
        return mode switch
        {
            PerformanceMode.Quiet => "power-saver",
            PerformanceMode.Performance => "performance",
            _ => "balanced"
        };
    }

    private static async Task<(int exitCode, string stdout)> RunPowerProfilesCtlAsync(string arguments)
    {
        var binary = ResolvePowerProfilesCtlPath();
        if (binary == null)
        {
            return (-1, string.Empty);
        }

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return (-1, string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, waitTask);

            return (process.ExitCode, (await stdoutTask).Trim());
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    private static async Task<PerformanceMode?> TryGetPowerProfilesCtlModeAsync()
    {
        var (exitCode, stdout) = await RunPowerProfilesCtlAsync("get");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        return ParsePowerProfilesCtlMode(stdout);
    }

    private static async Task<bool> TrySetPerformanceModeViaPowerProfilesCtlAsync(PerformanceMode mode)
    {
        var profile = ToPowerProfilesCtlMode(mode);
        var (exitCode, _) = await RunPowerProfilesCtlAsync($"set {profile}");
        return exitCode == 0;
    }

    public async Task SetCpuFanSpeedAsync(int percentage)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            int clamped = Math.Clamp(percentage, 0, 100);

            await TryEnableManualFanOverrideAsync();

            // Prefer direct hp-wmi fan output when exposed by kernel/firmware.
            try
            {
                if (File.Exists(HP_WMI_FAN1_OUTPUT))
                {
                    await File.WriteAllTextAsync(HP_WMI_FAN1_OUTPUT, clamped.ToString());
                    return;
                }

                var fanTargetPath = ResolveHwmonFanTargetPath(1);
                if (fanTargetPath != null)
                {
                    await File.WriteAllTextAsync(fanTargetPath, clamped.ToString());
                    return;
                }
            }
            catch
            {
                // Fall through to profile-based fallback.
            }

            // Boards that expose only hwmon pwm1_enable (no writable duty or fan-target file,
            // and often no thermal_profile either - see GitHub #174) have exactly one real fan
            // control surface: the coarse full-speed/auto toggle. This mirrors the CLI's already
            // -working LinuxEcController.SetFanProfileViaAcpiHwmon behavior for the same board
            // class, which the GUI previously never reached - it fell straight through to the
            // platform-profile fallback below, which is equally absent on these boards, so no
            // fan request had any effect at all.
            if (TryWriteHwmonPwmEnable(1, clamped >= 100 ? 0 : 2))
            {
                return;
            }

            // Fallback for hp_wmi-only boards that expose only thermal_profile:
            // approximate requested fan intensity by switching platform performance profile.
            var mode = clamped switch
            {
                <= 35 => PerformanceMode.Quiet,
                <= 70 => PerformanceMode.Balanced,
                _ => PerformanceMode.Performance
            };

            await ApplyFanFallbackProfileAsync(mode);
        });
    }

    public async Task SetGpuFanSpeedAsync(int percentage)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            int clamped = Math.Clamp(percentage, 0, 100);

            await TryEnableManualFanOverrideAsync();

            try
            {
                if (File.Exists(HP_WMI_FAN2_OUTPUT))
                {
                    await File.WriteAllTextAsync(HP_WMI_FAN2_OUTPUT, clamped.ToString());
                    return;
                }

                var fanTargetPath = ResolveHwmonFanTargetPath(2);
                if (fanTargetPath != null)
                {
                    await File.WriteAllTextAsync(fanTargetPath, clamped.ToString());
                    return;
                }
            }
            catch
            {
                // Fall through to profile-based fallback.
            }

            // See the matching comment in SetCpuFanSpeedAsync - same coarse pwm_enable fallback,
            // fan index 2. pwm1_enable/pwm2_enable are driven together by SetHwmonPwmEnable in
            // the CLI too (LinuxEcController.cs:975-979), so this mirrors that pairing.
            if (TryWriteHwmonPwmEnable(2, clamped >= 100 ? 0 : 2))
            {
                return;
            }

            var mode = clamped switch
            {
                <= 35 => PerformanceMode.Quiet,
                <= 70 => PerformanceMode.Balanced,
                _ => PerformanceMode.Performance
            };

            await ApplyFanFallbackProfileAsync(mode);
        });
    }

    private async Task ApplyFanFallbackProfileAsync(PerformanceMode mode)
    {
        if (_lastFanFallbackMode == mode)
            return;

        try
        {
            await SetPerformanceModeCoreAsync(mode);
            _lastFanFallbackMode = mode;
        }
        catch
        {
            // No profile interface available on this model/kernel. Keep best-effort behavior.
        }
    }

    public async Task<string> GetGpuModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "hybrid";

        var gpuVendors = await ReadDrmGpuVendorsAsync();
        var hasDiscrete = gpuVendors.Any(v => IsDiscreteGpuVendor(v.vendorId));
        var hasIntegrated = gpuVendors.Any(v => IsIntegratedGpuVendor(v.vendorId));

        if (hasDiscrete && hasIntegrated)
        {
            return "hybrid";
        }

        return hasDiscrete ? "discrete" : "integrated";
    }

    public async Task SetGpuModeAsync(string mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await Task.CompletedTask;
        throw new NotSupportedException("GPU mode switching is distro-specific and is not invoked through external tools by OmenCore. Use BIOS or your distro's GPU profile manager.");
    }

    public async Task SetKeyboardBrightnessAsync(int brightness)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            var backlightDir = ResolveBacklightDirectory() ?? LinuxSysfsPathMap.KeyboardBacklightPath;
            var maxPath = Path.Combine(backlightDir, "max_brightness");
            var brightnessPath = Path.Combine(backlightDir, "brightness");

            if (!File.Exists(brightnessPath))
            {
                throw new NotSupportedException("Keyboard brightness control is not exposed by this Linux kernel/board path.");
            }

            try
            {
                var maxBrightness = File.Exists(maxPath)
                    ? int.Parse(await File.ReadAllTextAsync(maxPath))
                    : 3;
                var scaledBrightness = (int)(brightness / 100.0 * maxBrightness);
                await File.WriteAllTextAsync(brightnessPath, scaledBrightness.ToString());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set keyboard brightness: {ex.Message}");
            }
        });
    }

    public async Task SetKeyboardColorAsync(byte r, byte g, byte b)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        await ExecuteWithIoLockAsync(async () =>
        {
            var backlightDir = ResolveBacklightDirectory();
            var multiIntensityPath = backlightDir != null ? Path.Combine(backlightDir, "multi_intensity") : null;
            var colorPath = backlightDir != null ? Path.Combine(backlightDir, "color") : null;

            try
            {
                // Preferred for hp-wmi multicolor interface (used by custom driver): "R G B"
                if (multiIntensityPath != null && File.Exists(multiIntensityPath))
                {
                    var rgbSpace = $"{r} {g} {b}";
                    await File.WriteAllTextAsync(multiIntensityPath, rgbSpace);
                    return;
                }

                // Legacy fallback used by some keyboard backlight drivers.
                if (colorPath != null && File.Exists(colorPath))
                {
                    var colorValue = $"{r:X2}{g:X2}{b:X2}";
                    await File.WriteAllTextAsync(colorPath, colorValue);
                    return;
                }

                // Additional 4-zone backends not addressed by the "hp::kbd_backlight" LED class
                // above. A single requested color is applied to all 4 zones, since none of these
                // interfaces are addressed independently by this method's single-RGB signature.
                // Source: openomen (GPLv3) - documented sysfs paths/wire formats, reimplemented
                // independently; see CHANGELOG_v4.1.7.md for the corroboration trail.
                if (TryWriteAllRgbZoneFiles(r, g, b) ||
                    TryWriteKeyboardLedsFile(r, g, b) ||
                    TryWriteZoneColorsBinary(r, g, b))
                {
                    return;
                }

                throw new NotSupportedException("No writable keyboard RGB interface was detected on this kernel/board.");
            }
            catch (Exception ex) when (ex is not NotSupportedException)
            {
                throw new InvalidOperationException($"Failed to set keyboard color: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Writes the same color to each existing "zoneNN" file under <see cref="LinuxSysfsPathMap.HpWmiRgbZonesDir"/>,
    /// as a plain 6-hex-char string (no "#"). Returns false (rather than throwing) when the
    /// directory or none of its zone files exist, so callers can fall through to the next backend.
    /// </summary>
    private static bool TryWriteAllRgbZoneFiles(byte r, byte g, byte b)
    {
        if (!LinuxSysfsPathMap.HasRgbZonesDir)
        {
            return false;
        }

        var colorValue = $"{r:X2}{g:X2}{b:X2}";
        var wroteAny = false;
        for (var zone = 0; zone < 4; zone++)
        {
            var zonePath = LinuxSysfsPathMap.ResolveRgbZoneFilePath(zone);
            if (zonePath == null)
            {
                continue;
            }

            File.WriteAllText(zonePath, colorValue);
            wroteAny = true;
        }

        return wroteAny;
    }

    /// <summary>
    /// Writes all 4 zones' colors concatenated into a single 24-char hex string to
    /// <see cref="LinuxSysfsPathMap.HpWmiKeyboardLedsPath"/> (the legacy single-file 4-zone
    /// interface). Returns false when the file doesn't exist.
    /// </summary>
    private static bool TryWriteKeyboardLedsFile(byte r, byte g, byte b)
    {
        if (!LinuxSysfsPathMap.HasKeyboardLedsFile)
        {
            return false;
        }

        var colorValue = $"{r:X2}{g:X2}{b:X2}";
        File.WriteAllText(LinuxSysfsPathMap.HpWmiKeyboardLedsPath, colorValue + colorValue + colorValue + colorValue);
        return true;
    }

    /// <summary>
    /// Writes a 12-byte binary payload (4 zones x RGB) to the "hp_omen::kbd_backlight/zone_colors"
    /// LED class file. Returns false when that file doesn't exist.
    /// </summary>
    private static bool TryWriteZoneColorsBinary(byte r, byte g, byte b)
    {
        var path = Path.Combine(LinuxSysfsPathMap.KeyboardBacklightPathAlt, "zone_colors");
        if (!File.Exists(path))
        {
            return false;
        }

        var binaryData = new byte[12];
        for (var zone = 0; zone < 4; zone++)
        {
            binaryData[zone * 3] = r;
            binaryData[zone * 3 + 1] = g;
            binaryData[zone * 3 + 2] = b;
        }

        File.WriteAllBytes(path, binaryData);
        return true;
    }

    private static string? ResolveBacklightDirectory() => LinuxSysfsPathMap.ResolveKeyboardBacklightDirectory();

    #region Private Helpers

    private async Task ExecuteWithIoLockAsync(Func<Task> operation)
    {
        await _ioLock.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<T> ExecuteWithIoLockAsync<T>(Func<Task<T>> operation)
    {
        await _ioLock.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Write hwmon pwm{fanIndex}_enable, the coarse full-speed(0)/auto(2) toggle that is the
    /// only writable fan control surface on boards with no duty/target file (GitHub #174).
    /// Mirrors LinuxEcController.SetHwmonPwmEnable's safety block: value 3 (fan off) is never
    /// allowed through this path. Returns false (not an exception) when the path doesn't exist,
    /// so callers can fall through to their next-best option.
    /// </summary>
    private static bool TryWriteHwmonPwmEnable(int fanIndex, int value)
    {
        if (value == 3)
        {
            return false;
        }

        var path = LinuxSysfsPathMap.ResolveHpWmiPwmEnablePath(fanIndex);
        if (path == null)
        {
            return false;
        }

        try
        {
            File.WriteAllText(path, value.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryEnableManualFanOverrideAsync()
    {
        try
        {
            if (File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                await File.WriteAllTextAsync(HP_WMI_FAN_ALWAYS_ON, "1");
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static async Task<string?> ReadFirstExistingTextAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return (await File.ReadAllTextAsync(path)).Trim();
                }
            }
            catch
            {
                // Ignore and continue to next candidate.
            }
        }

        return null;
    }

    private static string? ResolveHwmonFanTargetPath(int fanIndex)
    {
        try
        {
            foreach (var hwmonDir in LinuxSysfsPathMap.EnumerateHpWmiHwmonDirectories())
            {
                var targetPath = Path.Combine(hwmonDir, $"fan{fanIndex}_target");
                if (File.Exists(targetPath))
                    return targetPath;
            }
        }
        catch
        {
            // Best-effort resolution.
        }

        return null;
    }

    private static async Task<int> ReadTemperatureAsync(string type)
    {
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                try
                {
                    var namePath = Path.Combine(hwmon, "name");
                    if (!File.Exists(namePath)) continue;
                    
                    var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                    
                    // Match multiple CPU temperature sensor names
                    // Intel: coretemp, AMD: k10temp, zenpower, amd_energy
                    bool isCpuSensor = type == "coretemp" && 
                        (name == "coretemp" || name == "k10temp" || name == "zenpower" || 
                         name == "amd_energy" || name.Contains("cpu") || name.Contains("tctl"));
                    
                    if (isCpuSensor || name == type)
                    {
                        // Try different temperature input patterns
                        var tempFiles = new[] { "temp1_input", "temp2_input", "temp3_input", "Tctl" };
                        foreach (var tempFile in tempFiles)
                        {
                            var tempPath = Path.Combine(hwmon, tempFile);
                            if (File.Exists(tempPath))
                            {
                                var tempStr = await File.ReadAllTextAsync(tempPath);
                                if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                                    return temp;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        
        // Fallback: Try to find any temperature sensor
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                var tempFiles = Directory.GetFiles(hwmon, "temp*_input");
                foreach (var tempFile in tempFiles)
                {
                    var tempStr = await File.ReadAllTextAsync(tempFile);
                    if (int.TryParse(tempStr.Trim(), out var temp) && temp > 1000 && temp < 150000)
                        return temp; // Valid temperature in millidegrees
                }
            }
        }
        catch { }
        
        return 0;
    }

    private static async Task<int> ReadGpuTemperatureAsync()
    {
        foreach (var hwmon in SafeEnumerateDirectories(HWMON_BASE))
        {
            try
            {
                var namePath = Path.Combine(hwmon, "name");
                if (!File.Exists(namePath)) continue;
                
                var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                if (name == "amdgpu" ||
                    name == "nouveau" ||
                    name.Contains("nvidia") ||
                    name.Contains("radeon") ||
                    name.Contains("gpu") ||
                    name.Contains("i915") ||
                    name.Contains("intel"))
                {
                    var tempFiles = new[] { "temp1_input", "temp2_input", "temp3_input", "edge", "junction" };
                    foreach (var tempFile in tempFiles)
                    {
                        var tempPath = Path.Combine(hwmon, tempFile);
                        if (File.Exists(tempPath))
                        {
                            var tempStr = await File.ReadAllTextAsync(tempPath);
                            if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                                return temp;
                        }
                    }
                }
            }
            catch { }
        }

        return 0;
    }

    private static async Task<int> ReadFanRpmAsync(string type)
    {
        try
        {
            foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
            {
                try
                {
                    // Check if this hwmon is related to the requested type
                    var namePath = Path.Combine(hwmon, "name");
                    var deviceName = File.Exists(namePath) ? 
                        (await File.ReadAllTextAsync(namePath)).Trim().ToLower() : "";
                    
                    // Look for fan speed inputs
                    var fanInputs = Directory.GetFiles(hwmon, "fan*_input");
                    if (fanInputs.Length == 0)
                    {
                        // Some HP laptops expose fans via PWM files
                        fanInputs = Directory.GetFiles(hwmon, "pwm*");
                    }
                    
                    foreach (var fanInput in fanInputs)
                    {
                        var rpm = await File.ReadAllTextAsync(fanInput);
                        if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                            return rpmValue;
                    }
                }
                catch { }
            }
            
            // Try HP-specific fan paths
            var hpFanPaths = new[]
            {
                "/sys/devices/platform/hp-wmi/fan1_input",
                "/sys/devices/platform/hp-wmi/fan2_input",
                "/sys/class/hwmon/hwmon*/fan1_input"
            };
            
            foreach (var fanPath in hpFanPaths)
            {
                if (fanPath.Contains("*"))
                {
                    var matches = Directory.GetFiles(Path.GetDirectoryName(fanPath) ?? "", Path.GetFileName(fanPath));
                    foreach (var match in matches)
                    {
                        if (File.Exists(match))
                        {
                            var rpm = await File.ReadAllTextAsync(match);
                            if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                                return rpmValue;
                        }
                    }
                }
                else if (File.Exists(fanPath))
                {
                    var rpm = await File.ReadAllTextAsync(fanPath);
                    if (int.TryParse(rpm.Trim(), out var rpmValue) && rpmValue > 0)
                        return rpmValue;
                }
            }
        }
        catch { }
        return 0;
    }

    private static async Task<double> ReadCpuUsageAsync()
    {
        try
        {
            var stat1 = await File.ReadAllLinesAsync("/proc/stat");
            await Task.Delay(100);
            var stat2 = await File.ReadAllLinesAsync("/proc/stat");

            var cpu1 = ParseCpuLine(stat1[0]);
            var cpu2 = ParseCpuLine(stat2[0]);

            var total1 = cpu1.Sum();
            var total2 = cpu2.Sum();
            var idle1 = cpu1[3];
            var idle2 = cpu2[3];

            var totalDiff = total2 - total1;
            var idleDiff = idle2 - idle1;

            return totalDiff > 0 ? (1.0 - (double)idleDiff / totalDiff) * 100 : 0;
        }
        catch { }
        return 0;
    }

    private static long[] ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Select(long.Parse).ToArray();
    }

    private static async Task<(double percentage, double usedGb, double totalGb)> ReadMemoryUsageAsync()
    {
        try
        {
            var meminfo = await File.ReadAllLinesAsync("/proc/meminfo");
            long total = 0, available = 0;

            foreach (var line in meminfo)
            {
                if (line.StartsWith("MemTotal:"))
                    total = long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                else if (line.StartsWith("MemAvailable:"))
                    available = long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            }

            if (total > 0)
            {
                var totalGb = total / 1024.0 / 1024.0; // Convert KB to GB
                var usedGb = (total - available) / 1024.0 / 1024.0;
                var percentage = (1.0 - (double)available / total) * 100;
                return (percentage, usedGb, totalGb);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    private static async Task<(int percentage, bool onBattery)> ReadBatteryStatusAsync()
    {
        try
        {
            var batteries = Directory.GetDirectories(POWER_SUPPLY);
            foreach (var battery in batteries)
            {
                var type = await File.ReadAllTextAsync(Path.Combine(battery, "type"));
                if (type.Trim() == "Battery")
                {
                    var capacity = await File.ReadAllTextAsync(Path.Combine(battery, "capacity"));
                    var status = await File.ReadAllTextAsync(Path.Combine(battery, "status"));
                    var onBattery = status.Trim() == "Discharging";
                    return (int.Parse(capacity.Trim()), onBattery);
                }
            }
        }
        catch { }
        return (100, false);
    }

    private static async Task<bool> HasDiscreteGpuAsync()
    {
        var gpuVendors = await ReadDrmGpuVendorsAsync();
        return gpuVendors.Any(v => IsDiscreteGpuVendor(v.vendorId));
    }

    private static async Task<string?> ReadDmiStringAsync(string field)
    {
        var path = $"/sys/class/dmi/id/{field}";
        try
        {
            if (File.Exists(path))
                return (await File.ReadAllTextAsync(path)).Trim();
        }
        catch { }
        return null;
    }

    private static async Task<string> ReadCpuNameAsync()
    {
        try
        {
            var cpuinfo = await File.ReadAllLinesAsync("/proc/cpuinfo");
            var modelLine = cpuinfo.FirstOrDefault(l => l.StartsWith("model name"));
            if (modelLine != null)
            {
                return modelLine.Split(':')[1].Trim();
            }
        }
        catch { }
        return "Unknown CPU";
    }

    private static async Task<string> ReadGpuNameAsync()
    {
        // GitHub #186: the raw-PCI-ID fallback below ("NVIDIA GPU (0x2c59)") was the only name this
        // ever produced for a real RTX 5080 - NVML reports the actual product name
        // ("NVIDIA GeForce RTX 5080 Laptop GPU") directly, so prefer it when available.
        var nvmlGpu = NvmlInterop.TryGetPrimaryGpu();
        if (!string.IsNullOrWhiteSpace(nvmlGpu?.Name))
        {
            return nvmlGpu.Name;
        }

        var gpuVendors = await ReadDrmGpuVendorsAsync();
        var selected = gpuVendors.FirstOrDefault(v => IsDiscreteGpuVendor(v.vendorId));
        if (string.IsNullOrWhiteSpace(selected.vendorId))
        {
            selected = gpuVendors.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(selected.vendorId))
        {
            return FormatGpuName(selected.vendorId, selected.deviceId);
        }

        return "Unknown GPU";
    }

    private static async Task<IReadOnlyList<(string vendorId, string deviceId)>> ReadDrmGpuVendorsAsync()
    {
        var result = new List<(string vendorId, string deviceId)>();
        const string drmPath = "/sys/class/drm";

        foreach (var card in SafeEnumerateDirectories(drmPath))
        {
            var cardName = Path.GetFileName(card);
            if (string.IsNullOrWhiteSpace(cardName) ||
                !cardName.StartsWith("card", StringComparison.Ordinal) ||
                cardName.Contains("-", StringComparison.Ordinal))
            {
                continue;
            }

            var devicePath = Path.Combine(card, "device");
            var vendorPath = Path.Combine(devicePath, "vendor");
            if (!File.Exists(vendorPath))
            {
                continue;
            }

            try
            {
                var vendor = (await File.ReadAllTextAsync(vendorPath)).Trim().ToLowerInvariant();
                var deviceFile = Path.Combine(devicePath, "device");
                var device = File.Exists(deviceFile)
                    ? (await File.ReadAllTextAsync(deviceFile)).Trim().ToLowerInvariant()
                    : string.Empty;

                result.Add((vendor, device));
            }
            catch
            {
                // Best-effort sysfs probing.
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).ToArray()
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsDiscreteGpuVendor(string vendorId)
    {
        return string.Equals(vendorId, "0x10de", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(vendorId, "0x1002", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntegratedGpuVendor(string vendorId)
    {
        return string.Equals(vendorId, "0x8086", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGpuName(string vendorId, string deviceId)
    {
        var suffix = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : $" ({deviceId})";
        return vendorId.ToLowerInvariant() switch
        {
            "0x10de" => $"NVIDIA GPU{suffix}",
            "0x1002" => $"AMD Radeon GPU{suffix}",
            "0x8086" => $"Intel Integrated Graphics{suffix}",
            _ => $"GPU {vendorId}{suffix}"
        };
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _pollingTimer.Stop();
            _pollingTimer.Dispose();
            _ioLock.Dispose();
            _disposed = true;
        }
    }
}
