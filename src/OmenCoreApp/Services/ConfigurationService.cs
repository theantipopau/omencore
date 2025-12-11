using System;
using System.IO;
using System.Text.Json;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class ConfigurationService
    {
        private readonly string _configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenCore");
        private readonly string _configPath;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public AppConfig Config { get; private set; }

        public ConfigurationService()
        {
            Directory.CreateDirectory(_configDirectory);
            _configPath = Path.Combine(_configDirectory, "config.json");
            Config = Load();
        }

        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                var defaults = DefaultConfiguration.Create();
                Save(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                
                if (config == null)
                {
                    App.Logging.Warn("Config file is invalid, using defaults");
                    return DefaultConfiguration.Create();
                }

                // Validate and apply defaults for missing properties
                config = ValidateAndRepair(config);
                return config;
            }
            catch (Exception ex)
            {
                App.Logging.Error($"Failed to load config, using defaults: {ex.Message}");
                return DefaultConfiguration.Create();
            }
        }

        private AppConfig ValidateAndRepair(AppConfig config)
        {
            // Ensure essential collections are initialized
            config.FanPresets ??= new();
            config.PerformanceModes ??= new();
            config.SystemToggles ??= new();
            config.LightingProfiles ??= new();
            config.CorsairLightingPresets ??= new();
            config.DefaultCorsairDpi ??= new();
            config.MacroProfiles ??= new();
            config.EcFanRegisterMap ??= new();
            config.Undervolt ??= new();
            config.Monitoring ??= new();
            config.Updates ??= new();

            // Validate monitoring interval
            if (config.MonitoringIntervalMs < 500 || config.MonitoringIntervalMs > 10000)
            {
                App.Logging.Warn($"Invalid MonitoringIntervalMs ({config.MonitoringIntervalMs}), resetting to 1000");
                config.MonitoringIntervalMs = 1000;
            }

            // Validate EC device path
            if (string.IsNullOrWhiteSpace(config.EcDevicePath))
            {
                config.EcDevicePath = @"\\.\WinRing0_1_2";
            }

            return config;
        }

        public void Save(AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }

        public string GetConfigFolder() => _configDirectory;

        /// <summary>
        /// Exports the current configuration to a specified file path.
        /// </summary>
        public void ExportConfiguration(string filePath, AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Imports configuration from a specified file path.
        /// Returns the loaded configuration or null if the file is invalid.
        /// </summary>
        public AppConfig? ImportConfiguration(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Configuration file not found", filePath);
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
        }

        /// <summary>
        /// Validates that a configuration file is well-formed.
        /// </summary>
        public bool ValidateConfiguration(string filePath)
        {
            try
            {
                var config = ImportConfiguration(filePath);
                return config != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
