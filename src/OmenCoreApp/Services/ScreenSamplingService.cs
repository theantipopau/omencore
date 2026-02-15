using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services.Rgb;

namespace OmenCore.Services
{
    /// <summary>
    /// Service that samples screen colors for ambient RGB lighting.
    /// Captures colors from screen edges and applies them to RGB devices.
    /// </summary>
    public class ScreenSamplingService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly RgbManager _rgbManager;
        private readonly KeyboardLightingService? _keyboardLightingService;
        
        private Timer? _samplingTimer;
        private bool _isRunning;
        private bool _isDisposed;
        private int _sampleIntervalMs = 100;
        private int _smoothingFactor = 5; // Number of samples to average
        private Color[] _colorHistory;
        private int _historyIndex;
        private Color _lastAppliedColor;
        private int _changeThreshold = 20; // Min RGB difference to trigger update
        
        /// <summary>
        /// Whether ambient sampling is currently active.
        /// </summary>
        public bool IsRunning => _isRunning;
        
        /// <summary>
        /// Sampling interval in milliseconds.
        /// </summary>
        public int SampleIntervalMs
        {
            get => _sampleIntervalMs;
            set => _sampleIntervalMs = Math.Clamp(value, 50, 1000);
        }
        
        /// <summary>
        /// Number of samples to average for smoothing.
        /// </summary>
        public int SmoothingFactor
        {
            get => _smoothingFactor;
            set
            {
                _smoothingFactor = Math.Clamp(value, 1, 20);
                _colorHistory = new Color[_smoothingFactor];
                _historyIndex = 0;
            }
        }
        
        /// <summary>
        /// Minimum RGB difference threshold to trigger an update.
        /// </summary>
        public int ChangeThreshold
        {
            get => _changeThreshold;
            set => _changeThreshold = Math.Clamp(value, 0, 100);
        }
        
        /// <summary>
        /// Last sampled average color.
        /// </summary>
        public Color CurrentColor { get; private set; }
        
        /// <summary>
        /// Event fired when sampled color changes.
        /// </summary>
        public event EventHandler<Color>? ColorChanged;

        public ScreenSamplingService(
            LoggingService logging,
            RgbManager rgbManager,
            KeyboardLightingService? keyboardLightingService = null)
        {
            _logging = logging;
            _rgbManager = rgbManager;
            _keyboardLightingService = keyboardLightingService;
            _colorHistory = new Color[_smoothingFactor];
        }

        /// <summary>
        /// Start ambient screen sampling.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _samplingTimer = new Timer(SampleScreen, null, 0, _sampleIntervalMs);
            _logging.Info($"Screen sampling started (interval={_sampleIntervalMs}ms, smoothing={_smoothingFactor})");
        }

        /// <summary>
        /// Stop ambient screen sampling.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _samplingTimer?.Dispose();
            _samplingTimer = null;
            _logging.Info("Screen sampling stopped");
        }

        private void SampleScreen(object? state)
        {
            if (!_isRunning || _isDisposed) return;
            
            try
            {
                var sampledColor = CaptureAverageScreenColor();
                
                // Add to history for smoothing
                _colorHistory[_historyIndex] = sampledColor;
                _historyIndex = (_historyIndex + 1) % _smoothingFactor;
                
                // Calculate average
                var avgColor = CalculateAverageColor();
                CurrentColor = avgColor;
                
                // Only apply if color changed significantly
                if (ColorDifference(_lastAppliedColor, avgColor) >= _changeThreshold)
                {
                    _lastAppliedColor = avgColor;
                    ApplyColorToDevices(avgColor);
                    ColorChanged?.Invoke(this, avgColor);
                }
            }
            catch (Exception ex)
            {
                // Don't spam logs, sampling can fail occasionally
                System.Diagnostics.Debug.WriteLine($"Screen sampling error: {ex.Message}");
            }
        }

        private Color CaptureAverageScreenColor()
        {
            // Get screen dimensions
            var screenWidth = GetSystemMetrics(SM_CXSCREEN);
            var screenHeight = GetSystemMetrics(SM_CYSCREEN);
            
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return _lastAppliedColor;
            }
            
            // Sample points from edges (left, top, right, bottom strips)
            const int edgeWidth = 50;
            const int samplePoints = 10;
            
            long totalR = 0, totalG = 0, totalB = 0;
            int sampleCount = 0;
            
            var hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return _lastAppliedColor;
            
            try
            {
                // Sample left edge
                for (int i = 0; i < samplePoints; i++)
                {
                    int y = (screenHeight / samplePoints) * i + (screenHeight / samplePoints / 2);
                    var color = GetPixelColor(hdcScreen, edgeWidth / 2, y);
                    totalR += color.R; totalG += color.G; totalB += color.B;
                    sampleCount++;
                }
                
                // Sample right edge
                for (int i = 0; i < samplePoints; i++)
                {
                    int y = (screenHeight / samplePoints) * i + (screenHeight / samplePoints / 2);
                    var color = GetPixelColor(hdcScreen, screenWidth - edgeWidth / 2, y);
                    totalR += color.R; totalG += color.G; totalB += color.B;
                    sampleCount++;
                }
                
                // Sample top edge
                for (int i = 0; i < samplePoints; i++)
                {
                    int x = (screenWidth / samplePoints) * i + (screenWidth / samplePoints / 2);
                    var color = GetPixelColor(hdcScreen, x, edgeWidth / 2);
                    totalR += color.R; totalG += color.G; totalB += color.B;
                    sampleCount++;
                }
                
                // Sample bottom edge
                for (int i = 0; i < samplePoints; i++)
                {
                    int x = (screenWidth / samplePoints) * i + (screenWidth / samplePoints / 2);
                    var color = GetPixelColor(hdcScreen, x, screenHeight - edgeWidth / 2);
                    totalR += color.R; totalG += color.G; totalB += color.B;
                    sampleCount++;
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }
            
            if (sampleCount == 0) return _lastAppliedColor;
            
            return Color.FromArgb(
                (int)(totalR / sampleCount),
                (int)(totalG / sampleCount),
                (int)(totalB / sampleCount));
        }

        private Color GetPixelColor(IntPtr hdc, int x, int y)
        {
            uint pixel = GetPixel(hdc, x, y);
            if (pixel == 0xFFFFFFFF) // CLR_INVALID
            {
                return Color.Black;
            }
            
            int r = (int)(pixel & 0xFF);
            int g = (int)((pixel >> 8) & 0xFF);
            int b = (int)((pixel >> 16) & 0xFF);
            
            return Color.FromArgb(r, g, b);
        }

        private Color CalculateAverageColor()
        {
            int validCount = 0;
            long totalR = 0, totalG = 0, totalB = 0;
            
            foreach (var color in _colorHistory)
            {
                if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
                    continue;
                    
                totalR += color.R;
                totalG += color.G;
                totalB += color.B;
                validCount++;
            }
            
            if (validCount == 0) return Color.Black;
            
            return Color.FromArgb(
                (int)(totalR / validCount),
                (int)(totalG / validCount),
                (int)(totalB / validCount));
        }

        private int ColorDifference(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }

        private void ApplyColorToDevices(Color color)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Apply to RGB manager (Corsair, Logitech, Razer)
                    await _rgbManager.SyncStaticColorAsync(color);
                    
                    // Apply to HP OMEN keyboard
                    if (_keyboardLightingService?.IsAvailable == true)
                    {
                        var colors = new[] { color, color, color, color };
                        await _keyboardLightingService.SetAllZoneColors(colors);
                    }
                }
                catch
                {
                    // Ignore apply errors in ambient mode
                }
            });
        }

        /// <summary>
        /// Take a single sample and return the screen color (without applying to devices).
        /// </summary>
        public Color SampleOnce()
        {
            try
            {
                return CaptureAverageScreenColor();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Single sample failed: {ex.Message}");
                return Color.Black;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            Stop();
            _logging.Info("ScreenSamplingService disposed");
        }

        #region P/Invoke

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        #endregion
    }
}
