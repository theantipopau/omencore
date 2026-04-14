using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;
using OmenCore.Services.Diagnostics;

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
        private readonly ConfigurationService? _configService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Queue<MemoryHistorySample> _memoryHistory = new();
        private const string RefreshTimerRegistryName = "MemoryOptimizerRefresh";

        public ObservableCollection<ProcessMemoryInfo> TopProcesses { get; } = new();
        public ObservableCollection<string> ExcludedProcesses { get; } = new();

        // Memory info backing fields
        private long _totalPhysicalMB;
        private long _usedPhysicalMB;
        private long _availablePhysicalMB;
        private int _memoryLoadPercent;
        private long _systemCacheMB;
        private long _standbyListMB;
        private long _modifiedPageListMB;
        private long? _compressedMemoryMB;
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
        private bool _isPageActive = true;
        private string _statusMessage = "Done";
        private string _lastCleanResult = "";
        private bool _autoCleanEnabled;
        public ICommand CopyLastCleanCommand { get; }
        private int _autoCleanThreshold = 80;
        private int _autoCleanCheckIntervalSeconds = 30;
        private bool _intervalCleanEnabled;
        private int _cleanEveryMinutes = 10;
        private string _selectedProfile = "Balanced";
        private string _selectedAutoCleanProfile = "Balanced";
        private bool _applyingAutoCleanProfile;
        private bool _memoryCompressionSupported = true;
        private bool _memoryCompressionEnabled;
        private string _newExcludedProcessName = string.Empty;
        private string? _selectedExcludedProcess;
        private string _cleanupPreviewText = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public MemoryOptimizerViewModel(LoggingService logger, ConfigurationService? configService = null)
        {
            _logger = logger;
            _configService = configService;
            _memoryService = new MemoryOptimizerService(logger);

            _memoryService.StatusChanged += status =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() => SetStatusAction(status.TrimEnd('.')));
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
                        SetStatusDone($"Freed {result.FreedMB} MB");
                    }
                    else
                    {
                        LastCleanResult = $"Failed: {result.ErrorMessage}";
                        SetStatusFailed(result.ErrorMessage);
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

            CleanWithProfileCommand = new RelayCommand(_ => _ = CleanMemoryAsync(GetProfileFlags(_selectedProfile)),
                _ => !IsCleaning);

            AddExcludedProcessCommand = new RelayCommand(_ => AddExcludedProcess(),
                _ => !string.IsNullOrWhiteSpace(NewExcludedProcessName));
            RemoveExcludedProcessCommand = new RelayCommand(_ => RemoveExcludedProcess(),
                _ => !string.IsNullOrWhiteSpace(SelectedExcludedProcess));
            ToggleMemoryCompressionCommand = new RelayCommand(_ => ToggleMemoryCompression(),
                _ => MemoryCompressionSupported && !IsCleaning);
            CopyProcessNameCommand = new RelayCommand(param => CopyProcessName(param as ProcessMemoryInfo),
                param => param is ProcessMemoryInfo info && !string.IsNullOrWhiteSpace(info.ProcessName));
            OpenProcessLocationCommand = new RelayCommand(param => OpenProcessLocation(param as ProcessMemoryInfo),
                param => param is ProcessMemoryInfo info && !string.IsNullOrWhiteSpace(info.ExecutablePath));

            CopyLastCleanCommand = new RelayCommand(_ =>
            {
                try { Clipboard.SetText(LastCleanResult); }
                catch { _logger.Warn("Clipboard unavailable for CopyLastCleanResult"); }
            },
            _ => !string.IsNullOrEmpty(LastCleanResult));

            // Refresh timer: 2s when page is active, 30s when not visible
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (_, _) => RefreshMemoryInfo();
            _refreshTimer.Start();
                BackgroundTimerRegistry.Register(
                RefreshTimerRegistryName,
                "MemoryOptimizerViewModel",
                "Refreshes memory optimizer telemetry while the page is visible",
                2000,
                    BackgroundTimerTier.VisibleOnly);

            // Initial refresh
            RefreshMemoryInfo();
            RefreshMemoryCompressionState();
            
            // Restore persisted memory optimizer settings
            RestorePersistedSettings();
        }
        
        /// <summary>
        /// Load saved memory auto-clean settings from config file.
        /// </summary>
        private void RestorePersistedSettings()
        {
            var config = _configService?.Config;
            if (config == null) return;
            
            try
            {
                var savedProfile = string.IsNullOrWhiteSpace(config.MemoryAutoCleanProfile)
                    ? "Balanced"
                    : config.MemoryAutoCleanProfile;
                if (!AutoCleanProfileOptions.Contains(savedProfile, StringComparer.OrdinalIgnoreCase))
                {
                    savedProfile = "Balanced";
                }
                SelectedAutoCleanProfile = savedProfile;

                if (config.MemoryAutoCleanEnabled)
                {
                    _autoCleanThreshold = Math.Clamp(config.MemoryAutoCleanThreshold, 50, 95);
                    _autoCleanEnabled = true;
                    _memoryService.SetAutoCleanProfile(ParseAutoCleanProfile(_selectedAutoCleanProfile));
                    _memoryService.SetAutoClean(true, _autoCleanThreshold);
                    OnPropertyChanged(nameof(AutoCleanEnabled));
                    OnPropertyChanged(nameof(AutoCleanThreshold));
                    OnPropertyChanged(nameof(AutoCleanThresholdText));
                    OnPropertyChanged(nameof(AutoCleanCheckIntervalText));
                    _logger.Info($"Restored memory auto-clean: threshold={_autoCleanThreshold}%");
                }
                
                if (config.MemoryIntervalCleanEnabled)
                {
                    _cleanEveryMinutes = Math.Clamp(config.MemoryIntervalCleanMinutes, 1, 120);
                    _intervalCleanEnabled = true;
                    _memoryService.SetIntervalClean(true, _cleanEveryMinutes);
                    OnPropertyChanged(nameof(IntervalCleanEnabled));
                    OnPropertyChanged(nameof(CleanEveryMinutes));
                    OnPropertyChanged(nameof(CleanEveryMinutesText));
                    _logger.Info($"Restored memory interval clean: every {_cleanEveryMinutes} min");
                }

                if (config.MemoryExcludedProcesses != null && config.MemoryExcludedProcesses.Count > 0)
                {
                    ExcludedProcesses.Clear();
                    foreach (var processName in config.MemoryExcludedProcesses
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(NormalizeProcessName)
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ExcludedProcesses.Add(processName);
                    }
                }
                else
                {
                    ExcludedProcesses.Clear();
                    foreach (var defaultName in _memoryService.ExcludedProcessNames.OrderBy(n => n))
                    {
                        ExcludedProcesses.Add(defaultName);
                    }
                }

                _memoryService.SetExcludedProcessNames(ExcludedProcesses);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to restore memory optimizer settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Persist current memory auto-clean settings to config file.
        /// </summary>
        private void PersistSettings()
        {
            var config = _configService?.Config;
            if (config == null) return;
            
            try
            {
                config.MemoryAutoCleanEnabled = _autoCleanEnabled;
                config.MemoryAutoCleanThreshold = _autoCleanThreshold;
                config.MemoryAutoCleanProfile = _selectedAutoCleanProfile;
                config.MemoryIntervalCleanEnabled = _intervalCleanEnabled;
                config.MemoryIntervalCleanMinutes = _cleanEveryMinutes;
                config.MemoryExcludedProcesses = ExcludedProcesses.ToList();
                _configService!.Save(config);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to persist memory optimizer settings: {ex.Message}");
            }
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

        public long StandbyListMB
        {
            get => _standbyListMB;
            private set { _standbyListMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(StandbyListText)); }
        }

        public long ModifiedPageListMB
        {
            get => _modifiedPageListMB;
            private set { _modifiedPageListMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedPageListText)); }
        }

        public long? CompressedMemoryMB
        {
            get => _compressedMemoryMB;
            private set { _compressedMemoryMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompressedMemoryText)); }
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
        public string StandbyListText => $"{StandbyListMB} MB";
        public string ModifiedPageListText => $"{ModifiedPageListMB} MB";
        public string CompressedMemoryText => CompressedMemoryMB.HasValue ? $"{CompressedMemoryMB.Value} MB" : (MemoryCompressionEnabled ? "Enabled" : "Unavailable");
        public string PhysicalMemorySummary => $"Used {UsedPhysicalGB} • Available {AvailablePhysicalGB} • Cache {SystemCacheMB / 1024.0:F1} GB";
        public string VirtualMemorySummary => $"Commit {CommitText} • Page file {PageFileText}";
        public IEnumerable<double> RecentMemoryLoadHistory => _memoryHistory.Select(sample => (double)sample.LoadPercent);
        public bool HasMemoryHistory => _memoryHistory.Count >= 3;
        public string MemoryTrendSummary
        {
            get
            {
                if (_memoryHistory.Count == 0)
                {
                    return "No history collected yet.";
                }

                var loads = _memoryHistory.Select(sample => sample.LoadPercent).ToArray();
                var min = loads.Min();
                var max = loads.Max();
                var avg = loads.Average();
                var span = _memoryHistory.Last().Timestamp - _memoryHistory.First().Timestamp;
                return $"{span.TotalMinutes:F0} min history • avg {avg:F0}% • low {min}% • high {max}%";
            }
        }

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
                (CleanWithProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ToggleMemoryCompressionCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                _memoryService.SetAutoCleanProfile(ParseAutoCleanProfile(_selectedAutoCleanProfile));
                _memoryService.SetAutoClean(value, AutoCleanThreshold);
                OnPropertyChanged();
                PersistSettings();
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
                if (_applyingAutoCleanProfile)
                {
                    return;
                }
                if (_autoCleanEnabled)
                {
                    _memoryService.SetAutoClean(true, _autoCleanThreshold);
                }
                PersistSettings();
            }
        }

        public string AutoCleanThresholdText => $"{AutoCleanThreshold}%";

        public string[] AutoCleanProfileOptions => new[] { "Aggressive", "Balanced", "Conservative", "OffPeakOnly", "Manual" };

        public string SelectedAutoCleanProfile
        {
            get => _selectedAutoCleanProfile;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Balanced" : value.Trim();
                if (_selectedAutoCleanProfile == normalized)
                {
                    return;
                }

                _selectedAutoCleanProfile = normalized;
                OnPropertyChanged();
                ApplyAutoCleanProfile(normalized);
                PersistSettings();
            }
        }

        public string AutoCleanCheckIntervalText => $"Every {_autoCleanCheckIntervalSeconds} sec";

        public bool MemoryCompressionSupported
        {
            get => _memoryCompressionSupported;
            private set { _memoryCompressionSupported = value; OnPropertyChanged(); }
        }

        public bool MemoryCompressionEnabled
        {
            get => _memoryCompressionEnabled;
            private set
            {
                _memoryCompressionEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MemoryCompressionStatusText));
            }
        }

        public string MemoryCompressionStatusText => !MemoryCompressionSupported
            ? "Memory compression status unavailable"
            : (MemoryCompressionEnabled ? "Enabled" : "Disabled");

        public string NewExcludedProcessName
        {
            get => _newExcludedProcessName;
            set
            {
                if (_newExcludedProcessName == value)
                {
                    return;
                }

                _newExcludedProcessName = value;
                OnPropertyChanged();
                (AddExcludedProcessCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string? SelectedExcludedProcess
        {
            get => _selectedExcludedProcess;
            set
            {
                _selectedExcludedProcess = value;
                OnPropertyChanged();
                (RemoveExcludedProcessCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IntervalCleanEnabled
        {
            get => _intervalCleanEnabled;
            set
            {
                _intervalCleanEnabled = value;
                _memoryService.SetIntervalClean(value, CleanEveryMinutes);
                OnPropertyChanged();
                PersistSettings();
            }
        }

        public int CleanEveryMinutes
        {
            get => _cleanEveryMinutes;
            set
            {
                _cleanEveryMinutes = Math.Clamp(value, 1, 120);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CleanEveryMinutesText));
                if (_intervalCleanEnabled)
                {
                    _memoryService.SetIntervalClean(true, _cleanEveryMinutes);
                }
                PersistSettings();
            }
        }

        public string CleanEveryMinutesText => $"Every {CleanEveryMinutes} min";

        /// <summary>
        /// Gets or sets the selected memory cleaning profile (Conservative, Balanced, or Aggressive).
        /// </summary>
        public string SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    _selectedProfile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsConservative));
                    OnPropertyChanged(nameof(IsBalanced));
                    OnPropertyChanged(nameof(IsAggressive));
                    UpdateCleanupPreview();
                }
            }
        }

        public bool IsConservative
        {
            get => _selectedProfile == "Conservative";
            set { if (value) SelectedProfile = "Conservative"; }
        }

        public bool IsBalanced
        {
            get => _selectedProfile == "Balanced";
            set { if (value) SelectedProfile = "Balanced"; }
        }

        public bool IsAggressive
        {
            get => _selectedProfile == "Aggressive";
            set { if (value) SelectedProfile = "Aggressive"; }
        }

        public string CleanupPreviewText
        {
            get => _cleanupPreviewText;
            set { _cleanupPreviewText = value; OnPropertyChanged(); }
        }

        // ========== COMMANDS ==========

        public ICommand CleanSafeCommand { get; }
        public ICommand CleanAllCommand { get; }
        public ICommand CleanWorkingSetsCommand { get; }
        public ICommand CleanStandbyCommand { get; }
        public ICommand CleanFileCacheCommand { get; }
        public ICommand CleanModifiedPagesCommand { get; }
        public ICommand CleanCombinePagesCommand { get; }
        public ICommand CleanWithProfileCommand { get; }
        public ICommand AddExcludedProcessCommand { get; }
        public ICommand RemoveExcludedProcessCommand { get; }
        public ICommand ToggleMemoryCompressionCommand { get; }
        public ICommand CopyProcessNameCommand { get; }
        public ICommand OpenProcessLocationCommand { get; }

        // ========== METHODS ==========

        /// <summary>
        /// Maps a profile name to the corresponding MemoryCleanFlags.
        /// Conservative: WorkingSets only
        /// Balanced: WorkingSets, SystemFileCache, StandbyList
        /// Aggressive: AllSafe (all safe operations)
        /// </summary>
        private MemoryCleanFlags GetProfileFlags(string profile) => profile switch
        {
            "Conservative" => MemoryCleanFlags.WorkingSets,
            "Balanced" => MemoryCleanFlags.WorkingSets | MemoryCleanFlags.SystemFileCache | MemoryCleanFlags.StandbyList | MemoryCleanFlags.StandbyListLowPriority,
            "Aggressive" => MemoryCleanFlags.AllSafe,
            _ => MemoryCleanFlags.AllSafe // Default fallback to Balanced
        };

        /// <summary>
        /// Updates the cleanup preview text based on the current profile selection.
        /// </summary>
        private void UpdateCleanupPreview()
        {
            try
            {
                var flags = GetProfileFlags(_selectedProfile);
                var preview = _memoryService.PreviewMemoryCleaning(flags);
                CleanupPreviewText = $"This profile will free approximately {preview.EstimatedFreeMB} MB";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to update cleanup preview: {ex.Message}");
                CleanupPreviewText = "";
            }
        }

        private void ApplyAutoCleanProfile(string profileName)
        {
            var profile = ParseAutoCleanProfile(profileName);

            _memoryService.SetAutoCleanProfile(profile);

            _applyingAutoCleanProfile = true;
            try
            {
                if (profile != MemoryAutoCleanProfile.Manual)
                {
                    var (_, thresholdPercent) = GetAutoCleanProfileSettings(profile);
                    _autoCleanThreshold = thresholdPercent;
                    OnPropertyChanged(nameof(AutoCleanThreshold));
                    OnPropertyChanged(nameof(AutoCleanThresholdText));
                }
            }
            finally
            {
                _applyingAutoCleanProfile = false;
            }

            var (checkSeconds, _) = GetAutoCleanProfileSettings(profile);
            if (profile == MemoryAutoCleanProfile.Manual)
            {
                checkSeconds = _memoryService.AutoCleanCheckSeconds;
            }

            _autoCleanCheckIntervalSeconds = checkSeconds;
            OnPropertyChanged(nameof(AutoCleanCheckIntervalText));

            if (_autoCleanEnabled)
            {
                _memoryService.SetAutoClean(true, _autoCleanThreshold);
            }
        }

        private static (int CheckSeconds, int ThresholdPercent) GetAutoCleanProfileSettings(MemoryAutoCleanProfile profile)
        {
            return profile switch
            {
                MemoryAutoCleanProfile.Aggressive => (10, 75),
                MemoryAutoCleanProfile.Balanced => (30, 80),
                MemoryAutoCleanProfile.Conservative => (60, 85),
                MemoryAutoCleanProfile.OffPeakOnly => (300, 90),
                _ => (30, 80)
            };
        }

        private static MemoryAutoCleanProfile ParseAutoCleanProfile(string profileName)
        {
            if (Enum.TryParse<MemoryAutoCleanProfile>(profileName, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return MemoryAutoCleanProfile.Balanced;
        }

        private void AddExcludedProcess()
        {
            var normalized = NormalizeProcessName(NewExcludedProcessName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (ExcludedProcesses.Any(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            ExcludedProcesses.Add(normalized);
            ResortExcludedProcesses();
            _memoryService.SetExcludedProcessNames(ExcludedProcesses);
            NewExcludedProcessName = string.Empty;
            PersistSettings();
        }

        private void RemoveExcludedProcess()
        {
            if (string.IsNullOrWhiteSpace(SelectedExcludedProcess))
            {
                return;
            }

            var toRemove = SelectedExcludedProcess;
            ExcludedProcesses.Remove(toRemove);
            SelectedExcludedProcess = null;
            _memoryService.SetExcludedProcessNames(ExcludedProcesses);
            PersistSettings();
        }

        private void ResortExcludedProcesses()
        {
            var sorted = ExcludedProcesses.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            ExcludedProcesses.Clear();
            foreach (var name in sorted)
            {
                ExcludedProcesses.Add(name);
            }
        }

        private static string NormalizeProcessName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return string.Empty;
            }

            var normalized = processName.Trim();
            if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^4];
            }

            return normalized;
        }

        private void RefreshMemoryCompressionState()
        {
            var compressionEnabled = _memoryService.GetMemoryCompressionEnabled();
            if (!compressionEnabled.HasValue)
            {
                MemoryCompressionSupported = false;
                OnPropertyChanged(nameof(MemoryCompressionStatusText));
                return;
            }

            MemoryCompressionSupported = true;
            MemoryCompressionEnabled = compressionEnabled.Value;
            OnPropertyChanged(nameof(MemoryCompressionStatusText));
            (ToggleMemoryCompressionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ToggleMemoryCompression()
        {
            if (!MemoryCompressionSupported)
            {
                return;
            }

            var target = !MemoryCompressionEnabled;
            SetStatusAction(target ? "Enabling memory compression" : "Disabling memory compression");
            var success = _memoryService.SetMemoryCompressionEnabled(target);
            if (!success)
            {
                SetStatusFailed("Failed to change memory compression state");
                return;
            }

            MemoryCompressionEnabled = target;
            OnPropertyChanged(nameof(MemoryCompressionStatusText));
            SetStatusDone($"Memory compression {(target ? "enabled" : "disabled")}");
        }

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
                StandbyListMB = info.StandbyListMB;
                ModifiedPageListMB = info.ModifiedPageListMB;
                CompressedMemoryMB = info.CompressedMemoryMB;
                CommitTotalMB = info.CommitTotalMB;
                CommitLimitMB = info.CommitLimitMB;
                ProcessCount = info.ProcessCount;
                ThreadCount = info.ThreadCount;
                HandleCount = info.HandleCount;
                UsedPageFileMB = info.UsedPageFileMB;
                TotalPageFileMB = info.TotalPageFileMB;
                KernelNonPagedMB = info.KernelNonPagedMB;

                AddMemoryHistorySample(info.MemoryLoadPercent);
                OnPropertyChanged(nameof(PhysicalMemorySummary));
                OnPropertyChanged(nameof(VirtualMemorySummary));

                // Update top memory-consuming processes
                UpdateTopMemoryHogs();

                // Update cleanup preview
                UpdateCleanupPreview();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to refresh memory info: {ex.Message}");
            }
        }

        /// <summary>
        /// Notify the viewmodel whether its page is the active/visible view.
        /// When inactive, the refresh timer pauses entirely to avoid hidden-page churn.
        /// </summary>
        public void SetPageActive(bool active)
        {
            if (_isPageActive == active) return;
            _isPageActive = active;
            if (active)
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(2);
                _refreshTimer.Start();
                BackgroundTimerRegistry.Register(
                    RefreshTimerRegistryName,
                    "MemoryOptimizerViewModel",
                    "Refreshes memory optimizer telemetry while the page is visible",
                    2000,
                    BackgroundTimerTier.VisibleOnly);
                RefreshMemoryInfo();
            }
            else
            {
                _refreshTimer.Stop();
                BackgroundTimerRegistry.Unregister(RefreshTimerRegistryName);
            }
        }

        private void UpdateTopMemoryHogs()
        {
            try
            {
                var hogs = _memoryService.GetTopMemoryHogs(10);

                // Diff update: avoid clear-and-rebuild when the list is stable
                // Remove stale entries
                for (int i = TopProcesses.Count - 1; i >= 0; i--)
                {
                    if (!hogs.Any(h => h.ProcessName == TopProcesses[i].ProcessName))
                        TopProcesses.RemoveAt(i);
                }

                // Insert / update in sorted order
                for (int i = 0; i < hogs.Length; i++)
                {
                    var hog = hogs[i];
                    int existingIdx = -1;
                    for (int j = 0; j < TopProcesses.Count; j++)
                    {
                        if (TopProcesses[j].ProcessName == hog.ProcessName)
                        { existingIdx = j; break; }
                    }

                    if (existingIdx < 0)
                    {
                        TopProcesses.Insert(Math.Min(i, TopProcesses.Count), hog);
                    }
                    else
                    {
                        if (existingIdx != i)
                            TopProcesses.Move(existingIdx, Math.Min(i, TopProcesses.Count - 1));
                        TopProcesses[Math.Min(i, TopProcesses.Count - 1)] = hog;
                    }
                }

                // Trim extras
                while (TopProcesses.Count > hogs.Length)
                    TopProcesses.RemoveAt(TopProcesses.Count - 1);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to update top memory hogs: {ex.Message}");
            }
        }

        private void AddMemoryHistorySample(int memoryLoadPercent)
        {
            var now = DateTime.Now;
            _memoryHistory.Enqueue(new MemoryHistorySample(now, memoryLoadPercent));

            while (_memoryHistory.Count > 0 && now - _memoryHistory.Peek().Timestamp > TimeSpan.FromMinutes(30))
            {
                _memoryHistory.Dequeue();
            }

            OnPropertyChanged(nameof(RecentMemoryLoadHistory));
            OnPropertyChanged(nameof(HasMemoryHistory));
            OnPropertyChanged(nameof(MemoryTrendSummary));
        }

        private void CopyProcessName(ProcessMemoryInfo? process)
        {
            if (process == null || string.IsNullOrWhiteSpace(process.ProcessName))
            {
                return;
            }

            try
            {
                Clipboard.SetText(process.ProcessName);
                SetStatusDone($"Copied {process.ProcessName}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to copy process name: {ex.Message}");
            }
        }

        private void OpenProcessLocation(ProcessMemoryInfo? process)
        {
            if (process == null || string.IsNullOrWhiteSpace(process.ExecutablePath))
            {
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{process.ExecutablePath}\"");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to open process location: {ex.Message}");
                SetStatusFailed("Failed to open process location");
            }
        }

        private async Task CleanMemoryAsync(MemoryCleanFlags flags)
        {
            try
            {
                IsCleaning = true;
                SetStatusAction("Cleaning memory");

                var result = await _memoryService.CleanMemoryAsync(flags);

                if (result.Success)
                {
                    SetStatusDone($"Freed {result.FreedMB} MB");
                }
                else
                {
                    SetStatusFailed(result.ErrorMessage);
                }

                // Immediately refresh stats
                RefreshMemoryInfo();
            }
            catch (Exception ex)
            {
                _logger.Error($"Memory clean failed: {ex.Message}");
                SetStatusFailed(ex.Message);
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

        public void Dispose()
        {
            _refreshTimer.Stop();
            BackgroundTimerRegistry.Unregister(RefreshTimerRegistryName);
            _memoryService.Dispose();
        }

        private sealed record MemoryHistorySample(DateTime Timestamp, int LoadPercent);
    }
}
