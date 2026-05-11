using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Services
{
    public interface IFanVerificationService
    {
        bool IsAvailable { get; }
        Task<FanApplyResult> ApplyAndVerifyFanSpeedAsync(int fanIndex, int targetPercent, CancellationToken ct = default);
        Task<FanApplyResult> ApplyWithEnhancedVerificationAsync(int fanIndex, int targetPercent, bool autoRevertOnFailure = true, CancellationToken ct = default);
        Task<FanCalibrationResult> PerformFanCalibrationAsync(int fanIndex, CancellationToken ct = default);
        bool RestoreFanControlAfterCalibration();
        (int rpm, int level) GetCurrentFanState(int fanIndex);
        (int rpm, int level, RpmSource source) GetCurrentFanStateWithSource(int fanIndex);
        Task<(int avg, int min, int max)> GetStableFanRpmAsync(int fanIndex, int samples = 3, CancellationToken ct = default);
    }
}
