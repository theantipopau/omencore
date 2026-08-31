using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for managing RGB lighting configuration settings.
    /// Provides persistent storage for lighting preferences, thresholds, and effect settings.
    /// </summary>
    public class RgbLightingSettingsService
    {
        private readonly LoggingService _logging;
        private readonly string _settingsPath;
        private RgbLightingSettings _settings;

        public RgbLightingSettingsService(LoggingService logging)
        {
            _logging = logging;
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OmenCore",
                "rgb-lighting-settings.json"
            );

            _settings = LoadSettings();
        }

        /// <summary>
        /// Get the current lighting settings.
        /// </summary>
        public RgbLightingSettings GetSettings()
        {
            return _settings;
        }

        /// <summary>
        /// Update and save lighting settings.
        /// </summary>
        public async Task UpdateSettingsAsync(RgbLightingSettings newSettings)
        {
            _settings = newSettings;
            await SaveSettingsAsync();
            _logging.Info("RGB lighting settings updated and saved");
        }

        /// <summary>
        /// Reset settings to defaults.
        /// </summary>
        public async Task ResetToDefaultsAsync()
        {
            _settings = CreateDefaultSettings();
            await SaveSettingsAsync();
            _logging.Info("RGB lighting settings reset to defaults");
        }

        /// <summary>
        /// Export settings to a file.
        /// </summary>
        public async Task ExportSettingsAsync(string filePath)
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json);
            _logging.Info($"RGB lighting settings exported to {filePath}");
        }

        /// <summary>
        /// Import settings from a file.
        /// </summary>
        public async Task ImportSettingsAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var importedSettings = JsonSerializer.Deserialize<RgbLightingSettings>(json);

                if (importedSettings != null)
                {
                    await UpdateSettingsAsync(importedSettings);
                    _logging.Info($"RGB lighting settings imported from {filePath}");
                }
                else
                {
                    throw new InvalidOperationException("Invalid settings file format");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to import RGB lighting settings: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Get temperature color for a given temperature.
        /// </summary>
        public string GetTemperatureColor(double temperatureC)
        {
            if (temperatureC <= _settings.CpuTempThresholdLow)
                return _settings.CpuTempColorLow;
            else if (temperatureC <= _settings.CpuTempThresholdMedium)
                return _settings.CpuTempColorMedium;
            else
                return _settings.CpuTempColorHigh;
        }

        /// <summary>
        /// Get performance mode color.
        /// </summary>
        public string GetPerformanceModeColor(RgbPerformanceMode mode)
        {
            return mode switch
            {
                RgbPerformanceMode.Performance => _settings.PerformanceModeColorPerformance,
                RgbPerformanceMode.Balanced => _settings.PerformanceModeColorBalanced,
                RgbPerformanceMode.Quiet => _settings.PerformanceModeColorQuiet,
                _ => _settings.PerformanceModeColorBalanced
            };
        }

        /// <summary>
        /// Get throttling warning color.
        /// </summary>
        public string GetThrottlingColor()
        {
            return _settings.ThrottlingWarningColor;
        }

        /// <summary>
        /// Get preset effect settings.
        /// </summary>
        public LightingPresetSettings GetPresetSettings(string presetName)
        {
            return _settings.PresetSettings.TryGetValue(presetName, out var settings)
                ? settings
                : new LightingPresetSettings { Name = presetName };
        }

        private RgbLightingSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var loadedSettings = JsonSerializer.Deserialize<RgbLightingSettings>(json);

                    if (loadedSettings != null)
                    {
                        // Validate and upgrade settings if needed
                        ValidateAndUpgradeSettings(loadedSettings);
                        _logging.Info($"Loaded RGB lighting settings from {_settingsPath}");
                        return loadedSettings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load RGB lighting settings: {ex.Message}. Using defaults.");
            }

            // Return defaults if loading failed
            return CreateDefaultSettings();
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_settingsPath, json);
                _logging.Info($"Saved RGB lighting settings to {_settingsPath}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to save RGB lighting settings: {ex.Message}", ex);
            }
        }

        private void ValidateAndUpgradeSettings(RgbLightingSettings settings)
        {
            // Ensure all required properties have values
            if (string.IsNullOrEmpty(settings.CpuTempColorLow))
                settings.CpuTempColorLow = "#0000FF"; // Blue

            if (string.IsNullOrEmpty(settings.CpuTempColorMedium))
                settings.CpuTempColorMedium = "#FFFF00"; // Yellow

            if (string.IsNullOrEmpty(settings.CpuTempColorHigh))
                settings.CpuTempColorHigh = "#FF0000"; // Red

            // Ensure thresholds are reasonable
            if (settings.CpuTempThresholdLow < 0)
                settings.CpuTempThresholdLow = 40;

            if (settings.CpuTempThresholdMedium <= settings.CpuTempThresholdLow)
                settings.CpuTempThresholdMedium = 70;

            if (settings.CpuTempThresholdHigh <= settings.CpuTempThresholdMedium)
                settings.CpuTempThresholdHigh = 85;

            // Ensure GPU thresholds are set
            if (settings.GpuTempThresholdLow < 0)
                settings.GpuTempThresholdLow = settings.CpuTempThresholdLow;

            if (settings.GpuTempThresholdMedium <= settings.GpuTempThresholdLow)
                settings.GpuTempThresholdMedium = settings.CpuTempThresholdMedium;

            if (settings.GpuTempThresholdHigh <= settings.GpuTempThresholdMedium)
                settings.GpuTempThresholdHigh = settings.CpuTempThresholdHigh;
        }

        private RgbLightingSettings CreateDefaultSettings()
        {
            return new RgbLightingSettings
            {
                // Temperature thresholds and colors
                CpuTempThresholdLow = 40,
                CpuTempThresholdMedium = 70,
                CpuTempThresholdHigh = 85,
                CpuTempColorLow = "#0000FF",      // Blue
                CpuTempColorMedium = "#FFFF00",   // Yellow
                CpuTempColorHigh = "#FF0000",     // Red

                GpuTempThresholdLow = 40,
                GpuTempThresholdMedium = 70,
                GpuTempThresholdHigh = 85,
                GpuTempColorLow = "#0000FF",      // Blue
                GpuTempColorMedium = "#FFFF00",   // Yellow
                GpuTempColorHigh = "#FF0000",     // Red

                // Performance mode colors
                PerformanceModeColorPerformance = "#FF4444",  // Red
                PerformanceModeColorBalanced = "#44FF44",     // Green
                PerformanceModeColorQuiet = "#4444FF",        // Blue

                // Throttling
                ThrottlingWarningColor = "#FF00FF",  // Magenta
                ThrottlingEnabled = true,

                // Device settings
                KeyboardLightingEnabled = true,
                CorsairLightingEnabled = true,
                LogitechLightingEnabled = true,
                RazerLightingEnabled = true,

                // Brightness settings
                KeyboardBrightness = 100,
                CorsairBrightness = 100,
                LogitechBrightness = 100,

                // Preset settings
                PresetSettings = new Dictionary<string, LightingPresetSettings>
                {
                    ["Wave Blue"] = new LightingPresetSettings { Name = "Wave Blue", Speed = 50, Direction = "right" },
                    ["Wave Red"] = new LightingPresetSettings { Name = "Wave Red", Speed = 50, Direction = "left" },
                    ["Breathing Green"] = new LightingPresetSettings { Name = "Breathing Green", Speed = 30, Color = "#00FF00" },
                    ["Reactive Purple"] = new LightingPresetSettings { Name = "Reactive Purple", Speed = 100, Color = "#800080" },
                    ["Spectrum Flow"] = new LightingPresetSettings { Name = "Spectrum Flow", Speed = 75 },
                    ["Audio Reactive"] = new LightingPresetSettings { Name = "Audio Reactive", Sensitivity = 50 }
                },

                // Advanced settings
                TemperatureSmoothingEnabled = true,
                TemperatureSmoothingFactor = 0.3,
                ColorTransitionDurationMs = 500,
                AutoSaveEnabled = true,

                // Version info
                SettingsVersion = "1.0",
                LastModified = DateTime.Now
            };
        }
    }

    /// <summary>
    /// RGB lighting configuration settings.
    /// </summary>
    public class RgbLightingSettings
    {
        // Temperature-based lighting (OmenMon-style)
        public bool TemperatureBasedLightingEnabled { get; set; }
        
        public double CpuTempThresholdLow { get; set; }
        public double CpuTempThresholdMedium { get; set; }
        public double CpuTempThresholdHigh { get; set; }
        public string CpuTempColorLow { get; set; } = "";
        public string CpuTempColorMedium { get; set; } = "";
        public string CpuTempColorHigh { get; set; } = "";

        public double GpuTempThresholdLow { get; set; }
        public double GpuTempThresholdMedium { get; set; }
        public double GpuTempThresholdHigh { get; set; }
        public string GpuTempColorLow { get; set; } = "";
        public string GpuTempColorMedium { get; set; } = "";
        public string GpuTempColorHigh { get; set; } = "";

        // Performance mode colors
        public string PerformanceModeColorPerformance { get; set; } = "";
        public string PerformanceModeColorBalanced { get; set; } = "";
        public string PerformanceModeColorQuiet { get; set; } = "";

        // Throttling settings
        public string ThrottlingWarningColor { get; set; } = "";
        public bool ThrottlingEnabled { get; set; }

        // Device enablement
        public bool KeyboardLightingEnabled { get; set; }
        public bool CorsairLightingEnabled { get; set; }
        public bool LogitechLightingEnabled { get; set; }
        public bool RazerLightingEnabled { get; set; }

        // Brightness settings
        public int KeyboardBrightness { get; set; }
        public int CorsairBrightness { get; set; }
        public int LogitechBrightness { get; set; }

        // Preset configurations
        public Dictionary<string, LightingPresetSettings> PresetSettings { get; set; } = new();

        // Advanced settings
        public bool TemperatureSmoothingEnabled { get; set; }
        public double TemperatureSmoothingFactor { get; set; }
        public int ColorTransitionDurationMs { get; set; }
        public bool AutoSaveEnabled { get; set; }

        // Metadata
        public string SettingsVersion { get; set; } = "";
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// Settings for a specific lighting preset.
    /// </summary>
    public class LightingPresetSettings
    {
        public string Name { get; set; } = "";
        public string? Color { get; set; }
        public int Speed { get; set; } = 50;
        public string? Direction { get; set; }
        public int Sensitivity { get; set; } = 50;
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Performance mode enumeration.
    /// </summary>
    public enum RgbPerformanceMode
    {
        Performance,
        Balanced,
        Quiet
    }
}