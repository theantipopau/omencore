using System;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Interface for fan control operations.
    /// Implemented by both WMI-based and EC-based controllers.
    /// </summary>
    public interface IFanController : IDisposable
    {
        bool IsAvailable { get; }
        string Status { get; }
        string Backend { get; }

        bool ApplyPreset(FanPreset preset);
        bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve);
        bool SetFanSpeed(int percent);
        bool SetFanSpeeds(int cpuPercent, int gpuPercent);
        bool SetMaxFanSpeed(bool enabled);
        bool SetPerformanceMode(string modeName);
        bool RestoreAutoControl();
        IEnumerable<FanTelemetry> ReadFanSpeeds();

        // Quick profile methods
        void ApplyMaxCooling();
        void ApplyAutoMode();
        void ApplyQuietMode();

        /// <summary>
        /// Reset EC (Embedded Controller) to factory defaults.
        /// Restores BIOS control of fans and clears all manual overrides.
        /// Use this to fix stuck fan readings or restore normal BIOS display values.
        /// </summary>
        bool ResetEcToDefaults();

        // Verify that Max fan speed was applied successfully.
        // Returns true if verification succeeded; "details" contains a short diagnostic description.
        bool VerifyMaxApplied(out string details);
        
        /// <summary>
        /// Apply performance throttling mitigation via EC register 0x95.
        /// Discovered from omen-fan Linux utility - writing 0x31 to this register
        /// can help mitigate thermal throttling on some OMEN models.
        /// </summary>
        /// <returns>True if the mitigation was applied successfully.</returns>
        bool ApplyThrottlingMitigation();
    }
    

    /// <summary>
    /// Factory for creating fan controllers with automatic backend selection.
    /// Prioritizes WMI BIOS (no driver required) over OGH proxy over EC access (requires an EC-access backend like PawnIO/WinRing0).
    /// 
    /// For 2023+ OMEN laptops with Secure Boot, the selection order is:
    /// 1. OGH Proxy (requires OGH services running) - best for newer models
    /// 2. WMI BIOS (requires HP WMI classes) - works if BIOS responds
    /// 3. EC Access (requires PawnIO or WinRing0) - WinRing0 may require Secure Boot/Mem Integrity disabled
    /// 4. Fallback (monitoring only)
    /// </summary>
    public class FanControllerFactory
    {
        private readonly LoggingService? _logging;
        private readonly IHardwareMonitorBridge _hwMonitor;
        private readonly LibreHardwareMonitorImpl? _libreHwMonitor; // Optional, for enhanced metrics
        private readonly HpWmiBios _wmiBios; // For self-sufficient mode
        private readonly IEcAccess? _ecAccess;
        private readonly IReadOnlyDictionary<string, int>? _registerMap;
        private readonly DeviceCapabilities? _capabilities;
        private OghServiceProxy? _oghProxy;
        
        /// <summary>
        /// The backend currently in use (for UI display).
        /// </summary>
        public string ActiveBackend { get; private set; } = "None";

        public FanControllerFactory(
            IHardwareMonitorBridge hwMonitor,
            IEcAccess? ecAccess = null,
            IReadOnlyDictionary<string, int>? registerMap = null,
            LoggingService? logging = null,
            DeviceCapabilities? capabilities = null)
        {
            _hwMonitor = hwMonitor;
            _libreHwMonitor = hwMonitor as LibreHardwareMonitorImpl; // May be null if using WmiBiosMonitor
            _wmiBios = new HpWmiBios(logging); // Always available for self-sufficient fallback
            _ecAccess = ecAccess;
            _registerMap = registerMap;
            _logging = logging;
            _capabilities = capabilities;
        }
        
        /// <summary>
        /// Get fan speeds using the best available source.
        /// Priority: LibreHardwareMonitor > WMI BIOS
        /// </summary>
        private IEnumerable<(string Name, double Rpm)> GetFanSpeedsInternal()
        {
            // Try LibreHardwareMonitor first if available
            if (_libreHwMonitor != null)
            {
                try
                {
                    return _libreHwMonitor.GetFanSpeeds();
                }
                catch
                {
                    // Fall through to WMI BIOS
                }
            }
            
            // Fall back to WMI BIOS
            var rpms = _wmiBios.GetFanRpmDirect();
            var result = new List<(string, double)>();
            
            if (rpms.HasValue)
            {
                var (cpuRpm, gpuRpm) = rpms.Value;
                if (HpWmiBios.IsValidRpm(cpuRpm))
                {
                    result.Add(("CPU Fan", cpuRpm));
                }
                
                if (HpWmiBios.IsValidRpm(gpuRpm))
                {
                    result.Add(("GPU Fan", gpuRpm));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get CPU temperature using the best available source.
        /// </summary>
        private double GetCpuTemperatureInternal()
        {
            if (_libreHwMonitor != null)
            {
                try
                {
                    var temp = _libreHwMonitor.GetCpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            
            // Fall back to WMI BIOS
            return _wmiBios.GetTemperature() ?? 0;
        }
        
        /// <summary>
        /// Get GPU temperature using the best available source.
        /// </summary>
        private double GetGpuTemperatureInternal()
        {
            if (_libreHwMonitor != null)
            {
                try
                {
                    var temp = _libreHwMonitor.GetGpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            
            // Fall back to WMI BIOS
            return _wmiBios.GetGpuTemperature() ?? 0;
        }

        /// <summary>
        /// Create the best available fan controller.
        /// Uses DeviceCapabilities if available to skip unavailable backends.
        /// Order of preference: OGH Proxy > WMI BIOS > EC Access > Fallback
        /// </summary>
        public IFanController Create()
        {
            _logging?.Info("Initializing fan controller...");
            
            // If we have capability info, use it to optimize backend selection
            if (_capabilities != null)
            {
                _logging?.Info($"Using pre-detected capabilities: FanControl={_capabilities.FanControl}");
                return CreateFromCapabilities();
            }

            // Fallback: try all backends in order
            return CreateWithAutoDetection();
        }
        
        /// <summary>
        /// Create fan controller based on pre-detected capabilities (faster startup).
        /// </summary>
        private IFanController CreateFromCapabilities()
        {
            switch (_capabilities!.FanControl)
            {
                case FanControlMethod.OghProxy:
                    var oghController = TryCreateOghController();
                    if (oghController != null)
                    {
                        ActiveBackend = "OGH Proxy";
                        _logging?.Info("✓ Using OGH proxy (pre-detected)");
                        return oghController;
                    }
                    break;
                    
                case FanControlMethod.WmiBios:
                    var wmiController = TryCreateWmiController();
                    if (wmiController != null)
                    {
                        ActiveBackend = "WMI BIOS";
                        _logging?.Info("✓ Using WMI BIOS (pre-detected)");
                        return wmiController;
                    }
                    break;
                    
                case FanControlMethod.EcDirect:
                    var ecController = TryCreateEcController();
                    if (ecController != null)
                    {
                        ActiveBackend = "EC Direct";
                        _logging?.Info("✓ Using EC access (pre-detected)");
                        return ecController;
                    }
                    break;
            }
            
            // If pre-detected method failed, fall back to auto-detection
            _logging?.Warn("Pre-detected backend unavailable, trying auto-detection...");
            return CreateWithAutoDetection();
        }
        
        /// <summary>
        /// Create fan controller by trying all backends in order.
        /// OmenCore is designed to be FULLY INDEPENDENT from OMEN Gaming Hub.
        /// WMI BIOS is always preferred - OGH proxy is only a last-resort fallback.
        /// </summary>
        private IFanController CreateWithAutoDetection()
        {
            // Priority 1: WMI BIOS (no driver required, OGH-independent)
            // This is the preferred method for true independence from OGH
            var wmiController = TryCreateWmiController();
            if (wmiController != null)
            {
                ActiveBackend = "WMI BIOS";
                _logging?.Info("✓ Using WMI-based fan controller (OGH-independent, no driver required)");
                _logging?.Info("  Backend: HP WMI BIOS (HPWMISVC)");
                _logging?.Info("  Advantages: No additional drivers, Secure Boot compatible, OGH-independent");
                return wmiController;
            }
            
            // Fallback diagnostics: WMI BIOS unavailable
            _logging?.Warn("⚠️ WMI BIOS fan control not available on this system");
            _logging?.Info("  Possible reasons: Non-HP laptop, old BIOS, HPWMISVC not running");
            _logging?.Info("  Trying fallback: EC Direct access...");

            // Priority 2: EC Direct via PawnIO (Secure Boot compatible, OGH-independent)
            var ecController = TryCreateEcController();
            if (ecController != null)
            {
                ActiveBackend = "EC Direct";
                _logging?.Info("✓ Using EC-based fan controller (OGH-independent, requires PawnIO/WinRing0)");
                _logging?.Info($"  Backend: {_ecAccess?.GetType().Name ?? "Unknown EC access"}");
                _logging?.Info("  Advantages: Direct hardware control, works on older models");
                return ecController;
            }
            
            // Fallback diagnostics: EC access unavailable
            _logging?.Warn("⚠️ EC Direct fan control not available");
            _logging?.Info("  Possible reasons: WinRing0/PawnIO not loaded, Secure Boot blocking driver");
            _logging?.Info("  Trying fallback: OGH proxy...");

            // Priority 3: OGH proxy as last resort (requires OGH services)
            // Only used if WMI BIOS and EC both fail
            var oghController = TryCreateOghController();
            if (oghController != null)
            {
                ActiveBackend = "OGH Proxy";
                _logging?.Warn("⚠️ Using OGH proxy (WMI BIOS unavailable on this model)");
                _logging?.Info("  Backend: OMEN Gaming Hub WMI proxy");
                _logging?.Info("  Limitation: Requires OGH services running, profile-based only");
                _logging?.Info("  Consider reporting your model for native WMI BIOS support");
                return oghController;
            }
            
            // Fallback diagnostics: All control methods failed
            _logging?.Error("❌ All fan control backends unavailable:");
            _logging?.Error("  - WMI BIOS: Not available (HP WMI class missing or BIOS too old)");
            _logging?.Error($"  - EC Direct: {(_ecAccess == null ? "No EC access driver" : "EC not ready")}");
            _logging?.Error("  - OGH Proxy: Not available (OGH not installed or services not running)");
            _logging?.Info("  Falling back to monitoring-only mode (no fan control)");

            // Last resort: fallback controller with monitoring only
            ActiveBackend = "Monitoring Only";
            _logging?.Warn("⚠️ No fan control backend available - using monitoring-only mode");
            _logging?.Info("  Sensors will still work, but fan control will be unavailable");
            _logging?.Info("  Please report this configuration on GitHub for potential support");
            return new FallbackFanController(_libreHwMonitor, _logging);
        }

        /// <summary>
        /// Create OGH proxy-based controller if available.
        /// This is a FALLBACK method when WMI BIOS doesn't work on certain models.
        /// OmenCore prefers WMI BIOS for true OGH independence.
        /// </summary>
        public OghFanControllerWrapper? TryCreateOghController()
        {
            try
            {
                _oghProxy ??= new OghServiceProxy(_logging);
                
                if (_oghProxy.IsAvailable)
                {
                    _logging?.Info($"OGH proxy available - Services: {string.Join(", ", _oghProxy.Status.RunningServices)}");
                    return new OghFanControllerWrapper(_oghProxy, _libreHwMonitor, _logging);
                }
                
                // Try starting OGH services if installed but not running
                if (_oghProxy.Status.IsInstalled && !_oghProxy.Status.IsRunning)
                {
                    _logging?.Info("OGH installed but not running, attempting to start services...");
                    if (_oghProxy.TryStartOghServices() && _oghProxy.IsAvailable)
                    {
                        return new OghFanControllerWrapper(_oghProxy, _libreHwMonitor, _logging);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"OGH proxy initialization failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Create WMI-based controller if available.
        /// </summary>
        public WmiFanControllerWrapper? TryCreateWmiController()
        {
            try
            {
                var controller = new WmiFanController(_libreHwMonitor!, _logging);
                if (controller.IsAvailable)
                {
                    return new WmiFanControllerWrapper(controller, _logging);
                }
                controller.Dispose();
            }
            catch (Exception ex)
            {
                _logging?.Warn($"WMI controller initialization failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Create EC-based controller if available.
        /// </summary>
        public EcFanControllerWrapper? TryCreateEcController()
        {
            try
            {
                if (_ecAccess != null && _ecAccess.IsAvailable && _registerMap != null && _libreHwMonitor != null)
                {
                    var controller = new FanController(_ecAccess, _registerMap, _libreHwMonitor, _logging);
                    if (controller.IsEcReady)
                    {
                        return new EcFanControllerWrapper(controller, _libreHwMonitor, _logging);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"EC controller initialization failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Read fan RPM directly from EC registers (fallback when other methods fail).
        /// Based on FanController.ReadActualFanRpm logic.
        /// </summary>
        private (int fan1Rpm, int fan2Rpm) ReadFanRpmFromEc()
        {
            if (_ecAccess == null || !_ecAccess.IsAvailable)
                return (0, 0);
            // Retry/backoff strategy to mitigate inter-process EC contention.
            // Some contention manifests as transient 0 RPM or thrown timeouts when other apps access EC.
            const int attempts = 5;
            var readings = new List<(int f1, int f2)>();
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // Try alternative registers first (0x4A-0x4B for Fan1, 0x4C-0x4D for Fan2) - 16-bit RPM
                    try
                    {
                        var fan1Low = _ecAccess.ReadByte(0x4A);
                        var fan1High = _ecAccess.ReadByte(0x4B);
                        var fan2Low = _ecAccess.ReadByte(0x4C);
                        var fan2High = _ecAccess.ReadByte(0x4D);
                        _logging?.Debug($"EC Read alt RPM regs (attempt {attempt}): 0x4A=0x{fan1Low:X2}, 0x4B=0x{fan1High:X2}, 0x4C=0x{fan2Low:X2}, 0x4D=0x{fan2High:X2}");

                        var fan1Rpm = (fan1High << 8) | fan1Low;
                        var fan2Rpm = (fan2High << 8) | fan2Low;

                        // Validate RPM range (0-8000 is valid for laptop fans)
                        // 0xFF or 0xFFFF values indicate "no data" or error states
                        if ((HpWmiBios.IsValidRpm(fan1Rpm) && fan1Rpm > 0) || 
                            (HpWmiBios.IsValidRpm(fan2Rpm) && fan2Rpm > 0))
                        {
                            // Only return valid readings
                            var validF1 = HpWmiBios.IsValidRpm(fan1Rpm) ? fan1Rpm : 0;
                            var validF2 = HpWmiBios.IsValidRpm(fan2Rpm) ? fan2Rpm : 0;
                            readings.Add((validF1, validF2));
                            _logging?.Info($"EC RPMs (alt regs, attempt {attempt}): Fan1={validF1} RPM, Fan2={validF2} RPM");
                            // Good reading - return immediately
                            return (validF1, validF2);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging?.Debug($"EC alt register read failed (attempt {attempt}): {ex.Message}");
                        if (ex is TimeoutException || ex.Message.Contains("mutex", StringComparison.OrdinalIgnoreCase))
                        {
                            // Log contention event
                            _logging?.Warn($"EC contention detected during alt read (attempt {attempt}): {ex.Message}");
                        }
                    }

                    // Try primary registers (0x34/0x35) - units of 100 RPM
                    const ushort REG_FAN1_RPM = 0x34;
                    const ushort REG_FAN2_RPM = 0x35;

                    var fan1Unit = _ecAccess.ReadByte(REG_FAN1_RPM);
                    var fan2Unit = _ecAccess.ReadByte(REG_FAN2_RPM);
                    _logging?.Debug($"EC Read primary RPM regs (attempt {attempt}): 0x{REG_FAN1_RPM:X2}=0x{fan1Unit:X2}, 0x{REG_FAN2_RPM:X2}=0x{fan2Unit:X2}");

                    // Skip 0xFF values (invalid/error indicator)
                    // Max valid unit is 80 (8000 RPM / 100)
                    if (fan1Unit > 0 && fan1Unit < 0xFF || fan2Unit > 0 && fan2Unit < 0xFF)
                    {
                        // Only compute RPM for valid units (< 80 = 8000 RPM max)
                        var fan1Rpm = (fan1Unit > 0 && fan1Unit <= 80) ? fan1Unit * 100 : 0;
                        var fan2Rpm = (fan2Unit > 0 && fan2Unit <= 80) ? fan2Unit * 100 : 0;
                        
                        if (fan1Rpm > 0 || fan2Rpm > 0)
                        {
                            readings.Add((fan1Rpm, fan2Rpm));
                            _logging?.Info($"EC RPMs (primary, attempt {attempt}): Fan1={fan1Rpm} RPM, Fan2={fan2Rpm} RPM");
                            return (fan1Rpm, fan2Rpm);
                        }
                    }

                    // If we reach here, we got zeros - wait and retry
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"EC Read attempt {attempt} failed: {ex.Message}");
                }

                // Backoff with jitter
                try
                {
                    var delayMs = 50 * attempt + (new Random()).Next(20, 80);
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch { }
            }

            // If we collected any non-zero readings, pick the most recent non-zero
            for (int i = readings.Count - 1; i >= 0; i--)
            {
                var r = readings[i];
                if (r.f1 > 0 || r.f2 > 0) return r;
            }

            // Nothing useful found
            _logging?.Debug("EC ReadFanRpmFromEc: no valid RPM readings after retries");
            return (0, 0);
        }
    }

    /// <summary>
    /// Wrapper for OGH Service Proxy that implements IFanController.
    /// Uses OMEN Gaming Hub's WMI interface for fan control on 2023+ models.
    /// </summary>
    public class OghFanControllerWrapper : IFanController
    {
        public bool VerifyMaxApplied(out string details)
        {
            // Best-effort: check hardware monitor for RPMs or return fallback message
            var fans = SensorHelper.GetFanSpeeds(_hwMonitor);
            if (fans.Any())
            {
                details = $"HardwareMonitor RPMs: {string.Join(',', fans.Select(f => f.Rpm))}";
                return fans.Any(f => f.Rpm > 1000);
            }
            details = "No hardware RPM sensors available via HWMonitor (OGH).";
            return false;
        }
        
        private readonly OghServiceProxy _proxy;
        private readonly LibreHardwareMonitorImpl? _hwMonitor;
        private readonly LoggingService? _logging;
        private bool _disposed;

        public OghFanControllerWrapper(OghServiceProxy proxy, LibreHardwareMonitorImpl? hwMonitor, LoggingService? logging = null)
        {
            _proxy = proxy;
            _hwMonitor = hwMonitor;
            _logging = logging;
        }

        public bool IsAvailable => _proxy.IsAvailable;
        public string Status => _proxy.Status.CommandsWork ? "OGH proxy available" : _proxy.Status.Message;
        public string Backend => "OGH Proxy";

        public bool ApplyPreset(FanPreset preset)
        {
            try
            {
                // BUG FIX v2.6.1: Check for explicit Max mode or "Max" preset name and use SetMaxFan
                if (preset.Mode == FanMode.Max || preset.Name.Equals("Max", StringComparison.OrdinalIgnoreCase))
                {
                    _logging?.Info("OGH ApplyPreset: Max mode detected, calling SetMaxFan(true)");
                    return _proxy.SetMaxFan(true);
                }
                
                // Map preset to OGH thermal policy
                var policy = MapPresetToThermalPolicy(preset);
                return _proxy.SetThermalPolicy(policy);
            }
            catch (Exception ex)
            {
                _logging?.Error($"OGH ApplyPreset failed: {ex.Message}", ex);
                return false;
            }
        }

        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            // OGH doesn't support custom curves - use closest thermal policy
            _logging?.Info("OGH proxy: Custom curves not supported, using thermal policy approximation");
            
            // Analyze curve to determine aggressive/quiet mode
            int avgFanPercent = 0;
            int count = 0;
            foreach (var point in curve)
            {
                avgFanPercent += point.FanPercent;
                count++;
            }
            
            if (count > 0)
            {
                avgFanPercent /= count;
                if (avgFanPercent >= 70) return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Performance);
                if (avgFanPercent >= 50) return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
                return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Cool);
            }
            
            return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
        }

        public bool SetFanSpeed(int percent)
        {
            // Map percentage to thermal policy
            if (percent >= 80) return _proxy.SetMaxFan(true);
            if (percent >= 60) return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Performance);
            if (percent >= 40) return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
            return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Cool);
        }

        public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            // OGH doesn't support per-fan control, use average
            int avgPercent = (cpuPercent + gpuPercent) / 2;
            return SetFanSpeed(avgPercent);
        }

        public bool SetMaxFanSpeed(bool enabled)
        {
            return _proxy.SetMaxFan(enabled);
        }

        public bool SetPerformanceMode(string modeName)
        {
            // First try the new comprehensive performance policy method
            if (_proxy.SetPerformancePolicy(modeName))
                return true;
                
            // Fallback to thermal policy mapping
            var policy = MapModeNameToPolicy(modeName);
            return _proxy.SetThermalPolicy(policy);
        }

        public bool RestoreAutoControl()
        {
            return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
        }

        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Prefer OGH-provided fan telemetry when available
            try
            {
                var oghFans = _proxy.GetFanData();
                if (oghFans != null && oghFans.Length > 0)
                {
                    foreach (var fan in oghFans)
                        fans.Add(fan);
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"OGH GetFanData error: {ex.Message}");
            }

            // Fall back to sensor helper if OGH didn't provide data
            if (fans.Count == 0)
            {
                var fanSpeeds = SensorHelper.GetFanSpeeds(_hwMonitor);
                int index = 0;
                foreach (var (name, rpm) in fanSpeeds)
                {
                    fans.Add(new FanTelemetry
                    {
                        Name = name,
                        SpeedRpm = (int)rpm,
                        DutyCyclePercent = 0, // Unknown from HW monitor
                        Temperature = index == 0 ? SensorHelper.GetCpuTemperature(_hwMonitor) : SensorHelper.GetGpuTemperature(_hwMonitor)
                    });
                    index++;
                }
            }
            
            // Ensure we have at least placeholder entries
            if (fans.Count == 0)
            {
                fans.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = SensorHelper.GetCpuTemperature(_hwMonitor) });
                fans.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = SensorHelper.GetGpuTemperature(_hwMonitor) });
            }
            
            return fans;
        }

        private OghServiceProxy.ThermalPolicy MapPresetToThermalPolicy(FanPreset preset)
        {
            if (preset == null) return OghServiceProxy.ThermalPolicy.Default;
            
            var nameLower = preset.Name?.ToLowerInvariant() ?? "";
            if (nameLower.Contains("performance") || nameLower.Contains("turbo") || nameLower.Contains("max"))
                return OghServiceProxy.ThermalPolicy.Performance;
            if (nameLower.Contains("quiet") || nameLower.Contains("cool") || nameLower.Contains("silent"))
                return OghServiceProxy.ThermalPolicy.Cool;
            if (nameLower.Contains("l5p"))
                return OghServiceProxy.ThermalPolicy.L5P;
            
            return OghServiceProxy.ThermalPolicy.Default;
        }

        private OghServiceProxy.ThermalPolicy MapModeNameToPolicy(string? modeName)
        {
            var nameLower = modeName?.ToLowerInvariant() ?? "";
            if (nameLower.Contains("performance") || nameLower.Contains("turbo"))
                return OghServiceProxy.ThermalPolicy.Performance;
            if (nameLower.Contains("quiet") || nameLower.Contains("cool"))
                return OghServiceProxy.ThermalPolicy.Cool;
            if (nameLower.Contains("l5p"))
                return OghServiceProxy.ThermalPolicy.L5P;
            
            return OghServiceProxy.ThermalPolicy.Default;
        }

        private int EstimateDutyFromRpm(int rpm)
        {
            if (rpm == 0) return 0;
            const int minRpm = 1500;
            const int maxRpm = 6000;
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        public void ApplyMaxCooling() => _proxy.SetMaxFan(true);
        public void ApplyAutoMode() => _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
        public void ApplyQuietMode() => _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Cool);
        
        public bool ResetEcToDefaults()
        {
            _logging?.Info("Resetting EC to defaults via OGH proxy...");
            // OGH proxy: disable max fan and restore default thermal policy
            _proxy.SetMaxFan(false);
            return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Default);
        }
        
        public bool ApplyThrottlingMitigation()
        {
            _logging?.Info("Applying throttling mitigation via OGH proxy...");
            // OGH proxy: use Performance thermal policy for throttling mitigation
            return _proxy.SetThermalPolicy(OghServiceProxy.ThermalPolicy.Performance);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Don't dispose proxy - it may be shared
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Wrapper for WmiFanController that implements IFanController.
    /// </summary>
    public class WmiFanControllerWrapper : IFanController
    {
        public bool VerifyMaxApplied(out string details)
        {
            // WMI may not expose RPM directly; check hardware monitor first
            // Use controller's ReadFanSpeeds (which may read WMI or HWMonitor internally)
            try
            {
                var speeds = _controller.ReadFanSpeeds().ToList();
                if (speeds.Any())
                {
                    details = $"ReadFanSpeeds: {string.Join(',', speeds.Select(s => s.SpeedRpm))}";
                    return speeds.Any(s => s.SpeedRpm > 1000);
                }
            }
            catch (Exception ex)
            {
                details = $"Verify attempt failed: {ex.Message}";
                return false;
            }

            details = "No RPMs available via WMI or HWMonitor.";
            return false;
        }

        private readonly WmiFanController _controller;
        private readonly LoggingService? _logging;

        public WmiFanControllerWrapper(WmiFanController controller, LoggingService? logging = null)
        {
            _controller = controller;
            _logging = logging;
        }

        public bool IsAvailable => _controller.IsAvailable;
        public string Status => _controller.Status;
        public string Backend => "WMI BIOS";
        
        /// <summary>
        /// Check if WMI commands are ineffective on this model.
        /// Some newer OMEN models (Transcend, 2024+) return success but don't change fan speed.
        /// </summary>
        public bool CommandsIneffective => _controller.CommandsIneffective;
        
        /// <summary>
        /// Test if WMI commands actually affect fan behavior.
        /// Returns true if commands appear to work, false if they seem ineffective.
        /// </summary>
        public bool TestCommandEffectiveness() => _controller.TestCommandEffectiveness();

        public bool ApplyPreset(FanPreset preset) => _controller.ApplyPreset(preset);
        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => _controller.ApplyCustomCurve(curve);
        public bool SetFanSpeed(int percent) => _controller.SetFanSpeed(percent);
        public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => _controller.SetFanSpeeds(cpuPercent, gpuPercent);
        public bool SetMaxFanSpeed(bool enabled) => _controller.SetMaxFanSpeed(enabled);
        public bool SetPerformanceMode(string modeName) => _controller.SetPerformanceMode(modeName);
        public bool RestoreAutoControl() => _controller.RestoreAutoControl();
        public IEnumerable<FanTelemetry> ReadFanSpeeds() => _controller.ReadFanSpeeds();

        public void ApplyMaxCooling() => _controller.SetMaxFanSpeed(true);
        public void ApplyAutoMode() => _controller.RestoreAutoControl();
        public void ApplyQuietMode() => _controller.SetPerformanceMode("Cool");
        
        public bool ResetEcToDefaults()
        {
            _logging?.Info("Resetting EC to defaults via WMI BIOS...");
            return _controller.ResetEcToDefaults();
        }
        
        public bool ApplyThrottlingMitigation() => _controller.ApplyThrottlingMitigation();

        public void Dispose() => _controller.Dispose();
    }

    /// <summary>
    /// Wrapper for legacy FanController (EC-based) that implements IFanController.
    /// </summary>
    public class EcFanControllerWrapper : IFanController
    {
        /// <summary>
        /// Verify Max applied by checking EC RPM registers and hardware monitor values.
        /// Attempts to apply Max up to several retries when applying.
        /// </summary>
        public bool VerifyMaxApplied(out string details)
        {
            // Simple verification: read EC RPM registers via underlying controller
            try
            {
                var (f1, f2) = _controller.ReadActualFanRpmPublic();
                details = $"EC RPMs after apply: F1={f1},F2={f2}";
                return (f1 > 1000 || f2 > 1000);
            }
            catch (Exception ex)
            {
                details = $"Verify failed: {ex.Message}";
                return false;
            }
        }

        private readonly FanController _controller;
        private readonly LibreHardwareMonitorImpl? _hwMonitor;
        private readonly LoggingService? _logging;

        public EcFanControllerWrapper(FanController controller, LibreHardwareMonitorImpl? hwMonitor, LoggingService? logging = null)
        {
            _controller = controller;
            _hwMonitor = hwMonitor;
            _logging = logging;
        }

        public bool IsAvailable => _controller.IsEcReady;
        public string Status => _controller.IsEcReady ? "EC access available" : "EC access unavailable";
        public string Backend => "EC (WinRing0)";

        public bool ApplyPreset(FanPreset preset)
        {
            try
            {
                _controller.ApplyPreset(preset);
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply preset: {ex.Message}", ex);
                return false;
            }
        }

        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            try
            {
                _controller.ApplyCustomCurve(curve);
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to apply curve: {ex.Message}", ex);
                return false;
            }
        }

        public bool SetFanSpeed(int percent)
        {
            // EC controller doesn't have direct speed control, use curve
            var curve = new List<FanCurvePoint>
            {
                new FanCurvePoint { TemperatureC = 0, FanPercent = percent },
                new FanCurvePoint { TemperatureC = 100, FanPercent = percent }
            };
            return ApplyCustomCurve(curve);
        }

        public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            try
            {
                _controller.SetFanSpeeds(cpuPercent, gpuPercent);
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan speeds: {ex.Message}", ex);
                return false;
            }
        }

        public bool SetMaxFanSpeed(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    // Restore auto control to BIOS
                    _controller.RestoreAutoControl();
                    _logging?.Info("EC: Restored auto control");
                    return true;
                }

                if (!_controller.IsEcReady)
                {
                    _logging?.Warn("EC backend not ready - cannot apply Max fan speed");
                    return false;
                }

                // Retry loop with verification
                const int maxAttempts = 3;
                int attempt = 0;
                for (attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    _logging?.Info($"EC: Applying Max fan (attempt {attempt}/{maxAttempts})");

                    // Primary method: SetMaxSpeed (writes boost + max registers)
                    _controller.SetMaxSpeed();

                    // Wait briefly to allow fans to ramp
                    System.Threading.Thread.Sleep(150 * attempt);

                    // Verify via EC RPM registers
                    var (fan1, fan2) = _controller.ReadActualFanRpmPublic();
                    _logging?.Info($"EC Verify attempt {attempt}: RPMs read - Fan1={fan1}, Fan2={fan2}");

                    if (fan1 > 1000 || fan2 > 1000)
                    {
                        _logging?.Info($"EC: Max fan verified on attempt {attempt} (Fan1={fan1},Fan2={fan2})");
                        return true;
                    }

                    // Fallback attempt: Set explicit percent then boost
                    _logging?.Warn($"EC: Max apply not confirmed (attempt {attempt}), trying alternative sequence");
                    _controller.SetImmediatePercent(100); // uses explicit EC duty write as fallback
                    _controller.SetMaxSpeed();
                    System.Threading.Thread.Sleep(150);
                    var (fan1b, fan2b) = _controller.ReadActualFanRpmPublic();
                    _logging?.Info($"EC Verify alt attempt {attempt}: RPMs read - Fan1={fan1b}, Fan2={fan2b}");
                    if (fan1b > 1000 || fan2b > 1000)
                    {
                        _logging?.Info($"EC: Max fan verified after alt sequence on attempt {attempt}");
                        return true;
                    }
                }

                _logging?.Warn($"EC: Failed to verify Max fan after {maxAttempts} attempts");
                return false;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set max fan speed: {ex.Message}", ex);
                return false;
            }
        }

        public bool SetPerformanceMode(string modeName)
        {
            // EC controller doesn't support BIOS performance modes
            _logging?.Info($"EC backend: Performance mode '{modeName}' not directly supported, using fan curve approximation");
            
            var nameLower = modeName?.ToLowerInvariant() ?? "default";
            int targetPercent;
            
            if (nameLower.Contains("performance") || nameLower.Contains("turbo"))
            {
                targetPercent = 80;
            }
            else if (nameLower.Contains("quiet") || nameLower.Contains("cool"))
            {
                targetPercent = 40;
            }
            else
            {
                targetPercent = 60;
            }

            return SetFanSpeed(targetPercent);
        }

        public bool RestoreAutoControl()
        {
            try
            {
                // Use proper EC auto control restoration
                _controller.RestoreAutoControl();
                _logging?.Info("EC: Restored BIOS auto fan control");
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to restore auto control: {ex.Message}", ex);
                return false;
            }
        }

        public IEnumerable<FanTelemetry> ReadFanSpeeds() => _controller.ReadFanSpeeds();

        public void ApplyMaxCooling()
        {
            _logging?.Info("Applying Max cooling via EC (with fan boost)...");
            var ok = SetMaxFanSpeed(true);
            if (!ok)
            {
                _logging?.Warn("ApplyMaxCooling: Verification failed - Max may not be applied");
            }
        }
        
        public void ApplyAutoMode()
        {
            // Actually restore BIOS auto control instead of setting fixed 50%
            _logging?.Info("Applying Auto mode via EC (restoring BIOS control)...");
            RestoreAutoControl();
        }
        public void ApplyQuietMode() => SetFanSpeed(30);
        
        public bool ResetEcToDefaults()
        {
            _logging?.Info("Resetting EC to defaults via EC access...");
            return _controller.ResetEcToDefaults();
        }
        
        /// <summary>
        /// Apply throttling mitigation via EC register 0x95.
        /// Writes 0x31 (performance mode) to mitigate thermal throttling.
        /// </summary>
        public bool ApplyThrottlingMitigation()
        {
            const ushort EC_PERFORMANCE_REGISTER = 0x95;
            const byte EC_PERFORMANCE_MODE = 0x31;
            
            _logging?.Info("Applying throttling mitigation via EC register 0x95...");
            
            try
            {
                if (!_controller.IsEcReady)
                {
                    _logging?.Warn("EC not ready - cannot apply throttling mitigation");
                    return false;
                }
                
                // Write performance mode to register 0x95
                _controller.WriteEc(EC_PERFORMANCE_REGISTER, EC_PERFORMANCE_MODE);
                _logging?.Info($"✓ Throttling mitigation applied: EC[0x{EC_PERFORMANCE_REGISTER:X2}] = 0x{EC_PERFORMANCE_MODE:X2}");
                return true;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Throttling mitigation failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            // FanController doesn't implement IDisposable, nothing to dispose
        }
    }

    /// <summary>
    /// Fallback fan controller for systems without HP WMI or EC access.
    /// Provides monitoring-only functionality.
    /// </summary>
    public class FallbackFanController : IFanController
    {
        public bool VerifyMaxApplied(out string details)
        {
            details = "No fan control backend available (monitoring only) - cannot verify Max";
            return false;
        }
        
        private readonly LibreHardwareMonitorImpl? _hwMonitor;
        private readonly LoggingService? _logging;

        public FallbackFanController(LibreHardwareMonitorImpl? hwMonitor, LoggingService? logging = null)
        {
            _hwMonitor = hwMonitor;
            _logging = logging;
        }

        public bool IsAvailable => false;
        public string Status => "No fan control backend available (monitoring only)";
        public string Backend => "None (monitoring only)";

        public bool ApplyPreset(FanPreset preset)
        {
            _logging?.Warn("Fan control not available: Cannot apply preset");
            return false;
        }

        public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve)
        {
            _logging?.Warn("Fan control not available: Cannot apply curve");
            return false;
        }

        public bool SetFanSpeed(int percent)
        {
            _logging?.Warn("Fan control not available: Cannot set fan speed");
            return false;
        }

        public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
        {
            _logging?.Warn("Fan control not available: Cannot set fan speeds");
            return false;
        }

        public bool SetMaxFanSpeed(bool enabled)
        {
            _logging?.Warn("Fan control not available: Cannot set max fan speed");
            return false;
        }

        public bool SetPerformanceMode(string modeName)
        {
            _logging?.Warn("Fan control not available: Cannot set performance mode");
            return false;
        }

        public bool RestoreAutoControl()
        {
            _logging?.Info("Fan control not available: Auto control not applicable");
            return true; // Return true since there's nothing to restore
        }

        public IEnumerable<FanTelemetry> ReadFanSpeeds()
        {
            var fans = new List<FanTelemetry>();

            // Get fan speeds from hardware monitor (with WMI BIOS fallback)
            var fanSpeeds = SensorHelper.GetFanSpeeds(_hwMonitor);
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                fans.Add(new FanTelemetry
                {
                    Name = name,
                    SpeedRpm = (int)rpm,
                    DutyCyclePercent = EstimateDutyFromRpm((int)rpm),
                    Temperature = index == 0 ? SensorHelper.GetCpuTemperature(_hwMonitor) : SensorHelper.GetGpuTemperature(_hwMonitor)
                });
                index++;
            }

            // Fallback if no fans detected
            if (fans.Count == 0)
            {
                fans.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = SensorHelper.GetCpuTemperature(_hwMonitor) });
                fans.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = SensorHelper.GetGpuTemperature(_hwMonitor) });
            }

            return fans;
        }

        private int EstimateDutyFromRpm(int rpm)
        {
            if (rpm == 0) return 0;
            const int minRpm = 1500;
            const int maxRpm = 6000;
            return Math.Clamp((rpm - minRpm) * 100 / (maxRpm - minRpm), 0, 100);
        }

        public void ApplyMaxCooling() => _logging?.Warn("Fan control not available: Cannot apply max cooling");
        public void ApplyAutoMode() => _logging?.Warn("Fan control not available: Cannot apply auto mode");
        public void ApplyQuietMode() => _logging?.Warn("Fan control not available: Cannot apply quiet mode");
        
        public bool ResetEcToDefaults()
        {
            _logging?.Warn("EC reset not available: No fan control backend");
            return false;
        }
        
        public bool ApplyThrottlingMitigation()
        {
            _logging?.Warn("Throttling mitigation not available: No fan control backend");
            return false;
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
    
    /// <summary>
    /// Static helper for getting sensor data from the best available source.
    /// Used by fan controller wrappers to get data without requiring LibreHardwareMonitor.
    /// </summary>
    internal static class SensorHelper
    {
        private static HpWmiBios? _wmiBios;
        
        private static HpWmiBios WmiBios => _wmiBios ??= new HpWmiBios(null);
        
        /// <summary>
        /// Get fan speeds from LibreHardwareMonitor or WMI BIOS fallback.
        /// </summary>
        public static IEnumerable<(string Name, double Rpm)> GetFanSpeeds(LibreHardwareMonitorImpl? libreHw)
        {
            // Try LibreHardwareMonitor first
            if (libreHw != null)
            {
                try
                {
                    var speeds = libreHw.GetFanSpeeds();
                    if (speeds.Any())
                        return speeds;
                }
                catch { }
            }
            
            // Fall back to WMI BIOS
            var rpms = WmiBios.GetFanRpmDirect();
            var result = new List<(string, double)>();
            
            if (rpms.HasValue)
            {
                var (cpuRpm, gpuRpm) = rpms.Value;
                if (HpWmiBios.IsValidRpm(cpuRpm))
                    result.Add(("CPU Fan", cpuRpm));
                
                if (HpWmiBios.IsValidRpm(gpuRpm))
                    result.Add(("GPU Fan", gpuRpm));
            }
            
            return result;
        }
        
        /// <summary>
        /// Get CPU temperature from LibreHardwareMonitor or WMI BIOS fallback.
        /// </summary>
        public static double GetCpuTemperature(LibreHardwareMonitorImpl? libreHw)
        {
            if (libreHw != null)
            {
                try
                {
                    var temp = libreHw.GetCpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            
            return WmiBios.GetTemperature() ?? 0;
        }
        
        /// <summary>
        /// Get GPU temperature from LibreHardwareMonitor or WMI BIOS fallback.
        /// </summary>
        public static double GetGpuTemperature(LibreHardwareMonitorImpl? libreHw)
        {
            if (libreHw != null)
            {
                try
                {
                    var temp = libreHw.GetGpuTemperature();
                    if (temp > 0) return temp;
                }
                catch { }
            }
            
            return WmiBios.GetGpuTemperature() ?? 0;
        }
    }
}
