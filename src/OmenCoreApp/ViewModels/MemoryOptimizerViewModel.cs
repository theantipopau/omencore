using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the Memory Optimizer section within the Optimizer tab.
    /// Provides real-time memory stats and cleaning operations.
    /// </summary>
    public class MemoryOptimizerViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly LoggingService _logger;
        private readonly MemoryOptimizerService _memoryService;
        private readonly DispatcherTimer _refreshTimer;

        // Memory info backing fields
        private long _totalPhysicalMB;
        private long _usedPhysicalMB;
        private long _availablePhysicalMB;
        private int _memoryLoadPercent;
        private long _systemCacheMB;
        private long _commitTotalMB;
        private long _commitLimitMB;
        private int _processCount;
        private int _threadCount;
        private int _handleCount;
        private long _usedPageFileMB;
        private long _totalPageFileMB;
        private long _kernelNonPagedMB;

        // State
        private bool _isCleaning;
        private string _statusMessage = "Ready";
        private string _lastCleanResult = "";
        private bool _autoCleanEnabled;
        private int _autoCleanThreshold = 80;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MemoryOptimizerViewModel(LoggingService logger)
        {
            _logger = logger;
            _memoryService = new MemoryOptimizerService(logger);

            _memoryService.StatusChanged += status =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() => StatusMessage = status);
            };

            _memoryService.CleanCompleted += result =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (result.Success)
                    {
                        LastCleanResult = $"Freed {result.FreedMB} MB  •  {result.OperationsSucceeded} ops succeeded" +
                            (result.OperationsFailed > 0 ? $"  •  {result.OperationsFailed} failed" : "") +
                            $"  •  {result.Timestamp:HH:mm:ss}";
                    }
                    else
                    {
                        LastCleanResult = $"Failed: {result.ErrorMessage}";
                    }
                });
            };

            // Commands
            CleanSafeCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.AllSafe),
                _ => !IsCleaning);
            CleanAllCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.All),
                _ => !IsCleaning);
            CleanWorkingSetsCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.WorkingSets),
                _ => !IsCleaning);
            CleanStandbyCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.StandbyList | MemoryCleanFlags.StandbyListLowPriority),
                _ => !IsCleaning);
            CleanFileCacheCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.SystemFileCache),
                _ => !IsCleaning);
            CleanModifiedPagesCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.ModifiedPageList),
                _ => !IsCleaning);
            CleanCombinePagesCommand = new RelayCommand(_ => _ = CleanMemoryAsync(MemoryCleanFlags.CombinePages),
                _ => !IsCleaning);

            // Refresh timer - update memory stats every 2 seconds
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (_, _) => RefreshMemoryInfo();
            _refreshTimer.Start();

            // Initial refresh
            RefreshMemoryInfo();
        }

        // ========== MEMORY INFO PROPERTIES ==========

        public long TotalPhysicalMB
        {
            get => _totalPhysicalMB;
            private set { _totalPhysicalMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPhysicalGB)); }
        }

        public long UsedPhysicalMB
        {
            get => _usedPhysicalMB;
            private set { _usedPhysicalMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsedPhysicalGB)); OnPropertyChanged(nameof(MemoryBarWidth)); }
        }

        public long AvailablePhysicalMB
        {
            get => _availablePhysicalMB;
            private set { _availablePhysicalMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(AvailablePhysicalGB)); }
        }

        public int MemoryLoadPercent
        {
            get => _memoryLoadPercent;
            private set { _memoryLoadPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(MemoryLoadText)); OnPropertyChanged(nameof(MemoryBarColor)); OnPropertyChanged(nameof(MemoryBarWidth)); }
        }

        public long SystemCacheMB
        {
            get => _systemCacheMB;
            private set { _systemCacheMB = value; OnPropertyChanged(); }
        }

        public long CommitTotalMB
        {
            get => _commitTotalMB;
            private set { _commitTotalMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(CommitText)); }
        }

        public long CommitLimitMB
        {
            get => _commitLimitMB;
            private set { _commitLimitMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(CommitText)); }
        }

        public int ProcessCount
        {
            get => _processCount;
            private set { _processCount = value; OnPropertyChanged(); }
        }

        public int ThreadCount
        {
            get => _threadCount;
            private set { _threadCount = value; OnPropertyChanged(); }
        }

        public int HandleCount
        {
            get => _handleCount;
            private set { _handleCount = value; OnPropertyChanged(); }
        }

        public long UsedPageFileMB
        {
            get => _usedPageFileMB;
            private set { _usedPageFileMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageFileText)); }
        }

        public long TotalPageFileMB
        {
            get => _totalPageFileMB;
            private set { _totalPageFileMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageFileText)); }
        }

        public long KernelNonPagedMB
        {
            get => _kernelNonPagedMB;
            private set { _kernelNonPagedMB = value; OnPropertyChanged(); }
        }

        // ========== COMPUTED DISPLAY PROPERTIES ==========

        public string TotalPhysicalGB => $"{TotalPhysicalMB / 1024.0:F1} GB";
        public string UsedPhysicalGB => $"{UsedPhysicalMB / 1024.0:F1} GB";
        public string AvailablePhysicalGB => $"{AvailablePhysicalMB / 1024.0:F1} GB";
        public string MemoryLoadText => $"{MemoryLoadPercent}%";
        public string CommitText => $"{CommitTotalMB / 1024.0:F1} / {CommitLimitMB / 1024.0:F1} GB";
        public string PageFileText => $"{UsedPageFileMB / 1024.0:F1} / {TotalPageFileMB / 1024.0:F1} GB";

        /// <summary>
        /// Returns a proportional width for the memory usage bar (0.0 to 1.0 scale).
        /// Used as a multiplier against the actual bar container width.
        /// </summary>
        public double MemoryBarWidth => Math.Clamp(MemoryLoadPercent / 100.0, 0.0, 1.0);

        /// <summary>
        /// Color of the memory bar based on usage level.
        /// </summary>
        public string MemoryBarColor => MemoryLoadPercent switch
        {
            >= 90 => "#FF005C",  // Critical - Red
            >= 75 => "#FFB800",  // Warning - Amber 
            _ => "#00C8C8"       // Normal - Teal
        };

        // ========== STATE PROPERTIES ==========

        public bool IsCleaning
        {
            get => _isCleaning;
            private set
            {
                _isCleaning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotCleaning));
                
                // Update CanExecute for all clean commands
                (CleanSafeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanWorkingSetsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanStandbyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanFileCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanModifiedPagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CleanCombinePagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsNotCleaning => !_isCleaning;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string LastCleanResult
        {
            get => _lastCleanResult;
            private set { _lastCleanResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLastCleanResult)); }
        }

        public bool HasLastCleanResult => !string.IsNullOrEmpty(_lastCleanResult);

        public bool AutoCleanEnabled
        {
            get => _autoCleanEnabled;
            set
            {
                _autoCleanEnabled = value;
                _memoryService.SetAutoClean(value, AutoCleanThreshold);
                OnPropertyChanged();
            }
        }

        public int AutoCleanThreshold
        {
            get => _autoCleanThreshold;
            set
            {
                _autoCleanThreshold = Math.Clamp(value, 50, 95);
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoCleanThresholdText));
                if (_autoCleanEnabled)
                {
                    _memoryService.SetAutoClean(true, _autoCleanThreshold);
                }
            }
        }

        public string AutoCleanThresholdText => $"{AutoCleanThreshold}%";

        // ========== COMMANDS ==========

        public ICommand CleanSafeCommand { get; }
        public ICommand CleanAllCommand { get; }
        public ICommand CleanWorkingSetsCommand { get; }
        public ICommand CleanStandbyCommand { get; }
        public ICommand CleanFileCacheCommand { get; }
        public ICommand CleanModifiedPagesCommand { get; }
        public ICommand CleanCombinePagesCommand { get; }

        // ========== METHODS ==========

        private void RefreshMemoryInfo()
        {
            try
            {
                var info = _memoryService.GetMemoryInfo();

                TotalPhysicalMB = info.TotalPhysicalMB;
                UsedPhysicalMB = info.UsedPhysicalMB;
                AvailablePhysicalMB = info.AvailablePhysicalMB;
                MemoryLoadPercent = info.MemoryLoadPercent;
                SystemCacheMB = info.SystemCacheMB;
                CommitTotalMB = info.CommitTotalMB;
                CommitLimitMB = info.CommitLimitMB;
                ProcessCount = info.ProcessCount;
                ThreadCount = info.ThreadCount;
                HandleCount = info.HandleCount;
                UsedPageFileMB = info.UsedPageFileMB;
                TotalPageFileMB = info.TotalPageFileMB;
                KernelNonPagedMB = info.KernelNonPagedMB;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refresh memory info: {ex.Message}");
            }
        }

        private async Task CleanMemoryAsync(MemoryCleanFlags flags)
        {
            try
            {
                IsCleaning = true;
                StatusMessage = "Cleaning memory...";

                var result = await _memoryService.CleanMemoryAsync(flags);

                if (result.Success)
                {
                    StatusMessage = $"Freed {result.FreedMB} MB";
                }
                else
                {
                    StatusMessage = $"Failed: {result.ErrorMessage}";
                }

                // Immediately refresh stats
                RefreshMemoryInfo();
            }
            catch (Exception ex)
            {
                _logger.Error($"Memory clean failed: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsCleaning = false;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
            _memoryService.Dispose();
        }
    }
}
