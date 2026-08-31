using System;
using System.Drawing;
using System.Threading.Tasks;

namespace OmenCore.Services.KeyboardLighting
{
    /// <summary>
    /// Keyboard lighting method/protocol type.
    /// </summary>
    public enum KeyboardMethod
    {
        /// <summary>Method not determined yet.</summary>
        Unknown,
        
        /// <summary>No working method found.</summary>
        Unsupported,
        
        /// <summary>2018-2019 models: SetBacklight only (on/off, no color).</summary>
        BacklightOnly,
        
        /// <summary>2020-2022 models: SetColorTable (128-byte, 4-zone RGB).</summary>
        ColorTable2020,
        
        /// <summary>2023+ models: New SetKeyboardBacklight interface.</summary>
        NewWmi2023,
        
        /// <summary>Direct EC register writes (model-specific addresses).</summary>
        EcDirect,
        
        /// <summary>Per-key RGB via USB HID protocol.</summary>
        HidPerKey
    }

    /// <summary>
    /// Keyboard type classification.
    /// </summary>
    public enum KeyboardType
    {
        /// <summary>Unknown keyboard type.</summary>
        Unknown,
        
        /// <summary>Standard 4-zone RGB (most OMEN laptops 2020-2022).</summary>
        FourZone,
        
        /// <summary>TenKeyLess 4-zone (OMEN 15" models).</summary>
        FourZoneTkl,
        
        /// <summary>Per-key RGB (premium 2023+ models).</summary>
        PerKeyRgb,
        
        /// <summary>No RGB, backlight only (older models).</summary>
        BacklightOnly,
        
        /// <summary>Desktop keyboard (external USB).</summary>
        Desktop
    }

    /// <summary>
    /// Result of applying RGB settings.
    /// </summary>
    public class RgbApplyResult
    {
        /// <summary>Whether the backend reported success.</summary>
        public bool BackendReportedSuccess { get; set; }
        
        /// <summary>Whether readback verification matched (if supported).</summary>
        public bool VerificationPassed { get; set; }
        
        /// <summary>Whether the backend supports readback verification.</summary>
        public bool SupportsVerification { get; set; }
        
        /// <summary>User confirmed the change worked (for backends without verification).</summary>
        public bool? UserConfirmed { get; set; }
        
        /// <summary>Failure reason if not successful.</summary>
        public string? FailureReason { get; set; }

        /// <summary>Non-fatal readback warning when firmware accepted a write but verification was inconclusive.</summary>
        public string? VerificationAdvisory { get; set; }

        /// <summary>Firmware accepted the write, but OmenCore could not verify that the intended surface changed.</summary>
        public bool AcceptedButUnverified => BackendReportedSuccess && !string.IsNullOrWhiteSpace(VerificationAdvisory);
        
        /// <summary>Time taken to apply (ms).</summary>
        public int DurationMs { get; set; }
        
        /// <summary>The backend method used.</summary>
        public KeyboardMethod Method { get; set; }
        
        /// <summary>Overall success (backend success + verification if available).</summary>
        public bool Success => BackendReportedSuccess && !AcceptedButUnverified && (!SupportsVerification || VerificationPassed);
    }

    /// <summary>
    /// Interface for keyboard lighting backends.
    /// Each backend implements a different method of controlling the keyboard RGB.
    /// </summary>
    public interface IKeyboardBackend : IDisposable
    {
        /// <summary>Backend name for display/logging.</summary>
        string Name { get; }
        
        /// <summary>The method this backend uses.</summary>
        KeyboardMethod Method { get; }
        
        /// <summary>Whether this backend is available on the current system.</summary>
        bool IsAvailable { get; }
        
        /// <summary>Whether this backend supports color readback for verification.</summary>
        bool SupportsReadback { get; }
        
        /// <summary>Number of RGB zones (4 for most OMEN keyboards, 0 for per-key).</summary>
        int ZoneCount { get; }
        
        /// <summary>Whether this is a per-key RGB backend.</summary>
        bool IsPerKey { get; }
        
        /// <summary>
        /// Initialize the backend and check if it works on this system.
        /// </summary>
        /// <returns>True if backend is usable.</returns>
        Task<bool> InitializeAsync();
        
        /// <summary>
        /// Set colors for all zones (4-zone keyboards).
        /// </summary>
        /// <param name="zoneColors">Array of 4 colors, one per zone.</param>
        /// <returns>Result with success status and verification info.</returns>
        Task<RgbApplyResult> SetZoneColorsAsync(Color[] zoneColors);
        
        /// <summary>
        /// Set a single zone color.
        /// </summary>
        /// <param name="zone">Zone index (0-3).</param>
        /// <param name="color">Color to set.</param>
        /// <returns>Result with success status.</returns>
        Task<RgbApplyResult> SetZoneColorAsync(int zone, Color color);
        
        /// <summary>
        /// Read current zone colors (if supported).
        /// </summary>
        /// <returns>Array of zone colors, or null if not supported.</returns>
        Task<Color[]?> ReadZoneColorsAsync();
        
        /// <summary>
        /// Set brightness level.
        /// </summary>
        /// <param name="brightness">Brightness 0-100.</param>
        Task<bool> SetBrightnessAsync(int brightness);
        
        /// <summary>
        /// Turn keyboard backlight on or off.
        /// </summary>
        Task<bool> SetBacklightEnabledAsync(bool enabled);
        
        /// <summary>
        /// Apply a lighting effect (static, breathing, wave, etc.).
        /// </summary>
        /// <param name="effect">Effect type.</param>
        /// <param name="primaryColor">Primary color.</param>
        /// <param name="secondaryColor">Secondary color (for multi-color effects).</param>
        /// <param name="speed">Effect speed 0-100.</param>
        Task<RgbApplyResult> SetEffectAsync(KeyboardEffect effect, Color primaryColor, Color secondaryColor, int speed);
    }

    /// <summary>
    /// Keyboard lighting effects.
    /// </summary>
    public enum KeyboardEffect
    {
        Static = 0,
        Breathing = 1,
        ColorCycle = 2,
        Wave = 3,
        Reactive = 4,
        Off = 255
    }
}
