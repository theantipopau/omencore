using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Result of backend detection/probing.
    /// </summary>
    public class KeyboardProbeResult
    {
        /// <summary>The method that works for this system.</summary>
        public KeyboardMethod WorkingMethod { get; set; } = KeyboardMethod.Unsupported;
        
        /// <summary>The backend instance that works.</summary>
        public IKeyboardBackend? WorkingBackend { get; set; }
        
        /// <summary>Model configuration used (if detected).</summary>
        public KeyboardModelConfig? ModelConfig { get; set; }
        
        /// <summary>All methods that were tried.</summary>
        public List<string> TriedMethods { get; set; } = new();
        
        /// <summary>Detailed status message.</summary>
        public string StatusMessage { get; set; } = "";
        
        /// <summary>Whether the probe was successful.</summary>
        public bool Success => WorkingMethod != KeyboardMethod.Unsupported && WorkingBackend != null;
    }

    /// <summary>
    /// Unified keyboard lighting service with multi-backend support.
    /// 
    /// v1.5.0: Complete rework with:
    /// - Model-based configuration database
    /// - Multiple backend support (WMI BIOS, EC Direct, HID Per-Key)
    /// - Automatic backend detection and fallback
    /// - Readback verification where supported
    /// - User confirmation flow for unverifiable changes
    /// </summary>
    public class KeyboardLightingServiceV2 : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly HpWmiBios? _wmiBios;
        private readonly IEcAccess? _ecAccess;
        private readonly ConfigurationService? _configService;
        private readonly SystemInfoService? _systemInfoService;
        
        private IKeyboardBackend? _activeBackend;
        private KeyboardModelConfig? _modelConfig;
        private KeyboardProbeResult? _lastProbeResult;
        private bool _disposed;
        
        // Telemetry
        private int _applySuccessCount = 0;
        private int _applyFailureCount = 0;
        private readonly object _telemetryLock = new();
        
        /// <summary>Whether any keyboard lighting backend is available.</summary>
        public bool IsAvailable => _activeBackend?.IsAvailable ?? false;
        
        /// <summary>Name of the active backend.</summary>
        public string BackendName => _activeBackend?.Name ?? "None";
        
        /// <summary>Active backend method.</summary>
        public KeyboardMethod ActiveMethod => _activeBackend?.Method ?? KeyboardMethod.Unsupported;
        
        /// <summary>Keyboard type classification.</summary>
        public KeyboardType KeyboardType => _modelConfig?.KeyboardType ?? KeyboardType.Unknown;
        
        /// <summary>Number of zones (4 for most models, 0 for per-key).</summary>
        public int ZoneCount => _activeBackend?.ZoneCount ?? 0;
        
        /// <summary>Whether this is a per-key RGB keyboard.</summary>
        public bool IsPerKey => _activeBackend?.IsPerKey ?? false;
        
        /// <summary>Model configuration (if detected).</summary>
        public KeyboardModelConfig? ModelConfig => _modelConfig;
        
        /// <summary>Last probe result.</summary>
        public KeyboardProbeResult? LastProbeResult => _lastProbeResult;
        
        /// <summary>Telemetry: success rate as percentage.</summary>
        public double SuccessRate
        {
            get
            {
                lock (_telemetryLock)
                {
                    var total = _applySuccessCount + _applyFailureCount;
                    return total > 0 ? (_applySuccessCount * 100.0 / total) : 0;
                }
            }
        }
        
        public KeyboardLightingServiceV2(
            LoggingService logging,
            HpWmiBios? wmiBios = null,
            IEcAccess? ecAccess = null,
            ConfigurationService? configService = null,
            SystemInfoService? systemInfoService = null)
        {
            _logging = logging;
            _wmiBios = wmiBios;
            _ecAccess = ecAccess;
            _configService = configService;
            _systemInfoService = systemInfoService;
        }
        
        /// <summary>
        /// Initialize the service by detecting the best available backend.
        /// </summary>
        public async Task<KeyboardProbeResult> InitializeAsync()
        {
            _logging.Info("[KeyboardLightingV2] Starting keyboard backend detection...");
            
            var result = new KeyboardProbeResult();
            
            // Try to get model-specific configuration
            _modelConfig = DetectModelConfig();
            result.ModelConfig = _modelConfig;
            
            if (_modelConfig != null)
            {
                _logging.Info($"[KeyboardLightingV2] Detected model: {_modelConfig.ModelName} " +
                    $"(ProductId: {_modelConfig.ProductId}, Type: {_modelConfig.KeyboardType})");
                
                // Try preferred method first, then fallbacks
                var methodsToTry = new List<KeyboardMethod> { _modelConfig.PreferredMethod };
                methodsToTry.AddRange(_modelConfig.FallbackMethods);
                
                foreach (var method in methodsToTry.Where(m => m != KeyboardMethod.Unknown))
                {
                    result.TriedMethods.Add(method.ToString());
                    var backend = await TryInitializeBackend(method);
                    
                    if (backend != null)
                    {
                        _activeBackend = backend;
                        result.WorkingMethod = method;
                        result.WorkingBackend = backend;
                        result.StatusMessage = $"Using {backend.Name} for {_modelConfig.ModelName}";
                        _logging.Info($"[KeyboardLightingV2] ✓ Backend initialized: {backend.Name}");
                        break;
                    }
                }
            }
            else
            {
                _logging.Info("[KeyboardLightingV2] No model-specific config found, trying all backends...");
                
                // Try backends in order: WMI BIOS → EC Direct
                var methodsToTry = new[]
                {
                    KeyboardMethod.ColorTable2020,
                    KeyboardMethod.EcDirect
                };
                
                foreach (var method in methodsToTry)
                {
                    result.TriedMethods.Add(method.ToString());
                    var backend = await TryInitializeBackend(method);
                    
                    if (backend != null)
                    {
                        _activeBackend = backend;
                        result.WorkingMethod = method;
                        result.WorkingBackend = backend;
                        result.StatusMessage = $"Using {backend.Name} (auto-detected)";
                        _logging.Info($"[KeyboardLightingV2] ✓ Backend initialized: {backend.Name}");
                        break;
                    }
                }
            }
            
            if (!result.Success)
            {
                result.StatusMessage = $"No working keyboard backend found. Tried: {string.Join(", ", result.TriedMethods)}";
                _logging.Warn($"[KeyboardLightingV2] {result.StatusMessage}");
            }
            
            _lastProbeResult = result;
            return result;
        }
        
        private KeyboardModelConfig? DetectModelConfig()
        {
            try
            {
                // Try to get product ID from system info
                var systemInfo = _systemInfoService?.GetSystemInfo();
                
                // ProductName comes from Win32_BaseBoard.Product (e.g., "8BAD") — this is the
                // HP baseboard product ID that matches our keyboard model database entries.
                // SystemSku comes from Win32_ComputerSystemProduct.SKUNumber which is often a
                // serial-like value (e.g., "5CD349D9KV"), NOT the product ID.
                var productId = systemInfo?.ProductName?.Trim();
                _logging.Info($"[KeyboardLightingV2] Model detection: ProductName='{productId}', SystemSku='{systemInfo?.SystemSku}', Model='{systemInfo?.Model}'");
                
                if (!string.IsNullOrEmpty(productId))
                {
                    var config = KeyboardModelDatabase.GetConfig(productId);
                    if (config != null)
                    {
                        _logging.Info($"[KeyboardLightingV2] Matched by product ID: {productId} → {config.ModelName}");
                        return config;
                    }
                }
                
                // Fallback: try SystemSku in case it's a valid product ID on some systems
                if (!string.IsNullOrEmpty(systemInfo?.SystemSku))
                {
                    var config = KeyboardModelDatabase.GetConfig(systemInfo.SystemSku);
                    if (config != null)
                    {
                        _logging.Info($"[KeyboardLightingV2] Matched by SKU: {systemInfo.SystemSku} → {config.ModelName}");
                        return config;
                    }
                }
                
                // Try by model name
                if (!string.IsNullOrEmpty(systemInfo?.Model))
                {
                    var config = KeyboardModelDatabase.GetConfigByModelName(systemInfo.Model);
                    if (config != null)
                    {
                        _logging.Info($"[KeyboardLightingV2] Matched by model name: {systemInfo.Model} → {config.ModelName}");
                        return config;
                    }
                }
                
                // Return a default based on whether it's an OMEN
                if (systemInfo?.IsHpOmen == true || systemInfo?.IsHpVictus == true)
                {
                    _logging.Info("[KeyboardLightingV2] No specific model match, using default OMEN config");
                    return KeyboardModelDatabase.GetDefaultConfig();
                }
                
                _logging.Info("[KeyboardLightingV2] Not an HP OMEN/Victus system — no keyboard config");
                return null;
            }
            catch (Exception ex)
            {
                _logging.Warn($"[KeyboardLightingV2] Model detection failed: {ex.Message}");
                return KeyboardModelDatabase.GetDefaultConfig();
            }
        }
        
        private async Task<IKeyboardBackend?> TryInitializeBackend(KeyboardMethod method)
        {
            IKeyboardBackend? backend = null;
            
            try
            {
                switch (method)
                {
                    case KeyboardMethod.ColorTable2020:
                    case KeyboardMethod.NewWmi2023:
                        backend = new WmiBiosBackend(_wmiBios, _logging);
                        break;
                        
                    case KeyboardMethod.EcDirect:
                        // Allow EC if:
                        // 1. User explicitly enabled experimental EC keyboard, OR
                        // 2. Model config requires EC as preferred method, OR
                        // 3. Model has verified EC register maps (safe to use without experimental flag)
                        var ecEnabled = _configService?.Config?.ExperimentalEcKeyboardEnabled ?? false;
                        var modelRequiresEc = _modelConfig?.PreferredMethod == KeyboardMethod.EcDirect;
                        var modelHasVerifiedEcRegisters = _modelConfig?.EcColorRegisters != null && 
                            _modelConfig.EcColorRegisters.Length >= 12;
                        
                        if (ecEnabled || modelRequiresEc || modelHasVerifiedEcRegisters)
                        {
                            // Auto-enable PawnIO keyboard writes for verified models
                            if (modelHasVerifiedEcRegisters && !ecEnabled)
                            {
                                Hardware.PawnIOEcAccess.EnableExperimentalKeyboardWrites = true;
                                _logging.Info($"[KeyboardLightingV2] Auto-enabled EC keyboard writes for verified model: {_modelConfig!.ModelName}");
                            }
                            
                            backend = new EcDirectBackend(_ecAccess, _logging, _modelConfig);
                        }
                        else
                        {
                            _logging.Info("[KeyboardLightingV2] Skipping EC backend (not enabled in settings and model EC registers not verified)");
                            return null;
                        }
                        break;
                        
                    case KeyboardMethod.HidPerKey:
                        // TODO: Implement HID per-key backend
                        _logging.Info("[KeyboardLightingV2] HID per-key backend not yet implemented");
                        return null;
                        
                    case KeyboardMethod.BacklightOnly:
                        // No RGB control available
                        _logging.Info("[KeyboardLightingV2] Backlight-only model detected - no RGB control");
                        return null;
                        
                    default:
                        return null;
                }
                
                if (backend != null)
                {
                    var initialized = await backend.InitializeAsync();
                    if (initialized && backend.IsAvailable)
                    {
                        return backend;
                    }
                    else
                    {
                        backend.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"[KeyboardLightingV2] Backend {method} init failed: {ex.Message}");
                backend?.Dispose();
            }
            
            return null;
        }
        
        /// <summary>
        /// Apply a lighting profile to the keyboard.
        /// </summary>
        public async Task<RgbApplyResult> ApplyProfileAsync(LightingProfile profile)
        {
            if (!IsAvailable || _activeBackend == null)
            {
                return new RgbApplyResult
                {
                    FailureReason = "No keyboard backend available"
                };
            }
            
            _logging.Info($"[KeyboardLightingV2] Applying profile: {profile.Name} ({profile.Effect})");
            
            var primaryColor = ParseHexColor(profile.PrimaryColorHex);
            var secondaryColor = ParseHexColor(profile.SecondaryColorHex);
            
            // Map the effect type
            var effect = MapEffect(profile.Effect);
            
            RgbApplyResult result;
            if (effect == KeyboardEffect.Static)
            {
                // For static, set all zones to primary color
                var colors = new Color[] { primaryColor, primaryColor, primaryColor, primaryColor };
                result = await _activeBackend.SetZoneColorsAsync(colors);
            }
            else
            {
                result = await _activeBackend.SetEffectAsync(effect, primaryColor, secondaryColor, (int)(profile.EffectSpeed * 100));
            }
            
            // Set brightness if supported
            await _activeBackend.SetBrightnessAsync((int)profile.Brightness);
            
            TrackResult(result);
            return result;
        }
        
        /// <summary>
        /// Set all 4 zone colors at once.
        /// </summary>
        public async Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            if (!IsAvailable || _activeBackend == null)
            {
                return new RgbApplyResult
                {
                    FailureReason = "No keyboard backend available"
                };
            }
            
            var result = await _activeBackend.SetZoneColorsAsync(zoneColors);
            TrackResult(result);
            return result;
        }
        
        /// <summary>
        /// Set a single zone color.
        /// </summary>
        public async Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            if (!IsAvailable || _activeBackend == null)
            {
                return new RgbApplyResult
                {
                    FailureReason = "No keyboard backend available"
                };
            }
            
            var result = await _activeBackend.SetZoneColorAsync(zone, color);
            TrackResult(result);
            return result;
        }
        
        /// <summary>
        /// Read current zone colors from the keyboard.
        /// </summary>
        public Task<Color[]?> ReadZoneColorsAsync()
        {
            if (!IsAvailable || _activeBackend == null)
                return Task.FromResult<Color[]?>(null);
            
            return _activeBackend.ReadZoneColorsAsync();
        }
        
        /// <summary>
        /// Set keyboard brightness.
        /// </summary>
        public Task<bool> SetBrightnessAsync(int brightness)
        {
            if (!IsAvailable || _activeBackend == null)
                return Task.FromResult(false);
            
            return _activeBackend.SetBrightnessAsync(brightness);
        }
        
        /// <summary>
        /// Turn keyboard backlight on or off.
        /// </summary>
        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            if (!IsAvailable || _activeBackend == null)
                return Task.FromResult(false);
            
            return _activeBackend.SetBacklightEnabledAsync(enabled);
        }
        
        /// <summary>
        /// Run a test pattern to verify the keyboard responds.
        /// Returns result indicating if change was detected.
        /// </summary>
        public async Task<RgbApplyResult> RunTestPatternAsync()
        {
            if (!IsAvailable || _activeBackend == null)
            {
                return new RgbApplyResult
                {
                    FailureReason = "No keyboard backend available"
                };
            }
            
            _logging.Info("[KeyboardLightingV2] Running test pattern...");
            
            // Store original colors if possible
            var originalColors = await ReadZoneColorsAsync();
            
            // Apply test pattern: Red-Green-Blue-White
            var testColors = new Color[]
            {
                Color.Red,
                Color.Green,
                Color.Blue,
                Color.White
            };
            
            var result = await _activeBackend.SetZoneColorsAsync(testColors);
            
            if (result.Success)
            {
                _logging.Info("[KeyboardLightingV2] Test pattern applied successfully");
                
                // Wait a bit then restore if we had original colors
                if (originalColors != null)
                {
                    await Task.Delay(2000);
                    await _activeBackend.SetZoneColorsAsync(originalColors);
                    _logging.Info("[KeyboardLightingV2] Restored original colors");
                }
            }
            else
            {
                _logging.Warn($"[KeyboardLightingV2] Test pattern failed: {result.FailureReason}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Force switch to a specific backend (for testing/debugging).
        /// </summary>
        public async Task<bool> SwitchBackendAsync(KeyboardMethod method)
        {
            _logging.Info($"[KeyboardLightingV2] Switching to backend: {method}");
            
            var newBackend = await TryInitializeBackend(method);
            if (newBackend != null)
            {
                _activeBackend?.Dispose();
                _activeBackend = newBackend;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get telemetry data for debugging.
        /// </summary>
        public string GetTelemetryReport()
        {
            lock (_telemetryLock)
            {
                return $"Keyboard RGB Telemetry:\n" +
                    $"  Backend: {BackendName}\n" +
                    $"  Method: {ActiveMethod}\n" +
                    $"  Model: {_modelConfig?.ModelName ?? "Unknown"}\n" +
                    $"  Success: {_applySuccessCount}, Failure: {_applyFailureCount}\n" +
                    $"  Success Rate: {SuccessRate:F1}%";
            }
        }
        
        private void TrackResult(RgbApplyResult result)
        {
            lock (_telemetryLock)
            {
                if (result.Success)
                    _applySuccessCount++;
                else
                    _applyFailureCount++;
            }
        }
        
        private static Color ParseHexColor(string? hex)
        {
            if (string.IsNullOrEmpty(hex))
                return Color.White;
            
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(0, 2), 16),
                        Convert.ToInt32(hex.Substring(2, 2), 16),
                        Convert.ToInt32(hex.Substring(4, 2), 16));
                }
            }
            catch { }
            
            return Color.White;
        }
        
        private static KeyboardEffect MapEffect(LightingEffectType effectType)
        {
            return effectType switch
            {
                LightingEffectType.Static => KeyboardEffect.Static,
                LightingEffectType.Breathing => KeyboardEffect.Breathing,
                LightingEffectType.ColorCycle => KeyboardEffect.ColorCycle,
                LightingEffectType.Wave => KeyboardEffect.Wave,
                LightingEffectType.Reactive => KeyboardEffect.Reactive,
                _ => KeyboardEffect.Static
            };
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _activeBackend?.Dispose();
            _activeBackend = null;
        }
    }
}
