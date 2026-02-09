using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Keyboard backend using HP WMI BIOS interface.
    /// Works with most OMEN laptops from 2020-2022 using 128-byte ColorTable format.
    /// </summary>
    public class WmiBiosBackend : IKeyboardBackend
    {
        private readonly HpWmiBios? _wmiBios;
        private readonly LoggingService _logging;
        private bool _initialized;
        private bool _disposed;
        
        public string Name => "WMI BIOS (ColorTable)";
        public KeyboardMethod Method => KeyboardMethod.ColorTable2020;
        public bool IsAvailable => _initialized && (_wmiBios?.IsAvailable ?? false);
        public bool SupportsReadback => true; // GetColorTable supported
        public int ZoneCount => 4;
        public bool IsPerKey => false;
        
        public WmiBiosBackend(HpWmiBios? wmiBios, LoggingService logging)
        {
            _wmiBios = wmiBios;
            _logging = logging;
        }
        
        public Task<bool> InitializeAsync()
        {
            try
            {
                if (_wmiBios == null)
                {
                    _logging.Info("[WmiBiosBackend] No WMI BIOS instance provided");
                    _initialized = false;
                    return Task.FromResult(false);
                }
                
                if (!_wmiBios.IsAvailable)
                {
                    _logging.Info("[WmiBiosBackend] WMI BIOS not available on this system");
                    _initialized = false;
                    return Task.FromResult(false);
                }
                
                // Try to read current color table to verify functionality
                var currentColors = _wmiBios.GetColorTable();
                if (currentColors == null)
                {
                    _logging.Warn("[WmiBiosBackend] GetColorTable returned null - keyboard control may not be supported");
                    // Don't fail - some models don't support readback but do support write
                }
                else
                {
                    _logging.Info($"[WmiBiosBackend] Initialized - read {currentColors.Length} bytes from color table");
                }
                
                _initialized = true;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[WmiBiosBackend] Initialization failed: {ex.Message}", ex);
                _initialized = false;
                return Task.FromResult(false);
            }
        }
        
        public async Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            var sw = Stopwatch.StartNew();
            var result = new RgbApplyResult
            {
                Method = Method,
                SupportsVerification = SupportsReadback
            };
            
            try
            {
                if (!IsAvailable || _wmiBios == null)
                {
                    result.FailureReason = "WMI BIOS backend not available";
                    return result;
                }
                
                if (zoneColors.Length < 4)
                {
                    result.FailureReason = "Requires 4 zone colors";
                    return result;
                }
                
                // Build 12-byte color array (RGB * 4 zones)
                var colorTable = new byte[12];
                for (int i = 0; i < 4; i++)
                {
                    colorTable[i * 3] = zoneColors[i].R;
                    colorTable[i * 3 + 1] = zoneColors[i].G;
                    colorTable[i * 3 + 2] = zoneColors[i].B;
                }
                
                _logging.Info($"[WmiBiosBackend] Setting colors: " +
                    $"Z0=#{zoneColors[0].R:X2}{zoneColors[0].G:X2}{zoneColors[0].B:X2}, " +
                    $"Z1=#{zoneColors[1].R:X2}{zoneColors[1].G:X2}{zoneColors[1].B:X2}, " +
                    $"Z2=#{zoneColors[2].R:X2}{zoneColors[2].G:X2}{zoneColors[2].B:X2}, " +
                    $"Z3=#{zoneColors[3].R:X2}{zoneColors[3].G:X2}{zoneColors[3].B:X2}");
                
                result.BackendReportedSuccess = _wmiBios.SetColorTable(colorTable);
                
                if (!result.BackendReportedSuccess)
                {
                    result.FailureReason = "WMI SetColorTable returned false";
                    return result;
                }
                
                // Verify by reading back
                await Task.Delay(50); // Small delay for hardware to update
                
                var readBack = await ReadZoneColorsAsync();
                if (readBack != null)
                {
                    bool colorsMatch = true;
                    for (int i = 0; i < 4 && colorsMatch; i++)
                    {
                        // Allow small tolerance (some models round values)
                        colorsMatch = Math.Abs(readBack[i].R - zoneColors[i].R) <= 5 &&
                                     Math.Abs(readBack[i].G - zoneColors[i].G) <= 5 &&
                                     Math.Abs(readBack[i].B - zoneColors[i].B) <= 5;
                    }
                    
                    result.VerificationPassed = colorsMatch;
                    if (!colorsMatch)
                    {
                        _logging.Warn("[WmiBiosBackend] Color verification failed - readback doesn't match");
                        result.FailureReason = "Color verification failed - keyboard may not support this method";
                    }
                    else
                    {
                        _logging.Info("[WmiBiosBackend] ✓ Color verification passed");
                    }
                }
                else
                {
                    // Can't verify, assume success since backend reported OK
                    result.VerificationPassed = true;
                    result.SupportsVerification = false;
                    _logging.Info("[WmiBiosBackend] Cannot verify (readback not supported) - assuming success");
                }
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
                _logging.Error($"[WmiBiosBackend] SetZoneColorsAsync failed: {ex.Message}", ex);
            }
            finally
            {
                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;
            }
            
            return result;
        }
        
        public async Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            if (zone < 0 || zone > 3)
            {
                return new RgbApplyResult
                {
                    Method = Method,
                    FailureReason = $"Invalid zone {zone}, must be 0-3"
                };
            }
            
            // Read current colors, modify one zone, write back
            var currentColors = await ReadZoneColorsAsync();
            var colors = currentColors ?? new Color[4] { Color.Black, Color.Black, Color.Black, Color.Black };
            colors[zone] = color;
            
            return await SetZoneColorsAsync(colors);
        }
        
        public Task<Color[]?> ReadZoneColorsAsync()
        {
            try
            {
                if (!IsAvailable || _wmiBios == null)
                    return Task.FromResult<Color[]?>(null);
                
                var data = _wmiBios.GetColorTable();
                if (data == null || data.Length < 37) // Need at least 25 (offset) + 12 (colors)
                    return Task.FromResult<Color[]?>(null);
                
                // Colors are at offset 25 (after zone count + 24 padding)
                const int colorOffset = 25;
                var colors = new Color[4];
                
                for (int i = 0; i < 4; i++)
                {
                    int offset = colorOffset + (i * 3);
                    if (offset + 2 < data.Length)
                    {
                        colors[i] = Color.FromArgb(data[offset], data[offset + 1], data[offset + 2]);
                    }
                }
                
                return Task.FromResult<Color[]?>(colors);
            }
            catch (Exception ex)
            {
                _logging.Warn($"[WmiBiosBackend] ReadZoneColorsAsync failed: {ex.Message}");
                return Task.FromResult<Color[]?>(null);
            }
        }
        
        public Task<bool> SetBrightnessAsync(int brightness)
        {
            try
            {
                if (!IsAvailable || _wmiBios == null)
                    return Task.FromResult(false);
                
                // HP OMEN keyboard backlight supports brightness via the backlight command.
                // The WMI backlight byte encodes on/off + brightness level in the data byte:
                //   0x64 = off, 0xE4 = on (full), and brightness maps to 4 levels.
                // We map 0-100% to: 0=off, 1-33=low, 34-66=medium, 67-100=high.
                // Using SetBacklight(true) first, then adjusting color intensity as fallback.
                
                brightness = Math.Clamp(brightness, 0, 100);
                
                if (brightness == 0)
                {
                    var result = _wmiBios.SetBacklight(false);
                    _logging.Info($"[WmiBiosBackend] Backlight off via brightness=0, result={result}");
                    return Task.FromResult(result);
                }
                
                // Ensure backlight is on
                _wmiBios.SetBacklight(true);
                
                // Scale current colors by brightness percentage
                var currentColors = _wmiBios.GetColorTable();
                if (currentColors != null && currentColors.Length >= 37)
                {
                    const int colorOffset = 25;
                    var scaledColors = new byte[12];
                    for (int i = 0; i < 12; i++)
                    {
                        int offset = colorOffset + i;
                        if (offset < currentColors.Length)
                        {
                            scaledColors[i] = (byte)(currentColors[offset] * brightness / 100);
                        }
                    }
                    
                    // Ensure at least some brightness (don't go fully black when > 0%)
                    bool allZero = true;
                    for (int i = 0; i < 12; i++)
                        if (scaledColors[i] > 0) { allZero = false; break; }
                    
                    if (!allZero)
                    {
                        var setResult = _wmiBios.SetColorTable(scaledColors, ensureBacklightOn: false);
                        _logging.Info($"[WmiBiosBackend] Brightness set to {brightness}% via color scaling, result={setResult}");
                        return Task.FromResult(setResult);
                    }
                }
                
                _logging.Info($"[WmiBiosBackend] Brightness set to {brightness}% (backlight on, no color scaling available)");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[WmiBiosBackend] SetBrightnessAsync failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }
        
        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            try
            {
                if (!IsAvailable || _wmiBios == null)
                    return Task.FromResult(false);
                
                // Use black color for all zones to effectively disable
                if (!enabled)
                {
                    var blackColors = new byte[12]; // All zeros = black
                    return Task.FromResult(_wmiBios.SetColorTable(blackColors));
                }
                
                // For enable, just return true - user should set actual colors
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[WmiBiosBackend] SetBacklightEnabledAsync failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }
        
        public Task<RgbApplyResult> SetEffectAsync(KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed)
        {
            // WMI BIOS ColorTable backend only supports static colors
            // For effects, we'd need a different WMI method or EC access
            if (effect == KeyboardEffect.Static || effect == KeyboardEffect.Off)
            {
                var color = effect == KeyboardEffect.Off ? Color.Black : primaryColor;
                var colors = new Color[] { color, color, color, color };
                return SetZoneColorsAsync(colors);
            }
            
            return Task.FromResult(new RgbApplyResult
            {
                Method = Method,
                FailureReason = $"Effect '{effect}' not supported by WMI BIOS backend (only Static supported)"
            });
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _initialized = false;
            // WmiBios is not owned by this class, don't dispose it
        }
    }
}
