using System;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Controls CPU PL1/PL2 and GPU TGP power limits via EC registers.
    /// WARNING: EC register addresses are hardware-specific and vary by laptop model.
    /// Incorrect values can cause system instability or hardware damage.
    /// </summary>
    public class PowerLimitController
    {
        private readonly IEcAccess _ecAccess;
        
        // HP Omen EC register addresses (EXAMPLE - varies by model!)
        // These need to be confirmed for specific laptop models via EC datasheets or reverse engineering
        private const ushort EC_CPU_PL1_LOW = 0xC0;   // CPU sustained power limit (low byte)
        private const ushort EC_CPU_PL1_HIGH = 0xC1;  // CPU sustained power limit (high byte)
        private const ushort EC_CPU_PL2_LOW = 0xC2;   // CPU burst power limit (low byte)
        private const ushort EC_CPU_PL2_HIGH = 0xC3;  // CPU burst power limit (high byte)
        private const ushort EC_GPU_TGP_LOW = 0xC4;   // GPU total graphics power (low byte)
        private const ushort EC_GPU_TGP_HIGH = 0xC5;  // GPU total graphics power (high byte)
        
        // Some HP systems use a single performance mode register instead
        private const ushort EC_PERFORMANCE_MODE = 0xCE; // 0x00=Eco, 0x01=Balanced, 0x02=Performance
        
        private readonly bool _useSimplifiedMode;

        public PowerLimitController(IEcAccess ecAccess, bool useSimplifiedMode = true)
        {
            _ecAccess = ecAccess;
            _useSimplifiedMode = useSimplifiedMode;
        }

        public bool IsAvailable => _ecAccess.IsAvailable;

        /// <summary>
        /// Apply performance mode power limits
        /// </summary>
        public void ApplyPerformanceLimits(PerformanceMode mode)
        {
            if (!_ecAccess.IsAvailable)
            {
                throw new InvalidOperationException("EC access not available - WinRing0 driver required");
            }

            if (_useSimplifiedMode)
            {
                // Method 1: Use single EC register for performance mode (simpler, more compatible)
                ApplySimplifiedMode(mode);
            }
            else
            {
                // Method 2: Write individual power limit registers (more control, but hardware-specific)
                ApplyDetailedPowerLimits(mode);
            }
        }

        /// <summary>
        /// Apply simplified performance mode via single EC register.
        /// More compatible across HP Omen models.
        /// </summary>
        private void ApplySimplifiedMode(PerformanceMode mode)
        {
            byte modeValue = mode.Name.ToLowerInvariant() switch
            {
                "eco" => 0x00,
                "quiet" => 0x00,
                "balanced" => 0x01,
                "performance" => 0x02,
                "gaming" => 0x02,
                "turbo" => 0x03,
                _ => 0x01 // Default to balanced
            };

            try
            {
                _ecAccess.WriteByte(EC_PERFORMANCE_MODE, modeValue);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"EC register 0x{EC_PERFORMANCE_MODE:X} not in safety allowlist. " +
                    "Add to WinRing0EcAccess.AllowedWriteAddresses if you've verified this is correct for your hardware.");
            }
        }

        /// <summary>
        /// Apply detailed CPU and GPU power limits.
        /// WARNING: Requires hardware-specific EC addresses. Test thoroughly!
        /// </summary>
        private void ApplyDetailedPowerLimits(PerformanceMode mode)
        {
            // Convert watts to internal EC format (usually in 1/8 watt units)
            // Format varies by manufacturer - HP may use different encoding
            int cpuPl1 = mode.CpuPowerLimitWatts * 8;  // e.g., 45W = 360 units
            int cpuPl2 = (int)(mode.CpuPowerLimitWatts * 1.5 * 8); // Burst = 1.5x sustained
            int gpuTgp = mode.GpuPowerLimitWatts * 8;

            // Clamp values to reasonable ranges
            cpuPl1 = Math.Clamp(cpuPl1, 10 * 8, 150 * 8);  // 10W - 150W
            cpuPl2 = Math.Clamp(cpuPl2, 10 * 8, 200 * 8);  // 10W - 200W
            gpuTgp = Math.Clamp(gpuTgp, 30 * 8, 200 * 8);  // 30W - 200W

            try
            {
                // Write CPU PL1 (sustained)
                _ecAccess.WriteByte(EC_CPU_PL1_LOW, (byte)(cpuPl1 & 0xFF));
                _ecAccess.WriteByte(EC_CPU_PL1_HIGH, (byte)((cpuPl1 >> 8) & 0xFF));
                
                // Write CPU PL2 (burst)
                _ecAccess.WriteByte(EC_CPU_PL2_LOW, (byte)(cpuPl2 & 0xFF));
                _ecAccess.WriteByte(EC_CPU_PL2_HIGH, (byte)((cpuPl2 >> 8) & 0xFF));
                
                // Write GPU TGP
                _ecAccess.WriteByte(EC_GPU_TGP_LOW, (byte)(gpuTgp & 0xFF));
                _ecAccess.WriteByte(EC_GPU_TGP_HIGH, (byte)((gpuTgp >> 8) & 0xFF));
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    "Power limit EC registers not in safety allowlist. " +
                    "This is intentional - these addresses must be verified for your specific hardware model before use. " +
                    "See docs/POWER_LIMITS.md for instructions on enabling this feature safely.", ex);
            }
        }

        /// <summary>
        /// Read current power limits from EC (if supported)
        /// </summary>
        public (int CpuPl1, int CpuPl2, int GpuTgp)? ReadCurrentPowerLimits()
        {
            if (!_ecAccess.IsAvailable || _useSimplifiedMode)
            {
                return null;
            }

            try
            {
                var pl1Low = _ecAccess.ReadByte(EC_CPU_PL1_LOW);
                var pl1High = _ecAccess.ReadByte(EC_CPU_PL1_HIGH);
                var pl2Low = _ecAccess.ReadByte(EC_CPU_PL2_LOW);
                var pl2High = _ecAccess.ReadByte(EC_CPU_PL2_HIGH);
                var tgpLow = _ecAccess.ReadByte(EC_GPU_TGP_LOW);
                var tgpHigh = _ecAccess.ReadByte(EC_GPU_TGP_HIGH);

                var cpuPl1 = (pl1High << 8) | pl1Low;
                var cpuPl2 = (pl2High << 8) | pl2Low;
                var gpuTgp = (tgpHigh << 8) | tgpLow;

                // Convert from internal units to watts (divide by 8)
                return (cpuPl1 / 8, cpuPl2 / 8, gpuTgp / 8);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read current performance mode from EC
        /// </summary>
        public byte? ReadCurrentPerformanceMode()
        {
            if (!_ecAccess.IsAvailable)
            {
                return null;
            }

            try
            {
                return _ecAccess.ReadByte(EC_PERFORMANCE_MODE);
            }
            catch
            {
                return null;
            }
        }
    }
}
