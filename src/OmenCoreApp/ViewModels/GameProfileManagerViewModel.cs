using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for Game Profile Manager window.
    /// </summary>
    public class GameProfileManagerViewModel : INotifyPropertyChanged
    {
        private readonly GameProfileService _profileService;
        private readonly LoggingService _logging;
        private GameProfile? _selectedProfile;
        private string _searchText = string.Empty;

        public ObservableCollection<GameProfile> FilteredProfiles { get; } = new();
        
        public GameProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    _selectedProfile = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    FilterProfiles();
                }
            }
        }

        // Dropdown options
        public ObservableCollection<string> FanPresets { get; } = new() { "Default", "Silent", "Balanced", "Performance", "Extreme" };
        public ObservableCollection<string> PerformanceModes { get; } = new() { "Balanced", "Performance", "Comfort" };
        public ObservableCollection<GpuSwitchMode> GpuModes { get; } = new()
        {
            GpuSwitchMode.Hybrid,
            GpuSwitchMode.Discrete,
            GpuSwitchMode.Integrated
        };
        public ObservableCollection<string> KeyboardProfiles { get; } = new() { "Default", "Game Mode", "Ambient", "Custom 1", "Custom 2" };
        public ObservableCollection<string> PeripheralProfiles { get; } = new() { "Default", "RGB Wave", "Static Blue", "Breathing", "Reactive" };

        // Commands
        public ICommand CreateProfileCommand { get; }
        public ICommand DuplicateProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand ImportProfilesCommand { get; }
        public ICommand ExportProfilesCommand { get; }
        public ICommand BrowseExecutableCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameProfileManagerViewModel(GameProfileService profileService, LoggingService logging)
        {
            _profileService = profileService;
            _logging = logging;

            // Initialize commands
            CreateProfileCommand = new RelayCommand(_ => CreateProfile());
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => SelectedProfile != null);
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile != null);
            ImportProfilesCommand = new RelayCommand(_ => ImportProfiles());
            ExportProfilesCommand = new RelayCommand(_ => ExportProfiles());
            BrowseExecutableCommand = new RelayCommand(_ => BrowseExecutable(), _ => SelectedProfile != null);
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());

            // Load profiles
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            FilteredProfiles.Clear();
            foreach (var profile in _profileService.Profiles)
            {
                FilteredProfiles.Add(profile);
            }

            if (FilteredProfiles.Any())
            {
                SelectedProfile = FilteredProfiles.First();
            }
        }

        private void FilterProfiles()
        {
            FilteredProfiles.Clear();

            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? _profileService.Profiles
                : _profileService.Profiles.Where(p =>
                    p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    p.ExecutableName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            foreach (var profile in filtered)
            {
                FilteredProfiles.Add(profile);
            }
        }

        private void CreateProfile()
        {
            var profile = _profileService.CreateProfile("New Game Profile", "game.exe");
            FilterProfiles();
            SelectedProfile = profile;
            _logging.Info("Created new game profile");
        }

        private void DuplicateProfile()
        {
            if (SelectedProfile == null) return;

            var duplicate = _profileService.DuplicateProfile(SelectedProfile);
            FilterProfiles();
            SelectedProfile = duplicate;
            _logging.Info($"Duplicated profile: {SelectedProfile.Name}");
        }

        private async void DeleteProfile()
        {
            if (SelectedProfile == null) return;

            var result = MessageBox.Show(
                $"Delete profile '{SelectedProfile.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var toDelete = SelectedProfile;
                await _profileService.DeleteProfileAsync(toDelete);
                FilterProfiles();
                SelectedProfile = FilteredProfiles.FirstOrDefault();
                _logging.Info($"Deleted profile: {toDelete.Name}");
            }
        }

        private async void ImportProfiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Import Game Profiles"
            };

            if (dialog.ShowDialog() == true)
            {
                var count = await _profileService.ImportProfilesAsync(dialog.FileName);
                MessageBox.Show($"Imported {count} profile(s)", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                FilterProfiles();
                _logging.Info($"Imported {count} profiles from {dialog.FileName}");
            }
        }

        private async void ExportProfiles()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Export Game Profiles",
                FileName = $"omencore-profiles-{DateTime.Now:yyyy-MM-dd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                await _profileService.ExportProfilesAsync(dialog.FileName);
                MessageBox.Show($"Exported {_profileService.Profiles.Count} profile(s)", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                _logging.Info($"Exported profiles to {dialog.FileName}");
            }
        }

        private void BrowseExecutable()
        {
            if (SelectedProfile == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Game Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedProfile.ExecutablePath = dialog.FileName;
                SelectedProfile.ExecutableName = System.IO.Path.GetFileName(dialog.FileName);
                OnPropertyChanged(nameof(SelectedProfile));
            }
        }

        private async void Save()
        {
            await _profileService.SaveProfilesAsync();
            _logging.Info("Saved game profiles");
            CloseWindow();
        }

        private void Cancel()
        {
            // Reload profiles to discard changes
            _ = _profileService.LoadProfilesAsync();
            CloseWindow();
        }

        private void CloseWindow()
        {
            // Find the window and close it
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext == this)
                {
                    window.Close();
                    break;
                }
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
