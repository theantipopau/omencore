using System;
using System.Threading.Tasks;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Base interface for all hardware providers.
    /// Providers implement specific functionality for different control methods.
    /// </summary>
    public interface IHardwareProvider : IDisposable
    {
        /// <summary>Name of this provider for logging/display.</summary>
        string Name { get; }
        
        /// <summary>Whether this provider is currently available.</summary>
        bool IsAvailable { get; }
        
        /// <summary>Human-readable status message.</summary>
        string Status { get; }
        
        /// <summary>Priority for provider selection (higher = preferred).</summary>
        int Priority { get; }
        
        /// <summary>Initialize the provider. Returns true if successful.</summary>
        bool Initialize();
    }

    /// <summary>
    /// Interface for fan control providers.
    /// </summary>
    public interface IFanControlProvider : IHardwareProvider
    {
        /// <summary>The control method this provider uses.</summary>
        FanControlMethod Method { get; }
        
        /// <summary>Number of fans available.</summary>
        int FanCount { get; }
        
        /// <summary>Whether this provider can set fan speeds.</summary>
        bool CanSetSpeed { get; }
        
        /// <summary>Whether this provider can read fan RPM.</summary>
        bool CanReadRpm { get; }
        
        /// <summary>Available fan modes (if any).</summary>
        string[] AvailableModes { get; }
        
        /// <summary>Get current fan speeds (RPM).</summary>
        (int fan1Rpm, int fan2Rpm)? GetFanSpeeds();
        
        /// <summary>Set fan mode by name.</summary>
        bool SetFanMode(string mode);
        
        /// <summary>Set fan speed levels (0-100%).</summary>
        bool SetFanSpeed(int fan1Percent, int fan2Percent);
        
        /// <summary>Enable automatic fan control.</summary>
        bool SetAutoMode();
    }

    /// <summary>
    /// Interface for thermal sensor providers.
    /// </summary>
    public interface IThermalSensorProvider : IHardwareProvider
    {
        /// <summary>The sensor method this provider uses.</summary>
        ThermalSensorMethod Method { get; }
        
        /// <summary>Get CPU temperature in Celsius.</summary>
        float? GetCpuTemperature();
        
        /// <summary>Get GPU temperature in Celsius.</summary>
        float? GetGpuTemperature();
        
        /// <summary>Get all available temperature readings.</summary>
        (string name, float celsius)[] GetAllTemperatures();
    }

    /// <summary>
    /// Interface for GPU control providers.
    /// </summary>
    public interface IGpuControlProvider : IHardwareProvider
    {
        /// <summary>GPU vendor.</summary>
        GpuVendor Vendor { get; }
        
        /// <summary>Whether MUX switch is available.</summary>
        bool HasMuxSwitch { get; }
        
        /// <summary>Whether power control is available.</summary>
        bool HasPowerControl { get; }
        
        /// <summary>Get current GPU mode (Hybrid/Discrete/Optimus).</summary>
        string? GetGpuMode();
        
        /// <summary>Set GPU mode (requires reboot).</summary>
        bool SetGpuMode(string mode);
        
        /// <summary>Get current GPU power level.</summary>
        string? GetPowerLevel();
        
        /// <summary>Set GPU power level.</summary>
        bool SetPowerLevel(string level);
    }

    /// <summary>
    /// Interface for undervolt providers.
    /// </summary>
    public interface IUndervoltProvider : IHardwareProvider
    {
        /// <summary>The undervolt method this provider uses.</summary>
        UndervoltMethod Method { get; }
        
        /// <summary>Whether undervolting is currently active.</summary>
        bool IsActive { get; }
        
        /// <summary>Get current voltage offset in mV (negative = undervolt).</summary>
        int? GetCurrentOffset();
        
        /// <summary>Apply undervolt offset in mV.</summary>
        bool ApplyOffset(int millivolts);
        
        /// <summary>Reset to default voltage.</summary>
        bool Reset();
    }

    /// <summary>
    /// Interface for lighting control providers.
    /// </summary>
    public interface ILightingProvider : IHardwareProvider
    {
        /// <summary>Lighting capability type.</summary>
        LightingCapability Capability { get; }
        
        /// <summary>Number of lighting zones.</summary>
        int ZoneCount { get; }
        
        /// <summary>Set all zones to a single color.</summary>
        bool SetColor(byte r, byte g, byte b);
        
        /// <summary>Set a specific zone color.</summary>
        bool SetZoneColor(int zone, byte r, byte g, byte b);
        
        /// <summary>Set brightness (0-100%).</summary>
        bool SetBrightness(int percent);
        
        /// <summary>Turn lighting off.</summary>
        bool TurnOff();
    }
}
