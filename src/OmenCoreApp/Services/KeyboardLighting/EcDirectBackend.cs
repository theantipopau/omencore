using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Keyboard backend using direct EC (Embedded Controller) register access.
    /// This is a fallback for models where WMI doesn't work.
    /// WARNING: EC writes can be dangerous if wrong addresses are used.
    /// </summary>
    public class EcDirectBackend : IKeyboardBackend
    {
        private readonly IEcAccess? _ecAccess;
        private readonly LoggingService _logging;
        private bool _initialized;
        private bool _disposed;
        
        // Default EC register addresses (common on OMEN 15/16/17 2020-2022)
        private readonly byte[] _colorRegisters = new byte[]
        {
            0xB1, 0xB2, 0xB3,  // Zone 0 RGB
            0xB4, 0xB5, 0xB6,  // Zone 1 RGB
            0xB7, 0xB8, 0xB9,  // Zone 2 RGB
            0xBA, 0xBB, 0xBC   // Zone 3 RGB
        };
        private readonly byte _brightnessRegister = 0xBD;
        private readonly byte _effectRegister = 0xBE;
        private readonly byte _backlightControlRegister = 0xB0;
        
        public string Name => "EC Direct";
        public KeyboardMethod Method => KeyboardMethod.EcDirect;
        public bool IsAvailable => _initialized && (_ecAccess?.IsAvailable ?? false);
        public bool SupportsReadback => true;
        public int ZoneCount => 4;
        public bool IsPerKey => false;
        
        public EcDirectBackend(IEcAccess? ecAccess, LoggingService logging, KeyboardModelConfig? modelConfig = null)
        {
            _ecAccess = ecAccess;
            _logging = logging;
            _ = modelConfig;
            
            // Use model-specific registers if available
            if (modelConfig?.EcColorRegisters != null && modelConfig.EcColorRegisters.Length >= 12)
            {
                _colorRegisters = modelConfig.EcColorRegisters;
            }
            if (modelConfig?.EcBrightnessRegister.HasValue == true)
            {
                _brightnessRegister = modelConfig.EcBrightnessRegister.Value;
            }
        }
        
        public Task<bool> InitializeAsync()
        {
            try
            {
                if (_ecAccess == null)
                {
                    _logging.Info("[EcDirectBackend] No EC access instance provided");
                    _initialized = false;
                    return Task.FromResult(false);
                }
                
                if (!_ecAccess.IsAvailable)
                {
                    _logging.Info("[EcDirectBackend] EC access not available (PawnIO/WinRing0 required)");
                    _initialized = false;
                    return Task.FromResult(false);
                }
                
                // Try to read a known-safe register to verify EC access works
                try
                {
                    // Read backlight control register (should be safe to read)
                    var value = _ecAccess.ReadByte(_backlightControlRegister);
                    _logging.Info($"[EcDirectBackend] EC read test: reg 0x{_backlightControlRegister:X2} = 0x{value:X2}");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"[EcDirectBackend] EC read test failed: {ex.Message}");
                    // Don't fail initialization - write might still work
                }
                
                _logging.Info($"[EcDirectBackend] Initialized with registers: " +
                    $"Colors=0x{_colorRegisters[0]:X2}-0x{_colorRegisters[11]:X2}, " +
                    $"Brightness=0x{_brightnessRegister:X2}");
                
                _initialized = true;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[EcDirectBackend] Initialization failed: {ex.Message}", ex);
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
                if (!IsAvailable || _ecAccess == null)
                {
                    result.FailureReason = "EC access not available";
                    return result;
                }
                
                if (zoneColors.Length < 4)
                {
                    result.FailureReason = "Requires 4 zone colors";
                    return result;
                }
                
                _logging.Info($"[EcDirectBackend] ⚠️ Writing zone colors via EC (experimental)");
                _logging.Info($"[EcDirectBackend] Setting colors: " +
                    $"Z0=#{zoneColors[0].R:X2}{zoneColors[0].G:X2}{zoneColors[0].B:X2}, " +
                    $"Z1=#{zoneColors[1].R:X2}{zoneColors[1].G:X2}{zoneColors[1].B:X2}, " +
                    $"Z2=#{zoneColors[2].R:X2}{zoneColors[2].G:X2}{zoneColors[2].B:X2}, " +
                    $"Z3=#{zoneColors[3].R:X2}{zoneColors[3].G:X2}{zoneColors[3].B:X2}");
                
                // Write each zone's RGB values to EC registers
                bool allWritesSucceeded = true;
                for (int zone = 0; zone < 4 && allWritesSucceeded; zone++)
                {
                    int baseReg = zone * 3;
                    var color = zoneColors[zone];
                    
                    try
                    {
                        _ecAccess.WriteByte(_colorRegisters[baseReg], color.R);
                        _ecAccess.WriteByte(_colorRegisters[baseReg + 1], color.G);
                        _ecAccess.WriteByte(_colorRegisters[baseReg + 2], color.B);
                    }
                    catch (Exception ex)
                    {
                        _logging.Error($"[EcDirectBackend] Failed to write zone {zone}: {ex.Message}", ex);
                        allWritesSucceeded = false;
                        result.FailureReason = $"EC write failed for zone {zone}: {ex.Message}";
                    }
                }
                
                result.BackendReportedSuccess = allWritesSucceeded;
                
                if (allWritesSucceeded)
                {
                    // Verify by reading back
                    await Task.Delay(50);
                    
                    var readBack = await ReadZoneColorsAsync();
                    if (readBack != null)
                    {
                        bool colorsMatch = true;
                        for (int i = 0; i < 4 && colorsMatch; i++)
                        {
                            colorsMatch = readBack[i].R == zoneColors[i].R &&
                                         readBack[i].G == zoneColors[i].G &&
                                         readBack[i].B == zoneColors[i].B;
                        }
                        
                        result.VerificationPassed = colorsMatch;
                        if (colorsMatch)
                        {
                            _logging.Info("[EcDirectBackend] ✓ EC color verification passed");
                        }
                        else
                        {
                            _logging.Warn("[EcDirectBackend] EC color verification failed - readback mismatch");
                        }
                    }
                    else
                    {
                        result.VerificationPassed = true; // Can't verify, assume success
                        result.SupportsVerification = false;
                    }
                }
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
                _logging.Error($"[EcDirectBackend] SetZoneColorsAsync failed: {ex.Message}", ex);
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
            
            var currentColors = await ReadZoneColorsAsync();
            var colors = currentColors ?? new Color[4] { Color.Black, Color.Black, Color.Black, Color.Black };
            colors[zone] = color;
            
            return await SetZoneColorsAsync(colors);
        }
        
        public Task<Color[]?> ReadZoneColorsAsync()
        {
            try
            {
                if (!IsAvailable || _ecAccess == null)
                    return Task.FromResult<Color[]?>(null);
                
                var colors = new Color[4];
                for (int zone = 0; zone < 4; zone++)
                {
                    int baseReg = zone * 3;
                    byte r = _ecAccess.ReadByte(_colorRegisters[baseReg]);
                    byte g = _ecAccess.ReadByte(_colorRegisters[baseReg + 1]);
                    byte b = _ecAccess.ReadByte(_colorRegisters[baseReg + 2]);
                    colors[zone] = Color.FromArgb(r, g, b);
                }
                
                return Task.FromResult<Color[]?>(colors);
            }
            catch (Exception ex)
            {
                _logging.Warn($"[EcDirectBackend] ReadZoneColorsAsync failed: {ex.Message}");
                return Task.FromResult<Color[]?>(null);
            }
        }
        
        public Task<bool> SetBrightnessAsync(int brightness)
        {
            try
            {
                if (!IsAvailable || _ecAccess == null)
                    return Task.FromResult(false);
                
                // Clamp to 0-100 and map to 0-255 for EC
                brightness = Math.Clamp(brightness, 0, 100);
                byte ecValue = (byte)(brightness * 255 / 100);
                
                _ecAccess.WriteByte(_brightnessRegister, ecValue);
                _logging.Info($"[EcDirectBackend] Set brightness to {brightness}% (EC value: {ecValue})");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[EcDirectBackend] SetBrightnessAsync failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }
        
        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            try
            {
                if (!IsAvailable || _ecAccess == null)
                    return Task.FromResult(false);
                
                // Write to backlight control register
                byte value = enabled ? (byte)0x01 : (byte)0x00;
                _ecAccess.WriteByte(_backlightControlRegister, value);
                _logging.Info($"[EcDirectBackend] Set backlight enabled: {enabled}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logging.Error($"[EcDirectBackend] SetBacklightEnabledAsync failed: {ex.Message}", ex);
                return Task.FromResult(false);
            }
        }
        
        public async Task<RgbApplyResult> SetEffectAsync(KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed)
        {
            var result = new RgbApplyResult { Method = Method };
            
            try
            {
                if (!IsAvailable || _ecAccess == null)
                {
                    result.FailureReason = "EC access not available";
                    return result;
                }
                
                // Write effect type to effect register
                _ecAccess.WriteByte(_effectRegister, (byte)effect);
                _logging.Info($"[EcDirectBackend] Set effect to {effect} (EC value: {(byte)effect})");
                
                // For static, also set the colors
                if (effect == KeyboardEffect.Static)
                {
                    var colors = new Color[] { primaryColor, primaryColor, primaryColor, primaryColor };
                    return await SetZoneColorsAsync(colors);
                }
                else if (effect == KeyboardEffect.Off)
                {
                    await SetBacklightEnabledAsync(false);
                }
                
                result.BackendReportedSuccess = true;
                result.VerificationPassed = true;
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
                _logging.Error($"[EcDirectBackend] SetEffectAsync failed: {ex.Message}", ex);
            }
            
            return result;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _initialized = false;
            // EC access is not owned by this class
        }
    }
}
