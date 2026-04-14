using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
        private string _statusMessage = "Done: Click Scan to detect bloatware";
        private string _filterText = "";
        private BloatwareCategory? _selectedCategory;
        private BloatwareApp? _selectedApp;
        private string _riskFilter = "All";
        private int _bulkRemoveProgress;
        private int _bulkRemoveTotal;
        private bool _isBulkRemoving;
        private int _bulkRestoreProgress;
        private int _bulkRestoreTotal;
        private bool _isBulkRestoring;
        private bool _hasRemovalResults;
        private CancellationTokenSource? _bulkOperationCancellation;
        private string? _lastReportPath;
        private bool _isRemovalPreviewVisible;
        private string _removalPreviewTitle = "Removal Preview";
        private string _removalPreviewSummary = "";
        private int _removalPreviewEstimatedSeconds;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<BloatwareApp> AllApps { get; } = new();
        public ObservableCollection<BloatwareApp> FilteredApps { get; } = new();
        public ObservableCollection<BloatwareCategory> Categories { get; } = new();
        public ObservableCollection<BloatwareRemovalPreviewItem> RemovalPreviewItems { get; } = new();

        public ICommand ScanCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand RemoveAllLowRiskCommand { get; }
        public ICommand RestoreSelectedCommand { get; }
        public ICommand RestoreAllRemovedCommand { get; }
        public ICommand ExportResultLogCommand { get; }
        public ICommand CancelBulkOperationCommand { get; }
        public ICommand ViewLastReportCommand { get; }
        public ICommand ConfirmRemovalPreviewCommand { get; }
        public ICommand CancelRemovalPreviewCommand { get; }
        public ICommand ToggleAllPreviewItemsCommand { get; }

        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); RaiseCommandStates(); }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInteract)); RaiseCommandStates(); }
        }

        public bool CanInteract => !IsScanning && !IsProcessing;

        public bool IsRemovalPreviewVisible
        {
            get => _isRemovalPreviewVisible;
            private set
            {
                _isRemovalPreviewVisible = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

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
            set
            {
                _selectedApp = value;
                OnPropertyChanged();
                // Must raise CanExecuteChanged on the commands (not just PropertyChanged on
                // the computed bool) so WPF re-evaluates button IsEnabled via ICommand.CanExecute.
                RaiseCommandStates();
            }
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
            set { _isBulkRemoving = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCancelBulkOperation)); RaiseCommandStates(); }
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

        public bool IsBulkRestoring
        {
            get => _isBulkRestoring;
            set { _isBulkRestoring = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCancelBulkOperation)); RaiseCommandStates(); }
        }

        public int BulkRestoreProgress
        {
            get => _bulkRestoreProgress;
            set { _bulkRestoreProgress = value; OnPropertyChanged(); }
        }

        public int BulkRestoreTotal
        {
            get => _bulkRestoreTotal;
            set { _bulkRestoreTotal = value; OnPropertyChanged(); }
        }

        /// <summary>True after any bulk removal has completed, enabling the Export Log button.</summary>
        public bool HasRemovalResults
        {
            get => _hasRemovalResults;
            set { _hasRemovalResults = value; OnPropertyChanged(); }
        }

        public string? LastReportPath
        {
            get => _lastReportPath;
            private set
            {
                _lastReportPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLastReport));
                RaiseCommandStates();
            }
        }

        public bool HasLastReport => !string.IsNullOrWhiteSpace(LastReportPath) && File.Exists(LastReportPath);

        public string RemovalPreviewTitle
        {
            get => _removalPreviewTitle;
            private set { _removalPreviewTitle = value; OnPropertyChanged(); }
        }

        public string RemovalPreviewSummary
        {
            get => _removalPreviewSummary;
            private set { _removalPreviewSummary = value; OnPropertyChanged(); }
        }

        public string RemovalPreviewEstimatedText => _removalPreviewEstimatedSeconds <= 0
            ? "Estimated time: less than 1 minute"
            : $"Estimated time: about {_removalPreviewEstimatedSeconds / 60 + 1} minute(s)";

        public bool HasRemovalPreviewSelection => RemovalPreviewItems.Any(item => item.IsSelected);

        public bool CanCancelBulkOperation => IsBulkRemoving || IsBulkRestoring;

        public string BulkRemoveProgressText => BulkRemoveTotal > 0
            ? $"{BulkRemoveProgress}/{BulkRemoveTotal} done"
            : "Waiting...";

        public string BulkRestoreProgressText => BulkRestoreTotal > 0
            ? $"{BulkRestoreProgress}/{BulkRestoreTotal} done"
            : "Waiting...";

        public BloatwareManagerViewModel(LoggingService logger)
        {
            _logger = logger;
            _service = new BloatwareManagerService(logger);

            // Initialize commands
            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => CanInteract);
            RemoveSelectedCommand = new RelayCommand(async _ => await PrepareSelectedRemovalPreviewAsync(), _ => CanRemoveSelected);
            RemoveAllLowRiskCommand = new RelayCommand(async _ => await PrepareLowRiskRemovalPreviewAsync(), _ => LowRiskCount > 0 && CanInteract);
            RestoreSelectedCommand = new RelayCommand(async _ => await RestoreSelectedAsync(), _ => CanRestoreSelected);
            RestoreAllRemovedCommand = new RelayCommand(async _ => await RestoreAllRemovedAsync(), _ => RemovedCount > 0 && CanInteract);
            ExportResultLogCommand = new RelayCommand(_ => ExportResultLog(), _ => HasRemovalResults);
            CancelBulkOperationCommand = new RelayCommand(_ => CancelBulkOperation(), _ => CanCancelBulkOperation);
            ViewLastReportCommand = new RelayCommand(_ => ViewLastReport(), _ => HasLastReport);
            ConfirmRemovalPreviewCommand = new RelayCommand(async _ => await ConfirmRemovalPreviewAsync(), _ => IsRemovalPreviewVisible && HasRemovalPreviewSelection && CanInteract);
            CancelRemovalPreviewCommand = new RelayCommand(_ => CancelRemovalPreview(), _ => IsRemovalPreviewVisible && CanInteract);
            ToggleAllPreviewItemsCommand = new RelayCommand(_ => ToggleAllPreviewItems(), _ => IsRemovalPreviewVisible && RemovalPreviewItems.Count > 0 && CanInteract);

            // Initialize categories
            foreach (var cat in Enum.GetValues<BloatwareCategory>().Where(c => c != BloatwareCategory.Unknown))
            {
                Categories.Add(cat);
            }

            // Subscribe to service events
            _service.StatusChanged += status => Application.Current.Dispatcher.Invoke(() => StatusMessage = status);
            _service.AppRemoved += app => Application.Current.Dispatcher.Invoke(() => UpdateCounts());
            _service.AppRestored += app => Application.Current.Dispatcher.Invoke(() => UpdateCounts());

            // Warn immediately if not running as admin
            if (!BloatwareManagerService.IsRunningAsAdmin)
            {
                SetStatusFailed("OmenCore is not running as Administrator. Bloatware removal requires admin rights.");
            }
        }

        public async Task ScanAsync()
        {
            if (IsScanning) return;

            try
            {
                IsScanning = true;
                SetStatusAction("Scanning for bloatware");
                AllApps.Clear();
                FilteredApps.Clear();

                var apps = await _service.ScanForBloatwareAsync();

                foreach (var app in apps)
                {
                    AllApps.Add(app);
                }

                ApplyFilter();
                UpdateCounts();
                SetStatusDone($"Scan completed: {AllApps.Count} items detected");
            }
            catch (Exception ex)
            {
                _logger.Error($"Bloatware scan failed: {ex.Message}");
                SetStatusFailed($"Scan failed: {ex.Message}");
            }
            finally
            {
                IsScanning = false;
            }
        }

        private async Task PrepareSelectedRemovalPreviewAsync()
        {
            if (!BloatwareManagerService.IsRunningAsAdmin)
            {
                SetStatusFailed("Cannot remove: OmenCore is not running as Administrator.");
                return;
            }

            if (SelectedApp == null || IsProcessing)
            {
                return;
            }

            BuildRemovalPreview("Remove Selected Item", new List<BloatwareApp> { SelectedApp });
            await Task.CompletedTask;
        }

        private async Task ExecuteSingleRemovalAsync(BloatwareApp app)
        {
            try
            {
                IsProcessing = true;
                SetStatusAction($"Removing {app.Name}");
                await _service.RemoveAppAsync(app);
                HasRemovalResults = true;
                UpdateCounts();
                if (app.LastRemovalStatus == RemovalStatus.Failed)
                {
                    SetStatusFailed($"{app.Name} removal failed");
                }
                else
                {
                    SetStatusDone($"Removed {app.Name}");
                }

                GenerateAndStoreSessionReport();
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(CanRemoveSelected));
                OnPropertyChanged(nameof(CanRestoreSelected));
            }
        }

        private async Task PrepareLowRiskRemovalPreviewAsync()
        {
            if (!BloatwareManagerService.IsRunningAsAdmin)
            {
                SetStatusFailed("Cannot remove: OmenCore is not running as Administrator.");
                return;
            }

            if (IsProcessing) return;

            var lowRiskApps = AllApps.Where(a => a.RemovalRisk == RemovalRisk.Low && !a.IsRemoved).ToList();
            if (!lowRiskApps.Any())
            {
                return;
            }

            BuildRemovalPreview("Remove Low-Risk Bloatware", lowRiskApps);
            await Task.CompletedTask;
        }

        private async Task ExecuteBulkRemovalAsync(List<BloatwareApp> appsToRemove)
        {
            try
            {
                IsProcessing = true;
                IsBulkRemoving = true;
                BulkRemoveTotal = appsToRemove.Count;
                BulkRemoveProgress = 0;
                OnPropertyChanged(nameof(BulkRemoveProgressText));
                using var cancellation = BeginBulkOperation();
                var bulkResult = await _service.RemoveAppsWithRollbackAsync(
                    appsToRemove,
                    (index, total, app) => Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        BulkRemoveProgress = index;
                        OnPropertyChanged(nameof(BulkRemoveProgressText));
                        SetStatusAction($"Removing {index}/{total}: {app.Name}");
                    })),
                    cancellation.Token);

                if (bulkResult.Canceled)
                {
                    var canceledAt = bulkResult.CanceledAt?.Name ?? "the current item";
                    SetStatusFailed(
                        $"Bulk remove canceled at {canceledAt}. {bulkResult.Succeeded.Count} completed, {bulkResult.Skipped.Count} skipped. Export log for details.");
                }
                else if (bulkResult.Completed)
                {
                    SetStatusDone($"Removed {bulkResult.Succeeded.Count}/{bulkResult.RequestedTotal} items successfully");
                }
                else
                {
                    var failedName = bulkResult.FailedAt?.Name ?? "unknown item";
                    SetStatusFailed(
                        $"Bulk remove stopped at {failedName}. Rollback restored {bulkResult.RollbackSucceeded.Count}, " +
                        $"failed {bulkResult.RollbackFailed.Count}, skipped {bulkResult.RollbackSkipped.Count}. Export log for details.");
                }

                HasRemovalResults = true;
                UpdateCounts();
                GenerateAndStoreSessionReport();
            }
            finally
            {
                EndBulkOperation();
                IsProcessing = false;
                IsBulkRemoving = false;
                BulkRemoveProgress = 0;
                BulkRemoveTotal = 0;
                OnPropertyChanged(nameof(BulkRemoveProgressText));
            }
        }

        private void BuildRemovalPreview(string title, List<BloatwareApp> candidates)
        {
            RemovalPreviewItems.Clear();

            foreach (var app in candidates)
            {
                var item = new BloatwareRemovalPreviewItem(
                    app,
                    EstimateRemovalSeconds(app),
                    GetDependencyHint(app));

                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(BloatwareRemovalPreviewItem.IsSelected))
                    {
                        UpdateRemovalPreviewSummary();
                    }
                };

                RemovalPreviewItems.Add(item);
            }

            RemovalPreviewTitle = title;
            IsRemovalPreviewVisible = true;
            UpdateRemovalPreviewSummary();
        }

        private void UpdateRemovalPreviewSummary()
        {
            var selectedCount = RemovalPreviewItems.Count(item => item.IsSelected);
            var totalCount = RemovalPreviewItems.Count;
            _removalPreviewEstimatedSeconds = RemovalPreviewItems
                .Where(item => item.IsSelected)
                .Sum(item => item.EstimatedSeconds);

            RemovalPreviewSummary = $"Selected {selectedCount} of {totalCount} item(s).";
            OnPropertyChanged(nameof(RemovalPreviewEstimatedText));
            OnPropertyChanged(nameof(HasRemovalPreviewSelection));
            RaiseCommandStates();
        }

        private async Task ConfirmRemovalPreviewAsync()
        {
            if (!IsRemovalPreviewVisible || IsProcessing)
            {
                return;
            }

            var selectedApps = RemovalPreviewItems
                .Where(item => item.IsSelected)
                .Select(item => item.App)
                .ToList();

            if (!selectedApps.Any())
            {
                SetStatusFailed("No apps selected in removal preview.");
                return;
            }

            if (selectedApps.Any(app => app.RemovalRisk >= RemovalRisk.Medium))
            {
                var confirmation = MessageBox.Show(
                    "One or more selected items are Medium/High risk. Continue with removal?",
                    "Confirm Risky Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var canProceed = await EnsureRestorePointBeforeRemovalAsync(selectedApps.Count);
            if (!canProceed)
            {
                return;
            }

            CancelRemovalPreview();

            if (selectedApps.Count == 1)
            {
                await ExecuteSingleRemovalAsync(selectedApps[0]);
                return;
            }

            await ExecuteBulkRemovalAsync(selectedApps);
        }

        private async Task<bool> EnsureRestorePointBeforeRemovalAsync(int selectedCount)
        {
            if (!BloatwareManagerService.IsRunningAsAdmin)
            {
                return true;
            }

            var createRestorePoint = MessageBox.Show(
                $"Create a Windows restore point before removing {selectedCount} item(s)?\n\nRecommended for safety before system changes.",
                "Create Restore Point",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (createRestorePoint != MessageBoxResult.Yes)
            {
                SetStatusAction("Restore point creation skipped by user");
                return true;
            }

            SetStatusAction("Creating pre-removal restore point");
            var restorePointResult = await _service.EnsurePreRemovalRestorePointAsync(selectedCount);
            if (restorePointResult.Success)
            {
                var createdAtText = restorePointResult.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "now";
                var prefix = restorePointResult.ReusedExisting ? "Reusing recent restore point" : "Restore point created";
                SetStatusDone($"{prefix} ({createdAtText})");
                return true;
            }

            var continueWithoutRestorePoint = MessageBox.Show(
                $"Restore point could not be created:\n{restorePointResult.Message}\n\nContinue removal without a restore point?",
                "Restore Point Creation Failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (continueWithoutRestorePoint == MessageBoxResult.Yes)
            {
                SetStatusFailed("Proceeding without restore point by user choice");
                return true;
            }

            SetStatusFailed("Removal canceled because restore point was not created");
            return false;
        }

        private void CancelRemovalPreview()
        {
            IsRemovalPreviewVisible = false;
            RemovalPreviewItems.Clear();
            RemovalPreviewSummary = string.Empty;
            _removalPreviewEstimatedSeconds = 0;
            OnPropertyChanged(nameof(RemovalPreviewEstimatedText));
            OnPropertyChanged(nameof(HasRemovalPreviewSelection));
            RaiseCommandStates();
        }

        private void ToggleAllPreviewItems()
        {
            if (!RemovalPreviewItems.Any())
            {
                return;
            }

            var shouldSelectAll = RemovalPreviewItems.Any(item => !item.IsSelected);
            foreach (var item in RemovalPreviewItems)
            {
                item.IsSelected = shouldSelectAll;
            }

            UpdateRemovalPreviewSummary();
        }

        private int EstimateRemovalSeconds(BloatwareApp app)
        {
            return app.Type switch
            {
                BloatwareType.Win32App => 20,
                BloatwareType.AppxPackage => 8,
                BloatwareType.StartupItem => 3,
                BloatwareType.ScheduledTask => 3,
                _ => 8
            };
        }

        private string GetDependencyHint(BloatwareApp app)
        {
            var relatedCount = AllApps.Count(candidate =>
                !ReferenceEquals(candidate, app)
                && !candidate.IsRemoved
                && !string.IsNullOrWhiteSpace(candidate.Publisher)
                && candidate.Publisher.Equals(app.Publisher, StringComparison.OrdinalIgnoreCase));

            if (relatedCount <= 0)
            {
                return "No obvious linked packages detected";
            }

            return $"Potentially related packages from same publisher: {relatedCount}";
        }

        private async Task RestoreSelectedAsync()
        {
            if (SelectedApp == null || !SelectedApp.CanRestore || IsProcessing) return;

            try
            {
                IsProcessing = true;
                SetStatusAction($"Restoring {SelectedApp.Name}");
                await _service.RestoreAppAsync(SelectedApp);
                UpdateCounts();
                SetStatusDone($"Restored {SelectedApp.Name}");
            }
            finally
            {
                IsProcessing = false;
                OnPropertyChanged(nameof(CanRemoveSelected));
                OnPropertyChanged(nameof(CanRestoreSelected));
            }
        }

        private async Task RestoreAllRemovedAsync()
        {
            if (IsProcessing) return;

            var removedApps = AllApps.Where(a => a.IsRemoved && a.CanRestore).ToList();
            if (!removedApps.Any()) return;

            var result = MessageBox.Show(
                $"This will restore {removedApps.Count} removed items.\n\nAny dependencies or settings will be re-installed from their original sources.\n\nContinue?",
                "Restore All Removed Bloatware",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsProcessing = true;
                IsBulkRestoring = true;
                BulkRestoreTotal = removedApps.Count;
                BulkRestoreProgress = 0;
                OnPropertyChanged(nameof(BulkRestoreProgressText));
                using var cancellation = BeginBulkOperation();
                var restoreResult = await _service.RestoreAppsAsync(
                    removedApps,
                    (index, total, app) => Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        BulkRestoreProgress = index;
                        OnPropertyChanged(nameof(BulkRestoreProgressText));
                        SetStatusAction($"Restoring {index}/{total}: {app.Name}");
                    })),
                    cancellation.Token);

                if (restoreResult.Canceled)
                {
                    var canceledAt = restoreResult.CanceledAt?.Name ?? "the current item";
                    SetStatusFailed(
                        $"Bulk restore canceled at {canceledAt}. {restoreResult.Succeeded.Count} completed, {restoreResult.Skipped.Count} skipped.");
                }
                else if (restoreResult.Failed.Count > 0)
                {
                    SetStatusFailed(
                        $"Bulk restore finished with {restoreResult.Failed.Count} failures. Restored {restoreResult.Succeeded.Count}/{restoreResult.RequestedTotal} items.");
                }
                else
                {
                    SetStatusDone($"Restored {restoreResult.Succeeded.Count} bloatware items");
                }

                UpdateCounts();
                GenerateAndStoreSessionReport();
            }
            finally
            {
                EndBulkOperation();
                IsProcessing = false;
                IsBulkRestoring = false;
                BulkRestoreProgress = 0;
                BulkRestoreTotal = 0;
                OnPropertyChanged(nameof(BulkRestoreProgressText));
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

        private void ExportResultLog()
        {
            var path = _service.ExportRemovalLog(AllApps);
            if (path == null)
            {
                SetStatusFailed("Nothing to export — no removal attempts recorded in this session.");
                return;
            }

            LastReportPath = path;
            SetStatusDone($"Log exported: {path}");
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch { /* non-fatal; file is still created */ }
        }

        private void GenerateAndStoreSessionReport()
        {
            var path = _service.ExportRemovalLog(AllApps);
            if (!string.IsNullOrWhiteSpace(path))
            {
                LastReportPath = path;
            }
        }

        private void ViewLastReport()
        {
            if (!HasLastReport || string.IsNullOrWhiteSpace(LastReportPath))
            {
                SetStatusFailed("No report is available yet.");
                return;
            }

            try
            {
                Process.Start("explorer.exe", LastReportPath);
            }
            catch (Exception ex)
            {
                SetStatusFailed($"Failed to open report: {ex.Message}");
            }
        }

        private void SetStatusAction(string action)
        {
            StatusMessage = $"{action.Trim().TrimEnd('.')}...";
        }

        private void SetStatusDone(string details)
        {
            StatusMessage = $"Done: {details}";
        }

        private void SetStatusFailed(string reason)
        {
            StatusMessage = $"Failed: {reason}";
        }

        private CancellationTokenSource BeginBulkOperation()
        {
            EndBulkOperation();
            _bulkOperationCancellation = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanCancelBulkOperation));
            RaiseCommandStates();
            return _bulkOperationCancellation;
        }

        private void EndBulkOperation()
        {
            _bulkOperationCancellation?.Dispose();
            _bulkOperationCancellation = null;
            OnPropertyChanged(nameof(CanCancelBulkOperation));
            RaiseCommandStates();
        }

        private void CancelBulkOperation()
        {
            if (!CanCancelBulkOperation)
            {
                return;
            }

            _bulkOperationCancellation?.Cancel();
            SetStatusAction("Cancel requested");
        }

        private void RaiseCommandStates()
        {
            (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveAllLowRiskCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RestoreSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RestoreAllRemovedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportResultLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelBulkOperationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ViewLastReportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ConfirmRemovalPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelRemovalPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleAllPreviewItemsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class BloatwareRemovalPreviewItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public BloatwareApp App { get; }
        public string Name => App.Name;
        public RemovalRisk Risk => App.RemovalRisk;
        public int EstimatedSeconds { get; }
        public string EstimatedText => EstimatedSeconds <= 5 ? "< 5s" : $"~{EstimatedSeconds}s";
        public string DependencyHint { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public BloatwareRemovalPreviewItem(BloatwareApp app, int estimatedSeconds, string dependencyHint)
        {
            App = app;
            EstimatedSeconds = estimatedSeconds;
            DependencyHint = dependencyHint;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
