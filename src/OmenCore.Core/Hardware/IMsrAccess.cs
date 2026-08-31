using System;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Interface for MSR (Model-Specific Register) access providers.
    /// Implemented by PawnIOMsrAccess (recommended, Secure Boot compatible) and
    /// PawnIOMsrAccess.
    /// </summary>
    public interface IMsrAccess : IDisposable
    {
        /// <summary>
        /// Check if MSR access is available.
        /// </summary>
        bool IsAvailable { get; }
        
        // ==========================================
        // Intel Voltage Offset (Undervolt)
        // ==========================================
        
        /// <summary>
        /// Apply a voltage offset to CPU cores (negative = undervolt).
        /// </summary>
        /// <param name="offsetMv">Offset in millivolts (typically -200 to 0)</param>
        void ApplyCoreVoltageOffset(int offsetMv);
        
        /// <summary>
        /// Apply a voltage offset to CPU cache (negative = undervolt).
        /// </summary>
        /// <param name="offsetMv">Offset in millivolts (typically -200 to 0)</param>
        void ApplyCacheVoltageOffset(int offsetMv);
        
        /// <summary>
        /// Read current core voltage offset.
        /// </summary>
        int ReadCoreVoltageOffset();
        
        /// <summary>
        /// Read current cache voltage offset.
        /// </summary>
        int ReadCacheVoltageOffset();
        
        // ==========================================
        // TCC Offset (Thermal Control Circuit)
        // ==========================================
        
        /// <summary>
        /// Read the current TCC offset (temperature limit reduction).
        /// Returns 0-63, where 0 = no limit, 63 = max 63°C below TjMax.
        /// </summary>
        int ReadTccOffset();
        
        /// <summary>
        /// Read the TjMax (maximum junction temperature).
        /// </summary>
        int ReadTjMax();
        
        /// <summary>
        /// Set the TCC offset to limit maximum CPU temperature.
        /// Offset of N means CPU will throttle at (TjMax - N)°C.
        /// </summary>
        /// <param name="offset">Offset in degrees (0-63)</param>
        void SetTccOffset(int offset);
        
        /// <summary>
        /// Get the effective temperature limit (TjMax - TCC offset).
        /// </summary>
        int GetEffectiveTempLimit();
        
        // ==========================================
        // Throttling Detection (EDP)
        // ==========================================
        
        /// <summary>
        /// Read CPU thermal throttling status from MSR.
        /// Returns true if CPU is thermally throttling.
        /// </summary>
        bool ReadThermalThrottlingStatus();
        
        /// <summary>
        /// Read CPU power throttling status from MSR.
        /// Returns true if CPU is power limit throttling.
        /// </summary>
        bool ReadPowerThrottlingStatus();
        
        // ==========================================
        // Power Limit Control (EDP Override)
        // ==========================================
        
        /// <summary>
        /// Check if power limit MSR is locked by BIOS (cannot be modified until next reboot)
        /// </summary>
        bool IsPowerLimitLocked();
        
        /// <summary>
        /// Get detailed power limit status including PL1, PL2, enable states, and lock status
        /// </summary>
        (double Pl1Watts, double Pl2Watts, bool Pl1Enabled, bool Pl2Enabled, bool IsLocked) GetPowerLimitStatus();
        
        /// <summary>
        /// Set both PL1 and PL2 power limits at once (more efficient than separate calls).
        /// Only works if power limits are not locked by BIOS.
        /// </summary>
        /// <param name="pl1Watts">Sustained power limit (PL1) in watts</param>
        /// <param name="pl2Watts">Burst power limit (PL2) in watts</param>
        /// <returns>True if successfully set, false if locked or failed</returns>
        bool SetPowerLimits(double pl1Watts, double pl2Watts);
        
        /// <summary>
        /// Read current package power limit (PL1) in watts.
        /// </summary>
        double ReadPackagePowerLimit();
        
        /// <summary>
        /// Set package power limit (PL1) in watts.
        /// </summary>
        /// <param name="watts">Power limit in watts</param>
        void SetPackagePowerLimit(double watts);
        
        /// <summary>
        /// Read current package power limit time window in seconds.
        /// </summary>
        double ReadPackagePowerTimeWindow();
        
        /// <summary>
        /// Set package power limit time window in seconds.
        /// </summary>
        /// <param name="seconds">Time window in seconds</param>
        void SetPackagePowerTimeWindow(double seconds);
    }
}
