using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
        private string _statusMessage = "Ready";
        private OptimizationState? _currentState;
        private bool _hasUnsavedChanges;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public SystemOptimizerViewModel(LoggingService logger)
        {
            _logger = logger;
            
            // Initialize optimizer (creates its own backup service internally)
            _optimizerService = new SystemOptimizerService(logger);
            
            // Subscribe to status updates
            _optimizerService.StatusChanged += status => 
            {
                StatusMessage = status;
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
            
            // Load initial state
            _ = RefreshStateAsync();
        }

        // ========== PROPERTIES ==========

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotLoading)); }
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

        public int ActiveOptimizationCount => _currentState?.ActiveCount ?? 0;
        public int TotalOptimizationCount => _currentState?.TotalCount ?? 0;

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

        // ========== METHODS ==========

        public async Task RefreshStateAsync(bool showOverlay = true)
        {
            try
            {
                if (showOverlay)
                {
                    IsLoading = true;
                }

                StatusMessage = showOverlay ? "Scanning system state..." : "Refreshing optimizer state...";
                
                _currentState = await _optimizerService.GetCurrentStateAsync();
                
                UpdateOptimizationCollections();
                
                OnPropertyChanged(nameof(ActiveOptimizationCount));
                OnPropertyChanged(nameof(TotalOptimizationCount));
                OnPropertyChanged(nameof(OptimizationSummary));
                
                StatusMessage = "Ready";
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refresh optimization state: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
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
                StatusMessage = desiredState ?
                    $"Applying {item.Name}..." :
                    $"Reverting {item.Name}...";
                
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
                    StatusMessage = $"{item.Name} {(item.IsEnabled ? "enabled" : "disabled")}";
                    
                    if (result.RequiresReboot)
                    {
                        StatusMessage += " (reboot required)";
                    }
                }
                else
                {
                    StatusMessage = $"Failed: {result.ErrorMessage}";
                }
                
                // Refresh authoritative state without blocking the whole page for a single-item toggle.
                await RefreshStateAsync(showOverlay: false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to toggle optimization: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
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
                
                StatusMessage = $"Applied {successCount}/{results.Count} optimizations";
                
                if (failCount > 0)
                {
                    StatusMessage += $" ({failCount} failed)";
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
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to apply optimizations:\n{ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
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
                StatusMessage = $"Applied {successCount}/{results.Count} optimizations";
                
                await RefreshStateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply balanced: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
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
            
            try
            {
                IsLoading = true;
                var results = await _optimizerService.RevertAllAsync();
                
                var successCount = results.Count(r => r.Success);
                StatusMessage = $"Reverted {successCount}/{results.Count} optimizations";
                
                await RefreshStateAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to revert: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
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
    }

    /// <summary>
    /// Represents a single optimization toggle item in the UI.
    /// </summary>
    public class OptimizationItem : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private bool _isApplying;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public OptimizationRisk Risk { get; set; }
        public string? Warning { get; set; }
        
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async void Toggle(bool desiredState)
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
}
