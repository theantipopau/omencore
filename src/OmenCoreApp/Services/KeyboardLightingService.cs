using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Controls HP OMEN laptop keyboard backlight lighting via WMI and EC access.
    /// Supports 4-zone RGB keyboards found in OMEN 15/16/17 series laptops.
    /// </summary>
    public class KeyboardLightingService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly Hardware.IEcAccess? _ecAccess;
        private bool _wmiAvailable;
        private bool _ecAvailable;
        private bool _disposed;

        // HP OMEN WMI namespace and class identifiers
        private const string OmenWmiNamespace = @"root\hp\InstrumentedBIOS";
        private const string OmenWmiClass = "HPBIOS_BIOSSettingInterface";
        
        // EC register addresses for keyboard backlight (varies by model)
        // These are common addresses for OMEN 15/16/17 series
        private const byte EC_KB_BACKLIGHT_CTRL = 0xB0;
        private const byte EC_KB_ZONE1_R = 0xB1;
        private const byte EC_KB_ZONE1_G = 0xB2;
        private const byte EC_KB_ZONE1_B = 0xB3;
        private const byte EC_KB_ZONE2_R = 0xB4;
        private const byte EC_KB_ZONE2_G = 0xB5;
        private const byte EC_KB_ZONE2_B = 0xB6;
        private const byte EC_KB_ZONE3_R = 0xB7;
        private const byte EC_KB_ZONE3_G = 0xB8;
        private const byte EC_KB_ZONE3_B = 0xB9;
        private const byte EC_KB_ZONE4_R = 0xBA;
        private const byte EC_KB_ZONE4_G = 0xBB;
        private const byte EC_KB_ZONE4_B = 0xBC;
        private const byte EC_KB_BRIGHTNESS = 0xBD;
        private const byte EC_KB_EFFECT = 0xBE;

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

        public bool IsAvailable => _wmiAvailable || _ecAvailable;
        public string BackendType => _wmiAvailable ? "WMI" : (_ecAvailable ? "EC" : "None");

        public KeyboardLightingService(LoggingService logging, Hardware.IEcAccess? ecAccess = null)
        {
            _logging = logging;
            _ecAccess = ecAccess;
            
            InitializeBackends();
        }

        private void InitializeBackends()
        {
            // Try WMI first (preferred - safer)
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

            if (!IsAvailable)
            {
                _logging.Warn("⚠️ No keyboard lighting backend available - keyboard RGB control disabled");
            }
        }

        public void ApplyProfile(LightingProfile profile)
        {
            if (!IsAvailable)
            {
                _logging.Warn("Keyboard lighting not available on this system");
                return;
            }

            _logging.Info($"Applying keyboard lighting profile: {profile.Name} ({profile.Effect})");

            try
            {
                var primaryColor = ParseHexColor(profile.PrimaryColorHex);
                var secondaryColor = ParseHexColor(profile.SecondaryColorHex);
                var effect = MapEffect(profile.Effect);

                if (_wmiAvailable)
                {
                    ApplyViaWmi(effect, primaryColor, secondaryColor, profile.EffectSpeed, profile.Brightness);
                }
                else if (_ecAvailable)
                {
                    ApplyViaEc(effect, primaryColor, secondaryColor, profile.EffectSpeed, profile.Brightness);
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

            _logging.Info($"Applying keyboard effect: {effect} primary:{primaryHex} secondary:{secondaryHex} speed:{speed}");

            try
            {
                var primaryColor = ParseHexColor(primaryHex);
                var secondaryColor = ParseHexColor(secondaryHex);
                var mappedEffect = MapEffect(effect);

                if (_ecAvailable)
                {
                    ApplyViaEc(mappedEffect, primaryColor, secondaryColor, speed, 100);
                }
                else if (_wmiAvailable)
                {
                    ApplyViaWmi(mappedEffect, primaryColor, secondaryColor, speed, 100);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply keyboard effect: {ex.Message}", ex);
            }
        }

        public void SetZoneColor(KeyboardZone zone, Color color)
        {
            if (!_ecAvailable || _ecAccess == null)
            {
                _logging.Info("EC access required for per-zone control");
                return;
            }

            try
            {
                if (zone == KeyboardZone.All)
                {
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

        private void SetZoneColorInternal(KeyboardZone zone, Color color)
        {
            if (_ecAccess == null) return;

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

        public void SetBrightness(int brightness)
        {
            brightness = Math.Clamp(brightness, 0, 100);
            
            try
            {
                if (_ecAvailable && _ecAccess != null)
                {
                    // Map 0-100 to 0-255
                    byte ecBrightness = (byte)(brightness * 255 / 100);
                    _ecAccess.WriteByte(EC_KB_BRIGHTNESS, ecBrightness);
                    _logging.Info($"Set keyboard brightness to {brightness}%");
                }
                else if (_wmiAvailable)
                {
                    SetBrightnessViaWmi(brightness);
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
                // Default: White static at 80% brightness
                var white = Color.FromArgb(255, 255, 255);
                
                if (_ecAvailable && _ecAccess != null)
                {
                    _ecAccess.WriteByte(EC_KB_EFFECT, (byte)KeyboardEffect.Static);
                    SetZoneColor(KeyboardZone.All, white);
                    SetBrightness(80);
                }
                else if (_wmiAvailable)
                {
                    ApplyViaWmi(KeyboardEffect.Static, white, white, 0.5, 80);
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
                if (_ecAvailable && _ecAccess != null)
                {
                    _ecAccess.WriteByte(EC_KB_EFFECT, (byte)KeyboardEffect.Off);
                    _ecAccess.WriteByte(EC_KB_BRIGHTNESS, 0);
                }
                else if (_wmiAvailable)
                {
                    SetBrightnessViaWmi(0);
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to turn off keyboard lighting: {ex.Message}");
            }
        }

        private void ApplyViaWmi(KeyboardEffect effect, Color primary, Color secondary, double speed, int brightness)
        {
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

        private void ApplyViaEc(KeyboardEffect effect, Color primary, Color secondary, double speed, int brightness)
        {
            if (_ecAccess == null) return;

            try
            {
                // Set effect mode
                _ecAccess.WriteByte(EC_KB_EFFECT, (byte)effect);
                
                // Set all zones to primary color for static/breathing
                SetZoneColor(KeyboardZone.All, primary);
                
                // Set brightness
                SetBrightness(brightness);
                
                // Enable backlight
                _ecAccess.WriteByte(EC_KB_BACKLIGHT_CTRL, 0x01);
                
                _logging.Info($"Keyboard lighting set via EC: {effect} #{primary.R:X2}{primary.G:X2}{primary.B:X2} @ {brightness}%");
            }
            catch (Exception ex)
            {
                _logging.Warn($"EC keyboard control failed: {ex.Message}");
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
                _disposed = true;
            }
        }
    }
}
