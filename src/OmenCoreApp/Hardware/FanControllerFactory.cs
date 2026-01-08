using System;
using System.Collections.Generic;
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
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly IEcAccess? _ecAccess;
        private readonly IReadOnlyDictionary<string, int>? _registerMap;
        private readonly DeviceCapabilities? _capabilities;
        private OghServiceProxy? _oghProxy;
        
        /// <summary>
        /// The backend currently in use (for UI display).
        /// </summary>
        public string ActiveBackend { get; private set; } = "None";

        public FanControllerFactory(
            LibreHardwareMonitorImpl hwMonitor,
            IEcAccess? ecAccess = null,
            IReadOnlyDictionary<string, int>? registerMap = null,
            LoggingService? logging = null,
            DeviceCapabilities? capabilities = null)
        {
            _hwMonitor = hwMonitor;
            _ecAccess = ecAccess;
            _registerMap = registerMap;
            _logging = logging;
            _capabilities = capabilities;
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
                return wmiController;
            }

            // Priority 2: EC Direct via PawnIO (Secure Boot compatible, OGH-independent)
            var ecController = TryCreateEcController();
            if (ecController != null)
            {
                ActiveBackend = "EC Direct";
                _logging?.Info("✓ Using EC-based fan controller (OGH-independent, requires PawnIO/WinRing0)");
                return ecController;
            }

            // Priority 3: OGH proxy as last resort (requires OGH services)
            // Only used if WMI BIOS and EC both fail
            var oghController = TryCreateOghController();
            if (oghController != null)
            {
                ActiveBackend = "OGH Proxy";
                _logging?.Warn("⚠️ Using OGH proxy (WMI BIOS unavailable on this model)");
                _logging?.Info("  Consider reporting your model for native support");
                return oghController;
            }

            // Last resort: fallback controller with monitoring only
            ActiveBackend = "Monitoring Only";
            _logging?.Warn("⚠️ No fan control backend available - using monitoring-only mode");
            return new FallbackFanController(_hwMonitor, _logging);
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
                    return new OghFanControllerWrapper(_oghProxy, _hwMonitor, _logging);
                }
                
                // Try starting OGH services if installed but not running
                if (_oghProxy.Status.IsInstalled && !_oghProxy.Status.IsRunning)
                {
                    _logging?.Info("OGH installed but not running, attempting to start services...");
                    if (_oghProxy.TryStartOghServices() && _oghProxy.IsAvailable)
                    {
                        return new OghFanControllerWrapper(_oghProxy, _hwMonitor, _logging);
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
                var controller = new WmiFanController(_hwMonitor, _logging);
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
                if (_ecAccess != null && _ecAccess.IsAvailable && _registerMap != null)
                {
                    var controller = new FanController(_ecAccess, _registerMap, _hwMonitor);
                    if (controller.IsEcReady)
                    {
                        return new EcFanControllerWrapper(controller, _hwMonitor, _logging);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"EC controller initialization failed: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Wrapper for OGH Service Proxy that implements IFanController.
    /// Uses OMEN Gaming Hub's WMI interface for fan control on 2023+ models.
    /// </summary>
    public class OghFanControllerWrapper : IFanController
    {
        private readonly OghServiceProxy _proxy;
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly LoggingService? _logging;
        private bool _disposed;

        public OghFanControllerWrapper(OghServiceProxy proxy, LibreHardwareMonitorImpl hwMonitor, LoggingService? logging = null)
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
            
            // Try to get fan data from OGH
            var oghFans = _proxy.GetFanData();
            if (oghFans != null && oghFans.Length > 0)
            {
                foreach (var fan in oghFans)
                {
                    fans.Add(fan);
                }
            }
            else
            {
                // Fall back to hardware monitor
                var fanSpeeds = _hwMonitor.GetFanSpeeds();
                int index = 0;
                foreach (var (name, rpm) in fanSpeeds)
                {
                    fans.Add(new FanTelemetry
                    {
                        Name = name,
                        SpeedRpm = (int)rpm,
                        DutyCyclePercent = EstimateDutyFromRpm((int)rpm),
                        Temperature = index == 0 ? _hwMonitor.GetCpuTemperature() : _hwMonitor.GetGpuTemperature()
                    });
                    index++;
                }
            }
            
            // Ensure we have at least placeholder entries
            if (fans.Count == 0)
            {
                fans.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _hwMonitor.GetCpuTemperature() });
                fans.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _hwMonitor.GetGpuTemperature() });
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

        public void Dispose() => _controller.Dispose();
    }

    /// <summary>
    /// Wrapper for legacy FanController (EC-based) that implements IFanController.
    /// </summary>
    public class EcFanControllerWrapper : IFanController
    {
        private readonly FanController _controller;
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly LoggingService? _logging;

        public EcFanControllerWrapper(FanController controller, LibreHardwareMonitorImpl hwMonitor, LoggingService? logging = null)
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

        public bool SetMaxFanSpeed(bool enabled)
        {
            // EC controller: set max speed via curve
            return SetFanSpeed(enabled ? 100 : 50);
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
            // EC controller: set moderate speed as "auto"
            return SetFanSpeed(50);
        }

        public IEnumerable<FanTelemetry> ReadFanSpeeds() => _controller.ReadFanSpeeds();

        public void ApplyMaxCooling() => SetFanSpeed(100);
        public void ApplyAutoMode() => SetFanSpeed(50);
        public void ApplyQuietMode() => SetFanSpeed(30);
        
        public bool ResetEcToDefaults()
        {
            _logging?.Info("Resetting EC to defaults via EC access...");
            return _controller.ResetEcToDefaults();
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
        private readonly LibreHardwareMonitorImpl _hwMonitor;
        private readonly LoggingService? _logging;

        public FallbackFanController(LibreHardwareMonitorImpl hwMonitor, LoggingService? logging = null)
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

            // Get fan speeds from hardware monitor
            var fanSpeeds = _hwMonitor.GetFanSpeeds();
            int index = 0;

            foreach (var (name, rpm) in fanSpeeds)
            {
                fans.Add(new FanTelemetry
                {
                    Name = name,
                    SpeedRpm = (int)rpm,
                    DutyCyclePercent = EstimateDutyFromRpm((int)rpm),
                    Temperature = index == 0 ? _hwMonitor.GetCpuTemperature() : _hwMonitor.GetGpuTemperature()
                });
                index++;
            }

            // Fallback if no fans detected
            if (fans.Count == 0)
            {
                fans.Add(new FanTelemetry { Name = "CPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _hwMonitor.GetCpuTemperature() });
                fans.Add(new FanTelemetry { Name = "GPU Fan", SpeedRpm = 0, DutyCyclePercent = 0, Temperature = _hwMonitor.GetGpuTemperature() });
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

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
