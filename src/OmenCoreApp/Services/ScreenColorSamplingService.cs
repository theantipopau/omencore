using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services;
using OmenCore.Services.Rgb;

namespace OmenCore.Services
{
    /// <summary>
    /// Service that captures screen colors for ambient RGB lighting effects.
    /// Supports edge sampling (Ambilight-style), average color, and zone-based capture.
    /// </summary>
    public class ScreenColorSamplingService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly List<IRgbProvider> _rgbProviders = new();
        private bool _disposed;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;

        // Capture state
        private Bitmap? _captureBitmap;
        private Graphics? _captureGraphics;
        private Rectangle _screenBounds;
        private int _sampleWidth = 1920;
        private int _sampleHeight = 1080;

        // Zone configuration
        private List<ScreenZone> _zones = new();
        private int _edgeZoneCount = 6; // Zones per edge
        private int _edgeDepth = 100; // Pixels to sample from edge

        // Output colors
        private Dictionary<int, (byte R, byte G, byte B)> _zoneColors = new();
        private (byte R, byte G, byte B) _averageColor = (0, 0, 0);

        // Performance settings
        private int _captureIntervalMs = 33; // ~30 FPS
        private int _downscaleFactor = 4; // Downscale for faster processing
        private float _saturationBoost = 1.2f;
        private float _smoothingFactor = 0.7f; // Color smoothing (0 = no smoothing, 1 = very smooth)

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        private const int SRCCOPY = 0x00CC0020;

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether the service is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Gets or sets the capture mode.
        /// </summary>
        public ScreenCaptureMode Mode { get; set; } = ScreenCaptureMode.EdgeZones;

        /// <summary>
        /// Gets or sets the number of zones per screen edge.
        /// </summary>
        public int ZonesPerEdge
        {
            get => _edgeZoneCount;
            set
            {
                _edgeZoneCount = Math.Clamp(value, 1, 20);
                RebuildZones();
            }
        }

        /// <summary>
        /// Gets or sets the edge depth in pixels.
        /// </summary>
        public int EdgeDepth
        {
            get => _edgeDepth;
            set => _edgeDepth = Math.Clamp(value, 10, 500);
        }

        /// <summary>
        /// Gets or sets the saturation boost multiplier.
        /// </summary>
        public float SaturationBoost
        {
            get => _saturationBoost;
            set => _saturationBoost = Math.Clamp(value, 0.5f, 2f);
        }

        /// <summary>
        /// Gets or sets the color smoothing factor.
        /// </summary>
        public float SmoothingFactor
        {
            get => _smoothingFactor;
            set => _smoothingFactor = Math.Clamp(value, 0f, 0.95f);
        }

        /// <summary>
        /// Gets or sets the capture interval in milliseconds.
        /// </summary>
        public int CaptureIntervalMs
        {
            get => _captureIntervalMs;
            set => _captureIntervalMs = Math.Clamp(value, 16, 200); // 5-60 FPS
        }

        /// <summary>
        /// Gets the list of screen zones.
        /// </summary>
        public IReadOnlyList<ScreenZone> Zones => _zones.AsReadOnly();

        /// <summary>
        /// Gets the current average screen color.
        /// </summary>
        public (byte R, byte G, byte B) AverageColor => _averageColor;

        /// <summary>
        /// Event raised when screen colors are updated.
        /// </summary>
        public event EventHandler<ScreenColorsEventArgs>? ColorsUpdated;

        #endregion

        public ScreenColorSamplingService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Register an RGB provider to receive color updates.
        /// </summary>
        public void RegisterProvider(IRgbProvider provider)
        {
            if (!_rgbProviders.Contains(provider))
                _rgbProviders.Add(provider);
        }

        /// <summary>
        /// Start screen capture and color sampling.
        /// </summary>
        public Task<bool> StartAsync()
        {
            if (_isRunning) return Task.FromResult(true);

            try
            {
                _logging.Info("ScreenColorSampling: Starting...");

                // Get primary screen bounds
                _screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds 
                    ?? new Rectangle(0, 0, 1920, 1080);
                
                _sampleWidth = _screenBounds.Width / _downscaleFactor;
                _sampleHeight = _screenBounds.Height / _downscaleFactor;

                // Create capture bitmap
                _captureBitmap = new Bitmap(_sampleWidth, _sampleHeight, PixelFormat.Format24bppRgb);
                _captureGraphics = Graphics.FromImage(_captureBitmap);

                // Build zones
                RebuildZones();

                _cts = new CancellationTokenSource();
                _isRunning = true;

                // Start capture loop
                _captureTask = Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);

                _logging.Info($"ScreenColorSampling: Started - {_zones.Count} zones, {_sampleWidth}x{_sampleHeight} sample size");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"ScreenColorSampling: Start failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Stop screen capture.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            _logging.Info("ScreenColorSampling: Stopping...");

            _cts?.Cancel();

            if (_captureTask != null)
            {
                try
                {
                    await _captureTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }

            _captureGraphics?.Dispose();
            _captureBitmap?.Dispose();
            _captureGraphics = null;
            _captureBitmap = null;

            _isRunning = false;
            _logging.Info("ScreenColorSampling: Stopped");
        }

        /// <summary>
        /// Build screen zones based on current settings.
        /// </summary>
        private void RebuildZones()
        {
            _zones.Clear();
            _zoneColors.Clear();

            int zoneId = 0;
            int zoneWidth = _sampleWidth / _edgeZoneCount;
            int zoneHeight = _sampleHeight / _edgeZoneCount;
            int edgeDepthScaled = _edgeDepth / _downscaleFactor;

            // Top edge (left to right)
            for (int i = 0; i < _edgeZoneCount; i++)
            {
                _zones.Add(new ScreenZone(zoneId++, $"Top {i + 1}", ScreenEdge.Top,
                    new Rectangle(i * zoneWidth, 0, zoneWidth, edgeDepthScaled)));
            }

            // Right edge (top to bottom)
            for (int i = 0; i < _edgeZoneCount; i++)
            {
                _zones.Add(new ScreenZone(zoneId++, $"Right {i + 1}", ScreenEdge.Right,
                    new Rectangle(_sampleWidth - edgeDepthScaled, i * zoneHeight, edgeDepthScaled, zoneHeight)));
            }

            // Bottom edge (right to left)
            for (int i = _edgeZoneCount - 1; i >= 0; i--)
            {
                _zones.Add(new ScreenZone(zoneId++, $"Bottom {i + 1}", ScreenEdge.Bottom,
                    new Rectangle(i * zoneWidth, _sampleHeight - edgeDepthScaled, zoneWidth, edgeDepthScaled)));
            }

            // Left edge (bottom to top)
            for (int i = _edgeZoneCount - 1; i >= 0; i--)
            {
                _zones.Add(new ScreenZone(zoneId++, $"Left {i + 1}", ScreenEdge.Left,
                    new Rectangle(0, i * zoneHeight, edgeDepthScaled, zoneHeight)));
            }

            // Initialize zone colors
            foreach (var zone in _zones)
            {
                _zoneColors[zone.Id] = (0, 0, 0);
            }

            _logging.Debug($"ScreenColorSampling: Built {_zones.Count} zones");
        }

        /// <summary>
        /// Main capture loop.
        /// </summary>
        private async Task CaptureLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Capture screen
                    CaptureScreen();

                    // Sample colors
                    SampleColors();

                    // Apply to lighting
                    if (_rgbProviders.Count > 0)
                    {
                        await ApplyToLightingAsync();
                    }

                    // Raise event
                    ColorsUpdated?.Invoke(this, new ScreenColorsEventArgs(_zoneColors, _averageColor));

                    // Wait for next frame
                    await Task.Delay(_captureIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error($"ScreenColorSampling: Capture error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }
        }

        /// <summary>
        /// Capture the screen to the bitmap.
        /// </summary>
        private void CaptureScreen()
        {
            if (_captureBitmap == null || _captureGraphics == null) return;

            try
            {
                // Use GDI+ screen capture (works on Windows)
                _captureGraphics.CopyFromScreen(
                    _screenBounds.Left, _screenBounds.Top,
                    0, 0,
                    new Size(_sampleWidth, _sampleHeight),
                    CopyPixelOperation.SourceCopy);
            }
            catch (Exception ex)
            {
                _logging.Warn($"ScreenColorSampling: Screen capture failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sample colors from the captured screen.
        /// </summary>
        private void SampleColors()
        {
            if (_captureBitmap == null) return;

            try
            {
                // Lock bitmap for fast pixel access
                var bitmapData = _captureBitmap.LockBits(
                    new Rectangle(0, 0, _sampleWidth, _sampleHeight),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    int stride = bitmapData.Stride;
                    IntPtr scan0 = bitmapData.Scan0;

                    // Calculate average for entire screen
                    long totalR = 0, totalG = 0, totalB = 0;
                    int totalPixels = 0;

                    // Sample each zone
                    foreach (var zone in _zones)
                    {
                        var (r, g, b) = SampleZone(scan0, stride, zone.SampleRegion);
                        
                        // Apply saturation boost
                        (r, g, b) = BoostSaturation(r, g, b);

                        // Apply smoothing with previous value
                        var prev = _zoneColors.TryGetValue(zone.Id, out var p) ? p : (r, g, b);
                        r = (byte)(prev.Item1 * _smoothingFactor + r * (1 - _smoothingFactor));
                        g = (byte)(prev.Item2 * _smoothingFactor + g * (1 - _smoothingFactor));
                        b = (byte)(prev.Item3 * _smoothingFactor + b * (1 - _smoothingFactor));

                        _zoneColors[zone.Id] = (r, g, b);

                        totalR += r;
                        totalG += g;
                        totalB += b;
                        totalPixels++;
                    }

                    // Update average
                    if (totalPixels > 0)
                    {
                        _averageColor = (
                            (byte)(totalR / totalPixels),
                            (byte)(totalG / totalPixels),
                            (byte)(totalB / totalPixels)
                        );
                    }
                }
                finally
                {
                    _captureBitmap.UnlockBits(bitmapData);
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"ScreenColorSampling: Color sampling failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sample the average color of a rectangular region.
        /// </summary>
        private (byte R, byte G, byte B) SampleZone(IntPtr scan0, int stride, Rectangle region)
        {
            long r = 0, g = 0, b = 0;
            int pixelCount = 0;
            int sampleStep = 4; // Skip pixels for performance

            int maxX = Math.Min(region.Right, _sampleWidth);
            int maxY = Math.Min(region.Bottom, _sampleHeight);

            for (int y = region.Top; y < maxY; y += sampleStep)
            {
                for (int x = region.Left; x < maxX; x += sampleStep)
                {
                    int offset = y * stride + x * 3; // 3 bytes per pixel (BGR)
                    b += Marshal.ReadByte(scan0, offset);
                    g += Marshal.ReadByte(scan0, offset + 1);
                    r += Marshal.ReadByte(scan0, offset + 2);
                    pixelCount++;
                }
            }

            if (pixelCount == 0) return (0, 0, 0);

            return (
                (byte)(r / pixelCount),
                (byte)(g / pixelCount),
                (byte)(b / pixelCount)
            );
        }

        /// <summary>
        /// Boost color saturation.
        /// </summary>
        private (byte R, byte G, byte B) BoostSaturation(byte r, byte g, byte b)
        {
            // Convert to HSV
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            float h = 0, s = 0, v = max;

            if (delta > 0)
            {
                s = delta / max;
                
                if (rf >= max)
                    h = (gf - bf) / delta;
                else if (gf >= max)
                    h = 2 + (bf - rf) / delta;
                else
                    h = 4 + (rf - gf) / delta;
                
                h *= 60;
                if (h < 0) h += 360;
            }

            // Boost saturation
            s = Math.Min(1f, s * _saturationBoost);

            // Convert back to RGB
            return HsvToRgb(h / 360f, s, v);
        }

        /// <summary>
        /// Convert HSV to RGB.
        /// </summary>
        private (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
        {
            float r, g, b;
            
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        /// <summary>
        /// Apply zone colors to registered RGB providers.
        /// </summary>
        private async Task ApplyToLightingAsync()
        {
            if (_rgbProviders.Count == 0) return;

            try
            {
                // Apply average color to all providers
                var color = System.Drawing.Color.FromArgb(_averageColor.R, _averageColor.G, _averageColor.B);
                foreach (var provider in _rgbProviders)
                {
                    if (provider.IsAvailable)
                    {
                        await provider.SetStaticColorAsync(color);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"ScreenColorSampling: Failed to apply lighting: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the color for a specific zone.
        /// </summary>
        public (byte R, byte G, byte B) GetZoneColor(int zoneId)
        {
            return _zoneColors.TryGetValue(zoneId, out var color) ? color : ((byte)0, (byte)0, (byte)0);
        }

        /// <summary>
        /// Get colors for a specific edge.
        /// </summary>
        public IEnumerable<(byte R, byte G, byte B)> GetEdgeColors(ScreenEdge edge)
        {
            foreach (var zone in _zones)
            {
                if (zone.Edge == edge)
                {
                    yield return _zoneColors.TryGetValue(zone.Id, out var c) ? c : ((byte)0, (byte)0, (byte)0);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopAsync().Wait(TimeSpan.FromSeconds(2));
            _cts?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Screen capture modes.
    /// </summary>
    public enum ScreenCaptureMode
    {
        /// <summary>Sample colors from screen edges (Ambilight-style).</summary>
        EdgeZones,
        /// <summary>Single average color for entire screen.</summary>
        AverageColor,
        /// <summary>Dominant color using color clustering.</summary>
        DominantColor
    }

    /// <summary>
    /// Screen edges.
    /// </summary>
    public enum ScreenEdge
    {
        Top,
        Right,
        Bottom,
        Left
    }

    /// <summary>
    /// Represents a sampling zone on the screen.
    /// </summary>
    public class ScreenZone
    {
        public int Id { get; }
        public string Name { get; }
        public ScreenEdge Edge { get; }
        public Rectangle SampleRegion { get; }

        public ScreenZone(int id, string name, ScreenEdge edge, Rectangle region)
        {
            Id = id;
            Name = name;
            Edge = edge;
            SampleRegion = region;
        }
    }

    /// <summary>
    /// Event args for screen color updates.
    /// </summary>
    public class ScreenColorsEventArgs : EventArgs
    {
        public IReadOnlyDictionary<int, (byte R, byte G, byte B)> ZoneColors { get; }
        public (byte R, byte G, byte B) AverageColor { get; }

        public ScreenColorsEventArgs(Dictionary<int, (byte R, byte G, byte B)> zoneColors, (byte R, byte G, byte B) average)
        {
            ZoneColors = zoneColors;
            AverageColor = average;
        }
    }
}
