using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Services
{
    public interface IPowerVerificationService
    {
        bool IsAvailable { get; }
        Task<PowerLimitApplyResult> ApplyAndVerifyPowerLimitsAsync(PerformanceMode mode, CancellationToken ct = default);
        (int cpuPl1, int cpuPl2, int gpuTgp, int performanceMode) GetCurrentPowerLimits();
        Task<bool> VerifyPowerLimitsAsync(PerformanceMode expectedMode, CancellationToken ct = default);
    }
}