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
        /// True if the fan speed change was successfully applied and verified.
        /// </summary>
        public bool Success => WmiCallSucceeded && VerificationPassed;
        
        /// <summary>
        /// Percentage difference from expected RPM (for diagnostics).
        /// </summary>
        public double DeviationPercent => ExpectedRpm > 0 
            ? Math.Abs(ActualRpmAfter - ExpectedRpm) / (double)ExpectedRpm * 100 
            : 0;
    }

    /// <summary>
    /// Provides closed-loop verification for fan control commands.
    /// After setting a fan speed, reads back the actual RPM to verify it was applied.
    /// 
    /// This addresses the issue where "Requested % doesn't match actual fan speed".
    /// </summary>
    public class FanVerificationService : IFanVerificationService
    {
        private readonly HpWmiBios? _wmiBios;
        private readonly FanService? _fanService;
        private readonly LoggingService _logging;
        private const int MaxLevel = 55;  // HP uses 55 as max on most models
        private const int MinRpm = 0;
        private const int MaxRpm = 5500;  // Typical max RPM
        
        // Verification timing
        private const int FanResponseDelayMs = 2500;  // Fans have mechanical inertia
        private const int RetryDelayMs = 2000;
        private const double RpmTolerance = 0.20;  // 20% tolerance for RPM verification
        
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
                result.ErrorMessage = "WMI BIOS not available";
                result.Duration = DateTime.Now - startTime;
                return result;
            }
            
            try
            {
                // Read current state before change (from telemetry)
                result.ActualRpmBefore = GetCurrentRpm(fanIndex);
                _logging.Info($"Fan {fanIndex} ({result.FanName}) before: {result.ActualRpmBefore} RPM");
                
                // Convert percent to level
                result.AppliedLevel = PercentToLevel(targetPercent);
                
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
                
                if (!result.WmiCallSucceeded)
                {
                    _logging.Warn($"WMI SetFanLevel failed for fan {fanIndex}, level {result.AppliedLevel}");
                    result.ErrorMessage = "WMI call returned false";
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }
                
                _logging.Info($"Fan {fanIndex} set to level {result.AppliedLevel} ({targetPercent}%)");
                
                // Wait for fan to respond (fans have mechanical inertia)
                await Task.Delay(FanResponseDelayMs, ct);
                
                // Read back actual RPM from telemetry
                result.ActualRpmAfter = GetCurrentRpm(fanIndex);
                
                // Verify the change
                result.VerificationPassed = VerifyRpm(result);
                
                if (!result.VerificationPassed)
                {
                    _logging.Warn($"Fan {fanIndex} verification failed: Expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM ({result.DeviationPercent:F1}% deviation)");
                    
                    // Retry once
                    await Task.Delay(RetryDelayMs, ct);
                    result.ActualRpmAfter = GetCurrentRpm(fanIndex);
                    result.VerificationPassed = VerifyRpm(result);
                    
                    if (!result.VerificationPassed)
                    {
                        _logging.Error($"Fan {fanIndex} control not responding as expected after retry. Model may need calibration.");
                        result.ErrorMessage = $"RPM verification failed: expected ~{result.ExpectedRpm}, got {result.ActualRpmAfter}";
                    }
                    else
                    {
                        _logging.Info($"✓ Fan {fanIndex} verified on retry: {result.ActualRpmAfter} RPM");
                    }
                }
                else
                {
                    _logging.Info($"✓ Fan {fanIndex} verified: {result.ActualRpmAfter} RPM (expected ~{result.ExpectedRpm}, deviation {result.DeviationPercent:F1}%)");
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
    }
}
