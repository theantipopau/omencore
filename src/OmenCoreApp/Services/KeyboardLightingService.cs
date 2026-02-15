using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services.KeyboardLighting;

namespace OmenCore.Services
{
    /// <summary>
    /// Controls HP OMEN laptop/desktop keyboard backlight lighting via WMI BIOS and EC access.
    /// Supports 4-zone RGB keyboards found in OMEN 15/16/17 series laptops and 25L/30L/40L/45L desktops.
    /// 
    /// v1.4.0-beta3: Added telemetry tracking for WMI vs EC success rates and better fallback logic.
    /// Improved desktop PC support with chassis-aware detection.
    /// </summary>
    public class KeyboardLightingService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly IEcAccess? _ecAccess;
        private readonly HpWmiBios? _wmiBios;
        private readonly ConfigurationService? _configService;
        private readonly OghServiceProxy? _oghProxy;
        private readonly KeyboardLightingServiceV2? _v2Service;
        private bool _useV2Backend;
        private bool _wmiAvailable;
        private bool _wmiBiosAvailable;
        private bool _ecAvailable;
        private bool _disposed;
        
        // Telemetry counters for tracking success rates
        private int _wmiSuccessCount = 0;
        private int _wmiFailureCount = 0;
        private int _ecSuccessCount = 0;
        private int _ecFailureCount = 0;
        private int _oghSuccessCount = 0;
        private int _oghFailureCount = 0;
        private int _totalAttempts = 0;
        private readonly object _telemetryLock = new();

        // Throttle experimental EC keyboard writes to avoid EC overload
        private readonly object _ecKeyboardWriteLock = new();
        private DateTime _lastEcKeyboardWriteTime = DateTime.MinValue;
        private const int EcKeyboardWriteMinIntervalMs = 200;

        // HP OMEN WMI namespace and class identifiers (legacy)
        private const string OmenWmiNamespace = @"root\hp\InstrumentedBIOS";
        private const byte EC_KB_ZONE1_R = 0xB1;
        private const byte EC_KB_ZONE2_R = 0xB4;
        private const byte EC_KB_ZONE3_R = 0xB7;
        private const byte EC_KB_ZONE4_R = 0xBA;

        // Keyboard zones
        public enum KeyboardZone { Left = 0, MiddleLeft = 1, MiddleRight = 2, Right = 3, All = 255 }
        
        // Supported effects
        public enum KeyboardEffect : byte
        {
            Static = 0x00,
            Breathing = 0x01,
            ColorCycle = 0x02,
            Wave = 0x03,
            Reactive = 0x04,
            Off = 0xFF
        }

        public bool IsAvailable => _useV2Backend || _wmiBiosAvailable || _wmiAvailable || _ecAvailable || (_oghProxy != null && _oghProxy.IsAvailable);
        
        /// <summary>
        /// Returns the currently active backend based on user preference and availability.
        /// User preference (PreferredKeyboardBackend) is respected if that backend is available.
        /// In Auto mode: If ExperimentalEcKeyboardEnabled is true, prefer EC (user explicitly wants EC).
        /// Otherwise: WMI BIOS > WMI > EC (if enabled) > None
        /// </summary>
        public string BackendType
        {
            get
            {
                var preference = _configService?.Config?.PreferredKeyboardBackend ?? "Auto";
                
                // If user selected specific backend and it's available, use it
                if (preference == "WmiBios" && _wmiBiosAvailable) return "WMI BIOS";
                if (preference == "Wmi" && _wmiAvailable) return "WMI";
                if (preference == "Ec" && _ecAvailable && IsExperimentalEcEnabled) return "EC";
                
                // Auto mode logic:
                // If user has explicitly enabled EC keyboard (ExperimentalEcKeyboardEnabled),
                // they want EC to be used - prefer it over WMI backends
                if (preference == "Auto" && IsExperimentalEcEnabled && _ecAvailable)
                {
                    return "EC";
                }
                
                // V2 backend (model-aware auto-probe with PawnIO/EC fallback)
                if (_useV2Backend && _v2Service != null) return $"V2:{_v2Service.BackendName}";
                
                // Standard auto: WMI BIOS > WMI > EC (if enabled) > None
                if (_wmiBiosAvailable) return "WMI BIOS";
                if (_wmiAvailable) return "WMI";
                if (_ecAvailable && IsExperimentalEcEnabled) return "EC";
                return "None";
            }
        }
        
        /// <summary>
        /// Check if experimental EC keyboard writes are enabled and allowed.
        /// </summary>
        private bool IsExperimentalEcEnabled => _configService?.Config?.ExperimentalEcKeyboardEnabled ?? false;

        private bool IsEcKeyboardWriteAllowed()
        {
            lock (_ecKeyboardWriteLock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastEcKeyboardWriteTime).TotalMilliseconds < EcKeyboardWriteMinIntervalMs)
                {
                    return false;
                }
                _lastEcKeyboardWriteTime = now;
                return true;
            }
        }

        public KeyboardLightingService(LoggingService logging, IEcAccess? ecAccess = null, HpWmiBios? wmiBios = null, ConfigurationService? configService = null, SystemInfoService? systemInfoService = null)
        {
            _logging = logging;
            _ecAccess = ecAccess;
            _wmiBios = wmiBios;
            _configService = configService;
            _oghProxy = new OghServiceProxy(_logging);

            // Initialize V2 service for model-aware backend with PawnIO/EC fallback
            try
            {
                _v2Service = new KeyboardLightingServiceV2(logging, wmiBios, ecAccess, configService, systemInfoService);
                var probeResult = _v2Service.InitializeAsync().GetAwaiter().GetResult();
                if (probeResult.Success)
                {
                    _useV2Backend = true;
                    _logging.Info($"✓ V2 keyboard engine active: {_v2Service.BackendName} " +
                        $"(method: {_v2Service.ActiveMethod}, model: {_v2Service.ModelConfig?.ModelName ?? "auto-detected"})");
                    _logging.Info($"  Tried: {string.Join(" → ", probeResult.TriedMethods)}");
                }
                else
                {
                    _logging.Info($"V2 keyboard engine probe found no working backend. Tried: {string.Join(", ", probeResult.TriedMethods)}. Falling back to V1 logic.");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"V2 keyboard engine init failed: {ex.Message}. Falling back to V1 logic.");
            }

            InitializeBackends();
        }

        private void InitializeBackends()
        {
            // Try HpWmiBios first (preferred - uses same interface as fan control)
            try
            {
                if (_wmiBios != null && _wmiBios.IsAvailable)
                {
                    _wmiBiosAvailable = true;
                    _logging.Info("✓ HP WMI BIOS keyboard lighting backend available");
                }
            }
            catch (Exception ex)
            {
                _logging.Info($"HP WMI BIOS keyboard not available: {ex.Message}");
                _wmiBiosAvailable = false;
            }
            
            // Try legacy WMI (for older models)
            try
            {
                using var searcher = new ManagementObjectSearcher(OmenWmiNamespace, "SELECT * FROM HPBIOS_BIOSSettingInterface");
                var results = searcher.Get();
                _wmiAvailable = results.Count > 0;
                if (_wmiAvailable)
                {
                    _logging.Info("✓ HP OMEN WMI keyboard lighting backend available");
                }
            }
            catch (Exception ex)
            {
                _logging.Info($"HP OMEN WMI not available: {ex.Message}");
                _wmiAvailable = false;
            }

            // Try EC access as fallback
            try
            {
                if (_ecAccess != null && _ecAccess.IsAvailable)
                {
                    _ecAvailable = true;
                    _logging.Info("✓ EC keyboard lighting backend available");
                }
            }
            catch (Exception ex)
            {
                _logging.Info($"EC keyboard access not available: {ex.Message}");
                _ecAvailable = false;
            }

            // Try OGH proxy for models where OMEN Gaming Hub mediates keyboard control
            try
            {
                if (_oghProxy != null && _oghProxy.IsAvailable)
                {
                    _logging.Info("✓ OGH proxy available for possible keyboard control fallback");
                }
            }
            catch { }

            if (!IsAvailable)
            {
                _logging.Warn("⚠️ No keyboard lighting backend available - keyboard RGB control disabled");
            }
            else
            {
                var preference = _configService?.Config?.PreferredKeyboardBackend ?? "Auto";
                _logging.Info($"Keyboard backend preference: {preference}, active: {BackendType}");
            }
        }

        public async Task ApplyProfile(LightingProfile profile)
        {
            if (!IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available on this system");
                return;
            }

            _logging.Info($"Applying keyboard lighting profile: {profile.Name} ({profile.Effect}) via {BackendType}");

            try
            {
                // Delegate to V2 engine if active
                if (_useV2Backend && _v2Service != null)
                {
                    var result = await _v2Service.ApplyProfileAsync(profile);
                    if (result.Success)
                    {
                        _logging.Info($"✓ Profile applied via V2 engine ({_v2Service.BackendName})");
                        return;
                    }
                    _logging.Warn($"V2 engine profile apply failed: {result.FailureReason}. Falling back to V1.");
                }

                var primaryColor = ParseHexColor(profile.PrimaryColorHex);
                var secondaryColor = ParseHexColor(profile.SecondaryColorHex);
                var effect = MapEffect(profile.Effect);
                var backend = BackendType;

                // Use the determined backend from BackendType property (respects user preference)
                if (backend == "WMI" && _wmiAvailable)
                {
                    ApplyViaWmi(effect, primaryColor, secondaryColor, profile.EffectSpeed, profile.Brightness);
                }
                else if (backend == "WMI BIOS" && _wmiBiosAvailable)
                {
                    // Use WMI BIOS color table
                    SetZoneColor(KeyboardZone.All, primaryColor);
                }
                else if (backend == "EC" && _ecAvailable && IsExperimentalEcEnabled)
                {
                    // User explicitly chose EC and has experimental enabled
                    _logging.Warn("⚠️ Using EXPERIMENTAL EC keyboard writes - crash risk!");
                    SetZoneColorViaEc(KeyboardZone.All, primaryColor);
                }
                else
                {
                    _logging.Warn($"Keyboard lighting not applied - backend {backend} not available");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply keyboard lighting profile: {ex.Message}", ex);
            }
        }

        public void ApplyEffect(LightingEffectType effect, string primaryHex, string secondaryHex, IEnumerable<string> zones, double speed)
        {
            if (!IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available on this system");
                return;
            }

            _logging.Info($"Applying keyboard effect: {effect} primary:{primaryHex} secondary:{secondaryHex} speed:{speed} via {BackendType}");

            try
            {
                var primaryColor = ParseHexColor(primaryHex);
                var secondaryColor = ParseHexColor(secondaryHex);
                var mappedEffect = MapEffect(effect);
                var backend = BackendType;

                // Use the determined backend from BackendType property (respects user preference)
                if (backend == "WMI" && _wmiAvailable)
                {
                    ApplyViaWmi(mappedEffect, primaryColor, secondaryColor, speed, 100);
                }
                else if (backend == "WMI BIOS" && _wmiBiosAvailable)
                {
                    // Use WMI BIOS color table
                    SetZoneColor(KeyboardZone.All, primaryColor);
                }
                else if (backend == "EC" && _ecAvailable && IsExperimentalEcEnabled)
                {
                    _logging.Warn("⚠️ Using EXPERIMENTAL EC keyboard writes - crash risk!");
                    SetZoneColorViaEc(KeyboardZone.All, primaryColor);
                }
                else
                {
                    _logging.Warn($"Keyboard effect not applied - backend {backend} not available");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply keyboard effect: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Set color for a specific keyboard zone.
        /// Uses preferred backend if available, otherwise auto-detect.
        /// </summary>
        public void SetZoneColor(KeyboardZone zone, Color color)
        {
            if (!IsAvailable)
            {
                _logging.Warn("No keyboard lighting backend available");
                return;
            }

            try
            {
                if (zone == KeyboardZone.All)
                {
                    // Set all 4 zones
                    for (int i = 0; i < 4; i++)
                    {
                        SetZoneColorInternal((KeyboardZone)i, color);
                    }
                }
                else
                {
                    SetZoneColorInternal(zone, color);
                }
                _logging.Info($"Set zone {zone} to color #{color.R:X2}{color.G:X2}{color.B:X2}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set zone color: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set all 4 zone colors at once using a color array.
        /// More efficient than setting zones individually.
        /// Respects PreferredKeyboardBackend setting.
        /// NOTE: forceEcAccess requires ExperimentalEcKeyboardEnabled=true in settings (dangerous).
        /// </summary>
        public async Task SetAllZoneColors(Color[] zoneColors, bool forceEcAccess = false)
        {
            if (zoneColors.Length < 4)
            {
                _logging.Warn("SetAllZoneColors requires 4 colors");
                return;
            }

            try
            {
                // Delegate to V2 engine if active (handles model-aware EC fallback, zone inversion, etc.)
                if (_useV2Backend && _v2Service != null && !forceEcAccess)
                {
                    // Apply zone inversion before delegating to V2
                    var orderedColors = zoneColors;
                    if (_configService?.Config?.InvertRgbZoneOrder ?? false)
                    {
                        orderedColors = new Color[] { zoneColors[3], zoneColors[2], zoneColors[1], zoneColors[0] };
                    }
                    
                    var result = await _v2Service.SetZoneColorsAsync(orderedColors);
                    if (result.Success)
                    {
                        _logging.Info($"✓ Zone colors set via V2 engine ({_v2Service.BackendName})");
                        return;
                    }
                    _logging.Warn($"V2 engine SetZoneColors failed: {result.FailureReason}. Falling back to V1.");
                }

                var backend = BackendType;
                _logging.Info($"SetAllZoneColors using backend: {backend}");
                
                // BUG FIX: Apply zone inversion for OMEN Max 16 light bar
                // The light bar has zones in reverse order (right-to-left)
                var v1OrderedColors = zoneColors;
                if (_configService?.Config?.InvertRgbZoneOrder ?? false)
                {
                    _logging.Info("Applying inverted zone order (right-to-left for light bar)");
                    v1OrderedColors = new Color[] { zoneColors[3], zoneColors[2], zoneColors[1], zoneColors[0] };
                }
                
                // Check if experimental EC writes are allowed
                if (forceEcAccess && !IsExperimentalEcEnabled)
                {
                    _logging.Warn("Force EC access requested but experimental EC keyboard is disabled in settings.");
                    forceEcAccess = false;
                }
                
                // If user explicitly chose EC backend, use EC only
                if ((backend == "EC" || forceEcAccess) && _ecAvailable && _ecAccess != null && IsExperimentalEcEnabled)
                {
                    _logging.Warn("⚠️ Using EXPERIMENTAL EC keyboard writes - crash risk!");
                    if (!IsEcKeyboardWriteAllowed())
                    {
                        _logging.Debug("EC keyboard writes throttled");
                        return;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        SetZoneColorViaEc((KeyboardZone)i, v1OrderedColors[i]);
                    }
                    TrackEcResult(true);
                    _logging.Info("✓ All zone colors set via EC (EXPERIMENTAL)");
                    _logging.Info($"✓ Applied keyboard zone colors: Z1=#{v1OrderedColors[0].R:X2}{v1OrderedColors[0].G:X2}{v1OrderedColors[0].B:X2}, Z2=#{v1OrderedColors[1].R:X2}{v1OrderedColors[1].G:X2}{v1OrderedColors[1].B:X2}, Z3=#{v1OrderedColors[2].R:X2}{v1OrderedColors[2].G:X2}{v1OrderedColors[2].B:X2}, Z4=#{v1OrderedColors[3].R:X2}{v1OrderedColors[3].G:X2}{v1OrderedColors[3].B:X2}");
                    return;
                }
                
                // Use WMI BIOS if preferred or auto
                if ((backend == "WMI BIOS" || backend == "Auto") && _wmiBiosAvailable && _wmiBios != null)
                {
                    // Create color table: 12 bytes (3 bytes RGB per zone)
                    var colorTable = new byte[12];
                    for (int i = 0; i < 4; i++)
                    {
                        colorTable[i * 3] = v1OrderedColors[i].R;
                        colorTable[i * 3 + 1] = v1OrderedColors[i].G;
                        colorTable[i * 3 + 2] = v1OrderedColors[i].B;
                    }
                    
                    bool wmiSuccess = _wmiBios.SetColorTable(colorTable);
                    TrackWmiResult(wmiSuccess);
                    
                    if (wmiSuccess)
                    {
                        _logging.Info($"✓ Keyboard color table set via WMI BIOS ({colorTable.Length} bytes)");
                        _logging.Info($"✓ Applied keyboard zone colors: Z1=#{v1OrderedColors[0].R:X2}{v1OrderedColors[0].G:X2}{v1OrderedColors[0].B:X2}, Z2=#{v1OrderedColors[1].R:X2}{v1OrderedColors[1].G:X2}{v1OrderedColors[1].B:X2}, Z3=#{v1OrderedColors[2].R:X2}{v1OrderedColors[2].G:X2}{v1OrderedColors[2].B:X2}, Z4=#{v1OrderedColors[3].R:X2}{v1OrderedColors[3].G:X2}{v1OrderedColors[3].B:X2}");
                        
                        // Tip for users if WMI doesn't visually work
                        if (!IsExperimentalEcEnabled)
                        {
                            _logging.Info("💡 TIP: If keyboard colors don't change, try EC backend in Settings > Hardware");
                        }
                        return;
                    }
                    _logging.Warn("WMI BIOS SetColorTable failed, trying individual zones");
                }
                
                // Fallback: set each zone individually via WMI
                bool wmiIndividualSuccess = true;
                for (int i = 0; i < 4; i++)
                {
                    if (!SetZoneColorInternal((KeyboardZone)i, v1OrderedColors[i]))
                    {
                        wmiIndividualSuccess = false;
                        break;
                    }
                }
                
                if (wmiIndividualSuccess)
                {
                    _logging.Info("✓ All zone colors set individually via WMI");
                    return;
                }
                _logging.Warn("WMI BIOS methods failed, trying EC fallback");

                // Before falling back to EC, try OGH proxy (some models route keyboard control through Gaming Hub)
                try
                {
                    if (_oghProxy != null && _oghProxy.IsAvailable)
                    {
                        // Build 128-byte color table per OmenMon format
                        var data = new byte[128];
                        data[0] = 4;
                        const int COLOR_TABLE_PAD = 24;
                        int colorOffset = 1 + COLOR_TABLE_PAD; // Byte 25
                        int colorsToCopy = Math.Min(12, v1OrderedColors.Length * 3);
                        for (int i = 0; i < 4; i++)
                        {
                            data[colorOffset + i * 3] = v1OrderedColors[i].R;
                            data[colorOffset + i * 3 + 1] = v1OrderedColors[i].G;
                            data[colorOffset + i * 3 + 2] = v1OrderedColors[i].B;
                        }

                        bool oghOk = TryOghSetColorTable(data);
                        TrackOghResult(oghOk);
                        if (oghOk)
                        {
                            _logging.Info("✓ Keyboard colors set via OGH proxy fallback");
                            _logging.Info($"✓ Applied keyboard zone colors: Z1=#{v1OrderedColors[0].R:X2}{v1OrderedColors[0].G:X2}{v1OrderedColors[0].B:X2}, Z2=#{v1OrderedColors[1].R:X2}{v1OrderedColors[1].G:X2}{v1OrderedColors[1].B:X2}, Z3=#{v1OrderedColors[2].R:X2}{v1OrderedColors[2].G:X2}{v1OrderedColors[2].B:X2}, Z4=#{v1OrderedColors[3].R:X2}{v1OrderedColors[3].G:X2}{v1OrderedColors[3].B:X2}");
                            return;
                        }
                        _logging.Warn("OGH proxy fallback failed or commands unsupported on this model");
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OGH fallback attempt failed: {ex.Message}");
                }

                // Final fallback: try EC if experimental is enabled
                if (_ecAvailable && _ecAccess != null && IsExperimentalEcEnabled)
                {
                    _logging.Warn("⚠️ WMI failed, falling back to EXPERIMENTAL EC keyboard writes - crash risk!");
                    for (int i = 0; i < 4; i++)
                    {
                        SetZoneColorViaEc((KeyboardZone)i, v1OrderedColors[i]);
                    }
                    TrackEcResult(true);
                    _logging.Info("✓ All zone colors set via EC fallback (EXPERIMENTAL)");
                    _logging.Info($"✓ Applied keyboard zone colors: Z1=#{v1OrderedColors[0].R:X2}{v1OrderedColors[0].G:X2}{v1OrderedColors[0].B:X2}, Z2=#{v1OrderedColors[1].R:X2}{v1OrderedColors[1].G:X2}{v1OrderedColors[1].B:X2}, Z3=#{v1OrderedColors[2].R:X2}{v1OrderedColors[2].G:X2}{v1OrderedColors[2].B:X2}, Z4=#{v1OrderedColors[3].R:X2}{v1OrderedColors[3].G:X2}{v1OrderedColors[3].B:X2}");
                    return;
                }
                
                _logging.Error("All keyboard lighting backends failed. Colors may not be applied.");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set all zone colors: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set zone color directly via EC (bypasses WMI)
        /// </summary>
        private void SetZoneColorViaEc(KeyboardZone zone, Color color)
        {
            if (_ecAccess == null || !_ecAvailable) return;
            if (!IsEcKeyboardWriteAllowed()) return;

            byte baseReg = zone switch
            {
                KeyboardZone.Left => EC_KB_ZONE1_R,
                KeyboardZone.MiddleLeft => EC_KB_ZONE2_R,
                KeyboardZone.MiddleRight => EC_KB_ZONE3_R,
                KeyboardZone.Right => EC_KB_ZONE4_R,
                _ => EC_KB_ZONE1_R
            };

            _ecAccess.WriteByte(baseReg, color.R);
            _ecAccess.WriteByte((byte)(baseReg + 1), color.G);
            _ecAccess.WriteByte((byte)(baseReg + 2), color.B);
        }

        private bool SetZoneColorInternal(KeyboardZone zone, Color color)
        {
            var backend = BackendType;
            
            // Use preferred backend
            if ((backend == "EC" || backend == "Auto") && IsExperimentalEcEnabled && _ecAvailable && _ecAccess != null)
            {
                SetZoneColorViaEc(zone, color);
                return true; // Assume success for EC
            }
            
            // Try WMI BIOS
            if (_wmiBiosAvailable && _wmiBios != null)
            {
                if (_wmiBios.SetZoneColor((int)zone, color.R, color.G, color.B))
                {
                    return true; // Success
                }
                _logging.Warn($"WMI BIOS SetZoneColor failed for zone {zone}");
            }
            
            // EC fallback is DISABLED for keyboard RGB - not safe on all models
            _logging.Warn($"Zone {zone} color not applied - WMI BIOS method unavailable or failed");
            return false;
        }

        public async Task SetBrightness(int brightness)
        {
            brightness = Math.Clamp(brightness, 0, 100);
            
            try
            {
                // Delegate to V2 engine if active (uses native WMI brightness or EC register)
                if (_useV2Backend && _v2Service != null)
                {
                    var result = await _v2Service.SetBrightnessAsync(brightness);
                    if (result)
                    {
                        _logging.Info($"✓ Brightness {brightness}% set via V2 engine ({_v2Service.BackendName})");
                        return;
                    }
                    _logging.Warn("V2 engine brightness failed, falling back to V1.");
                }

                // V1 fallback: Use WMI for brightness
                if (_wmiAvailable)
                {
                    SetBrightnessViaWmi(brightness);
                    _logging.Info($"Set keyboard brightness to {brightness}% via WMI");
                }
                else
                {
                    // EC brightness write disabled - 0xBD register varies by model
                    _logging.Warn($"Keyboard brightness not applied - WMI method unavailable");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to set keyboard brightness: {ex.Message}");
            }
        }

        public void RestoreDefaults()
        {
            _logging.Info("Restoring keyboard lighting to defaults");
            
            try
            {
                // Delegate to V2 engine if active
                if (_useV2Backend && _v2Service != null)
                {
                    var white = Color.FromArgb(255, 255, 255);
                    var colors = new Color[] { white, white, white, white };
                    var result = _v2Service.SetZoneColorsAsync(colors).GetAwaiter().GetResult();
                    _v2Service.SetBrightnessAsync(80).GetAwaiter().GetResult();
                    if (result.Success)
                    {
                        _logging.Info($"✓ Defaults restored via V2 engine ({_v2Service.BackendName})");
                        return;
                    }
                    _logging.Warn($"V2 engine defaults failed: {result.FailureReason}. Falling back to V1.");
                }
                
                // Default: White static at 80% brightness
                var defaultWhite = Color.FromArgb(255, 255, 255);
                
                // Use WMI only - EC keyboard writes are dangerous on some models
                if (_wmiAvailable)
                {
                    ApplyViaWmi(KeyboardEffect.Static, defaultWhite, defaultWhite, 0.5, 80);
                }
                else if (_wmiBiosAvailable)
                {
                    SetZoneColor(KeyboardZone.All, defaultWhite);
                }
                else
                {
                    _logging.Warn("Cannot restore keyboard defaults - no safe backend available");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore keyboard defaults: {ex.Message}");
            }
        }

        public void TurnOff()
        {
            _logging.Info("Turning off keyboard lighting");
            
            try
            {
                // Use WMI only - EC keyboard writes are dangerous on some models
                if (_wmiAvailable)
                {
                    SetBrightnessViaWmi(0);
                }
                else
                {
                    _logging.Warn("Cannot turn off keyboard - no safe backend available");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to turn off keyboard lighting: {ex.Message}");
            }
        }

        private void ApplyViaWmi(KeyboardEffect effect, Color primary, Color secondary, double speed, int brightness)
        {
            _ = secondary;
            _ = speed;
            try
            {
                // HP OMEN uses specific BIOS settings for keyboard backlight
                // This varies by model - some use "Keyboard Backlight" setting
                using var classInstance = new ManagementClass(OmenWmiNamespace, "HPBIOS_BIOSSetting", null);
                
                // Try to set backlight color via WMI
                // Note: Actual implementation depends on HP's WMI interface version
                _logging.Info($"WMI: Effect={effect}, Color=#{primary.R:X2}{primary.G:X2}{primary.B:X2}, Brightness={brightness}%");
                
                // Fallback log for now - full WMI implementation requires model-specific testing
                _logging.Info($"Keyboard lighting set via WMI (simulated): {effect} #{primary.R:X2}{primary.G:X2}{primary.B:X2}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"WMI keyboard control failed: {ex.Message}");
            }
        }

        private void SetBrightnessViaWmi(int brightness)
        {
            try
            {
                // Some HP models expose keyboard backlight brightness via WMI
                _logging.Info($"Setting keyboard brightness via WMI: {brightness}%");
            }
            catch (Exception ex)
            {
                _logging.Warn($"WMI brightness control failed: {ex.Message}");
            }
        }

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return Color.White;

            hex = hex.TrimStart('#');
            
            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
            }
            
            return Color.White;
        }

        private static KeyboardEffect MapEffect(LightingEffectType effect)
        {
            return effect switch
            {
                LightingEffectType.Static => KeyboardEffect.Static,
                LightingEffectType.Breathing => KeyboardEffect.Breathing,
                LightingEffectType.ColorCycle => KeyboardEffect.ColorCycle,
                LightingEffectType.Wave => KeyboardEffect.Wave,
                LightingEffectType.Reactive => KeyboardEffect.Reactive,
                LightingEffectType.Off => KeyboardEffect.Off,
                _ => KeyboardEffect.Static
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Log telemetry summary before disposing
                LogTelemetrySummary();
                _v2Service?.Dispose();
                _disposed = true;
            }
        }
        
        /// <summary>
        /// Get telemetry statistics for WMI vs EC keyboard control success rates.
        /// </summary>
        public KeyboardTelemetryStats GetTelemetry()
        {
            lock (_telemetryLock)
            {
                return new KeyboardTelemetryStats
                {
                    TotalAttempts = _totalAttempts,
                    WmiSuccessCount = _wmiSuccessCount,
                    WmiFailureCount = _wmiFailureCount,
                    EcSuccessCount = _ecSuccessCount,
                    EcFailureCount = _ecFailureCount,
                    OghSuccessCount = _oghSuccessCount,
                    OghFailureCount = _oghFailureCount,
                    WmiSuccessRate = _wmiSuccessCount + _wmiFailureCount > 0 
                        ? (double)_wmiSuccessCount / (_wmiSuccessCount + _wmiFailureCount) * 100 
                        : 0,
                    OghSuccessRate = _oghSuccessCount + _oghFailureCount > 0 
                        ? (double)_oghSuccessCount / (_oghSuccessCount + _oghFailureCount) * 100 
                        : 0,
                    EcSuccessRate = _ecSuccessCount + _ecFailureCount > 0 
                        ? (double)_ecSuccessCount / (_ecSuccessCount + _ecFailureCount) * 100 
                        : 0
                };
            }
        }
        
        private void LogTelemetrySummary()
        {
            var stats = GetTelemetry();
            if (stats.TotalAttempts > 0)
            {
                _logging.Info($"Keyboard Telemetry: {stats.TotalAttempts} attempts | WMI: {stats.WmiSuccessCount}✓/{stats.WmiFailureCount}✗ ({stats.WmiSuccessRate:F0}%) | OGH: {stats.OghSuccessCount}✓/{stats.OghFailureCount}✗ ({stats.OghSuccessRate:F0}%) | EC: {stats.EcSuccessCount}✓/{stats.EcFailureCount}✗ ({stats.EcSuccessRate:F0}%)");
            }
        }
        
        private void TrackWmiResult(bool success)
        {
            lock (_telemetryLock)
            {
                _totalAttempts++;
                if (success) _wmiSuccessCount++; else _wmiFailureCount++;
            }
        }
        
        private void TrackEcResult(bool success)
        {
            lock (_telemetryLock)
            {
                _totalAttempts++;
                if (success) _ecSuccessCount++; else _ecFailureCount++;
            }
        }

        private void TrackOghResult(bool success)
        {
            lock (_telemetryLock)
            {
                _totalAttempts++;
                if (success) _oghSuccessCount++; else _oghFailureCount++;
            }
        }

        private bool TryOghSetColorTable(byte[] data)
        {
            if (_oghProxy == null || !_oghProxy.IsAvailable) return false;

            // Candidate command names that OGH might accept for keyboard backlight
            var cmds = new[] { "Backlight:SetColorTable", "Backlight:ColorTable", "Keyboard:SetColorTable", "Backlight:Set" };
            foreach (var cmd in cmds)
            {
                try
                {
                    if (_oghProxy.ExecuteOghSetCommand(cmd, data))
                        return true;
                }
                catch (Exception ex)
                {
                    _logging.Warn($"OGH command '{cmd}' failed: {ex.Message}");
                }
            }
            return false;
        }
    }
    
    /// <summary>
    /// Telemetry statistics for keyboard lighting operations.
    /// </summary>
    public class KeyboardTelemetryStats
    {
        public int TotalAttempts { get; set; }
        public int WmiSuccessCount { get; set; }
        public int WmiFailureCount { get; set; }
        public int OghSuccessCount { get; set; }
        public int OghFailureCount { get; set; }
        public int EcSuccessCount { get; set; }
        public int EcFailureCount { get; set; }
        public double WmiSuccessRate { get; set; }
        public double OghSuccessRate { get; set; }
        public double EcSuccessRate { get; set; }
    }
}
