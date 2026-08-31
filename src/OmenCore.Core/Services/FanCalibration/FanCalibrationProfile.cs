using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmenCore.Services.FanCalibration
{
    /// <summary>
    /// Fan calibration profile for a specific OMEN model.
    /// Stores the mapping from fan level to actual RPM.
    /// </summary>
    public class FanCalibrationProfile
    {
        /// <summary>
        /// HP Product ID (from WMI or registry).
        /// </summary>
        public string ProductId { get; set; } = string.Empty;
        
        /// <summary>
        /// Human-readable model name.
        /// </summary>
        public string ModelName { get; set; } = string.Empty;
        
        /// <summary>
        /// Maximum fan level supported by this model (e.g., 55 or 100).
        /// </summary>
        public int MaxLevel { get; set; } = 55;
        
        /// <summary>
        /// Minimum level where fans actually spin (many models don't spin below 20-30).
        /// </summary>
        public int MinSpinLevel { get; set; } = 20;
        
        /// <summary>
        /// Number of fans in the system.
        /// </summary>
        public int FanCount { get; set; } = 2;
        
        /// <summary>
        /// Fan 0 (CPU) calibration data: level -> RPM mapping.
        /// Key = level (0-55 or 0-100), Value = measured RPM.
        /// </summary>
        public Dictionary<int, int> Fan0LevelToRpm { get; set; } = new();
        
        /// <summary>
        /// Fan 1 (GPU) calibration data: level -> RPM mapping.
        /// </summary>
        public Dictionary<int, int> Fan1LevelToRpm { get; set; } = new();
        
        /// <summary>
        /// Maximum RPM measured for Fan 0.
        /// </summary>
        public int Fan0MaxRpm { get; set; } = 5000;
        
        /// <summary>
        /// Maximum RPM measured for Fan 1.
        /// </summary>
        public int Fan1MaxRpm { get; set; } = 5000;
        
        /// <summary>
        /// Whether this model supports direct RPM commands (rare).
        /// </summary>
        public bool SupportsDirectRpm { get; set; } = false;
        
        /// <summary>
        /// When the calibration was performed.
        /// </summary>
        public DateTime CalibratedAt { get; set; } = DateTime.MinValue;
        
        /// <summary>
        /// Whether calibration data is valid and complete.
        /// </summary>
        [JsonIgnore]
        public bool IsValid => 
            !string.IsNullOrEmpty(ProductId) && 
            Fan0LevelToRpm.Count > 0 && 
            MaxLevel > 0 &&
            CalibratedAt != DateTime.MinValue;
        
        /// <summary>
        /// Convert a target percentage (0-100) to the appropriate level for this model.
        /// </summary>
        public int PercentToLevel(int targetPercent)
        {
            if (targetPercent <= 0) return 0;
            if (targetPercent >= 100) return MaxLevel;
            
            // Scale percent to level range
            // Account for MinSpinLevel - below this, fans don't actually spin
            if (targetPercent < (MinSpinLevel * 100 / MaxLevel))
            {
                return 0; // Below minimum spin threshold
            }
            
            return (int)Math.Round(targetPercent * MaxLevel / 100.0);
        }
        
        /// <summary>
        /// Get expected RPM for a target percentage.
        /// </summary>
        public int GetExpectedRpm(int fanIndex, int targetPercent)
        {
            // Ensure we have at least a baseline curve so callers don't get zero RPM
            EnsureBaselineCurve();

            var level = PercentToLevel(targetPercent);
            var mapping = fanIndex == 0 ? Fan0LevelToRpm : Fan1LevelToRpm;
            
            // Find closest calibrated level
            if (mapping.TryGetValue(level, out int rpm))
            {
                return rpm;
            }
            
            // Interpolate between known points
            int lowerLevel = 0, upperLevel = MaxLevel;
            int lowerRpm = 0, upperRpm = fanIndex == 0 ? Fan0MaxRpm : Fan1MaxRpm;
            
            foreach (var kvp in mapping)
            {
                if (kvp.Key <= level && kvp.Key > lowerLevel)
                {
                    lowerLevel = kvp.Key;
                    lowerRpm = kvp.Value;
                }
                if (kvp.Key >= level && kvp.Key < upperLevel)
                {
                    upperLevel = kvp.Key;
                    upperRpm = kvp.Value;
                }
            }
            
            if (upperLevel == lowerLevel) return lowerRpm;
            
            // Linear interpolation
            double ratio = (double)(level - lowerLevel) / (upperLevel - lowerLevel);
            return (int)(lowerRpm + ratio * (upperRpm - lowerRpm));
        }

        /// <summary>
        /// Seed a minimal baseline curve when no calibration data exists.
        /// This keeps verification and UI flows from reporting 0 RPM on first run.
        /// </summary>
        private void EnsureBaselineCurve()
        {
            if (Fan0LevelToRpm.Count == 0)
            {
                Fan0LevelToRpm[0] = 0;
                Fan0LevelToRpm[MaxLevel] = Fan0MaxRpm;
                Fan0LevelToRpm[Math.Max(MinSpinLevel, 25)] = (int)(Fan0MaxRpm * 0.35);
            }

            if (Fan1LevelToRpm.Count == 0)
            {
                Fan1LevelToRpm[0] = 0;
                Fan1LevelToRpm[MaxLevel] = Fan1MaxRpm;
                Fan1LevelToRpm[Math.Max(MinSpinLevel, 25)] = (int)(Fan1MaxRpm * 0.35);
            }
        }
        
        /// <summary>
        /// Get the level that would produce a target RPM.
        /// </summary>
        public int RpmToLevel(int fanIndex, int targetRpm)
        {
            var mapping = fanIndex == 0 ? Fan0LevelToRpm : Fan1LevelToRpm;
            var maxRpm = fanIndex == 0 ? Fan0MaxRpm : Fan1MaxRpm;
            
            if (targetRpm <= 0) return 0;
            if (targetRpm >= maxRpm) return MaxLevel;
            
            // Find the level that produces closest RPM
            int closestLevel = 0;
            int closestDiff = int.MaxValue;
            
            foreach (var kvp in mapping)
            {
                int diff = Math.Abs(kvp.Value - targetRpm);
                if (diff < closestDiff)
                {
                    closestDiff = diff;
                    closestLevel = kvp.Key;
                }
            }
            
            return closestLevel;
        }
    }
    
    /// <summary>
    /// Result of applying a fan speed change.
    /// </summary>
    public class FanApplyResult
    {
        public int FanIndex { get; set; }
        public int RequestedPercent { get; set; }
        public int AppliedLevel { get; set; }
        public int ExpectedRpm { get; set; }
        public int ActualRpmBefore { get; set; }
        public int ActualRpmAfter { get; set; }
        public bool WmiCallSucceeded { get; set; }
        public TimeSpan ResponseTime { get; set; }
        
        /// <summary>
        /// Whether the fan responded as expected (within 15% tolerance).
        /// </summary>
        public bool VerificationPassed => 
            WmiCallSucceeded && 
            (ExpectedRpm == 0 || Math.Abs(ActualRpmAfter - ExpectedRpm) < ExpectedRpm * 0.15);
        
        /// <summary>
        /// Percentage error from expected RPM.
        /// </summary>
        public double PercentError => 
            ExpectedRpm > 0 ? Math.Abs(ActualRpmAfter - ExpectedRpm) * 100.0 / ExpectedRpm : 0;
    }
    
    /// <summary>
    /// Fan calibration step during the calibration wizard.
    /// </summary>
    public class CalibrationStep
    {
        public int Level { get; set; }
        public int Fan0Rpm { get; set; }
        public int Fan1Rpm { get; set; }
        public bool Completed { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
