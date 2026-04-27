using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Provides verification for power limit control commands.
    /// After setting power limits, reads back EC registers to verify they were applied.
    ///
    /// This addresses issues where power limit writes appear to succeed but don't take effect.
    /// </summary>
    public class PowerVerificationService : IPowerVerificationService
    {
        private readonly PowerLimitController _powerController;
        private readonly IEcAccess _ecAccess;
        private readonly LoggingService _logging;

        // EC registers to read back
        private const ushort EC_PERFORMANCE_MODE = 0xCE;
        private const ushort EC_CPU_PL1_LOW = 0xC0;
        private const ushort EC_CPU_PL1_HIGH = 0xC1;
        private const ushort EC_CPU_PL2_LOW = 0xC2;
        private const ushort EC_CPU_PL2_HIGH = 0xC3;
        private const ushort EC_GPU_TGP_LOW = 0xC4;
        private const ushort EC_GPU_TGP_HIGH = 0xC5;

        public PowerVerificationService(PowerLimitController powerController, IEcAccess ecAccess, LoggingService logging)
        {
            _powerController = powerController;
            _ecAccess = ecAccess;
            _logging = logging;
        }

        public bool IsAvailable => _powerController.IsAvailable && _ecAccess.IsAvailable;

        public async Task<PowerLimitApplyResult> ApplyAndVerifyPowerLimitsAsync(PerformanceMode mode, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new PowerLimitApplyResult
            {
                RequestedMode = mode
            };

            try
            {
                // Apply the power limits
                _powerController.ApplyPerformanceLimits(mode);
                result.EcWriteSucceeded = true;

                // Determine expected performance mode value
                result.AppliedPerformanceMode = GetExpectedPerformanceModeRegister(mode);

                // Wait a bit for EC to process
                await Task.Delay(500, ct);

                // Read back values
                var readBack = GetCurrentPowerLimits();
                result.ReadBackPerformanceMode = readBack.performanceMode;
                result.ReadBackCpuPl1 = readBack.cpuPl1;
                result.ReadBackCpuPl2 = readBack.cpuPl2;
                result.ReadBackGpuTgp = readBack.gpuTgp;

                // Verify
                result.VerificationPassed = result.ValuesMatch;

                if (!result.VerificationPassed)
                {
                    result.ErrorMessage = $"Read-back verification failed. Expected mode: 0x{result.AppliedPerformanceMode:X}, got: 0x{result.ReadBackPerformanceMode:X}";
                    _logging.Warn(result.ErrorMessage);
                }
                else
                {
                    _logging.Info($"✓ Power limits verified for {mode.Name} mode");
                }
            }
            catch (Exception ex)
            {
                result.EcWriteSucceeded = false;
                result.ErrorMessage = ex.Message;
                _logging.Error($"Failed to apply/verify power limits for {mode.Name}: {ex.Message}");
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        public (int cpuPl1, int cpuPl2, int gpuTgp, int performanceMode) GetCurrentPowerLimits()
        {
            if (!IsAvailable)
            {
                return (0, 0, 0, 0);
            }

            try
            {
                int performanceMode = _ecAccess.ReadByte(EC_PERFORMANCE_MODE);

                // Read power limit registers (if available)
                int cpuPl1 = ReadWord(EC_CPU_PL1_LOW, EC_CPU_PL1_HIGH);
                int cpuPl2 = ReadWord(EC_CPU_PL2_LOW, EC_CPU_PL2_HIGH);
                int gpuTgp = ReadWord(EC_GPU_TGP_LOW, EC_GPU_TGP_HIGH);

                return (cpuPl1, cpuPl2, gpuTgp, performanceMode);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to read power limit registers: {ex.Message}");
                return (0, 0, 0, 0);
            }
        }

        public async Task<bool> VerifyPowerLimitsAsync(PerformanceMode expectedMode, CancellationToken ct = default)
        {
            if (!IsAvailable)
            {
                return false;
            }

            try
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                var readBack = GetCurrentPowerLimits();
                var expectedRegister = GetExpectedPerformanceModeRegister(expectedMode);
                var verified = readBack.performanceMode == expectedRegister;

                if (!verified)
                {
                    _logging.Warn(
                        $"Power limits verification failed. Expected mode: 0x{expectedRegister:X}, got: 0x{readBack.performanceMode:X}");
                }

                return verified;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Power limits verification threw: {ex.Message}");
                return false;
            }
        }

        private static int GetExpectedPerformanceModeRegister(PerformanceMode mode)
        {
            return mode.Name.ToLowerInvariant() switch
            {
                "eco" => 0x00,
                "quiet" => 0x00,
                "balanced" => 0x01,
                "performance" => 0x02,
                "gaming" => 0x02,
                "turbo" => 0x03,
                _ => 0x01
            };
        }

        private int ReadWord(ushort lowAddr, ushort highAddr)
        {
            try
            {
                byte low = _ecAccess.ReadByte(lowAddr);
                byte high = _ecAccess.ReadByte(highAddr);
                return (high << 8) | low;
            }
            catch
            {
                return 0;
            }
        }
    }
}
