using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services.FanCalibration
{
    /// <summary>
    /// Service for fan calibration, verification, and closed-loop control.
    /// 
    /// Key features:
    /// 1. Calibration wizard: Steps through fan levels to measure actual RPM
    /// 2. Closed-loop verification: Confirms fan speed changes were applied
    /// 3. Model-specific profiles: Stores calibration data per OMEN model
    /// </summary>
    public class FanCalibrationService
    {
        private readonly IFanController _fanController;
        private readonly SystemInfoService _systemInfo;
        private readonly LoggingService _logging;
        private FanCalibrationProfile? _activeProfile;
        
        // Calibration wizard state
        private bool _calibrationInProgress = false;
        private readonly List<CalibrationStep> _calibrationSteps = new();
        private int _currentStep = 0;
        
        // Known good profiles (can be loaded from file)
        private readonly Dictionary<string, FanCalibrationProfile> _knownProfiles = new();
        
        // Default calibration levels to test
        private static readonly int[] DefaultCalibrationLevels = { 0, 10, 20, 30, 40, 45, 50, 55 };
        
        // Fan response delay (mechanical inertia)
        private const int FanResponseDelayMs = 3000;

        public FanCalibrationProfile? ActiveProfile => _activeProfile;
        public bool IsCalibrating => _calibrationInProgress;
        public int CalibrationProgress => _calibrationSteps.Count > 0 
            ? (_currentStep * 100 / _calibrationSteps.Count) 
            : 0;

        public event EventHandler<CalibrationStep>? CalibrationStepCompleted;
        public event EventHandler<FanCalibrationProfile>? CalibrationCompleted;
        public event EventHandler<string>? CalibrationError;

        public FanCalibrationService(
            IFanController fanController,
            SystemInfoService systemInfo,
            LoggingService logging)
        {
            _fanController = fanController;
            _systemInfo = systemInfo;
            _logging = logging;
            
            LoadKnownProfiles();
        }
        
        /// <summary>
        /// Initialize the service and try to load a profile for the current system.
        /// </summary>
        public async Task InitializeAsync()
        {
            var productId = GetCurrentProductId();
            
            // Try to load existing profile
            if (_knownProfiles.TryGetValue(productId, out var profile))
            {
                _activeProfile = profile;
                _logging.Info($"Loaded fan calibration profile for {profile.ModelName} (ProductId: {productId})");
            }
            else
            {
                // Create a default profile based on common OMEN settings
                _activeProfile = CreateDefaultProfile(productId);
                _logging.Info($"Using default fan calibration profile for ProductId: {productId}");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Start the calibration wizard.
        /// </summary>
        public async Task StartCalibrationAsync(CancellationToken ct = default)
        {
            if (_calibrationInProgress)
            {
                _logging.Warn("Calibration already in progress");
                return;
            }
            
            _calibrationInProgress = true;
            _calibrationSteps.Clear();
            _currentStep = 0;
            
            var productId = GetCurrentProductId();
            var profile = new FanCalibrationProfile
            {
                ProductId = productId,
                ModelName = _systemInfo.GetSystemInfo().Model ?? "Unknown OMEN",
                MaxLevel = 55, // Default for most OMEN models
                FanCount = 2
            };
            
            try
            {
                _logging.Info("Starting fan calibration wizard...");
                
                // Initialize steps
                foreach (var level in DefaultCalibrationLevels)
                {
                    _calibrationSteps.Add(new CalibrationStep { Level = level });
                }
                
                // Step through each level
                foreach (var step in _calibrationSteps)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    _logging.Info($"Calibration step: Setting fan level to {step.Level}...");
                    
                    // Apply the level
                    var percent = step.Level * 100 / 55; // Convert level to percent
                    _fanController.SetFanSpeed(percent);
                    
                    // Wait for fans to respond
                    await Task.Delay(FanResponseDelayMs, ct);
                    
                    // Read actual RPM - fans are returned as a list indexed by position
                    var fanSpeeds = _fanController.ReadFanSpeeds().ToList();
                    step.Fan0Rpm = fanSpeeds.Count > 0 ? fanSpeeds[0].Rpm : 0;
                    step.Fan1Rpm = fanSpeeds.Count > 1 ? fanSpeeds[1].Rpm : 0;
                    step.Completed = true;
                    step.Timestamp = DateTime.Now;
                    
                    // Store in profile
                    profile.Fan0LevelToRpm[step.Level] = step.Fan0Rpm;
                    profile.Fan1LevelToRpm[step.Level] = step.Fan1Rpm;
                    
                    // Track max RPM
                    if (step.Fan0Rpm > profile.Fan0MaxRpm) profile.Fan0MaxRpm = step.Fan0Rpm;
                    if (step.Fan1Rpm > profile.Fan1MaxRpm) profile.Fan1MaxRpm = step.Fan1Rpm;
                    
                    // Detect minimum spin level
                    if (step.Fan0Rpm > 100 && profile.MinSpinLevel == 20)
                    {
                        profile.MinSpinLevel = step.Level;
                    }
                    
                    _currentStep++;
                    _logging.Info($"  Level {step.Level}: CPU Fan = {step.Fan0Rpm} RPM, GPU Fan = {step.Fan1Rpm} RPM");
                    CalibrationStepCompleted?.Invoke(this, step);
                }
                
                profile.CalibratedAt = DateTime.Now;
                
                // Save profile
                _knownProfiles[productId] = profile;
                _activeProfile = profile;
                SaveKnownProfiles();
                
                _logging.Info($"✓ Fan calibration complete for {profile.ModelName}");
                _logging.Info($"  Max Level: {profile.MaxLevel}");
                _logging.Info($"  Min Spin Level: {profile.MinSpinLevel}");
                _logging.Info($"  Fan 0 Max RPM: {profile.Fan0MaxRpm}");
                _logging.Info($"  Fan 1 Max RPM: {profile.Fan1MaxRpm}");
                
                CalibrationCompleted?.Invoke(this, profile);
            }
            catch (OperationCanceledException)
            {
                _logging.Warn("Fan calibration cancelled by user");
                CalibrationError?.Invoke(this, "Calibration cancelled");
            }
            catch (Exception ex)
            {
                _logging.Error($"Fan calibration failed: {ex.Message}", ex);
                CalibrationError?.Invoke(this, ex.Message);
            }
            finally
            {
                _calibrationInProgress = false;
                
                // Restore auto control
                _fanController.RestoreAutoControl();
            }
        }
        
        /// <summary>
        /// Cancel the calibration wizard.
        /// </summary>
        public void CancelCalibration()
        {
            _calibrationInProgress = false;
            _fanController.RestoreAutoControl();
        }
        
        /// <summary>
        /// Apply a fan speed with closed-loop verification.
        /// </summary>
        public async Task<FanApplyResult> ApplyAndVerifyAsync(int fanIndex, int targetPercent, CancellationToken ct = default)
        {
            var result = new FanApplyResult
            {
                FanIndex = fanIndex,
                RequestedPercent = targetPercent
            };
            
            // Get calibration data
            var profile = _activeProfile ?? CreateDefaultProfile(GetCurrentProductId());
            result.AppliedLevel = profile.PercentToLevel(targetPercent);
            result.ExpectedRpm = profile.GetExpectedRpm(fanIndex, targetPercent);
            
            // Read current RPM - fans are indexed by list position
            var fanSpeedsBefore = _fanController.ReadFanSpeeds().ToList();
            result.ActualRpmBefore = fanSpeedsBefore.Count > fanIndex ? fanSpeedsBefore[fanIndex].Rpm : 0;
            
            // Apply the speed
            var startTime = DateTime.Now;
            result.WmiCallSucceeded = _fanController.SetFanSpeed(targetPercent);
            
            if (!result.WmiCallSucceeded)
            {
                _logging.Warn($"Fan {fanIndex} SetFanSpeed({targetPercent}%) failed");
                return result;
            }
            
            // Wait for fan to respond
            await Task.Delay(FanResponseDelayMs, ct);
            result.ResponseTime = DateTime.Now - startTime;
            
            // Read actual RPM after change
            var fanSpeedsAfter = _fanController.ReadFanSpeeds().ToList();
            result.ActualRpmAfter = fanSpeedsAfter.Count > fanIndex ? fanSpeedsAfter[fanIndex].Rpm : 0;
            
            // Verify
            if (!result.VerificationPassed)
            {
                _logging.Warn($"Fan {fanIndex} verification warning: Expected ~{result.ExpectedRpm} RPM, got {result.ActualRpmAfter} RPM ({result.PercentError:F1}% error)");
                
                // Retry once
                await Task.Delay(2000, ct);
                fanSpeedsAfter = _fanController.ReadFanSpeeds().ToList();
                result.ActualRpmAfter = fanSpeedsAfter.Count > fanIndex ? fanSpeedsAfter[fanIndex].Rpm : 0;
                
                if (result.VerificationPassed)
                {
                    _logging.Info($"Fan {fanIndex} verified on retry: {result.ActualRpmAfter} RPM");
                }
                else
                {
                    _logging.Warn($"Fan {fanIndex} control may not be working correctly. Consider running calibration.");
                }
            }
            else
            {
                _logging.Debug($"✓ Fan {fanIndex} verified: {result.ActualRpmAfter} RPM (expected ~{result.ExpectedRpm})");
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if calibration is recommended for the current system.
        /// </summary>
        public bool IsCalibrationRecommended()
        {
            if (_activeProfile == null) return true;
            if (!_activeProfile.IsValid) return true;
            
            // Recommend recalibration if older than 30 days
            if ((DateTime.Now - _activeProfile.CalibratedAt).TotalDays > 30)
            {
                _logging.Info("Fan calibration is older than 30 days, recalibration recommended");
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get a description of the current calibration status.
        /// </summary>
        public string GetCalibrationStatus()
        {
            if (_activeProfile == null)
            {
                return "No calibration profile loaded";
            }
            
            if (!_activeProfile.IsValid)
            {
                return "Calibration profile is incomplete";
            }
            
            var age = DateTime.Now - _activeProfile.CalibratedAt;
            if (age.TotalDays > 30)
            {
                return $"Calibrated {age.TotalDays:F0} days ago (recalibration recommended)";
            }
            
            return $"Calibrated {age.TotalDays:F0} days ago for {_activeProfile.ModelName}";
        }
        
        #region Profile Management
        
        private string GetCurrentProductId()
        {
            var info = _systemInfo.GetSystemInfo();
            return info.SystemSku ?? info.Model ?? "UNKNOWN";
        }
        
        private FanCalibrationProfile CreateDefaultProfile(string productId)
        {
            // Default profile based on common OMEN characteristics
            return new FanCalibrationProfile
            {
                ProductId = productId,
                ModelName = "Default OMEN Profile",
                MaxLevel = 55,
                MinSpinLevel = 20,
                FanCount = 2,
                Fan0MaxRpm = 5000,
                Fan1MaxRpm = 5000,
                // Default linear mapping
                Fan0LevelToRpm = new Dictionary<int, int>
                {
                    { 0, 0 },
                    { 10, 0 },
                    { 20, 2000 },
                    { 30, 2800 },
                    { 40, 3500 },
                    { 50, 4200 },
                    { 55, 5000 }
                },
                Fan1LevelToRpm = new Dictionary<int, int>
                {
                    { 0, 0 },
                    { 10, 0 },
                    { 20, 2000 },
                    { 30, 2800 },
                    { 40, 3500 },
                    { 50, 4200 },
                    { 55, 5000 }
                }
            };
        }
        
        private void LoadKnownProfiles()
        {
            try
            {
                var profilePath = GetProfilePath();
                if (File.Exists(profilePath))
                {
                    var json = File.ReadAllText(profilePath);
                    var profiles = JsonSerializer.Deserialize<List<FanCalibrationProfile>>(json);
                    if (profiles != null)
                    {
                        foreach (var profile in profiles)
                        {
                            _knownProfiles[profile.ProductId] = profile;
                        }
                        _logging.Info($"Loaded {_knownProfiles.Count} known fan calibration profiles");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load fan calibration profiles: {ex.Message}");
            }
        }
        
        private void SaveKnownProfiles()
        {
            try
            {
                var profilePath = GetProfilePath();
                var dir = Path.GetDirectoryName(profilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                var profiles = _knownProfiles.Values.ToList();
                var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(profilePath, json);
                _logging.Info($"Saved {profiles.Count} fan calibration profiles");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to save fan calibration profiles: {ex.Message}", ex);
            }
        }
        
        private string GetProfilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "OmenCore", "fan_calibration_profiles.json");
        }
        
        #endregion
    }
}
