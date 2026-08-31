using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services
{
    /// <summary>
    /// Stores and retrieves fan calibration data for specific laptop models.
    /// Enables accurate RPM→% mapping by learning from actual hardware behavior.
    /// </summary>
    public class FanCalibrationStorageService
    {
        private readonly LoggingService _logging;
        private readonly string _calibrationDataPath;
        private Dictionary<string, FanModelCalibration> _calibrations = new();

        public FanCalibrationStorageService(LoggingService logging)
        {
            _logging = logging;
            _calibrationDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OmenCore",
                "fan-calibration.json"
            );

            LoadCalibrations();
        }

        /// <summary>
        /// Normalize model text into a stable key used for fan calibration storage lookups.
        /// </summary>
        public static string NormalizeModelId(string modelInfo)
        {
            return (modelInfo ?? string.Empty)
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);
        }

        /// <summary>
        /// Get calibration data for a specific model.
        /// </summary>
        public FanModelCalibration? GetCalibration(string modelId)
        {
            return _calibrations.TryGetValue(modelId, out var calibration) ? calibration : null;
        }

        /// <summary>
        /// Store calibration data for a model.
        /// </summary>
        public async Task StoreCalibrationAsync(string modelId, FanModelCalibration calibration)
        {
            _calibrations[modelId] = calibration;
            await SaveCalibrationsAsync();
            _logging.Info($"Stored fan calibration for model {modelId}: {calibration.FanCalibrations.Count} fans");
        }

        /// <summary>
        /// Get expected RPM for a given percentage using stored calibration data.
        /// Falls back to default estimation if no calibration exists.
        /// </summary>
        public int GetCalibratedRpm(string modelId, int fanIndex, int targetPercent)
        {
            var calibration = GetCalibration(modelId);
            if (calibration?.FanCalibrations != null && calibration.FanCalibrations.Count > fanIndex)
            {
                var fanCal = calibration.FanCalibrations[fanIndex];
                return fanCal.GetExpectedRpm(targetPercent);
            }

            // Fallback to default estimation
            return PercentToExpectedRpmDefault(targetPercent);
        }

        /// <summary>
        /// Get expected percentage for a given RPM using stored calibration data.
        /// </summary>
        public int GetCalibratedPercent(string modelId, int fanIndex, int actualRpm)
        {
            var calibration = GetCalibration(modelId);
            if (calibration?.FanCalibrations != null && calibration.FanCalibrations.Count > fanIndex)
            {
                var fanCal = calibration.FanCalibrations[fanIndex];
                return fanCal.GetExpectedPercent(actualRpm);
            }

            // Fallback to default estimation
            return RpmToPercentDefault(actualRpm);
        }

        /// <summary>
        /// Check if calibration exists for a model.
        /// </summary>
        public bool HasCalibration(string modelId)
        {
            return _calibrations.ContainsKey(modelId);
        }

        /// <summary>
        /// Get all stored model IDs.
        /// </summary>
        public IEnumerable<string> GetCalibratedModels()
        {
            return _calibrations.Keys;
        }

        /// <summary>
        /// Remove calibration data for a model.
        /// </summary>
        public async Task RemoveCalibrationAsync(string modelId)
        {
            if (_calibrations.Remove(modelId))
            {
                await SaveCalibrationsAsync();
                _logging.Info($"Removed fan calibration for model {modelId}");
            }
        }

        /// <summary>
        /// Create calibration from calibration results.
        /// </summary>
        public FanModelCalibration CreateCalibrationFromResults(
            string modelId,
            List<FanCalibrationResult> results,
            string modelName = "")
        {
            var calibration = new FanModelCalibration
            {
                ModelId = modelId,
                ModelName = modelName,
                CreatedDate = DateTime.Now,
                FanCalibrations = new List<FanCalibrationData>()
            };

            // Group results by fan index
            var fanGroups = results.GroupBy(r => r.FanIndex);

            foreach (var fanGroup in fanGroups)
            {
                var fanCal = new FanCalibrationData
                {
                    FanIndex = fanGroup.Key,
                    FanName = fanGroup.First().FanName,
                    CalibrationPoints = new List<FanCalibrationDataPoint>()
                };

                // Convert calibration points to data points
                foreach (var result in fanGroup)
                {
                    foreach (var point in result.CalibrationPoints.Where(p => p.VerificationPassed))
                    {
                        fanCal.CalibrationPoints.Add(new FanCalibrationDataPoint
                        {
                            Percent = point.RequestedPercent,
                            MeasuredRpm = point.MeasuredRpm,
                            AppliedLevel = point.AppliedLevel
                        });
                    }
                }

                // Sort by percentage for better interpolation
                fanCal.CalibrationPoints = fanCal.CalibrationPoints
                    .OrderBy(p => p.Percent)
                    .ToList();

                calibration.FanCalibrations.Add(fanCal);
            }

            return calibration;
        }

        private void LoadCalibrations()
        {
            try
            {
                if (File.Exists(_calibrationDataPath))
                {
                    var json = File.ReadAllText(_calibrationDataPath);
                    _calibrations = JsonSerializer.Deserialize<Dictionary<string, FanModelCalibration>>(json)
                                   ?? new Dictionary<string, FanModelCalibration>();

                    _logging.Info($"Loaded {_calibrations.Count} fan calibrations from {_calibrationDataPath}");
                }
                else
                {
                    _calibrations = new Dictionary<string, FanModelCalibration>();
                    _logging.Info("No existing fan calibration data found");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to load fan calibrations: {ex.Message}", ex);
                _calibrations = new Dictionary<string, FanModelCalibration>();
            }
        }

        private async Task SaveCalibrationsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_calibrationDataPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_calibrations, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_calibrationDataPath, json);
                _logging.Info($"Saved {_calibrations.Count} fan calibrations to {_calibrationDataPath}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to save fan calibrations: {ex.Message}", ex);
            }
        }

        // Default estimation methods (fallback when no calibration exists)
        internal static int PercentToExpectedRpmDefault(int percent)
        {
            // Simple linear estimation: 0% = 0 RPM, 100% = 5500 RPM
            return (int)(percent / 100.0 * 5500);
        }

        internal static int RpmToPercentDefault(int rpm)
        {
            // Reverse of above
            return Math.Min(100, Math.Max(0, (int)(rpm / 5500.0 * 100)));
        }
    }

    /// <summary>
    /// Calibration data for a specific laptop model.
    /// </summary>
    public class FanModelCalibration
    {
        public string ModelId { get; set; } = "";
        public string ModelName { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public List<FanCalibrationData> FanCalibrations { get; set; } = new();
    }

    /// <summary>
    /// Calibration data for a single fan.
    /// </summary>
    public class FanCalibrationData
    {
        public int FanIndex { get; set; }
        public string FanName { get; set; } = "";
        public List<FanCalibrationDataPoint> CalibrationPoints { get; set; } = new();

        /// <summary>
        /// Get expected RPM for a target percentage using interpolation.
        /// </summary>
        public int GetExpectedRpm(int targetPercent)
        {
            if (CalibrationPoints.Count == 0)
                return FanCalibrationStorageService.PercentToExpectedRpmDefault(targetPercent);

            // Find exact match first
            var exactMatch = CalibrationPoints.FirstOrDefault(p => p.Percent == targetPercent);
            if (exactMatch != null)
                return exactMatch.MeasuredRpm;

            // Interpolate between points
            var lowerPoint = CalibrationPoints.LastOrDefault(p => p.Percent <= targetPercent);
            var upperPoint = CalibrationPoints.FirstOrDefault(p => p.Percent >= targetPercent);

            if (lowerPoint == null && upperPoint == null)
                return FanCalibrationStorageService.PercentToExpectedRpmDefault(targetPercent);

            if (lowerPoint == null)
                return upperPoint!.MeasuredRpm;

            if (upperPoint == null)
                return lowerPoint.MeasuredRpm;

            // Linear interpolation
            var percentDiff = upperPoint.Percent - lowerPoint.Percent;
            if (percentDiff == 0)
                return lowerPoint.MeasuredRpm;

            var rpmDiff = upperPoint.MeasuredRpm - lowerPoint.MeasuredRpm;
            var percentRatio = (targetPercent - lowerPoint.Percent) / (double)percentDiff;

            return lowerPoint.MeasuredRpm + (int)(rpmDiff * percentRatio);
        }

        /// <summary>
        /// Get expected percentage for a measured RPM using reverse interpolation.
        /// </summary>
        public int GetExpectedPercent(int measuredRpm)
        {
            if (CalibrationPoints.Count == 0)
                return FanCalibrationStorageService.RpmToPercentDefault(measuredRpm);

            // Find exact match first
            var exactMatch = CalibrationPoints.FirstOrDefault(p => p.MeasuredRpm == measuredRpm);
            if (exactMatch != null)
                return exactMatch.Percent;

            // Interpolate between points
            var lowerPoint = CalibrationPoints.LastOrDefault(p => p.MeasuredRpm <= measuredRpm);
            var upperPoint = CalibrationPoints.FirstOrDefault(p => p.MeasuredRpm >= measuredRpm);

            if (lowerPoint == null && upperPoint == null)
                return FanCalibrationStorageService.RpmToPercentDefault(measuredRpm);

            if (lowerPoint == null)
                return upperPoint!.Percent;

            if (upperPoint == null)
                return lowerPoint.Percent;

            // Linear interpolation
            var rpmDiff = upperPoint.MeasuredRpm - lowerPoint.MeasuredRpm;
            if (rpmDiff == 0)
                return lowerPoint.Percent;

            var percentDiff = upperPoint.Percent - lowerPoint.Percent;
            var rpmRatio = (measuredRpm - lowerPoint.MeasuredRpm) / (double)rpmDiff;

            return Math.Min(100, Math.Max(0, lowerPoint.Percent + (int)(percentDiff * rpmRatio)));
        }
    }

    /// <summary>
    /// Single data point in fan calibration.
    /// </summary>
    public class FanCalibrationDataPoint
    {
        public int Percent { get; set; }
        public int MeasuredRpm { get; set; }
        public int AppliedLevel { get; set; }
    }
}