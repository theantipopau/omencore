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
    // EC sysfs path
    private const string EC_PATH = "/sys/kernel/debug/ec/ec0/io";
    
    // HP-WMI paths (for newer models like OMEN 16 2023+)
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
    private const string HP_WMI_THERMAL = "/sys/devices/platform/hp-wmi/thermal_profile";
    private const string HP_WMI_FAN_ALWAYS_ON = "/sys/devices/platform/hp-wmi/fan_always_on";
    private const string HP_WMI_FAN1 = "/sys/devices/platform/hp-wmi/fan1_output";
    private const string HP_WMI_FAN2 = "/sys/devices/platform/hp-wmi/fan2_output";
    
    // ACPI Platform Profile (kernel 5.18+, used by 2025+ OMEN models)
    private const string ACPI_PLATFORM_PROFILE = "/sys/firmware/acpi/platform_profile";
    private const string ACPI_PLATFORM_PROFILE_CHOICES = "/sys/firmware/acpi/platform_profile_choices";
    
    // HP-WMI hwmon paths (2025+ models use standard hwmon interface for fan control)
    // Discovered at runtime since hwmon number varies
    private string? _hwmonPwm1EnablePath;
    private string? _hwmonPwm2EnablePath;
    private string? _hwmonFan1InputPath;
    private string? _hwmonFan2InputPath;
    
    // DMI paths for model detection
    private const string DMI_PRODUCT_NAME = "/sys/class/dmi/id/product_name";
    private const string DMI_PRODUCT_NAME_ALT = "/sys/devices/virtual/dmi/id/product_name";
    
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
        "17t-ah0",    // OMEN MAX Gaming Laptop 17t-ah0xxx (2025, if exists)
        "17-ah0",     // OMEN MAX Gaming Laptop 17-ah0xxx (2025, if exists)
    };
    
    public bool IsAvailable { get; }
    public bool HasEcAccess { get; }
    public bool HasHpWmiAccess { get; }
    public bool HasAcpiProfileAccess { get; }
    public bool HasHwmonFanAccess { get; }
    public bool IsUnsafeEcModel { get; }
    public string AccessMethod { get; }
    public string? DetectedModel { get; }
    
    public LinuxEcController()
    {
        // Detect model first to determine safe access methods
        DetectedModel = DetectModelName();
        IsUnsafeEcModel = CheckUnsafeEcModel(DetectedModel);
        
        HasEcAccess = File.Exists(EC_PATH) && !IsUnsafeEcModel;
        
        // HP-WMI is available if directory exists AND has actual control files
        HasHpWmiAccess = Directory.Exists(HP_WMI_PATH) && (
            File.Exists(HP_WMI_THERMAL) ||
            File.Exists(HP_WMI_FAN_ALWAYS_ON) ||
            File.Exists(HP_WMI_FAN1) ||
            File.Exists(HP_WMI_FAN2));
        
        // ACPI platform profile (kernel 5.18+, used by 2025+ models)
        HasAcpiProfileAccess = File.Exists(ACPI_PLATFORM_PROFILE);
        
        // Discover hp-wmi hwmon interface (pwm control for 2025+ models)
        DiscoverHwmonFanControl();
        HasHwmonFanAccess = _hwmonPwm1EnablePath != null;
        
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
            Console.WriteLine($"⚠ Model '{DetectedModel}' detected - direct EC register access disabled (different register layout).");
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
    
    /// <summary>
    /// Check if the detected model has an unknown/unsafe EC register layout.
    /// 2025+ OMEN Max models have completely different EC registers - writing legacy
    /// addresses to them causes EC panic (caps lock blinking pattern).
    /// GitHub Issue #60: OMEN Max 16t-ah000 EC panic from writing to 0x34/0x35
    /// (these addresses contain serial number data on 2025 models, not fan registers).
    /// </summary>
    private static bool CheckUnsafeEcModel(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return false;
        
        var modelLower = modelName.ToLowerInvariant();
        return UnsafeEcModelPatterns.Any(pattern => modelLower.Contains(pattern.ToLowerInvariant()));
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
                
                var pwm2Enable = Path.Combine(hwmonDir, "pwm2_enable");
                if (File.Exists(pwm2Enable))
                    _hwmonPwm2EnablePath = pwm2Enable;
                
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
            ["is_root"] = CheckRootAccess()
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
        var wmiFiles = new[] { HP_WMI_THERMAL, HP_WMI_FAN_ALWAYS_ON, HP_WMI_FAN1, HP_WMI_FAN2 };
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
            using var fs = new FileStream(EC_PATH, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(address, SeekOrigin.Begin);
            var value = fs.ReadByte();
            return value >= 0 ? (byte)value : null;
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
            using var fs = new FileStream(EC_PATH, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            fs.Seek(address, SeekOrigin.Begin);
            fs.WriteByte(value);
            fs.Flush();
            return true;
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
        if (!HasHpWmiAccess || !File.Exists(HP_WMI_THERMAL))
            return false;

        try
        {
            var validProfiles = new[] { "quiet", "balanced", "performance", "extreme" };
            if (!validProfiles.Contains(profile.ToLower()))
                return false;

            File.WriteAllText(HP_WMI_THERMAL, profile.ToLower());
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
        if (!HasHpWmiAccess || !File.Exists(HP_WMI_THERMAL))
            return null;

        try
        {
            return File.ReadAllText(HP_WMI_THERMAL).Trim();
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
        return HasHpWmiAccess && File.Exists(HP_WMI_THERMAL);
    }
    
    #endregion
    
    #region ACPI Platform Profile (2025+ Models)
    
    /// <summary>
    /// Get available ACPI platform profiles.
    /// Returns profiles like: "low-power", "balanced", "performance"
    /// </summary>
    public string[] GetAcpiProfileChoices()
    {
        if (!HasAcpiProfileAccess || !File.Exists(ACPI_PLATFORM_PROFILE_CHOICES))
            return Array.Empty<string>();
        
        try
        {
            return File.ReadAllText(ACPI_PLATFORM_PROFILE_CHOICES).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return Array.Empty<string>(); }
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
            var choices = GetAcpiProfileChoices();
            if (choices.Length > 0 && !choices.Contains(profile, StringComparer.OrdinalIgnoreCase))
                return false;
            
            File.WriteAllText(ACPI_PLATFORM_PROFILE, profile.ToLowerInvariant());
            return true;
        }
        catch { return false; }
    }
    
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
        if (HasHpWmiAccess && File.Exists(HP_WMI_THERMAL))
        {
            return SetHpWmiThermalProfile(profile);
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
            FanProfile.Max => "performance",
            _ => "balanced"
        };
        
        try
        {
            File.WriteAllText(HP_WMI_THERMAL, profileValue);
            
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
        
        return success;
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
        if (HasHpWmiAccess && File.Exists(HP_WMI_THERMAL))
        {
            return RestoreAutoModeViaHpWmi();
        }
        
        // Try ACPI/hwmon path (2025+ OMEN Max models)
        if (HasAcpiProfileAccess || HasHwmonFanAccess)
        {
            bool success = false;
            if (HasAcpiProfileAccess)
                success = SetAcpiProfile("balanced");
            if (HasHwmonFanAccess)
                success = SetHwmonPwmEnable(2) || success; // 2 = BIOS auto
            return success;
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
            if (File.Exists(HP_WMI_THERMAL))
            {
                File.WriteAllText(HP_WMI_THERMAL, "balanced");
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
    /// </summary>
    public PerformanceMode GetPerformanceMode()
    {
        var value = ReadByte(REG_PERF_MODE);
        return value switch
        {
            PERF_MODE_DEFAULT => PerformanceMode.Default,
            PERF_MODE_PERFORMANCE => PerformanceMode.Performance,
            PERF_MODE_COOL => PerformanceMode.Cool,
            _ => PerformanceMode.Balanced
        };
    }
    
    /// <summary>
    /// Set performance mode.
    /// </summary>
    public bool SetPerformanceMode(PerformanceMode mode)
    {
        var value = mode switch
        {
            PerformanceMode.Default => PERF_MODE_DEFAULT,
            PerformanceMode.Performance => PERF_MODE_PERFORMANCE,
            PerformanceMode.Cool => PERF_MODE_COOL,
            PerformanceMode.Balanced => PERF_MODE_DEFAULT,
            _ => PERF_MODE_DEFAULT
        };
        
        return WriteByte(REG_PERF_MODE, value);
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
