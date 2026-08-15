using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
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
        private readonly RuntimeEcOperationCoordinator? _ecOperationCoordinator;
        
        private IKeyboardBackend? _activeBackend;
        private KeyboardModelConfig? _modelConfig;
        private KeyboardProbeResult? _lastProbeResult;
        private bool _disposed;
        private readonly SemaphoreSlim _backendOperationLock = new(1, 1);
        
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
            SystemInfoService? systemInfoService = null,
            RuntimeEcOperationCoordinator? ecOperationCoordinator = null)
        {
            _logging = logging;
            _wmiBios = wmiBios;
            _ecAccess = ecAccess;
            _configService = configService;
            _systemInfoService = systemInfoService;
            _ecOperationCoordinator = ecOperationCoordinator;
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

                // HidPerKey FIRST, and its absence from this list was a real bug: a per-key keyboard
                // on any board the model database does not recognise was invisible, because the
                // fallback only ever tried the two four-zone paths. Those "succeed" on a per-key
                // chassis by landing on the light bar, so detection stopped at a backend that
                // cannot touch the keyboard.
                //
                // Putting it first is safe because the per-key backends identify their own device
                // and decline when it is absent - unlike the WMI paths, which answer for whatever
                // surface the firmware wires them to.
                var methodsToTry = new[]
                {
                    KeyboardMethod.HidPerKey,
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
                        var config = KeyboardModelDatabase.GetConfig(productId, systemInfo?.Model);
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
                if (systemInfo?.IsHpVictus == true)
                {
                    _logging.Info("[KeyboardLightingV2] No specific Victus keyboard match, using conservative backlight-only config");
                    return KeyboardModelDatabase.GetDefaultVictusConfig();
                }

                if (systemInfo?.IsHpOmen == true)
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
        
        /// <summary>
        /// The active backend's measured lamp map - every addressable key with its real position
        /// and the HID usage it lights - or empty when the backend cannot supply one.
        ///
        /// EMPTY IS THE NORMAL ANSWER on most hardware, and callers must treat it as "no real
        /// layout is known here" rather than as a failure. Only a backend that has interrogated
        /// the device returns anything; nothing here infers a layout from a model name.
        /// </summary>
        public IReadOnlyList<Hardware.HidLampArray.LampInfo> GetMeasuredKeyMap() =>
            (_activeBackend as DojoPerKeyBackend)?.GetKeyMap()
            ?? (IReadOnlyList<Hardware.HidLampArray.LampInfo>)Array.Empty<Hardware.HidLampArray.LampInfo>();

        /// <summary>
        /// The keyboard's key/LED layout, or null when the board is not in the catalogue.
        ///
        /// FINER THAN <see cref="GetMeasuredKeyMap"/>, and the two do not line up. LampArray reports
        /// 120 lamps on 8D87 where the colour map has 176 LEDs: an F key is one lamp and two LEDs,
        /// Num0 is two lamps and three LEDs, and the Omen and Copilot keys are LEDs with no lamp at
        /// all. So a caller that wants every addressable light has to come through here, not through
        /// the lamp map, and pair it with <see cref="SetLedColorsAsync"/>.
        /// </summary>
        public KeyboardLayout? GetKeyboardLayout() => (_activeBackend as DojoPerKeyBackend)?.Layout;

        /// <summary>
        /// Colour individually addressed keys, leaving every unnamed key alone.
        /// Keys are lamp ids from <see cref="GetMeasuredKeyMap"/>.
        /// </summary>
        public async Task<bool> SetKeyColorsAsync(IReadOnlyDictionary<ushort, Color> keyColors)
        {
            if (_activeBackend is not DojoPerKeyBackend dojo) return false;

            await _backendOperationLock.WaitAsync();
            try
            {
                return dojo.SetKeyColors(keyColors);
            }
            finally
            {
                _backendOperationLock.Release();
            }
        }

        /// <summary>
        /// Colour individual LEDs of the MCU's colour map, addressed by the positions in
        /// <see cref="KeyboardKey.Leds"/>. Every position not named takes <paramref name="background"/>,
        /// because the map is written whole - there is no partial update on this path.
        /// </summary>
        public async Task<bool> SetLedColorsAsync(IReadOnlyDictionary<int, Color> ledColors, Color background)
        {
            if (_activeBackend is not DojoPerKeyBackend dojo) return false;

            await _backendOperationLock.WaitAsync();
            try
            {
                return dojo.SetLedColors(ledColors, background);
            }
            finally
            {
                _backendOperationLock.Release();
            }
        }

        /// <summary>
        /// Set the LampArray intensity used by subsequent per-key colour writes, without repainting.
        /// </summary>
        public bool SetPerKeyBrightness(int brightness)
        {
            if (_activeBackend is not DojoPerKeyBackend dojo)
            {
                // Not a failure to retry - the other backends have no per-write brightness at all.
                // Logged because the caller's only alternative is to show a slider that does
                // nothing and never say so.
                _logging.Info($"[KeyboardLightingV2] Per-key brightness {brightness} not applied: " +
                              $"backend '{_activeBackend?.Name ?? "none"}' scales nothing per write");
                return false;
            }

            dojo.SetLampIntensity(brightness);
            return true;
        }

        /// <summary>Whether this exact keyboard was confirmed on hardware, rather than matched from
        /// HP's device table. False is not a failure — it means "expected to work, unproven here".</summary>
        public bool IsVerifiedPerKeyDevice =>
            (_activeBackend as DojoPerKeyBackend)?.IsVerifiedDevice ?? false;

        /// <summary>USB identity of the per-key keyboard, for display and field reports.</summary>
        public string PerKeyDeviceIdentity =>
            (_activeBackend as DojoPerKeyBackend)?.DeviceIdentity ?? string.Empty;

        /// <summary>Whether the active backend can run the device's own animation engine.</summary>
        public bool SupportsDeviceEffects =>
            (_activeBackend as DojoPerKeyBackend)?.SupportsDeviceEffects ?? false;

        /// <summary>
        /// Install one of the device's built-in effects, with the fields that effect consumes.
        ///
        /// Distinct from <see cref="IKeyboardBackend.SetEffectAsync"/>, whose four-effect vocabulary
        /// is all the shared interface can express. The device has twelve.
        /// </summary>
        public async Task<RgbApplyResult> SetDeviceEffectAsync(Hardware.DojoKeyboardMcu.EffectRecord record)
        {
            if (_activeBackend is not DojoPerKeyBackend dojo)
                return new RgbApplyResult { FailureReason = "No device-effect capable backend is active" };

            await _backendOperationLock.WaitAsync();
            try
            {
                return dojo.SetDeviceEffect(record);
            }
            finally
            {
                _backendOperationLock.Release();
            }
        }

        /// <summary>The effect the device is currently holding, or null if it will not answer.</summary>
        public Hardware.DojoKeyboardMcu.EffectRecord? ReadDeviceEffect() =>
            (_activeBackend as DojoPerKeyBackend)?.ReadDeviceEffect();

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
                            
                            backend = new EcDirectBackend(_ecAccess, _logging, _modelConfig, _ecOperationCoordinator);
                        }
                        else
                        {
                            _logging.Info("[KeyboardLightingV2] Skipping EC backend (not enabled in settings and model EC registers not verified)");
                            return null;
                        }
                        break;
                        
                    case KeyboardMethod.HidPerKey:
                        // Two per-key backends, for two different keyboards - not a primary and a
                        // fallback. DojoPerKeyBackend serves Darfon 0D62:54BF (OMEN MAX 16, board
                        // 8D87), whose protocol was measured on hardware; HidPerKeyBackend serves
                        // the 0x03F0 OMEN family and speaks a command set this keyboard does not.
                        // Neither can stand in for the other, so each declines cleanly when its
                        // device is absent and the loop moves on.
                        var dojo = new DojoPerKeyBackend(_logging);
                        if (await dojo.InitializeAsync() && dojo.IsAvailable)
                        {
                            return dojo;
                        }

                        dojo.Dispose();
                        backend = new HidPerKeyBackend(_logging);
                        break;
                        
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
            await _backendOperationLock.WaitAsync();
            try
            {
                if (!IsAvailable || _activeBackend == null)
                {
                    return new RgbApplyResult
                    {
                        FailureReason = "No keyboard backend available"
                    };
                }

                _logging.Info($"[KeyboardLightingV2] Applying profile: {profile.Name} ({profile.Effect})");

                var targetBrightness = Math.Clamp((int)profile.Brightness, 0, 100);
                var effect = MapEffect(profile.Effect);

                var primaryColor = ParseHexColor(profile.PrimaryColorHex);
                var secondaryColor = ParseHexColor(profile.SecondaryColorHex);

                var result = await ApplyWithBackendFallbackAsync(async backend =>
                {
                    // Some BIOS revisions (notably on 8BCD/F.31 reports) can reset visible keyboard
                    // state when brightness is written after color-table updates. Apply brightness first,
                    // then write colors/effects as the final command.
                    await backend.SetBrightnessAsync(targetBrightness);
                    await Task.Delay(50);

                    if (effect == KeyboardEffect.Static)
                    {
                        var colors = new Color[] { primaryColor, primaryColor, primaryColor, primaryColor };
                        return await backend.SetZoneColorsAsync(colors);
                    }

                    return await backend.SetEffectAsync(effect, primaryColor, secondaryColor, (int)(profile.EffectSpeed * 100));
                }, $"profile {profile.Name}");

                TrackResult(result);
                return result;
            }
            finally
            {
                _backendOperationLock.Release();
            }
        }
        
        /// <summary>
        /// Set all 4 zone colors at once.
        /// </summary>
        public async Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors)
        {
            await _backendOperationLock.WaitAsync();
            try
            {
                if (!IsAvailable || _activeBackend == null)
                {
                    return new RgbApplyResult
                    {
                        FailureReason = "No keyboard backend available"
                    };
                }

                var result = await ApplyWithBackendFallbackAsync(
                    backend => backend.SetZoneColorsAsync(zoneColors),
                    "zone color apply");
                TrackResult(result);
                return result;
            }
            finally
            {
                _backendOperationLock.Release();
            }
        }
        
        /// <summary>
        /// Set a single zone color.
        /// </summary>
        public async Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color)
        {
            await _backendOperationLock.WaitAsync();
            try
            {
                if (!IsAvailable || _activeBackend == null)
                {
                    return new RgbApplyResult
                    {
                        FailureReason = "No keyboard backend available"
                    };
                }

                var result = await ApplyWithBackendFallbackAsync(
                    backend => backend.SetZoneColorAsync(zone, color),
                    $"zone {zone} color apply");
                TrackResult(result);
                return result;
            }
            finally
            {
                _backendOperationLock.Release();
            }
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
            return ApplyBooleanWithBackendFallbackAsync(
                backend => backend.SetBrightnessAsync(brightness),
                $"brightness {Math.Clamp(brightness, 0, 100)}");
        }
        
        /// <summary>
        /// Turn keyboard backlight on or off.
        /// </summary>
        public Task<bool> SetBacklightEnabledAsync(bool enabled)
        {
            return ApplyBooleanWithBackendFallbackAsync(
                backend => backend.SetBacklightEnabledAsync(enabled),
                enabled ? "backlight enable" : "backlight disable");
        }
        
        /// <summary>
        /// Run a test pattern to verify the keyboard responds.
        /// Returns result indicating if change was detected.
        /// </summary>
        public async Task<RgbApplyResult> RunTestPatternAsync()
        {
            await _backendOperationLock.WaitAsync();
            try
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
            
                var result = await ApplyWithBackendFallbackAsync(
                    backend => backend.SetZoneColorsAsync(testColors),
                    "test pattern");
            
                if (result.Success)
                {
                    _logging.Info("[KeyboardLightingV2] Test pattern applied successfully");
                
                    // Wait a bit then restore if we had original colors
                    if (originalColors != null)
                    {
                        await Task.Delay(2000);
                        await ApplyWithBackendFallbackAsync(
                            backend => backend.SetZoneColorsAsync(originalColors),
                            "test pattern restore");
                        _logging.Info("[KeyboardLightingV2] Restored original colors");
                    }
                }
                else
                {
                    _logging.Warn($"[KeyboardLightingV2] Test pattern failed: {result.FailureReason}");
                }
            
                return result;
            }
            finally
            {
                _backendOperationLock.Release();
            }
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

        private async Task<RgbApplyResult> ApplyWithBackendFallbackAsync(
            Func<IKeyboardBackend, Task<RgbApplyResult>> applyAction,
            string operationName)
        {
            if (!IsAvailable || _activeBackend == null)
            {
                return new RgbApplyResult
                {
                    FailureReason = "No keyboard backend available"
                };
            }

            var activeBackend = _activeBackend;
            var activeResult = await applyAction(activeBackend);
            if (activeResult.Success)
            {
                return activeResult;
            }

            foreach (var method in GetFallbackMethods(activeBackend.Method))
            {
                var fallbackBackend = await TryInitializeBackend(method);
                if (fallbackBackend == null)
                {
                    continue;
                }

                var fallbackResult = await applyAction(fallbackBackend);
                if (fallbackResult.Success)
                {
                    _logging.Warn($"[KeyboardLightingV2] {operationName} succeeded via fallback backend {fallbackBackend.Name} after {activeBackend.Name} failed");
                    _activeBackend = fallbackBackend;
                    activeBackend.Dispose();
                    return fallbackResult;
                }

                fallbackBackend.Dispose();
            }

            _logging.Warn($"[KeyboardLightingV2] {operationName} failed on active backend {activeBackend.Name}");
            return activeResult;
        }

        private IEnumerable<KeyboardMethod> GetFallbackMethods(KeyboardMethod currentMethod)
        {
            var methods = new List<KeyboardMethod>();

            if (_modelConfig?.FallbackMethods is { Length: > 0 })
            {
                methods.AddRange(_modelConfig.FallbackMethods);
            }

            if (_modelConfig?.KeyboardType is KeyboardType.FourZone or KeyboardType.FourZoneTkl or KeyboardType.Unknown)
            {
                methods.AddRange(new[]
                {
                    KeyboardMethod.NewWmi2023,
                    KeyboardMethod.ColorTable2020,
                    KeyboardMethod.EcDirect
                });
            }

            return methods
                .Where(method => method != currentMethod && method != KeyboardMethod.Unknown && method != KeyboardMethod.BacklightOnly)
                .Distinct();
        }

        private async Task<bool> ApplyBooleanWithBackendFallbackAsync(
            Func<IKeyboardBackend, Task<bool>> applyAction,
            string operationName)
        {
            await _backendOperationLock.WaitAsync();
            try
            {
                if (!IsAvailable || _activeBackend == null)
                {
                    return false;
                }

                var activeBackend = _activeBackend;
                if (await applyAction(activeBackend))
                {
                    return true;
                }

                foreach (var method in GetFallbackMethods(activeBackend.Method))
                {
                    var fallbackBackend = await TryInitializeBackend(method);
                    if (fallbackBackend == null)
                    {
                        continue;
                    }

                    if (await applyAction(fallbackBackend))
                    {
                        _logging.Warn($"[KeyboardLightingV2] {operationName} succeeded via fallback backend {fallbackBackend.Name} after {activeBackend.Name} failed");
                        _activeBackend = fallbackBackend;
                        activeBackend.Dispose();
                        return true;
                    }

                    fallbackBackend.Dispose();
                }

                _logging.Warn($"[KeyboardLightingV2] {operationName} failed on active backend {activeBackend.Name}");
                return false;
            }
            finally
            {
                _backendOperationLock.Release();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeyboardLightingV2] ParseHexColor failed for '{hex}': {ex.Message}");
            }
            
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
            _backendOperationLock.Dispose();
        }
    }
}
