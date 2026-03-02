using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmenCore.Services;
using OmenCore.Services.BloatwareManager;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the Bloatware Manager view.
    /// Provides UI bindings for scanning, removing, and restoring bloatware.
    /// </summary>
    public class BloatwareManagerViewModel : INotifyPropertyChanged
    {
        private readonly BloatwareManagerService _service;
        private readonly LoggingService _logger;
        private bool _isScanning;
        private bool _isProcessing;
        private string _statusMessage = "Click 'Scan' to detect bloatware";
        private string _filterText = "";
        private BloatwareCategory? _selectedCategory;
        private BloatwareApp? _selectedApp;
        private string _riskFilter = "All";
        private int _bulkRemoveProgress;
        private int _bulkRemoveTotal;
        private bool _isBulkRemoving;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<BloatwareApp> AllApps { get; } = new();
        public ObservableCollection<BloatwareApp> FilteredApps { get; } = new();
        public ObservableCollection<BloatwareCategory> Categories { get; } = new();

        public ICommand ScanCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand RemoveAllLowRiskCommand { get; }
        public ICommand RestoreSelectedCommand { get; }

        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); }
        }

        public bool CanInteract => !IsScanning && !IsProcessing;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string FilterText
        {
            get => _filterText;
            set { _filterText = value; OnPropertyChanged(); ApplyFilter(); }
        }

        public BloatwareCategory? SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(); ApplyFilter(); }
        }

        public BloatwareApp? SelectedApp
        {
            get => _selectedApp;
            set { _selectedApp = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRemoveSelected)); OnPropertyChanged(nameof(CanRestoreSelected)); }
        }

        public bool CanRemoveSelected => SelectedApp != null && !SelectedApp.IsRemoved && CanInteract;
        public bool CanRestoreSelected => SelectedApp != null && SelectedApp.IsRemoved && SelectedApp.CanRestore && CanInteract;

        public int TotalCount => AllApps.Count;
        public int RemovedCount => AllApps.Count(a => a.IsRemoved);
        public int LowRiskCount => AllApps.Count(a => a.RemovalRisk == RemovalRisk.Low && !a.IsRemoved);

        public string RiskFilter
        {
            get => _riskFilter;
            set
            {
                _riskFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRiskAll));
                OnPropertyChanged(nameof(IsRiskLow));
                OnPropertyChanged(nameof(IsRiskMedium));
                OnPropertyChanged(nameof(IsRiskHigh));
                ApplyFilter();
            }
        }

        public bool IsRiskAll    { get => _riskFilter == "All";    set { if (value) RiskFilter = "All"; } }
        public bool IsRiskLow    { get => _riskFilter == "Low";    set { if (value) RiskFilter = "Low"; } }
        public bool IsRiskMedium { get => _riskFilter == "Medium"; set { if (value) RiskFilter = "Medium"; } }
        public bool IsRiskHigh   { get => _riskFilter == "High";   set { if (value) RiskFilter = "High"; } }

        public bool IsBulkRemoving
        {
            get => _isBulkRemoving;
            set { _isBulkRemoving = value; OnPropertyChanged(); }
        }

        public int BulkRemoveProgress
        {
            get => _bulkRemoveProgress;
            set { _bulkRemoveProgress = value; OnPropertyChanged(); }
        }

        public int BulkRemoveTotal
        {
            get => _bulkRemoveTotal;
            set { _bulkRemoveTotal = value; OnPropertyChanged(); }
        }

        public BloatwareManagerViewModel(LoggingService logger)
        {
            _logger = logger;
            _service = new BloatwareManagerService(logger);

            // Initialize commands
            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => CanInteract);
            RemoveSelectedCommand = new RelayCommand(async _ => await RemoveSelectedAsync(), _ => CanRemoveSelected);
            RemoveAllLowRiskCommand = new RelayCommand(async _ => await RemoveAllLowRiskAsync(), _ => LowRiskCount > 0 && CanInteract);
            RestoreSelectedCommand = new RelayCommand(async _ => await RestoreSelectedAsync(), _ => CanRestoreSelected);

            // Initialize categories
            foreach (var cat in Enum.GetValues<BloatwareCategory>().Where(c => c != BloatwareCategory.Unknown))
            {
                Categories.Add(cat);
            }

            // Subscribe to service events
            _service.StatusChanged += status => Application.Current.Dispatcher.Invoke(() => StatusMessage = status);
            _service.AppRemoved += app => Application.Current.Dispatcher.Invoke(() => UpdateCounts());
            _service.AppRestored += app => Application.Current.Dispatcher.Invoke(() => UpdateCounts());
        }

        public async Task ScanAsync()
        {
            if (IsScanning) return;

            try
            {
                IsScanning = true;
                AllApps.Clear();
                FilteredApps.Clear();

                var apps = await _service.ScanForBloatwareAsync();

                foreach (var app in apps)
                {
                    AllApps.Add(app);
                }

                ApplyFilter();
                UpdateCounts();
            }
            catch (Exception ex)
            {
                _logger.Error($"Bloatware scan failed: {ex.Message}");
                StatusMessage = $"Scan failed: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private async Task RemoveSelectedAsync()
        {
            if (SelectedApp == null || IsProcessing) return;

            var app = SelectedApp;

            if (app.RemovalRisk >= RemovalRisk.Medium)
            {
                var result = MessageBox.Show(
                    $"Removing '{app.Name}' has {app.RemovalRisk} risk.\n\n{app.Description}\n\nAre you sure you want to continue?",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            try
            {
                IsProcessing = true;
                await _service.RemoveAppAsync(app);
                UpdateCounts();
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(CanRemoveSelected));
                OnPropertyChanged(nameof(CanRestoreSelected));
            }
        }

        private async Task RemoveAllLowRiskAsync()
        {
            if (IsProcessing) return;

            var lowRiskApps = AllApps.Where(a => a.RemovalRisk == RemovalRisk.Low && !a.IsRemoved).ToList();
            if (!lowRiskApps.Any()) return;

            var result = MessageBox.Show(
                $"This will remove {lowRiskApps.Count} low-risk bloatware items.\n\nThese are safe to remove and can be restored from the Microsoft Store if needed.\n\nContinue?",
                "Remove All Low-Risk Bloatware",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsProcessing = true;
                IsBulkRemoving = true;
                BulkRemoveTotal = lowRiskApps.Count;
                BulkRemoveProgress = 0;
                var count = 0;

                foreach (var app in lowRiskApps)
                {
                    count++;
                    BulkRemoveProgress = count;
                    StatusMessage = $"Removing {count}/{lowRiskApps.Count}: {app.Name}...";
                    await _service.RemoveAppAsync(app);
                }

                StatusMessage = $"Removed {count} bloatware items";
                UpdateCounts();
            }
            finally
            {
                IsProcessing = false;
                IsBulkRemoving = false;
                BulkRemoveProgress = 0;
            }
        }

        private async Task RestoreSelectedAsync()
        {
            if (SelectedApp == null || !SelectedApp.CanRestore || IsProcessing) return;

            try
            {
                IsProcessing = true;
                await _service.RestoreAppAsync(SelectedApp);
                UpdateCounts();
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(CanRemoveSelected));
                OnPropertyChanged(nameof(CanRestoreSelected));
            }
        }

        private void ApplyFilter()
        {
            FilteredApps.Clear();

            var filtered = AllApps.AsEnumerable();

            // Filter by category
            if (SelectedCategory.HasValue)
            {
                filtered = filtered.Where(a => a.Category == SelectedCategory.Value);
            }

            // Filter by risk level
            if (_riskFilter != "All")
            {
                filtered = _riskFilter switch
                {
                    "Low"    => filtered.Where(a => a.RemovalRisk == RemovalRisk.Low),
                    "Medium" => filtered.Where(a => a.RemovalRisk == RemovalRisk.Medium),
                    "High"   => filtered.Where(a => a.RemovalRisk == RemovalRisk.High),
                    _        => filtered
                };
            }

            // Filter by text
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var search = FilterText.ToLowerInvariant();
                filtered = filtered.Where(a =>
                    a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    a.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    a.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var app in filtered)
            {
                FilteredApps.Add(app);
            }

            OnPropertyChanged(nameof(FilteredApps));
        }

        private void UpdateCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(RemovedCount));
            OnPropertyChanged(nameof(LowRiskCount));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
