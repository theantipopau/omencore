using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class SystemControlViewModel : ViewModelBase
    {
        private readonly UndervoltService _undervoltService;
        private readonly PerformanceModeService _performanceModeService;
        private readonly OmenGamingHubCleanupService _cleanupService;
        private readonly SystemRestoreService _restoreService;
        private readonly GpuSwitchService _gpuSwitchService;
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private readonly HpWmiBios? _wmiBios;
        private readonly OghServiceProxy? _oghProxy;
        private readonly SystemInfoService? _systemInfoService;
        private IMsrAccess? _msrAccess;  // Changed from WinRing0MsrAccess to IMsrAccess

        private PerformanceMode? _selectedPerformanceMode;
        private UndervoltStatus _undervoltStatus = UndervoltStatus.CreateUnknown();
        private double _requestedCoreOffset;
        private double _requestedCacheOffset;
        private bool _respectExternalUndervolt = true;
        private bool _enablePerCoreUndervolt;
        private int?[]? _requestedPerCoreOffsets;
        private bool _cleanupInProgress;
        private string _cleanupStatus = "Status: Not checked";

        public ObservableCollection<PerformanceMode> PerformanceModes { get; } = new();
        public ObservableCollection<string> OmenCleanupSteps { get; } = new();

        public PerformanceMode? SelectedPerformanceMode
        {
            get => _selectedPerformanceMode;
            set
            {
                if (_selectedPerformanceMode != value)
                {
                    _selectedPerformanceMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPerformanceModeName));  // Notify sidebar to update
                    OnPropertyChanged(nameof(IsQuietMode));
                    OnPropertyChanged(nameof(IsBalancedMode));
                    OnPropertyChanged(nameof(IsPerformanceMode));
                }
            }
        }

        public UndervoltStatus UndervoltStatus
        {
            get => _undervoltStatus;
            private set
            {
                _undervoltStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UndervoltStatusSummary));
                (ApplyUndervoltCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public double RequestedCoreOffset
        {
            get => _requestedCoreOffset;
            set
            {
                if (_requestedCoreOffset != value)
                {
                    _requestedCoreOffset = value;
                    OnPropertyChanged();
                }
            }
        }

        public double RequestedCacheOffset
        {
            get => _requestedCacheOffset;
            set
            {
                if (_requestedCacheOffset != value)
                {
                    _requestedCacheOffset = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnablePerCoreUndervolt
        {
            get => _enablePerCoreUndervolt;
            set
            {
                if (_enablePerCoreUndervolt != value)
                {
                    _enablePerCoreUndervolt = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PerCoreUndervoltVisible));
                    (ApplyUndervoltCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int?[]? RequestedPerCoreOffsets
        {
            get => _requestedPerCoreOffsets;
            set
            {
                if (_requestedPerCoreOffsets != value)
                {
                    _requestedPerCoreOffsets = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool PerCoreUndervoltVisible => EnablePerCoreUndervolt && IsUndervoltSupported;

        public ObservableCollection<PerCoreOffsetViewModel> PerCoreOffsets { get; } = new();

        public bool RespectExternalUndervolt
        {
            get => _respectExternalUndervolt;
            set
            {
                if (_respectExternalUndervolt != value)
                {
                    _respectExternalUndervolt = value;
                    OnPropertyChanged();
                    (ApplyUndervoltCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string UndervoltStatusSummary
        {
            get
            {
                if (UndervoltStatus == null)
                    return "n/a";

                if (UndervoltStatus.HasPerCoreOffsets && UndervoltStatus.CurrentPerCoreOffsetsMv != null)
                {
                    // Safely extract non-null per-core offsets and avoid nullability warnings / empty sequences
                    var values = UndervoltStatus.CurrentPerCoreOffsetsMv.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
                    var activeCores = values.Length;
                    var avgOffset = activeCores > 0 ? values.Average() : 0.0;
                    return $"Per-Core: {activeCores} cores active | Avg {avgOffset:+0;-0;0} mV | Cache {UndervoltStatus.CurrentCacheOffsetMv:+0;-0;0} mV";
                }
                else
                {
                    return $"Core {UndervoltStatus.CurrentCoreOffsetMv:+0;-0;0} mV | Cache {UndervoltStatus.CurrentCacheOffsetMv:+0;-0;0} mV";
                }
            }
        }
        
        /// <summary>
        /// Whether undervolting is supported on this system.
        /// False for AMD Ryzen CPUs that don't support Curve Optimizer, or when no MSR driver available.
        /// </summary>
        public bool IsUndervoltSupported => _undervoltService != null && !string.IsNullOrEmpty(UndervoltStatusText) && !UndervoltStatusText.Contains("not supported", StringComparison.OrdinalIgnoreCase) && !UndervoltStatusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Explanation of why undervolting is not supported on this system.
        /// </summary>
        public string UndervoltNotSupportedReason
        {
            get
            {
                if (_undervoltService == null)
                    return "Undervolt service failed to initialize. This may be due to missing driver files or insufficient permissions.";
                
                var warning = UndervoltStatus?.Warning;
                if (!string.IsNullOrEmpty(warning))
                    return warning;
                
                // Check CPU type
                var cpuVendor = Hardware.CpuUndervoltProviderFactory.DetectedVendor;
                var cpuName = Hardware.CpuUndervoltProviderFactory.CpuName;
                
                if (cpuVendor == Hardware.CpuUndervoltProviderFactory.CpuVendor.AMD)
                {
                    return $"AMD {cpuName} detected.\n\n" +
                           "AMD Ryzen CPUs use Curve Optimizer (CO) instead of traditional voltage offset undervolting. " +
                           "CO requires specific SMU (System Management Unit) commands that may not be available or working on all models.\n\n" +
                           "Supported: Ryzen 5000+ series with PawnIO driver installed.\n" +
                           "Alternative: Use Ryzen Master or BIOS Curve Optimizer settings.";
                }
                else
                {
                    return $"Intel {cpuName} detected.\n\n" +
                           "CPU voltage control is blocked by Intel's Plundervolt security mitigations (CVE-2019-11157). " +
                           "Most BIOS updates after 2020 lock MSR 0x150 (voltage offset register).\n\n" +
                           "Possible solutions:\n" +
                           "• Check if your BIOS has an option to unlock overclocking/undervolt\n" +
                           "• Some manufacturers provide BIOS updates that re-enable voltage control\n" +
                           "• Use Intel XTU if your system supports it";
                }
            }
        }
        
        public string UndervoltStatusText => UndervoltStatus?.HasExternalController == true 
            ? $"External controller detected: {UndervoltStatus?.ExternalController ?? "Unknown"}" 
            : (UndervoltStatus?.CurrentCoreOffsetMv != 0 || UndervoltStatus?.CurrentCacheOffsetMv != 0)
                ? $"Active: Core {UndervoltStatus?.CurrentCoreOffsetMv:+0;-0;0} mV, Cache {UndervoltStatus?.CurrentCacheOffsetMv:+0;-0;0} mV"
                : "No undervolt active";
        
        public System.Windows.Media.Brush UndervoltStatusColor => UndervoltStatus?.HasExternalController == true
            ? System.Windows.Media.Brushes.Orange
            : (UndervoltStatus?.CurrentCoreOffsetMv != 0 || UndervoltStatus?.CurrentCacheOffsetMv != 0)
                ? System.Windows.Media.Brushes.Lime
                : System.Windows.Media.Brushes.Gray;
        
        // External controller detection properties
        public bool HasExternalUndervoltController => UndervoltStatus?.HasExternalController ?? false;
        
        public string ExternalControllerName => UndervoltStatus?.ExternalController ?? "Unknown";
        
        public string ExternalControllerWarning => UndervoltStatus?.ExternalController switch
        {
            "Intel XTU" => "Intel Extreme Tuning Utility (XTU) is controlling CPU voltage settings. XTU blocks MSR (Model Specific Register) access for other applications, preventing OmenCore from applying undervolts.",
            "ThrottleStop" => "ThrottleStop is running and managing CPU voltage. It may conflict with OmenCore's undervolt settings.",
            "Intel DTT" => "Intel Dynamic Tuning Technology service is active and may be controlling voltage settings.",
            "OMEN Gaming Hub" => "OMEN Gaming Hub is managing CPU settings. While OGH is installed, some voltage controls may be handled by HP's software.",
            "HP OmenCap (DriverStore)" => "OmenCap.exe is running from Windows DriverStore. This HP component persists after OMEN Gaming Hub uninstall and blocks MSR access for undervolting.",
            _ => $"{UndervoltStatus?.ExternalController ?? "An external program"} is controlling CPU voltage settings and may conflict with OmenCore."
        };
        
        public string ExternalControllerHowToFix => UndervoltStatus?.ExternalController switch
        {
            "Intel XTU" => "1. Open Services (Win+R → services.msc)\n" +
                           "2. Find 'Intel(R) Extreme Tuning Utility' service\n" +
                           "3. Right-click → Stop, then set Startup type to 'Disabled'\n" +
                           "4. Optionally uninstall XTU from Control Panel\n" +
                           "5. Restart OmenCore",
            "ThrottleStop" => "1. Close ThrottleStop from the system tray\n" +
                              "2. Disable ThrottleStop's auto-start (uncheck 'Start Minimized')\n" +
                              "3. Restart OmenCore to take over voltage control",
            "Intel DTT" => "1. Open Services (Win+R → services.msc)\n" +
                           "2. Find 'Intel(R) Dynamic Tuning Service'\n" +
                           "3. Stop and disable the service\n" +
                           "4. Note: This may affect Intel thermal management",
            "OMEN Gaming Hub" => "1. Use OmenCore's 'Clean OMEN Gaming Hub' feature in Settings\n" +
                                 "2. Or uninstall OGH from Control Panel → Programs\n" +
                                 "3. Restart your computer after removal",
            "HP OmenCap (DriverStore)" => "OmenCap persists in Windows DriverStore after OGH uninstall:\n" +
                                          "1. Open Admin Command Prompt\n" +
                                          "2. Run: pnputil /enum-drivers | findstr /i omen\n" +
                                          "3. Note the oem##.inf for hpomencustomcapcomp\n" +
                                          "4. Run: pnputil /delete-driver oem##.inf /force\n" +
                                          "5. Reboot your computer\n" +
                                          "6. This fully removes HP's hardware access component",
            _ => "Close or disable the external application, then restart OmenCore."
        };

        public string PerformanceModeDescription => SelectedPerformanceMode?.Description ?? SelectedPerformanceMode?.Name switch
        {
            "Balanced" => "Normal mode - balanced performance",
            "Performance" => "High performance - maximum GPU wattage",
            "Eco" => "Power saving mode - reduced wattage",
            _ => "Select a performance mode"
        };
        
        /// <summary>
        /// Whether EC-level power limit control is available for performance modes.
        /// When false, performance modes only change Windows power plan and fan policy.
        /// </summary>
        public bool PerformanceModeEcControlAvailable => _performanceModeService?.EcPowerControlAvailable ?? false;
        
        /// <summary>
        /// Human-readable description of what controls are available for performance modes.
        /// </summary>
        public string PerformanceModeCapabilities => _performanceModeService?.ControlCapabilityDescription ?? "Windows Power Plan";
        
        /// <summary>
        /// Short status message for performance mode capabilities shown in UI.
        /// </summary>
        public string PerformanceModeCapabilityStatus
        {
            get
            {
                if (PerformanceModeEcControlAvailable)
                    return "✓ Full control (Power Plan + Fan + CPU/GPU Limits)";
                
                // Check if this is a Spectre laptop - provide more specific guidance
                var sysInfo = _systemInfoService?.GetSystemInfo();
                if (sysInfo?.IsHpSpectre == true)
                    return "ℹ️ HP Spectre: Power Plan + Fan only (use Intel XTU or ThrottleStop for CPU power limits)";
                
                return "ℹ️ Partial control (Power Plan + Fan only - EC unavailable)";
            }
        }

        private string _currentGpuMode = "Detecting...";
        public string CurrentGpuMode
        {
            get => _currentGpuMode;
            private set
            {
                if (_currentGpuMode != value)
                {
                    _currentGpuMode = value;
                    OnPropertyChanged();
                }
            }
        }
        
        // GPU Power Boost settings (TGP/PPAB control)
        private string _gpuPowerBoostLevel = "Medium";
        public string GpuPowerBoostLevel
        {
            get => _gpuPowerBoostLevel;
            set
            {
                if (_gpuPowerBoostLevel != value)
                {
                    _gpuPowerBoostLevel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuPowerBoostDescription));
                }
            }
        }
        
        private bool _gpuPowerBoostAvailable;
        public bool GpuPowerBoostAvailable
        {
            get => _gpuPowerBoostAvailable;
            private set
            {
                if (_gpuPowerBoostAvailable != value)
                {
                    _gpuPowerBoostAvailable = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private string _gpuPowerBoostStatus = "Detecting...";
        public string GpuPowerBoostStatus
        {
            get => _gpuPowerBoostStatus;
            private set
            {
                if (_gpuPowerBoostStatus != value)
                {
                    _gpuPowerBoostStatus = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string GpuPowerBoostDescription => GpuPowerBoostLevel switch
        {
            "Minimum" => "Base TGP only - Lower power, quieter operation, better battery life",
            "Medium" => "Custom TGP enabled - Balanced performance and thermals",
            "Maximum" => "Custom TGP + Dynamic Boost (PPAB) - Maximum GPU wattage (+15W boost)",
            "Extended" => "Extended Boost (PPAB+) - For RTX 5080/newer GPUs that support +25W or more",
            _ => "Select GPU power level"
        };
        
        public ObservableCollection<string> GpuPowerBoostLevels { get; } = new() { "Minimum", "Medium", "Maximum", "Extended" };
        
        // TCC Offset (CPU Temperature Limit)
        private TccOffsetStatus _tccStatus = TccOffsetStatus.CreateUnsupported();
        public TccOffsetStatus TccStatus
        {
            get => _tccStatus;
            private set
            {
                _tccStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TccStatusText));
                OnPropertyChanged(nameof(TccEffectiveLimit));
                OnPropertyChanged(nameof(TccSliderMaximum));
            }
        }
        
        private int _requestedTccOffset;
        public int RequestedTccOffset
        {
            get => _requestedTccOffset;
            set
            {
                if (_requestedTccOffset != value)
                {
                    _requestedTccOffset = Math.Clamp(value, 0, 63);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RequestedTempLimit));
                }
            }
        }
        
        public int RequestedTempLimit => TccStatus.TjMax - RequestedTccOffset;
        public string TccStatusText => TccStatus.StatusMessage;
        public int TccEffectiveLimit => TccStatus.EffectiveLimit;
        public int TccSliderMaximum => TccStatus.TjMax > 0 ? TccStatus.TjMax - 50 : 50; // Don't allow limiting below 50°C
        
        public ICommand ApplyTccOffsetCommand { get; private set; } = null!;
        public ICommand ResetTccOffsetCommand { get; private set; } = null!;
        
        public ObservableCollection<GpuSwitchMode> GpuSwitchModes { get; } = new();
        public GpuSwitchMode? SelectedGpuMode { get; set; }
        public bool CleanupUninstallApp { get; set; } = true;
        public bool CleanupRemoveServices { get; set; } = true;
        public bool CleanupRegistryEntries { get; set; } = false;
        public bool CleanupRemoveLegacyInstallers { get; set; } = true;
        public bool CleanupRemoveFiles { get; set; } = true;
        public bool CleanupKillProcesses { get; set; } = true;
        public string CleanupStatusText => CleanupStatus;
        public ICommand SwitchGpuModeCommand { get; }
        public ICommand CreateRestorePointCommand { get; }
        public ICommand RunCleanupCommand { get; }
        public ICommand ApplyUndervoltPresetCommand { get; }
        public ICommand ApplyAggressiveUndervoltCommand { get; }
        public ICommand ApplyGpuPowerBoostCommand { get; }

        public bool CleanupInProgress
        {
            get => _cleanupInProgress;
            private set
            {
                if (_cleanupInProgress != value)
                {
                    _cleanupInProgress = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasCleanupSteps));
                    (CleanupOmenHubCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }
        
        private bool _cleanupComplete;
        public bool CleanupComplete
        {
            get => _cleanupComplete;
            private set
            {
                if (_cleanupComplete != value)
                {
                    _cleanupComplete = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool HasCleanupSteps => OmenCleanupSteps.Count > 0;

        public string CleanupStatus
        {
            get => _cleanupStatus;
            private set
            {
                if (_cleanupStatus != value)
                {
                    _cleanupStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ApplyPerformanceModeCommand { get; }
        public ICommand ApplyUndervoltCommand { get; }
        public ICommand ResetUndervoltCommand { get; }
        public ICommand CleanupOmenHubCommand { get; }

        public SystemControlViewModel(
            UndervoltService undervoltService, 
            PerformanceModeService performanceModeService, 
            OmenGamingHubCleanupService cleanupService,
            SystemRestoreService restoreService,
            GpuSwitchService gpuSwitchService,
            LoggingService logging,
            ConfigurationService configService,
            HpWmiBios? wmiBios = null,
            OghServiceProxy? oghProxy = null,
            SystemInfoService? systemInfoService = null)
        {
            _undervoltService = undervoltService;
            _performanceModeService = performanceModeService;
            _cleanupService = cleanupService;
            _restoreService = restoreService;
            _gpuSwitchService = gpuSwitchService;
            _logging = logging;
            _configService = configService;
            _wmiBios = wmiBios;
            _oghProxy = oghProxy;
            _systemInfoService = systemInfoService;

            _undervoltService.StatusChanged += (s, status) => 
            {
                UndervoltStatus = status;
                OnPropertyChanged(nameof(UndervoltStatusText));
                OnPropertyChanged(nameof(UndervoltStatusColor));
                OnPropertyChanged(nameof(HasExternalUndervoltController));
                OnPropertyChanged(nameof(ExternalControllerName));
                OnPropertyChanged(nameof(ExternalControllerWarning));
                OnPropertyChanged(nameof(ExternalControllerHowToFix));
            };

            ApplyPerformanceModeCommand = new RelayCommand(_ => ApplyPerformanceMode(), _ => SelectedPerformanceMode != null);
            SelectPerformanceModeCommand = new RelayCommand(param => SelectPerformanceMode(param?.ToString() ?? "Balanced"));
            ApplyUndervoltCommand = new AsyncRelayCommand(_ => ApplyUndervoltAsync(), _ => !RespectExternalUndervolt || !UndervoltStatus.HasExternalController);
            ResetUndervoltCommand = new AsyncRelayCommand(async _ => await _undervoltService.ResetAsync());
            ApplyUndervoltPresetCommand = new AsyncRelayCommand(ApplyUndervoltPresetAsync);
            ApplyAggressiveUndervoltCommand = new AsyncRelayCommand(_ => ApplyAggressiveUndervoltAsync());
            CleanupOmenHubCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            RunCleanupCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            CreateRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync());
            SwitchGpuModeCommand = new AsyncRelayCommand(_ => SwitchGpuModeAsync());
            ApplyGpuPowerBoostCommand = new RelayCommand(_ => ApplyGpuPowerBoost(), _ => GpuPowerBoostAvailable);
            ApplyTccOffsetCommand = new RelayCommand(_ => ApplyTccOffset(), _ => TccStatus.IsSupported);
            ResetTccOffsetCommand = new RelayCommand(_ => ResetTccOffset(), _ => TccStatus.IsSupported);

            // Initialize performance modes with descriptions
            PerformanceModes.Add(new PerformanceMode { Name = "Quiet", Description = "Power saving mode - reduced fan noise, lower power limits. Best for quiet environments and light tasks." });
            PerformanceModes.Add(new PerformanceMode { Name = "Balanced", Description = "Default mode - balanced performance and power consumption. Good for everyday use." });
            PerformanceModes.Add(new PerformanceMode { Name = "Performance", Description = "High performance mode - maximum CPU/GPU power, fans ramp up faster. Best for gaming and heavy workloads." });
            
            // Restore last selected performance mode from config, or default to Balanced
            var savedModeName = _configService.Config.LastPerformanceModeName;
            var savedMode = !string.IsNullOrEmpty(savedModeName) 
                ? PerformanceModes.FirstOrDefault(m => m.Name == savedModeName) 
                : PerformanceModes.FirstOrDefault(m => m.Name == "Balanced");
            SelectedPerformanceMode = savedMode ?? PerformanceModes.FirstOrDefault();
            
            if (savedMode != null)
            {
                _logging.Info($"Restored last performance mode: {savedModeName}");
                
                // Actually apply the saved performance mode on startup
                // Schedule with delay to ensure BIOS is ready
                _ = Task.Run(async () =>
                {
                    await ReapplySettingWithRetryAsync(
                        "Performance Mode",
                        () => ReapplySavedPerformanceMode(savedMode),
                        maxRetries: 3,
                        initialDelayMs: 2000,
                        maxDelayMs: 5000
                    );
                });
            }
            
            // Restore last GPU Power Boost level from config
            var savedGpuBoostLevel = _configService.Config.LastGpuPowerBoostLevel;
            if (!string.IsNullOrEmpty(savedGpuBoostLevel) && GpuPowerBoostLevels.Contains(savedGpuBoostLevel))
            {
                _gpuPowerBoostLevel = savedGpuBoostLevel;
                _logging.Info($"Restored last GPU Power Boost level from config: {savedGpuBoostLevel}");
                
                // Reapply the saved GPU Power Boost level on startup with proper retry logic
                // This fixes the issue where GPU TGP resets to Minimum after reboot
                _ = Task.Run(async () =>
                {
                    await ReapplySettingWithRetryAsync(
                        "GPU Power Boost",
                        () => ReapplySavedGpuPowerBoost(savedGpuBoostLevel),
                        maxRetries: 5,
                        initialDelayMs: 1500,
                        maxDelayMs: 5000
                    );
                });
            }

            // Initialize GPU modes
            GpuSwitchModes.Add(GpuSwitchMode.Hybrid);
            GpuSwitchModes.Add(GpuSwitchMode.Discrete);
            GpuSwitchModes.Add(GpuSwitchMode.Integrated);
            
            // Detect current GPU mode
            DetectGpuMode();
            
            // Detect GPU Power Boost availability
            DetectGpuPowerBoost();
            
            // Initialize TCC offset (Intel CPU temperature limit)
            InitializeTccOffset();


            
            // Load undervolt preferences from config
            var undervoltPrefs = _configService.Config.Undervolt;
            RequestedCoreOffset = undervoltPrefs.DefaultOffset.CoreMv;
            RequestedCacheOffset = undervoltPrefs.DefaultOffset.CacheMv;
            EnablePerCoreUndervolt = undervoltPrefs.EnablePerCoreUndervolt;
            RequestedPerCoreOffsets = undervoltPrefs.PerCoreOffsetsMv?.Clone() as int?[];
            RespectExternalUndervolt = undervoltPrefs.RespectExternalControllers;
            
            // Initialize per-core offset view models
            InitializePerCoreOffsets();
        }

        private void InitializeTccOffset()
        {
            try
            {
                _msrAccess = MsrAccessFactory.Create(_logging);
                if (_msrAccess == null || !_msrAccess.IsAvailable)
                {
                    TccStatus = TccOffsetStatus.CreateUnsupported("No MSR access available (install PawnIO for TCC control)");
                    _msrAccess = null;
                    return;
                }

                var tj = _msrAccess.ReadTjMax();
                var currentOffset = _msrAccess.ReadTccOffset();
                TccStatus = TccOffsetStatus.CreateSupported(tj, currentOffset);

                var savedOffset = _configService.Config.LastTccOffset;
                if (savedOffset.HasValue && savedOffset.Value > 0)
                {
                    if (currentOffset != savedOffset.Value)
                    {
                        _logging.Info($"TCC offset needs restoration: saved {savedOffset.Value}°C differs from current {currentOffset}°C");
                        _ = Task.Run(async () =>
                        {
                            await ReapplySettingWithRetryAsync(
                                "TCC Offset",
                                () => ReapplySavedTccOffset(savedOffset.Value),
                                maxRetries: 8,
                                initialDelayMs: 1500,
                                maxDelayMs: 8000
                            );
                        });
                    }
                    else
                    {
                        _logging.Info($"TCC offset already at saved value ({savedOffset.Value}°C), no restoration needed");
                    }
                }
                else
                {
                    _logging.Info("No saved TCC offset to restore (either null or 0)");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"TCC offset not available: {ex.Message}");
                TccStatus = TccOffsetStatus.CreateUnsupported($"Not supported: {ex.Message}");
                _msrAccess = null;
            }
        }
        
        private void InitializePerCoreOffsets()
        {
            PerCoreOffsets.Clear();
            
            // Assume up to 16 cores for now (can be made dynamic later)
            const int maxCores = 16;
            
            for (int i = 0; i < maxCores; i++)
            {
                var vm = new PerCoreOffsetViewModel
                {
                    CoreName = $"Core {i}",
                    CoreIndex = i,
                    OffsetMv = RequestedPerCoreOffsets != null && i < RequestedPerCoreOffsets.Length 
                        ? RequestedPerCoreOffsets[i] 
                        : null
                };
                
                // Subscribe to changes
                vm.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(PerCoreOffsetViewModel.OffsetMv))
                    {
                        UpdateRequestedPerCoreOffsets();
                    }
                };
                
                PerCoreOffsets.Add(vm);
            }
        }
        
        private void UpdateRequestedPerCoreOffsets()
        {
            var offsets = new int?[PerCoreOffsets.Count];
            bool hasAnyOffset = false;
            
            for (int i = 0; i < PerCoreOffsets.Count; i++)
            {
                offsets[i] = PerCoreOffsets[i].OffsetMv;
                if (offsets[i].HasValue)
                    hasAnyOffset = true;
            }
            
            RequestedPerCoreOffsets = hasAnyOffset ? offsets : null;
        }
        
        /// <summary>
        /// Reapply saved TCC offset on startup to maintain temperature limits after reboot.
        /// Throws an exception if the operation fails, enabling retry logic.
        /// </summary>
        private void ReapplySavedTccOffset(int savedOffset)
        {
            if (_msrAccess == null || !TccStatus.IsSupported)
                throw new InvalidOperationException("MSR access not available or TCC not supported");
                
            _logging.Info($"Reapplying saved TCC offset on startup: {savedOffset}°C");
            _msrAccess.SetTccOffset(savedOffset);
            
            // Verify it was applied
            var verifiedOffset = _msrAccess.ReadTccOffset();
            if (verifiedOffset == savedOffset)
            {
                _logging.Info($"✓ TCC offset restored on startup: {savedOffset}°C (effective limit: {TccStatus.TjMax - savedOffset}°C)");
                
                // Update UI on dispatcher thread
                App.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    RequestedTccOffset = savedOffset;
                    TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, savedOffset);
                });
            }
            else
            {
                throw new InvalidOperationException($"TCC offset verification failed: requested {savedOffset}°C, got {verifiedOffset}°C");
            }
        }
        
        /// <summary>
        /// Helper method to reapply a setting with exponential backoff retry.
        /// This fixes the issue where settings don't survive reboot because WMI/BIOS
        /// may not be ready immediately after Windows login.
        /// </summary>
        /// <param name="settingName">Name of the setting for logging</param>
        /// <param name="applyAction">Action that applies the setting (should throw on failure)</param>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="initialDelayMs">Initial delay before first attempt</param>
        /// <param name="maxDelayMs">Maximum delay between retries</param>
        private async Task ReapplySettingWithRetryAsync(
            string settingName,
            Action applyAction,
            int maxRetries = 5,
            int initialDelayMs = 1500,
            int maxDelayMs = 5000)
        {
            // Initial delay to let system stabilize after login
            await Task.Delay(initialDelayMs);
            
            int attempt = 0;
            int currentDelay = initialDelayMs;
            
            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    applyAction();
                    _logging.Info($"✓ {settingName} restored on attempt {attempt}/{maxRetries}");
                    return; // Success!
                }
                catch (Exception ex)
                {
                    _logging.Warn($"{settingName} restoration attempt {attempt}/{maxRetries} failed: {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        // Exponential backoff with jitter
                        var jitter = new Random().Next(0, 500);
                        var nextDelay = Math.Min(currentDelay * 2 + jitter, maxDelayMs);
                        _logging.Info($"Retrying {settingName} in {nextDelay}ms...");
                        await Task.Delay(nextDelay);
                        currentDelay = nextDelay;
                    }
                }
            }
            
            _logging.Warn($"× {settingName} restoration failed after {maxRetries} attempts");
        }
        
        private void ApplyTccOffset()
        {
            if (_msrAccess == null || !TccStatus.IsSupported)
                return;
                
            try
            {
                _msrAccess.SetTccOffset(RequestedTccOffset);
                var newLimit = TccStatus.TjMax - RequestedTccOffset;
                _logging.Info($"TCC offset set to {RequestedTccOffset}°C (effective limit: {newLimit}°C)");
                
                // Save to config for persistence across reboots
                SaveTccOffsetToConfig(RequestedTccOffset);
                
                // Refresh status
                var currentOffset = _msrAccess.ReadTccOffset();
                TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, currentOffset);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply TCC offset: {ex.Message}", ex);
            }
        }
        
        private void SaveTccOffsetToConfig(int offset)
        {
            try
            {
                var config = _configService.Config;
                config.LastTccOffset = offset;
                _configService.Save(config);
                _logging.Info($"TCC offset saved to config: {offset}°C");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save TCC offset to config: {ex.Message}");
            }
        }
        
        private void ResetTccOffset()
        {
            if (_msrAccess == null || !TccStatus.IsSupported)
                return;
                
            try
            {
                _msrAccess.SetTccOffset(0);
                RequestedTccOffset = 0;
                _logging.Info("TCC offset reset to 0 (no temperature limit)");
                
                // Refresh status
                TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, 0);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to reset TCC offset: {ex.Message}", ex);
            }
        }
        
        private void DetectGpuPowerBoost()
        {
            // Try WMI BIOS first (preferred)
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                GpuPowerBoostAvailable = true;
                var gpuPower = _wmiBios.GetGpuPower();
                if (gpuPower.HasValue)
                {
                    // Check for extended PPAB values (RTX 5080 etc.)
                    if (gpuPower.Value.customTgp && gpuPower.Value.ppab)
                    {
                        // Standard Maximum level - can't distinguish extended via bool
                        GpuPowerBoostLevel = "Maximum";
                        GpuPowerBoostStatus = "Maximum (Custom TGP + Dynamic Boost)";
                    }
                    else if (gpuPower.Value.customTgp)
                    {
                        GpuPowerBoostLevel = "Medium";
                        GpuPowerBoostStatus = "Medium (Custom TGP)";
                    }
                    else
                    {
                        GpuPowerBoostLevel = "Minimum";
                        GpuPowerBoostStatus = "Minimum (Base TGP)";
                    }
                }
                else
                {
                    GpuPowerBoostStatus = "Could not read current setting";
                }
                _logging.Info($"✓ GPU Power Boost available via WMI BIOS. Current: {GpuPowerBoostStatus}");
                return;
            }
            
            // Fallback: Try OGH proxy (for systems where WMI BIOS commands fail)
            if (_oghProxy != null && _oghProxy.Status.WmiAvailable)
            {
                var (success, level, levelName) = _oghProxy.GetGpuPowerLevel();
                if (success)
                {
                    GpuPowerBoostAvailable = true;
                    // Map OGH level to our names including Extended
                    var mappedLevel = level switch
                    {
                        0 => "Minimum",
                        1 => "Medium",
                        2 => "Maximum",
                        3 => "Extended",
                        _ => levelName
                    };
                    GpuPowerBoostLevel = mappedLevel;
                    GpuPowerBoostStatus = $"{mappedLevel} (via OGH)";
                    _logging.Info($"✓ GPU Power Boost available via OGH. Current: {GpuPowerBoostStatus}");
                    return;
                }
                
                // OGH WMI exists but GPU power commands failed - don't enable if commands don't work
                _logging.Warn("GPU Power Boost: OGH WMI exists but GetGpuPowerLevel() failed");
            }
            
            // Neither backend functional - provide detailed explanation
            GpuPowerBoostAvailable = false;
            var modelNotSupportedMsg = @"Not available - This OMEN model does not support WMI GPU power commands.

The HP WMI BIOS interface exists but GPU power commands return empty results. " +
                "This is a known limitation on some newer OMEN models (17-ck2xxx series and others).";
            GpuPowerBoostStatus = modelNotSupportedMsg;
            _logging.Info("GPU Power Boost: HP WMI BIOS interface not available, OGH not functional");
        }
        
        private void ApplyGpuPowerBoost()
        {
            // Try WMI BIOS first (preferred)
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                var level = GpuPowerBoostLevel switch
                {
                    "Minimum" => HpWmiBios.GpuPowerLevel.Minimum,
                    "Medium" => HpWmiBios.GpuPowerLevel.Medium,
                    "Maximum" => HpWmiBios.GpuPowerLevel.Maximum,
                    "Extended" => HpWmiBios.GpuPowerLevel.Extended3,
                    _ => HpWmiBios.GpuPowerLevel.Medium
                };

                if (_wmiBios.SetGpuPower(level))
                {
                    GpuPowerBoostStatus = GpuPowerBoostLevel switch
                    {
                        "Minimum" => "✓ Minimum (Base TGP only)",
                        "Medium" => "✓ Medium (Custom TGP enabled)",
                        "Maximum" => "✓ Maximum (Custom TGP + Dynamic Boost +15W)",
                        "Extended" => "✓ Extended (PPAB+ for RTX 5080, +25W if supported)",
                        _ => "Applied"
                    };
                    _logging.Info($"✓ GPU Power Boost set to: {GpuPowerBoostLevel} via WMI BIOS");
                    
                    // Save to config for persistence (note: may still reset after sleep/reboot on some models)
                    SaveGpuPowerBoostToConfig();
                    return;
                }
            }
            
            // Fallback: Try OGH proxy
            if (_oghProxy != null && _oghProxy.Status.WmiAvailable)
            {
                var levelValue = GpuPowerBoostLevel switch
                {
                    "Minimum" => 0,
                    "Medium" => 1,
                    "Maximum" => 2,
                    "Extended" => 3,  // Try extended value for OGH
                    _ => 1
                };
                
                if (_oghProxy.SetGpuPowerLevel(levelValue))
                {
                    GpuPowerBoostStatus = GpuPowerBoostLevel switch
                    {
                        "Minimum" => "✓ Minimum (Base TGP only, via OGH)",
                        "Medium" => "✓ Medium (Custom TGP, via OGH)",
                        "Maximum" => "✓ Maximum (Dynamic Boost, via OGH)",
                        "Extended" => "✓ Extended (PPAB+, via OGH)",
                        _ => "Applied via OGH"
                    };
                    _logging.Info($"✓ GPU Power Boost set to: {GpuPowerBoostLevel} via OGH");
                    
                    // Save to config for persistence
                    SaveGpuPowerBoostToConfig();
                    return;
                }
            }
            
            GpuPowerBoostStatus = "Failed - WMI GPU power commands not supported on this model";
            _logging.Warn($"Failed to set GPU Power Boost to: {GpuPowerBoostLevel} - WMI commands not functional on this OMEN model");
        }
        
        /// <summary>
        /// Reapply the saved GPU Power Boost level on startup.
        /// This is called after a delay to ensure WMI/BIOS is ready.
        /// <summary>
        /// Reapply saved GPU Power Boost level on startup.
        /// Throws an exception if the operation fails, enabling retry logic.
        /// </summary>
        private void ReapplySavedGpuPowerBoost(string savedLevel)
        {
            _logging.Info($"Reapplying saved GPU Power Boost level on startup: {savedLevel}");
            
            // Try WMI BIOS first
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                var level = savedLevel switch
                {
                    "Minimum" => HpWmiBios.GpuPowerLevel.Minimum,
                    "Medium" => HpWmiBios.GpuPowerLevel.Medium,
                    "Maximum" => HpWmiBios.GpuPowerLevel.Maximum,
                    _ => HpWmiBios.GpuPowerLevel.Medium
                };

                if (_wmiBios.SetGpuPower(level))
                {
                    _logging.Info($"✓ GPU Power Boost reapplied on startup: {savedLevel} via WMI BIOS");
                    
                    // Update status on UI thread
                    App.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        GpuPowerBoostStatus = savedLevel switch
                        {
                            "Minimum" => "Minimum (Base TGP only) - Restored",
                            "Medium" => "Medium (Custom TGP) - Restored",
                            "Maximum" => "Maximum (Custom TGP + Dynamic Boost) - Restored",
                            _ => $"{savedLevel} - Restored"
                        };
                    });
                    return;
                }
            }
            
            // Fallback: Try OGH proxy
            if (_oghProxy != null && _oghProxy.Status.WmiAvailable)
            {
                var levelValue = savedLevel switch
                {
                    "Minimum" => 0,
                    "Medium" => 1,
                    "Maximum" => 2,
                    _ => 1
                };
                
                if (_oghProxy.SetGpuPowerLevel(levelValue))
                {
                    _logging.Info($"✓ GPU Power Boost reapplied on startup: {savedLevel} via OGH");
                    
                    App.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        GpuPowerBoostStatus = $"{savedLevel} (via OGH) - Restored";
                    });
                    return;
                }
            }
            
            // Neither WMI nor OGH succeeded - throw to trigger retry
            throw new InvalidOperationException("GPU Power Boost restoration failed - WMI BIOS and OGH both unavailable or returned failure");
        }
        
        private void SaveGpuPowerBoostToConfig()
        {
            try
            {
                var config = _configService.Config;
                config.LastGpuPowerBoostLevel = GpuPowerBoostLevel;
                _configService.Save(config);
                _logging.Info($"GPU Power Boost level saved to config: {GpuPowerBoostLevel}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save GPU Power Boost level to config: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reapply saved performance mode on startup.
        /// Throws an exception if the operation fails, enabling retry logic.
        /// </summary>
        private void ReapplySavedPerformanceMode(PerformanceMode mode)
        {
            _logging.Info($"Reapplying saved performance mode on startup: {mode.Name}");
            
            try
            {
                // Apply the performance mode via the service
                _performanceModeService.Apply(mode);
                
                _logging.Info($"✓ Performance mode reapplied on startup: {mode.Name}");
                
                // Update UI on dispatcher thread
                App.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    OnPropertyChanged(nameof(CurrentPerformanceModeName));
                    OnPropertyChanged(nameof(SelectedPerformanceMode));
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to reapply performance mode: {ex.Message}");
                throw; // Re-throw to trigger retry logic
            }
        }
        
        private void DetectGpuMode()
        {
            try
            {
                var mode = _gpuSwitchService.DetectCurrentMode();
                CurrentGpuMode = mode switch
                {
                    GpuSwitchMode.Hybrid => "Hybrid (MSHybrid/Optimus)",
                    GpuSwitchMode.Discrete => "Discrete GPU Only",
                    GpuSwitchMode.Integrated => "Integrated GPU Only",
                    _ => "Unknown"
                };
                _logging.Info($"Detected GPU mode: {CurrentGpuMode}");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to detect GPU mode", ex);
                CurrentGpuMode = "Detection Failed";
            }
        }

        private void ApplyPerformanceMode()
        {
            if (SelectedPerformanceMode != null)
            {
                _performanceModeService.Apply(SelectedPerformanceMode);
                _logging.Info($"Performance mode applied: {SelectedPerformanceMode.Name}");
                
                // Save the selected mode to config for persistence
                var config = _configService.Config;
                config.LastPerformanceModeName = SelectedPerformanceMode.Name;
                _configService.Save(config);
                _logging.Info($"Performance mode saved to config: {SelectedPerformanceMode.Name}");
                
                OnPropertyChanged(nameof(CurrentPerformanceModeName));
                OnPropertyChanged(nameof(SelectedPerformanceMode));
                OnPropertyChanged(nameof(IsQuietMode));
                OnPropertyChanged(nameof(IsBalancedMode));
                OnPropertyChanged(nameof(IsPerformanceMode));
            }
        }
        
        public string CurrentPerformanceModeName => SelectedPerformanceMode?.Name ?? "Auto";
        
        // Mode boolean properties for UI binding
        public bool IsQuietMode => SelectedPerformanceMode?.Name == "Quiet";
        public bool IsBalancedMode => SelectedPerformanceMode?.Name == "Balanced";
        public bool IsPerformanceMode => SelectedPerformanceMode?.Name == "Performance";
        
        // Command for selecting performance mode from Advanced view
        public ICommand SelectPerformanceModeCommand { get; }
        
        private void SelectPerformanceMode(string modeName)
        {
            var mode = PerformanceModes.FirstOrDefault(m => m.Name == modeName);
            if (mode != null)
            {
                SelectedPerformanceMode = mode;
                ApplyPerformanceMode();
            }
        }
        
        /// <summary>
        /// Select a performance mode by name without applying it.
        /// Used for UI synchronization when mode is changed externally (e.g., power automation).
        /// </summary>
        public void SelectModeByNameNoApply(string modeName)
        {
            var mode = PerformanceModes.FirstOrDefault(m => 
                m.Name.Equals(modeName, StringComparison.OrdinalIgnoreCase));
            if (mode != null && _selectedPerformanceMode != mode)
            {
                _selectedPerformanceMode = mode;
                OnPropertyChanged(nameof(SelectedPerformanceMode));
                OnPropertyChanged(nameof(CurrentPerformanceModeName));
                OnPropertyChanged(nameof(IsQuietMode));
                OnPropertyChanged(nameof(IsBalancedMode));
                OnPropertyChanged(nameof(IsPerformanceMode));
            }
        }

        private async Task ApplyUndervoltAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                var offset = new UndervoltOffset
                {
                    CoreMv = RequestedCoreOffset,
                    CacheMv = RequestedCacheOffset
                };

                // Add per-core offsets if enabled
                if (EnablePerCoreUndervolt && RequestedPerCoreOffsets != null)
                {
                    offset.PerCoreOffsetsMv = RequestedPerCoreOffsets;
                }

                await _undervoltService.ApplyAsync(offset);

                // Save undervolt preferences to config
                var config = _configService.Config;
                config.Undervolt.DefaultOffset.CoreMv = RequestedCoreOffset;
                config.Undervolt.DefaultOffset.CacheMv = RequestedCacheOffset;
                config.Undervolt.EnablePerCoreUndervolt = EnablePerCoreUndervolt;
                config.Undervolt.PerCoreOffsetsMv = RequestedPerCoreOffsets?.Clone() as int?[];
                config.Undervolt.RespectExternalControllers = RespectExternalUndervolt;
                _configService.Save(config);
            }, "Applying undervolt settings...");
        }

        private async Task ApplyUndervoltPresetAsync(object? parameter)
        {
            if (parameter is string offsetStr && double.TryParse(offsetStr, out double offset))
            {
                RequestedCoreOffset = offset;
                RequestedCacheOffset = offset;
                await ApplyUndervoltAsync();
            }
        }
        
        /// <summary>
        /// Apply aggressive undervolt (-140mV) with confirmation dialog.
        /// High undervolt values can cause system instability, BSODs, or crashes.
        /// </summary>
        private async Task ApplyAggressiveUndervoltAsync()
        {
            var result = System.Windows.MessageBox.Show(
                "⚠️ WARNING: Aggressive Undervolt\n\n" +
                "You are about to apply a -140mV undervolt offset.\n\n" +
                "This is an aggressive setting that may cause:\n" +
                "• Blue screen crashes (BSOD)\n" +
                "• Application crashes or freezes\n" +
                "• System instability under load\n\n" +
                "Only proceed if you understand the risks and are prepared to:\n" +
                "• Reboot if your system becomes unstable\n" +
                "• Test thoroughly with stress tests (Prime95, OCCT)\n\n" +
                "The undervolt will not persist after reboot, so a crash will reset it.\n\n" +
                "Continue with -140mV undervolt?",
                "Aggressive Undervolt Warning",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _logging.Warn("User confirmed aggressive undervolt (-140mV)");
                RequestedCoreOffset = -140;
                RequestedCacheOffset = -140;
                await ApplyUndervoltAsync();
            }
            else
            {
                _logging.Info("User cancelled aggressive undervolt");
            }
        }

        private async Task CreateRestorePointAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                _logging.Info("Creating system restore point...");
                var result = await _restoreService.CreateRestorePointAsync("OmenCore - Before System Changes");
                
                if (result.Success)
                {
                    _logging.Info($"✓ System restore point created successfully (Sequence: {result.SequenceNumber})");
                }
                else
                {
                    _logging.Error($"✗ Failed to create restore point: {result.Message}");
                }
            }, "Creating system restore point...");
        }

        private async Task SwitchGpuModeAsync()
        {
            if (SelectedGpuMode == null)
            {
                _logging.Warn("No GPU mode selected");
                return;
            }
            
            // Check if GPU mode switching is supported BEFORE attempting
            if (!_gpuSwitchService.IsSupported)
            {
                var reason = _gpuSwitchService.UnsupportedReason;
                _logging.Warn($"GPU mode switching not available: {reason}");
                System.Windows.MessageBox.Show(
                    $"GPU mode switching is not available on this system.\n\n" +
                    $"Reason: {reason}\n\n" +
                    "This feature requires:\n" +
                    "• HP OMEN laptop with BIOS GPU mode support\n" +
                    "• HP WMI BIOS interface for GPU settings\n\n" +
                    "Note: HP Transcend, Victus, and some other HP models\n" +
                    "do not support this feature through OmenCore.\n\n" +
                    "To change GPU modes on unsupported systems:\n" +
                    "• Use NVIDIA Control Panel (for Optimus)\n" +
                    "• Check BIOS settings directly\n" +
                    "• Use manufacturer's control software",
                    "GPU Mode Switching Not Supported - OmenCore",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            await ExecuteWithLoadingAsync(() => {
                var targetMode = SelectedGpuMode.Value;
                _logging.Info($"⚡ Attempting to switch GPU mode to: {targetMode}");

                var success = _gpuSwitchService.Switch(targetMode);

                if (success)
                {
                    _logging.Info($"✓ GPU mode switch initiated. System restart required to apply changes.");

                    // Show restart prompt
                    var result = System.Windows.MessageBox.Show(
                        $"GPU mode has been set to {targetMode}.\n\n" +
                        "A system restart is required for changes to take effect.\n\n" +
                        "Would you like to restart now?",
                        "Restart Required - OmenCore",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        // Restart the system
                        System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                    }
                }
                else
                {
                    _logging.Error("✗ GPU mode switch failed");
                    System.Windows.MessageBox.Show(
                        "Failed to switch GPU mode. Please check the logs for details.",
                        "GPU Mode Switch Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }

                return Task.CompletedTask;
            });
        }

        private async Task RunCleanupAsync()
        {
            OmenCleanupSteps.Clear();
            _cleanupService.StepCompleted += OnStepCompleted;

            try
            {
                await ExecuteWithLoadingAsync(async () =>
                {
                    var options = new OmenCleanupOptions
                    {
                        RemoveStorePackage = CleanupUninstallApp,
                        RemoveServicesAndTasks = CleanupRemoveServices,
                        RemoveRegistryTraces = CleanupRegistryEntries,
                        RemoveLegacyInstallers = CleanupRemoveLegacyInstallers,
                        RemoveResidualFiles = CleanupRemoveFiles,
                        KillRunningProcesses = CleanupKillProcesses,
                        DryRun = false,
                        PreserveFirewallRules = true
                    };

                    var result = await _cleanupService.CleanupAsync(options);
                    CleanupStatus = result.Success ? "✓ Cleanup complete - restart recommended" : "⚠ Cleanup failed";
                    CleanupComplete = result.Success;
                }, "Running HP Omen cleanup...");
            }
            finally
            {
                _cleanupService.StepCompleted -= OnStepCompleted;
                CleanupInProgress = false;
            }
        }

        private void OnStepCompleted(string step)
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                OmenCleanupSteps.Add(step);
                CleanupStatus = step;
                OnPropertyChanged(nameof(HasCleanupSteps));
            });
        }

public class PerCoreOffsetViewModel : ViewModelBase
        {
            private int? _offsetMv;

            public string CoreName { get; set; } = string.Empty;
            public int CoreIndex { get; set; }

            public int? OffsetMv
            {
                get => _offsetMv;
                set
                {
                    if (_offsetMv != value)
                    {
                        _offsetMv = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(OffsetText));
                    }
                }
            }

            public string OffsetText => OffsetMv.HasValue ? $"{OffsetMv.Value:+0;-0;0} mV" : "Global";
        }
    }
}
