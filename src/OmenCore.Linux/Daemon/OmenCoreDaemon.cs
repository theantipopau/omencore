using System.Runtime.InteropServices;
using OmenCore.Linux.Config;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Daemon;

/// <summary>
/// OmenCore Linux Daemon - Background service for automatic hardware control.
/// 
/// Features:
/// - Automatic fan curve application
/// - Temperature monitoring
/// - Configuration file watching
/// - Signal handling (SIGTERM, SIGHUP)
/// - PID file management
/// - Graceful shutdown with settings restoration
/// - Low-overhead mode on battery (v2.7.0)
/// </summary>
public class OmenCoreDaemon : IDisposable
{
    private const string PidFilePath = "/var/run/omencore.pid";
    private const string LogFilePath = "/var/log/omencore.log";
    
    private readonly OmenCoreConfig _config;
    private readonly LinuxEcController _ec;
    private readonly LinuxHwMonController _hwmon;
    private readonly LinuxKeyboardController _keyboard;
    private readonly LinuxBatteryController _battery;
    private readonly FanCurveEngine? _fanCurveEngine;
    private readonly CancellationTokenSource _cts = new();
    
    private bool _isRunning;
    private bool _lowOverheadMode;
    private FileSystemWatcher? _configWatcher;
    
    public OmenCoreDaemon(OmenCoreConfig config)
    {
        _config = config;
        _ec = new LinuxEcController();
        _hwmon = new LinuxHwMonController();
        _keyboard = new LinuxKeyboardController();
        _battery = new LinuxBatteryController();
        
        // Initialize fan curve engine if custom curve is enabled
        if (_config.Fan.Profile == "custom" && _config.Fan.Curve.Enabled)
        {
            _fanCurveEngine = new FanCurveEngine(_ec, _hwmon, _config);
            _fanCurveEngine.OnLog += Log;
            _fanCurveEngine.OnSpeedChange += OnFanSpeedChange;
        }
    }
    
    /// <summary>
    /// Run the daemon (blocking).
    /// </summary>
    public async Task RunAsync()
    {
        if (_isRunning)
        {
            Log("Daemon is already running");
            return;
        }
        
        _isRunning = true;
        
        // Check prerequisites
        if (!LinuxEcController.CheckRootAccess())
        {
            Log("Error: Root privileges required");
            return;
        }
        
        if (!_ec.IsAvailable)
        {
            Log("Error: EC not available. Load ec_sys with write_support=1");
            return;
        }
        
        // Create PID file
        WritePidFile();
        
        // Setup signal handlers
        SetupSignalHandlers();
        
        // Setup config file watcher
        SetupConfigWatcher();
        
        Log("═══════════════════════════════════════════════════════════");
        Log("          OmenCore Linux Daemon v2.8.0 Started            ");
        Log("═══════════════════════════════════════════════════════════");
        Log($"Config: {(_config.Fan.Profile == "custom" ? "Custom fan curve" : $"Profile: {_config.Fan.Profile}")}");
        Log($"Poll interval: {_config.General.PollIntervalMs}ms");
        
        // Apply startup configuration
        if (_config.Startup.ApplyOnBoot)
        {
            await ApplyStartupConfigAsync();
        }
        
        // Start fan curve engine if enabled
        if (_fanCurveEngine != null)
        {
            await _fanCurveEngine.StartAsync();
        }
        
        // Main daemon loop
        try
        {
            await RunMainLoopAsync();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            await ShutdownAsync();
        }
    }
    
    /// <summary>
    /// Signal the daemon to stop.
    /// </summary>
    public void Stop()
    {
        Log("Shutdown signal received");
        _cts.Cancel();
    }
    
    /// <summary>
    /// Get effective poll interval based on low-overhead mode.
    /// </summary>
    private int GetEffectivePollInterval()
    {
        return _lowOverheadMode 
            ? _config.General.LowOverhead.PollIntervalMs 
            : _config.General.PollIntervalMs;
    }
    
    /// <summary>
    /// Check and update low-overhead mode based on battery state.
    /// </summary>
    private void UpdateLowOverheadMode()
    {
        if (!_config.General.LowOverhead.EnableOnBattery)
            return;
            
        var onBattery = _battery.IsOnBattery();
        
        if (onBattery != _lowOverheadMode)
        {
            _lowOverheadMode = onBattery;
            
            if (!_config.General.LowOverhead.ReduceLogging)
            {
                Log(_lowOverheadMode 
                    ? "Switched to low-overhead mode (on battery)" 
                    : "Switched to normal mode (on AC)");
            }
        }
    }
    
    private async Task RunMainLoopAsync()
    {
        var logCounter = 0;
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Check battery state for low-overhead mode
                UpdateLowOverheadMode();
                
                // If not using custom curve, just monitor and log
                if (_fanCurveEngine == null)
                {
                    var cpuTemp = _ec.GetCpuTemperature() ?? _hwmon.GetCpuTemperature() ?? 0;
                    var gpuTemp = _ec.GetGpuTemperature() ?? _hwmon.GetGpuTemperature() ?? 0;
                    var (fan1, fan2) = _ec.GetFanSpeeds();
                    
                    // Log periodically (less often in low-overhead mode)
                    logCounter++;
                    var logInterval = _lowOverheadMode ? 60 : 30;
                    var pollInterval = GetEffectivePollInterval();
                    
                    if (logCounter * pollInterval / 1000 >= logInterval)
                    {
                        if (!_lowOverheadMode || !_config.General.LowOverhead.ReduceLogging)
                        {
                            var batteryStr = _lowOverheadMode ? $" [Battery {_battery.GetBatteryPercentage()}%]" : "";
                            Log($"Status: CPU {cpuTemp}°C, GPU {gpuTemp}°C, Fans {fan1}/{fan2} RPM{batteryStr}");
                        }
                        logCounter = 0;
                    }
                }
                
                await Task.Delay(GetEffectivePollInterval(), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"Error in main loop: {ex.Message}");
            }
        }
    }
    
    private async Task ApplyStartupConfigAsync()
    {
        Log("Applying startup configuration...");
        
        // Apply fan profile
        if (_config.Fan.Profile != "custom")
        {
            var profile = _config.Fan.Profile.ToLower() switch
            {
                "auto" => FanProfile.Auto,
                "silent" => FanProfile.Silent,
                "balanced" => FanProfile.Balanced,
                "gaming" => FanProfile.Gaming,
                "max" => FanProfile.Max,
                _ => FanProfile.Auto
            };
            
            if (_ec.SetFanProfile(profile))
            {
                Log($"  Fan profile: {_config.Fan.Profile}");
            }
        }
        
        // Apply fan boost
        if (_config.Fan.Boost)
        {
            _ec.SetFanBoost(true);
            Log("  Fan boost: enabled");
        }
        
        // Apply performance mode
        var perfMode = _config.Performance.Mode.ToLower() switch
        {
            "performance" => PerformanceMode.Performance,
            "cool" => PerformanceMode.Cool,
            _ => PerformanceMode.Default
        };
        
        if (_ec.SetPerformanceMode(perfMode))
        {
            Log($"  Performance mode: {_config.Performance.Mode}");
        }
        
        // Apply keyboard settings
        if (_config.Keyboard.Enabled)
        {
            if (TryParseColor(_config.Keyboard.Color, out var r, out var g, out var b))
            {
                _keyboard.SetAllZonesColor(r, g, b);
                Log($"  Keyboard color: #{_config.Keyboard.Color}");
            }
            
            _keyboard.SetBrightness(_config.Keyboard.Brightness);
            Log($"  Keyboard brightness: {_config.Keyboard.Brightness}%");
        }
        
        Log("Startup configuration applied");
        await Task.CompletedTask;
    }
    
    private async Task ShutdownAsync()
    {
        Log("Shutting down...");
        
        // Stop fan curve engine
        _fanCurveEngine?.Stop();
        
        // Restore settings if configured
        if (_config.Startup.RestoreOnExit)
        {
            Log("Restoring default settings...");
            _ec.SetFanState(biosControl: true);
            _ec.SetFanBoost(false);
        }
        
        // Remove PID file
        RemovePidFile();
        
        // Stop config watcher
        _configWatcher?.Dispose();
        
        Log("Daemon stopped");
        await Task.CompletedTask;
    }
    
    private void WritePidFile()
    {
        try
        {
            var pid = Environment.ProcessId;
            File.WriteAllText(PidFilePath, pid.ToString());
            Log($"PID file created: {PidFilePath} ({pid})");
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not create PID file: {ex.Message}");
        }
    }
    
    private void RemovePidFile()
    {
        try
        {
            if (File.Exists(PidFilePath))
            {
                File.Delete(PidFilePath);
            }
        }
        catch { }
    }
    
    private void SetupSignalHandlers()
    {
        // Handle SIGTERM and SIGINT for graceful shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Stop();
        };
        
        // Handle SIGHUP for config reload (Linux only)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        }
    }
    
    private void SetupConfigWatcher()
    {
        var configDir = Path.GetDirectoryName(OmenCoreConfig.DefaultConfigPath);
        if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
        {
            return;
        }
        
        try
        {
            _configWatcher = new FileSystemWatcher(configDir, "config.toml")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            
            _configWatcher.Changed += (_, _) =>
            {
                Log("Configuration file changed - restart daemon to apply");
            };
            
            _configWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not setup config watcher: {ex.Message}");
        }
    }
    
    private void OnFanSpeedChange(int temp, int targetSpeed, int actualSpeed)
    {
        // Additional logging or actions on fan speed change
    }
    
    private static bool TryParseColor(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return false;
        
        try
        {
            r = Convert.ToByte(hex[..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"[{timestamp}] {message}";
        
        Console.WriteLine(line);
        
        // Also write to log file if running as service
        try
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch { }
    }
    
    public void Dispose()
    {
        Stop();
        _fanCurveEngine?.Dispose();
        _configWatcher?.Dispose();
        _cts.Dispose();
    }
}
