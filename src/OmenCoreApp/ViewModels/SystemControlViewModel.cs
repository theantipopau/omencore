using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
        private readonly NvapiService? _nvapiService;
        private readonly AmdGpuService? _amdGpuService;
        private readonly FanService? _fanService;
        private readonly HardwareMonitoringService? _hardwareMonitoringService;
        private IMsrAccess? _msrAccess;  // Changed from WinRing0MsrAccess to IMsrAccess
        private readonly IEcAccess? _ecAccess;
        private EdpThrottlingMitigationService? _edpMitigationService;

        private PerformanceMode? _selectedPerformanceMode;
        private UndervoltStatus _undervoltStatus = UndervoltStatus.CreateUnknown();
        private double _requestedCoreOffset;
        private double _requestedCacheOffset;
        private bool _respectExternalUndervolt = true;
        private bool _enablePerCoreUndervolt;
        private int?[]? _requestedPerCoreOffsets;
        private string _undervoltLastActionText = "No recent undervolt action";
        private System.Windows.Media.Brush _undervoltLastActionColor = System.Windows.Media.Brushes.Gray;
        private bool _cleanupInProgress;
        private string _cleanupStatus = "Status: Not checked";
        private CancellationTokenSource? _gpuOcTestCancellation;
        private GpuOcSnapshot? _gpuOcTestSnapshot;
        private int _gpuOcTestCountdownSeconds;
        private bool _gpuOcTestPending;
        private string _gpuOcTestStatusText = "Test Apply runs tuning temporarily and reverts automatically unless you keep it.";

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
                    OnPropertyChanged(nameof(CurrentPerformanceModeIndicator));
                    OnPropertyChanged(nameof(IsQuietMode));
                    OnPropertyChanged(nameof(IsBalancedMode));
                    OnPropertyChanged(nameof(IsPerformanceMode));
                    
                    // Auto-save selected performance mode to config
                    if (value != null)
                    {
                        SavePerformanceModeToConfig(value.Name);
                    }
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
        /// Whether undervolting/curve optimizer control is actionable on this system.
        /// </summary>
        public bool IsUndervoltSupported
        {
            get
            {
                if (_undervoltService == null || UndervoltStatus == null)
                {
                    return false;
                }

                if (UndervoltStatus.HasExternalController && RespectExternalUndervolt)
                {
                    return false;
                }

                var reason = (UndervoltStatus.Warning ?? UndervoltStatus.Error ?? string.Empty).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    return true;
                }

                // Treat hard backend/capability failures as unsupported.
                if (reason.Contains("not available") ||
                    reason.Contains("does not support") ||
                    reason.Contains("not yet supported") ||
                    reason.Contains("unsupported") ||
                    reason.Contains("unavailable") ||
                    reason.Contains("service failed to initialize"))
                {
                    return false;
                }

                return true;
            }
        }

        public string UndervoltSectionTitle => IsAmdCpu ? "CPU Curve Optimizer (AMD)" : "CPU Undervolting";

        public string UndervoltActionLabel => IsAmdCpu ? "Apply Curve Optimizer" : "Apply Undervolt";

        public string UndervoltGuidanceText => IsAmdCpu
            ? "AMD uses Curve Optimizer (CO) instead of direct mV offset writes. More negative values are stronger CO undervolt."
            : "Reduce CPU voltage offset to lower temperature and power draw. More negative values apply stronger undervolt.";

        public string UndervoltBackendText
        {
            get
            {
                if (_undervoltService == null)
                {
                    return "Backend: unavailable";
                }

                var provider = _undervoltService.Provider;
                if (provider is AmdUndervoltProvider amd)
                {
                    return $"Backend: AMD Curve Optimizer via {amd.ActiveBackend}";
                }

                if (provider is IntelUndervoltProvider intel)
                {
                    return $"Backend: Intel voltage offset via {intel.ActiveBackend}";
                }

                return $"Backend: {provider.GetType().Name}";
            }
        }

        public string UndervoltLastActionText
        {
            get => _undervoltLastActionText;
            private set
            {
                if (_undervoltLastActionText != value)
                {
                    _undervoltLastActionText = value;
                    OnPropertyChanged();
                }
            }
        }

        public System.Windows.Media.Brush UndervoltLastActionColor
        {
            get => _undervoltLastActionColor;
            private set
            {
                if (_undervoltLastActionColor != value)
                {
                    _undervoltLastActionColor = value;
                    OnPropertyChanged();
                }
            }
        }
        
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
            : !string.IsNullOrWhiteSpace(UndervoltStatus?.Warning)
                ? UndervoltStatus!.Warning!
            : !string.IsNullOrWhiteSpace(UndervoltStatus?.Error)
                ? UndervoltStatus!.Error!
            : (UndervoltStatus?.CurrentCoreOffsetMv != 0 || UndervoltStatus?.CurrentCacheOffsetMv != 0)
                ? (IsAmdCpu
                    ? $"Active CO: Core {UndervoltStatus?.CurrentCoreOffsetMv:+0;-0;0} mV eq. / iGPU {UndervoltStatus?.CurrentCacheOffsetMv:+0;-0;0} mV eq."
                    : $"Active: Core {UndervoltStatus?.CurrentCoreOffsetMv:+0;-0;0} mV, Cache {UndervoltStatus?.CurrentCacheOffsetMv:+0;-0;0} mV")
                : (IsAmdCpu ? "No Curve Optimizer offset active" : "No undervolt active");
        
        public System.Windows.Media.Brush UndervoltStatusColor => UndervoltStatus?.HasExternalController == true
            ? System.Windows.Media.Brushes.Orange
            : !string.IsNullOrWhiteSpace(UndervoltStatus?.Warning)
                ? System.Windows.Media.Brushes.Gold
            : !string.IsNullOrWhiteSpace(UndervoltStatus?.Error)
                ? System.Windows.Media.Brushes.OrangeRed
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

        // CPU Vendor Properties (for UI display)
        private string _cpuVendor = "Intel";
        public string CpuVendor
        {
            get => _cpuVendor;
            private set { _cpuVendor = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIntelCpu)); OnPropertyChanged(nameof(IsAmdCpu)); }
        }
        
        private string _cpuDisplayName = "CPU";
        public string CpuDisplayName
        {
            get => _cpuDisplayName;
            private set { _cpuDisplayName = value; OnPropertyChanged(); }
        }
        
        public bool IsIntelCpu => CpuVendor == "Intel";
        
        // AMD Power Control Properties
        private bool _isAmdCpu;
        public bool IsAmdCpu
        {
            get => _isAmdCpu;
            private set
            {
                _isAmdCpu = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AmdPowerLimitsAvailable));
                OnPropertyChanged(nameof(ShowAmdPowerUnavailableMessage));
                OnPropertyChanged(nameof(AmdPowerLimitsStatus));
                OnPropertyChanged(nameof(NoTuningAvailable));
            }
        }

        private uint _amdStapmLimitWatts = 25;
        public uint AmdStapmLimitWatts
        {
            get => _amdStapmLimitWatts;
            set
            {
                if (_amdStapmLimitWatts != value)
                {
                    _amdStapmLimitWatts = Math.Clamp(value, 15u, 54u);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AmdStapmLimitText));
                }
            }
        }

        public string AmdStapmLimitText => $"{AmdStapmLimitWatts}W";

        private uint _amdTempLimitC = 95;
        public uint AmdTempLimitC
        {
            get => _amdTempLimitC;
            set
            {
                if (_amdTempLimitC != value)
                {
                    _amdTempLimitC = Math.Clamp(value, 75u, 105u);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AmdTempLimitText));
                }
            }
        }

        public string AmdTempLimitText => $"{AmdTempLimitC}°C";

        public bool AmdPowerLimitsAvailable =>
            IsAmdCpu &&
            _undervoltService?.Provider is AmdUndervoltProvider amdProvider &&
            !string.Equals(amdProvider.ActiveBackend, "None", StringComparison.OrdinalIgnoreCase);

        public bool ShowAmdPowerUnavailableMessage => IsAmdCpu && !AmdPowerLimitsAvailable;

        public string AmdPowerLimitsStatus
        {
            get
            {
                if (!IsAmdCpu)
                {
                    return "AMD Ryzen CPU not detected.";
                }

                if (_undervoltService?.Provider is AmdUndervoltProvider amdProvider)
                {
                    if (string.Equals(amdProvider.ActiveBackend, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        return "AMD SMU backend unavailable. Install PawnIO and run OmenCore as administrator to enable STAPM/Tctl control.";
                    }

                    return $"Backend: {amdProvider.ActiveBackend}. STAPM and Tctl writes are available for this Ryzen platform.";
                }

                return "AMD power tuning provider is unavailable on this system.";
            }
        }

        public string PerformanceModeDescription => SelectedPerformanceMode?.Description ?? SelectedPerformanceMode?.Name switch
        {
            "Balanced" => "Normal mode - balanced performance",
            "Performance" => "High performance - maximum GPU wattage",
            "Eco" => "Power saving mode - reduced wattage",
            _ => "Select a performance mode"
        };

        public bool IsFanPerformanceLinked => _configService.Config.LinkFanToPerformanceMode;

        public string FanPerformanceLinkBadgeText => IsFanPerformanceLinked
            ? "Fan linked to performance"
            : "Fan independent";

        public string PerformanceModeFanPolicyHint => IsFanPerformanceLinked
            ? "This mode can also update fan policy because linked mode is enabled."
            : "This mode only changes power/performance behavior. Your current fan preset or custom curve stays active.";

        public bool ShowFanPerformanceInfoBanner => !IsFanPerformanceLinked && !_configService.Config.DismissedFanPerformanceDecouplingNotice;
        
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

        private double _latestGpuPowerWatts;
        private string _gpuFullPowerText = "Full power • waiting for telemetry";

        public bool IsGpuFullPowerActive => GpuPowerBoostAvailable &&
            (string.Equals(GpuPowerBoostLevel, "Maximum", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GpuPowerBoostLevel, "Extended", StringComparison.OrdinalIgnoreCase));

        public bool ShowGpuFullPowerPill => _hardwareMonitoringService != null && IsGpuFullPowerActive;

        public string GpuFullPowerText
        {
            get => _gpuFullPowerText;
            private set
            {
                if (_gpuFullPowerText != value)
                {
                    _gpuFullPowerText = value;
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
                    OnPropertyChanged(nameof(GpuPowerBoostStatusDescription));
                    OnPropertyChanged(nameof(IsGpuFullPowerActive));
                    OnPropertyChanged(nameof(ShowGpuFullPowerPill));
                    OnPropertyChanged(nameof(CurrentPerformanceModeIndicator));
                    if (IsGpuFullPowerActive)
                    {
                        RefreshGpuPowerPill();
                    }
                    
                    // Update fan service with new GPU power boost level
                    if (_fanService != null)
                    {
                        _fanService.GpuPowerBoostLevel = value;
                    }
                    
                    // Auto-save GPU power boost level to config
                    SaveGpuPowerBoostToConfig();
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
                    OnPropertyChanged(nameof(IsGpuFullPowerActive));
                    OnPropertyChanged(nameof(ShowGpuFullPowerPill));
                    OnPropertyChanged(nameof(CurrentPerformanceModeIndicator));
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

        public string GpuPowerBoostStatusDescription
        {
            get
            {
                if (!GpuPowerBoostAvailable)
                    return "GPU Power Boost not available on this system";

                var nvapiNote = GpuNvapiAvailable ?
                    " (NVAPI power limits available for fine-tuning)" : "";

                return $"{GpuPowerBoostDescription}{nvapiNote}";
            }
        }
        
        public ObservableCollection<string> GpuPowerBoostLevels { get; } = new() { "Minimum", "Medium", "Maximum", "Extended" };

        // GPU Overclocking (NVAPI)
        private int _gpuCoreClockOffset;
        public int GpuCoreClockOffset
        {
            get => _gpuCoreClockOffset;
            set
            {
                if (_gpuCoreClockOffset != value)
                {
                    _gpuCoreClockOffset = Math.Clamp(value, GpuCoreOffsetMin, GpuCoreOffsetMax);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuCoreClockOffsetText));
                }
            }
        }

        public string GpuCoreClockOffsetText => GpuCoreClockOffset >= 0 ? $"+{GpuCoreClockOffset} MHz" : $"{GpuCoreClockOffset} MHz";
        public string GpuCoreOffsetRangeText => $"{FormatSignedValue(GpuCoreOffsetMin, "MHz")} to {FormatSignedValue(GpuCoreOffsetMax, "MHz")}";

        private int _gpuMemoryClockOffset;
        public int GpuMemoryClockOffset
        {
            get => _gpuMemoryClockOffset;
            set
            {
                if (_gpuMemoryClockOffset != value)
                {
                    _gpuMemoryClockOffset = Math.Clamp(value, GpuMemoryOffsetMin, GpuMemoryOffsetMax);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuMemoryClockOffsetText));
                }
            }
        }

        public string GpuMemoryClockOffsetText => GpuMemoryClockOffset >= 0 ? $"+{GpuMemoryClockOffset} MHz" : $"{GpuMemoryClockOffset} MHz";
        public string GpuMemoryOffsetRangeText => $"{FormatSignedValue(GpuMemoryOffsetMin, "MHz")} to {FormatSignedValue(GpuMemoryOffsetMax, "MHz")}";

        private int _gpuPowerLimitPercent = 100;
        public int GpuPowerLimitPercent
        {
            get => _gpuPowerLimitPercent;
            set
            {
                if (_gpuPowerLimitPercent != value)
                {
                    _gpuPowerLimitPercent = Math.Clamp(value, GpuPowerLimitMin, GpuPowerLimitMax);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuPowerLimitText));
                }
            }
        }

        public string GpuPowerLimitText => $"{GpuPowerLimitPercent}%";
        public string GpuPowerLimitRangeText => $"{GpuPowerLimitMin}% to {GpuPowerLimitMax}%";

        public bool IsGpuOcTestPending
        {
            get => _gpuOcTestPending;
            private set
            {
                if (_gpuOcTestPending != value)
                {
                    _gpuOcTestPending = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuOcTestActionText));
                    (TestGpuOcCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (ConfirmGpuOcTestCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ApplyGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ResetGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int GpuOcTestCountdownSeconds
        {
            get => _gpuOcTestCountdownSeconds;
            private set
            {
                if (_gpuOcTestCountdownSeconds != value)
                {
                    _gpuOcTestCountdownSeconds = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuOcTestActionText));
                }
            }
        }

        public string GpuOcTestStatusText
        {
            get => _gpuOcTestStatusText;
            private set
            {
                if (_gpuOcTestStatusText != value)
                {
                    _gpuOcTestStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GpuOcTestActionText => IsGpuOcTestPending
            ? $"Test active: auto-revert in {GpuOcTestCountdownSeconds}s unless you press Keep"
            : "Test Apply runs the selected tuning for 30 seconds, then restores the previous GPU state unless you keep it.";

        private int _gpuVoltageOffsetMv;
        public int GpuVoltageOffsetMv
        {
            get => _gpuVoltageOffsetMv;
            set
            {
                if (_gpuVoltageOffsetMv != value)
                {
                    _gpuVoltageOffsetMv = Math.Clamp(value, -200, 100);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuVoltageOffsetText));
                }
            }
        }

        public string GpuVoltageOffsetText => GpuVoltageOffsetMv >= 0 ? $"+{GpuVoltageOffsetMv} mV" : $"{GpuVoltageOffsetMv} mV";

        private bool _gpuOcAvailable;
        public bool GpuOcAvailable
        {
            get => _gpuOcAvailable;
            private set
            {
                if (_gpuOcAvailable != value)
                {
                    _gpuOcAvailable = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuOcNotAvailable));
                    NotifyGpuOcMetadataChanged();
                    (ApplyGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ResetGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (TestGpuOcCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _gpuNvapiAvailable;
        public bool GpuNvapiAvailable
        {
            get => _gpuNvapiAvailable;
            private set
            {
                if (_gpuNvapiAvailable != value)
                {
                    _gpuNvapiAvailable = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuOcNotAvailable));
                    NotifyGpuOcMetadataChanged();
                    (ApplyGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ResetGpuOcCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (TestGpuOcCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }
        
        /// <summary>
        /// True when NVAPI is available but clock offsets are not supported.
        /// Used to show informational message in UI.
        /// </summary>
        public bool GpuOcNotAvailable => GpuNvapiAvailable && !GpuOcAvailable;
        public bool GpuExtendedOffsetsAvailable => GpuCoreOffsetMax > 200 || GpuMemoryOffsetMax > 500 || GpuPowerLimitMax > 115;
        public string GpuOcCapabilityBadgeText => GpuOcNotAvailable ? "Power Limit Only" : GpuExtendedOffsetsAvailable ? "Extended Range" : "Detected Range";
        public string GpuOcCapabilityDescription => GpuOcNotAvailable
            ? "This GPU/driver exposes NVAPI power tuning, but clock and voltage offsets remain locked."
            : GpuExtendedOffsetsAvailable
                ? "NVAPI reported a wider-than-default range for this GPU. Values near the top end should be treated as experimental."
                : "This GPU is using the standard laptop-safe NVAPI range. Stability still varies by silicon and BIOS.";
        public string GpuOcDetectedLimitHeadline => string.IsNullOrWhiteSpace(GpuDisplayName)
            ? "Detected GPU tuning limits will appear after initialization."
            : GpuOcNotAvailable
                ? $"Detected power range for {GpuDisplayName}: {GpuPowerLimitRangeText}. Clock offsets are blocked by the current driver or firmware path."
                : $"Detected limits for {GpuDisplayName}: Core {GpuCoreOffsetRangeText}, Memory {GpuMemoryOffsetRangeText}, Power {GpuPowerLimitRangeText}.";
        public string GpuOcRecommendationText => BuildGpuOcRecommendationText();
        
        /// <summary>
        /// True when no tuning features are available at all.
        /// Used to show "no tuning available" message in Tuning tab.
        /// </summary>
        public bool NoTuningAvailable => !IsUndervoltSupported && !TccStatus.IsSupported && !GpuNvapiAvailable && !GpuAmdAvailable && !GpuPowerBoostAvailable && !CpuPowerLimitsAvailable && !AmdPowerLimitsAvailable;

        // GPU Vendor detection and info
        private string _gpuVendor = "Unknown";
        public string GpuVendor
        {
            get => _gpuVendor;
            private set
            {
                if (_gpuVendor != value)
                {
                    _gpuVendor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNvidiaGpu));
                    OnPropertyChanged(nameof(IsAmdGpu));
                    OnPropertyChanged(nameof(GpuOcSectionVisible));
                }
            }
        }
        
        public bool IsNvidiaGpu => GpuVendor == "NVIDIA";
        public bool IsAmdGpu => GpuVendor == "AMD";
        public bool GpuOcSectionVisible => GpuNvapiAvailable || GpuAmdAvailable;
        
        private string _gpuDriverVersion = "";
        public string GpuDriverVersion
        {
            get => _gpuDriverVersion;
            private set
            {
                if (_gpuDriverVersion != value)
                {
                    _gpuDriverVersion = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private string _gpuDisplayName = "";
        public string GpuDisplayName
        {
            get => _gpuDisplayName;
            private set
            {
                if (_gpuDisplayName != value)
                {
                    _gpuDisplayName = value;
                    OnPropertyChanged();
                    NotifyGpuOcMetadataChanged();
                }
            }
        }
        
        private bool _gpuAmdAvailable;
        public bool GpuAmdAvailable
        {
            get => _gpuAmdAvailable;
            private set
            {
                if (_gpuAmdAvailable != value)
                {
                    _gpuAmdAvailable = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GpuOcSectionVisible));
                }
            }
        }

        // AMD GPU OC properties
        private int _amdCoreClockOffset;
        public int AmdCoreClockOffset
        {
            get => _amdCoreClockOffset;
            set { if (_amdCoreClockOffset != value) { _amdCoreClockOffset = value; OnPropertyChanged(); } }
        }

        private int _amdMemoryClockOffset;
        public int AmdMemoryClockOffset
        {
            get => _amdMemoryClockOffset;
            set { if (_amdMemoryClockOffset != value) { _amdMemoryClockOffset = value; OnPropertyChanged(); } }
        }

        private int _amdPowerLimitPercent;
        public int AmdPowerLimitPercent
        {
            get => _amdPowerLimitPercent;
            set { if (_amdPowerLimitPercent != value) { _amdPowerLimitPercent = value; OnPropertyChanged(); } }
        }

        public ICommand ApplyAmdGpuOcCommand { get; private set; } = null!;
        public ICommand ResetAmdGpuCommand { get; private set; } = null!;

        private string _gpuOcStatus = "Not available";
        public string GpuOcStatus
        {
            get => _gpuOcStatus;
            private set
            {
                if (_gpuOcStatus != value)
                {
                    _gpuOcStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        // GPU OC limits (set by NvapiService based on GPU type)
        public int GpuCoreOffsetMin { get; private set; } = -500;
        public int GpuCoreOffsetMax { get; private set; } = 200;
        public int GpuMemoryOffsetMin { get; private set; } = -500;
        public int GpuMemoryOffsetMax { get; private set; } = 500;
        public int GpuPowerLimitMin { get; private set; } = 50;
        public int GpuPowerLimitMax { get; private set; } = 115;

        private void NotifyGpuOcMetadataChanged()
        {
            OnPropertyChanged(nameof(GpuOcNotAvailable));
            OnPropertyChanged(nameof(GpuExtendedOffsetsAvailable));
            OnPropertyChanged(nameof(GpuOcCapabilityBadgeText));
            OnPropertyChanged(nameof(GpuOcCapabilityDescription));
            OnPropertyChanged(nameof(GpuOcDetectedLimitHeadline));
            OnPropertyChanged(nameof(GpuOcRecommendationText));
            OnPropertyChanged(nameof(GpuCoreOffsetRangeText));
            OnPropertyChanged(nameof(GpuMemoryOffsetRangeText));
            OnPropertyChanged(nameof(GpuPowerLimitRangeText));
        }

        private static string FormatSignedValue(int value, string unit)
        {
            return value >= 0 ? $"+{value} {unit}" : $"{value} {unit}";
        }

        private bool IsLaptopGpuModel()
        {
            return GpuDisplayName.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
                   GpuDisplayName.Contains("Max-Q", StringComparison.OrdinalIgnoreCase) ||
                   GpuDisplayName.Contains("Mobile", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRtx50SeriesLaptop()
        {
            return IsLaptopGpuModel() && GpuDisplayName.Contains("RTX 50", StringComparison.OrdinalIgnoreCase);
        }

        private int GetRecommendedGpuCoreOffset()
        {
            var preferred = IsRtx50SeriesLaptop() ? 150 : IsLaptopGpuModel() ? 125 : 175;
            return Math.Clamp(preferred, GpuCoreOffsetMin, GpuCoreOffsetMax);
        }

        private int GetRecommendedGpuMemoryOffset()
        {
            var preferred = IsRtx50SeriesLaptop() ? 400 : IsLaptopGpuModel() ? 300 : 500;
            return Math.Clamp(preferred, GpuMemoryOffsetMin, GpuMemoryOffsetMax);
        }

        private int GetRecommendedGpuPowerLimit()
        {
            var preferred = IsLaptopGpuModel() ? 105 : 110;
            if (GpuPowerLimitMax < 100)
            {
                preferred = GpuPowerLimitMax;
            }

            return Math.Clamp(preferred, GpuPowerLimitMin, GpuPowerLimitMax);
        }

        private string BuildGpuOcRecommendationText()
        {
            if (!GpuNvapiAvailable)
            {
                return "NVAPI tuning guidance will appear after the GPU is detected.";
            }

            if (GpuOcNotAvailable)
            {
                return $"Model-aware guardrail: this {GetGpuOcPlatformLabel()} currently supports power tuning only. Start near {GetRecommendedGpuPowerLimit()}% and treat any clock work as an external-tool workflow.";
            }

            return $"Model-aware guardrail for {GetGpuOcPlatformLabel()}: start around Core {FormatSignedValue(GetRecommendedGpuCoreOffset(), "MHz")}, Memory {FormatSignedValue(GetRecommendedGpuMemoryOffset(), "MHz")}, Power {GetRecommendedGpuPowerLimit()}%. Values close to the detected max should be considered experimental until validated under load.";
        }

        private string GetGpuOcPlatformLabel()
        {
            if (IsRtx50SeriesLaptop())
            {
                return "RTX 50-series laptop GPUs";
            }

            if (IsLaptopGpuModel())
            {
                return "laptop NVIDIA GPUs";
            }

            if (GpuDisplayName.Contains("RTX 40", StringComparison.OrdinalIgnoreCase))
            {
                return "RTX 40-series GPUs";
            }

            if (GpuDisplayName.Contains("RTX 30", StringComparison.OrdinalIgnoreCase))
            {
                return "RTX 30-series GPUs";
            }

            return string.IsNullOrWhiteSpace(GpuDisplayName) ? "this GPU" : GpuDisplayName;
        }

        private string ApplyGpuOcRequestToUi(int requestedCoreOffset, int requestedMemoryOffset, int requestedPowerLimit, int requestedVoltageOffset)
        {
            GpuCoreClockOffset = GpuOcAvailable ? requestedCoreOffset : 0;
            GpuMemoryClockOffset = GpuOcAvailable ? requestedMemoryOffset : 0;
            GpuPowerLimitPercent = requestedPowerLimit;
            GpuVoltageOffsetMv = GpuOcAvailable ? requestedVoltageOffset : 0;

            var adjustments = new List<string>();
            if (requestedCoreOffset != GpuCoreClockOffset)
            {
                adjustments.Add($"core {FormatSignedValue(requestedCoreOffset, "MHz")} -> {FormatSignedValue(GpuCoreClockOffset, "MHz")}");
            }

            if (requestedMemoryOffset != GpuMemoryClockOffset)
            {
                adjustments.Add($"memory {FormatSignedValue(requestedMemoryOffset, "MHz")} -> {FormatSignedValue(GpuMemoryClockOffset, "MHz")}");
            }

            if (requestedPowerLimit != GpuPowerLimitPercent)
            {
                adjustments.Add($"power {requestedPowerLimit}% -> {GpuPowerLimitPercent}%");
            }

            if (requestedVoltageOffset != GpuVoltageOffsetMv)
            {
                adjustments.Add($"voltage {FormatSignedValue(requestedVoltageOffset, "mV")} -> {FormatSignedValue(GpuVoltageOffsetMv, "mV")}");
            }

            return adjustments.Count == 0 ? string.Empty : string.Join(", ", adjustments);
        }

        private sealed class GpuOcSnapshot
        {
            public int CoreClockOffsetMHz { get; init; }
            public int MemoryClockOffsetMHz { get; init; }
            public int PowerLimitPercent { get; init; }
            public int VoltageOffsetMv { get; init; }
        }

        private sealed class GpuOcApplyResult
        {
            public required bool CoreSuccess { get; init; }
            public required bool MemorySuccess { get; init; }
            public required bool PowerSuccess { get; init; }
            public required bool VoltageSuccess { get; init; }
            public required string StatusText { get; init; }
            public string? GuardrailNote { get; init; }
            public bool Success => CoreSuccess && MemorySuccess && PowerSuccess && VoltageSuccess;
        }

        private static readonly HashSet<string> BuiltInGpuOcProfileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Stock (Default)",
            "Safe",
            "Balanced",
            "Max Experimental"
        };

        private GpuOcSnapshot CaptureCurrentGpuOcSnapshot()
        {
            return new GpuOcSnapshot
            {
                CoreClockOffsetMHz = _nvapiService?.CoreClockOffsetMHz ?? GpuCoreClockOffset,
                MemoryClockOffsetMHz = _nvapiService?.MemoryClockOffsetMHz ?? GpuMemoryClockOffset,
                PowerLimitPercent = _nvapiService?.PowerLimitPercent ?? GpuPowerLimitPercent,
                VoltageOffsetMv = _nvapiService?.VoltageOffsetMv ?? GpuVoltageOffsetMv
            };
        }

        private bool IsBuiltInGpuOcProfile(string? profileName)
        {
            return !string.IsNullOrWhiteSpace(profileName) && BuiltInGpuOcProfileNames.Contains(profileName);
        }

        private int ClampPresetValue(int preferredValue, int minValue, int maxValue, int step)
        {
            if (maxValue <= 0)
            {
                return 0;
            }

            var clamped = Math.Clamp(preferredValue, Math.Max(0, minValue), maxValue);
            if (step <= 1)
            {
                return clamped;
            }

            return (clamped / step) * step;
        }

        private List<Models.GpuOcProfile> BuildBuiltInGpuOcProfiles()
        {
            var profiles = new List<Models.GpuOcProfile>
            {
                new()
                {
                    Name = "Stock (Default)",
                    CoreClockOffsetMHz = 0,
                    MemoryClockOffsetMHz = 0,
                    PowerLimitPercent = 100,
                    VoltageOffsetMv = 0,
                    Description = "Factory defaults - no overclocking"
                }
            };

            if (GpuOcNotAvailable)
            {
                profiles.Add(new Models.GpuOcProfile
                {
                    Name = "Safe",
                    CoreClockOffsetMHz = 0,
                    MemoryClockOffsetMHz = 0,
                    PowerLimitPercent = Math.Clamp(95, GpuPowerLimitMin, GpuPowerLimitMax),
                    VoltageOffsetMv = 0,
                    Description = "Power-only profile for cooler, quieter GPU behavior"
                });
                profiles.Add(new Models.GpuOcProfile
                {
                    Name = "Balanced",
                    CoreClockOffsetMHz = 0,
                    MemoryClockOffsetMHz = 0,
                    PowerLimitPercent = Math.Clamp(100, GpuPowerLimitMin, GpuPowerLimitMax),
                    VoltageOffsetMv = 0,
                    Description = "Power-only profile matching the detected stock envelope"
                });
                profiles.Add(new Models.GpuOcProfile
                {
                    Name = "Max Experimental",
                    CoreClockOffsetMHz = 0,
                    MemoryClockOffsetMHz = 0,
                    PowerLimitPercent = GpuPowerLimitMax,
                    VoltageOffsetMv = 0,
                    Description = "Uses the highest detected power limit. Validate stability and temperatures before keeping it."
                });
                return profiles;
            }

            var safeCore = ClampPresetValue(Math.Max(50, GetRecommendedGpuCoreOffset() / 2), GpuCoreOffsetMin, GpuCoreOffsetMax, 25);
            var safeMemory = ClampPresetValue(Math.Max(150, GetRecommendedGpuMemoryOffset() / 2), GpuMemoryOffsetMin, GpuMemoryOffsetMax, 50);
            var safePower = Math.Clamp(100, GpuPowerLimitMin, GpuPowerLimitMax);

            var balancedCore = ClampPresetValue(GetRecommendedGpuCoreOffset(), GpuCoreOffsetMin, GpuCoreOffsetMax, 25);
            var balancedMemory = ClampPresetValue(GetRecommendedGpuMemoryOffset(), GpuMemoryOffsetMin, GpuMemoryOffsetMax, 50);
            var balancedPower = GetRecommendedGpuPowerLimit();

            var maxCore = ClampPresetValue((int)Math.Floor(Math.Max(0, GpuCoreOffsetMax) * 0.9), GpuCoreOffsetMin, GpuCoreOffsetMax, 25);
            var maxMemory = ClampPresetValue((int)Math.Floor(Math.Max(0, GpuMemoryOffsetMax) * 0.85), GpuMemoryOffsetMin, GpuMemoryOffsetMax, 50);
            var maxPower = GpuPowerLimitMax;

            profiles.Add(new Models.GpuOcProfile
            {
                Name = "Safe",
                CoreClockOffsetMHz = safeCore,
                MemoryClockOffsetMHz = safeMemory,
                PowerLimitPercent = safePower,
                VoltageOffsetMv = 0,
                Description = "Conservative starting point tuned to the detected GPU range"
            });
            profiles.Add(new Models.GpuOcProfile
            {
                Name = "Balanced",
                CoreClockOffsetMHz = balancedCore,
                MemoryClockOffsetMHz = balancedMemory,
                PowerLimitPercent = balancedPower,
                VoltageOffsetMv = 0,
                Description = $"Device-aware daily profile for {GetGpuOcPlatformLabel()}"
            });
            profiles.Add(new Models.GpuOcProfile
            {
                Name = "Max Experimental",
                CoreClockOffsetMHz = maxCore,
                MemoryClockOffsetMHz = maxMemory,
                PowerLimitPercent = maxPower,
                VoltageOffsetMv = 0,
                Description = "Near-top detected range. Use Test Apply first and keep only after stability validation."
            });

            return profiles;
        }

        private GpuOcApplyResult ApplyGpuOcValues(int requestedCoreOffset, int requestedMemoryOffset, int requestedPowerLimit, int requestedVoltageOffset, bool persistToConfig, string successPrefix)
        {
            var guardrailNote = ApplyGpuOcRequestToUi(requestedCoreOffset, requestedMemoryOffset, requestedPowerLimit, requestedVoltageOffset);

            bool coreSuccess = true;
            bool memSuccess = true;
            bool powerSuccess = true;
            bool voltageSuccess = true;

            if (_nvapiService == null || !GpuNvapiAvailable)
            {
                return new GpuOcApplyResult
                {
                    CoreSuccess = false,
                    MemorySuccess = false,
                    PowerSuccess = false,
                    VoltageSuccess = false,
                    StatusText = "GPU overclocking not available",
                    GuardrailNote = guardrailNote
                };
            }

            try
            {
                if (GpuOcAvailable && (GpuCoreClockOffset != 0 || _nvapiService.CoreClockOffsetMHz != 0))
                {
                    coreSuccess = _nvapiService.SetCoreClockOffset(GpuCoreClockOffset);
                    _logging.Info($"GPU core clock offset {(coreSuccess ? "applied" : "failed")}: {GpuCoreClockOffset} MHz");
                }

                if (GpuOcAvailable && (GpuMemoryClockOffset != 0 || _nvapiService.MemoryClockOffsetMHz != 0))
                {
                    memSuccess = _nvapiService.SetMemoryClockOffset(GpuMemoryClockOffset);
                    _logging.Info($"GPU memory clock offset {(memSuccess ? "applied" : "failed")}: {GpuMemoryClockOffset} MHz");
                }

                if (GpuPowerLimitPercent != 100 || _nvapiService.PowerLimitPercent != 100)
                {
                    powerSuccess = _nvapiService.SetPowerLimit(GpuPowerLimitPercent);
                    _logging.Info($"GPU power limit {(powerSuccess ? "applied" : "failed")}: {GpuPowerLimitPercent}%");
                }

                if (GpuOcAvailable && (GpuVoltageOffsetMv != 0 || _nvapiService.VoltageOffsetMv != 0))
                {
                    voltageSuccess = _nvapiService.SetVoltageOffset(GpuVoltageOffsetMv);
                    _logging.Info($"GPU voltage offset {(voltageSuccess ? "applied" : "failed")}: {GpuVoltageOffsetMv} mV");
                }

                if (coreSuccess && memSuccess && powerSuccess && voltageSuccess)
                {
                    if (persistToConfig)
                    {
                        SaveGpuOcToConfig();
                    }

                    var statusText = GpuOcAvailable
                        ? $"{successPrefix}: Core {GpuCoreClockOffsetText}, Mem {GpuMemoryClockOffsetText}, Power {GpuPowerLimitText}, Voltage {GpuVoltageOffsetText}"
                        : $"{successPrefix}: Power {GpuPowerLimitText} (clock offsets unavailable on this GPU/driver)";

                    if (!string.IsNullOrEmpty(guardrailNote))
                    {
                        statusText += $" | Guardrails: {guardrailNote}";
                    }

                    return new GpuOcApplyResult
                    {
                        CoreSuccess = true,
                        MemorySuccess = true,
                        PowerSuccess = true,
                        VoltageSuccess = true,
                        StatusText = statusText,
                        GuardrailNote = guardrailNote
                    };
                }

                var failures = new List<string>();
                if (!coreSuccess) failures.Add("core");
                if (!memSuccess) failures.Add("memory");
                if (!powerSuccess) failures.Add("power");
                if (!voltageSuccess) failures.Add("voltage");

                return new GpuOcApplyResult
                {
                    CoreSuccess = coreSuccess,
                    MemorySuccess = memSuccess,
                    PowerSuccess = powerSuccess,
                    VoltageSuccess = voltageSuccess,
                    StatusText = $"⚠ Partial: {string.Join(", ", failures)} failed - API may be restricted",
                    GuardrailNote = guardrailNote
                };
            }
            catch (Exception ex)
            {
                _logging.Error($"GPU OC apply failed: {ex.Message}", ex);
                return new GpuOcApplyResult
                {
                    CoreSuccess = false,
                    MemorySuccess = false,
                    PowerSuccess = false,
                    VoltageSuccess = false,
                    StatusText = $"Error: {ex.Message}",
                    GuardrailNote = guardrailNote
                };
            }
        }

        private async Task StartGpuOcTestApplyAsync()
        {
            if (_nvapiService == null || !GpuNvapiAvailable || IsGpuOcTestPending)
            {
                return;
            }

            var confirmation = System.Windows.MessageBox.Show(
                "Test Apply will apply the current GPU tuning for 30 seconds and then automatically restore the previous GPU state unless you press Keep. Continue?",
                "Test GPU Tuning",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirmation != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            _gpuOcTestSnapshot = CaptureCurrentGpuOcSnapshot();
            var testResult = ApplyGpuOcValues(GpuCoreClockOffset, GpuMemoryClockOffset, GpuPowerLimitPercent, GpuVoltageOffsetMv, false, "✓ Test applied");
            GpuOcStatus = testResult.StatusText;

            if (!testResult.Success)
            {
                _gpuOcTestSnapshot = null;
                GpuOcTestStatusText = "Test Apply did not start because one or more GPU settings failed to apply.";
                return;
            }

            _gpuOcTestCancellation?.Cancel();
            _gpuOcTestCancellation?.Dispose();
            _gpuOcTestCancellation = new CancellationTokenSource();

            IsGpuOcTestPending = true;
            GpuOcTestCountdownSeconds = 30;
            GpuOcTestStatusText = "Test tuning is live. Run a quick benchmark or game, then press Keep if stable.";
            _logging.Info("GPU OC test apply started for 30 seconds");

            try
            {
                while (GpuOcTestCountdownSeconds > 0)
                {
                    await Task.Delay(1000, _gpuOcTestCancellation.Token);
                    GpuOcTestCountdownSeconds--;
                }

                RevertGpuOcTest("GPU test expired; previous tuning restored.");
            }
            catch (OperationCanceledException)
            {
                _logging.Info("GPU OC test apply countdown cancelled");
            }
        }

        private void ConfirmGpuOcTest()
        {
            if (!IsGpuOcTestPending)
            {
                return;
            }

            _gpuOcTestCancellation?.Cancel();
            _gpuOcTestCancellation?.Dispose();
            _gpuOcTestCancellation = null;
            _gpuOcTestSnapshot = null;
            IsGpuOcTestPending = false;
            GpuOcTestCountdownSeconds = 0;
            SaveGpuOcToConfig();
            GpuOcStatus = GpuOcAvailable
                ? $"✓ Test confirmed: Core {GpuCoreClockOffsetText}, Mem {GpuMemoryClockOffsetText}, Power {GpuPowerLimitText}, Voltage {GpuVoltageOffsetText}"
                : $"✓ Test confirmed: Power {GpuPowerLimitText}";
            GpuOcTestStatusText = "Test tuning was kept and saved for startup reapply.";
            _logging.Info("GPU OC test apply confirmed by user");
        }

        private void RevertGpuOcTest(string statusText)
        {
            var snapshot = _gpuOcTestSnapshot;

            _gpuOcTestCancellation?.Dispose();
            _gpuOcTestCancellation = null;
            _gpuOcTestSnapshot = null;
            IsGpuOcTestPending = false;
            GpuOcTestCountdownSeconds = 0;

            if (snapshot == null)
            {
                GpuOcTestStatusText = "No test snapshot was available to restore.";
                return;
            }

            var revertResult = ApplyGpuOcValues(
                snapshot.CoreClockOffsetMHz,
                snapshot.MemoryClockOffsetMHz,
                snapshot.PowerLimitPercent,
                snapshot.VoltageOffsetMv,
                false,
                "✓ Reverted");

            GpuOcStatus = revertResult.StatusText;
            GpuOcTestStatusText = statusText;
            _logging.Info(statusText);
        }
        
        // GPU OC Profiles
        public ObservableCollection<Models.GpuOcProfile> GpuOcProfiles { get; } = new();
        
        private Models.GpuOcProfile? _selectedGpuOcProfile;
        public Models.GpuOcProfile? SelectedGpuOcProfile
        {
            get => _selectedGpuOcProfile;
            set
            {
                if (_selectedGpuOcProfile != value)
                {
                    _selectedGpuOcProfile = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        LoadGpuOcProfile(value);
                    }
                }
            }
        }
        
        private string _newGpuOcProfileName = "";
        public string NewGpuOcProfileName
        {
            get => _newGpuOcProfileName;
            set
            {
                if (_newGpuOcProfileName != value)
                {
                    _newGpuOcProfileName = value;
                    OnPropertyChanged();
                }
            }
        }

        // CPU Power Limits (PL1/PL2)
        private int _cpuPl1Watts = 45;
        public int CpuPl1Watts
        {
            get => _cpuPl1Watts;
            set
            {
                if (_cpuPl1Watts != value)
                {
                    _cpuPl1Watts = Math.Clamp(value, CpuPl1Min, CpuPl1Max);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CpuPl1Text));
                }
            }
        }

        public string CpuPl1Text => $"{CpuPl1Watts}W";

        private int _cpuPl2Watts = 65;
        public int CpuPl2Watts
        {
            get => _cpuPl2Watts;
            set
            {
                if (_cpuPl2Watts != value)
                {
                    _cpuPl2Watts = Math.Clamp(value, CpuPl2Min, CpuPl2Max);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CpuPl2Text));
                }
            }
        }

        public string CpuPl2Text => $"{CpuPl2Watts}W";

        private bool _cpuPowerLimitsAvailable;
        public bool CpuPowerLimitsAvailable
        {
            get => _cpuPowerLimitsAvailable;
            private set
            {
                if (_cpuPowerLimitsAvailable != value)
                {
                    _cpuPowerLimitsAvailable = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _cpuPowerLimitsStatus = "Detecting...";
        public string CpuPowerLimitsStatus
        {
            get => _cpuPowerLimitsStatus;
            private set
            {
                if (_cpuPowerLimitsStatus != value)
                {
                    _cpuPowerLimitsStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _cpuPowerLimitsLocked;
        public bool CpuPowerLimitsLocked
        {
            get => _cpuPowerLimitsLocked;
            private set
            {
                if (_cpuPowerLimitsLocked != value)
                {
                    _cpuPowerLimitsLocked = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _currentPl1Watts;
        public double CurrentPl1Watts
        {
            get => _currentPl1Watts;
            private set
            {
                if (Math.Abs(_currentPl1Watts - value) > 0.1)
                {
                    _currentPl1Watts = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPl1Text));
                }
            }
        }

        public string CurrentPl1Text => $"Current: {CurrentPl1Watts:F0}W";

        private double _currentPl2Watts;
        public double CurrentPl2Watts
        {
            get => _currentPl2Watts;
            private set
            {
                if (Math.Abs(_currentPl2Watts - value) > 0.1)
                {
                    _currentPl2Watts = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPl2Text));
                }
            }
        }

        public string CurrentPl2Text => $"Current: {CurrentPl2Watts:F0}W";

        public string CpuPl1Description => "Sustained power limit (PL1) - Long-term sustained TDP";
        public string CpuPl2Description => "Burst power limit (PL2) - Short-term turbo boost power";

        // CPU PL limits (vary by CPU model)
        public int CpuPl1Min { get; private set; } = 15;
        public int CpuPl1Max { get; private set; } = 65;
        public int CpuPl2Min { get; private set; } = 25;
        public int CpuPl2Max { get; private set; } = 115;
        
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

        // Display Overdrive
        private bool _displayOverdriveEnabled;
        public bool DisplayOverdriveEnabled
        {
            get => _displayOverdriveEnabled;
            set
            {
                if (_displayOverdriveEnabled != value)
                {
                    _displayOverdriveEnabled = value;
                    OnPropertyChanged(nameof(DisplayOverdriveEnabled));
                    // Apply immediately when toggled
                    _ = SetDisplayOverdriveAsync(value);
                }
            }
        }

        private bool _displayOverdriveSupported;
        public bool DisplayOverdriveSupported
        {
            get => _displayOverdriveSupported;
            private set
            {
                if (_displayOverdriveSupported != value)
                {
                    _displayOverdriveSupported = value;
                    OnPropertyChanged(nameof(DisplayOverdriveSupported));
                }
            }
        }

        private async Task SetDisplayOverdriveAsync(bool enabled)
        {
            if (_wmiBios == null) return;
            
            await Task.Run(() =>
            {
                try
                {
                    var success = _wmiBios.SetDisplayOverdrive(enabled);
                    if (!success)
                    {
                        _logging.Warn($"Display overdrive toggle failed");
                        // Revert UI
                        _displayOverdriveEnabled = !enabled;
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => OnPropertyChanged(nameof(DisplayOverdriveEnabled)));
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Display overdrive error: {ex.Message}", ex);
                    _displayOverdriveEnabled = !enabled;
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => OnPropertyChanged(nameof(DisplayOverdriveEnabled)));
                }
            });
        }

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
        public ICommand ApplyGpuOcCommand { get; }
        public ICommand ResetGpuOcCommand { get; }
        public ICommand TestGpuOcCommand { get; }
        public ICommand ConfirmGpuOcTestCommand { get; }
        public ICommand SaveGpuOcProfileCommand { get; }
        public ICommand DeleteGpuOcProfileCommand { get; }
        public ICommand ApplyAmdPowerLimitsCommand { get; }
        public ICommand ResetAmdPowerLimitsCommand { get; }
        public ICommand ApplyCpuPowerLimitsCommand { get; }
        public ICommand ResetCpuPowerLimitsCommand { get; }
        public ICommand RefreshCpuPowerLimitsCommand { get; }
        public ICommand DismissFanPerformanceInfoBannerCommand { get; }

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
            SystemInfoService? systemInfoService = null,
            NvapiService? nvapiService = null,
            FanService? fanService = null,
            HardwareMonitoringService? hardwareMonitoringService = null,
            IEcAccess? ecAccess = null,
            AmdGpuService? amdGpuService = null)
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
            _nvapiService = nvapiService;
            _fanService = fanService;
            _hardwareMonitoringService = hardwareMonitoringService;
            _ecAccess = ecAccess;
            _amdGpuService = amdGpuService;

            if (_hardwareMonitoringService != null)
            {
                _hardwareMonitoringService.SampleUpdated += OnMonitoringSampleUpdated;
            }

            // Initialize GPU OC if available
            InitializeGpuOc();

            _undervoltService.StatusChanged += (s, status) => 
            {
                UndervoltStatus = status;
                OnPropertyChanged(nameof(UndervoltStatusText));
                OnPropertyChanged(nameof(UndervoltStatusColor));
                OnPropertyChanged(nameof(IsUndervoltSupported));
                OnPropertyChanged(nameof(UndervoltSectionTitle));
                OnPropertyChanged(nameof(UndervoltActionLabel));
                OnPropertyChanged(nameof(UndervoltGuidanceText));
                OnPropertyChanged(nameof(UndervoltBackendText));
                OnPropertyChanged(nameof(HasExternalUndervoltController));
                OnPropertyChanged(nameof(ExternalControllerName));
                OnPropertyChanged(nameof(ExternalControllerWarning));
                OnPropertyChanged(nameof(ExternalControllerHowToFix));
                (ApplyUndervoltCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            };

            ApplyPerformanceModeCommand = new RelayCommand(_ => ApplyPerformanceMode(), _ => SelectedPerformanceMode != null);
            SelectPerformanceModeCommand = new RelayCommand(param => SelectPerformanceMode(param?.ToString() ?? "Balanced"));
            ApplyUndervoltCommand = new AsyncRelayCommand(_ => ApplyUndervoltAsync(), _ => IsUndervoltSupported && (!RespectExternalUndervolt || !UndervoltStatus.HasExternalController));
            ResetUndervoltCommand = new AsyncRelayCommand(_ => ResetUndervoltAsync(), _ => IsUndervoltSupported);
            ApplyUndervoltPresetCommand = new AsyncRelayCommand(ApplyUndervoltPresetAsync);
            ApplyAggressiveUndervoltCommand = new AsyncRelayCommand(_ => ApplyAggressiveUndervoltAsync());
            CleanupOmenHubCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            RunCleanupCommand = new AsyncRelayCommand(_ => RunCleanupAsync(), _ => !CleanupInProgress);
            CreateRestorePointCommand = new AsyncRelayCommand(_ => CreateRestorePointAsync());
            SwitchGpuModeCommand = new AsyncRelayCommand(_ => SwitchGpuModeAsync());
            ApplyGpuPowerBoostCommand = new RelayCommand(_ => ApplyGpuPowerBoost(), _ => GpuPowerBoostAvailable);
            ApplyGpuOcCommand = new RelayCommand(_ => ApplyGpuOc(), _ => GpuNvapiAvailable && !IsGpuOcTestPending);
            ResetGpuOcCommand = new RelayCommand(_ => ResetGpuOc(), _ => GpuNvapiAvailable && !IsGpuOcTestPending);
            TestGpuOcCommand = new AsyncRelayCommand(_ => StartGpuOcTestApplyAsync(), _ => GpuNvapiAvailable && !IsGpuOcTestPending);
            ConfirmGpuOcTestCommand = new RelayCommand(_ => ConfirmGpuOcTest(), _ => IsGpuOcTestPending);
            
            // AMD GPU OC commands
            ApplyAmdGpuOcCommand = new RelayCommand(_ => ApplyAmdGpuOc(), _ => _amdGpuService?.IsAvailable == true);
            ResetAmdGpuCommand = new RelayCommand(_ => ResetAmdGpuOc(), _ => _amdGpuService?.IsAvailable == true);
            SaveGpuOcProfileCommand = new RelayCommand(_ => SaveGpuOcProfile(), _ => GpuOcAvailable && !string.IsNullOrWhiteSpace(NewGpuOcProfileName));
            DeleteGpuOcProfileCommand = new RelayCommand(_ => DeleteGpuOcProfile(), _ => SelectedGpuOcProfile != null);
            ApplyTccOffsetCommand = new RelayCommand(_ => ApplyTccOffset(), _ => TccStatus.IsSupported);
            ResetTccOffsetCommand = new RelayCommand(_ => ResetTccOffset(), _ => TccStatus.IsSupported);
            ApplyAmdPowerLimitsCommand = new RelayCommand(_ => ApplyAmdPowerLimits(), _ => AmdPowerLimitsAvailable);
            ResetAmdPowerLimitsCommand = new RelayCommand(_ => ResetAmdPowerLimits(), _ => AmdPowerLimitsAvailable);
            ApplyCpuPowerLimitsCommand = new RelayCommand(_ => ApplyCpuPowerLimits(), _ => CpuPowerLimitsAvailable && !CpuPowerLimitsLocked);
            ResetCpuPowerLimitsCommand = new RelayCommand(_ => ResetCpuPowerLimits(), _ => CpuPowerLimitsAvailable && !CpuPowerLimitsLocked);
            RefreshCpuPowerLimitsCommand = new RelayCommand(_ => RefreshCpuPowerLimits());
            DismissFanPerformanceInfoBannerCommand = new RelayCommand(_ => DismissFanPerformanceInfoBanner());
            
            // Detect AMD CPU
            var cpuVendorEnum = Hardware.CpuUndervoltProviderFactory.DetectedVendor;
            IsAmdCpu = cpuVendorEnum == Hardware.CpuUndervoltProviderFactory.CpuVendor.AMD;
            CpuVendor = cpuVendorEnum == Hardware.CpuUndervoltProviderFactory.CpuVendor.AMD ? "AMD" : "Intel";
            CpuDisplayName = Hardware.CpuUndervoltProviderFactory.CpuName;
            OnPropertyChanged(nameof(UndervoltSectionTitle));
            OnPropertyChanged(nameof(UndervoltActionLabel));
            OnPropertyChanged(nameof(UndervoltGuidanceText));
            OnPropertyChanged(nameof(UndervoltBackendText));
            
            // Restore AMD power limits if applicable
            if (IsAmdCpu)
            {
                RestoreAmdPowerLimitsFromConfig();
            }
            
            // Load saved GPU OC profiles
            LoadGpuOcProfiles();

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

            bool startupHardwareRestoreEnabled = ShouldRunStartupHardwareRestore();
            
            if (savedMode != null)
            {
                _logging.Info($"Restored last performance mode: {savedModeName}");

                if (startupHardwareRestoreEnabled)
                {
                    // Actually apply the saved performance mode on startup
                    // Schedule with delay to ensure BIOS is ready
                    RunStartupTask(
                        () => ReapplySettingWithRetryAsync(
                            "Performance Mode",
                            () => ReapplySavedPerformanceMode(savedMode),
                            maxRetries: 3,
                            initialDelayMs: 2000,
                            maxDelayMs: 5000
                        ),
                        "Performance Mode restore");
                }
                else
                {
                    _logging.Warn("Startup hardware restore is disabled - skipping automatic Performance Mode reapply");
                }
            }
            
            // Restore last GPU Power Boost level from config
            var savedGpuBoostLevel = _configService.Config.LastGpuPowerBoostLevel;
            if (!string.IsNullOrEmpty(savedGpuBoostLevel) && GpuPowerBoostLevels.Contains(savedGpuBoostLevel))
            {
                _gpuPowerBoostLevel = savedGpuBoostLevel;
                _logging.Info($"Restored last GPU Power Boost level from config: {savedGpuBoostLevel}");

                if (startupHardwareRestoreEnabled)
                {
                    // Reapply the saved GPU Power Boost level on startup with proper retry logic
                    // This fixes the issue where GPU TGP resets to Minimum after reboot
                    RunStartupTask(
                        () => ReapplySettingWithRetryAsync(
                            "GPU Power Boost",
                            () => ReapplySavedGpuPowerBoost(savedGpuBoostLevel),
                            maxRetries: 5,
                            initialDelayMs: 1500,
                            maxDelayMs: 5000
                        ),
                        "GPU Power Boost restore");
                }
                else
                {
                    _logging.Warn("Startup hardware restore is disabled - skipping automatic GPU Power Boost reapply");
                }
            }

            // Initialize GPU modes
            GpuSwitchModes.Add(GpuSwitchMode.Hybrid);
            GpuSwitchModes.Add(GpuSwitchMode.Discrete);
            GpuSwitchModes.Add(GpuSwitchMode.Integrated);
            
            // Detect current GPU mode
            DetectGpuMode();
            
            // Detect GPU Power Boost availability
            DetectGpuPowerBoost();
            
            // Detect display overdrive support
            DetectDisplayOverdrive();
            
            // Initialize TCC offset (Intel CPU temperature limit)
            InitializeTccOffset();

            // Initialize CPU power limits (PL1/PL2)
            InitializeCpuPowerLimits();
            
            // Load undervolt preferences from config (with null safety)
            var undervoltPrefs = _configService.Config.Undervolt ?? new UndervoltPreferences();
            var defaultOffset = undervoltPrefs.DefaultOffset ?? new UndervoltOffset();
            RequestedCoreOffset = defaultOffset.CoreMv;
            RequestedCacheOffset = defaultOffset.CacheMv;
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
                
                // Enable MSR-based throttling detection in hardware monitoring
                _hardwareMonitoringService?.SetMsrAccess(_msrAccess);

                // Initialize EDP throttling mitigation service
                _edpMitigationService = new EdpThrottlingMitigationService(_msrAccess, _undervoltService, _logging);
                _edpMitigationService.ThrottlingDetected += OnEdpThrottlingDetected;
                _edpMitigationService.MitigationApplied += OnEdpMitigationApplied;
                _edpMitigationService.MitigationRemoved += OnEdpMitigationRemoved;
                _edpMitigationService.Start();

                var savedOffset = _configService.Config.LastTccOffset;
                if (savedOffset.HasValue && savedOffset.Value > 0)
                {
                    if (!ShouldRunStartupHardwareRestore())
                    {
                        _logging.Warn("Startup hardware restore is disabled - skipping automatic TCC offset reapply");
                    }
                    else if (currentOffset != savedOffset.Value)
                    {
                        _logging.Info($"TCC offset needs restoration: saved {savedOffset.Value}°C differs from current {currentOffset}°C");
                        RunStartupTask(
                            () => ReapplySettingWithRetryAsync(
                                "TCC Offset",
                                () => ReapplySavedTccOffset(savedOffset.Value),
                                maxRetries: 8,
                                initialDelayMs: 1500,
                                maxDelayMs: 8000
                            ),
                            "TCC Offset restore");
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

        private bool ShouldRunStartupHardwareRestore()
        {
            var config = _configService.Config;
            if (!config.EnableStartupHardwareRestore)
            {
                return false;
            }

            var model = _systemInfoService?.GetSystemInfo().Model ?? string.Empty;
            bool riskyModel = model.Contains("OMEN 16", StringComparison.OrdinalIgnoreCase) ||
                              model.Contains("Victus", StringComparison.OrdinalIgnoreCase);

            if (riskyModel && !config.AllowStartupRestoreOnOmen16OrVictus)
            {
                _logging.Warn($"Startup hardware restore blocked on sensitive model '{model}'. Enable AllowStartupRestoreOnOmen16OrVictus to override.");
                return false;
            }

            return true;
        }

        private void RunStartupTask(Func<Task> startupAction, string actionName)
        {
            _ = startupAction().ContinueWith(
                t =>
                {
                    var ex = t.Exception?.GetBaseException();
                    if (ex != null)
                    {
                        _logging.Error($"Startup task '{actionName}' faulted: {ex.Message}", ex);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        
        private void ApplyTccOffset()
        {
            if (_msrAccess == null)
            {
                _logging.Warn("Cannot apply TCC offset: MSR access not available (install PawnIO or disable Secure Boot)");
                System.Windows.MessageBox.Show(
                    "TCC offset cannot be applied - MSR access not available.\n\n" +
                    "This requires either:\n" +
                    "• PawnIO driver installed (pawnio.eu)\n" +
                    "• Secure Boot disabled with WinRing0\n\n" +
                    "Without MSR access, CPU temperature limits cannot be modified.",
                    "TCC Offset Unavailable",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            if (!TccStatus.IsSupported)
            {
                _logging.Warn("TCC offset not supported on this CPU");
                return;
            }
                
            try
            {
                // Read current value before change
                var offsetBefore = _msrAccess.ReadTccOffset();
                
                // Apply the new offset
                _msrAccess.SetTccOffset(RequestedTccOffset);
                
                // Read back to verify write succeeded
                var offsetAfter = _msrAccess.ReadTccOffset();
                var newLimit = TccStatus.TjMax - offsetAfter;
                
                if (offsetAfter == RequestedTccOffset)
                {
                    _logging.Info($"✓ TCC offset applied: {offsetAfter}°C (effective limit: {newLimit}°C)");
                    
                    // Save to config for persistence across reboots
                    SaveTccOffsetToConfig(RequestedTccOffset);
                    
                    // Update status
                    TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, offsetAfter);
                }
                else if (offsetAfter == offsetBefore)
                {
                    _logging.Warn($"× TCC offset write failed - value unchanged at {offsetBefore}°C. " +
                                  "This may indicate HVCI/Secure Boot is blocking MSR writes.");
                    System.Windows.MessageBox.Show(
                        $"TCC offset could not be changed - the value remained at {offsetBefore}°C.\n\n" +
                        "This usually means:\n" +
                        "• Hyper-V/HVCI (Core Isolation) is blocking MSR writes\n" +
                        "• Secure Boot is preventing kernel-mode access\n\n" +
                        "To fix:\n" +
                        "1. Open Windows Security → Device Security → Core Isolation\n" +
                        "2. Disable 'Memory Integrity'\n" +
                        "3. Restart your computer\n\n" +
                        "Or use Intel XTU which has a signed driver.",
                        "TCC Offset Write Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
                else
                {
                    _logging.Warn($"⚠ TCC offset partially applied: requested {RequestedTccOffset}°C, " +
                                  $"actual {offsetAfter}°C (was {offsetBefore}°C)");
                    // Still update UI with actual value
                    TccStatus = TccOffsetStatus.CreateSupported(TccStatus.TjMax, offsetAfter);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply TCC offset: {ex.Message}", ex);
                System.Windows.MessageBox.Show(
                    $"Failed to apply TCC offset: {ex.Message}\n\n" +
                    "Ensure you have MSR access via PawnIO or WinRing0.",
                    "TCC Offset Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
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

        // ==========================================
        // CPU Power Limits (PL1/PL2) Methods
        // ==========================================

        private void InitializeCpuPowerLimits()
        {
            if (_msrAccess == null || !_msrAccess.IsAvailable)
            {
                CpuPowerLimitsAvailable = false;
                CpuPowerLimitsStatus = "No MSR access (install PawnIO for power limit control)";
                CpuPowerLimitsLocked = true;
                return;
            }

            try
            {
                var (pl1, pl2, pl1Enabled, pl2Enabled, isLocked) = _msrAccess.GetPowerLimitStatus();
                
                CurrentPl1Watts = pl1;
                CurrentPl2Watts = pl2;
                CpuPl1Watts = (int)Math.Round(pl1);
                CpuPl2Watts = (int)Math.Round(pl2);
                CpuPowerLimitsLocked = isLocked;

                if (isLocked)
                {
                    CpuPowerLimitsAvailable = true;
                    CpuPowerLimitsStatus = $"⚠️ BIOS Locked - Read-only (PL1: {pl1:F0}W, PL2: {pl2:F0}W)";
                    _logging.Warn("CPU power limits are locked by BIOS - cannot modify until next reboot");
                }
                else
                {
                    CpuPowerLimitsAvailable = true;
                    CpuPowerLimitsStatus = $"✓ Available (PL1: {pl1:F0}W, PL2: {pl2:F0}W)";
                    _logging.Info($"CPU power limits initialized: PL1={pl1:F0}W, PL2={pl2:F0}W, Locked={isLocked}");
                }

                // Set reasonable max limits based on current values
                CpuPl1Max = Math.Max(115, (int)(pl1 * 1.5));
                CpuPl2Max = Math.Max(175, (int)(pl2 * 1.5));
            }
            catch (Exception ex)
            {
                CpuPowerLimitsAvailable = false;
                CpuPowerLimitsStatus = $"Error: {ex.Message}";
                CpuPowerLimitsLocked = true;
                _logging.Warn($"Failed to initialize CPU power limits: {ex.Message}");
            }
        }

        private void RefreshCpuPowerLimits()
        {
            InitializeCpuPowerLimits();
            OnPropertyChanged(nameof(CpuPowerLimitsStatus));
            OnPropertyChanged(nameof(CurrentPl1Text));
            OnPropertyChanged(nameof(CurrentPl2Text));
        }

        private void ApplyCpuPowerLimits()
        {
            if (_msrAccess == null)
            {
                _logging.Warn("Cannot apply CPU power limits: MSR access not available");
                System.Windows.MessageBox.Show(
                    "CPU power limits cannot be applied - MSR access not available.\n\n" +
                    "Install PawnIO driver from pawnio.eu to enable power limit control.",
                    "Power Limits Unavailable",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (CpuPowerLimitsLocked)
            {
                _logging.Warn("Cannot apply CPU power limits: locked by BIOS");
                System.Windows.MessageBox.Show(
                    "CPU power limits are locked by BIOS and cannot be modified.\n\n" +
                    "This lock is set during boot and persists until the next restart.\n" +
                    "Some laptops may require specific BIOS settings or Unleashed Mode to unlock.",
                    "Power Limits Locked",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Ensure PL2 >= PL1
                int pl1 = CpuPl1Watts;
                int pl2 = Math.Max(CpuPl2Watts, pl1);

                _logging.Info($"Applying CPU power limits: PL1={pl1}W, PL2={pl2}W");
                
                bool success = _msrAccess.SetPowerLimits(pl1, pl2);
                
                if (success)
                {
                    // Verify the write
                    var (newPl1, newPl2, _, _, _) = _msrAccess.GetPowerLimitStatus();
                    CurrentPl1Watts = newPl1;
                    CurrentPl2Watts = newPl2;
                    
                    _logging.Info($"✓ CPU power limits applied: PL1={newPl1:F0}W, PL2={newPl2:F0}W");
                    CpuPowerLimitsStatus = $"✓ Applied (PL1: {newPl1:F0}W, PL2: {newPl2:F0}W)";

                    // Save to config
                    SaveCpuPowerLimitsToConfig(pl1, pl2);
                }
                else
                {
                    _logging.Warn("CPU power limits write returned false - may be locked");
                    System.Windows.MessageBox.Show(
                        "Failed to apply CPU power limits.\n\n" +
                        "The BIOS may have locked the MSR registers during boot.",
                        "Power Limits Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply CPU power limits: {ex.Message}", ex);
                System.Windows.MessageBox.Show(
                    $"Error applying CPU power limits: {ex.Message}",
                    "Power Limits Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ResetCpuPowerLimits()
        {
            if (_msrAccess == null || CpuPowerLimitsLocked)
                return;

            try
            {
                // Reset to Intel defaults: typically 45W PL1 and 65W PL2 for mobile
                int defaultPl1 = 45;
                int defaultPl2 = 65;

                _logging.Info($"Resetting CPU power limits to defaults: PL1={defaultPl1}W, PL2={defaultPl2}W");
                _msrAccess.SetPowerLimits(defaultPl1, defaultPl2);

                CpuPl1Watts = defaultPl1;
                CpuPl2Watts = defaultPl2;

                var (newPl1, newPl2, _, _, _) = _msrAccess.GetPowerLimitStatus();
                CurrentPl1Watts = newPl1;
                CurrentPl2Watts = newPl2;
                CpuPowerLimitsStatus = $"✓ Reset to defaults (PL1: {newPl1:F0}W, PL2: {newPl2:F0}W)";

                _logging.Info("CPU power limits reset to defaults");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to reset CPU power limits: {ex.Message}", ex);
            }
        }

        private void SaveCpuPowerLimitsToConfig(int pl1, int pl2)
        {
            try
            {
                var config = _configService.Config;
                config.LastCpuPl1Watts = pl1;
                config.LastCpuPl2Watts = pl2;
                _configService.Save(config);
                _logging.Info($"CPU power limits saved to config: PL1={pl1}W, PL2={pl2}W");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save CPU power limits to config: {ex.Message}");
            }
        }

        private void RefreshGpuPowerPill()
        {
            var text = _latestGpuPowerWatts > 0
                ? $"Full power • {_latestGpuPowerWatts:F0}W"
                : "Full power • waiting for telemetry";
            GpuFullPowerText = text;
        }

        private void OnMonitoringSampleUpdated(object? sender, MonitoringSample sample)
        {
            _latestGpuPowerWatts = sample.GpuPowerWatts;

            if (!IsGpuFullPowerActive)
                return;

            App.Current?.Dispatcher?.BeginInvoke(RefreshGpuPowerPill);
        }
        
        private void DetectGpuPowerBoost()
        {
            // HP Victus models do not expose custom TGP/PPAB control via BIOS.
            // Probing WMI on Victus can still return non-null values (shared BIOS bridge),
            // which would incorrectly enable the GPU Power Boost UI and produce API errors on apply.
            var sysInfo = _systemInfoService?.GetSystemInfo();
            if (sysInfo?.IsHpVictus == true)
            {
                GpuPowerBoostAvailable = false;
                GpuPowerBoostStatus = "Not supported — HP Victus BIOS does not expose custom TGP/PPAB control";
                _logging.Info("GPU Power Boost: skipped — HP Victus does not support WMI TGP/PPAB control");
                return;
            }

            // Check if user has a saved preference - don't overwrite it
            var savedLevel = _configService.Config.LastGpuPowerBoostLevel;
            var hasSavedPreference = !string.IsNullOrEmpty(savedLevel) && GpuPowerBoostLevels.Contains(savedLevel);
            
            // Try WMI BIOS first (preferred)
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                var gpuPower = _wmiBios.GetGpuPower();
                if (gpuPower.HasValue)
                {
                    GpuPowerBoostAvailable = true;
                    
                    // Detect current state for status display
                    string detectedLevel;
                    if (gpuPower.Value.customTgp && gpuPower.Value.ppab)
                    {
                        detectedLevel = "Maximum";
                    }
                    else if (gpuPower.Value.customTgp)
                    {
                        detectedLevel = "Medium";
                    }
                    else
                    {
                        detectedLevel = "Minimum";
                    }
                    
                    // Only update level if user has no saved preference
                    if (!hasSavedPreference)
                    {
                        GpuPowerBoostLevel = detectedLevel;
                    }
                    GpuPowerBoostStatus = $"{detectedLevel} (detected via WMI)";
                    _logging.Info($"✓ GPU Power Boost available via WMI BIOS. Detected: {detectedLevel}, User pref: {savedLevel ?? "none"}");
                    return;
                }
                else
                {
                    _logging.Info("WMI BIOS available but GetGpuPower returned null - trying EC fallback");
                }
            }
            
            // Fallback: Try OGH proxy (for systems where WMI BIOS commands fail)
            if (_oghProxy != null && _oghProxy.Status.WmiAvailable)
            {
                var (success, level, levelName) = _oghProxy.GetGpuPowerLevel();
                if (success)
                {
                    GpuPowerBoostAvailable = true;
                    var mappedLevel = level switch
                    {
                        0 => "Minimum",
                        1 => "Medium",
                        2 => "Maximum",
                        3 => "Extended",
                        _ => levelName
                    };
                    
                    if (!hasSavedPreference)
                    {
                        GpuPowerBoostLevel = mappedLevel;
                    }
                    GpuPowerBoostStatus = $"{mappedLevel} (detected via OGH)";
                    _logging.Info($"✓ GPU Power Boost available via OGH. Detected: {mappedLevel}, User pref: {savedLevel ?? "none"}");
                    return;
                }
                
                _logging.Warn("GPU Power Boost: OGH WMI exists but GetGpuPowerLevel() failed");
            }
            
            // Fallback: Try EC-based detection for OMEN 17-ck2xxx models with RTX 4090
            if (_ecAccess != null && _ecAccess.IsAvailable)
            {
                var model = _systemInfoService?.GetSystemInfo().Model ?? "Unknown";
                _logging.Info($"Checking EC-based GPU boost for model: {model}");
                
                if (model.Contains("OMEN", StringComparison.OrdinalIgnoreCase) || 
                    model.Contains("17-ck", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("16-wd", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var perfMode = _ecAccess.ReadByte(0xCE);
                        _logging.Info($"EC performance mode register 0xCE = 0x{perfMode:X2}");
                        
                        GpuPowerBoostAvailable = true;
                        
                        // EC register 0xCE maps to performance mode, not GPU boost directly
                        // We still allow GPU boost control via EC even if current mode is 0
                        // Only update level if user has no saved preference
                        if (!hasSavedPreference)
                        {
                            GpuPowerBoostLevel = perfMode switch
                            {
                                0 => "Minimum",
                                1 => "Minimum",
                                2 => "Medium",
                                3 => "Maximum",
                                _ => "Medium"
                            };
                        }
                        GpuPowerBoostStatus = $"EC mode 0x{perfMode:X2} (experimental) - User setting will be applied";
                        _logging.Info($"✓ GPU Power Boost enabled via EC. EC=0x{perfMode:X2}, User pref: {savedLevel ?? "none"}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"EC GPU boost detection failed: {ex.Message}");
                    }
                }
            }
            
            // Neither backend functional - but don't override if already enabled by a successful apply
            if (!GpuPowerBoostAvailable)
            {
                GpuPowerBoostAvailable = false;
                GpuPowerBoostStatus = "Not available on this model";
                _logging.Info("GPU Power Boost: No functional backend found");
            }
            else
            {
                _logging.Info("GPU Power Boost: Detection failed but already enabled by prior successful apply");
            }
        }
        
        private void DetectDisplayOverdrive()
        {
            if (_wmiBios == null || !_wmiBios.IsAvailable) return;

            try
            {
                var status = _wmiBios.GetDisplayOverdrive();
                if (status.HasValue)
                {
                    DisplayOverdriveSupported = true;
                    _displayOverdriveEnabled = status.Value;
                    OnPropertyChanged(nameof(DisplayOverdriveEnabled));
                    _logging.Info($"✓ Display overdrive supported. Current: {(status.Value ? "enabled" : "disabled")}");
                }
                else
                {
                    DisplayOverdriveSupported = false;
                    _logging.Info("Display overdrive not supported on this model");
                }
            }
            catch (Exception ex)
            {
                DisplayOverdriveSupported = false;
                _logging.Warn($"Display overdrive detection failed: {ex.Message}");
            }
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
                    var baseStatus = GpuPowerBoostLevel switch
                    {
                        "Minimum" => "✓ Minimum (Base TGP only)",
                        "Medium" => "✓ Medium (Custom TGP enabled)",
                        "Maximum" => "✓ Maximum (Custom TGP + Dynamic Boost +15W)",
                        "Extended" => "✓ Extended (PPAB+ for RTX 5080, +25W if supported)",
                        _ => "Applied"
                    };

                    // If NVAPI is available, suggest using power limits for fine-tuning
                    if (GpuNvapiAvailable && GpuPowerLimitPercent != 100)
                    {
                        GpuPowerBoostStatus = $"{baseStatus} + NVAPI {GpuPowerLimitPercent}% limit";
                    }
                    else if (GpuNvapiAvailable)
                    {
                        GpuPowerBoostStatus = $"{baseStatus} (NVAPI power limits available)";
                    }
                    else
                    {
                        GpuPowerBoostStatus = baseStatus;
                    }

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

            // Fallback: Try EC-based GPU boost (experimental, model-specific)
            // NOTE: EC registers for PPAB vary by laptop model and are not publicly documented
            // This is a placeholder for future model-specific implementations
            if (TryApplyEcGpuBoost())
            {
                GpuPowerBoostStatus = GpuPowerBoostLevel switch
                {
                    "Minimum" => "✓ Minimum (Base TGP, via EC)",
                    "Medium" => "✓ Medium (Custom TGP, via EC)",
                    "Maximum" => "✓ Maximum (Dynamic Boost +15W, via EC)",
                    "Extended" => "✓ Extended (PPAB+ +25W, via EC)",
                    _ => "Applied via EC"
                };
                _logging.Info($"✓ GPU Power Boost set to: {GpuPowerBoostLevel} via EC (experimental)");
                
                // Save to config for persistence
                SaveGpuPowerBoostToConfig();
                return;
            }
            
            GpuPowerBoostStatus = "Failed - WMI/EC commands not supported on this model";
            _logging.Warn($"Failed to set GPU Power Boost to: {GpuPowerBoostLevel} - WMI and EC methods unavailable");
        }

        /// <summary>
        /// Try to apply GPU power boost via EC registers (experimental, model-specific)
        /// This implementation attempts common PPAB registers for OMEN 17-ck2xxx series
        /// </summary>
        private bool TryApplyEcGpuBoost()
        {
            _logging.Info("TryApplyEcGpuBoost() called - attempting EC-based GPU boost");
            
            if (_ecAccess == null || !_ecAccess.IsAvailable)
            {
                _logging.Info("EC access not available for GPU boost");
                return false;
            }

            // Get system model to determine register map
            var model = _systemInfoService?.GetSystemInfo().Model ?? "Unknown";
            _logging.Info($"Attempting EC-based GPU boost for model: {model}");

            // OMEN laptop models that support EC-based performance/GPU control
            if (model.Contains("OMEN", StringComparison.OrdinalIgnoreCase) || 
                model.Contains("17-ck", StringComparison.OrdinalIgnoreCase) ||
                model.Contains("16-wd", StringComparison.OrdinalIgnoreCase))
            {
                _logging.Info("Model matches OMEN laptop, proceeding with EC boost implementation");
                
                try
                {
                    // Register 0xCE: Performance mode (controls PPAB/GPU power)
                    // Values: 0=Quiet, 1=Default, 2=Performance, 3=Extreme (enables PPAB +15W boost)
                    var currentMode = _ecAccess.ReadByte(0xCE);
                    _logging.Info($"Current performance mode register 0xCE: 0x{currentMode:X2}");

                    // Map our GPU boost level to EC performance mode value
                    byte targetMode = GpuPowerBoostLevel switch
                    {
                        "Minimum" => 0,      // Quiet - no boost
                        "Medium" => 2,       // Performance - some boost
                        "Maximum" => 3,      // Extreme - full PPAB +15W
                        "Extended" => 3,     // Use Extreme for Extended too
                        _ => 2
                    };

                    if (currentMode != targetMode)
                    {
                        _ecAccess.WriteByte(0xCE, targetMode);
                        _logging.Info($"Set performance mode to 0x{targetMode:X2} for {GpuPowerBoostLevel} boost");
                        
                        // Verify the change
                        System.Threading.Thread.Sleep(50); // Small delay for EC to process
                        var newMode = _ecAccess.ReadByte(0xCE);
                        if (newMode == targetMode)
                        {
                            _logging.Info($"✓ Performance mode set to 0x{targetMode:X2} successfully");
                            return true;
                        }
                        else
                        {
                            _logging.Warn($"Failed to set performance mode, wrote 0x{targetMode:X2} but read 0x{newMode:X2}");
                            // Return true anyway if value changed at all - some laptops may limit values
                            if (newMode != currentMode)
                            {
                                _logging.Info($"Performance mode changed from 0x{currentMode:X2} to 0x{newMode:X2}");
                                return true;
                            }
                        }
                    }
                    else
                    {
                        _logging.Info($"Performance mode already at 0x{targetMode:X2}");
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logging.Error($"EC GPU boost failed: {ex.Message}");
                    return false;
                }
            }
            else
            {
                _logging.Info($"EC GPU boost not implemented for model: {model}");
                return false;
            }
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
                        GpuPowerBoostAvailable = true;
                        GpuPowerBoostLevel = savedLevel;
                        GpuPowerBoostStatus = savedLevel switch
                        {
                            "Minimum" => "✓ Minimum (Base TGP only) - Restored",
                            "Medium" => "✓ Medium (Custom TGP) - Restored",
                            "Maximum" => "✓ Maximum (Dynamic Boost) - Restored",
                            _ => $"✓ {savedLevel} - Restored"
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
                        GpuPowerBoostAvailable = true;
                        GpuPowerBoostLevel = savedLevel;
                        GpuPowerBoostStatus = $"✓ {savedLevel} (via OGH) - Restored";
                    });
                    return;
                }
            }
            
            // Fallback: Try EC-based GPU boost for OMEN models
            if (_ecAccess != null && _ecAccess.IsAvailable)
            {
                var model = _systemInfoService?.GetSystemInfo().Model ?? "Unknown";
                if (model.Contains("OMEN", StringComparison.OrdinalIgnoreCase) || 
                    model.Contains("17-ck", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("16-wd", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // EC register 0xCE controls performance mode which affects GPU power
                        // 0=Quiet, 1=Default, 2=Performance, 3=Extreme (enables PPAB)
                        byte ecValue = savedLevel switch
                        {
                            "Minimum" => 0,
                            "Medium" => 2,
                            "Maximum" => 3,  // Extreme mode enables PPAB +15W
                            _ => 2
                        };
                        
                        _ecAccess.WriteByte(0xCE, ecValue);
                        var readBack = _ecAccess.ReadByte(0xCE);
                        
                        if (readBack == ecValue)
                        {
                            _logging.Info($"✓ GPU Power Boost reapplied on startup: {savedLevel} via EC (0xCE={ecValue})");
                            
                            App.Current?.Dispatcher?.BeginInvoke(() =>
                            {
                                GpuPowerBoostAvailable = true;
                                GpuPowerBoostLevel = savedLevel;
                                GpuPowerBoostStatus = savedLevel switch
                                {
                                    "Minimum" => "✓ Minimum (Base TGP, via EC) - Restored",
                                    "Medium" => "✓ Medium (Custom TGP, via EC) - Restored",
                                    "Maximum" => "✓ Maximum (Dynamic Boost +15W, via EC) - Restored",
                                    "Extended" => "✓ Extended (PPAB+ +25W, via EC) - Restored",
                                    _ => $"✓ {savedLevel} (via EC) - Restored"
                                };
                            });
                            return;
                        }
                        else
                        {
                            _logging.Warn($"EC GPU boost write verification failed: wrote 0x{ecValue:X2}, read 0x{readBack:X2}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Warn($"EC GPU boost failed: {ex.Message}");
                    }
                }
            }
            
            // Neither WMI nor OGH nor EC succeeded - throw to trigger retry
            throw new InvalidOperationException("GPU Power Boost restoration failed - all backends unavailable or returned failure");
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
        
        private void SavePerformanceModeToConfig(string modeName)
        {
            try
            {
                var config = _configService.Config;
                config.LastPerformanceModeName = modeName;
                _configService.Save(config);
                _logging.Info($"Performance mode saved to config: {modeName}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save performance mode to config: {ex.Message}");
            }
        }
        
        #region GPU Overclocking (NVAPI)
        
        /// <summary>
        /// Initialize GPU overclocking via NVAPI.
        /// </summary>
        private void InitializeGpuOc()
        {
            // First, detect GPU vendor from system info
            DetectGpuVendor();
            
            if (_nvapiService == null)
            {
                GpuOcAvailable = false;
                GpuOcStatus = "NVAPI not available";
                return;
            }
            
            try
            {
                if (_nvapiService.Initialize())
                {
                    GpuNvapiAvailable = true;
                    GpuOcAvailable = _nvapiService.SupportsOverclocking;
                    GpuVendor = "NVIDIA";
                    GpuDisplayName = _nvapiService.GpuName;
                    
                    // Preserve driver version from SystemInfo if not already set
                    if (string.IsNullOrEmpty(GpuDriverVersion) || GpuDriverVersion == "Unknown")
                    {
                        var systemInfo = _systemInfoService?.GetSystemInfo();
                        var nvidiaGpu = systemInfo?.Gpus?.FirstOrDefault(g => g.Vendor == "NVIDIA");
                        if (nvidiaGpu != null && !string.IsNullOrEmpty(nvidiaGpu.DriverVersion))
                        {
                            GpuDriverVersion = nvidiaGpu.DriverVersion;
                            _logging.Info($"GPU driver version from SystemInfo: {GpuDriverVersion}");
                        }
                        else
                        {
                            GpuDriverVersion = "Unknown";
                            _logging.Warn("Could not retrieve GPU driver version from SystemInfo");
                        }
                    }
                    
                    // Set limits from the service
                    GpuCoreOffsetMin = _nvapiService.MinCoreOffset;
                    GpuCoreOffsetMax = _nvapiService.MaxCoreOffset;
                    GpuMemoryOffsetMin = _nvapiService.MinMemoryOffset;
                    GpuMemoryOffsetMax = _nvapiService.MaxMemoryOffset;
                    GpuPowerLimitMin = _nvapiService.MinPowerLimit;
                    GpuPowerLimitMax = _nvapiService.MaxPowerLimit;
                    
                    // Get current values
                    GpuCoreClockOffset = _nvapiService.CoreClockOffsetMHz;
                    GpuMemoryClockOffset = _nvapiService.MemoryClockOffsetMHz;
                    GpuPowerLimitPercent = _nvapiService.PowerLimitPercent;
                    GpuVoltageOffsetMv = _nvapiService.VoltageOffsetMv;
                    
                    if (GpuOcAvailable)
                    {
                        GpuOcStatus = $"✓ {_nvapiService.GpuName} - Ready";
                        _logging.Info($"GPU OC initialized: {_nvapiService.GpuName}, Supports OC: {_nvapiService.SupportsOverclocking}");
                    }
                    else
                    {
                        GpuOcStatus = $"{_nvapiService.GpuName} - Power limit tuning available, clock offsets blocked";
                        _logging.Info($"GPU detected with power-only NVAPI tuning: {_nvapiService.GpuName}");
                    }

                    // Restore saved settings for both full-OC and power-only NVAPI systems.
                    RestoreGpuOcFromConfig();
                }
                else
                {
                    GpuOcAvailable = false;
                    GpuOcStatus = "NVIDIA GPU not detected";
                    
                    // Try AMD GPU initialization
                    InitializeAmdGpu();
                }
            }
            catch (Exception ex)
            {
                GpuOcAvailable = false;
                GpuOcStatus = $"Initialization failed: {ex.Message}";
                _logging.Error($"GPU OC initialization failed: {ex.Message}", ex);
            }
            
            // Notify UI of limit changes
            OnPropertyChanged(nameof(GpuCoreOffsetMin));
            OnPropertyChanged(nameof(GpuCoreOffsetMax));
            OnPropertyChanged(nameof(GpuMemoryOffsetMin));
            OnPropertyChanged(nameof(GpuMemoryOffsetMax));
            OnPropertyChanged(nameof(GpuPowerLimitMin));
            OnPropertyChanged(nameof(GpuPowerLimitMax));
            NotifyGpuOcMetadataChanged();
        }
        
        /// <summary>
        /// Detect GPU vendor and driver version from SystemInfo.
        /// </summary>
        private void DetectGpuVendor()
        {
            try
            {
                _logging.Info("Detecting GPU vendor from SystemInfo...");
                
                var systemInfo = _systemInfoService?.GetSystemInfo();
                if (systemInfo?.Gpus == null || systemInfo.Gpus.Count == 0)
                {
                    _logging.Warn("No GPUs found in SystemInfo");
                    return;
                }
                
                _logging.Info($"Found {systemInfo.Gpus.Count} GPU(s) in SystemInfo");
                foreach (var gpu in systemInfo.Gpus)
                {
                    _logging.Info($"  → {gpu.Name} (Vendor: {gpu.Vendor}, Driver: {gpu.DriverVersion})");
                }
                
                // Find first discrete GPU (prefer NVIDIA/AMD > Intel > any known vendor > first entry)
                var discreteGpu =
                    systemInfo.Gpus.FirstOrDefault(g => g.Vendor == "NVIDIA" || g.Vendor == "AMD") ??
                    systemInfo.Gpus.FirstOrDefault(g => g.Vendor == "Intel") ??
                    systemInfo.Gpus.FirstOrDefault(g => g.Vendor != "Unknown") ??
                    systemInfo.Gpus[0];
                
                GpuVendor = discreteGpu.Vendor;
                GpuDisplayName = discreteGpu.Name;
                GpuDriverVersion = !string.IsNullOrEmpty(discreteGpu.DriverVersion) 
                    ? discreteGpu.DriverVersion 
                    : "Unknown";
                
                _logging.Info($"Primary GPU selected: {GpuDisplayName} ({GpuVendor}), Driver: {GpuDriverVersion}");
                
                // Notify UI of changes
                OnPropertyChanged(nameof(GpuVendor));
                OnPropertyChanged(nameof(GpuDisplayName));
                OnPropertyChanged(nameof(GpuDriverVersion));
                OnPropertyChanged(nameof(IsNvidiaGpu));
                OnPropertyChanged(nameof(IsAmdGpu));
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to detect GPU vendor: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize AMD GPU (placeholder for future ADLX integration).
        /// </summary>
        private void InitializeAmdGpu()
        {
            _logging.Info($"InitializeAmdGpu called, GpuVendor={GpuVendor}");
            
            if (GpuVendor != "AMD")
            {
                _logging.Info("Not an AMD GPU, skipping AMD initialization");
                return;
            }
            
            // AMD GPU detected — check if ADL service is available
            GpuAmdAvailable = true;
            
            if (_amdGpuService?.IsAvailable == true)
            {
                GpuOcStatus = $"✓ {GpuDisplayName} - AMD overclocking available via ADL2";
                _logging.Info($"AMD GPU OC available: {GpuDisplayName} via ADL2");
            }
            else
            {
                GpuOcStatus = $"⚠ {GpuDisplayName} - Install AMD Adrenalin drivers for GPU tuning";
                _logging.Info($"AMD GPU detected: {GpuDisplayName}. ADL2 not available (Adrenalin drivers required).");
            }
        }
        
        /// <summary>
        /// Apply GPU clock offsets and power limit.
        /// </summary>
        private void ApplyGpuOc()
        {
            var result = ApplyGpuOcValues(GpuCoreClockOffset, GpuMemoryClockOffset, GpuPowerLimitPercent, GpuVoltageOffsetMv, true, "✓ Applied");
            GpuOcStatus = result.StatusText;
        }
        
        /// <summary>
        /// Reset GPU clock offsets and power limit to defaults.
        /// </summary>
        private void ResetGpuOc()
        {
            GpuCoreClockOffset = 0;
            GpuMemoryClockOffset = 0;
            GpuPowerLimitPercent = 100;
            GpuVoltageOffsetMv = 0;
            
            if (_nvapiService != null && GpuNvapiAvailable)
            {
                if (GpuOcAvailable)
                {
                    _nvapiService.SetCoreClockOffset(0);
                    _nvapiService.SetMemoryClockOffset(0);
                    _nvapiService.SetVoltageOffset(0);
                }

                _nvapiService.SetPowerLimit(100);
            }
            
            GpuOcStatus = GpuOcAvailable
                ? "Reset to defaults (Core: 0 MHz, Memory: 0 MHz, Power: 100%, Voltage: 0 mV)"
                : "Reset to defaults (Power: 100%)";
            SaveGpuOcToConfig();
            _logging.Info("GPU OC reset to defaults");
        }

        #region AMD GPU Overclocking

        private void ApplyAmdGpuOc()
        {
            if (_amdGpuService == null || !_amdGpuService.IsAvailable)
            {
                _logging.Warn("AMD GPU OC: Service not available");
                return;
            }

            try
            {
                bool coreSuccess = _amdGpuService.SetCoreClockOffset(AmdCoreClockOffset);
                bool memSuccess = _amdGpuService.SetMemoryClockOffset(AmdMemoryClockOffset);
                bool powerSuccess = _amdGpuService.SetPowerLimit(AmdPowerLimitPercent);

                if (coreSuccess && memSuccess && powerSuccess)
                {
                    GpuOcStatus = $"AMD GPU OC Applied: Core {AmdCoreClockOffset:+#;-#;0} MHz, Mem {AmdMemoryClockOffset:+#;-#;0} MHz, Power {AmdPowerLimitPercent}%";
                    _logging.Info($"✓ AMD GPU OC applied: Core={AmdCoreClockOffset}, Mem={AmdMemoryClockOffset}, Power={AmdPowerLimitPercent}%");
                }
                else
                {
                    GpuOcStatus = "AMD GPU OC: Some settings failed to apply";
                    _logging.Warn($"AMD GPU OC partial failure: Core={coreSuccess}, Mem={memSuccess}, Power={powerSuccess}");
                }
            }
            catch (Exception ex)
            {
                GpuOcStatus = $"AMD GPU OC Error: {ex.Message}";
                _logging.Error($"AMD GPU OC failed: {ex.Message}", ex);
            }
        }

        private void ResetAmdGpuOc()
        {
            AmdCoreClockOffset = 0;
            AmdMemoryClockOffset = 0;
            AmdPowerLimitPercent = 0;

            if (_amdGpuService?.IsAvailable == true)
            {
                _amdGpuService.ResetToDefaults();
            }

            GpuOcStatus = "AMD GPU OC reset to defaults";
            _logging.Info("AMD GPU OC reset to defaults");
        }

        #endregion
        
        /// <summary>
        /// Save GPU OC settings to config for persistence.
        /// </summary>
        private void SaveGpuOcToConfig()
        {
            try
            {
                var config = _configService.Config;
                if (config.GpuOc == null)
                    config.GpuOc = new GpuOcSettings();
                
                config.GpuOc.CoreClockOffsetMHz = GpuOcAvailable ? GpuCoreClockOffset : 0;
                config.GpuOc.MemoryClockOffsetMHz = GpuOcAvailable ? GpuMemoryClockOffset : 0;
                config.GpuOc.PowerLimitPercent = GpuPowerLimitPercent;
                config.GpuOc.VoltageOffsetMv = GpuOcAvailable ? GpuVoltageOffsetMv : 0;
                config.GpuOc.ApplyOnStartup = true; // Default to reapply
                
                _configService.Save(config);
                _logging.Info($"GPU OC settings saved: Core={GpuCoreClockOffset}, Mem={GpuMemoryClockOffset}, Power={GpuPowerLimitPercent}%, Voltage={GpuVoltageOffsetMv}mV");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save GPU OC settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Restore GPU OC settings from config on startup.
        /// </summary>
        private void RestoreGpuOcFromConfig()
        {
            try
            {
                var config = _configService.Config;
                if (config.GpuOc != null && config.GpuOc.ApplyOnStartup)
                {
                    var guardrailNote = ApplyGpuOcRequestToUi(
                        config.GpuOc.CoreClockOffsetMHz,
                        config.GpuOc.MemoryClockOffsetMHz,
                        config.GpuOc.PowerLimitPercent,
                        config.GpuOc.VoltageOffsetMv ?? 0);
                    
                    _logging.Info($"GPU OC settings restored from config: Core={GpuCoreClockOffset}, Mem={GpuMemoryClockOffset}, Power={GpuPowerLimitPercent}%, Voltage={GpuVoltageOffsetMv}mV");
                    if (!string.IsNullOrEmpty(guardrailNote))
                    {
                        GpuOcStatus = $"Restored with guardrails: {guardrailNote}";
                        _logging.Info($"GPU OC restore adjusted to detected limits: {guardrailNote}");
                    }
                    
                    // Apply restored settings after a short delay
                    RunStartupTask(async () =>
                    {
                        await Task.Delay(2000); // Wait for GPU to be fully ready
                        var dispatcher = App.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(ApplyGpuOc);
                        }
                        else
                        {
                            ApplyGpuOc();
                        }
                    }, "GPU OC restore");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore GPU OC settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load saved GPU OC profiles from config.
        /// </summary>
        private void LoadGpuOcProfiles()
        {
            try
            {
                GpuOcProfiles.Clear();

                foreach (var builtInProfile in BuildBuiltInGpuOcProfiles())
                {
                    GpuOcProfiles.Add(builtInProfile);
                }
                
                // Load custom profiles from config
                var config = _configService.Config;
                foreach (var profile in config.GpuOcProfiles)
                {
                    if (!GpuOcProfiles.Any(p => p.Name == profile.Name))
                    {
                        GpuOcProfiles.Add(profile);
                    }
                }

                var lastProfileName = config.LastGpuOcProfileName switch
                {
                    "Power Saver" => "Safe",
                    "Mild OC" => "Balanced",
                    _ => config.LastGpuOcProfileName
                };

                SelectedGpuOcProfile = GpuOcProfiles.FirstOrDefault(p => p.Name == lastProfileName) ?? GpuOcProfiles.FirstOrDefault();
                
                _logging.Info($"Loaded {GpuOcProfiles.Count} GPU OC profiles ({config.GpuOcProfiles.Count} custom)");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to load GPU OC profiles: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load a GPU OC profile into the sliders.
        /// </summary>
        private void LoadGpuOcProfile(Models.GpuOcProfile profile)
        {
            var guardrailNote = ApplyGpuOcRequestToUi(
                profile.CoreClockOffsetMHz,
                profile.MemoryClockOffsetMHz,
                profile.PowerLimitPercent,
                profile.VoltageOffsetMv);
            
            _logging.Info($"Loaded GPU OC profile: {profile.Name} (Core: {profile.CoreClockOffsetMHz}, Mem: {profile.MemoryClockOffsetMHz}, Power: {profile.PowerLimitPercent}%, Voltage: {profile.VoltageOffsetMv}mV)");
            GpuOcStatus = string.IsNullOrEmpty(guardrailNote)
                ? $"Loaded profile: {profile.Name}"
                : $"Loaded '{profile.Name}' with guardrails: {guardrailNote}";
            
            // Save last selected profile
            var config = _configService.Config;
            config.LastGpuOcProfileName = profile.Name;
            _configService.Save(config);
        }
        
        /// <summary>
        /// Save current GPU OC settings as a new profile.
        /// </summary>
        private void SaveGpuOcProfile()
        {
            if (string.IsNullOrWhiteSpace(NewGpuOcProfileName))
            {
                System.Windows.MessageBox.Show("Please enter a name for the profile.", "Profile Name Required",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            
            // Check if name already exists (for built-in profiles)
            if (GpuOcProfiles.Any(p => p.Name == NewGpuOcProfileName && 
                IsBuiltInGpuOcProfile(p.Name)))
            {
                System.Windows.MessageBox.Show("Cannot overwrite built-in profiles. Please choose a different name.",
                    "Invalid Name", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            // Remove existing profile with same name
            var existing = GpuOcProfiles.FirstOrDefault(p => p.Name == NewGpuOcProfileName);
            if (existing != null)
            {
                GpuOcProfiles.Remove(existing);
            }
            
            // Create new profile
            var profile = new Models.GpuOcProfile
            {
                Name = NewGpuOcProfileName,
                CoreClockOffsetMHz = GpuCoreClockOffset,
                MemoryClockOffsetMHz = GpuMemoryClockOffset,
                PowerLimitPercent = GpuPowerLimitPercent,
                VoltageOffsetMv = GpuVoltageOffsetMv,
                Description = $"Custom profile: Core {GpuCoreClockOffsetText}, Mem {GpuMemoryClockOffsetText}, Power {GpuPowerLimitText}, Voltage {GpuVoltageOffsetText}",
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };
            
            GpuOcProfiles.Add(profile);
            SelectedGpuOcProfile = profile;
            
            // Save to config
            SaveGpuOcProfilesToConfig();
            
            // Clear input
            NewGpuOcProfileName = "";
            
            GpuOcStatus = $"✓ Saved profile: {profile.Name}";
            _logging.Info($"Saved GPU OC profile: {profile.Name}");
        }
        
        /// <summary>
        /// Delete the selected GPU OC profile.
        /// </summary>
        private void DeleteGpuOcProfile()
        {
            if (SelectedGpuOcProfile == null)
                return;
            
            // Don't allow deleting built-in profiles
            if (IsBuiltInGpuOcProfile(SelectedGpuOcProfile.Name))
            {
                System.Windows.MessageBox.Show("Cannot delete built-in profiles.", "Delete Profile",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            var profileName = SelectedGpuOcProfile.Name;
            GpuOcProfiles.Remove(SelectedGpuOcProfile);
            SelectedGpuOcProfile = GpuOcProfiles.FirstOrDefault();
            
            // Save to config
            SaveGpuOcProfilesToConfig();
            
            GpuOcStatus = $"Deleted profile: {profileName}";
            _logging.Info($"Deleted GPU OC profile: {profileName}");
        }
        
        /// <summary>
        /// Save custom GPU OC profiles to config.
        /// </summary>
        private void SaveGpuOcProfilesToConfig()
        {
            try
            {
                var config = _configService.Config;
                
                // Only save custom profiles (not built-in)
                config.GpuOcProfiles = GpuOcProfiles
                    .Where(p => !IsBuiltInGpuOcProfile(p.Name))
                    .ToList();
                
                _configService.Save(config);
                _logging.Info($"Saved {config.GpuOcProfiles.Count} custom GPU OC profiles to config");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save GPU OC profiles: {ex.Message}");
            }
        }
        
        #endregion
        
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
                _logging.ErrorWithContext(
                    component: "SystemControlViewModel",
                    operation: "DetectGpuMode",
                    message: "Failed to detect GPU mode",
                    ex: ex);
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
                OnPropertyChanged(nameof(CurrentPerformanceModeIndicator));
                OnPropertyChanged(nameof(SelectedPerformanceMode));
                OnPropertyChanged(nameof(IsQuietMode));
                OnPropertyChanged(nameof(IsBalancedMode));
                OnPropertyChanged(nameof(IsPerformanceMode));
                OnPropertyChanged(nameof(IsFanPerformanceLinked));
                OnPropertyChanged(nameof(FanPerformanceLinkBadgeText));
                OnPropertyChanged(nameof(PerformanceModeFanPolicyHint));
                OnPropertyChanged(nameof(ShowFanPerformanceInfoBanner));
            }
        }

        public void RefreshFanLinkState()
        {
            OnPropertyChanged(nameof(IsFanPerformanceLinked));
            OnPropertyChanged(nameof(FanPerformanceLinkBadgeText));
            OnPropertyChanged(nameof(PerformanceModeFanPolicyHint));
            OnPropertyChanged(nameof(ShowFanPerformanceInfoBanner));
        }

        private void DismissFanPerformanceInfoBanner()
        {
            var config = _configService.Config;
            if (config.DismissedFanPerformanceDecouplingNotice)
            {
                return;
            }

            config.DismissedFanPerformanceDecouplingNotice = true;
            _configService.Save(config);
            RefreshFanLinkState();
        }
        
        public string CurrentPerformanceModeName => SelectedPerformanceMode?.Name ?? "Auto";

        public string CurrentPerformanceModeIndicator
        {
            get
            {
                var modeName = CurrentPerformanceModeName;

                if (!IsPerformanceMode)
                {
                    return modeName;
                }

                if (!GpuPowerBoostAvailable)
                {
                    return $"{modeName} • GPU Power: base";
                }

                var boostText = GpuPowerBoostLevel switch
                {
                    "Maximum" => "+15W GPU Boost",
                    "Extended" => "+25W GPU Boost",
                    "Medium" => "Balanced GPU power",
                    "Minimum" => "Base TGP",
                    _ => "GPU power unknown"
                };

                return $"{modeName} • {boostText}";
            }
        }
        
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
        
        /// <summary>
        /// Select and apply a performance mode but don't save to config.
        /// Used when restoring defaults after game exit - we apply balanced mode 
        /// temporarily but preserve the user's saved preference for next startup.
        /// </summary>
        public void SelectPerformanceModeWithoutSave(string modeName)
        {
            var mode = PerformanceModes.FirstOrDefault(m => 
                m.Name.Equals(modeName, StringComparison.OrdinalIgnoreCase));
            if (mode != null)
            {
                _selectedPerformanceMode = mode;
                _performanceModeService.Apply(mode);
                _logging.Info($"Performance mode applied (temporary, not saved): {mode.Name}");
                
                OnPropertyChanged(nameof(SelectedPerformanceMode));
                OnPropertyChanged(nameof(CurrentPerformanceModeName));
                OnPropertyChanged(nameof(IsQuietMode));
                OnPropertyChanged(nameof(IsBalancedMode));
                OnPropertyChanged(nameof(IsPerformanceMode));
            }
        }

        private async Task ApplyUndervoltAsync()
        {
            try
            {
                UndervoltLastActionText = "Applying CPU tuning settings...";
                UndervoltLastActionColor = System.Windows.Media.Brushes.SkyBlue;
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

                    // Save undervolt preferences to config (with null safety)
                    var config = _configService.Config;
                    config.Undervolt ??= new UndervoltPreferences();
                    config.Undervolt.DefaultOffset ??= new UndervoltOffset();
                    config.Undervolt.DefaultOffset.CoreMv = RequestedCoreOffset;
                    config.Undervolt.DefaultOffset.CacheMv = RequestedCacheOffset;
                    config.Undervolt.EnablePerCoreUndervolt = EnablePerCoreUndervolt;
                    config.Undervolt.PerCoreOffsetsMv = RequestedPerCoreOffsets?.Clone() as int?[];
                    config.Undervolt.RespectExternalControllers = RespectExternalUndervolt;
                    _configService.Save(config);
                }, "Applying undervolt settings...");

                UndervoltLastActionText = IsAmdCpu
                    ? $"Curve Optimizer applied: Core {RequestedCoreOffset:+0;-0;0} mV eq., iGPU {RequestedCacheOffset:+0;-0;0} mV eq."
                    : $"Undervolt applied: Core {RequestedCoreOffset:+0;-0;0} mV, Cache {RequestedCacheOffset:+0;-0;0} mV";
                UndervoltLastActionColor = System.Windows.Media.Brushes.Lime;
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "SystemControlViewModel",
                    operation: "ApplyUndervoltAsync",
                    message: "Failed to apply undervolt",
                    ex: ex);
                UndervoltLastActionText = $"Apply failed: {ex.Message}";
                UndervoltLastActionColor = System.Windows.Media.Brushes.OrangeRed;
                System.Windows.MessageBox.Show(
                    $"Failed to apply undervolt settings:\n\n{ex.Message}\n\n" +
                    "This may be caused by:\n" +
                    "• MSR access blocked (install PawnIO or disable Secure Boot)\n" +
                    "• HVCI/Core Isolation enabled\n" +
                    "• Unsupported CPU",
                    "Undervolt Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task ResetUndervoltAsync()
        {
            try
            {
                UndervoltLastActionText = IsAmdCpu ? "Resetting Curve Optimizer offsets..." : "Resetting undervolt offsets...";
                UndervoltLastActionColor = System.Windows.Media.Brushes.SkyBlue;
                await _undervoltService.ResetAsync();
                UndervoltLastActionText = IsAmdCpu ? "Curve Optimizer offsets reset to defaults" : "Undervolt offsets reset to defaults";
                UndervoltLastActionColor = System.Windows.Media.Brushes.Lime;
            }
            catch (Exception ex)
            {
                _logging.ErrorWithContext(
                    component: "SystemControlViewModel",
                    operation: "ResetUndervoltAsync",
                    message: "Failed to reset undervolt",
                    ex: ex);
                UndervoltLastActionText = $"Reset failed: {ex.Message}";
                UndervoltLastActionColor = System.Windows.Media.Brushes.OrangeRed;
            }
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

        #region EDP Throttling Mitigation

        private void OnEdpThrottlingDetected(object? sender, EdpThrottlingEventArgs e)
        {
            _logging.Info($"EDP throttling detected at {e.Timestamp:O}");
        }

        private void OnEdpMitigationApplied(object? sender, EdpThrottlingEventArgs e)
        {
            _logging.Info($"EDP throttling mitigation applied: {e.UndervoltOffsetMv}mV additional undervolt");
        }

        private void OnEdpMitigationRemoved(object? sender, EdpThrottlingEventArgs e)
        {
            _logging.Info($"EDP throttling mitigation removed");
        }

        #endregion

        #region AMD Power/Temperature Controls

        private void ApplyAmdPowerLimits()
        {
            if (_undervoltService?.Provider is not AmdUndervoltProvider amdProvider)
            {
                _logging.Warn("AMD power limits not available - not an AMD CPU or provider not initialized");
                return;
            }

            try
            {
                // Apply STAPM limit (sustained power)
                uint stapmMw = AmdStapmLimitWatts * 1000; // Convert W to mW
                var stapmResult = amdProvider.SetStapmLimit(stapmMw);
                
                // Apply temperature limit
                var tempResult = amdProvider.SetTctlTemp(AmdTempLimitC);
                
                if (stapmResult == RyzenSmu.SmuStatus.Ok && tempResult == RyzenSmu.SmuStatus.Ok)
                {
                    _logging.Info($"AMD power limits applied: STAPM={AmdStapmLimitWatts}W, Temp={AmdTempLimitC}°C");
                    SaveAmdPowerLimitsToConfig();
                }
                else
                {
                    _logging.Warn($"AMD power limits partially applied: STAPM={stapmResult}, Temp={tempResult}");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply AMD power limits: {ex.Message}", ex);
            }
        }

        private void ResetAmdPowerLimits()
        {
            // Reset to conservative defaults
            AmdStapmLimitWatts = 25;
            AmdTempLimitC = 95;
            
            ApplyAmdPowerLimits();
            _logging.Info("AMD power limits reset to defaults");
        }

        private void SaveAmdPowerLimitsToConfig()
        {
            try
            {
                var config = _configService.Config;
                if (config.AmdPowerLimits == null)
                    config.AmdPowerLimits = new AmdPowerLimits();
                
                config.AmdPowerLimits.StapmLimitWatts = AmdStapmLimitWatts;
                config.AmdPowerLimits.TempLimitC = AmdTempLimitC;
                
                _configService.Save(config);
                _logging.Info($"AMD power limits saved: STAPM={AmdStapmLimitWatts}W, Temp={AmdTempLimitC}°C");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to save AMD power limits: {ex.Message}");
            }
        }

        private void RestoreAmdPowerLimitsFromConfig()
        {
            try
            {
                var config = _configService.Config;
                if (config.AmdPowerLimits != null)
                {
                    AmdStapmLimitWatts = config.AmdPowerLimits.StapmLimitWatts;
                    AmdTempLimitC = config.AmdPowerLimits.TempLimitC;
                    
                    _logging.Info($"AMD power limits restored: STAPM={AmdStapmLimitWatts}W, Temp={AmdTempLimitC}°C");
                    
                    // Apply after delay
                    RunStartupTask(async () =>
                    {
                        await Task.Delay(2000);
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(ApplyAmdPowerLimits);
                        }
                        else
                        {
                            ApplyAmdPowerLimits();
                        }
                    }, "AMD power limits restore");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore AMD power limits: {ex.Message}");
            }
        }

        #endregion

        public void Cleanup()
        {
            if (_hardwareMonitoringService != null)
                _hardwareMonitoringService.SampleUpdated -= OnMonitoringSampleUpdated;
            if (_edpMitigationService != null)
            {
                _edpMitigationService.ThrottlingDetected -= OnEdpThrottlingDetected;
                _edpMitigationService.MitigationApplied -= OnEdpMitigationApplied;
                _edpMitigationService.MitigationRemoved -= OnEdpMitigationRemoved;
            }
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
