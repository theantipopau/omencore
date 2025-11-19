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

        public ConfigurationService()
        {
            Directory.CreateDirectory(_configDirectory);
            _configPath = Path.Combine(_configDirectory, "config.json");
        }

        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                var defaults = DefaultConfiguration.Create();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            return config ?? DefaultConfiguration.Create();
        }

        public void Save(AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }

        public string GetConfigFolder() => _configDirectory;
    }
}
