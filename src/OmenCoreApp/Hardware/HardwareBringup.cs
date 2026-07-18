using System;
using System.Linq;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// One-time hardware bring-up: NVAPI init, PawnIO MSR probe, WmiBiosMonitor construction,
    /// capability detection, EC access acquisition, fan controller construction.
    ///
    /// Extracted verbatim from MainViewModel's constructor (Stage 1 of the DI migration —
    /// see ROADMAP_v4.0.0.md Phase B). This sequence has real side effects (can spawn the
    /// out-of-process hardware worker, blocks synchronously for up to ~5s during capability
    /// probing) and must run eagerly, once, at the same point in app startup it always has —
    /// do not defer construction.
    /// </summary>
    public class HardwareBringup
    {
        public HardwareBringup(LoggingService logging, AppConfig config)
        {
            // ═══════════════════════════════════════════════════════════════════
            // SELF-SUSTAINING MONITORING ARCHITECTURE (v2.8.6+)
            //
            // OmenCore is self-sustaining without external monitoring dependencies.
            // Primary: WMI BIOS (temps, fans) + NVAPI (GPU metrics)
            // Optional: PawnIO MSR (CPU throttling detection only)
            //
            // This is the same approach as OmenMon — rock-solid, no dropouts.
            // ═══════════════════════════════════════════════════════════════════

            // 1. Initialize NVAPI early (for GPU load, clocks, VRAM, power)
            NvapiService? nvapiForMonitoring = null;
            try
            {
                NvapiService = new NvapiService(logging);
                if (NvapiService.Initialize())
                {
                    nvapiForMonitoring = NvapiService;
                    logging.Info($"✓ NVAPI initialized for monitoring: {NvapiService.GpuName}");
                }
                else
                {
                    logging.Info("NVAPI initialization returned false — GPU metrics will be limited");
                }
            }
            catch (Exception ex)
            {
                logging.Warn($"NVAPI initialization failed: {ex.Message}");
                NvapiService = null;
            }

            // 2. Try PawnIO MSR for CPU throttling detection (optional, non-critical)
            PawnIOMsrAccess? msrForMonitoring = null;
            bool pawnIOInstalledButMsrFailed = false;
            try
            {
                var msrAccess = new PawnIOMsrAccess();
                if (msrAccess.IsAvailable)
                {
                    msrForMonitoring = msrAccess;
                    logging.Info("✓ PawnIO MSR available for throttling detection");
                }
                else
                {
                    msrAccess.Dispose();
                    logging.Info("PawnIO MSR not available — throttling detection disabled");

                    // Check if PawnIO is installed but MSR module failed to load
                    // (indicates post-installation reboot needed)
                    if (PawnIOMsrAccess.IsPawnIOInstalled())
                    {
                        pawnIOInstalledButMsrFailed = true;
                        logging.Warn("[PawnIO] Installed but MSR initialization failed — driver may need a reboot to activate");
                    }
                }
            }
            catch (Exception ex)
            {
                logging.Debug($"PawnIO MSR init: {ex.Message}");

                // Check if PawnIO is installed
                if (PawnIOMsrAccess.IsPawnIOInstalled())
                {
                    pawnIOInstalledButMsrFailed = true;
                    logging.Warn($"[PawnIO] Installed but MSR initialization failed: {ex.Message}");
                }
            }

            // If PawnIO is installed but MSR failed, show notification to user
            if (pawnIOInstalledButMsrFailed)
            {
                logging.Info("⚠️  CPU power reading will report 0W. Please restart your computer to fully activate PawnIO driver.");
            }

            // 3. Create self-sustaining WmiBiosMonitor as PRIMARY monitoring bridge
            var wmiBiosMonitor = new WmiBiosMonitor(logging, nvapiForMonitoring, msrForMonitoring);

            // If battery monitoring is disabled in config, prevent all battery WMI queries
            if (config.Battery?.DisableMonitoring == true)
            {
                wmiBiosMonitor.DisableBatteryMonitoring();
                logging.Info("⚡ Battery monitoring disabled by config (Battery.DisableMonitoring=true)");
            }

            if (wmiBiosMonitor.IsAvailable)
            {
                logging.Info($"✓ Self-sustaining monitoring active: {wmiBiosMonitor.MonitoringSource}");
            }
            else
            {
                logging.Warn("WMI BIOS not available — monitoring will return zeros for some metrics");
            }

            // WmiBiosMonitor is ALWAYS the primary bridge — no LHM fallback needed
            WmiBiosMonitor = wmiBiosMonitor;

            // Run capability detection to identify available backends
            var capabilityService = new CapabilityDetectionService(logging);
            var capabilities = capabilityService.DetectCapabilities();
            var pawnIoBackend = capabilities.BackendStatuses.FirstOrDefault(b =>
                string.Equals(b.Name, "PawnIO", StringComparison.OrdinalIgnoreCase));

            // Run runtime probing to verify capability accuracy (especially for undervolt MSR access)
            capabilityService.ProbeRuntimeCapabilities();

            Capabilities = capabilities;

            // Set capability warning if functionality is limited
            if (capabilities.FanWritesBlockedForSafety)
            {
                CapabilityWarning = $"Desktop PC detected ({capabilities.Chassis}). Fan writes are disabled in v3.6.3 safety mode; monitoring and supported performance controls remain available.";
                logging.Warn("Desktop OMEN PC - fan writes disabled by v3.6.3 safety gate. Desktop RGB remains available via USB HID where supported.");
            }
            else if (capabilities.CanUndervolt && !capabilities.UndervoltRuntimeReady)
            {
                CapabilityWarning = $"Undervolt disabled: {capabilities.UndervoltBlockReason ?? "MSR access unavailable"}";
                logging.Warn(CapabilityWarning);
            }
            else if (capabilities.SecureBootEnabled && pawnIoBackend is { Healthy: false })
            {
                CapabilityWarning = "Secure Boot enabled — install PawnIO for EC/MSR features. Core monitoring and many controls continue via WMI.";
            }
            else if (capabilities.FanControl == FanControlMethod.MonitoringOnly)
            {
                CapabilityWarning = "Fan control unavailable - monitoring only mode.";
            }

            // Initialize EC access with automatic backend selection.
            // PawnIO is the direct EC backend.
            IEcAccess? ec = null;
            try
            {
                ec = EcAccessFactory.GetEcAccess();
                EcAccess = ec;
                if (ec != null && ec.IsAvailable)
                {
                    logging.Info($"EC access initialized: {EcAccessFactory.GetStatusMessage()}");
                    EcBackend = EcAccessFactory.ActiveBackend.ToString();
                }
                else
                {
                    logging.Info("EC access not available; will try WMI BIOS for fan control");
                    EcBackend = "None";
                }
            }
            catch (Exception ex)
            {
                logging.Info($"EC access initialization skipped: {ex.Message}");
                EcBackend = "Error";
            }

            // Create fan controller with intelligent backend selection using pre-detected capabilities.
            // Priority: OGH Proxy > WMI BIOS (no driver) > EC (PawnIO-preferred) > Fallback (monitoring only)
            var fanControllerFactory = new FanControllerFactory(wmiBiosMonitor, ec, config.EcFanRegisterMap, logging, capabilities, config.MaxFanLevelOverride);
            FanController = fanControllerFactory.Create();
            FanBackend = fanControllerFactory.ActiveBackend;
        }

        public NvapiService? NvapiService { get; }
        public WmiBiosMonitor WmiBiosMonitor { get; }
        public DeviceCapabilities Capabilities { get; }
        public string? CapabilityWarning { get; }
        public IEcAccess? EcAccess { get; }
        public string EcBackend { get; } = "Detecting...";
        public IFanController FanController { get; }
        public string FanBackend { get; } = "Detecting...";
    }
}
