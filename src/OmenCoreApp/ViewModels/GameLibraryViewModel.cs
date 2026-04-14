using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the Game Library view showing detected and profiled games.
    /// </summary>
    public class GameLibraryViewModel : INotifyPropertyChanged
    {
        private readonly LoggingService _logging;
        private readonly GameLibraryService _libraryService;
        private readonly GameProfileService _profileService;
        
        private bool _isScanning;
        private string _searchFilter = string.Empty;
        private GamePlatformFilter _platformFilter = GamePlatformFilter.All;
        private GameLibraryItem? _selectedGame;
        private readonly List<GameLibraryItem> _manualGames = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameLibraryViewModel(
            LoggingService logging,
            GameLibraryService libraryService,
            GameProfileService profileService)
        {
            _logging = logging;
            _libraryService = libraryService;
            _profileService = profileService;

            // Initialize commands
            ScanLibraryCommand = new AsyncRelayCommand(async _ => await ScanLibraryAsync(), _ => !IsScanning);
            AddGameCommand = new RelayCommand(AddGameManually, _ => !IsScanning);
            CreateProfileCommand = new RelayCommand(CreateProfile, _ => SelectedGame != null && !SelectedGame.HasProfile);
            EditProfileCommand = new RelayCommand(EditProfile, _ => SelectedGame?.HasProfile == true);
            LaunchGameCommand = new RelayCommand(LaunchGame, _ => SelectedGame != null);
            RefreshCommand = new RelayCommand(_ => RefreshView());

            // Subscribe to library scan events
            _libraryService.ScanCompleted += OnScanCompleted;
            _profileService.ActiveProfileChanged += OnActiveProfileChanged;
        }

        #region Properties

        public ObservableCollection<GameLibraryItem> Games { get; } = new();
        public ObservableCollection<GameLibraryItem> FilteredGames { get; } = new();

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (_isScanning != value)
                {
                    _isScanning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScanStatusText));
                    (ScanLibraryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (AddGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string ScanStatusText => IsScanning ? "Scanning game libraries..." : $"{FilteredGames.Count} games found";

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (_searchFilter != value)
                {
                    _searchFilter = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public GamePlatformFilter PlatformFilter
        {
            get => _platformFilter;
            set
            {
                if (_platformFilter != value)
                {
                    _platformFilter = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public GameLibraryItem? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame != value)
                {
                    _selectedGame = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedGame));
                    
                    // v2.8.6: Re-evaluate button CanExecute when selection changes
                    // Without this, Launch/Create/Edit buttons stay disabled after selecting a game
                    (LaunchGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (CreateProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (EditProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedGame => SelectedGame != null;

        public IEnumerable<GamePlatformFilter> PlatformFilters => Enum.GetValues<GamePlatformFilter>();

        #endregion

        #region Commands

        public ICommand ScanLibraryCommand { get; }
        public ICommand AddGameCommand { get; }
        public ICommand CreateProfileCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand LaunchGameCommand { get; }
        public ICommand RefreshCommand { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Initialize and perform initial scan.
        /// </summary>
        public async Task InitializeAsync()
        {
            await ScanLibraryAsync();
        }

        /// <summary>
        /// Scan all game platforms for installed games.
        /// </summary>
        public async Task ScanLibraryAsync()
        {
            if (IsScanning) return;

            try
            {
                IsScanning = true;
                Games.Clear();
                FilteredGames.Clear();

                var detectedGames = await _libraryService.ScanAllPlatformsAsync();

                foreach (var game in detectedGames)
                {
                    var item = new GameLibraryItem
                    {
                        Name = game.Name,
                        Platform = game.Platform,
                        InstallPath = game.InstallPath,
                        ExecutablePath = game.ExecutablePath,
                        ExecutableName = game.ExecutableName,
                        PlatformId = game.PlatformId,
                        SteamAppId = game.SteamAppId,
                        LaunchCommand = game.GetLaunchCommand()
                    };

                    AttachProfileInfo(item);

                    Games.Add(item);
                }

                // Preserve manually-added games across rescans.
                foreach (var manual in _manualGames)
                {
                    if (!string.IsNullOrWhiteSpace(manual.ExecutablePath) &&
                        Games.Any(g => string.Equals(g.ExecutablePath, manual.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    AttachProfileInfo(manual);
                    Games.Add(manual);
                }

                ApplyFilters();
                _logging.Info($"Game library loaded: {Games.Count} games, {Games.Count(g => g.HasProfile)} with profiles");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to scan game library: {ex.Message}", ex);
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void AddGameManually(object? _)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select game executable",
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    Multiselect = true,
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var addedCount = 0;
                foreach (var path in dialog.FileNames)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    if (Games.Any(g => string.Equals(g.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var item = new GameLibraryItem
                    {
                        Name = Path.GetFileNameWithoutExtension(path),
                        Platform = GamePlatform.Manual,
                        InstallPath = Path.GetDirectoryName(path),
                        ExecutablePath = path,
                        ExecutableName = Path.GetFileName(path),
                        LaunchCommand = path
                    };

                    AttachProfileInfo(item);
                    _manualGames.Add(item);
                    Games.Add(item);
                    SelectedGame = item;
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    ApplyFilters();
                    _logging.Info($"Manually added {addedCount} game(s) to library");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to add game manually: {ex.Message}", ex);
            }
        }

        private void AttachProfileInfo(GameLibraryItem item)
        {
            var existingProfile = _profileService.Profiles.FirstOrDefault(p =>
                string.Equals(p.ExecutableName, item.ExecutableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ExecutablePath, item.ExecutablePath, StringComparison.OrdinalIgnoreCase));

            item.HasProfile = existingProfile != null;
            item.Profile = existingProfile;
            item.ProfileName = existingProfile?.Name;
        }

        /// <summary>
        /// Apply search and platform filters.
        /// </summary>
        private void ApplyFilters()
        {
            FilteredGames.Clear();

            var filtered = Games.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                filtered = filtered.Where(g => 
                    g.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Apply platform filter
            if (PlatformFilter != GamePlatformFilter.All)
            {
                var targetPlatform = (GamePlatform)PlatformFilter;
                filtered = filtered.Where(g => g.Platform == targetPlatform);
            }

            foreach (var game in filtered.OrderBy(g => g.Name))
            {
                FilteredGames.Add(game);
            }

            OnPropertyChanged(nameof(ScanStatusText));
        }

        /// <summary>
        /// Create a new profile for the selected game.
        /// </summary>
        private void CreateProfile(object? _)
        {
            if (SelectedGame == null || string.IsNullOrEmpty(SelectedGame.ExecutableName)) return;

            var profile = _profileService.CreateProfile(
                SelectedGame.Name,
                SelectedGame.ExecutableName);

            if (!string.IsNullOrEmpty(SelectedGame.ExecutablePath))
            {
                profile.ExecutablePath = SelectedGame.ExecutablePath;
            }

            SelectedGame.HasProfile = true;
            SelectedGame.Profile = profile;
            SelectedGame.ProfileName = profile.Name;

            _logging.Info($"Created profile for: {SelectedGame.Name}");
            
            // Trigger profile editor
            ProfileCreated?.Invoke(this, new ProfileEventArgs(profile));
        }

        /// <summary>
        /// Edit existing profile for the selected game.
        /// </summary>
        private void EditProfile(object? _)
        {
            if (SelectedGame?.Profile == null) return;

            ProfileEditRequested?.Invoke(this, new ProfileEventArgs(SelectedGame.Profile));
        }

        /// <summary>
        /// Launch the selected game.
        /// </summary>
        private void LaunchGame(object? _)
        {
            if (SelectedGame == null) return;

            try
            {
                var command = SelectedGame.LaunchCommand;
                if (string.IsNullOrEmpty(command)) return;

                if (command.StartsWith("steam://") || command.StartsWith("com.epicgames.") || command.StartsWith("goggalaxy://"))
                {
                    // URL-based launch
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = command,
                        UseShellExecute = true
                    });
                }
                else if (System.IO.File.Exists(command))
                {
                    // Direct executable launch
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = command,
                        UseShellExecute = true,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(command)
                    });
                }

                _logging.Info($"Launched: {SelectedGame.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to launch {SelectedGame.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Refresh the view without full rescan.
        /// </summary>
        private void RefreshView()
        {
            // Update profile status for all games
            foreach (var game in Games)
            {
                var existingProfile = _profileService.Profiles.FirstOrDefault(p =>
                    string.Equals(p.ExecutableName, game.ExecutableName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase));

                game.HasProfile = existingProfile != null;
                game.Profile = existingProfile;
                game.ProfileName = existingProfile?.Name;
            }

            ApplyFilters();
        }

        private void OnScanCompleted(object? sender, GameLibraryScanEventArgs e)
        {
            _logging.Info($"Library scan completed: {e.GamesFound} games");
        }

        private void OnActiveProfileChanged(object? sender, EventArgs e)
        {
            // Could update UI to show currently active game
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Events

        public event EventHandler<ProfileEventArgs>? ProfileCreated;
        public event EventHandler<ProfileEventArgs>? ProfileEditRequested;

        #endregion
    }

    /// <summary>
    /// Represents a game in the library view.
    /// </summary>
    public class GameLibraryItem : INotifyPropertyChanged
    {
        private bool _hasProfile;
        private string? _profileName;
        private GameProfile? _profile;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; set; } = string.Empty;
        public GamePlatform Platform { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? ExecutableName { get; set; }
        public string? PlatformId { get; set; }
        public string? SteamAppId { get; set; }
        public string? LaunchCommand { get; set; }

        public bool HasProfile
        {
            get => _hasProfile;
            set
            {
                if (_hasProfile != value)
                {
                    _hasProfile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProfileStatusText));
                    OnPropertyChanged(nameof(ProfileStatusIcon));
                }
            }
        }

        public string? ProfileName
        {
            get => _profileName;
            set
            {
                if (_profileName != value)
                {
                    _profileName = value;
                    OnPropertyChanged();
                }
            }
        }

        public GameProfile? Profile
        {
            get => _profile;
            set
            {
                _profile = value;
                OnPropertyChanged();
            }
        }

        public string ProfileStatusText => HasProfile ? $"Profile: {ProfileName}" : "No profile";
        public string ProfileStatusIcon => HasProfile ? "✓" : "○";

        public string PlatformIcon => Platform switch
        {
            GamePlatform.Steam => "🎮",
            GamePlatform.Epic => "🏪",
            GamePlatform.GOG => "🌌",
            GamePlatform.Xbox => "🎯",
            GamePlatform.Ubisoft => "🔷",
            GamePlatform.EA => "⚡",
            GamePlatform.BattleNet => "❄️",
            GamePlatform.Manual => "📌",
            _ => "📁"
        };

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum GamePlatformFilter
    {
        All = 0,
        Steam = 1,
        Epic = 2,
        GOG = 3,
        Xbox = 4,
        Ubisoft = 5,
        EA = 6,
        Manual = 7
    }

    public class ProfileEventArgs : EventArgs
    {
        public GameProfile Profile { get; }
        public ProfileEventArgs(GameProfile profile) => Profile = profile;
    }
}
