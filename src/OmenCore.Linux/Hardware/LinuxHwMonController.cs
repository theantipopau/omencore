namespace OmenCore.Linux.Hardware;

/// <summary>
/// Linux hwmon sensor interface for temperature monitoring.
/// 
/// Reads from /sys/class/hwmon/* to get CPU and GPU temperatures.
/// This is preferred over EC-based temperature reading when available.
/// 
/// Enhanced in v2.7.0 (#24):
/// - Multiple fallback sensor detection
/// - Sensor health tracking
/// - Cached paths for low-overhead mode
/// </summary>
public class LinuxHwMonController
{
    private const string HWMON_PATH = "/sys/class/hwmon";
    private const string THERMAL_ZONE_PATH = "/sys/class/thermal";
    private const int MinPlausibleTemperatureC = 1;
    private const int MaxPlausibleTemperatureC = 125;
    
    private readonly List<string> _cpuSensorPaths = new();
    private readonly List<(int Rank, string Path)> _cpuSensorCandidates = new();
    private readonly List<string> _gpuSensorPaths = new();
    private readonly Dictionary<string, int> _sensorFailureCount = new();
    private readonly int _maxFailuresBeforeSkip = 5;
    private DateTime _lastFullScan = DateTime.MinValue;
    private readonly TimeSpan _rescanInterval = TimeSpan.FromMinutes(5);
    
    public bool HasCpuSensor => _cpuSensorPaths.Count > 0;
    public bool HasGpuSensor => _gpuSensorPaths.Count > 0;
    public int AvailableSensorCount => _cpuSensorPaths.Count + _gpuSensorPaths.Count;
    
    public LinuxHwMonController()
    {
        DiscoverSensors();
    }
    
    /// <summary>
    /// Discover all available sensors (hwmon + thermal zones).
    /// </summary>
    public void DiscoverSensors()
    {
        _cpuSensorPaths.Clear();
        _cpuSensorCandidates.Clear();
        _gpuSensorPaths.Clear();
        _lastFullScan = DateTime.Now;
        
        DiscoverHwmonSensors();
        DiscoverThermalZones();

        // Readings take the first readable path, so order must be by sensor quality, not by
        // sysfs enumeration order. GitHub #214 (8BCA, Ryzen 7940HS): hwmon listed a dead
        // acpitz zone stuck at +20.0°C ahead of k10temp, so the fan curve and the thermal
        // emergency both saw 20°C while the CPU sat at 99°C. OrderBy is stable, so ties keep
        // discovery order.
        foreach (var candidate in _cpuSensorCandidates.OrderBy(c => c.Rank))
        {
            if (!_cpuSensorPaths.Contains(candidate.Path))
                _cpuSensorPaths.Add(candidate.Path);
        }
    }

    /// <summary>
    /// Preference rank for a CPU temperature source (lower is better). Dedicated CPU drivers
    /// read the die sensor directly; generic ACPI thermal zones (acpitz) are firmware-defined,
    /// often not the CPU at all, and on some boards frozen at a constant value, so they are
    /// only a last resort.
    /// </summary>
    public static int GetCpuSensorRank(string sensorName)
    {
        var name = sensorName.Trim().ToLowerInvariant();
        if (name.Contains("coretemp") || name.Contains("k10temp") || name.Contains("zenpower"))
            return 0;
        if (name.Contains("x86_pkg"))
            return 1;
        if (name.Contains("acpitz"))
            return 5;
        if (name.Contains("hp") || name.Contains("thinkpad"))
            return 2;
        if (name.Contains("amd_energy"))
            return 3;
        return 4;
    }
    
    private void DiscoverHwmonSensors()
    {
        if (!Directory.Exists(HWMON_PATH))
            return;
            
        foreach (var hwmonDir in Directory.GetDirectories(HWMON_PATH))
        {
            try
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (!File.Exists(namePath))
                    continue;
                    
                var name = File.ReadAllText(namePath).Trim().ToLower();
                
                // CPU temperature sensors (in priority order)
                if (name.Contains("coretemp") || name.Contains("k10temp") || 
                    name.Contains("zenpower") || name.Contains("amd_energy") ||
                    name.Contains("thinkpad") || name.Contains("hp") ||
                    name.Contains("acpitz"))
                {
                    AddCpuSensorPaths(hwmonDir, GetCpuSensorRank(name));
                }
                
                // GPU temperature sensors (in priority order)
                if (name.Contains("nvidia") || name.Contains("nouveau") || 
                    name.Contains("amdgpu") || name.Contains("radeon"))
                {
                    AddGpuSensorPaths(hwmonDir);
                }
            }
            catch
            {
                // Ignore errors during discovery
            }
        }
    }
    
    private void DiscoverThermalZones()
    {
        if (!Directory.Exists(THERMAL_ZONE_PATH))
            return;
            
        foreach (var zoneDir in Directory.GetDirectories(THERMAL_ZONE_PATH, "thermal_zone*"))
        {
            try
            {
                var typePath = Path.Combine(zoneDir, "type");
                var tempPath = Path.Combine(zoneDir, "temp");
                
                if (!File.Exists(typePath) || !File.Exists(tempPath))
                    continue;
                    
                var type = File.ReadAllText(typePath).Trim().ToLower();
                
                // CPU-related thermal zones
                if (type.Contains("x86_pkg") || type.Contains("acpitz") || 
                    type.Contains("cpu") || type.Contains("soc"))
                {
                    _cpuSensorCandidates.Add((GetCpuSensorRank(type), tempPath));
                }
                
                // GPU thermal zones (less common but worth checking)
                if (type.Contains("gpu") || type.Contains("nvidia"))
                {
                    if (!_gpuSensorPaths.Contains(tempPath))
                        _gpuSensorPaths.Add(tempPath);
                }
            }
            catch { }
        }
    }
    
    private void AddCpuSensorPaths(string hwmonDir, int rank)
    {
        // Try temp files in priority order
        foreach (var suffix in new[] { "temp1_input", "temp2_input", "temp3_input" })
        {
            var path = Path.Combine(hwmonDir, suffix);
            if (File.Exists(path))
            {
                _cpuSensorCandidates.Add((rank, path));
            }
        }
    }
    
    private void AddGpuSensorPaths(string hwmonDir)
    {
        foreach (var suffix in new[] { "temp1_input", "temp2_input" })
        {
            var path = Path.Combine(hwmonDir, suffix);
            if (File.Exists(path) && !_gpuSensorPaths.Contains(path))
            {
                _gpuSensorPaths.Add(path);
            }
        }
    }
    
    /// <summary>
    /// Get CPU temperature with fallback to multiple sources.
    /// </summary>
    public int? GetCpuTemperature()
    {
        return GetCpuTemperatureReading()?.Temperature;
    }
    
    /// <summary>
    /// Get GPU temperature with fallback to multiple sources.
    /// </summary>
    public int? GetGpuTemperature()
    {
        return GetGpuTemperatureReading()?.Temperature;
    }

    public LinuxTemperatureReading? GetCpuTemperatureReading()
    {
        return GetTemperatureReading(_cpuSensorPaths);
    }

    public LinuxTemperatureReading? GetGpuTemperatureReading()
    {
        return GetTemperatureReading(_gpuSensorPaths);
    }
    
    private bool ShouldSkipSensor(string path)
    {
        return _sensorFailureCount.TryGetValue(path, out var count) && count >= _maxFailuresBeforeSkip;
    }

    private LinuxTemperatureReading? GetTemperatureReading(List<string> sensorPaths)
    {
        if (DateTime.Now - _lastFullScan > _rescanInterval)
        {
            DiscoverSensors();
        }

        foreach (var path in sensorPaths)
        {
            if (ShouldSkipSensor(path))
            {
                continue;
            }

            var temp = ReadTemperatureFile(path);
            if (temp.HasValue)
            {
                ResetSensorFailure(path);
                return new LinuxTemperatureReading
                {
                    Temperature = temp.Value,
                    Source = GetSensorSource(path),
                    Path = path
                };
            }

            RecordSensorFailure(path);
        }

        return null;
    }

    private static string GetSensorSource(string path)
    {
        if (path.StartsWith(HWMON_PATH, StringComparison.Ordinal))
        {
            return "hwmon";
        }

        if (path.StartsWith(THERMAL_ZONE_PATH, StringComparison.Ordinal))
        {
            return "thermal-zone";
        }

        return "sysfs";
    }
    
    private void RecordSensorFailure(string path)
    {
        _sensorFailureCount.TryGetValue(path, out var count);
        _sensorFailureCount[path] = count + 1;
    }
    
    private void ResetSensorFailure(string path)
    {
        _sensorFailureCount[path] = 0;
    }
    
    /// <summary>
    /// Read temperature from a file. Handles both hwmon (millidegrees) 
    /// and thermal_zone (millidegrees) formats.
    /// </summary>
    private int? ReadTemperatureFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
                
            var content = File.ReadAllText(path).Trim();
            if (int.TryParse(content, out var value))
            {
                // Both hwmon and thermal_zone report millidegrees
                var temperature = value / 1000;
                return temperature >= MinPlausibleTemperatureC && temperature <= MaxPlausibleTemperatureC
                    ? temperature
                    : null;
            }
        }
        catch
        {
            // Ignore read errors
        }
        
        return null;
    }
    
    /// <summary>
    /// Get all available temperature sensors.
    /// </summary>
    public IEnumerable<(string Name, string Path, int Temperature)> GetAllSensors()
    {
        var results = new List<(string Name, string Path, int Temperature)>();
        
        if (!Directory.Exists(HWMON_PATH))
            return results;
            
        foreach (var hwmonDir in Directory.GetDirectories(HWMON_PATH))
        {
            string? name = null;
            try
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (File.Exists(namePath))
                    name = File.ReadAllText(namePath).Trim();
            }
            catch { }
            
            // Find temp files
            try
            {
                foreach (var tempFile in Directory.GetFiles(hwmonDir, "temp*_input"))
                {
                    try
                    {
                        var content = File.ReadAllText(tempFile).Trim();
                        if (int.TryParse(content, out var millidegrees))
                        {
                            var label = Path.GetFileNameWithoutExtension(tempFile);
                            results.Add((name ?? "unknown", label, millidegrees / 1000));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        return results;
    }
}
