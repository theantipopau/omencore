using OmenCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Unified profile import/export for fan curves, performance modes, and RGB presets.
    /// Allows users to share complete OmenCore configurations.
    /// </summary>
    public class ProfileExportService
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;

        public ProfileExportService(LoggingService logging, ConfigurationService configService)
        {
            _logging = logging;
            _configService = configService;
        }

        /// <summary>
        /// Export complete profile including fan presets, performance modes, and RGB presets
        /// </summary>
        public async Task<bool> ExportProfileAsync(string filePath, AppConfig config)
        {
            try
            {
                var export = new OmenCoreProfile
                {
                    ExportDate = DateTime.Now,
                    Version = "2.8.6",
                    SystemInfo = new ProfileSystemInfo
                    {
                        OsVersion = Environment.OSVersion.ToString(),
                        CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown",
                        MachineName = Environment.MachineName
                    },
                    FanPresets = config.FanPresets?.Where(p => !p.IsBuiltIn).ToList() ?? new List<FanPreset>(),
                    PerformanceModes = config.PerformanceModes?.ToList() ?? new List<PerformanceMode>(),
                    GpuOcProfiles = config.GpuOcProfiles?.ToList() ?? new List<GpuOcProfile>(),
                    Settings = new ProfileSettings
                    {
                        BatteryChargeThreshold = config.Battery?.ChargeThresholdPercent ?? 80,
                        BatteryChargeLimitEnabled = config.Battery?.ChargeLimitEnabled ?? false,
                        FanHysteresisEnabled = config.FanHysteresis?.Enabled ?? true,
                        FanHysteresisDeadZone = config.FanHysteresis?.DeadZone ?? 3.0,
                        FanHysteresisRampUpDelay = config.FanHysteresis?.RampUpDelay ?? 0.5,
                        FanHysteresisRampDownDelay = config.FanHysteresis?.RampDownDelay ?? 2.0
                    }
                };

                var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                await File.WriteAllTextAsync(filePath, json);
                _logging.Info($"📤 Profile exported to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to export profile: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Import profile from file
        /// </summary>
        public async Task<OmenCoreProfile?> ImportProfileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var profile = JsonSerializer.Deserialize<OmenCoreProfile>(json);

                if (profile == null)
                {
                    _logging.Warn("Failed to deserialize profile - null result");
                    return null;
                }

                _logging.Info($"📥 Profile imported from: {filePath} (version {profile.Version}, exported {profile.ExportDate:yyyy-MM-dd})");
                return profile;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to import profile: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Apply imported profile to current configuration
        /// </summary>
        public void ApplyProfile(OmenCoreProfile profile, AppConfig config, bool mergeFanPresets = true, bool mergePerformanceModes = false, bool mergeRgbPresets = true, bool applySettings = false)
        {
            try
            {
                // Fan Presets
                if (mergeFanPresets && profile.FanPresets?.Count > 0)
                {
                    if (config.FanPresets == null) config.FanPresets = new List<FanPreset>();

                    foreach (var preset in profile.FanPresets)
                    {
                        // Check for duplicate names
                        var existing = config.FanPresets.FirstOrDefault(p => p.Name == preset.Name);
                        if (existing != null)
                        {
                            config.FanPresets.Remove(existing);
                            _logging.Info($"Replaced existing fan preset: {preset.Name}");
                        }

                        config.FanPresets.Add(preset);
                        _logging.Info($"Added fan preset: {preset.Name}");
                    }
                }

                // Performance Modes (optional - usually don't want to replace)
                if (mergePerformanceModes && profile.PerformanceModes?.Count > 0)
                {
                    if (config.PerformanceModes == null) config.PerformanceModes = new List<PerformanceMode>();

                    foreach (var mode in profile.PerformanceModes)
                    {
                        var existing = config.PerformanceModes.FirstOrDefault(m => m.Name == mode.Name);
                        if (existing != null)
                        {
                            config.PerformanceModes.Remove(existing);
                        }

                        config.PerformanceModes.Add(mode);
                        _logging.Info($"Added performance mode: {mode.Name}");
                    }
                }

                // Settings (optional)
                if (applySettings && profile.Settings != null)
                {
                    if (config.Battery == null) config.Battery = new BatterySettings();
                    config.Battery.ChargeThresholdPercent = profile.Settings.BatteryChargeThreshold;
                    config.Battery.ChargeLimitEnabled = profile.Settings.BatteryChargeLimitEnabled;

                    if (config.FanHysteresis == null) config.FanHysteresis = new FanHysteresisSettings();
                    config.FanHysteresis.Enabled = profile.Settings.FanHysteresisEnabled;
                    config.FanHysteresis.DeadZone = profile.Settings.FanHysteresisDeadZone;
                    config.FanHysteresis.RampUpDelay = profile.Settings.FanHysteresisRampUpDelay;
                    config.FanHysteresis.RampDownDelay = profile.Settings.FanHysteresisRampDownDelay;

                    _logging.Info("Applied profile settings");
                }

                _logging.Info("✓ Profile applied successfully");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply profile: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Complete OmenCore profile export format
    /// </summary>
    public class OmenCoreProfile
    {
        public DateTime ExportDate { get; set; }
        public string Version { get; set; } = "2.8.6";
        public ProfileSystemInfo? SystemInfo { get; set; }
        public List<FanPreset>? FanPresets { get; set; }
        public List<PerformanceMode>? PerformanceModes { get; set; }
        public List<GpuOcProfile>? GpuOcProfiles { get; set; }
        public ProfileSettings? Settings { get; set; }
    }

    public class ProfileSystemInfo
    {
        public string? OsVersion { get; set; }
        public string? CpuName { get; set; }
        public string? MachineName { get; set; }
    }

    public class ProfileSettings
    {
        public int BatteryChargeThreshold { get; set; }
        public bool BatteryChargeLimitEnabled { get; set; }
        public bool FanHysteresisEnabled { get; set; }
        public double FanHysteresisDeadZone { get; set; }
        public double FanHysteresisRampUpDelay { get; set; }
        public double FanHysteresisRampDownDelay { get; set; }
    }
}
