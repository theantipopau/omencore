using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OmenCore.Services;
using OmenCore.Services.SystemOptimizer;
using OmenCore.Services.SystemOptimizer.Optimizations;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the System Optimizer view.
    /// </summary>
    public class SystemOptimizerViewModel : INotifyPropertyChanged
    {
        private readonly LoggingService _logger;
        private readonly SystemOptimizerService _optimizerService;
        
        private bool _isLoading;
        private string _statusMessage = "Done";
        private OptimizationState? _currentState;
        private bool _hasUnsavedChanges;
        private int _operationCurrentStep;
        private int _operationTotalSteps;
        private string _operationProgressLabel = string.Empty;
        private readonly DispatcherTimer _verificationTimer;
        private DateTime? _lastVerifiedAt;
        private string _verificationSummary = "Verification pending";
        private bool _hasStateDrift;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public SystemOptimizerViewModel(LoggingService logger)
        {
            _logger = logger;
            
            // Initialize optimizer (creates its own backup service internally)
            _optimizerService = new SystemOptimizerService(logger);
            
            // Subscribe to status updates — marshal to UI thread so StatusMessage
            // can be safely set from background threads (avoids cross-thread binding errors).
            _optimizerService.StatusChanged += status => 
            {
                Application.Current?.Dispatcher?.BeginInvoke(() => HandleServiceStatus(status));
            };
            
            // Initialize collections
            PowerOptimizations = new ObservableCollection<OptimizationItem>();
            ServiceOptimizations = new ObservableCollection<OptimizationItem>();
            NetworkOptimizations = new ObservableCollection<OptimizationItem>();
            InputOptimizations = new ObservableCollection<OptimizationItem>();
            VisualOptimizations = new ObservableCollection<OptimizationItem>();
            StorageOptimizations = new ObservableCollection<OptimizationItem>();
            
            // Initialize commands
            RefreshCommand = new RelayCommand(_ => _ = RefreshStateAsync());
            ApplyGamingMaxCommand = new RelayCommand(_ => _ = ApplyGamingMaxAsync());
            ApplyBalancedCommand = new RelayCommand(_ => _ = ApplyBalancedAsync());
            RevertAllCommand = new RelayCommand(_ => _ = RevertAllAsync());
            ApplyPowerCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Power & Performance", PowerOptimizations), _ => CanApplyCategory(PowerOptimizations));
            ApplyServicesCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Windows Services", ServiceOptimizations), _ => CanApplyCategory(ServiceOptimizations));
            ApplyNetworkCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Network & Latency", NetworkOptimizations), _ => CanApplyCategory(NetworkOptimizations));
            ApplyInputCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Input & Gaming", InputOptimizations), _ => CanApplyCategory(InputOptimizations));
            ApplyVisualCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Visual Effects", VisualOptimizations), _ => CanApplyCategory(VisualOptimizations));
            ApplyStorageCommand = new RelayCommand(_ => _ = ApplyCategoryAsync("Storage & Disk", StorageOptimizations), _ => CanApplyCategory(StorageOptimizations));

            _verificationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(1)
            };
            _verificationTimer.Tick += async (_, _) => await RunBackgroundVerificationAsync();
            _verificationTimer.Start();
            
            // Load initial state
            _ = RefreshStateAsync();
        }

        // ========== PROPERTIES ==========

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotLoading)); RaiseCategoryCommandStates(); }
        }

        public bool IsNotLoading => !_isLoading;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { _hasUnsavedChanges = value; OnPropertyChanged(); }
        }

        public int OperationCurrentStep
        {
            get => _operationCurrentStep;
            private set { _operationCurrentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(OperationProgressValue)); OnPropertyChanged(nameof(OperationProgressText)); OnPropertyChanged(nameof(HasOperationProgress)); }
        }

        public int OperationTotalSteps
        {
            get => _operationTotalSteps;
            private set { _operationTotalSteps = value; OnPropertyChanged(); OnPropertyChanged(nameof(OperationProgressMaximum)); OnPropertyChanged(nameof(OperationProgressText)); OnPropertyChanged(nameof(HasOperationProgress)); }
        }

        public string OperationProgressLabel
        {
            get => _operationProgressLabel;
            private set { _operationProgressLabel = value; OnPropertyChanged(); }
        }

        public bool HasOperationProgress => OperationTotalSteps > 0;
        public double OperationProgressValue => OperationCurrentStep;
        public double OperationProgressMaximum => OperationTotalSteps > 0 ? OperationTotalSteps : 1;
        public string OperationProgressText => HasOperationProgress ? $"Step {OperationCurrentStep}/{OperationTotalSteps}" : string.Empty;

        public int ActiveOptimizationCount => _currentState?.ActiveCount ?? 0;
        public int TotalOptimizationCount => _currentState?.TotalCount ?? 0;
        public DateTime? LastVerifiedAt
        {
            get => _lastVerifiedAt;
            private set
            {
                _lastVerifiedAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastVerifiedText));
            }
        }

        public string LastVerifiedText => LastVerifiedAt.HasValue
            ? $"Last verified {LastVerifiedAt.Value:g}"
            : "Verification pending";

        public string VerificationSummary
        {
            get => _verificationSummary;
            private set { _verificationSummary = value; OnPropertyChanged(); }
        }

        public bool HasStateDrift
        {
            get => _hasStateDrift;
            private set { _hasStateDrift = value; OnPropertyChanged(); }
        }

        public string OptimizationSummary => 
            $"{ActiveOptimizationCount} of {TotalOptimizationCount} optimizations active";

        // Optimization Collections
        public ObservableCollection<OptimizationItem> PowerOptimizations { get; }
        public ObservableCollection<OptimizationItem> ServiceOptimizations { get; }
        public ObservableCollection<OptimizationItem> NetworkOptimizations { get; }
        public ObservableCollection<OptimizationItem> InputOptimizations { get; }
        public ObservableCollection<OptimizationItem> VisualOptimizations { get; }
        public ObservableCollection<OptimizationItem> StorageOptimizations { get; }

        // ========== COMMANDS ==========

        public ICommand RefreshCommand { get; }
        public ICommand ApplyGamingMaxCommand { get; }
        public ICommand ApplyBalancedCommand { get; }
        public ICommand RevertAllCommand { get; }
        public ICommand ApplyPowerCommand { get; }
        public ICommand ApplyServicesCommand { get; }
        public ICommand ApplyNetworkCommand { get; }
        public ICommand ApplyInputCommand { get; }
        public ICommand ApplyVisualCommand { get; }
        public ICommand ApplyStorageCommand { get; }

        // ========== METHODS ==========

        public async Task RefreshStateAsync(bool showOverlay = true)
        {
            try
            {
                if (showOverlay)
                {
                    IsLoading = true;
                }

                SetStatusAction(showOverlay ? "Scanning system state" : "Refreshing optimizer state");
                
                _currentState = await _optimizerService.VerifyStateAsync();
                LastVerifiedAt = _currentState.LastChecked;
                VerificationSummary = "Verified against current system state";
                HasStateDrift = false;
                
                UpdateOptimizationCollections();
                
                OnPropertyChanged(nameof(ActiveOptimizationCount));
                OnPropertyChanged(nameof(TotalOptimizationCount));
                OnPropertyChanged(nameof(OptimizationSummary));
                
                SetStatusDone();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refresh optimization state: {ex.Message}");
                SetStatusFailed(ex.Message);
            }
            finally
            {
                if (showOverlay)
                {
                    IsLoading = false;
                }
            }
        }

        private void UpdateOptimizationCollections()
        {
            if (_currentState == null) return;
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Power
                PowerOptimizations.Clear();
                PowerOptimizations.Add(new OptimizationItem
                {
                    Id = "power_ultimate_perf",
                    Name = "Ultimate Performance Plan",
                    Description = "Sets Windows power plan to Ultimate Performance for maximum CPU/GPU performance",
                    IsEnabled = _currentState.Power.UltimatePerformancePlan,
                    Risk = OptimizationRisk.Low,
                    Category = "Power",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                PowerOptimizations.Add(new OptimizationItem
                {
                    Id = "power_gpu_scheduling",
                    Name = "Hardware GPU Scheduling",
                    Description = "Enables hardware-accelerated GPU scheduling (Windows 10 2004+)",
                    IsEnabled = _currentState.Power.HardwareGpuScheduling,
                    Risk = OptimizationRisk.Low,
                    Category = "Power",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                PowerOptimizations.Add(new OptimizationItem
                {
                    Id = "power_game_mode",
                    Name = "Game Mode",
                    Description = "Enables Windows Game Mode for better gaming performance",
                    IsEnabled = _currentState.Power.GameModeEnabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Power",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                PowerOptimizations.Add(new OptimizationItem
                {
                    Id = "power_foreground_priority",
                    Name = "Foreground Priority Boost",
                    Description = "Increases CPU priority for foreground applications",
                    IsEnabled = _currentState.Power.ForegroundPriority,
                    Risk = OptimizationRisk.Low,
                    Category = "Power",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                // Services
                ServiceOptimizations.Clear();
                ServiceOptimizations.Add(new OptimizationItem
                {
                    Id = "service_telemetry",
                    Name = "Disable Telemetry",
                    Description = "Disables Windows telemetry data collection (DiagTrack service)",
                    IsEnabled = _currentState.Services.TelemetryDisabled,
                    Risk = OptimizationRisk.Medium,
                    Category = "Services",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                ServiceOptimizations.Add(new OptimizationItem
                {
                    Id = "service_sysmain",
                    Name = "Disable SysMain (Superfetch)",
                    Description = "Disables Superfetch service - recommended for SSD systems",
                    IsEnabled = _currentState.Services.SysMainDisabled,
                    Risk = OptimizationRisk.Medium,
                    Category = "Services",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                ServiceOptimizations.Add(new OptimizationItem
                {
                    Id = "service_search",
                    Name = "Disable Search Indexing",
                    Description = "Disables Windows Search indexing for lower disk/CPU usage",
                    IsEnabled = _currentState.Services.SearchIndexingDisabled,
                    Risk = OptimizationRisk.Medium,
                    Warning = "May slow down file searches",
                    Category = "Services",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                ServiceOptimizations.Add(new OptimizationItem
                {
                    Id = "service_diagtrack",
                    Name = "Disable Connected Experiences",
                    Description = "Disables Connected User Experiences and Telemetry service",
                    IsEnabled = _currentState.Services.DiagTrackDisabled,
                    Risk = OptimizationRisk.Medium,
                    Category = "Services",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                // Network
                NetworkOptimizations.Clear();
                NetworkOptimizations.Add(new OptimizationItem
                {
                    Id = "network_tcp_nodelay",
                    Name = "TCP No Delay",
                    Description = "Disables TCP delayed acknowledgments for lower latency",
                    IsEnabled = _currentState.Network.TcpNoDelay,
                    Risk = OptimizationRisk.Low,
                    Category = "Network",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                NetworkOptimizations.Add(new OptimizationItem
                {
                    Id = "network_tcp_ack",
                    Name = "TCP ACK Optimization",
                    Description = "Optimizes TCP ACK frequency for faster responses",
                    IsEnabled = _currentState.Network.TcpAckFrequency,
                    Risk = OptimizationRisk.Low,
                    Category = "Network",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                NetworkOptimizations.Add(new OptimizationItem
                {
                    Id = "network_delivery_opt",
                    Name = "Disable P2P Updates",
                    Description = "Disables Delivery Optimization P2P sharing",
                    IsEnabled = _currentState.Network.DeliveryOptimizationDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Network",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                NetworkOptimizations.Add(new OptimizationItem
                {
                    Id = "network_nagle",
                    Name = "Disable Nagle Algorithm",
                    Description = "Reduces TCP buffering delay - helpful for online games",
                    IsEnabled = _currentState.Network.NagleDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Network",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                // Input
                InputOptimizations.Clear();
                InputOptimizations.Add(new OptimizationItem
                {
                    Id = "input_mouse_accel",
                    Name = "Disable Mouse Acceleration",
                    Description = "Disables 'Enhance pointer precision' for 1:1 mouse movement",
                    IsEnabled = _currentState.Input.MouseAccelerationDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Input",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                InputOptimizations.Add(new OptimizationItem
                {
                    Id = "input_game_dvr",
                    Name = "Disable Game DVR",
                    Description = "Disables background game recording for better performance",
                    IsEnabled = _currentState.Input.GameDvrDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Input",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                InputOptimizations.Add(new OptimizationItem
                {
                    Id = "input_game_bar",
                    Name = "Disable Xbox Game Bar",
                    Description = "Disables the Xbox Game Bar overlay",
                    IsEnabled = _currentState.Input.GameBarDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Input",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                InputOptimizations.Add(new OptimizationItem
                {
                    Id = "input_fullscreen_opt",
                    Name = "Fullscreen Optimizations",
                    Description = "Configures fullscreen optimizations for better compatibility",
                    IsEnabled = _currentState.Input.FullscreenOptimizationsDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Input",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                // Visual
                VisualOptimizations.Clear();
                VisualOptimizations.Add(new OptimizationItem
                {
                    Id = "visual_transparency",
                    Name = "Disable Transparency",
                    Description = "Disables Windows transparency effects",
                    IsEnabled = _currentState.Visual.TransparencyDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Visual",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                VisualOptimizations.Add(new OptimizationItem
                {
                    Id = "visual_animations",
                    Name = "Disable Animations",
                    Description = "Disables window animations for snappier UI",
                    IsEnabled = _currentState.Visual.AnimationsDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Visual",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                // Storage
                StorageOptimizations.Clear();
                if (_currentState.Storage.IsSsd)
                {
                    StorageOptimizations.Add(new OptimizationItem
                    {
                        Id = "storage_trim",
                        Name = "TRIM for SSD",
                        Description = "Enables TRIM command for SSD health and performance",
                        IsEnabled = _currentState.Storage.TrimEnabled,
                        Risk = OptimizationRisk.Low,
                        Category = "Storage",
                        OnToggleAsync = ToggleOptimizationAsync
                    });
                }
                StorageOptimizations.Add(new OptimizationItem
                {
                    Id = "storage_last_access",
                    Name = "Disable Last Access Timestamps",
                    Description = "Disables NTFS last access time updates for lower disk activity",
                    IsEnabled = _currentState.Storage.LastAccessDisabled,
                    Risk = OptimizationRisk.Low,
                    Category = "Storage",
                    OnToggleAsync = ToggleOptimizationAsync
                });
                StorageOptimizations.Add(new OptimizationItem
                {
                    Id = "storage_8dot3",
                    Name = "Disable 8.3 Names",
                    Description = "Disables legacy short filename creation",
                    IsEnabled = _currentState.Storage.ShortNamesDisabled,
                    Risk = OptimizationRisk.Low,
                    Warning = "May break very old software",
                    Category = "Storage",
                    OnToggleAsync = ToggleOptimizationAsync
                });

                ApplyRiskAssessmentMetadata(PowerOptimizations);
                ApplyRiskAssessmentMetadata(ServiceOptimizations);
                ApplyRiskAssessmentMetadata(NetworkOptimizations);
                ApplyRiskAssessmentMetadata(InputOptimizations);
                ApplyRiskAssessmentMetadata(VisualOptimizations);
                ApplyRiskAssessmentMetadata(StorageOptimizations);
                RaiseCategoryCommandStates();
            });
        }

        private async Task ToggleOptimizationAsync(OptimizationItem item, bool desiredState)
        {
            try
            {
                if (item.IsApplying)
                {
                    return;
                }

                item.IsApplying = true;
                SetStatusAction(desiredState ? $"Applying {item.Name}" : $"Reverting {item.Name}");
                
                OptimizationResult result;
                
                if (desiredState)
                {
                    result = await _optimizerService.ApplyOptimizationAsync(item.Id);
                }
                else
                {
                    result = await _optimizerService.RevertOptimizationAsync(item.Id);
                }
                
                if (result.Success)
                {
                    item.IsEnabled = desiredState;
                    SetStatusDone($"{item.Name} {(item.IsEnabled ? "enabled" : "disabled")}");
                    
                    if (result.RequiresReboot)
                    {
                        SetStatusDone($"{item.Name} {(item.IsEnabled ? "enabled" : "disabled")} (reboot required)");
                    }
                }
                else
                {
                    SetStatusFailed(result.ErrorMessage);
                }
                
                // Refresh authoritative state without blocking the whole page for a single-item toggle.
                await RefreshStateAsync(showOverlay: false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to toggle optimization: {ex.Message}");
                SetStatusFailed(ex.Message);
            }
            finally
            {
                item.IsApplying = false;
            }
        }

        private async Task ApplyGamingMaxAsync()
        {
            var msgResult = MessageBox.Show(
                "This will apply ALL gaming optimizations for maximum performance.\n\n" +
                "A system restore point will be created before making changes.\n\n" +
                "Continue?",
                "Apply Gaming Maximum",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (msgResult != MessageBoxResult.Yes) return;
            
            try
            {
                IsLoading = true;
                var results = await _optimizerService.ApplyGamingMaximumAsync();
                
                var successCount = results.Count(r => r.Success);
                var failCount = results.Count - successCount;
                var rebootNeeded = results.Any(r => r.RequiresReboot && r.Success);
                
                SetStatusDone($"Applied {successCount}/{results.Count} optimizations");
                
                if (failCount > 0)
                {
                    SetStatusDone($"Applied {successCount}/{results.Count} optimizations ({failCount} failed)");
                }
                
                if (rebootNeeded)
                {
                    var reboot = MessageBox.Show(
                        "Some changes require a reboot to take effect.\n\nReboot now?",
                        "Reboot Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (reboot == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                    }
                }
                
                await RefreshStateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply gaming max: {ex.Message}");
                SetStatusFailed(ex.Message);
                MessageBox.Show($"Failed to apply optimizations:\n{ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ApplyCategoryAsync(string categoryName, ObservableCollection<OptimizationItem> optimizations)
        {
            var pendingItems = optimizations.Where(item => !item.IsEnabled).ToList();
            if (!pendingItems.Any())
            {
                SetStatusDone($"{categoryName} is already fully applied");
                return;
            }

            var confirmation = MessageBox.Show(
                $"This will apply {pendingItems.Count} optimization(s) in {categoryName}.\n\nContinue?",
                $"Apply {categoryName}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                IsLoading = true;
                OperationTotalSteps = pendingItems.Count;

                var successCount = 0;
                var failCount = 0;

                for (var index = 0; index < pendingItems.Count; index++)
                {
                    var item = pendingItems[index];
                    OperationCurrentStep = index + 1;
                    OperationProgressLabel = $"Applying {item.Name}";
                    SetStatusAction($"Applying {item.Name}");

                    var result = await _optimizerService.ApplyOptimizationAsync(item.Id);
                    if (result.Success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                if (failCount > 0)
                {
                    SetStatusDone($"Applied {successCount}/{pendingItems.Count} items in {categoryName} ({failCount} failed)");
                }
                else
                {
                    SetStatusDone($"Applied {successCount} {categoryName} optimization(s)");
                }

                await RefreshStateAsync(showOverlay: false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply {categoryName}: {ex.Message}");
                SetStatusFailed(ex.Message);
            }
            finally
            {
                ClearOperationProgress();
                IsLoading = false;
            }
        }

        private async Task ApplyBalancedAsync()
        {
            var msgResult = MessageBox.Show(
                "This will apply recommended optimizations with minimal risk.\n\n" +
                "A system restore point will be created before making changes.\n\n" +
                "Continue?",
                "Apply Balanced Optimizations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            
            if (msgResult != MessageBoxResult.Yes) return;
            
            try
            {
                IsLoading = true;
                var results = await _optimizerService.ApplyBalancedAsync();
                
                var successCount = results.Count(r => r.Success);
                SetStatusDone($"Applied {successCount}/{results.Count} optimizations");
                
                await RefreshStateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply balanced: {ex.Message}");
                SetStatusFailed(ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RevertAllAsync()
        {
            var msgResult = MessageBox.Show(
                "This will revert ALL optimizations to Windows defaults.\n\n" +
                "Your backed-up settings will be restored.\n\n" +
                "Continue?",
                "Revert All Optimizations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (msgResult != MessageBoxResult.Yes) return;
            
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                IsLoading = true;
                var results = await _optimizerService.RevertAllAsync(cts.Token);
                
                var successCount = results.Count(r => r.Success);
                SetStatusDone($"Reverted {successCount}/{results.Count} optimizations");
                
                await RefreshStateAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.Error("Revert operation timed out after 60 seconds");
                SetStatusFailed("Revert timed out — check system state manually");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to revert: {ex.Message}");
                SetStatusFailed(ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetStatusAction(string action)
        {
            StatusMessage = $"{action.Trim().TrimEnd('.')}...";
        }

        private void SetStatusDone(string? details = null)
        {
            StatusMessage = string.IsNullOrWhiteSpace(details) ? "Done" : $"Done: {details}";
        }

        private void SetStatusFailed(string? reason)
        {
            StatusMessage = $"Failed: {reason}";
        }

        private void HandleServiceStatus(string status)
        {
            StatusMessage = status;
            UpdateOperationProgressFromStatus(status);
        }

        private void UpdateOperationProgressFromStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            var match = Regex.Match(status, @"^\[(\d+)(?:-\d+)?\/(\d+)\]\s*(.+)$");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var currentStep) &&
                int.TryParse(match.Groups[2].Value, out var totalSteps))
            {
                OperationCurrentStep = currentStep;
                OperationTotalSteps = totalSteps;
                OperationProgressLabel = match.Groups[3].Value.Trim();
                return;
            }

            if (status.StartsWith("Revert complete", StringComparison.OrdinalIgnoreCase) ||
                status.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) ||
                status.StartsWith("Done", StringComparison.OrdinalIgnoreCase) ||
                status.StartsWith("Complete:", StringComparison.OrdinalIgnoreCase))
            {
                ClearOperationProgress();
            }
        }

        private void ClearOperationProgress()
        {
            OperationCurrentStep = 0;
            OperationTotalSteps = 0;
            OperationProgressLabel = string.Empty;
            RaiseCategoryCommandStates();
        }

        private async Task RunBackgroundVerificationAsync()
        {
            if (IsLoading || _currentState == null)
            {
                return;
            }

            try
            {
                var expectedState = _currentState;
                var verifiedState = await _optimizerService.VerifyStateAsync();
                LastVerifiedAt = verifiedState.LastChecked;

                var driftedOptimizations = GetDriftedOptimizations(expectedState, verifiedState).ToList();
                if (!driftedOptimizations.Any())
                {
                    VerificationSummary = "Verified: no drift detected";
                    HasStateDrift = false;
                    return;
                }

                var autoCorrectionResults = await _optimizerService.CorrectMinorDriftAsync(expectedState, verifiedState);
                if (autoCorrectionResults.Any(result => result.Success))
                {
                    verifiedState = await _optimizerService.VerifyStateAsync();
                    LastVerifiedAt = verifiedState.LastChecked;
                    driftedOptimizations = GetDriftedOptimizations(expectedState, verifiedState).ToList();
                }

                _currentState = verifiedState;
                UpdateOptimizationCollections();
                OnPropertyChanged(nameof(ActiveOptimizationCount));
                OnPropertyChanged(nameof(TotalOptimizationCount));
                OnPropertyChanged(nameof(OptimizationSummary));

                HasStateDrift = driftedOptimizations.Any();
                VerificationSummary = HasStateDrift
                    ? $"Drift detected: {string.Join(", ", driftedOptimizations.Take(3))}"
                    : $"Verified: auto-corrected {autoCorrectionResults.Count(result => result.Success)} service setting(s)";
            }
            catch (Exception ex)
            {
                _logger.Error($"Background optimizer verification failed: {ex.Message}");
                VerificationSummary = "Verification failed";
            }
        }

        private static IEnumerable<string> GetDriftedOptimizations(OptimizationState expected, OptimizationState actual)
        {
            if (expected.Power.UltimatePerformancePlan != actual.Power.UltimatePerformancePlan) yield return "Ultimate Performance Plan";
            if (expected.Power.HardwareGpuScheduling != actual.Power.HardwareGpuScheduling) yield return "Hardware GPU Scheduling";
            if (expected.Power.GameModeEnabled != actual.Power.GameModeEnabled) yield return "Game Mode";
            if (expected.Power.ForegroundPriority != actual.Power.ForegroundPriority) yield return "Foreground Priority Boost";

            if (expected.Services.TelemetryDisabled != actual.Services.TelemetryDisabled) yield return "Disable Telemetry";
            if (expected.Services.SysMainDisabled != actual.Services.SysMainDisabled) yield return "Disable SysMain";
            if (expected.Services.SearchIndexingDisabled != actual.Services.SearchIndexingDisabled) yield return "Disable Search Indexing";
            if (expected.Services.DiagTrackDisabled != actual.Services.DiagTrackDisabled) yield return "Disable Connected Experiences";

            if (expected.Network.TcpNoDelay != actual.Network.TcpNoDelay) yield return "TCP No Delay";
            if (expected.Network.TcpAckFrequency != actual.Network.TcpAckFrequency) yield return "TCP ACK Optimization";
            if (expected.Network.DeliveryOptimizationDisabled != actual.Network.DeliveryOptimizationDisabled) yield return "Disable P2P Updates";
            if (expected.Network.NagleDisabled != actual.Network.NagleDisabled) yield return "Disable Nagle Algorithm";

            if (expected.Input.MouseAccelerationDisabled != actual.Input.MouseAccelerationDisabled) yield return "Disable Mouse Acceleration";
            if (expected.Input.GameDvrDisabled != actual.Input.GameDvrDisabled) yield return "Disable Game DVR";
            if (expected.Input.GameBarDisabled != actual.Input.GameBarDisabled) yield return "Disable Xbox Game Bar";
            if (expected.Input.FullscreenOptimizationsDisabled != actual.Input.FullscreenOptimizationsDisabled) yield return "Fullscreen Optimizations";

            if (expected.Visual.TransparencyDisabled != actual.Visual.TransparencyDisabled) yield return "Disable Transparency";
            if (expected.Visual.AnimationsDisabled != actual.Visual.AnimationsDisabled) yield return "Disable Animations";

            if (expected.Storage.TrimEnabled != actual.Storage.TrimEnabled) yield return "TRIM for SSD";
            if (expected.Storage.LastAccessDisabled != actual.Storage.LastAccessDisabled) yield return "Disable Last Access Timestamps";
            if (expected.Storage.ShortNamesDisabled != actual.Storage.ShortNamesDisabled) yield return "Disable 8.3 Names";
        }

        private bool CanApplyCategory(ObservableCollection<OptimizationItem> optimizations)
        {
            return !IsLoading && optimizations.Any(item => !item.IsEnabled);
        }

        private void RaiseCategoryCommandStates()
        {
            (ApplyPowerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyServicesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyNetworkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyInputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyVisualCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyStorageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private static void ApplyRiskAssessmentMetadata(IEnumerable<OptimizationItem> items)
        {
            foreach (var item in items)
            {
                var details = GetRiskAssessment(item.Id);
                item.DetailedDescription = details.DetailedDescription;
                item.AffectedRegistryKeys = details.AffectedRegistryKeys;
                item.ServiceNamesAffected = details.ServiceNamesAffected;
                item.UndoInstructions = details.UndoInstructions;
            }
        }

        private static OptimizationRiskAssessment GetRiskAssessment(string optimizationId)
        {
            return optimizationId switch
            {
                "power_ultimate_perf" => new OptimizationRiskAssessment(
                    "Switches the active Windows power plan to the highest-performance profile available so CPU and GPU boost behavior stays aggressive during gameplay.",
                    Array.Empty<string>(),
                    new[] { "powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61", "Fallback: powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" },
                    "Restore the Balanced plan with: powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e"),
                "power_gpu_scheduling" => new OptimizationRiskAssessment(
                    "Enables Hardware-Accelerated GPU Scheduling so Windows can submit GPU work with lower CPU overhead on supported systems.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode" },
                    Array.Empty<string>(),
                    "Set HwSchMode back to 0 or remove it, then reboot Windows."),
                "power_game_mode" => new OptimizationRiskAssessment(
                    "Turns on Windows Game Mode so the OS prioritizes the active game and reduces background interference.",
                    new[] { @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled" },
                    Array.Empty<string>(),
                    "Set AutoGameModeEnabled to 0 or remove the value."),
                "power_foreground_priority" => new OptimizationRiskAssessment(
                    "Raises foreground scheduling priority so the active game receives more CPU time relative to background tasks.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation" },
                    Array.Empty<string>(),
                    "Restore the default Win32PrioritySeparation value or remove the override."),
                "service_telemetry" => new OptimizationRiskAssessment(
                    "Disables Windows telemetry policy values that control how much diagnostic data the OS is allowed to collect.",
                    new[]
                    {
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry",
                        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection\AllowTelemetry"
                    },
                    Array.Empty<string>(),
                    "Remove the AllowTelemetry overrides to return to the Windows default policy."),
                "service_sysmain" => new OptimizationRiskAssessment(
                    "Stops and disables SysMain so Windows stops preloading applications in the background, which is often preferable on SSD systems.",
                    Array.Empty<string>(),
                    new[] { "SysMain service" },
                    "Set the SysMain service back to Automatic and start it from Services."),
                "service_search" => new OptimizationRiskAssessment(
                    "Stops and disables Windows Search indexing to reduce background disk and CPU usage.",
                    Array.Empty<string>(),
                    new[] { "WSearch service" },
                    "Set the Windows Search service back to Automatic and start it from Services."),
                "service_diagtrack" => new OptimizationRiskAssessment(
                    "Disables the Connected User Experiences and Telemetry service so background telemetry processing is reduced.",
                    Array.Empty<string>(),
                    new[] { "DiagTrack service" },
                    "Set the DiagTrack service back to Automatic and start it from Services."),
                "network_tcp_nodelay" => new OptimizationRiskAssessment(
                    "Forces TCP to send packets without delayed acknowledgments, reducing latency at the cost of more frequent network traffic.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpNoDelay" },
                    Array.Empty<string>(),
                    "Remove TcpNoDelay or set it back to 0."),
                "network_tcp_ack" => new OptimizationRiskAssessment(
                    "Changes TCP acknowledgment behavior so packets are acknowledged immediately instead of being batched.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpAckFrequency" },
                    Array.Empty<string>(),
                    "Remove TcpAckFrequency or restore the default behavior."),
                "network_delivery_opt" => new OptimizationRiskAssessment(
                    "Disables Delivery Optimization peer-to-peer update sharing so Windows Update no longer uploads update data to other machines.",
                    new[] { @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode" },
                    Array.Empty<string>(),
                    "Remove DODownloadMode to let Windows manage Delivery Optimization normally."),
                "network_nagle" => new OptimizationRiskAssessment(
                    "Adjusts TCP buffering behavior to minimize acknowledgment delay for latency-sensitive workloads.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TCPDelAckTicks" },
                    Array.Empty<string>(),
                    "Remove TCPDelAckTicks to restore the default stack behavior."),
                "input_mouse_accel" => new OptimizationRiskAssessment(
                    "Disables Enhanced Pointer Precision so mouse movement stays raw and 1:1, which is generally preferred for games.",
                    new[]
                    {
                        @"HKCU\Control Panel\Mouse\MouseSpeed",
                        @"HKCU\Control Panel\Mouse\MouseThreshold1",
                        @"HKCU\Control Panel\Mouse\MouseThreshold2"
                    },
                    Array.Empty<string>(),
                    "Set MouseSpeed back to 1 and restore threshold values, or re-enable pointer precision in Windows mouse settings."),
                "input_game_dvr" => new OptimizationRiskAssessment(
                    "Disables background game recording so Game DVR stops reserving resources while games are running.",
                    new[]
                    {
                        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled",
                        @"HKCU\System\GameConfigStore\GameDVR_Enabled",
                        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR"
                    },
                    Array.Empty<string>(),
                    "Remove the Game DVR policy values or set them back to enabled."),
                "input_game_bar" => new OptimizationRiskAssessment(
                    "Disables Xbox Game Bar overlays and startup prompts so they do not interrupt gameplay.",
                    new[]
                    {
                        @"HKCU\SOFTWARE\Microsoft\GameBar\UseNexusForGameBarEnabled",
                        @"HKCU\SOFTWARE\Microsoft\GameBar\ShowStartupPanel"
                    },
                    Array.Empty<string>(),
                    "Remove those Game Bar values or re-enable Xbox Game Bar in Windows Settings."),
                "input_fullscreen_opt" => new OptimizationRiskAssessment(
                    "Tunes fullscreen optimization behavior for games by adjusting Windows GameConfigStore flags.",
                    new[]
                    {
                        @"HKCU\System\GameConfigStore\GameDVR_FSEBehaviorMode",
                        @"HKCU\System\GameConfigStore\GameDVR_HonorUserFSEBehaviorMode",
                        @"HKCU\System\GameConfigStore\GameDVR_FSEBehavior"
                    },
                    Array.Empty<string>(),
                    "Remove the GameConfigStore fullscreen values to restore Windows defaults."),
                "visual_transparency" => new OptimizationRiskAssessment(
                    "Disables acrylic and transparency effects to reduce compositing overhead and visual distractions.",
                    new[] { @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency" },
                    Array.Empty<string>(),
                    "Set EnableTransparency back to 1 or re-enable Transparency Effects in Personalization settings."),
                "visual_animations" => new OptimizationRiskAssessment(
                    "Turns off common shell animations and menu delays so the desktop feels snappier and uses fewer visual effects.",
                    new[]
                    {
                        @"HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate",
                        @"HKCU\Control Panel\Desktop\MenuShowDelay",
                        @"HKCU\Control Panel\Desktop\SmoothScroll"
                    },
                    Array.Empty<string>(),
                    "Restore MinAnimate, MenuShowDelay, and SmoothScroll to their default values if you want the stock Windows UI feel back."),
                "storage_trim" => new OptimizationRiskAssessment(
                    "Enables the NTFS TRIM path so SSDs can reclaim deleted blocks efficiently and maintain performance over time.",
                    Array.Empty<string>(),
                    new[] { "fsutil behavior set DisableDeleteNotify 0" },
                    "Run fsutil behavior set DisableDeleteNotify 1 to turn the override back off."),
                "storage_last_access" => new OptimizationRiskAssessment(
                    "Disables NTFS last-access timestamp updates to reduce metadata writes on storage devices.",
                    Array.Empty<string>(),
                    new[] { "fsutil behavior set disablelastaccess 1" },
                    "Run fsutil behavior set disablelastaccess 0 to restore timestamp updates."),
                "storage_8dot3" => new OptimizationRiskAssessment(
                    "Disables legacy 8.3 short filename generation, which can slightly reduce file-system bookkeeping on modern systems.",
                    new[] { @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\NtfsDisable8dot3NameCreation" },
                    Array.Empty<string>(),
                    "Set NtfsDisable8dot3NameCreation back to 0 if older software depends on DOS-style short names."),
                _ => new OptimizationRiskAssessment(
                    "This optimization changes Windows settings that OmenCore can also revert later through the built-in restore flow.",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    null)
            };
        }
    }

    /// <summary>
    /// Represents a single optimization toggle item in the UI.
    /// </summary>
    public class OptimizationItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private bool _isApplying;
        private bool _isDetailsExpanded;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public OptimizationRisk Risk { get; set; }
        public string? Warning { get; set; }
        public string? DetailedDescription { get; set; }
        public IReadOnlyList<string> AffectedRegistryKeys { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ServiceNamesAffected { get; set; } = Array.Empty<string>();
        public string? UndoInstructions { get; set; }
        
        public Func<OptimizationItem, bool, Task>? OnToggleAsync { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public bool IsApplying
        {
            get => _isApplying;
            set { _isApplying = value; OnPropertyChanged(); }
        }

        public bool IsDetailsExpanded
        {
            get => _isDetailsExpanded;
            set { _isDetailsExpanded = value; OnPropertyChanged(); }
        }

        public string RiskText => Risk switch
        {
            OptimizationRisk.Low => "Low Risk",
            OptimizationRisk.Medium => "Medium Risk",
            OptimizationRisk.High => "High Risk",
            _ => "Unknown"
        };

        public string RiskColor => Risk switch
        {
            OptimizationRisk.Low => "#00C8C8",
            OptimizationRisk.Medium => "#FFB800",
            OptimizationRisk.High => "#FF005C",
            _ => "#808080"
        };

        public bool HasWarning => !string.IsNullOrEmpty(Warning);
        public bool HasDetailedDescription => !string.IsNullOrWhiteSpace(DetailedDescription);
        public bool HasRegistryPreview => AffectedRegistryKeys.Count > 0;
        public bool HasServicePreview => ServiceNamesAffected.Count > 0;
        public bool HasUndoInstructions => !string.IsNullOrWhiteSpace(UndoInstructions);
        public bool HasDetails => HasDetailedDescription || HasRegistryPreview || HasServicePreview || HasUndoInstructions;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task Toggle(bool desiredState)
        {
            try
            {
                if (OnToggleAsync != null)
                {
                    await OnToggleAsync(this, desiredState);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toggle failed: {ex.Message}");
            }
        }
    }

    public sealed record OptimizationRiskAssessment(
        string DetailedDescription,
        IReadOnlyList<string> AffectedRegistryKeys,
        IReadOnlyList<string> ServiceNamesAffected,
        string? UndoInstructions);
}
