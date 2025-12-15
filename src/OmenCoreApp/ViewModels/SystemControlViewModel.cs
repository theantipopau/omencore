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
        private WinRing0MsrAccess? _msrAccess;

        private PerformanceMode? _selectedPerformanceMode;
        private UndervoltStatus _undervoltStatus = UndervoltStatus.CreateUnknown();
        private double _requestedCoreOffset;
        private double _requestedCacheOffset;
        private bool _respectExternalUndervolt = true;
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

        public string UndervoltStatusSummary => UndervoltStatus == null ? "n/a" : $"Core {UndervoltStatus.CurrentCoreOffsetMv:+0;-0;0} mV | Cache {UndervoltStatus.CurrentCacheOffsetMv:+0;-0;0} mV";
        
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
            _ => "Close or disable the external application, then restart OmenCore."
        };

        public string PerformanceModeDescription => SelectedPerformanceMode?.Description ?? SelectedPerformanceMode?.Name switch
        {
            "Balanced" => "Normal mode - balanced performance",
            "Performance" => "High performance - maximum GPU wattage",
            "Eco" => "Power saving mode - reduced wattage",
            _ => "Select a performance mode"
        };

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
            _ => "Select GPU power level"
        };
        
        public ObservableCollection<string> GpuPowerBoostLevels { get; } = new() { "Minimum", "Medium", "Maximum" };
        
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
                    (CleanupOmenHubCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

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
            OghServiceProxy? oghProxy = null)
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
            ApplyUndervoltCommand = new AsyncRelayCommand(_ => ApplyUndervoltAsync(), _ => !RespectExternalUndervolt || !UndervoltStatus.HasExternalController);
            ResetUndervoltCommand = new AsyncRelayCommand(async _ => await _undervoltService.ResetAsync());
            ApplyUndervoltPresetCommand = new AsyncRelayCommand(ApplyUndervoltPresetAsync);
            CleanupOmenHubCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            RunCleanupCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            CreateRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync());
            SwitchGpuModeCommand = new AsyncRelayCommand(_ => SwitchGpuModeAsync());
            ApplyGpuPowerBoostCommand = new RelayCommand(_ => ApplyGpuPowerBoost(), _ => GpuPowerBoostAvailable);
            ApplyTccOffsetCommand = new RelayCommand(_ => ApplyTccOffset(), _ => TccStatus.IsSupported);
            ResetTccOffsetCommand = new RelayCommand(_ => ResetTccOffset(), _ => TccStatus.IsSupported);

            // Initialize performance modes
            PerformanceModes.Add(new PerformanceMode { Name = "Balanced" });
            PerformanceModes.Add(new PerformanceMode { Name = "Performance" });
            PerformanceModes.Add(new PerformanceMode { Name = "Quiet" });
            
            // Restore last selected performance mode from config, or default to first
            var savedModeName = _configService.Config.LastPerformanceModeName;
            var savedMode = !string.IsNullOrEmpty(savedModeName) 
                ? PerformanceModes.FirstOrDefault(m => m.Name == savedModeName) 
                : null;
            SelectedPerformanceMode = savedMode ?? PerformanceModes.FirstOrDefault();
            
            if (savedMode != null)
            {
                _logging.Info($"Restored last performance mode: {savedModeName}");
            }
            
            // Restore last GPU Power Boost level from config
            var savedGpuBoostLevel = _configService.Config.LastGpuPowerBoostLevel;
            if (!string.IsNullOrEmpty(savedGpuBoostLevel) && GpuPowerBoostLevels.Contains(savedGpuBoostLevel))
            {
                _gpuPowerBoostLevel = savedGpuBoostLevel;
                _logging.Info($"Restored last GPU Power Boost level from config: {savedGpuBoostLevel}");
                
                // Reapply the saved GPU Power Boost level on startup
                // This fixes the issue where GPU TGP resets to Minimum after reboot
                _ = Task.Run(async () =>
                {
                    // Wait a moment for WMI/BIOS to be fully ready
                    await Task.Delay(2000);
                    ReapplySavedGpuPowerBoost(savedGpuBoostLevel);
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
            
            // Initial undervolt status will be set via StatusChanged event
        }
        
        private void InitializeTccOffset()
        {
            try
            {
                _msrAccess = new WinRing0MsrAccess();
                if (_msrAccess.IsAvailable)
                {
                    var tjMax = _msrAccess.ReadTjMax();
                    var currentOffset = _msrAccess.ReadTccOffset();
                    TccStatus = TccOffsetStatus.CreateSupported(tjMax, currentOffset);
                    RequestedTccOffset = currentOffset;
                    _logging.Info($"TCC offset available: TjMax={tjMax}°C, Current offset={currentOffset}°C, Effective limit={tjMax - currentOffset}°C");
                }
                else
                {
                    TccStatus = TccOffsetStatus.CreateUnsupported("WinRing0 driver not available");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"TCC offset not available: {ex.Message}");
                TccStatus = TccOffsetStatus.CreateUnsupported($"Not supported: {ex.Message}");
                _msrAccess = null;
            }
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
                
                // Refresh status
                var currentOffset = _msrAccess.ReadTccOffset();
                TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, currentOffset);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply TCC offset: {ex.Message}", ex);
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
                    if (gpuPower.Value.customTgp && gpuPower.Value.ppab)
                    {
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
                    GpuPowerBoostLevel = levelName;
                    GpuPowerBoostStatus = $"{levelName} (via OGH)";
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
                    _ => HpWmiBios.GpuPowerLevel.Medium
                };

                if (_wmiBios.SetGpuPower(level))
                {
                    GpuPowerBoostStatus = GpuPowerBoostLevel switch
                    {
                        "Minimum" => "✓ Minimum (Base TGP only)",
                        "Medium" => "✓ Medium (Custom TGP enabled)",
                        "Maximum" => "✓ Maximum (Custom TGP + Dynamic Boost +15W)",
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
                    _ => 1
                };
                
                if (_oghProxy.SetGpuPowerLevel(levelValue))
                {
                    GpuPowerBoostStatus = GpuPowerBoostLevel switch
                    {
                        "Minimum" => "✓ Minimum (Base TGP only, via OGH)",
                        "Medium" => "✓ Medium (Custom TGP, via OGH)",
                        "Maximum" => "✓ Maximum (Dynamic Boost, via OGH)",
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
        /// </summary>
        private void ReapplySavedGpuPowerBoost(string savedLevel)
        {
            try
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
                
                _logging.Warn($"Could not reapply GPU Power Boost on startup - WMI commands not available");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to reapply GPU Power Boost on startup: {ex.Message}");
            }
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
            }
        }
        
        public string CurrentPerformanceModeName => SelectedPerformanceMode?.Name ?? "Auto";

        private async Task ApplyUndervoltAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                var offset = new UndervoltOffset
                {
                    CoreMv = RequestedCoreOffset,
                    CacheMv = RequestedCacheOffset
                };
                await _undervoltService.ApplyAsync(offset);
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

            await ExecuteWithLoadingAsync(async () =>
            {
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
                        _logging.Info("User accepted restart - initiating system restart");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "shutdown",
                            Arguments = "/r /t 5 /c \"Restarting to apply GPU mode change\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                }
                else
                {
                    _logging.Warn($"⚠️ GPU mode switching failed. Current mode: {CurrentGpuMode}");
                    System.Windows.MessageBox.Show(
                        "GPU mode switching failed.\n\n" +
                        "The HP BIOS did not accept the mode change.\n" +
                        "This can happen if:\n" +
                        "• Your BIOS doesn't have this setting\n" +
                        "• A BIOS password is set\n" +
                        "• The BIOS version doesn't support WMI control\n\n" +
                        "Try changing GPU mode directly in BIOS settings.",
                        "GPU Mode Switch Failed - OmenCore",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
                
                await Task.CompletedTask;
            }, "Switching GPU mode...");
        }

        private async Task RunCleanupAsync()
        {
            CleanupInProgress = true;
            CleanupStatus = "Running cleanup...";
            OmenCleanupSteps.Clear();
            
            // Subscribe to real-time progress updates
            void OnStepCompleted(string step)
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    OmenCleanupSteps.Add(step);
                    CleanupStatus = step;
                });
            }
            
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
                    CleanupStatus = result.Success ? "✓ Cleanup complete" : "⚠ Cleanup failed";
                }, "Running HP Omen cleanup...");
            }
            finally
            {
                _cleanupService.StepCompleted -= OnStepCompleted;
                CleanupInProgress = false;
            }
        }
    }
}
