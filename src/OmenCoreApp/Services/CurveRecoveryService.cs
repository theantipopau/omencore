using OmenCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Auto-revert fan curves if they cause sustained overheating.
    /// Maintains last-known-good curve and thermal safety history.
    /// </summary>
    public class CurveRecoveryService
    {
        private readonly LoggingService _logging;
        private readonly FanService _fanService;
        private readonly ConfigurationService _configService;

        private FanPreset? _lastKnownGoodPreset;
        private DateTime _curveAppliedTime;
        private readonly List<ThermalEvent> _thermalHistory = new();

        private Timer? _monitorTimer;
        private bool _isMonitoring;

        private const int MonitorIntervalMs = 10000; // Check every 10 seconds
        private const int OverheatThresholdC = 90; // Consider 90°C+ as overheating
        private const int SustainedOverheatSeconds = 120; // Revert after 2 minutes of sustained overheat

        public CurveRecoveryService(LoggingService logging, FanService fanService, ConfigurationService configService)
        {
            _logging = logging;
            _fanService = fanService;
            _configService = configService;
        }

        /// <summary>
        /// Record current preset as last known good
        /// </summary>
        public void RecordKnownGoodPreset(FanPreset preset)
        {
            _lastKnownGoodPreset = new FanPreset
            {
                Name = preset.Name + " (Backup)",
                IsBuiltIn = false,
                Curve = preset.Curve.Select(p => new FanCurvePoint { TemperatureC = p.TemperatureC, FanPercent = p.FanPercent }).ToList()
            };

            _curveAppliedTime = DateTime.Now;
            _thermalHistory.Clear();
            _logging.Info($"✓ Recorded last-known-good preset: {preset.Name}");
        }

        /// <summary>
        /// Start thermal monitoring for current curve
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _monitorTimer = new Timer(CheckThermalHealth, null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(MonitorIntervalMs));
            _logging.Debug("Curve recovery monitoring started");
        }

        /// <summary>
        /// Stop thermal monitoring
        /// </summary>
        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitorTimer?.Dispose();
            _monitorTimer = null;
            _logging.Debug("Curve recovery monitoring stopped");
        }

        /// <summary>
        /// Record temperature reading
        /// </summary>
        public void RecordTemperature(double cpuTemp, double gpuTemp)
        {
            if (!_isMonitoring) return;

            var maxTemp = Math.Max(cpuTemp, gpuTemp);

            if (maxTemp >= OverheatThresholdC)
            {
                _thermalHistory.Add(new ThermalEvent
                {
                    Timestamp = DateTime.Now,
                    CpuTemp = cpuTemp,
                    GpuTemp = gpuTemp,
                    IsOverheating = true
                });

                // Keep only last 5 minutes of history
                var cutoff = DateTime.Now.AddMinutes(-5);
                _thermalHistory.RemoveAll(e => e.Timestamp < cutoff);
            }
            else
            {
                // Clear history if temps are normal
                if (_thermalHistory.Count > 0 && maxTemp < OverheatThresholdC - 5)
                {
                    _thermalHistory.Clear();
                }
            }
        }

        private void CheckThermalHealth(object? state)
        {
            try
            {
                if (!_isMonitoring || _lastKnownGoodPreset == null) return;

                // Check for sustained overheating
                var recentOverheats = _thermalHistory.Where(e => e.Timestamp > DateTime.Now.AddSeconds(-SustainedOverheatSeconds)).ToList();

                if (recentOverheats.Count >= (SustainedOverheatSeconds / (MonitorIntervalMs / 1000)) * 0.8) // 80% of checks were overheating
                {
                    _logging.Error($"🚨 CURVE RECOVERY: Sustained overheating detected - reverting to last known good preset");

                    // Auto-revert to last known good curve
                    Task.Run(() =>
                    {
                        try
                        {
                            StopMonitoring(); // Stop to prevent recursion

                            _fanService.ApplyPreset(_lastKnownGoodPreset);
                            _logging.Warn($"✓ Reverted to last known good preset: {_lastKnownGoodPreset.Name}");

                            // Notify user
                            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    $"Your fan curve caused sustained overheating!\n\n" +
                                    $"Temperatures exceeded {OverheatThresholdC}°C for over {SustainedOverheatSeconds / 60} minutes.\n\n" +
                                    $"OmenCore has automatically reverted to your previous fan curve:\n" +
                                    $"\"{_lastKnownGoodPreset.Name}\"\n\n" +
                                    $"Please review your fan curve and ensure adequate cooling at high temperatures.",
                                    "Curve Recovery",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning);
                            });

                            // Save recovery event to config
                            SaveRecoveryEvent();
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"Curve recovery failed: {ex.Message}", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Thermal health check error: {ex.Message}", ex);
            }
        }

        private void SaveRecoveryEvent()
        {
            try
            {
                var recoveryDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OmenCore",
                    "recovery");

                Directory.CreateDirectory(recoveryDir);

                var recoveryEvent = new
                {
                    Timestamp = DateTime.Now,
                    Reason = "Sustained overheating",
                    ThermalHistory = _thermalHistory.Select(e => new
                    {
                        Time = e.Timestamp,
                        CpuTemp = e.CpuTemp,
                        GpuTemp = e.GpuTemp
                    }).ToList(),
                    RecoveredPreset = _lastKnownGoodPreset?.Name ?? "Unknown"
                };

                var json = JsonSerializer.Serialize(recoveryEvent, new JsonSerializerOptions { WriteIndented = true });
                var fileName = $"recovery_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                File.WriteAllText(Path.Combine(recoveryDir, fileName), json);

                _logging.Info($"Recovery event saved: {fileName}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save recovery event: {ex.Message}");
            }
        }
    }

    internal class ThermalEvent
    {
        public DateTime Timestamp { get; set; }
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
        public bool IsOverheating { get; set; }
    }
}
