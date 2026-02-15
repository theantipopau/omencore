using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services
{
    /// <summary>
    /// Service that applies keyboard RGB colors based on CPU/GPU temperature.
    /// OmenMon-style temperature visualization - cool blue to hot red gradient.
    /// 
    /// Temperature zones:
    /// - Cool (≤40°C): Blue (#0000FF)
    /// - Medium (40-70°C): Gradient from blue through green/yellow
    /// - Hot (70-85°C): Orange/Red gradient
    /// - Critical (>85°C): Pulsing red warning
    /// </summary>
    public class TemperatureRgbService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly KeyboardLightingService? _keyboardService;
        private readonly HpWmiBios? _wmiBios;
        private readonly RgbLightingSettingsService _settingsService;
        private readonly FanService? _fanService;
        
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private bool _disposed;
        
        // Settings cache
        private bool _enabled;
        private double _lastCpuTemp;
        private double _lastGpuTemp;
        private Color _lastAppliedColor = Color.Black;
        
        // Update rate limiting
        private DateTime _lastColorUpdate = DateTime.MinValue;
        private const int MinUpdateIntervalMs = 500; // Don't update faster than 2Hz
        
        public bool IsEnabled => _enabled;
        public bool IsRunning => _isRunning;
        
        public TemperatureRgbService(
            LoggingService logging,
            RgbLightingSettingsService settingsService,
            KeyboardLightingService? keyboardService = null,
            HpWmiBios? wmiBios = null,
            FanService? fanService = null)
        {
            _logging = logging;
            _settingsService = settingsService;
            _keyboardService = keyboardService;
            _wmiBios = wmiBios;
            _fanService = fanService;
        }
        
        /// <summary>
        /// Start temperature-based RGB monitoring and application.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _enabled = true;
            _cts = new CancellationTokenSource();
            _isRunning = true;
            
            _ = Task.Run(() => MonitorLoopAsync(_cts.Token));
            _logging.Info("Temperature-based RGB lighting started");
        }
        
        /// <summary>
        /// Stop temperature-based RGB monitoring.
        /// </summary>
        public void Stop()
        {
            _enabled = false;
            _isRunning = false;
            _cts?.Cancel();
            _logging.Info("Temperature-based RGB lighting stopped");
        }
        
        /// <summary>
        /// Apply temperature-based color immediately for given temps.
        /// Useful for testing or manual triggering.
        /// </summary>
        public async Task ApplyForTemperature(double cpuTemp, double gpuTemp)
        {
            var settings = _settingsService.GetSettings();
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            var color = CalculateTemperatureColor(maxTemp, settings);
            
            await ApplyColorToKeyboard(color);
            _lastCpuTemp = cpuTemp;
            _lastGpuTemp = gpuTemp;
            _lastAppliedColor = color;
        }
        
        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Get current temperatures from fan service if available
                    double cpuTemp = 0, gpuTemp = 0;
                    
                    if (_fanService?.ThermalProvider != null)
                    {
                        var temps = _fanService.ThermalProvider.ReadTemperatures();
                        foreach (var t in temps)
                        {
                            if (t.Sensor.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                                cpuTemp = Math.Max(cpuTemp, t.Celsius);
                            else if (t.Sensor.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                                gpuTemp = Math.Max(gpuTemp, t.Celsius);
                        }
                    }
                    else if (_wmiBios != null)
                    {
                        // Fallback to direct WMI BIOS reads
                        var temps = _wmiBios.GetBothTemperatures();
                        if (temps.HasValue)
                        {
                            cpuTemp = temps.Value.cpuTemp;
                            gpuTemp = temps.Value.gpuTemp;
                        }
                    }
                    
                    // Check if we should update
                    if (ShouldUpdateColor(cpuTemp, gpuTemp))
                    {
                        await ApplyForTemperature(cpuTemp, gpuTemp);
                    }
                    
                    // Poll every 2 seconds
                    await Task.Delay(2000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Temperature RGB update error: {ex.Message}");
                    await Task.Delay(5000, ct);
                }
            }
        }
        
        private bool ShouldUpdateColor(double cpuTemp, double gpuTemp)
        {
            // Rate limit updates
            if ((DateTime.Now - _lastColorUpdate).TotalMilliseconds < MinUpdateIntervalMs)
                return false;
            
            // Update if temperature changed significantly (>2°C)
            var maxTemp = Math.Max(cpuTemp, gpuTemp);
            var lastMaxTemp = Math.Max(_lastCpuTemp, _lastGpuTemp);
            
            return Math.Abs(maxTemp - lastMaxTemp) > 2.0;
        }
        
        /// <summary>
        /// Calculate the color for a given temperature based on settings.
        /// Uses smooth gradient interpolation for natural transitions.
        /// </summary>
        private Color CalculateTemperatureColor(double temperature, RgbLightingSettings settings)
        {
            // Parse colors from settings
            var colorLow = ParseHexColor(settings.CpuTempColorLow);
            var colorMedium = ParseHexColor(settings.CpuTempColorMedium);
            var colorHigh = ParseHexColor(settings.CpuTempColorHigh);
            
            // Get thresholds
            var thresholdLow = settings.CpuTempThresholdLow;
            var thresholdMedium = settings.CpuTempThresholdMedium;
            var thresholdHigh = settings.CpuTempThresholdHigh;
            
            // Calculate color based on temperature zone
            if (temperature <= thresholdLow)
            {
                return colorLow; // Cool - blue
            }
            else if (temperature <= thresholdMedium)
            {
                // Gradient from low to medium
                var t = (temperature - thresholdLow) / (thresholdMedium - thresholdLow);
                return LerpColor(colorLow, colorMedium, t);
            }
            else if (temperature <= thresholdHigh)
            {
                // Gradient from medium to high
                var t = (temperature - thresholdMedium) / (thresholdHigh - thresholdMedium);
                return LerpColor(colorMedium, colorHigh, t);
            }
            else
            {
                // Above high threshold - solid red (could add pulsing effect here)
                return colorHigh;
            }
        }
        
        /// <summary>
        /// Linear interpolation between two colors.
        /// </summary>
        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t)
            );
        }
        
        /// <summary>
        /// Apply a color to the keyboard (all zones).
        /// </summary>
        private async Task ApplyColorToKeyboard(Color color)
        {
            _lastColorUpdate = DateTime.Now;
            
            // Try WMI BIOS first (direct, most reliable)
            if (_wmiBios?.IsAvailable == true)
            {
                var colors = new Color[] { color, color, color, color };
                var colorTable = new byte[12];
                for (int i = 0; i < 4; i++)
                {
                    colorTable[i * 3] = colors[i].R;
                    colorTable[i * 3 + 1] = colors[i].G;
                    colorTable[i * 3 + 2] = colors[i].B;
                }
                
                if (_wmiBios.SetColorTable(colorTable))
                {
                    _logging.Info($"Temperature RGB applied: #{color.R:X2}{color.G:X2}{color.B:X2} " +
                        $"(CPU: {_lastCpuTemp:F0}°C, GPU: {_lastGpuTemp:F0}°C)");
                    return;
                }
            }
            
            // Fallback to KeyboardLightingService
            if (_keyboardService?.IsAvailable == true)
            {
                var colors = new Color[] { color, color, color, color };
                await _keyboardService.SetAllZoneColors(colors);
                _logging.Info($"Temperature RGB applied via KLS: #{color.R:X2}{color.G:X2}{color.B:X2}");
            }
        }
        
        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.Blue;
            
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(0, 2), 16),
                        Convert.ToInt32(hex.Substring(2, 2), 16),
                        Convert.ToInt32(hex.Substring(4, 2), 16)
                    );
                }
            }
            catch { }
            
            return Color.Blue;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Stop();
            _cts?.Dispose();
        }
    }
}
