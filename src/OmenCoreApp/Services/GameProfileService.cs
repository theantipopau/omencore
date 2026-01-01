using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Manages game profiles and automatic profile switching.
    /// </summary>
    public class GameProfileService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly ProcessMonitoringService _processMonitor;
        private readonly string _profilesPath;
        private readonly ObservableCollection<GameProfile> _profiles = new();
        private GameProfile? _activeProfile;
        private DateTime _activeProfileStartTime;

        public ReadOnlyObservableCollection<GameProfile> Profiles { get; }
        public GameProfile? ActiveProfile
        {
            get => _activeProfile;
            private set
            {
                if (_activeProfile != value)
                {
                    _activeProfile = value;
                    ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Fired when the active profile changes (game switch or manual override).
        /// </summary>
        public event EventHandler? ActiveProfileChanged;

        /// <summary>
        /// Fired when a profile should be applied (for UI to trigger settings).
        /// </summary>
        public event EventHandler<ProfileApplyEventArgs>? ProfileApplyRequested;

        public GameProfileService(
            LoggingService logging,
            ProcessMonitoringService processMonitor,
            ConfigurationService config)
        {
            _logging = logging;
            _processMonitor = processMonitor;
            _ = config;

            // Store profiles in AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var omenCorePath = Path.Combine(appDataPath, "OmenCore");
            _profilesPath = Path.Combine(omenCorePath, "game_profiles.json");

            Profiles = new ReadOnlyObservableCollection<GameProfile>(_profiles);

            // Hook into process monitoring
            _processMonitor.ProcessDetected += OnProcessDetected;
            _processMonitor.ProcessExited += OnProcessExited;
        }

        /// <summary>
        /// Initialize service and load existing profiles.
        /// </summary>
        public async Task InitializeAsync()
        {
            await LoadProfilesAsync();
            _logging.Info($"Game profile service initialized with {_profiles.Count} profile(s)");

            // Start monitoring tracked games
            UpdateTrackedProcesses();
            _processMonitor.StartMonitoring();
        }

        /// <summary>
        /// Load profiles from disk.
        /// </summary>
        public async Task LoadProfilesAsync()
        {
            try
            {
                if (!File.Exists(_profilesPath))
                {
                    _logging.Info("No existing game profiles found");
                    return;
                }

                var json = await File.ReadAllTextAsync(_profilesPath);
                var loaded = JsonSerializer.Deserialize<List<GameProfile>>(json);

                if (loaded != null)
                {
                    _profiles.Clear();
                    foreach (var profile in loaded)
                    {
                        _profiles.Add(profile);
                    }
                    _logging.Info($"Loaded {_profiles.Count} game profile(s)");
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to load game profiles", ex);
            }
        }

        /// <summary>
        /// Save profiles to disk.
        /// </summary>
        public async Task SaveProfilesAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_profilesPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_profiles.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_profilesPath, json);
                _logging.Info($"Saved {_profiles.Count} game profile(s)");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to save game profiles", ex);
            }
        }

        /// <summary>
        /// Create a new profile.
        /// </summary>
        public GameProfile CreateProfile(string name, string executableName)
        {
            var profile = new GameProfile
            {
                Name = name,
                ExecutableName = executableName
            };

            _profiles.Add(profile);
            UpdateTrackedProcesses();
            _logging.Info($"Created new game profile: {name}");

            return profile;
        }

        /// <summary>
        /// Update an existing profile.
        /// </summary>
        public async Task UpdateProfileAsync(GameProfile profile)
        {
            profile.ModifiedAt = DateTime.Now;
            await SaveProfilesAsync();
            UpdateTrackedProcesses();
            _logging.Info($"Updated profile: {profile.Name}");
        }

        /// <summary>
        /// Delete a profile.
        /// </summary>
        public async Task DeleteProfileAsync(GameProfile profile)
        {
            _profiles.Remove(profile);
            await SaveProfilesAsync();
            UpdateTrackedProcesses();
            _logging.Info($"Deleted profile: {profile.Name}");
        }

        /// <summary>
        /// Duplicate an existing profile.
        /// </summary>
        public GameProfile DuplicateProfile(GameProfile source)
        {
            var clone = source.Clone();
            _profiles.Add(clone);
            _logging.Info($"Duplicated profile: {source.Name} → {clone.Name}");
            return clone;
        }

        /// <summary>
        /// Manually activate a profile (override game detection).
        /// </summary>
        public void ActivateProfile(GameProfile profile)
        {
            if (ActiveProfile != null)
            {
                // Stop timing previous profile
                RecordPlaytime(ActiveProfile);
            }

            ActiveProfile = profile;
            _activeProfileStartTime = DateTime.Now;
            profile.LaunchCount++;
            _logging.Info($"Manually activated profile: {profile.Name}");

            // Trigger settings application
            ProfileApplyRequested?.Invoke(this, new ProfileApplyEventArgs(profile, ProfileTrigger.Manual));
        }

        /// <summary>
        /// Deactivate current profile (restore defaults).
        /// </summary>
        public void DeactivateProfile()
        {
            if (ActiveProfile != null)
            {
                RecordPlaytime(ActiveProfile);
                _logging.Info($"Deactivated profile: {ActiveProfile.Name}");
                ActiveProfile = null;
            }
        }

        /// <summary>
        /// Import profiles from a JSON file.
        /// </summary>
        public async Task<int> ImportProfilesAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var imported = JsonSerializer.Deserialize<List<GameProfile>>(json);

                if (imported == null || imported.Count == 0)
                {
                    _logging.Warn("No profiles found in import file");
                    return 0;
                }

                foreach (var profile in imported)
                {
                    // Regenerate IDs to avoid conflicts
                    profile.Id = Guid.NewGuid().ToString();
                    profile.CreatedAt = DateTime.Now;
                    profile.ModifiedAt = DateTime.Now;
                    _profiles.Add(profile);
                }

                await SaveProfilesAsync();
                UpdateTrackedProcesses();
                _logging.Info($"Imported {imported.Count} profile(s)");
                return imported.Count;
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to import profiles", ex);
                return 0;
            }
        }

        /// <summary>
        /// Export profiles to a JSON file.
        /// </summary>
        public async Task ExportProfilesAsync(string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_profiles.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filePath, json);
                _logging.Info($"Exported {_profiles.Count} profile(s) to {filePath}");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to export profiles", ex);
            }
        }

        private void OnProcessDetected(object? sender, ProcessDetectedEventArgs e)
        {
            // Find matching profile (by priority if multiple match)
            var matchingProfiles = _profiles
                .Where(p => p.MatchesProcess(e.ProcessInfo.ProcessName, e.ProcessInfo.ExecutablePath))
                .OrderByDescending(p => p.Priority)
                .ToList();

            if (matchingProfiles.Any())
            {
                var profile = matchingProfiles.First();

                if (ActiveProfile != null)
                {
                    // Stop timing previous profile
                    RecordPlaytime(ActiveProfile);
                }

                ActiveProfile = profile;
                _activeProfileStartTime = DateTime.Now;
                profile.LaunchCount++;

                _logging.Info($"Auto-activated profile '{profile.Name}' for {e.ProcessInfo.ProcessName}");

                // Trigger settings application
                ProfileApplyRequested?.Invoke(this, new ProfileApplyEventArgs(profile, ProfileTrigger.GameLaunch));
            }
        }

        private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
        {
            // Check if the exited process was our active profile
            if (ActiveProfile != null && ActiveProfile.MatchesProcess(e.ProcessInfo.ProcessName, e.ProcessInfo.ExecutablePath))
            {
                RecordPlaytime(ActiveProfile);
                _logging.Info($"Profile '{ActiveProfile.Name}' deactivated (game closed, playtime: {e.Runtime:hh\\:mm\\:ss})");

                ActiveProfile = null;

                // Trigger restore defaults
                ProfileApplyRequested?.Invoke(this, new ProfileApplyEventArgs(null, ProfileTrigger.GameExit));
            }
        }

        private void RecordPlaytime(GameProfile profile)
        {
            var playtime = DateTime.Now - _activeProfileStartTime;
            profile.TotalPlaytimeMs += (long)playtime.TotalMilliseconds;
            
            // Save with error handling (non-blocking)
            _ = SavePlaytimeAsync(profile.Name);
        }

        private async Task SavePlaytimeAsync(string profileName)
        {
            try
            {
                await SaveProfilesAsync();
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to save playtime for profile '{profileName}'", ex);
            }
        }

        private void UpdateTrackedProcesses()
        {
            _processMonitor.ClearTrackedProcesses();

            foreach (var profile in _profiles.Where(p => p.IsEnabled && !string.IsNullOrEmpty(p.ExecutableName)))
            {
                _processMonitor.TrackProcess(profile.ExecutableName);
            }
        }

        public void Dispose()
        {
            _processMonitor.ProcessDetected -= OnProcessDetected;
            _processMonitor.ProcessExited -= OnProcessExited;
        }
    }

    public enum ProfileTrigger
    {
        Manual,
        GameLaunch,
        GameExit
    }

    public class ProfileApplyEventArgs : EventArgs
    {
        public GameProfile? Profile { get; }
        public ProfileTrigger Trigger { get; }

        public ProfileApplyEventArgs(GameProfile? profile, ProfileTrigger trigger)
        {
            Profile = profile;
            Trigger = trigger;
        }
    }
}
