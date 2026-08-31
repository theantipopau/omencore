namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux EC (Embedded Controller) interface for fan and performance control.
/// 
/// Access via /sys/kernel/debug/ec/ec0/io requires:
///   1. Root privileges
///   2. ec_sys kernel module loaded with write_support=1:
///      sudo modprobe ec_sys write_support=1
/// 
/// Alternatively uses hp-wmi driver if available (newer models):
///   sudo modprobe hp-wmi
/// 
/// Register map based on omen-fan project:
/// https://github.com/alou-S/omen-fan/blob/main/docs/probes.md
/// </summary>
public class LinuxEcController
{
    private static readonly object EcIoLock = new();

    // EC sysfs path
    private const string EC_PATH = "/sys/kernel/debug/ec/ec0/io";
    
    // HP-WMI paths (for newer models like OMEN 16 2023+)
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
    private const string HP_WMI_THERMAL = "/sys/devices/platform/hp-wmi/thermal_profile";
    private const string HP_WMI_THERMAL_ALT = "/sys/devices/platform/hp-wmi/thermal-profile";
    private const string HP_WMI_FAN_ALWAYS_ON = "/sys/devices/platform/hp-wmi/fan_always_on";
    private const string HP_WMI_FAN1 = "/sys/devices/platform/hp-wmi/fan1_output";
    private const string HP_WMI_FAN2 = "/sys/devices/platform/hp-wmi/fan2_output";
    
    // ACPI Platform Profile (kernel 5.18+, used by 2025+ OMEN models)
    private const string ACPI_PLATFORM_PROFILE = "/sys/firmware/acpi/platform_profile";
    private const string ACPI_PLATFORM_PROFILE_CHOICES = "/sys/firmware/acpi/platform_profile_choices";
    private const string HP_WMI_THERMAL_CHOICES = "/sys/devices/platform/hp-wmi/thermal_profile_choices";
    private const string HP_WMI_THERMAL_CHOICES_ALT = "/sys/devices/platform/hp-wmi/thermal-profile-choices";
    private const string HP_WMI_PLATFORM_PROFILE = "/sys/devices/platform/hp-wmi/platform_profile";
    private const string HP_WMI_PLATFORM_PROFILE_ALT = "/sys/devices/platform/hp-wmi/platform-profile";
    private const string HP_WMI_PLATFORM_PROFILE_CHOICES = "/sys/devices/platform/hp-wmi/platform_profile_choices";
    private const string HP_WMI_PLATFORM_PROFILE_CHOICES_ALT = "/sys/devices/platform/hp-wmi/platform-profile-choices";
    private const string HP_WMI_PERFORMANCE_PROFILE = "/sys/devices/platform/hp-wmi/performance_profile";
    private const string HP_WMI_PERFORMANCE_PROFILE_ALT = "/sys/devices/platform/hp-wmi/performance-profile";
    private const string HP_WMI_PERFORMANCE_PROFILE_CHOICES = "/sys/devices/platform/hp-wmi/performance_profile_choices";
    private const string HP_WMI_PERFORMANCE_PROFILE_CHOICES_ALT = "/sys/devices/platform/hp-wmi/performance-profile-choices";
    
    // HP-WMI hwmon paths (2025+ models use standard hwmon interface for fan control)
    // Discovered at runtime since hwmon number varies
    private string? _hwmonPwm1EnablePath;
    private string? _hwmonPwm2EnablePath;
    private string? _hwmonPwm1Path;
    private string? _hwmonPwm2Path;
    private string? _hwmonFan1InputPath;
    private string? _hwmonFan2InputPath;
    
    // DMI paths for model detection
    private const string DMI_PRODUCT_NAME = "/sys/class/dmi/id/product_name";
    private const string DMI_PRODUCT_NAME_ALT = "/sys/devices/virtual/dmi/id/product_name";
    private const string DMI_BOARD_NAME = "/sys/class/dmi/id/board_name";
    private const string DMI_BOARD_NAME_ALT = "/sys/devices/virtual/dmi/id/board_name";
    
    // EC Register addresses (from omen-fan - older models OMEN 15 2020, etc.)
    // WARNING: These registers are ONLY valid for pre-2025 OMEN models!
    // 2025+ models (OMEN Max 16t, etc.) have a completely different EC register layout.
    // Writing to these registers on 2025+ models WILL cause EC panic (caps lock blinking).
    private const byte REG_FAN1_SPEED_SET = 0x34;      // Fan 1 speed in units of 100 RPM
    private const byte REG_FAN2_SPEED_SET = 0x35;      // Fan 2 speed in units of 100 RPM
    private const byte REG_FAN1_SPEED_PCT = 0x2E;      // Fan 1 speed 0-100%
    private const byte REG_FAN2_SPEED_PCT = 0x2F;      // Fan 2 speed 0-100%
    private const byte REG_FAN_BOOST = 0xEC;           // Fan boost: 0x00=OFF, 0x0C=ON
    private const byte REG_FAN_STATE = 0xF4;           // Fan state: 0x00=Enable, 0x02=Disable
    private const byte REG_CPU_TEMP = 0x57;            // CPU temperature
    private const byte REG_GPU_TEMP = 0xB7;            // GPU temperature
    private const byte REG_BIOS_CONTROL = 0x62;        // BIOS control: 0x00=Enabled, 0x06=Disabled
    private const byte REG_TIMER = 0x63;               // Timer (counts down from 0x78)
    private const byte REG_PERF_MODE = 0x95;           // Performance mode
    private const byte REG_THERMAL_POWER = 0xBA;       // Thermal power limit (0-5)
    
    // Performance mode values
    private const byte PERF_MODE_DEFAULT = 0x30;
    private const byte PERF_MODE_PERFORMANCE = 0x31;
    private const byte PERF_MODE_COOL = 0x50;
    
    // Model patterns where direct EC access is UNSAFE (different register layout)
    // These models cause EC panic (caps lock blinking) when legacy EC registers are written
    private static readonly string[] UnsafeEcModelPatterns = new[]
    {
        "16t-ah0",    // OMEN MAX Gaming Laptop 16t-ah000 (2025, Intel Core Ultra 7/9)
        "16-ah0",     // OMEN MAX Gaming Laptop 16-ah0xxx (2025)
        "16-ap0",     // OMEN 16 ap0xxx (2025) uses hp-wmi/hwmon/platform-profile paths
        "17t-ah0",    // OMEN MAX Gaming Laptop 17t-ah0xxx (2025, if exists)
        "17-ah0",     // OMEN MAX Gaming Laptop 17-ah0xxx (2025, if exists)
        "transcend 14" // Transcend variants often use non-legacy EC maps and hp-wmi flow
    };

    // Board IDs known to expose hp-wmi with missing/partial thermal interfaces where
    // direct legacy EC writes are unreliable or immediately reverted by firmware watchdogs.
    private static readonly string[] UnsafeEcBoardIds = new[]
    {
        "8c58",
        "8d24",
        "8d26",
        "8e35",
        "8e41"
    };

    private static readonly string[] KnownHpWmiDkmsPackageNames =
    {
        "hp-omen-gaming-wmi",
        "hp-omen-wmi",
        "hp-wmi-omen",
        "hp-wmi"
    };
    
    public bool IsAvailable { get; }
    public bool HasEcAccess { get; }
    public bool HasHpWmiAccess { get; }
    public bool HasAcpiProfileAccess { get; }
    public bool HasHwmonFanAccess { get; }
    public bool HasHwmonPwmDutyAccess => _hwmonPwm1EnablePath != null && _hwmonPwm1Path != null;
    public bool HasHpWmiDkmsCompatibleFanBackend { get; }
    public bool HpWmiModuleLooksDkms { get; }
    public string HpWmiModuleSource { get; }
    public string HpWmiCompatibilityLabel { get; }
    public bool IsUnsafeEcModel { get; }
    public string AccessMethod { get; }
    public string? LastPerformanceModeBackend { get; private set; }
    public string? DetectedModel { get; }
    public string? DetectedBoardId { get; }
    
    public LinuxEcController()
    {
        // Detect model first to determine safe access methods
        DetectedModel = DetectModelName();
        DetectedBoardId = DetectBoardId();
        IsUnsafeEcModel = CheckUnsafeEcModel(DetectedModel, DetectedBoardId);
        
        HasEcAccess = File.Exists(EC_PATH) && !IsUnsafeEcModel;
        
        // HP-WMI is available if directory exists AND has actual control files
        HasHpWmiAccess = Directory.Exists(HP_WMI_PATH) && (
            ResolveHpWmiThermalPath() != null ||
            File.Exists(HP_WMI_FAN_ALWAYS_ON) ||
            File.Exists(HP_WMI_FAN1) ||
            File.Exists(HP_WMI_FAN2));
        
        // ACPI platform profile (kernel 5.18+, used by 2025+ models)
        HasAcpiProfileAccess = File.Exists(ACPI_PLATFORM_PROFILE);
        
        // Discover hp-wmi hwmon interface (pwm control for 2025+ models)
        DiscoverHwmonFanControl();
        HasHwmonFanAccess = _hwmonPwm1EnablePath != null;
        HasHpWmiDkmsCompatibleFanBackend = HasHwmonFanAccess &&
            (_hwmonPwm1Path != null || _hwmonPwm2Path != null || _hwmonFan1InputPath != null || _hwmonFan2InputPath != null);
        HpWmiModuleSource = DetectHpWmiModuleSource();
        HpWmiModuleLooksDkms = DetectHpWmiDkmsInstall(HpWmiModuleSource);
        HpWmiCompatibilityLabel = BuildHpWmiCompatibilityLabel();
        
        IsAvailable = HasEcAccess || HasHpWmiAccess || HasAcpiProfileAccess || HasHwmonFanAccess;
        
        // Priority: hp-wmi files > hwmon pwm > ACPI profile > ec_sys
        if (HasHpWmiAccess)
            AccessMethod = "hp-wmi";
        else if (HasHwmonFanAccess)
            AccessMethod = "hp-wmi-hwmon";
        else if (HasAcpiProfileAccess)
            AccessMethod = "acpi-profile";
        else if (HasEcAccess)
            AccessMethod = "ec_sys";
        else
            AccessMethod = "none";
        
        if (IsUnsafeEcModel)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ Model '{DetectedModel}' (board '{DetectedBoardId ?? "unknown"}') detected - direct EC register access disabled (different register layout or firmware watchdog behavior).");
            Console.WriteLine($"  Using safe interface: {AccessMethod}");
            Console.ResetColor();
        }
    }
    
    /// <summary>
    /// Detect HP model name from DMI.
    /// </summary>
    private static string? DetectModelName()
    {
        try
        {
            var path = File.Exists(DMI_PRODUCT_NAME) ? DMI_PRODUCT_NAME : DMI_PRODUCT_NAME_ALT;
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch { }
        return null;
    }

    private static string? ResolveFirstExistingPath(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveHpWmiThermalPath()
    {
        return LinuxSysfsPathMap.ResolveFirstExistingFile(LinuxSysfsPathMap.HpWmiProfilePaths);
    }

    public string GetPerformanceModeBackendDescription()
    {
        var hpWmiPath = ResolveHpWmiThermalPath();
        if (HasHpWmiAccess && hpWmiPath != null)
        {
            return $"hp-wmi {Path.GetFileName(hpWmiPath)}";
        }

        if (HasAcpiProfileAccess)
        {
            return "ACPI platform_profile";
        }

        if (HasEcAccess)
        {
            return "ec_sys performance register";
        }

        return "none";
    }

    /// <summary>
    /// Detect board identifier from DMI (for example: 8C58).
    /// </summary>
    private static string? DetectBoardId()
    {
        try
        {
            var path = File.Exists(DMI_BOARD_NAME) ? DMI_BOARD_NAME : DMI_BOARD_NAME_ALT;
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch { }
        return null;
    }
    
    /// <summary>
    /// Check if the detected model has an unknown/unsafe EC register layout.
    /// 2025+ OMEN Max models have completely different EC registers - writing legacy
    /// addresses to them causes EC panic (caps lock blinking pattern).
    /// GitHub Issue #60: OMEN Max 16t-ah000 EC panic from writing to 0x34/0x35
    /// (these addresses contain serial number data on 2025 models, not fan registers).
    /// </summary>
    private static bool CheckUnsafeEcModel(string? modelName, string? boardId)
    {
        if (!string.IsNullOrEmpty(modelName))
        {
            var modelLower = modelName.ToLowerInvariant();
            if (UnsafeEcModelPatterns.Any(pattern => modelLower.Contains(pattern.ToLowerInvariant())))
                return true;
        }

        if (!string.IsNullOrEmpty(boardId))
        {
            var boardLower = boardId.ToLowerInvariant();
            if (UnsafeEcBoardIds.Any(id => boardLower.Equals(id, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
    
    /// <summary>
    /// Discover hp-wmi hwmon fan control paths.
    /// 2025+ OMEN models expose fan control via standard hwmon interface:
    ///   /sys/devices/platform/hp-wmi/hwmon/hwmonN/pwm1_enable
    ///   0 = full speed, 1 = manual, 2 = auto (BIOS), 3 = fan off
    /// </summary>
    private void DiscoverHwmonFanControl()
    {
        var hpWmiHwmonPath = Path.Combine(HP_WMI_PATH, "hwmon");
        if (!Directory.Exists(hpWmiHwmonPath))
            return;
        
        foreach (var hwmonDir in Directory.GetDirectories(hpWmiHwmonPath))
        {
            var pwm1Enable = Path.Combine(hwmonDir, "pwm1_enable");
            if (File.Exists(pwm1Enable))
            {
                _hwmonPwm1EnablePath = pwm1Enable;
                
                // Check for fan RPM inputs
                var fan1Input = Path.Combine(hwmonDir, "fan1_input");
                if (File.Exists(fan1Input))
                    _hwmonFan1InputPath = fan1Input;
                
                var fan2Input = Path.Combine(hwmonDir, "fan2_input");
                if (File.Exists(fan2Input))
                    _hwmonFan2InputPath = fan2Input;

                var pwm1 = Path.Combine(hwmonDir, "pwm1");
                if (File.Exists(pwm1))
                    _hwmonPwm1Path = pwm1;

                var pwm2Enable = Path.Combine(hwmonDir, "pwm2_enable");
                if (File.Exists(pwm2Enable))
                    _hwmonPwm2EnablePath = pwm2Enable;

                var pwm2 = Path.Combine(hwmonDir, "pwm2");
                if (File.Exists(pwm2))
                    _hwmonPwm2Path = pwm2;
                
                break;
            }
        }
    }
    
    /// <summary>
    /// Get detailed diagnostic information for troubleshooting.
    /// </summary>
    public Dictionary<string, object> GetDiagnostics()
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["ec_available"] = IsAvailable,
            ["ec_access_method"] = AccessMethod,
            ["ec_sys_path"] = EC_PATH,
            ["ec_sys_exists"] = File.Exists(EC_PATH),
            ["hp_wmi_path"] = HP_WMI_PATH,
            ["hp_wmi_exists"] = Directory.Exists(HP_WMI_PATH),
            ["kernel_version"] = GetKernelVersion(),
            ["distribution"] = GetDistributionInfo(),
            ["is_root"] = CheckRootAccess(),
            ["detected_model"] = DetectedModel ?? "unknown",
            ["detected_board_id"] = DetectedBoardId ?? "unknown",
            ["hwmon_pwm1_enable_path"] = _hwmonPwm1EnablePath ?? "",
            ["hwmon_pwm2_enable_path"] = _hwmonPwm2EnablePath ?? "",
            ["hwmon_pwm1_path"] = _hwmonPwm1Path ?? "",
            ["hwmon_pwm2_path"] = _hwmonPwm2Path ?? "",
            ["hwmon_fan1_input_path"] = _hwmonFan1InputPath ?? "",
            ["hwmon_fan2_input_path"] = _hwmonFan2InputPath ?? "",
            ["hwmon_pwm_duty_available"] = HasHwmonPwmDutyAccess,
            ["hp_wmi_module_source"] = HpWmiModuleSource,
            ["hp_wmi_module_looks_dkms"] = HpWmiModuleLooksDkms,
            ["hp_wmi_dkms_compatible_fan_backend"] = HasHpWmiDkmsCompatibleFanBackend,
            ["hp_wmi_compatibility_label"] = HpWmiCompatibilityLabel
        };
        
        // Check file permissions if paths exist
        if (File.Exists(EC_PATH))
        {
            try
            {
                var info = new FileInfo(EC_PATH);
                diagnostics["ec_sys_permissions"] = $"{info.UnixFileMode}";
                diagnostics["ec_sys_can_read"] = CanReadFile(EC_PATH);
                diagnostics["ec_sys_can_write"] = CanWriteFile(EC_PATH);
            }
            catch (Exception ex)
            {
                diagnostics["ec_sys_permissions_error"] = ex.Message;
            }
        }
        
        // Check HP-WMI files
        var wmiFiles = new[] { HP_WMI_THERMAL, HP_WMI_THERMAL_ALT, HP_WMI_FAN_ALWAYS_ON, HP_WMI_FAN1, HP_WMI_FAN2 };
        foreach (var file in wmiFiles)
        {
            diagnostics[$"hp_wmi_{Path.GetFileName(file)}"] = File.Exists(file);
        }
        
        return diagnostics;
    }
    
    private string GetKernelVersion()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "uname",
                    Arguments = "-r",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return output;
        }
        catch
        {
            return "unknown";
        }
    }
    
    private string GetDistributionInfo()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var id = lines.FirstOrDefault(l => l.StartsWith("ID="))?.Split('=')[1].Trim('"') ?? "unknown";
                var version = lines.FirstOrDefault(l => l.StartsWith("VERSION_ID="))?.Split('=')[1].Trim('"') ?? "";
                return $"{id} {version}".Trim();
            }
        }
        catch
        {
            // Ignore errors
        }
        return "unknown";
    }

    private string BuildHpWmiCompatibilityLabel()
    {
        if (!Directory.Exists(HP_WMI_PATH))
        {
            return "hp-wmi unavailable";
        }

        if (HasHpWmiDkmsCompatibleFanBackend)
        {
            return HpWmiModuleLooksDkms
                ? "hp-omen-gaming-wmi-dkms compatible hp-wmi/hwmon fan backend"
                : "upstream hp-wmi/hwmon fan backend";
        }

        if (HasHwmonFanAccess)
        {
            return "hp-wmi hwmon policy backend without writable PWM duty";
        }

        if (HasHpWmiAccess || HasAcpiProfileAccess)
        {
            return "hp-wmi/profile backend";
        }

        return "hp-wmi present without supported OMEN control files";
    }

    private static string DetectHpWmiModuleSource()
    {
        if (!Directory.Exists("/sys/module/hp_wmi"))
        {
            return "not-loaded";
        }

        var modinfoPath = TryRunCommand("modinfo", "-F filename hp_wmi");
        if (!string.IsNullOrWhiteSpace(modinfoPath))
        {
            return modinfoPath.Trim();
        }

        return "loaded";
    }

    private static bool DetectHpWmiDkmsInstall(string moduleSource)
    {
        if (moduleSource.Contains("dkms", StringComparison.OrdinalIgnoreCase) ||
            moduleSource.Contains("updates", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string dkmsRoot = "/var/lib/dkms";
        if (!Directory.Exists(dkmsRoot))
        {
            return false;
        }

        try
        {
            return Directory.GetDirectories(dkmsRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Any(name => KnownHpWmiDkmsPackageNames.Any(
                    known => name!.Contains(known, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static string? TryRunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
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

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(750))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort only; detection must not break controller setup.
                }
                return null;
            }

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
    
    private bool CanReadFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private bool CanWriteFile(string path)
    {
        try
        {
            using var fs = File.OpenWrite(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public static bool CheckRootAccess()
    {
        return Environment.UserName == "root" || Mono.Unix.Native.Syscall.getuid() == 0;
    }
    
    /// <summary>
    /// Read a byte from the EC at the specified address.
    /// </summary>
    public byte? ReadByte(byte address)
    {
        if (!HasEcAccess) return null;
        
        try
        {
            lock (EcIoLock)
            {
                using var fs = new FileStream(EC_PATH, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(address, SeekOrigin.Begin);
                var value = fs.ReadByte();
                return value >= 0 ? (byte)value : null;
            }
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Write a byte to the EC at the specified address.
    /// SAFETY: Blocked on 2025+ OMEN models with unknown EC register layouts.
    /// </summary>
    public bool WriteByte(byte address, byte value)
    {
        if (!HasEcAccess) return false;
        
        // Safety check: block EC writes on models with unknown register layout
        if (IsUnsafeEcModel)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ EC write blocked: Register 0x{address:X2} write denied for safety.");
            Console.WriteLine($"  Model '{DetectedModel}' has an unmapped EC register layout.");
            Console.WriteLine($"  Writing to legacy registers causes EC panic (caps lock blinking).");
            Console.ResetColor();
            return false;
        }
        
        try
        {
            lock (EcIoLock)
            {
                using var fs = new FileStream(EC_PATH, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                fs.Seek(address, SeekOrigin.Begin);
                fs.WriteByte(value);
                fs.Flush();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
    
    #region HP-WMI Support (2023+ Models)
    
    /// <summary>
    /// Set thermal profile via hp-wmi (for 2023+ OMEN models).
    /// Available profiles: "quiet", "balanced", "performance", "extreme"
    /// </summary>
    public bool SetHpWmiThermalProfile(string profile)
    {
        var thermalPath = ResolveHpWmiThermalPath();
        if (!HasHpWmiAccess || thermalPath == null)
            return false;

        try
        {
            var normalized = profile.Trim().ToLowerInvariant();
            var choices = GetHpWmiProfileChoices();
            normalized = ResolveSupportedProfileAlias(normalized, choices);

            if (choices.Length > 0 && !choices.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                return false;

            if (choices.Length == 0 && !IsKnownProfileValue(normalized))
                return false;

            File.WriteAllText(thermalPath, normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Get current thermal profile from hp-wmi.
    /// </summary>
    public string? GetHpWmiThermalProfile()
    {
        var thermalPath = ResolveHpWmiThermalPath();
        if (!HasHpWmiAccess || thermalPath == null)
            return null;

        try
        {
            return File.ReadAllText(thermalPath).Trim();
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Set fan speed via hp-wmi (if fan output controls exist).
    /// </summary>
    public bool SetHpWmiFanSpeed(int fanIndex, int percent)
    {
        if (!HasHpWmiAccess)
            return false;

        var fanPath = fanIndex == 0 ? HP_WMI_FAN1 : HP_WMI_FAN2;
        if (!File.Exists(fanPath))
            return false;

        try
        {
            // Enable fan_always_on to prevent BIOS from overriding
            if (File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                File.WriteAllText(HP_WMI_FAN_ALWAYS_ON, "1");
            }

            // Write fan speed percentage
            File.WriteAllText(fanPath, percent.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Get fan speed from hp-wmi.
    /// </summary>
    public int? GetHpWmiFanSpeed(int fanIndex)
    {
        if (!HasHpWmiAccess)
            return null;

        var fanPath = fanIndex == 0 ? HP_WMI_FAN1 : HP_WMI_FAN2;
        if (!File.Exists(fanPath))
            return null;

        try
        {
            var text = File.ReadAllText(fanPath).Trim();
            if (int.TryParse(text, out var value))
                return value;
        }
        catch
        {
            // Ignore
        }

        return null;
    }
    
    /// <summary>
    /// Check if hp-wmi has fan output controls (for direct fan speed setting).
    /// </summary>
    public bool HasHpWmiFanControls()
    {
        return HasHpWmiAccess && (File.Exists(HP_WMI_FAN1) || File.Exists(HP_WMI_FAN2));
    }
    
    /// <summary>
    /// Check if hp-wmi has thermal profile control.
    /// </summary>
    public bool HasHpWmiThermalProfile()
    {
        return HasHpWmiAccess && ResolveHpWmiThermalPath() != null;
    }
    
    #endregion
    
    #region ACPI Platform Profile (2025+ Models)
    
    /// <summary>
    /// Get available ACPI platform profiles.
    /// Returns profiles like: "low-power", "balanced", "performance"
    /// </summary>
    public string[] GetAcpiProfileChoices()
    {
        var choicePaths = new[]
        {
            ACPI_PLATFORM_PROFILE_CHOICES,
            HP_WMI_PLATFORM_PROFILE_CHOICES,
            HP_WMI_PLATFORM_PROFILE_CHOICES_ALT,
            HP_WMI_PERFORMANCE_PROFILE_CHOICES,
            HP_WMI_PERFORMANCE_PROFILE_CHOICES_ALT,
            HP_WMI_THERMAL_CHOICES,
            HP_WMI_THERMAL_CHOICES_ALT
        };

        foreach (var path in choicePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return ParseProfileChoices(File.ReadAllText(path));
            }
            catch
            {
                // Try next path.
            }
        }

        return Array.Empty<string>();
    }
    
    /// <summary>
    /// Get current ACPI platform profile.
    /// </summary>
    public string? GetAcpiProfile()
    {
        if (!HasAcpiProfileAccess)
            return null;
        
        try
        {
            return File.ReadAllText(ACPI_PLATFORM_PROFILE).Trim();
        }
        catch { return null; }
    }
    
    /// <summary>
    /// Set ACPI platform profile.
    /// Valid values typically: "low-power", "balanced", "performance"
    /// </summary>
    public bool SetAcpiProfile(string profile)
    {
        if (!HasAcpiProfileAccess)
            return false;
        
        try
        {
            var normalized = profile.Trim().ToLowerInvariant();
            var choices = GetAcpiProfileChoices();
            if (choices.Length > 0 && !choices.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                normalized = ResolveSupportedProfileAlias(normalized, choices);
            }

            if (choices.Length > 0 && !choices.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                return false;

            File.WriteAllText(ACPI_PLATFORM_PROFILE, normalized);
            return true;
        }
        catch { return false; }
    }

    private static string ResolveSupportedProfileAlias(string requested, string[] choices)
    {
        if (choices.Length == 0)
        {
            return requested;
        }

        var exact = choices.FirstOrDefault(choice => choice.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact.ToLowerInvariant();
        }

        if (IsPerformanceProfile(requested))
        {
            return FirstSupportedProfile(choices, "performance", "balanced-performance", "extreme", "high-performance") ?? requested;
        }

        if (IsExtremeProfile(requested))
        {
            return FirstSupportedProfile(choices, "extreme", "performance", "balanced-performance") ?? requested;
        }

        if (IsCoolProfile(requested))
        {
            return FirstSupportedProfile(choices, "quiet", "low-power", "power-saver", "cool") ?? requested;
        }

        if (IsBalancedProfile(requested))
        {
            return FirstSupportedProfile(choices, "balanced", "default", "normal") ?? requested;
        }

        return requested;
    }

    private static string[] GetHpWmiProfileChoices()
    {
        foreach (var path in LinuxSysfsPathMap.HpWmiProfileChoicePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return ParseProfileChoices(File.ReadAllText(path));
            }
            catch
            {
                // Try the next hp-wmi choices path.
            }
        }

        return Array.Empty<string>();
    }

    private static string[] ParseProfileChoices(string choices)
    {
        return choices
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(choice => choice.Trim().Trim('[', ']').ToLowerInvariant())
            .Where(choice => !string.IsNullOrWhiteSpace(choice))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FirstSupportedProfile(string[] choices, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = choices.FirstOrDefault(choice => choice.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match.ToLowerInvariant();
            }
        }

        return null;
    }

    private static bool IsKnownProfileValue(string profile) =>
        IsBalancedProfile(profile) ||
        IsPerformanceProfile(profile) ||
        IsExtremeProfile(profile) ||
        IsCoolProfile(profile);

    private static bool IsBalancedProfile(string profile) =>
        profile.Equals("balanced", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("default", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static bool IsPerformanceProfile(string profile) =>
        profile.Equals("performance", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("balanced-performance", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("high-performance", StringComparison.OrdinalIgnoreCase);

    private static bool IsExtremeProfile(string profile) =>
        profile.Equals("extreme", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("max", StringComparison.OrdinalIgnoreCase);

    private static bool IsCoolProfile(string profile) =>
        profile.Equals("quiet", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("cool", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("silent", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("low-power", StringComparison.OrdinalIgnoreCase) ||
        profile.Equals("power-saver", StringComparison.OrdinalIgnoreCase);
    
    #endregion
    
    #region Hwmon PWM Fan Control (2025+ Models)
    
    /// <summary>
    /// Set fan control mode via hwmon pwm_enable.
    /// Used by 2025+ OMEN Max models where standard hp-wmi files don't exist
    /// but hp-wmi/hwmon/hwmonN/pwm1_enable is available.
    /// 
    /// Values:
    ///   0 = Full speed (all fans max)
    ///   1 = Manual PWM control
    ///   2 = Automatic (BIOS controlled) 
    ///   3 = Fan off (DANGEROUS - use with extreme caution)
    /// </summary>
    public bool SetHwmonPwmEnable(int value)
    {
        if (_hwmonPwm1EnablePath == null)
            return false;
        
        try
        {
            // Safety: never allow value 3 (fan off) through this interface
            if (value == 3)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Fan off (pwm_enable=3) is blocked for safety.");
                Console.ResetColor();
                return false;
            }
            
            File.WriteAllText(_hwmonPwm1EnablePath, value.ToString());
            
            // Also set pwm2 if it exists (second fan)
            if (_hwmonPwm2EnablePath != null)
                File.WriteAllText(_hwmonPwm2EnablePath, value.ToString());
            
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Set manual fan duty through hp-wmi hwmon pwm files.
    /// Uses pwm_enable=1 and writes pwmN in the kernel-standard 1..255 range.
    /// </summary>
    public bool SetHwmonPwmDutyPercent(int percent)
    {
        if (!HasHwmonPwmDutyAccess || _hwmonPwm1Path == null)
            return false;

        var pct = Math.Clamp(percent, 1, 100);
        var duty = Math.Clamp((int)Math.Round(pct * 255.0 / 100.0), 1, 255);

        try
        {
            if (!SetHwmonPwmEnable(1))
                return false;

            File.WriteAllText(_hwmonPwm1Path, duty.ToString());
            if (_hwmonPwm2Path != null)
                File.WriteAllText(_hwmonPwm2Path, duty.ToString());

            return true;
        }
        catch { return false; }
    }

    public int? GetHwmonPwmDutyPercent()
    {
        if (_hwmonPwm1Path == null)
            return null;

        try
        {
            var text = ReadSysfsFile(_hwmonPwm1Path);
            if (int.TryParse(text, out var duty))
            {
                return Math.Clamp((int)Math.Round(duty * 100.0 / 255.0), 0, 100);
            }
        }
        catch { }

        return null;
    }
    
    /// <summary>
    /// Get current hwmon pwm_enable value.
    /// </summary>
    public int? GetHwmonPwmEnable()
    {
        if (_hwmonPwm1EnablePath == null)
            return null;
        
        try
        {
            var text = File.ReadAllText(_hwmonPwm1EnablePath).Trim();
            if (int.TryParse(text, out var value))
                return value;
        }
        catch { }
        return null;
    }
    
    /// <summary>
    /// Get fan RPM from hwmon fan_input (if available).
    /// Uses unbuffered sysfs reads to get fresh values each call.
    /// </summary>
    public (int fan1, int fan2) GetHwmonFanSpeeds()
    {
        int fan1 = 0, fan2 = 0;
        
        if (_hwmonFan1InputPath != null)
        {
            try
            {
                var text = ReadSysfsFile(_hwmonFan1InputPath);
                if (int.TryParse(text, out var val)) fan1 = val;
            }
            catch { }
        }
        
        if (_hwmonFan2InputPath != null)
        {
            try
            {
                var text = ReadSysfsFile(_hwmonFan2InputPath);
                if (int.TryParse(text, out var val)) fan2 = val;
            }
            catch { }
        }
        
        return (fan1, fan2);
    }
    
    /// <summary>
    /// Read a sysfs file with no buffering to ensure fresh values.
    /// Standard File.ReadAllText() may return stale page-cached content on some kernels.
    /// </summary>
    private static string ReadSysfsFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, 
                                       FileShare.ReadWrite, bufferSize: 1, 
                                       FileOptions.None);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd().Trim();
    }
    
    #endregion
    
    #region Fan Control
    
    /// <summary>
    /// Get current fan speeds in RPM.
    /// Tries hwmon first (2025+ models), then EC registers (legacy models).
    /// </summary>
    public (int fan1, int fan2) GetFanSpeeds()
    {
        // Try hwmon fan_input first (2025+ models)
        if (HasHwmonFanAccess)
        {
            var (f1, f2) = GetHwmonFanSpeeds();
            if (f1 > 0 || f2 > 0)
                return (f1, f2);
        }
        
        if (!HasEcAccess)
            return (0, 0);

        var fan1 = (ReadByte(REG_FAN1_SPEED_SET) ?? 0) * 100;
        var fan2 = (ReadByte(REG_FAN2_SPEED_SET) ?? 0) * 100;
        return (fan1, fan2);
    }
    
    /// <summary>
    /// Get current fan speeds as percentage (0-100).
    /// Supports both EC register and hwmon backends.
    /// </summary>
    public (int fan1, int fan2) GetFanSpeedPercent()
    {
        // For hwmon-only models (no EC access), estimate percentage from RPM
        if (HasHwmonFanAccess && !HasEcAccess)
        {
            var (rpm1, rpm2) = GetHwmonFanSpeeds();
            const int estimatedMaxRpm = 5500;
            return (
                Math.Clamp(rpm1 * 100 / Math.Max(estimatedMaxRpm, 1), 0, 100),
                Math.Clamp(rpm2 * 100 / Math.Max(estimatedMaxRpm, 1), 0, 100)
            );
        }
        
        if (!HasEcAccess)
            return (0, 0);

        var fan1 = ReadByte(REG_FAN1_SPEED_PCT) ?? 0;
        var fan2 = ReadByte(REG_FAN2_SPEED_PCT) ?? 0;
        return (fan1, fan2);
    }
    
    /// <summary>
    /// Set Fan 1 speed in units of 100 RPM.
    /// </summary>
    public bool SetFan1Speed(byte speedUnit)
    {
        return WriteByte(REG_FAN1_SPEED_SET, speedUnit);
    }
    
    /// <summary>
    /// Set Fan 2 speed in units of 100 RPM.
    /// </summary>
    public bool SetFan2Speed(byte speedUnit)
    {
        return WriteByte(REG_FAN2_SPEED_SET, speedUnit);
    }
    
    /// <summary>
    /// Set both fan speeds to the same percentage.
    /// </summary>
    public bool SetFanSpeedPercent(int percent)
    {
        var pct = (byte)Math.Clamp(percent, 0, 100);

        if (HasHwmonPwmDutyAccess)
        {
            return pct == 0
                ? SetHwmonPwmEnable(2)
                : SetHwmonPwmDutyPercent(pct);
        }

        // Convert % to RPM units (assuming max ~5500 RPM = 55 units)
        var speedUnit = (byte)(pct * 55 / 100);
        
        return SetFan1Speed(speedUnit) && SetFan2Speed(speedUnit);
    }
    
    /// <summary>
    /// Enable or disable fan boost mode.
    /// </summary>
    public bool SetFanBoost(bool enabled)
    {
        return WriteByte(REG_FAN_BOOST, (byte)(enabled ? 0x0C : 0x00));
    }
    
    /// <summary>
    /// Set fan profile.
    /// Uses hp-wmi if available, then ACPI platform_profile + hwmon, then EC.
    /// </summary>
    public bool SetFanProfile(FanProfile profile)
    {
        // Try hp-wmi thermal_profile first (newer 2023+ models)
        if (HasHpWmiAccess && ResolveHpWmiThermalPath() != null)
        {
            if (SetHpWmiThermalProfile(profile))
            {
                return true;
            }
        }
        
        // Try ACPI platform_profile + hwmon pwm (2025+ OMEN Max models)
        if (HasAcpiProfileAccess || HasHwmonFanAccess)
        {
            return SetFanProfileViaAcpiHwmon(profile);
        }
        
        // Fall back to EC register method (older models)
        if (!HasEcAccess)
            return false;
            
        return profile switch
        {
            FanProfile.Auto => RestoreAutoMode(),
            FanProfile.Silent => SetManualFanSpeed(30),
            FanProfile.Balanced => SetManualFanSpeed(50),
            FanProfile.Gaming => SetManualFanSpeed(80),
            FanProfile.Max => SetManualFanSpeed(100),
            _ => false
        };
    }
    
    /// <summary>
    /// Set thermal profile via hp-wmi driver (newer OMEN models).
    /// </summary>
    private bool SetHpWmiThermalProfile(FanProfile profile)
    {
        var profileValue = profile switch
        {
            FanProfile.Auto => "balanced",
            FanProfile.Silent => "quiet",
            FanProfile.Balanced => "balanced", 
            FanProfile.Gaming => "performance",
            FanProfile.Max => "extreme",
            _ => "balanced"
        };
        
        try
        {
            if (!SetHpWmiThermalProfile(profileValue))
            {
                return false;
            }
            
            // For Max mode, also enable fan_always_on if available
            if (profile == FanProfile.Max && File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                File.WriteAllText(HP_WMI_FAN_ALWAYS_ON, "1");
            }
            else if (profile == FanProfile.Auto && File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                File.WriteAllText(HP_WMI_FAN_ALWAYS_ON, "0");
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Set fan profile via ACPI platform_profile and/or hwmon pwm_enable.
    /// Used by 2025+ OMEN Max models that don't have the legacy hp-wmi thermal_profile file.
    /// 
    /// ACPI profiles: "low-power" (quiet), "balanced", "performance"
    /// Hwmon pwm_enable: 0=full speed, 2=auto (BIOS)
    /// 
    /// GitHub Issue #60: OMEN Max 16t-ah000 uses this interface.
    /// </summary>
    private bool SetFanProfileViaAcpiHwmon(FanProfile profile)
    {
        bool success = false;

        if (!HasAcpiProfileAccess && HasHwmonFanAccess && profile is not (FanProfile.Auto or FanProfile.Max))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ This kernel exposes hwmon fan mode only, not a writable thermal profile.");
            Console.WriteLine("  Auto and Max can be requested through pwm_enable; Silent/Balanced/Gaming require ACPI/HP-WMI profile support.");
            Console.ResetColor();
            return false;
        }
        
        // Map fan profile to ACPI platform profile
        if (HasAcpiProfileAccess)
        {
            var acpiProfile = profile switch
            {
                FanProfile.Auto => "balanced",
                FanProfile.Silent => "low-power",
                FanProfile.Balanced => "balanced",
                FanProfile.Gaming => "performance",
                FanProfile.Max => "performance",
                _ => "balanced"
            };
            
            success = SetAcpiProfile(acpiProfile);
        }
        
        // For Max mode, also set pwm_enable=0 (full speed) for temporary boost
        // For Auto mode, set pwm_enable=2 (BIOS auto)
        if (HasHwmonFanAccess)
        {
            var pwmValue = profile switch
            {
                FanProfile.Max => 0,        // Full speed
                FanProfile.Auto => 2,       // BIOS auto
                FanProfile.Silent => 2,     // Let BIOS handle with low-power profile
                FanProfile.Balanced => 2,   // Let BIOS handle with balanced profile
                FanProfile.Gaming => 2,     // Let BIOS handle with performance profile
                _ => 2
            };

            success = SetHwmonPwmEnable(pwmValue) || success;
        }

        // GitHub #183 (board 8D87): pwm_enable=2 is a policy flag telling the firmware "you
        // take over" - it is not a guarantee the firmware actually resumes driving pwm1. On
        // this board, degraded ACPI (kernel logs showed WMAA/WHCM/WQB* method aborts) let the
        // write succeed while both fans stayed at 0 RPM under a full gaming load, with nothing
        // else watching (this is a one-shot CLI command, not a monitored daemon) - the machine
        // thermally shut down. Verify the transition actually resumed cooling before reporting
        // Auto as applied.
        if (success && profile == FanProfile.Auto && HasHwmonFanAccess)
        {
            success = VerifyAutoModeResumedCoolingOrFallBackToMax();
        }

        return success;
    }

    /// <summary>
    /// Polls fan RPM for a few seconds after an Auto-mode write. If both fans are still at
    /// 0 RPM while CPU or GPU temperature is above a safety threshold, restores Max mode (the
    /// same write path GitHub #183's reporter confirmed works reliably on this board) instead
    /// of leaving the user with dead cooling under load. Returns false when the fallback fired,
    /// so the caller reports the Auto transition as failed rather than successful - matching
    /// the issue's own suggested fix ("report Auto as failed when RPM readback contradicts the
    /// requested state").
    ///
    /// 85C is a deliberately conservative "clearly not idle, needs airflow" bar - well below
    /// the 90-95C ramp/emergency range this project already treats as thermal-protection
    /// territory elsewhere, so this fires with margin to spare rather than waiting until
    /// things are already dangerous. A machine that's merely cool and briefly fanless while
    /// idle never reaches this threshold and is left alone.
    /// </summary>
    private bool VerifyAutoModeResumedCoolingOrFallBackToMax()
    {
        const int SafetyTempThresholdC = 85;
        const int PollAttempts = 4;
        const int PollIntervalMs = 1000;

        var hwmon = new LinuxHwMonController();

        for (var i = 0; i < PollAttempts; i++)
        {
            Thread.Sleep(PollIntervalMs);

            var (fan1, fan2) = GetFanSpeeds();
            if (fan1 > 0 || fan2 > 0)
            {
                // Fans responded - Auto genuinely took over.
                return true;
            }

            var cpuTemp = LinuxTelemetryResolver.GetCpuTemperature(this, hwmon)?.Temperature ?? 0;
            var gpuTemp = LinuxTelemetryResolver.GetGpuTemperature(this, hwmon)?.Temperature ?? 0;
            var hottest = Math.Max(cpuTemp, gpuTemp);

            if (hottest < SafetyTempThresholdC)
            {
                // Not obviously dangerous yet - keep watching rather than jumping straight to
                // Max on a machine that may just be genuinely idle.
                continue;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"⚠ SAFETY: both fans stayed at 0 RPM after switching to Auto while CPU/GPU is at {hottest}°C.");
            Console.WriteLine("  Restoring Max fan mode automatically to prevent a thermal shutdown (see GitHub issue #183).");
            Console.ResetColor();

            SetFanProfileViaAcpiHwmon(FanProfile.Max);
            return false;
        }

        // Fans stayed at 0 for the whole poll window but never got hot enough to trip the
        // safety threshold - most likely a genuinely idle machine, not the #183 failure mode.
        return true;
    }
    
    /// <summary>
    /// Restore BIOS automatic fan control via EC registers.
    /// This resets all manual overrides and lets the BIOS control fans.
    /// 
    /// Based on Issue #27: Auto mode not restoring correctly on some OMEN 15 2020 models.
    /// The fix requires a more thorough EC reset sequence.
    /// </summary>
    public bool RestoreAutoMode()
    {
        // Try hp-wmi first (newer models like OMEN 16 2023+)
        if (HasHpWmiAccess && ResolveHpWmiThermalPath() != null)
        {
            return RestoreAutoModeViaHpWmi();
        }
        
        // Try ACPI/hwmon path (2025+ OMEN Max models). Routed through the same
        // SetFanProfileViaAcpiHwmon(Auto) path SetFanProfile() itself uses (rather than
        // duplicating the ACPI-profile/pwm_enable writes here) so this gets the GitHub #183
        // fan-resumed-cooling safety check for free - this method is also called directly by
        // the fan-curve daemon (Daemon/FanCurveEngine.cs), which needs that protection at
        // least as much as the one-shot CLI does, since nothing else is watching either way.
        if (HasAcpiProfileAccess || HasHwmonFanAccess)
        {
            return SetFanProfileViaAcpiHwmon(FanProfile.Auto);
        }

        // Fall back to EC register method (older models)
        if (!HasEcAccess)
            return false;
        
        // Full EC reset sequence to restore BIOS control
        // Order matters! Some models need specific sequencing.
        
        // Step 1: Clear manual fan speed registers first (write 0 to let BIOS control)
        WriteByte(REG_FAN1_SPEED_SET, 0x00);
        WriteByte(REG_FAN2_SPEED_SET, 0x00);
        WriteByte(REG_FAN1_SPEED_PCT, 0x00);
        WriteByte(REG_FAN2_SPEED_PCT, 0x00);
        
        // Step 2: Disable fan boost
        WriteByte(REG_FAN_BOOST, 0x00);
        
        // Step 3: Enable fan state (allow BIOS to control) - BEFORE enabling BIOS control
        if (!WriteByte(REG_FAN_STATE, 0x00))
            return false;
            
        // Step 4: Re-enable BIOS fan control
        if (!WriteByte(REG_BIOS_CONTROL, 0x00))
            return false;
        
        // Step 5: Reset timer to trigger BIOS to recalculate fan speeds
        // Timer counts down from 0x78 (120); resetting to 0x78 forces BIOS to take over
        WriteByte(REG_TIMER, 0x78);
        
        // Step 6: Wait briefly then verify BIOS has taken control
        Thread.Sleep(100);
        
        // Double-check: write fan state again to ensure BIOS control
        WriteByte(REG_FAN_STATE, 0x00);
        
        return true;
    }
    
    /// <summary>
    /// Restore auto mode via HP-WMI driver (newer models).
    /// </summary>
    private bool RestoreAutoModeViaHpWmi()
    {
        try
        {
            // Set thermal profile to balanced (auto)
            var thermalPath = ResolveHpWmiThermalPath();
            if (thermalPath != null)
            {
                File.WriteAllText(thermalPath, "balanced");
            }
            
            // Disable fan_always_on to let BIOS control
            if (File.Exists(HP_WMI_FAN_ALWAYS_ON))
            {
                File.WriteAllText(HP_WMI_FAN_ALWAYS_ON, "0");
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Set manual fan speed (disables BIOS control).
    /// </summary>
    private bool SetManualFanSpeed(int percent)
    {
        // First disable BIOS control to take over
        WriteByte(REG_BIOS_CONTROL, 0x06);  // Disable BIOS control
        WriteByte(REG_FAN_STATE, 0x02);     // Disable auto state
        
        // Set the speed
        return SetFanSpeedPercent(percent);
    }
    
    /// <summary>
    /// Enable or disable BIOS fan control.
    /// </summary>
    public bool SetFanState(bool biosControl)
    {
        return WriteByte(REG_FAN_STATE, (byte)(biosControl ? 0x00 : 0x02));
    }
    
    #endregion
    
    #region Temperature
    
    /// <summary>
    /// Get CPU temperature from EC.
    /// Returns null on 2025+ models where EC register 0x57 contains non-temperature data.
    /// </summary>
    public int? GetCpuTemperature()
    {
        if (IsUnsafeEcModel || !HasEcAccess)
            return null;
        
        var temp = ReadByte(REG_CPU_TEMP);
        
        // Sanity check: reject obviously invalid temperatures
        // Valid range: 10°C to 115°C (beyond TjMax of any current laptop CPU)
        if (temp.HasValue && (temp.Value < 10 || temp.Value > 115))
            return null;
        
        return temp;
    }
    
    /// <summary>
    /// Get GPU temperature from EC.
    /// Returns null on 2025+ models where EC register 0xB7 contains non-temperature data
    /// (e.g., reading 0xC0 = 192°C is clearly garbage from wrong register layout).
    /// GitHub Issue #60: OMEN Max 16t reports 128°C/192°C from wrong EC registers.
    /// </summary>
    public int? GetGpuTemperature()
    {
        if (IsUnsafeEcModel || !HasEcAccess)
            return null;
        
        var temp = ReadByte(REG_GPU_TEMP);
        
        // Sanity check: reject obviously invalid temperatures
        if (temp.HasValue && (temp.Value < 10 || temp.Value > 115))
            return null;
        
        return temp;
    }
    
    #endregion
    
    #region Performance
    
    /// <summary>
    /// Get current performance mode.
    /// Reads hp-wmi thermal_profile first when available (same priority as SetPerformanceMode),
    /// falling back to the EC register so the reported mode always reflects the active backend.
    /// </summary>
    public PerformanceMode GetPerformanceMode()
    {
        // Priority 1: hp-wmi — read the current thermal_profile string
        var thermalPath = ResolveHpWmiThermalPath();
        if (HasHpWmiAccess && thermalPath != null)
        {
            try
            {
                var profile = File.ReadAllText(thermalPath).Trim().ToLowerInvariant();
                return profile switch
                {
                    "performance" => PerformanceMode.Performance,
                    "balanced-performance" => PerformanceMode.Performance,
                    "high-performance" => PerformanceMode.Performance,
                    "extreme"     => PerformanceMode.Performance,
                    "quiet"       => PerformanceMode.Cool,
                    "cool"        => PerformanceMode.Cool,
                    "silent"      => PerformanceMode.Cool,
                    "low-power"   => PerformanceMode.Cool,
                    "power-saver" => PerformanceMode.Cool,
                    "balanced"    => PerformanceMode.Default,
                    "default"     => PerformanceMode.Default,
                    "normal"      => PerformanceMode.Default,
                    "auto"        => PerformanceMode.Default,
                    _             => PerformanceMode.Default
                };
            }
            catch { /* fall through */ }
        }

        // Priority 2: ACPI platform_profile
        if (HasAcpiProfileAccess)
        {
            var profile = GetAcpiProfile()?.ToLowerInvariant();
            if (profile != null)
            {
                return profile switch
                {
                    "performance" => PerformanceMode.Performance,
                    "balanced-performance" => PerformanceMode.Performance,
                    "low-power"   => PerformanceMode.Cool,
                    "quiet"       => PerformanceMode.Cool,
                    "balanced"    => PerformanceMode.Default,
                    _             => PerformanceMode.Default
                };
            }
        }

        // Priority 3: EC register
        var value = ReadByte(REG_PERF_MODE);
        return value switch
        {
            PERF_MODE_DEFAULT     => PerformanceMode.Default,
            PERF_MODE_PERFORMANCE => PerformanceMode.Performance,
            PERF_MODE_COOL        => PerformanceMode.Cool,
            _                     => PerformanceMode.Balanced
        };
    }
    
    /// <summary>
    /// Set performance mode.
    /// </summary>
    public bool SetPerformanceMode(PerformanceMode mode)
    {
        // RC-4 fix: route through the appropriate backend in priority order.
        // Previously this only called WriteByte() which requires HasEcAccess,
        // silently returning false on hp-wmi-only systems (e.g. OMEN 16-wf1xxx / Board 8C78).

        // Priority 1: hp-wmi thermal_profile (Secure Boot safe, most compatible)
        if (HasHpWmiAccess)
        {
            var hpWmiPath = ResolveHpWmiThermalPath();
            var wmiProfile = mode switch
            {
                PerformanceMode.Performance => "performance",
                PerformanceMode.Cool        => "quiet",
                _                           => "balanced"
            };
            if (SetHpWmiThermalProfile(wmiProfile))
            {
                LastPerformanceModeBackend = hpWmiPath != null
                    ? $"hp-wmi {Path.GetFileName(hpWmiPath)}"
                    : "hp-wmi profile";
                return true;
            }
            // Fall through to next backend on failure
        }

        // Priority 2: ACPI platform_profile
        if (HasAcpiProfileAccess)
        {
            var profileChoices = GetAcpiProfileChoices();
            var acpiProfile = mode switch
            {
                PerformanceMode.Performance => profileChoices.Contains("performance", StringComparer.OrdinalIgnoreCase)
                    ? "performance"
                    : "balanced-performance",
                PerformanceMode.Cool        => "low-power",
                _                           => "balanced"
            };
            if (SetAcpiProfile(acpiProfile))
            {
                LastPerformanceModeBackend = "ACPI platform_profile";
                return true;
            }
        }

        // Priority 3: Direct EC register (requires ec_sys / HasEcAccess)
        if (HasEcAccess)
        {
            var value = mode switch
            {
                PerformanceMode.Default     => PERF_MODE_DEFAULT,
                PerformanceMode.Performance => PERF_MODE_PERFORMANCE,
                PerformanceMode.Cool        => PERF_MODE_COOL,
                PerformanceMode.Balanced    => PERF_MODE_DEFAULT,
                _                           => PERF_MODE_DEFAULT
            };
            var success = WriteByte(REG_PERF_MODE, value);
            if (success)
            {
                LastPerformanceModeBackend = "ec_sys performance register";
            }

            return success;
        }

        return false;
    }
    
    /// <summary>
    /// Set TCC offset (0-15).
    /// Note: This may not work on all models via EC.
    /// </summary>
    public bool SetTccOffset(int offset)
    {
        // TCC offset is typically set via MSR, not EC
        // This is a placeholder for potential EC-based TCC control
        return false;
    }
    
    /// <summary>
    /// Set thermal power limit multiplier (0-5).
    /// </summary>
    public bool SetThermalPowerLimit(int level)
    {
        var value = (byte)Math.Clamp(level, 0, 5);
        return WriteByte(REG_THERMAL_POWER, value);
    }
    
    #endregion
}

public enum FanProfile
{
    Auto,
    Silent,
    Balanced,
    Gaming,
    Max
}

public enum PerformanceMode
{
    Default,
    Balanced,
    Performance,
    Cool
}
