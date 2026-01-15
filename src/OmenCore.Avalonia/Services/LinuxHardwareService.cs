using System.Runtime.InteropServices;

namespace OmenCore.Avalonia.Services;

/// <summary>
/// Linux implementation of hardware service using sysfs and ACPI interfaces.
/// </summary>
public class LinuxHardwareService : IHardwareService, IDisposable
{
    private readonly System.Timers.Timer _pollingTimer;
    private HardwareStatus _lastStatus = new();
    private SystemCapabilities? _capabilities;
    private bool _disposed;

    // HP OMEN specific paths
    private const string HP_WMI_PATH = "/sys/devices/platform/hp-wmi";
    private const string OMEN_THERMAL_PATH = "/sys/devices/platform/hp-wmi/thermal_profile";
    private const string HWMON_BASE = "/sys/class/hwmon";
    private const string POWER_SUPPLY = "/sys/class/power_supply";
    private const string BACKLIGHT_PATH = "/sys/class/leds/hp::kbd_backlight";
    
    public event EventHandler<HardwareStatus>? StatusChanged;

    public LinuxHardwareService()
    {
        _pollingTimer = new System.Timers.Timer(1000);
        _pollingTimer.Elapsed += async (s, e) => await PollHardwareAsync();
        _pollingTimer.Start();
    }

    private async Task PollHardwareAsync()
    {
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
    }

    private static bool HasStatusChanged(HardwareStatus old, HardwareStatus current)
    {
        return Math.Abs(old.CpuTemperature - current.CpuTemperature) > 1 ||
               Math.Abs(old.GpuTemperature - current.GpuTemperature) > 1 ||
               old.CpuFanRpm != current.CpuFanRpm ||
               old.GpuFanRpm != current.GpuFanRpm;
    }

    public async Task<HardwareStatus> GetStatusAsync()
    {
        var status = new HardwareStatus();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Return mock data for testing on Windows
            return GetMockStatus();
        }

        // Read CPU temperature from hwmon
        status.CpuTemperature = await ReadTemperatureAsync("coretemp") / 1000.0;
        
        // Read GPU temperature (NVIDIA or AMD)
        status.GpuTemperature = await ReadGpuTemperatureAsync() / 1000.0;
        
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
        if (_capabilities != null)
            return _capabilities;

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
                ModelName = "HP OMEN 16 (Mock)",
                CpuName = "AMD Ryzen 9 7945HX",
                GpuName = "NVIDIA GeForce RTX 4070"
            };
        }

        // Check for HP OMEN WMI
        _capabilities.SupportsFanControl = File.Exists(OMEN_THERMAL_PATH);
        
        // Check keyboard backlight
        _capabilities.HasKeyboardBacklight = Directory.Exists(BACKLIGHT_PATH);
        
        // TODO: Detect RGB capabilities
        _capabilities.HasFourZoneRgb = File.Exists("/sys/class/leds/hp::kbd_backlight/color");
        
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

    public async Task<PerformanceMode> GetPerformanceModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PerformanceMode.Balanced;

        try
        {
            if (File.Exists(OMEN_THERMAL_PATH))
            {
                var profile = await File.ReadAllTextAsync(OMEN_THERMAL_PATH);
                return profile.Trim().ToLower() switch
                {
                    "quiet" => PerformanceMode.Quiet,
                    "balanced" or "balanced-performance" => PerformanceMode.Balanced,
                    "performance" => PerformanceMode.Performance,
                    _ => PerformanceMode.Balanced
                };
            }
        }
        catch { }

        return PerformanceMode.Balanced;
    }

    public async Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var profile = mode switch
        {
            PerformanceMode.Quiet => "quiet",
            PerformanceMode.Balanced => "balanced",
            PerformanceMode.Performance => "performance",
            _ => "balanced"
        };

        try
        {
            await File.WriteAllTextAsync(OMEN_THERMAL_PATH, profile);
        }
        catch (UnauthorizedAccessException)
        {
            // Need root permissions - could call pkexec
            throw new InvalidOperationException("Root permissions required to change performance mode");
        }
    }

    public async Task SetCpuFanSpeedAsync(int percentage)
    {
        // HP OMEN fan control via WMI or EC
        // This typically requires a kernel driver like hp-omen-helper
        await Task.CompletedTask;
    }

    public async Task SetGpuFanSpeedAsync(int percentage)
    {
        // Similar to CPU fan
        await Task.CompletedTask;
    }

    public async Task<string> GetGpuModeAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "hybrid";

        // Check for NVIDIA optimus/prime status
        try
        {
            if (File.Exists("/proc/driver/nvidia/params"))
            {
                // Check PRIME profile
                var primeProfile = await RunCommandAsync("prime-select", "query");
                return primeProfile.Trim().ToLower() switch
                {
                    "nvidia" => "discrete",
                    "intel" or "amd" => "integrated",
                    "on-demand" => "hybrid",
                    _ => "hybrid"
                };
            }
        }
        catch { }

        return "hybrid";
    }

    public async Task SetGpuModeAsync(string mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var primeMode = mode.ToLower() switch
        {
            "discrete" => "nvidia",
            "integrated" => "intel",
            "hybrid" => "on-demand",
            _ => "on-demand"
        };

        try
        {
            await RunCommandAsync("pkexec", $"prime-select {primeMode}");
        }
        catch
        {
            throw new InvalidOperationException("Failed to switch GPU mode. Please reboot and try again.");
        }
    }

    public async Task SetKeyboardBrightnessAsync(int brightness)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var maxPath = Path.Combine(BACKLIGHT_PATH, "max_brightness");
        var brightnessPath = Path.Combine(BACKLIGHT_PATH, "brightness");

        try
        {
            var maxBrightness = int.Parse(await File.ReadAllTextAsync(maxPath));
            var scaledBrightness = (int)(brightness / 100.0 * maxBrightness);
            await File.WriteAllTextAsync(brightnessPath, scaledBrightness.ToString());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set keyboard brightness: {ex.Message}");
        }
    }

    public async Task SetKeyboardColorAsync(byte r, byte g, byte b)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var colorPath = Path.Combine(BACKLIGHT_PATH, "color");

        try
        {
            // Format depends on the driver - typically RGB hex or space-separated
            var colorValue = $"{r:X2}{g:X2}{b:X2}";
            await File.WriteAllTextAsync(colorPath, colorValue);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set keyboard color: {ex.Message}");
        }
    }

    #region Private Helpers

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
        // Try NVIDIA first via nvidia-smi
        try
        {
            var nvidiaTemp = await RunCommandAsync("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader,nounits");
            if (int.TryParse(nvidiaTemp.Trim(), out var temp) && temp > 0)
                return temp * 1000;
        }
        catch { }

        // Try AMD via hwmon
        foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
        {
            try
            {
                var namePath = Path.Combine(hwmon, "name");
                if (!File.Exists(namePath)) continue;
                
                var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                if (name == "amdgpu" || name.Contains("radeon") || name.Contains("gpu"))
                {
                    // Try different temperature files for AMD GPUs
                    var tempFiles = new[] { "temp1_input", "temp2_input", "edge", "junction" };
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

        // Try Intel integrated GPU
        foreach (var hwmon in Directory.GetDirectories(HWMON_BASE))
        {
            try
            {
                var namePath = Path.Combine(hwmon, "name");
                if (!File.Exists(namePath)) continue;
                
                var name = (await File.ReadAllTextAsync(namePath)).Trim().ToLower();
                if (name.Contains("i915") || name.Contains("intel"))
                {
                    var tempPath = Path.Combine(hwmon, "temp1_input");
                    if (File.Exists(tempPath))
                    {
                        var tempStr = await File.ReadAllTextAsync(tempPath);
                        if (int.TryParse(tempStr.Trim(), out var temp) && temp > 0)
                            return temp;
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
        // Check for NVIDIA
        if (File.Exists("/proc/driver/nvidia/version"))
            return true;

        // Check for AMD discrete
        try
        {
            var lspci = await RunCommandAsync("lspci", "-d ::0302");
            return !string.IsNullOrWhiteSpace(lspci);
        }
        catch { }

        return false;
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
        // Try NVIDIA nvidia-smi
        try
        {
            var gpuName = await RunCommandAsync("nvidia-smi", "--query-gpu=gpu_name --format=csv,noheader");
            if (!string.IsNullOrWhiteSpace(gpuName) && !gpuName.Contains("error", StringComparison.OrdinalIgnoreCase))
                return gpuName.Trim();
        }
        catch { }

        // Try lspci for discrete GPUs (VGA compatible controller or 3D controller)
        try
        {
            // 0300 = VGA compatible controller, 0302 = 3D controller (discrete GPUs)
            var lspciOutput = await RunCommandAsync("lspci", "");
            var lines = lspciOutput.Split('\n');
            
            foreach (var line in lines)
            {
                var lower = line.ToLower();
                // Look for NVIDIA or AMD discrete GPUs
                if ((lower.Contains("vga") || lower.Contains("3d")) && 
                    (lower.Contains("nvidia") || lower.Contains("radeon") || lower.Contains("amd")))
                {
                    // Extract GPU name from line like "01:00.0 VGA compatible controller: NVIDIA Corporation GA104 [GeForce RTX 3070]"
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < line.Length - 1)
                    {
                        var colonIndex2 = line.IndexOf(':', colonIndex + 1);
                        if (colonIndex2 > 0 && colonIndex2 < line.Length - 1)
                        {
                            return line.Substring(colonIndex2 + 1).Trim();
                        }
                    }
                }
            }
            
            // Fallback: Any VGA/3D device
            foreach (var line in lines)
            {
                if (line.ToLower().Contains("vga") || line.ToLower().Contains("3d"))
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var colonIndex2 = line.IndexOf(':', colonIndex + 1);
                        if (colonIndex2 > 0)
                            return line.Substring(colonIndex2 + 1).Trim();
                    }
                }
            }
        }
        catch { }

        // Try reading from DRM subsystem
        try
        {
            var drmPath = "/sys/class/drm";
            if (Directory.Exists(drmPath))
            {
                var cards = Directory.GetDirectories(drmPath).Where(d => Path.GetFileName(d).StartsWith("card"));
                foreach (var card in cards)
                {
                    var devicePath = Path.Combine(card, "device");
                    if (Directory.Exists(devicePath))
                    {
                        // Try to read vendor/device info
                        var vendorPath = Path.Combine(devicePath, "vendor");
                        if (File.Exists(vendorPath))
                        {
                            var vendor = (await File.ReadAllTextAsync(vendorPath)).Trim();
                            if (vendor == "0x10de") return "NVIDIA GPU";
                            if (vendor == "0x1002") return "AMD Radeon GPU";
                            if (vendor == "0x8086") return "Intel Integrated Graphics";
                        }
                    }
                }
            }
        }
        catch { }

        return "Unknown GPU";
    }

    private static async Task<string> RunCommandAsync(string command, string args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _pollingTimer.Stop();
            _pollingTimer.Dispose();
            _disposed = true;
        }
    }
}
