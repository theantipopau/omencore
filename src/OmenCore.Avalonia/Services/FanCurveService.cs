namespace OmenCore.Avalonia.Services;

/// <summary>
/// Service for managing custom fan curves.
/// </summary>
public interface IFanCurveService
{
    /// <summary>
    /// Gets the current CPU fan curve.
    /// </summary>
    List<FanCurvePoint> GetCpuFanCurve();
    
    /// <summary>
    /// Gets the current GPU fan curve.
    /// </summary>
    List<FanCurvePoint> GetGpuFanCurve();
    
    /// <summary>
    /// Sets the CPU fan curve.
    /// </summary>
    void SetCpuFanCurve(IEnumerable<FanCurvePoint> curve);
    
    /// <summary>
    /// Sets the GPU fan curve.
    /// </summary>
    void SetGpuFanCurve(IEnumerable<FanCurvePoint> curve);
    
    /// <summary>
    /// Applies the current fan curves to hardware.
    /// </summary>
    Task ApplyAsync();
    
    /// <summary>
    /// Loads preset fan curves.
    /// </summary>
    (List<FanCurvePoint> cpu, List<FanCurvePoint> gpu) GetPreset(string name);
    
    /// <summary>
    /// Gets available preset names.
    /// </summary>
    IReadOnlyList<string> GetPresetNames();

    /// <summary>
    /// Saves or updates a named preset.
    /// </summary>
    void SavePreset(string name, IEnumerable<FanCurvePoint> cpuCurve, IEnumerable<FanCurvePoint> gpuCurve);
}

/// <summary>
/// Fan curve service implementation.
/// </summary>
public class FanCurveService : IFanCurveService
{
    private readonly IHardwareService _hardwareService;
    private List<FanCurvePoint> _cpuCurve;
    private List<FanCurvePoint> _gpuCurve;

    private static readonly Dictionary<string, (List<FanCurvePoint> cpu, List<FanCurvePoint> gpu)> Presets = new()
    {
        ["Silent"] = (
            new List<FanCurvePoint>
            {
                new(40, 0), new(50, 20), new(60, 35), new(70, 50), new(80, 70), new(90, 90), new(100, 100)
            },
            new List<FanCurvePoint>
            {
                new(40, 0), new(50, 20), new(60, 35), new(70, 45), new(80, 65), new(90, 85), new(100, 100)
            }
        ),
        ["Balanced"] = (
            new List<FanCurvePoint>
            {
                new(40, 30), new(50, 40), new(60, 50), new(70, 65), new(80, 80), new(90, 95), new(100, 100)
            },
            new List<FanCurvePoint>
            {
                new(40, 30), new(50, 40), new(60, 50), new(70, 60), new(80, 75), new(90, 90), new(100, 100)
            }
        ),
        ["Performance"] = (
            new List<FanCurvePoint>
            {
                new(40, 40), new(50, 50), new(60, 65), new(70, 80), new(80, 95), new(90, 100), new(100, 100)
            },
            new List<FanCurvePoint>
            {
                new(40, 40), new(50, 55), new(60, 70), new(70, 85), new(80, 100), new(90, 100), new(100, 100)
            }
        ),
        ["Aggressive"] = (
            new List<FanCurvePoint>
            {
                new(40, 50), new(50, 70), new(60, 85), new(70, 100), new(80, 100), new(90, 100), new(100, 100)
            },
            new List<FanCurvePoint>
            {
                new(40, 50), new(50, 70), new(60, 90), new(70, 100), new(80, 100), new(90, 100), new(100, 100)
            }
        )
    };

    public FanCurveService(IHardwareService hardwareService)
    {
        _hardwareService = hardwareService;
        var balanced = Presets["Balanced"];
        _cpuCurve = new List<FanCurvePoint>(balanced.cpu);
        _gpuCurve = new List<FanCurvePoint>(balanced.gpu);
    }

    public List<FanCurvePoint> GetCpuFanCurve() => new(_cpuCurve);
    public List<FanCurvePoint> GetGpuFanCurve() => new(_gpuCurve);

    public void SetCpuFanCurve(IEnumerable<FanCurvePoint> curve)
    {
        _cpuCurve = curve.OrderBy(p => p.Temperature).ToList();
    }

    public void SetGpuFanCurve(IEnumerable<FanCurvePoint> curve)
    {
        _gpuCurve = curve.OrderBy(p => p.Temperature).ToList();
    }

    public async Task ApplyAsync()
    {
        var status = await _hardwareService.GetStatusAsync();
        
        // Calculate fan speeds based on current temperatures
        var cpuFanSpeed = InterpolateFanSpeed(_cpuCurve, status.CpuTemperature);
        var gpuFanSpeed = InterpolateFanSpeed(_gpuCurve, status.GpuTemperature);
        
        await _hardwareService.SetCpuFanSpeedAsync(cpuFanSpeed);
        await _hardwareService.SetGpuFanSpeedAsync(gpuFanSpeed);
    }

    public (List<FanCurvePoint> cpu, List<FanCurvePoint> gpu) GetPreset(string name)
    {
        if (Presets.TryGetValue(name, out var preset))
        {
            return (new List<FanCurvePoint>(preset.cpu), new List<FanCurvePoint>(preset.gpu));
        }

        var balanced = Presets["Balanced"];
        return (new List<FanCurvePoint>(balanced.cpu), new List<FanCurvePoint>(balanced.gpu));
    }

    public IReadOnlyList<string> GetPresetNames() => Presets.Keys.ToList();

    public void SavePreset(string name, IEnumerable<FanCurvePoint> cpuCurve, IEnumerable<FanCurvePoint> gpuCurve)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name cannot be empty.", nameof(name));
        }

        var normalizedName = name.Trim();
        var cpu = cpuCurve.OrderBy(p => p.Temperature).Select(p => new FanCurvePoint(p.Temperature, Math.Clamp(p.FanSpeed, 0, 100))).ToList();
        var gpu = gpuCurve.OrderBy(p => p.Temperature).Select(p => new FanCurvePoint(p.Temperature, Math.Clamp(p.FanSpeed, 0, 100))).ToList();

        if (cpu.Count < 2 || gpu.Count < 2)
        {
            throw new InvalidOperationException("Fan curve presets require at least 2 points for CPU and GPU.");
        }

        Presets[normalizedName] = (cpu, gpu);
    }

    private static int InterpolateFanSpeed(List<FanCurvePoint> curve, double temperature)
    {
        if (curve.Count == 0) return 50;
        
        var temp = (int)temperature;
        
        // Below first point
        if (temp <= curve[0].Temperature)
            return curve[0].FanSpeed;
        
        // Above last point
        if (temp >= curve[^1].Temperature)
            return curve[^1].FanSpeed;
        
        // Find surrounding points and interpolate
        for (int i = 0; i < curve.Count - 1; i++)
        {
            if (temp >= curve[i].Temperature && temp <= curve[i + 1].Temperature)
            {
                var t = (double)(temp - curve[i].Temperature) / (curve[i + 1].Temperature - curve[i].Temperature);
                var speed = (int)(curve[i].FanSpeed + t * (curve[i + 1].FanSpeed - curve[i].FanSpeed));
                return Math.Clamp(speed, 0, 100);  // Safety: Prevent runaway from corrupted curve data (GitHub #49)
            }
        }
        
        return 50;
    }
}
