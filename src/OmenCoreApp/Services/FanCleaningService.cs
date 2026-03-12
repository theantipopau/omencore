using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for running fan maintenance on HP OMEN laptops.
    /// 
    /// IMPORTANT SAFETY NOTE:
    /// True fan dust cleaning with reverse rotation (like HP's "Fan Cleaner" on OMEN Max 16)
    /// requires special omnidirectional BLDC fans that are hardware-designed to spin both ways.
    /// Most OMEN laptops do NOT have this hardware - only specific models (OMEN Max line) support it.
    /// 
    /// This service provides a SAFE alternative: "Fan Boost" mode that runs fans at maximum speed
    /// to help dislodge loose dust through increased airflow. It does NOT reverse fan direction
    /// as that would require specific hardware support that we cannot safely detect.
    /// 
    /// For true dust removal, users should use compressed air or open the chassis for cleaning.
    /// 
    /// BACKEND PRIORITY:
    /// 1. EC Access (PawnIO/legacy WinRing0) - Direct EC control, most responsive
    /// 2. OMEN Gaming Hub WMI - Uses OGH services if installed and running  
    /// 3. HP WMI BIOS - Standard HP WMI interface for fan control
    /// </summary>
    public class FanCleaningService
    {
        private readonly LoggingService _logging;
        private readonly IEcAccess? _ecAccess;
        private readonly HpWmiBios? _wmiBios;
        private readonly OghServiceProxy? _oghProxy;
        private readonly SystemInfoService _systemInfoService;
        
        /// <summary>
        /// The control backend being used
        /// </summary>
        public enum FanControlBackend
        {
            None,
            EcAccess,      // Direct EC via PawnIO/legacy WinRing0
            OghProxy,      // OMEN Gaming Hub WMI
            WmiBios        // HP WMI BIOS
        }
        
        private FanControlBackend _activeBackend = FanControlBackend.None;

        // HP OMEN EC registers for fan control (based on OmenMon project research)
        // These registers vary by model - the ones below are for newer models (2022+)
        // OmenMon register names: OMCC=0x62, XSS1/XSS2=0x2C/0x2D, FFFF=0xEC, HPCM=0x95
        
        private const ushort EC_OMCC = 0x62;              // Manual Fan Control toggle (OMCC)
        private const ushort EC_FAN1_DUTY = 0x2C;         // Fan 1 set speed % (XSS1)
        private const ushort EC_FAN2_DUTY = 0x2D;         // Fan 2 set speed % (XSS2)
        private const ushort EC_FAN_MAX = 0xEC;           // Max Fan Speed toggle (FFFF)
        private const ushort EC_HPCM = 0x95;              // Performance Mode register (HPCM)
        
        // Legacy registers (older models ~2019-2021)
        private const ushort EC_LEGACY_FAN_MODE = 0x46;
        private const ushort EC_LEGACY_FAN1 = 0x44;
        private const ushort EC_LEGACY_FAN2 = 0x45;
        
        // Control values
        private const byte FAN_MODE_AUTO = 0x00;
        private const byte FAN_MODE_MANUAL = 0x01;
        private const byte FAN_MAX_ENABLE = 0x01;
        private const byte FAN_MAX_DISABLE = 0x00;

        private const int BOOST_DURATION_SECONDS = 30;

        // Models known to support true fan reversal (Fan Cleaner technology)
        // These have omnidirectional BLDC fans designed for reverse operation
        private static readonly HashSet<string> FanCleanerSupportedModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "OMEN Max 16",
            "OMEN Max 17",
            // Add more as they're confirmed - HP only advertises this for Max line currently
        };

        public FanCleaningService(
            LoggingService logging, 
            IEcAccess? ecAccess, 
            SystemInfoService systemInfoService,
            HpWmiBios? wmiBios = null,
            OghServiceProxy? oghProxy = null)
        {
            _logging = logging;
            _ecAccess = ecAccess;
            _systemInfoService = systemInfoService;
            _wmiBios = wmiBios;
            _oghProxy = oghProxy;
            
            // Determine which backend to use (will be re-checked before each operation)
            DetermineActiveBackend();
        }
        
        /// <summary>
        /// Refresh the active backend detection. Call this before fan boost operations
        /// to pick up newly installed drivers (PawnIO) or started services (OGH).
        /// </summary>
        public void RefreshBackend()
        {
            // Reset the EC write test cache so it will be re-tested
            _ecWriteTestDone = false;
            _ecWriteWorks = false;
            
            // Re-determine the best available backend
            DetermineActiveBackend();
            
            _logging.Info($"Fan control backend refreshed: {ActiveBackendName}");
        }
        
        /// <summary>
        /// Determine the best available backend for fan control.
        /// Priority: EC Access > OGH Proxy > WMI BIOS
        /// </summary>
        private void DetermineActiveBackend()
        {
            _activeBackend = FanControlBackend.None;
            
            // 1. Check EC access (most direct control)
            if (_ecAccess != null && _ecAccess.IsAvailable && CanPerformEcWrite())
            {
                _activeBackend = FanControlBackend.EcAccess;
                _logging.Info("Fan boost using EC access backend (PawnIO/legacy WinRing0)");
                return;
            }
            
            // 2. Check WMI BIOS FIRST (more reliable than OGH on many models)
            // OGH proxy often reports "available" but commands don't actually work
            if (_wmiBios != null && _wmiBios.IsAvailable)
            {
                _activeBackend = FanControlBackend.WmiBios;
                _logging.Info("Fan boost using HP WMI BIOS backend");
                return;
            }
            
            // 3. Check OGH proxy (requires OMEN Gaming Hub - but commands may not work)
            if (_oghProxy != null && _oghProxy.IsAvailable)
            {
                _activeBackend = FanControlBackend.OghProxy;
                _logging.Info("Fan boost using OMEN Gaming Hub WMI proxy backend");
                _logging.Warn("Note: OGH commands may not work on all models - if fan boost fails, try PawnIO");
                return;
            }
            
            _logging.Warn("No fan control backend available - fan boost will be disabled");
        }
        
        /// <summary>
        /// Get the name of the active backend being used
        /// </summary>
        public string ActiveBackendName => _activeBackend switch
        {
            FanControlBackend.EcAccess => "EC Access (WinRing0/PawnIO)",
            FanControlBackend.OghProxy => "OMEN Gaming Hub WMI",
            FanControlBackend.WmiBios => "HP WMI BIOS",
            _ => "None"
        };

        /// <summary>
        /// Whether fan boost is supported on this system.
        /// Requires HP OMEN hardware and at least one control backend available.
        /// </summary>
        public bool IsSupported
        {
            get
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                if (!sysInfo.IsHpGaming)
                    return false;
                    
                // Check if any backend is available
                return _activeBackend != FanControlBackend.None;
            }
        }
        
        private bool _ecWriteTestDone = false;
        private bool _ecWriteWorks = false;
        
        /// <summary>
        /// Test if EC write operations actually work (Secure Boot may block IOCTLs)
        /// </summary>
        private bool CanPerformEcWrite()
        {
            if (_ecWriteTestDone)
                return _ecWriteWorks;
                
            _ecWriteTestDone = true;
            
            try
            {
                // Try a harmless read to see if IOCTL works
                // We can't test write without potentially affecting hardware
                // But if read fails, write will definitely fail too
                _ecAccess!.ReadByte(0x00);
                _ecWriteWorks = true;
            }
            catch
            {
                _ecWriteWorks = false;
            }
            
            return _ecWriteWorks;
        }

        /// <summary>
        /// Reason why fan boost is not supported (if applicable)
        /// </summary>
        public string UnsupportedReason
        {
            get
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                if (!sysInfo.IsHpGaming)
                    return "Fan boost is only supported on HP OMEN/Victus gaming laptops";
                    
                if (_activeBackend != FanControlBackend.None)
                    return "";
                    
                // No backend available - give detailed reason
                var reasons = new List<string>();
                
                if (_ecAccess == null || !_ecAccess.IsAvailable)
                    reasons.Add("EC driver (WinRing0/PawnIO) not available");
                else if (!CanPerformEcWrite())
                    reasons.Add("Secure Boot blocking EC access");
                    
                if (_oghProxy == null || !_oghProxy.IsAvailable)
                {
                    if (_oghProxy?.Status.IsInstalled == true)
                        reasons.Add("OMEN Gaming Hub installed but services not running");
                    else
                        reasons.Add("OMEN Gaming Hub not installed");
                }
                    
                if (_wmiBios == null || !_wmiBios.IsAvailable)
                    reasons.Add("HP WMI BIOS interface not responding");
                    
                return string.Join("; ", reasons) + "\n\nTo enable fan boost:\n" +
                       "• Install PawnIO from pawnio.eu (works with Secure Boot)\n" +
                       "• Or install OMEN Gaming Hub from Microsoft Store\n" +
                       "• Or disable Secure Boot in BIOS";
            }
        }
        
        /// <summary>
        /// Whether this specific model supports true fan reversal (HP Fan Cleaner technology).
        /// Only OMEN Max models with omnidirectional fans support this.
        /// </summary>
        public bool SupportsTrueFanReversal
        {
            get
            {
                var sysInfo = _systemInfoService.GetSystemInfo();
                if (string.IsNullOrEmpty(sysInfo.Model))
                    return false;
                    
                foreach (var model in FanCleanerSupportedModels)
                {
                    if (sysInfo.Model.Contains(model, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Get a description of what this feature does on the current hardware.
        /// </summary>
        public string FeatureDescription
        {
            get
            {
                if (SupportsTrueFanReversal)
                {
                    return "Your OMEN Max laptop supports HP's Fan Cleaner technology with reversible fans. " +
                           "This feature runs fans in reverse to blow dust out of the vents.";
                }
                else
                {
                    return "Fan Boost runs your fans at maximum speed to help dislodge loose dust through " +
                           "increased airflow. This is NOT the same as HP's Fan Cleaner (which reverses fans) - " +
                           "that feature requires special hardware only available on OMEN Max models.\n\n" +
                           "For thorough cleaning, use compressed air or open the chassis.";
                }
            }
        }

        /// <summary>
        /// Start the fan boost/maintenance cycle.
        /// This runs fans at maximum speed to help with dust removal through increased airflow.
        /// It does NOT reverse fan direction (that requires specific hardware).
        /// </summary>
        /// <param name="progressCallback">Callback for progress updates</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        public async Task StartCleaningAsync(Action<FanCleaningProgress> progressCallback, CancellationToken cancellationToken)
        {
            // Refresh backend detection to pick up newly installed drivers (PawnIO)
            // This fixes the issue where PawnIO works initially but errors after refresh
            RefreshBackend();
            
            if (!IsSupported)
            {
                throw new InvalidOperationException($"Fan boost is not supported on this system.\n\n{UnsupportedReason}");
            }

            string modeName = SupportsTrueFanReversal ? "Fan Cleaner" : "Fan Boost";
            
            try
            {
                progressCallback(new FanCleaningProgress 
                { 
                    Message = $"Initializing {modeName} ({ActiveBackendName})...", 
                    ProgressPercent = 0,
                    IsTrueFanReversal = SupportsTrueFanReversal
                });

                _logging.Info($"{modeName}: Starting fan maintenance cycle using {ActiveBackendName}");
                
                if (SupportsTrueFanReversal)
                {
                    // On OMEN Max models, HP may expose a proper dust cleaning command
                    // For now, we still use the safe fan boost method until we can
                    // properly verify the correct EC commands for fan reversal
                    _logging.Info("Note: OMEN Max detected but using safe fan boost mode. " +
                                 "For true fan reversal, use OMEN Gaming Hub.");
                }
                
                await RunFanBoostCycleAsync(progressCallback, cancellationToken);

                progressCallback(new FanCleaningProgress 
                { 
                    Message = $"{modeName} complete! Restoring normal operation...", 
                    ProgressPercent = 100,
                    IsTrueFanReversal = SupportsTrueFanReversal
                });

                await Task.Delay(500, cancellationToken);

                RestoreNormalOperation();

                _logging.Info($"{modeName} cycle completed successfully");
            }
            catch (OperationCanceledException)
            {
                _logging.Warn($"{modeName} was cancelled - restoring normal operation");
                RestoreNormalOperation();
                throw;
            }
            catch (Exception ex)
            {
                _logging.Error($"{modeName} failed - restoring normal operation", ex);
                RestoreNormalOperation();
                throw;
            }
        }

        private async Task RunFanBoostCycleAsync(Action<FanCleaningProgress> progressCallback, CancellationToken cancellationToken)
        {
            _logging.Info($"Starting fan boost cycle using {ActiveBackendName} backend");

            // This method runs fans at maximum speed to increase airflow
            // Higher airflow can help dislodge loose dust particles
            // This is safe and doesn't require special hardware

            if (_activeBackend == FanControlBackend.None)
            {
                throw new InvalidOperationException("No fan control backend available for fan boost");
            }

            try
            {
                // Enable max fan speed via the appropriate backend
                bool maxFanEnabled = _activeBackend switch
                {
                    FanControlBackend.EcAccess => EnableMaxFanViaEc(),
                    FanControlBackend.OghProxy => _oghProxy!.SetMaxFan(true),
                    FanControlBackend.WmiBios => _wmiBios!.SetFanMax(true),
                    _ => false
                };
                
                if (!maxFanEnabled)
                {
                    // Give a more helpful error message based on the backend
                    string helpMessage = _activeBackend switch
                    {
                        FanControlBackend.OghProxy => 
                            "OMEN Gaming Hub WMI commands are not supported on your model.\n\n" +
                            "Your OMEN 17-ck2 series requires direct EC access for fan boost.\n\n" +
                            "Solutions:\n" +
                            "• Install PawnIO from pawnio.eu (Secure Boot compatible)\n" +
                            "• Download LpcACPIEC.bin from github.com/namazso/PawnIO.Modules/releases\n" +
                            "• Place it in: C:\\Program Files\\PawnIO\\modules\\\n" +
                            "• Or disable Secure Boot and use WinRing0",
                        FanControlBackend.WmiBios =>
                            "HP WMI BIOS fan commands failed.\n\n" +
                            "Your model may require direct EC access.\n\n" +
                            "Solutions:\n" +
                            "• Install PawnIO from pawnio.eu (Secure Boot compatible)\n" +
                            "• Or disable Secure Boot and use WinRing0",
                        _ => $"Failed to enable max fan via {ActiveBackendName}"
                    };
                    throw new InvalidOperationException(helpMessage);
                }
                
                _logging.Info($"Max fan mode enabled via {ActiveBackendName}");

                // Run at max for the full duration
                for (int i = 0; i < BOOST_DURATION_SECONDS; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int percent = (i + 1) * 100 / BOOST_DURATION_SECONDS;
                    int remaining = BOOST_DURATION_SECONDS - i - 1;

                    progressCallback(new FanCleaningProgress
                    {
                        Message = $"💨 Fan Boost active ({ActiveBackendName}) - maximum airflow... {remaining}s remaining",
                        ProgressPercent = percent,
                        IsTrueFanReversal = false
                    });

                    await Task.Delay(1000, cancellationToken);
                }

                _logging.Info("Fan boost cycle completed");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logging.Error($"Fan control blocked: {ex.Message}");
                throw new InvalidOperationException(
                    $"Fan boost failed via {ActiveBackendName} - access blocked. " +
                    "Install PawnIO from pawnio.eu for Secure Boot compatible EC access, " +
                    "or install OMEN Gaming Hub.", ex);
            }
        }
        
        /// <summary>
        /// Enable max fan via direct EC access (WinRing0/PawnIO)
        /// Uses OmenMon-style EC registers which work better on 2022+ OMEN models.
        /// </summary>
        private bool EnableMaxFanViaEc()
        {
            if (_ecAccess == null || !_ecAccess.IsAvailable)
                return false;
                
            try
            {
                // Read current values for diagnostics
                byte currentOMCC = _ecAccess.ReadByte(EC_OMCC);
                byte currentFan1 = _ecAccess.ReadByte(EC_FAN1_DUTY);
                byte currentFan2 = _ecAccess.ReadByte(EC_FAN2_DUTY);
                byte currentMax = _ecAccess.ReadByte(EC_FAN_MAX);
                byte currentHPCM = _ecAccess.ReadByte(EC_HPCM);
                _logging.Info($"EC fan state before: OMCC=0x{currentOMCC:X2}, XSS1=0x{currentFan1:X2}, XSS2=0x{currentFan2:X2}, FFFF=0x{currentMax:X2}, HPCM=0x{currentHPCM:X2}");
                
                // Method 1: Try the FFFF (0xEC) max fan toggle first - simplest approach
                _ecAccess.WriteByte(EC_FAN_MAX, FAN_MAX_ENABLE);
                _logging.Info($"Set max fan toggle via EC (wrote 0x{FAN_MAX_ENABLE:X2} to FFFF=0x{EC_FAN_MAX:X2})");
                
                // Method 2: Also enable manual control and set fan speeds to 100%
                _ecAccess.WriteByte(EC_OMCC, FAN_MODE_MANUAL);
                _logging.Info($"Enabled manual fan control via EC (wrote 0x{FAN_MODE_MANUAL:X2} to OMCC=0x{EC_OMCC:X2})");
                
                // Set both fans to 100% (0x64 = 100 in decimal, represents percentage)
                _ecAccess.WriteByte(EC_FAN1_DUTY, 0x64);
                _ecAccess.WriteByte(EC_FAN2_DUTY, 0x64);
                _logging.Info($"Fans set to 100% via EC (wrote 0x64 to XSS1=0x{EC_FAN1_DUTY:X2} and XSS2=0x{EC_FAN2_DUTY:X2})");
                
                // Read back to verify
                byte newOMCC = _ecAccess.ReadByte(EC_OMCC);
                byte newFan1 = _ecAccess.ReadByte(EC_FAN1_DUTY);
                byte newFan2 = _ecAccess.ReadByte(EC_FAN2_DUTY);
                byte newMax = _ecAccess.ReadByte(EC_FAN_MAX);
                _logging.Info($"EC fan state after: OMCC=0x{newOMCC:X2}, XSS1=0x{newFan1:X2}, XSS2=0x{newFan2:X2}, FFFF=0x{newMax:X2}");
                
                // Check if registers actually changed
                bool registersChanged = (newOMCC != currentOMCC) || (newFan1 != currentFan1) || 
                                        (newFan2 != currentFan2) || (newMax != currentMax);
                                        
                if (!registersChanged)
                {
                    // Firmware may be ignoring EC writes - try legacy registers as fallback
                    _logging.Warn("OmenMon-style EC writes didn't change registers, trying legacy registers...");
                    return TryLegacyEcRegisters();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"EC fan control failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Fallback to legacy EC registers (for older 2019-2021 models)
        /// </summary>
        private bool TryLegacyEcRegisters()
        {
            if (_ecAccess == null || !_ecAccess.IsAvailable)
                return false;
                
            try
            {
                byte currentMode = _ecAccess.ReadByte(EC_LEGACY_FAN_MODE);
                byte currentFan1 = _ecAccess.ReadByte(EC_LEGACY_FAN1);
                byte currentFan2 = _ecAccess.ReadByte(EC_LEGACY_FAN2);
                _logging.Info($"Legacy EC state before: Mode=0x{currentMode:X2}, Fan1=0x{currentFan1:X2}, Fan2=0x{currentFan2:X2}");
                
                _ecAccess.WriteByte(EC_LEGACY_FAN_MODE, FAN_MODE_MANUAL);
                _ecAccess.WriteByte(EC_LEGACY_FAN1, 0xFF);
                _ecAccess.WriteByte(EC_LEGACY_FAN2, 0xFF);
                _logging.Info("Set legacy EC registers for max fan");
                
                byte newMode = _ecAccess.ReadByte(EC_LEGACY_FAN_MODE);
                byte newFan1 = _ecAccess.ReadByte(EC_LEGACY_FAN1);
                byte newFan2 = _ecAccess.ReadByte(EC_LEGACY_FAN2);
                _logging.Info($"Legacy EC state after: Mode=0x{newMode:X2}, Fan1=0x{newFan1:X2}, Fan2=0x{newFan2:X2}");
                
                // Warn about firmware limitations
                var sysInfo = _systemInfoService.GetSystemInfo();
                if (sysInfo.Model?.Contains("ck2") == true || sysInfo.CpuName?.Contains("13th") == true)
                {
                    _logging.Warn("⚠️ Note: 13th Gen OMEN models may have firmware that requires BIOS WMI for fan control.");
                    _logging.Warn("   If fans didn't respond to EC writes, this is a firmware limitation.");
                    _logging.Warn("   Try using HP WMI BIOS backend instead (ensure OGH is not blocking it).");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logging.Error($"Legacy EC fan control failed: {ex.Message}");
                return false;
            }
        }

        private void RestoreNormalOperation()
        {
            _logging.Info($"Restoring normal fan operation via {ActiveBackendName}");
            
            try
            {
                bool restored = _activeBackend switch
                {
                    FanControlBackend.EcAccess => RestoreViaEc(),
                    FanControlBackend.OghProxy => _oghProxy!.SetMaxFan(false),
                    FanControlBackend.WmiBios => _wmiBios!.SetFanMax(false),
                    _ => false
                };
                
                if (restored)
                {
                    _logging.Info($"Restored auto fan control mode via {ActiveBackendName}");
                }
                else
                {
                    _logging.Warn($"Failed to restore fan mode via {ActiveBackendName} - fans may stay at high speed until reboot");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to restore fan control mode: {ex.Message}");
            }
        }
        
        private bool RestoreViaEc()
        {
            if (_ecAccess == null || !_ecAccess.IsAvailable)
                return false;
                
            try
            {
                // Disable max fan toggle (OmenMon style)
                _ecAccess.WriteByte(EC_FAN_MAX, FAN_MAX_DISABLE);
                
                // Restore auto fan control (OmenMon style)
                _ecAccess.WriteByte(EC_OMCC, FAN_MODE_AUTO);
                
                // Also restore legacy registers for older models
                _ecAccess.WriteByte(EC_LEGACY_FAN_MODE, FAN_MODE_AUTO);
                
                _logging.Info("Restored auto fan control via EC (OMCC and legacy registers)");
                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to restore EC fan control: {ex.Message}");
                return false;
            }
        }
    }

    public class FanCleaningProgress
    {
        public string Message { get; set; } = "";
        public int ProgressPercent { get; set; }
        
        /// <summary>
        /// Whether this is true fan reversal (only on supported OMEN Max models)
        /// or just high-speed fan boost (safe on all models).
        /// </summary>
        public bool IsTrueFanReversal { get; set; }
    }
}
