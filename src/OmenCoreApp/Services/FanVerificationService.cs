using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Results from applying and verifying a fan speed change.
    /// </summary>
    public class FanApplyResult
    {
        public int FanIndex { get; set; }
        public string FanName { get; set; } = "";
        public int RequestedPercent { get; set; }
        public int AppliedLevel { get; set; }
        public int ActualRpmBefore { get; set; }
        public int ActualRpmAfter { get; set; }
        public int ExpectedRpm { get; set; }
        public bool WmiCallSucceeded { get; set; }
        public bool VerificationPassed { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        
        /// <summary>
        /// RPM standard deviation from multi-sample verification (lower = more stable).
        /// </summary>
        public double RpmStandardDeviation { get; set; }
        
        /// <summary>
        /// Number of samples taken during verification.
        /// </summary>
        public int SampleCount { get; set; }
        
        /// <summary>
        /// True if the fan speed change was successfully applied and verified.
        /// </summary>
        public bool Success => WmiCallSucceeded && VerificationPassed;
        
        /// <summary>
        /// Percentage difference from expected RPM (for diagnostics).
        /// </summary>
        public double DeviationPercent => ExpectedRpm > 0 
            ? Math.Abs(ActualRpmAfter - ExpectedRpm) / (double)ExpectedRpm * 100 
            : 0;
        
        /// <summary>
        /// Overall verification score 0-100 (v2.7.0).
        /// Combines accuracy, stability, and success factors.
        /// 100 = perfect match, 0 = complete failure.
        /// </summary>
        public int VerificationScore
        {
            get
            {
                if (!WmiCallSucceeded) return 0;
                if (ActualRpmAfter <= 0 && RequestedPercent > 0) return 5; // Minimal score for zero RPM
                
                // Accuracy score (0-50): Based on deviation from expected RPM
                // 0% deviation = 50 points, 15% deviation = 0 points
                double accuracyScore = Math.Max(0, 50 - (DeviationPercent / 15.0 * 50));
                
                // Stability score (0-30): Based on standard deviation of samples
                // 0 std dev = 30 points, 200+ std dev = 0 points
                double stabilityScore = Math.Max(0, 30 - (RpmStandardDeviation / 200.0 * 30));
                
                // Response score (0-20): Based on actual change from before
                // Full change = 20 points, no change = 0 points
                double responseScore = 0;
                if (ActualRpmBefore != ActualRpmAfter && RequestedPercent > 0)
                {
                    responseScore = 20; // Fan responded
                }
                else if (RequestedPercent == 0 && ActualRpmAfter < 1000)
                {
                    responseScore = 20; // Correctly went to low/off
                }
                
                return (int)Math.Round(accuracyScore + stabilityScore + responseScore);
            }
        }
        
        /// <summary>
        /// Human-readable score rating (Excellent/Good/Fair/Poor/Failed).
        /// </summary>
        public string ScoreRating => VerificationScore switch
        {
            >= 90 => "Excellent",
            >= 70 => "Good",
            >= 50 => "Fair",
            >= 25 => "Poor",
            _ => "Failed"
        };
    }

    /// <summary>
    /// Provides closed-loop verification for fan control commands.
    /// After setting a fan speed, reads back the actual RPM to verify it was applied.
    /// 
    /// Enhanced with:
    /// - Multi-sample verification (reads RPM multiple times to ensure stability)
    /// - Auto-revert on failure (restores previous state if verification fails)
    /// - Detailed diagnostics (logs suggestion to switch backend if commands ineffective)
    /// - Retry logic for transient failures
    /// 
    /// This addresses the issue where "Requested % doesn't match actual fan speed".
    /// </summary>
    public class FanVerificationService : IFanVerificationService
    {
        private readonly HpWmiBios? _wmiBios;
        private readonly FanService? _fanService;
        private readonly LoggingService _logging;
        
        // Verification parameters
        private const int VerificationRetries = 3;          // Retry verification up to 3 times (increased from 2)
        private const int VerificationSamples = 5;          // Take 5 RPM samples and average (increased from 3)
        private const int SampleDelayMs = 200;              // Wait 200ms between samples (reduced for faster verification)
        private const int MaxLevel = 55;  // HP uses 55 as max on most models
        private const int MinRpm = 0;
        private const int MaxRpm = 5500;  // Typical max RPM
        
        // Verification timing
        private const int FanResponseDelayMs = 2000;  // Reduced from 2500ms for faster response
        private const int RetryDelayMs = 1500;        // Reduced from 2000ms
        private const double RpmTolerance = 0.15;     // Tighter tolerance (reduced from 0.18)
        
        // Auto-revert settings
        private const bool AutoRevertOnFailure = true;     // Enable auto-revert to previous state
        private const int RevertDelayMs = 1000;            // Delay before reverting
        
        public FanVerificationService(HpWmiBios? wmiBios, FanService? fanService, LoggingService logging)
        {
            _wmiBios = wmiBios;
            _fanService = fanService;
            _logging = logging;
        }
        
        /// <summary>
        /// Check if verification is available.
        /// </summary>
        public bool IsAvailable => (_wmiBios?.IsAvailable ?? false) || (_fanService != null);
        
        /// <summary>
        /// Get current RPM from fan telemetry.
        /// </summary>
        private int GetCurrentRpm(int fanIndex)
        {
            if (_fanService?.FanTelemetry != null && _fanService.FanTelemetry.Count > fanIndex)
            {
                return _fanService.FanTelemetry[fanIndex].SpeedRpm;
            }
            return 0;
        }
        
        /// <summary>
        /// Get fan name from telemetry.
        /// </summary>
        private string GetFanName(int fanIndex)
        {
            if (_fanService?.FanTelemetry != null && _fanService.FanTelemetry.Count > fanIndex)
            {
                return _fanService.FanTelemetry[fanIndex].Name;
            }
            return fanIndex == 0 ? "CPU Fan" : "GPU Fan";
        }
        
        /// <summary>
        /// Apply a fan speed and verify it was actually applied by reading back RPM.
        /// </summary>
        /// <param name="fanIndex">Fan index (0 for CPU, 1 for GPU typically)</param>
        /// <param name="targetPercent">Target fan speed percentage (0-100)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Result containing applied values and verification status</returns>
        public async Task<FanApplyResult> ApplyAndVerifyFanSpeedAsync(
            int fanIndex, 
            int targetPercent,
            CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new FanApplyResult
            {
                FanIndex = fanIndex,
                FanName = GetFanName(fanIndex),
                RequestedPercent = targetPercent,
                ExpectedRpm = PercentToExpectedRpm(targetPercent)
            };
            
            if (_wmiBios == null || !_wmiBios.IsAvailable)
            {
                // Fall back to FanService if WMI is not available
                if (_fanService == null)
                {
                    result.ErrorMessage = "No fan control backend available";
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }
                
                // Use FanService for fan control
                return await ApplyAndVerifyFanSpeedViaFanServiceAsync(fanIndex, targetPercent, ct);
            }
            
            try
            {
                // Read current state before change (from telemetry)
                result.ActualRpmBefore = GetCurrentRpm(fanIndex);
                _logging.Info($"Fan {fanIndex} ({result.FanName}) before: {result.ActualRpmBefore} RPM");
                
                // Convert percent to level
                result.AppliedLevel = PercentToLevel(targetPercent);
                
                // For 100%, use SetFanMax which achieves true maximum RPM
                // SetFanLevel(55) may be capped by BIOS on some models
                if (targetPercent >= 100)
                {
                    result.WmiCallSucceeded = _wmiBios.SetFanMax(true);
                    if (result.WmiCallSucceeded)
                    {
                        _logging.Info($"Fan {fanIndex} set to MAX (100%) via SetFanMax");
                    }
                    else
                    {
                        // Fallback to SetFanLevel
                        result.WmiCallSucceeded = _wmiBios.SetFanLevel(55, 55);
                        _logging.Info($"Fan {fanIndex} set to 100% via SetFanLevel(55) fallback");
                    }
                }
                else
                {
                    // For <100%, disable max mode first (in case it was enabled)
                    _wmiBios.SetFanMax(false);
                    
                    // Apply the fan level (need both fans)
                    byte fan1Level = (byte)(fanIndex == 0 ? result.AppliedLevel : 0);
                    byte fan2Level = (byte)(fanIndex == 1 ? result.AppliedLevel : 0);
                    
                    // Get current levels for the other fan to preserve it
                    var currentLevels = _wmiBios.GetFanLevel();
                    if (currentLevels.HasValue)
                    {
                        if (fanIndex == 0)
                            fan2Level = currentLevels.Value.fan2;
                        else
                            fan1Level = currentLevels.Value.fan1;
                    }
                    
                    result.WmiCallSucceeded = _wmiBios.SetFanLevel(fan1Level, fan2Level);
                    
                    if (result.WmiCallSucceeded)
                    {
                        _logging.Info($"Fan {fanIndex} set to level {result.AppliedLevel} ({targetPercent}%)");
                    }
                }
                
                // Check if WMI call failed
                if (!result.WmiCallSucceeded)
                {
                    _logging.Warn($"WMI fan control failed for fan {fanIndex}, level {result.AppliedLevel}");
                    result.ErrorMessage = "WMI call returned false";
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }
                
                // Wait for fan to respond (fans have mechanical inertia)
                await Task.Delay(FanResponseDelayMs, ct);
                
                // Multi-sample verification: take several RPM readings and average them
                int totalAttempts = 0;
                for (int attempt = 0; attempt <= VerificationRetries; attempt++)
                {
                    totalAttempts = attempt + 1;
                    
                    // Take multiple samples and average them for stability
                    var rpmSamples = new int[VerificationSamples];
                    for (int i = 0; i < VerificationSamples; i++)
                    {
                        rpmSamples[i] = GetCurrentRpm(fanIndex);
                        if (i < VerificationSamples - 1)
                            await Task.Delay(SampleDelayMs, ct);
                    }
                    
                    // Use average RPM for verification
                    result.ActualRpmAfter = (int)rpmSamples.Average();
                    result.SampleCount = VerificationSamples;
                    
                    // Calculate standard deviation for stability scoring (v2.7.0)
                    double mean = rpmSamples.Average();
                    double sumSquares = rpmSamples.Sum(r => Math.Pow(r - mean, 2));
                    result.RpmStandardDeviation = Math.Sqrt(sumSquares / rpmSamples.Length);
                    
                    _logging.Info($"Fan {fanIndex} RPM samples: [{string.Join(", ", rpmSamples)}], Average: {result.ActualRpmAfter} RPM, StdDev: {result.RpmStandardDeviation:F1}");
                    
                    // Verify the change
                    result.VerificationPassed = VerifyRpm(result);
                    
                    if (result.VerificationPassed)
                    {
                        _logging.Info($"✓ Fan {fanIndex} verified: {result.ActualRpmAfter} RPM (expected ~{result.ExpectedRpm}, deviation {result.DeviationPercent:F1}%, score {result.VerificationScore}/100 {result.ScoreRating})");
                        break;
                    }
                    else if (attempt < VerificationRetries)
                    {
                        _logging.Warn($"Fan {fanIndex} verification attempt {attempt + 1} failed: Expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM ({result.DeviationPercent:F1}% deviation). Retrying...");
                        await Task.Delay(RetryDelayMs, ct);
                    }
                }
                
                // Final diagnostic message if verification still failed
                if (!result.VerificationPassed)
                {
                    _logging.Error($"Fan {fanIndex} verification failed after {totalAttempts} attempts: expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM");
                    result.ErrorMessage = $"RPM verification failed after {totalAttempts} attempts: expected ~{result.ExpectedRpm}, got {result.ActualRpmAfter}";
                    
                    // Track failure for diagnostics
                    _logging.Warn($"⚠️ Fan {fanIndex} commands appear ineffective. This model may not support WMI-based control. Consider using OGH proxy backend if available, or verify EC register mapping.");
                    result.ErrorMessage += " [TIP: Consider switching to OGH proxy backend for this model]";
                    
                    // Auto-revert attempt: set fans back to auto mode
                    _logging.Warn($"Attempting to restore auto control due to verification failure...");
                    try
                    {
                        _wmiBios?.SetFanMode(HpWmiBios.FanMode.Default);
                    }
                    catch { /* Ignore revert errors */ }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logging.Error($"Fan verification exception: {ex.Message}", ex);
            }
            
            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Apply and verify fan speed using FanService (fallback when WMI is unavailable).
        /// </summary>
        private async Task<FanApplyResult> ApplyAndVerifyFanSpeedViaFanServiceAsync(
            int fanIndex, 
            int targetPercent,
            CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new FanApplyResult
            {
                FanIndex = fanIndex,
                FanName = GetFanName(fanIndex),
                RequestedPercent = targetPercent,
                ExpectedRpm = PercentToExpectedRpm(targetPercent)
            };

            try
            {
                // Read current state before change (from telemetry)
                result.ActualRpmBefore = GetCurrentRpm(fanIndex);
                _logging.Info($"Fan {fanIndex} ({result.FanName}) before: {result.ActualRpmBefore} RPM");

                // Get current fan speeds to preserve the other fan
                var currentCpuPercent = 50; // Default
                var currentGpuPercent = 50; // Default
                
                // Try to estimate current speeds from telemetry
                if (_fanService?.FanTelemetry != null && _fanService.FanTelemetry.Count > 0)
                {
                    // Estimate based on RPM
                    for (int i = 0; i < _fanService.FanTelemetry.Count && i < 2; i++)
                    {
                        var rpm = _fanService.FanTelemetry[i].SpeedRpm;
                        var estimatedPercent = RpmToPercent(rpm);
                        if (i == 0) currentCpuPercent = estimatedPercent;
                        else currentGpuPercent = estimatedPercent;
                    }
                }

                // Set the target fan to the requested percent, keep other fan at current
                int cpuPercent = fanIndex == 0 ? targetPercent : currentCpuPercent;
                int gpuPercent = fanIndex == 1 ? targetPercent : currentGpuPercent;

                result.WmiCallSucceeded = _fanService!.ForceSetFanSpeeds(cpuPercent, gpuPercent);
                
                if (result.WmiCallSucceeded)
                {
                    _logging.Info($"Fan {fanIndex} set to {targetPercent}% via FanService (CPU:{cpuPercent}%, GPU:{gpuPercent}%)");
                }
                else
                {
                    result.ErrorMessage = "FanService call returned false";
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                // Wait for fan to respond
                await Task.Delay(FanResponseDelayMs, ct);

                // Multi-sample verification
                int totalAttempts = 0;
                for (int attempt = 0; attempt <= VerificationRetries; attempt++)
                {
                    totalAttempts = attempt + 1;

                    // Take multiple samples
                    var rpmSamples = new int[VerificationSamples];
                    for (int i = 0; i < VerificationSamples; i++)
                    {
                        rpmSamples[i] = GetCurrentRpm(fanIndex);
                        if (i < VerificationSamples - 1)
                            await Task.Delay(SampleDelayMs, ct);
                    }

                    // Use average RPM
                    result.ActualRpmAfter = (int)rpmSamples.Average();
                    result.SampleCount = VerificationSamples;
                    
                    // Calculate standard deviation for stability scoring (v2.7.0)
                    double mean = rpmSamples.Average();
                    double sumSquares = rpmSamples.Sum(r => Math.Pow(r - mean, 2));
                    result.RpmStandardDeviation = Math.Sqrt(sumSquares / rpmSamples.Length);

                    _logging.Info($"Fan {fanIndex} RPM samples: [{string.Join(", ", rpmSamples)}], Average: {result.ActualRpmAfter} RPM, StdDev: {result.RpmStandardDeviation:F1}");

                    // Verify
                    result.VerificationPassed = VerifyRpm(result);

                    if (result.VerificationPassed)
                    {
                        _logging.Info($"✓ Fan {fanIndex} verified: {result.ActualRpmAfter} RPM (expected ~{result.ExpectedRpm}, deviation {result.DeviationPercent:F1}%, score {result.VerificationScore}/100 {result.ScoreRating})");
                        break;
                    }
                    else if (attempt < VerificationRetries)
                    {
                        _logging.Warn($"Fan {fanIndex} verification attempt {attempt + 1} failed: Expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM ({result.DeviationPercent:F1}% deviation). Retrying...");
                        await Task.Delay(RetryDelayMs, ct);
                    }
                }

                // Final diagnostic
                if (!result.VerificationPassed)
                {
                    _logging.Error($"Fan {fanIndex} verification failed after {totalAttempts} attempts: expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM");
                    result.ErrorMessage = $"RPM verification failed after {totalAttempts} attempts: expected ~{result.ExpectedRpm}, got {result.ActualRpmAfter}";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logging.Error($"Fan verification via FanService exception: {ex.Message}", ex);
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }
        
        /// <summary>
        /// Read current fan speed without making changes (diagnostic tool).
        /// </summary>
        public (int rpm, int level) GetCurrentFanState(int fanIndex)
        {
            var rpm = GetCurrentRpm(fanIndex);
            
            // Get level from WMI BIOS if available
            int level = 0;
            if (_wmiBios?.IsAvailable == true)
            {
                var levels = _wmiBios.GetFanLevel();
                if (levels.HasValue)
                {
                    level = fanIndex == 0 ? levels.Value.fan1 : levels.Value.fan2;
                }
            }
            else
            {
                // Estimate from RPM
                level = RpmToLevel(rpm);
            }
            
            return (rpm, level);
        }
        
        /// <summary>
        /// Read current fan speed with RPM source information.
        /// </summary>
        public (int rpm, int level, RpmSource source) GetCurrentFanStateWithSource(int fanIndex)
        {
            var (rpm, level) = GetCurrentFanState(fanIndex);
            
            // Get source from telemetry
            RpmSource source = RpmSource.Unknown;
            if (_fanService?.FanTelemetry != null && _fanService.FanTelemetry.Count > fanIndex)
            {
                source = _fanService.FanTelemetry[fanIndex].RpmSource;
            }
            
            return (rpm, level, source);
        }
        
        /// <summary>
        /// Verify fan reading by checking it multiple times.
        /// </summary>
        public async Task<(int avg, int min, int max)> GetStableFanRpmAsync(int fanIndex, int samples = 3, CancellationToken ct = default)
        {
            int sum = 0;
            int min = int.MaxValue;
            int max = int.MinValue;
            
            for (int i = 0; i < samples; i++)
            {
                var rpm = GetCurrentRpm(fanIndex);
                sum += rpm;
                min = Math.Min(min, rpm);
                max = Math.Max(max, rpm);
                
                if (i < samples - 1)
                    await Task.Delay(500, ct);
            }
            
            return (sum / samples, min == int.MaxValue ? 0 : min, max == int.MinValue ? 0 : max);
        }
        
        #region Conversion Helpers
        
        /// <summary>
        /// Convert percentage (0-100) to HP fan level.
        /// HP typically uses levels 0-55, not 0-100.
        /// </summary>
        private int PercentToLevel(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            // Linear mapping: 0% -> 0, 100% -> MaxLevel
            return (int)Math.Round(percent / 100.0 * MaxLevel);
        }
        
        /// <summary>
        /// Convert HP fan level back to a percentage.
        /// </summary>
        private int LevelToPercent(int level)
        {
            level = Math.Clamp(level, 0, MaxLevel);
            return (int)Math.Round(level / (double)MaxLevel * 100);
        }

        /// <summary>
        /// Convert expected percentage to expected RPM.
        /// This is an approximation - real calibration data would be better.
        /// </summary>
        private int PercentToExpectedRpm(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            // Assume linear relationship (could be calibrated per-model)
            // Most laptops: 0% = 0 RPM, 100% = ~5000-6000 RPM
            if (percent == 0) return 0;
            return (int)(MinRpm + (MaxRpm - MinRpm) * (percent / 100.0));
        }
        
        /// <summary>
        /// Estimate level from RPM (inverse of apply).
        /// </summary>
        private int RpmToLevel(int rpm)
        {
            if (rpm <= 0) return 0;
            var percent = Math.Min(100, (rpm / (double)MaxRpm) * 100);
            return (int)Math.Round(percent / 100.0 * MaxLevel);
        }

        /// <summary>
        /// Estimate percentage from RPM.
        /// </summary>
        private int RpmToPercent(int rpm)
        {
            if (rpm <= 0) return 0;
            return (int)Math.Min(100, (rpm / (double)MaxRpm) * 100);
        }
        
        /// <summary>
        /// Check if the actual RPM is within tolerance of expected.
        /// </summary>
        private bool VerifyRpm(FanApplyResult result)
        {
            // If requesting 0%, fan should be off or very low
            if (result.RequestedPercent == 0)
            {
                return result.ActualRpmAfter < 1000;  // Should be nearly stopped
            }
            
            // For non-zero, check within tolerance
            var tolerance = result.ExpectedRpm * RpmTolerance;
            return Math.Abs(result.ActualRpmAfter - result.ExpectedRpm) <= tolerance;
        }

        #endregion

        /// <summary>
        /// Enhanced verification with multiple read-backs and auto-revert capability.
        /// Attempts multiple verification cycles before giving up.
        /// </summary>
        public async Task<FanApplyResult> ApplyWithEnhancedVerificationAsync(
            int fanIndex,
            int targetPercent,
            bool autoRevertOnFailure = true,
            CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new FanApplyResult
            {
                FanIndex = fanIndex,
                FanName = GetFanName(fanIndex),
                RequestedPercent = targetPercent,
                ExpectedRpm = PercentToExpectedRpm(targetPercent)
            };

            if (_wmiBios == null || !_wmiBios.IsAvailable)
            {
                result.ErrorMessage = "WMI BIOS not available";
                result.Duration = DateTime.Now - startTime;
                return result;
            }

            // Store original state for potential revert
            var originalState = GetCurrentFanState(fanIndex);
            result.ActualRpmBefore = originalState.rpm;

            try
            {
                _logging.Info($"Starting enhanced verification for fan {fanIndex} ({result.FanName}) at {targetPercent}%");

                // First attempt with standard verification
                var standardResult = await ApplyAndVerifyFanSpeedAsync(fanIndex, targetPercent, ct);
                if (standardResult.Success)
                {
                    _logging.Info($"✓ Enhanced verification passed on first attempt for fan {fanIndex}");
                    return standardResult;
                }

                _logging.Warn($"Standard verification failed for fan {fanIndex}, attempting enhanced verification cycles...");

                // Enhanced verification: multiple read-back attempts
                for (int cycle = 1; cycle <= 2; cycle++) // 2 additional cycles
                {
                    _logging.Info($"Enhanced verification cycle {cycle} for fan {fanIndex}");

                    // Wait longer for fan to stabilize
                    await Task.Delay(FanResponseDelayMs + (cycle * 500), ct);

                    // Take more samples for better accuracy
                    var (avgRpm, minRpm, maxRpm) = await GetStableFanRpmAsync(fanIndex, 7, ct); // 7 samples

                    result.ActualRpmAfter = avgRpm;

                    // Check if we're within a more lenient tolerance for enhanced verification
                    var lenientTolerance = result.ExpectedRpm * (RpmTolerance * 1.5); // 50% more lenient
                    var withinLenientTolerance = Math.Abs(result.ActualRpmAfter - result.ExpectedRpm) <= lenientTolerance;

                    if (withinLenientTolerance)
                    {
                        _logging.Info($"✓ Enhanced verification passed on cycle {cycle} for fan {fanIndex}: {avgRpm} RPM (range: {minRpm}-{maxRpm})");
                        result.VerificationPassed = true;
                        result.WmiCallSucceeded = true; // Assume it worked since we got here
                        result.Duration = DateTime.Now - startTime;
                        return result;
                    }

                    _logging.Warn($"Enhanced verification cycle {cycle} failed: expected ~{result.ExpectedRpm}, got {avgRpm} RPM (range: {minRpm}-{maxRpm})");
                }

                // All verification attempts failed
                result.ActualRpmAfter = GetCurrentRpm(fanIndex);
                result.VerificationPassed = false;
                result.WmiCallSucceeded = false;
                result.ErrorMessage = $"Enhanced verification failed after multiple attempts: expected ~{result.ExpectedRpm}, got {result.ActualRpmAfter}";

                // Auto-revert if enabled and requested
                if (autoRevertOnFailure && AutoRevertOnFailure)
                {
                    _logging.Warn($"Auto-reverting fan {fanIndex} to previous state due to verification failure...");
                    await Task.Delay(RevertDelayMs, ct);

                    try
                    {
                        // Try to restore original fan level
                        var originalPercent = LevelToPercent(originalState.level);
                        var revertResult = await ApplyAndVerifyFanSpeedAsync(fanIndex, originalPercent, ct);

                        if (revertResult.Success)
                        {
                            _logging.Info($"✓ Successfully reverted fan {fanIndex} to {originalPercent}% ({originalState.rpm} RPM)");
                            result.ErrorMessage += " [Auto-reverted to previous state]";
                        }
                        else
                        {
                            _logging.Warn($"Failed to auto-revert fan {fanIndex} to previous state");
                            result.ErrorMessage += " [Auto-revert failed]";
                        }
                    }
                    catch (Exception revertEx)
                    {
                        _logging.Error($"Exception during auto-revert: {revertEx.Message}");
                        result.ErrorMessage += " [Auto-revert exception]";
                    }
                }

                _logging.Error($"✗ Enhanced verification completely failed for fan {fanIndex}: {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Enhanced verification exception: {ex.Message}";
                _logging.Error($"Enhanced verification exception for fan {fanIndex}: {ex.Message}", ex);
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Perform a comprehensive fan calibration test across multiple speeds.
        /// Useful for generating calibration data for specific laptop models.
        /// </summary>
        public async Task<FanCalibrationResult> PerformFanCalibrationAsync(
            int fanIndex,
            CancellationToken ct = default)
        {
            var calibrationResult = new FanCalibrationResult
            {
                FanIndex = fanIndex,
                FanName = GetFanName(fanIndex),
                CalibrationPoints = new List<FanCalibrationPoint>(),
                StartTime = DateTime.Now
            };

            _logging.Info($"Starting fan calibration for {calibrationResult.FanName} (fan {fanIndex})");

            // Test speeds: 0%, 20%, 40%, 60%, 80%, 100%
            var testSpeeds = new[] { 0, 20, 40, 60, 80, 100 };

            foreach (var speed in testSpeeds)
            {
                if (ct.IsCancellationRequested)
                {
                    calibrationResult.ErrorMessage = "Calibration cancelled";
                    break;
                }

                _logging.Info($"Calibrating fan {fanIndex} at {speed}%...");

                try
                {
                    // Apply the speed
                    var applyResult = await ApplyWithEnhancedVerificationAsync(fanIndex, speed, false, ct);

                    // Wait for stabilization
                    await Task.Delay(3000, ct);

                    // Take multiple readings
                    var (avgRpm, minRpm, maxRpm) = await GetStableFanRpmAsync(fanIndex, 10, ct);

                    var point = new FanCalibrationPoint
                    {
                        RequestedPercent = speed,
                        AppliedLevel = applyResult.AppliedLevel,
                        MeasuredRpm = avgRpm,
                        RpmRangeMin = minRpm,
                        RpmRangeMax = maxRpm,
                        VerificationPassed = applyResult.Success,
                        Duration = applyResult.Duration
                    };

                    calibrationResult.CalibrationPoints.Add(point);

                    _logging.Info($"Calibration point: {speed}% → {avgRpm} RPM (range: {minRpm}-{maxRpm}, verified: {applyResult.Success})");

                    // Wait between tests to prevent thermal stress
                    if (speed < 100)
                        await Task.Delay(2000, ct);
                }
                catch (Exception ex)
                {
                    _logging.Error($"Calibration failed at {speed}%: {ex.Message}");

                    var errorPoint = new FanCalibrationPoint
                    {
                        RequestedPercent = speed,
                        ErrorMessage = ex.Message
                    };
                    calibrationResult.CalibrationPoints.Add(errorPoint);
                }
            }

            calibrationResult.EndTime = DateTime.Now;
            calibrationResult.Duration = calibrationResult.EndTime - calibrationResult.StartTime;
            calibrationResult.Success = calibrationResult.CalibrationPoints.All(p => p.VerificationPassed);

            _logging.Info($"Fan calibration completed for {calibrationResult.FanName}: {calibrationResult.CalibrationPoints.Count} points, success: {calibrationResult.Success}");

            return calibrationResult;
        }
    }

    /// <summary>
    /// Result of a fan calibration operation.
    /// </summary>
    public class FanCalibrationResult
    {
        public int FanIndex { get; set; }
        public string FanName { get; set; } = "";
        public List<FanCalibrationPoint> CalibrationPoints { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Overall calibration score (0-100) based on all test points (v2.7.0).
        /// </summary>
        public int OverallScore
        {
            get
            {
                if (!CalibrationPoints.Any()) return 0;
                var validPoints = CalibrationPoints.Where(p => p.VerificationPassed).ToList();
                if (!validPoints.Any()) return 0;
                return (int)validPoints.Average(p => p.Score);
            }
        }
        
        /// <summary>
        /// Human-readable score rating for the overall calibration.
        /// </summary>
        public string OverallRating => OverallScore switch
        {
            >= 90 => "Excellent",
            >= 70 => "Good",
            >= 50 => "Fair",
            >= 25 => "Poor",
            _ => "Failed"
        };
    }

    /// <summary>
    /// Single calibration point data.
    /// </summary>
    public class FanCalibrationPoint
    {
        public int RequestedPercent { get; set; }
        public int AppliedLevel { get; set; }
        public int MeasuredRpm { get; set; }
        public int RpmRangeMin { get; set; }
        public int RpmRangeMax { get; set; }
        public bool VerificationPassed { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// Expected RPM for the requested percent.
        /// </summary>
        public int ExpectedRpm => (int)(RequestedPercent / 100.0 * 5500);
        
        /// <summary>
        /// Deviation percentage from expected RPM.
        /// </summary>
        public double DeviationPercent => ExpectedRpm > 0 
            ? Math.Abs(MeasuredRpm - ExpectedRpm) / (double)ExpectedRpm * 100 
            : 0;
        
        /// <summary>
        /// Score for this calibration point (0-100).
        /// </summary>
        public int Score
        {
            get
            {
                if (!VerificationPassed) return 0;
                if (MeasuredRpm <= 0 && RequestedPercent > 0) return 5;
                
                // Score based on deviation (15% = 0, 0% = 100)
                double score = Math.Max(0, 100 - (DeviationPercent / 15.0 * 100));
                
                // Bonus for stability (narrow RPM range)
                int range = RpmRangeMax - RpmRangeMin;
                if (range < 50) score = Math.Min(100, score + 5); // Very stable
                else if (range > 200) score = Math.Max(0, score - 10); // Unstable
                
                return (int)Math.Round(score);
            }
        }
    }
}
