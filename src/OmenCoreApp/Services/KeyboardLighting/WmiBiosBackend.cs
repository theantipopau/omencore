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
                
                brightness = Math.Clamp(brightness, 0, 100);
                
                if (brightness == 0)
                {
                    // Turn backlight completely off using native WMI command
                    var result = _wmiBios.SetBrightnessLevel(0x64); // 0x64 (100) = OFF
                    _logging.Info($"[WmiBiosBackend] Backlight off via native brightness command, result={result}");
                    return Task.FromResult(result);
                }
                
                // Use native WMI brightness command (command type 5)
                // Map 0-100% to WMI brightness range:
                //   0x64 (100) = OFF/minimum, 0xE4 (228) = ON/maximum
                //   Linear interpolation between 100 and 228
                byte wmiBrightness = (byte)(100 + (brightness * 128 / 100)); // 100..228
                
                var setBright = _wmiBios.SetBrightnessLevel(wmiBrightness);
                _logging.Info($"[WmiBiosBackend] Native brightness set to {brightness}% → WMI 0x{wmiBrightness:X2} ({wmiBrightness}), result={setBright}");
                
                // Fallback: if native brightness fails, try color-scaling approach
                if (!setBright)
                {
                    _logging.Warn("[WmiBiosBackend] Native brightness failed, falling back to color scaling");
                    _wmiBios.SetBacklight(true);
                    
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
                        
                        bool allZero = true;
                        for (int i = 0; i < 12; i++)
                            if (scaledColors[i] > 0) { allZero = false; break; }
                        
                        if (!allZero)
                        {
                            var setResult = _wmiBios.SetColorTable(scaledColors, ensureBacklightOn: false);
                            _logging.Info($"[WmiBiosBackend] Brightness fallback via color scaling: {brightness}%, result={setResult}");
                            return Task.FromResult(setResult);
                        }
                    }
                }
                
                return Task.FromResult(setBright);
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
                
                // Use native WMI backlight command for proper on/off control
                var result = _wmiBios.SetBacklight(enabled);
                _logging.Info($"[WmiBiosBackend] Backlight {(enabled ? "ON" : "OFF")} via native SetBacklight, result={result}");
                
                // Fallback: if native command fails, use brightness level byte
                if (!result)
                {
                    byte brightnessVal = enabled ? (byte)0xE4 : (byte)0x64; // 228=ON, 100=OFF
                    result = _wmiBios.SetBrightnessLevel(brightnessVal);
                    _logging.Info($"[WmiBiosBackend] Backlight fallback via SetBrightnessLevel(0x{brightnessVal:X2}), result={result}");
                }
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logging.Error($"[WmiBiosBackend] SetBacklightEnabledAsync failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }
        
        public Task<RgbApplyResult> SetEffectAsync(KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed)
        {
            // Static and Off effects use existing color table approach
            if (effect == KeyboardEffect.Static || effect == KeyboardEffect.Off)
            {
                var color = effect == KeyboardEffect.Off ? Color.Black : primaryColor;
                var colors = new Color[] { color, color, color, color };
                return SetZoneColorsAsync(colors);
            }
            
            // LED animation effects via WMI BIOS command type 7 (SetLedAnimation)
            // Data format (from OmenHubLighter WMILedAnimation enum):
            //   Byte 0: Zone (0xFF = all zones)
            //   Byte 1: ColorMode (1=breathing, 2=color cycle, 3=wave)
            //   Byte 2-3: Time/speed (lower = faster, range ~1-11)
            //   Byte 4: Brightness (0-100)
            //   Byte 5: ColorCount
            //   Byte 6+: RGB color data (3 bytes per color)
            
            if (_wmiBios == null)
            {
                return Task.FromResult(new RgbApplyResult
                {
                    Method = Method,
                    FailureReason = "WMI BIOS not available"
                });
            }
            
            try
            {
                byte colorMode;
                byte colorCount;
                
                switch (effect)
                {
                    case KeyboardEffect.Breathing:
                        colorMode = 1;
                        colorCount = 1;
                        break;
                    case KeyboardEffect.ColorCycle:
                        colorMode = 2;
                        colorCount = 2;
                        break;
                    case KeyboardEffect.Wave:
                        colorMode = 3;
                        colorCount = 2;
                        break;
                    default:
                        return Task.FromResult(new RgbApplyResult
                        {
                            Method = Method,
                            FailureReason = $"Effect '{effect}' not supported by WMI BIOS backend"
                        });
                }
                
                // Map speed 0-100 to WMI animation time (11=slowest, 1=fastest)
                int animSpeed = Math.Max(1, 11 - (speed * 10 / 100));
                
                // Build the animation data packet
                var animData = new byte[128];
                animData[0] = 0xFF;                 // Zone: all zones
                animData[1] = colorMode;             // Effect type
                animData[2] = (byte)(animSpeed & 0xFF);     // Time low byte
                animData[3] = (byte)((animSpeed >> 8) & 0xFF); // Time high byte
                animData[4] = 100;                   // Brightness: full
                animData[5] = colorCount;            // Number of colors
                
                // Primary color at offset 6
                animData[6] = primaryColor.R;
                animData[7] = primaryColor.G;
                animData[8] = primaryColor.B;
                
                // Secondary color at offset 9 (for ColorCycle/Wave)
                if (colorCount >= 2)
                {
                    animData[9] = secondaryColor.R;
                    animData[10] = secondaryColor.G;
                    animData[11] = secondaryColor.B;
                }
                
                _logging.Info($"[WmiBiosBackend] Setting effect {effect}: mode={colorMode}, speed={animSpeed}, " +
                    $"colors=#{primaryColor.R:X2}{primaryColor.G:X2}{primaryColor.B:X2}" +
                    (colorCount >= 2 ? $"/#{secondaryColor.R:X2}{secondaryColor.G:X2}{secondaryColor.B:X2}" : ""));
                
                var success = _wmiBios.SetLedAnimation(animData);
                
                var result = new RgbApplyResult
                {
                    Method = Method,
                    BackendReportedSuccess = success,
                    VerificationPassed = success, // Can't easily verify animations
                    SupportsVerification = false
                };
                
                if (!success)
                {
                    result.FailureReason = $"SetLedAnimation failed for effect '{effect}' — hardware may not support LED animations. " +
                        "Try updating BIOS or check if your keyboard model supports effects.";
                    _logging.Warn($"[WmiBiosBackend] LED animation failed for {effect} - hardware may not support this");
                }
                else
                {
                    _logging.Info($"[WmiBiosBackend] ✓ LED animation set: {effect}");
                }
                
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logging.Error($"[WmiBiosBackend] SetEffectAsync failed: {ex.Message}", ex);
                return Task.FromResult(new RgbApplyResult
                {
                    Method = Method,
                    FailureReason = ex.Message
                });
            }
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
