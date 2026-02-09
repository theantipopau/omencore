using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// WMI BIOS-based hardware monitor that requires NO external dependencies.
    /// Uses HP's built-in WMI BIOS interface for temperature and fan monitoring.
    /// This is the self-sufficient fallback when LibreHardwareMonitor is unavailable.
    /// 
    /// Capabilities:
    /// - CPU Temperature (via WMI BIOS command 0x23)
    /// - GPU Temperature (via WMI BIOS command 0x23 or fallback)
    /// - Fan RPM (via WMI BIOS command 0x2D/0x38)
    /// - No kernel driver required
    /// - Works without admin rights for monitoring
    /// </summary>
    public class WmiBiosMonitor : IHardwareMonitorBridge, IDisposable
    {
        private readonly LoggingService? _logging;
        private readonly HpWmiBios _wmiBios;
        private bool _disposed;
        
        // Cached values for performance
        private double _cachedCpuTemp;
        private double _cachedGpuTemp;
        private int _cachedCpuFanRpm;
        private int _cachedGpuFanRpm;
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(500);
        
        // CPU/GPU load - not available via WMI BIOS, use performance counters
        private double _cachedCpuLoad;
        private double _cachedGpuLoad = 0; // GPU load not available via WMI BIOS
        
        // Dead battery detection — stop polling Win32_Battery if battery appears dead/absent
        private bool _batteryMonitoringDisabled;
        private int _consecutiveZeroBatteryReads;
        private const int MaxZeroBatteryReadsBeforeDisable = 3;
        private DateTime _lastBatteryQuery = DateTime.MinValue;
        private readonly TimeSpan _batteryQueryCooldown = TimeSpan.FromSeconds(10);
        
        public bool IsAvailable => _wmiBios.IsAvailable;
        
        public WmiBiosMonitor(LoggingService? logging = null)
        {
            _logging = logging;
            _wmiBios = new HpWmiBios(logging);
            
            if (_wmiBios.IsAvailable)
            {
                _logging?.Info("[WmiBiosMonitor] Initialized successfully - using HP WMI BIOS for monitoring");
            }
            else
            {
                _logging?.Warn("[WmiBiosMonitor] WMI BIOS not available - monitoring will be limited");
            }
        }
        
        public async Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
        {
            // Check cache
            if (DateTime.Now - _lastUpdate < _cacheLifetime)
            {
                return BuildSampleFromCache();
            }
            
            await Task.Run(() => UpdateReadings(), token);
            _lastUpdate = DateTime.Now;
            
            return BuildSampleFromCache();
        }
        
        /// <summary>
        /// WMI BIOS monitor doesn't need restart - it has no persistent state.
        /// Always returns true as there's nothing to restart.
        /// </summary>
        public Task<bool> TryRestartAsync()
        {
            _logging?.Info("[WmiBiosMonitor] TryRestartAsync called - WMI BIOS monitor requires no restart");
            // WMI BIOS has no persistent state to restart - always succeeds
            return Task.FromResult(true);
        }
        
        private void UpdateReadings()
        {
            if (_disposed || !_wmiBios.IsAvailable) return;
            
            try
            {
                // Get temperatures from WMI BIOS
                var temps = _wmiBios.GetBothTemperatures();
                
                if (temps.HasValue)
                {
                    var (cpuTemp, gpuTemp) = temps.Value;
                    if (cpuTemp > 0)
                    {
                        _cachedCpuTemp = cpuTemp;
                    }
                    
                    if (gpuTemp > 0)
                    {
                        _cachedGpuTemp = gpuTemp;
                    }
                }
                
                // Get fan RPMs from WMI BIOS
                var rpms = _wmiBios.GetFanRpmDirect();
                
                if (rpms.HasValue)
                {
                    var (cpuRpm, gpuRpm) = rpms.Value;
                    if (HpWmiBios.IsValidRpm(cpuRpm))
                    {
                        _cachedCpuFanRpm = cpuRpm;
                    }
                    
                    if (HpWmiBios.IsValidRpm(gpuRpm))
                    {
                        _cachedGpuFanRpm = gpuRpm;
                    }
                }
                
                // CPU load via performance counter (lightweight)
                try
                {
                    using var cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                    cpuCounter.NextValue(); // First call always returns 0
                    Thread.Sleep(100);
                    _cachedCpuLoad = cpuCounter.NextValue();
                }
                catch
                {
                    // Performance counters may not be available
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] Update failed: {ex.Message}");
            }
        }
        
        private MonitoringSample BuildSampleFromCache()
        {
            return new MonitoringSample
            {
                Timestamp = DateTime.Now,
                CpuTemperatureC = _cachedCpuTemp,
                GpuTemperatureC = _cachedGpuTemp,
                CpuLoadPercent = _cachedCpuLoad,
                GpuLoadPercent = _cachedGpuLoad,
                FanRpm = _cachedCpuFanRpm,
                Fan1Rpm = _cachedCpuFanRpm,
                Fan2Rpm = _cachedGpuFanRpm,
                GpuFanPercent = EstimateFanPercent(_cachedGpuFanRpm),
                // RAM usage via WMI
                RamUsageGb = GetUsedMemoryGB(),
                RamTotalGb = GetTotalPhysicalMemoryGB(),
                BatteryChargePercent = GetBatteryCharge(),
                IsOnAcPower = IsOnAcPower(),
            };
        }
        
        private static int EstimateFanPercent(int rpm)
        {
            // Estimate percentage based on typical laptop fan range (0-5500 RPM)
            if (rpm <= 0) return 0;
            if (rpm >= 5500) return 100;
            return (int)(rpm / 55.0);
        }
        
        private static double GetTotalPhysicalMemoryGB()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    if (obj["TotalPhysicalMemory"] is ulong bytes)
                    {
                        return bytes / (1024.0 * 1024 * 1024);
                    }
                }
            }
            catch { }
            return 16; // Default assumption
        }
        
        private static double GetUsedMemoryGB()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    if (obj["FreePhysicalMemory"] is ulong freeKb)
                    {
                        double totalGb = GetTotalPhysicalMemoryGB();
                        double freeGb = freeKb / (1024.0 * 1024);
                        return totalGb - freeGb;
                    }
                }
            }
            catch { }
            return 8; // Default assumption
        }
        
        private double GetBatteryCharge()
        {
            // If battery monitoring is disabled (dead/removed battery), skip WMI query entirely
            if (_batteryMonitoringDisabled) return 100;
            
            // Cooldown: don't query Win32_Battery more than once every 10 seconds
            if (DateTime.Now - _lastBatteryQuery < _batteryQueryCooldown) return 100;
            _lastBatteryQuery = DateTime.Now;
            
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT EstimatedChargeRemaining FROM Win32_Battery");
                var results = searcher.Get();
                bool foundBattery = false;
                
                foreach (var obj in results)
                {
                    foundBattery = true;
                    if (obj["EstimatedChargeRemaining"] is ushort charge)
                    {
                        if (charge == 0 && IsOnAcPower())
                        {
                            _consecutiveZeroBatteryReads++;
                            if (_consecutiveZeroBatteryReads >= MaxZeroBatteryReadsBeforeDisable)
                            {
                                _batteryMonitoringDisabled = true;
                                _logging?.Warn("[WmiBiosMonitor] Dead battery detected (0% on AC for 3+ reads) — disabling battery WMI queries to prevent EC timeouts");
                                return 100;
                            }
                        }
                        else
                        {
                            _consecutiveZeroBatteryReads = 0;
                        }
                        return charge;
                    }
                }
                
                if (!foundBattery)
                {
                    // No battery found in WMI — likely removed or not present
                    _batteryMonitoringDisabled = true;
                    _logging?.Info("[WmiBiosMonitor] No battery detected in Win32_Battery — disabling battery queries");
                    return 100;
                }
            }
            catch (Exception ex)
            {
                // WMI query failed — could be EC timeout on dead battery
                _consecutiveZeroBatteryReads++;
                if (_consecutiveZeroBatteryReads >= MaxZeroBatteryReadsBeforeDisable)
                {
                    _batteryMonitoringDisabled = true;
                    _logging?.Warn($"[WmiBiosMonitor] Battery WMI queries failing repeatedly ({ex.Message}) — disabling to prevent EC timeouts");
                }
                return 100;
            }
            return 100;
        }
        
        private bool IsOnAcPower()
        {
            // If battery monitoring disabled, assume AC (dead battery = always plugged in)
            if (_batteryMonitoringDisabled) return true;
            
            try
            {
                // Use SystemInformation first — doesn't go through EC
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                return powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            }
            catch { }
            
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT BatteryStatus FROM Win32_Battery");
                foreach (var obj in searcher.Get())
                {
                    // BatteryStatus: 1=Discharging, 2=AC Power, 3-9 various charging states
                    if (obj["BatteryStatus"] is ushort status)
                    {
                        return status >= 2; // 2+ means on AC power or charging
                    }
                }
            }
            catch { }
            return true; // Assume AC if we can't determine
        }
        
        /// <summary>
        /// Externally disable battery monitoring (e.g., from config setting).
        /// </summary>
        public void DisableBatteryMonitoring()
        {
            _batteryMonitoringDisabled = true;
            _logging?.Info("[WmiBiosMonitor] Battery monitoring disabled by config");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _wmiBios.Dispose();
        }
    }
}
