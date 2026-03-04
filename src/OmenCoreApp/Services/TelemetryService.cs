using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OmenCore.Services;

namespace OmenCore.Services
{
    /// <summary>
    /// Lightweight telemetry collector for anonymous, aggregated PID success/failure counts.
    /// Respects ConfigurationService.Config.TelemetryEnabled and writes a small JSON blob to config folder.
    /// </summary>
    public class TelemetryService : ITelemetryService
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly string _path;
        private readonly object _lock = new();
        private TelemetryData _data = new();

        public TelemetryService(LoggingService logging, ConfigurationService configService)
        {
            _logging = logging;
            _configService = configService;
            _path = Path.Combine(configService.GetConfigFolder(), "telemetry.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var data = JsonSerializer.Deserialize<TelemetryData>(json);
                    if (data != null) _data = data;
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load telemetry file: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save telemetry file: {ex.Message}");
            }
        }

        /// <summary>
        /// Export the current telemetry file (copy) to a timestamped location and return the path.
        /// Returns null if no telemetry data exists or export fails.
        /// </summary>
        public virtual string? ExportTelemetry()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;

                var dir = Path.GetDirectoryName(_path) ?? Path.GetTempPath();
                var exportName = $"telemetry_export_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var exportPath = Path.Combine(dir, exportName);
                File.Copy(_path, exportPath, overwrite: true);
                _logging.Info($"Telemetry exported to: {exportPath}");
                return exportPath;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to export telemetry: {ex.Message}");
                return null;
            }
        }

        public void IncrementPidSuccess(int pid)
        {
            if (!_configService.Config.TelemetryEnabled) return;
            lock (_lock)
            {
                if (!_data.PidStats.TryGetValue(pid.ToString(), out var stat))
                {
                    stat = new PidStat();
                    _data.PidStats[pid.ToString()] = stat;
                }
                stat.Success++;
                stat.LastSeen = DateTime.UtcNow;
                Save();
            }
        }

        public void IncrementPidFailure(int pid)
        {
            if (!_configService.Config.TelemetryEnabled) return;
            lock (_lock)
            {
                if (!_data.PidStats.TryGetValue(pid.ToString(), out var stat))
                {
                    stat = new PidStat();
                    _data.PidStats[pid.ToString()] = stat;
                }
                stat.Failure++;
                stat.LastSeen = DateTime.UtcNow;
                Save();
            }
        }

        public IReadOnlyDictionary<string, PidStat> GetStats()
        {
            lock (_lock)
            {
                return new Dictionary<string, PidStat>(_data.PidStats);
            }
        }

        private class TelemetryData
        {
            public Dictionary<string, PidStat> PidStats { get; set; } = new Dictionary<string, PidStat>();
            public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        }

        public class PidStat
        {
            public long Success { get; set; }
            public long Failure { get; set; }
            public DateTime LastSeen { get; set; }
        }
    }
}
