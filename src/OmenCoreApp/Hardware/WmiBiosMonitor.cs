using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Self-sustaining WMI BIOS + NVAPI hardware monitor — NO LHM/WinRing0 dependency.
    /// 
    /// This is OmenCore's PRIMARY monitoring bridge. It reads all sensor data using
    /// native Windows APIs and NVIDIA's NVAPI — no external kernel drivers required.
    /// 
    /// Data Sources:
    /// - CPU/GPU Temperature: HP WMI BIOS (command 0x23) — same as OmenMon
    /// - Fan RPM: HP WMI BIOS (command 0x38) — hardware-accurate
    /// - CPU Load: Windows PerformanceCounter
    /// - GPU Load/Temp/Clocks/VRAM/Power: NVAPI (via NvAPIWrapper)
    /// - CPU Throttling: PawnIO MSR 0x19C (if available)
    /// - RAM: WMI Win32_OperatingSystem / Win32_ComputerSystem
    /// - Battery: WMI Win32_Battery + SystemInformation.PowerStatus
    /// - SSD Temperature: WMI MSStorageDriver (if available)
    /// 
    /// PawnIO is used ONLY for MSR-based throttling detection — NOT for core monitoring.
    /// </summary>
    public class WmiBiosMonitor : IHardwareMonitorBridge, IDisposable
    {
        private readonly LoggingService? _logging;
        private readonly HpWmiBios _wmiBios;
        private readonly NvapiService? _nvapi;
        private readonly PawnIOMsrAccess? _msrAccess;
        private readonly string _systemModel;
        private readonly bool _preferWorkerCpuTempForModel;
        private bool _disposed;
        
        // Cached values for performance
        private double _cachedCpuTemp;
        private double _cachedGpuTemp;
        private int _cachedCpuFanRpm;
        private int _cachedGpuFanRpm;
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromMilliseconds(500);
        private readonly SemaphoreSlim _updateGate = new(1, 1);
        
        // CPU/GPU load
        private double _cachedCpuLoad;
        private double _cachedGpuLoad;
        
        // CPU clock from WMI
        private double _cachedCpuClockMhz;
        
        // ACPI thermal zone for higher-precision CPU temp
        private bool _acpiThermalAvailable = true; // Try ACPI first, disable on failure
        private string? _cpuThermalZoneInstance;
        
        // CPU temperature freeze detection (AMD WMI BIOS sensor sometimes stops updating)
        private double _lastCpuTempReading;
        private int _consecutiveIdenticalCpuTempReads;
        private double _lastValidCpuTempBeforeFreeze;
        private const int MaxConsecutiveIdenticalTempReads = 12; // Faster freeze recovery in steady-state polling
        private bool _cpuTempFrozen;
        private DateTime _cpuTempFrozeAt = DateTime.MinValue;
        private bool _cpuTempFallbackLogged;
        private bool _modelCpuTempPreferenceLogged;
        private LibreHardwareMonitorImpl? _tempFallbackMonitor;
        private bool _tempFallbackInitAttempted;
        private bool _lhmFallbackDisabledLogged;
        private const double ImplausiblyLowCpuTempThresholdC = 33.0;
        private const double CpuLoadThresholdForLowTempFallbackPercent = 20.0;
        private const double MaxAcpiDeltaFromWmiC = 18.0;
        
        // GPU temperature freeze detection (similar to CPU temp)
        private double _lastGpuTempReading;
        private int _consecutiveIdenticalGpuTempReads;
        private double _lastValidGpuTempBeforeFreeze;
        private bool _gpuTempFrozen;
        private DateTime _gpuTempFrozeAt = DateTime.MinValue;
        private int _consecutiveGpuInactiveReads;
        private bool _gpuInactive;
        
        // GPU metrics from NVAPI
        private double _cachedGpuPowerWatts;
        private double _lastValidGpuPowerWatts;
        private int _consecutiveZeroGpuPowerReads;
        private double _cachedGpuClockMhz;
        private double _cachedGpuMemClockMhz;
        private double _cachedGpuVramUsedMb;
        private double _cachedGpuVramTotalMb;
        private string _cachedGpuName = string.Empty;
        
        // CPU throttling & power from PawnIO MSR
        private bool _cachedCpuThermalThrottling;
        private bool _cachedCpuPowerThrottling;
        private double _cachedCpuPowerWatts;
        private double _lastValidCpuPowerWatts;
        private int _consecutiveZeroCpuPowerReads;

        // Power telemetry smoothing for transient sensor dropouts
        private const int MaxTransientZeroPowerReads = 30;
        private const double ActiveLoadThresholdPercent = 2.0;
        private const double ActiveTempThresholdC = 38.0;
        private bool _powerFallbackLogged;
        
        // SSD temperature
        private double _cachedSsdTemp;
        private bool _ssdTempAvailable = true; // Optimistic, disable on first failure
        
        // Battery
        private double _cachedBatteryDischargeRate;
        private bool _batteryMonitoringDisabled;
        private int _consecutiveZeroBatteryReads;
        private const int MaxZeroBatteryReadsBeforeDisable = 3;
        private DateTime _lastBatteryQuery = DateTime.MinValue;
        private readonly TimeSpan _batteryQueryCooldown = TimeSpan.FromSeconds(10);
        
        // NVAPI failure tracking — disable after repeated failures, then auto-recover after cooldown
        private int _nvapiConsecutiveFailures;
        private bool _nvapiMonitoringDisabled;
        private DateTime _nvapiDisabledUntil = DateTime.MinValue;
        private const int MaxNvapiFailuresBeforeDisable = 10;
        private const int NvapiRecoveryCooldownSeconds = 60;
        
        // MSI Afterburner coexistence — read GPU metrics from shared memory instead of NVAPI polling
        private ConflictDetectionService? _afterburnerService;
        private bool _afterburnerCoexistenceActive;

        // CPU PerformanceCounter — persistent instance avoids 100ms sleep + allocation every poll
        private System.Diagnostics.PerformanceCounter? _cpuPerfCounter;
        private bool _cpuPerfCounterAvailable = true;
        private static int _workerPrelaunchAttempted;
        
        public bool IsAvailable => _wmiBios.IsAvailable;

        /// <summary>
        /// True when model-specific CPU temperature source override is active.
        /// </summary>
        public bool IsModelCpuTempOverrideActive => _preferWorkerCpuTempForModel;

        /// <summary>
        /// Human-readable model name used for CPU temperature source override decisions.
        /// </summary>
        public string ModelName => _systemModel;

        public string MonitoringSource => _nvapi?.IsAvailable == true 
            ? "WMI BIOS + NVAPI (Self-Sustaining)" 
            : "WMI BIOS (Self-Sustaining)";
        
        /// <summary>
        /// Creates a self-sustaining hardware monitor.
        /// </summary>
        /// <param name="logging">Logging service</param>
        /// <param name="nvapi">Optional NVAPI service for GPU metrics (load, clocks, VRAM, power)</param>
        /// <param name="msrAccess">Optional PawnIO MSR access for CPU throttling detection</param>
        public WmiBiosMonitor(LoggingService? logging = null, NvapiService? nvapi = null, PawnIOMsrAccess? msrAccess = null)
        {
            _logging = logging;
            _wmiBios = new HpWmiBios(logging);
            _nvapi = nvapi;
            _msrAccess = msrAccess;
            _systemModel = GetSystemModel();
            _preferWorkerCpuTempForModel = ShouldPreferWorkerCpuTemp(_systemModel);
            
            if (_wmiBios.IsAvailable)
            {
                _logging?.Info("[WmiBiosMonitor] ✓ HP WMI BIOS available — primary temp/fan source");
            }
            else
            {
                _logging?.Warn("[WmiBiosMonitor] ✗ WMI BIOS not available — monitoring will be limited");
            }
            
            if (_nvapi?.IsAvailable == true)
            {
                _cachedGpuName = _nvapi.GpuName;
                _logging?.Info($"[WmiBiosMonitor] ✓ NVAPI available — GPU metrics: {_cachedGpuName}");
            }
            else
            {
                _logging?.Info("[WmiBiosMonitor] NVAPI not available — GPU load/clocks/VRAM will be unavailable");
            }
            
            if (_msrAccess != null)
            {
                _logging?.Info("[WmiBiosMonitor] ✓ PawnIO MSR available — CPU throttling detection enabled");
            }

            if (_preferWorkerCpuTempForModel)
            {
                _logging?.Warn($"[WmiBiosMonitor] Model '{_systemModel}' detected — prioritizing worker-backed CPU temperature source for accuracy");
            }

            TryPrelaunchHardwareWorker();

            // Prewarm the fallback monitor so the crash-isolated hardware worker is available
            // shortly after app startup rather than only on first fallback event.
            _ = EnsureTempFallbackMonitor();

            // Initialise the CPU PerformanceCounter on a background thread — the constructor +
            // first NextValue() call can block the calling thread for 8-10 seconds on some machines.
            // The read path already guards with (_cpuPerfCounterAvailable && _cpuPerfCounter != null)
            // so missing the first few poll cycles is harmless.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var pc = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                    pc.NextValue(); // baseline — must be called before first real read
                    _cpuPerfCounter = pc; // assign after warm-up so read path never sees a cold counter
                    _logging?.Info("[WmiBiosMonitor] ✓ CPU PerformanceCounter initialised (persistent, background)");
                }
                catch (Exception ex)
                {
                    _cpuPerfCounterAvailable = false;
                    _logging?.Warn($"[WmiBiosMonitor] CPU PerformanceCounter not available: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Enable Afterburner coexistence mode. When Afterburner shared memory is available,
        /// GPU temp/clocks/power are read from it instead of NVAPI, eliminating polling contention.
        /// NVAPI is reduced to load+VRAM only (lightweight calls with minimal contention).
        /// </summary>
        public void SetAfterburnerCoexistence(ConflictDetectionService conflictService)
        {
            _afterburnerService = conflictService;
            _logging?.Info("[WmiBiosMonitor] Afterburner coexistence configured — will auto-activate when shared memory is available");
        }
        
        public async Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
        {
            // Check cache
            if (DateTime.Now - _lastUpdate < _cacheLifetime)
            {
                return BuildSampleFromCache();
            }

            if (_disposed) return BuildSampleFromCache();
            await _updateGate.WaitAsync(token);
            try
            {
                // Re-check cache after waiting for in-flight update
                if (DateTime.Now - _lastUpdate < _cacheLifetime)
                {
                    return BuildSampleFromCache();
                }

                await Task.Run(() => UpdateReadings(), token);
                _lastUpdate = DateTime.Now;
            }
            finally
            {
                // Guard against ObjectDisposedException if WmiBiosMonitor is disposed
                // while a monitoring iteration is in flight (shutdown race condition).
                try { _updateGate.Release(); }
                catch (ObjectDisposedException) { }
            }
            
            return BuildSampleFromCache();
        }
        
        /// <summary>
        /// Reset accumulated failure state so the next poll cycle retries all sources.
        /// Called by HardwareMonitoringService after consecutive timeout errors.
        /// </summary>
        public Task<bool> TryRestartAsync()
        {
            // Reset NVAPI suspended state so the next UpdateReadings() immediately retries
            // GPU telemetry rather than waiting for the cooldown timer to expire.
            if (_nvapiMonitoringDisabled)
            {
                _nvapiMonitoringDisabled = false;
                _nvapiConsecutiveFailures = 0;
                _nvapiDisabledUntil = DateTime.MinValue;
                _logging?.Info("[WmiBiosMonitor] TryRestartAsync: NVAPI failure state cleared — GPU monitoring will retry on next poll");
            }
            else
            {
                _logging?.Info("[WmiBiosMonitor] TryRestartAsync: no suspended sources to reset");
            }
            return Task.FromResult(true);
        }
        
        private void UpdateReadings()
        {
            if (_disposed) return;
            
            try
            {
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 1: HP WMI BIOS — CPU/GPU temp + Fan RPM
                // This is the same source OmenMon uses. Rock-solid, no dependencies.
                // Only attempt when WMI BIOS is functional.
                // ═══════════════════════════════════════════════════════════════
                
                if (_wmiBios.IsAvailable)
                {
                    var temps = _wmiBios.GetBothTemperatures();
                    if (temps.HasValue)
                    {
                        var (cpuTemp, gpuTemp) = temps.Value;
                        if (cpuTemp > 0)
                        {
                            // Detect CPU temperature freeze (AMD WMI sensor sometimes stops updating)
                            if (Math.Abs(cpuTemp - _lastCpuTempReading) < 0.1) // Same temp (within 0.1°C)
                            {
                                _consecutiveIdenticalCpuTempReads++;
                                if (_consecutiveIdenticalCpuTempReads > MaxConsecutiveIdenticalTempReads && !_cpuTempFrozen)
                                {
                                    _cpuTempFrozen = true;
                                    _cpuTempFrozeAt = DateTime.UtcNow;
                                    _logging?.Warn($"🥶 CPU temperature appears frozen at {cpuTemp:F1}°C for {_consecutiveIdenticalCpuTempReads} readings (load={_cachedCpuLoad:F0}%, power={_cachedCpuPowerWatts:F1}W)");
                                }
                            }
                            else
                            {
                                // Temperature changed — sensor is responding
                                _consecutiveIdenticalCpuTempReads = 0;
                                if (_cpuTempFrozen)
                                {
                                    TimeSpan frozenDuration = DateTime.UtcNow - _cpuTempFrozeAt;
                                    _logging?.Info($"✓ CPU temperature sensor recovered after {frozenDuration.TotalSeconds:F0}s freeze");
                                    _cpuTempFrozen = false;
                                }
                                _lastValidCpuTempBeforeFreeze = cpuTemp;
                            }

                            _lastCpuTempReading = cpuTemp;
                            _cachedCpuTemp = cpuTemp;
                        }
                        if (gpuTemp > 0)
                        {
                            // Detect GPU temperature freeze (similar to CPU)
                            if (Math.Abs(gpuTemp - _lastGpuTempReading) < 0.1)
                            {
                                _consecutiveIdenticalGpuTempReads++;
                                if (_consecutiveIdenticalGpuTempReads > MaxConsecutiveIdenticalTempReads && !_gpuTempFrozen)
                                {
                                    _gpuTempFrozen = true;
                                    _gpuTempFrozeAt = DateTime.UtcNow;
                                    _logging?.Warn($"🥶 GPU temperature appears frozen at {gpuTemp:F1}°C for {_consecutiveIdenticalGpuTempReads} readings");
                                }
                            }
                            else
                            {
                                _consecutiveIdenticalGpuTempReads = 0;
                                if (_gpuTempFrozen)
                                {
                                    TimeSpan frozenDuration = DateTime.UtcNow - _gpuTempFrozeAt;
                                    _logging?.Info($"✓ GPU temperature sensor recovered after {frozenDuration.TotalSeconds:F0}s freeze");
                                    _gpuTempFrozen = false;
                                }
                                _lastValidGpuTempBeforeFreeze = gpuTemp;
                            }

                            _lastGpuTempReading = gpuTemp;
                            _cachedGpuTemp = gpuTemp;
                        }
                    }

                    var rpms = _wmiBios.GetFanRpmDirect();
                    if (rpms.HasValue)
                    {
                        var (cpuRpm, gpuRpm) = rpms.Value;
                        _cachedCpuFanRpm = HpWmiBios.IsValidRpm(cpuRpm) ? cpuRpm : 0;
                        _cachedGpuFanRpm = HpWmiBios.IsValidRpm(gpuRpm) ? gpuRpm : 0;
                    }
                    else
                    {
                        // V1 fallback: GetFanRpmDirect (0x38) not available, use GetFanLevel (0x2D)
                        // Fan levels are in krpm units (e.g., level 44 = 4400 RPM)
                        var levels = _wmiBios.GetFanLevel();
                        if (levels.HasValue)
                        {
                            var (fan1Level, fan2Level) = levels.Value;
                            int fan1Rpm = fan1Level * 100;
                            int fan2Rpm = fan2Level * 100;
                            _cachedCpuFanRpm = HpWmiBios.IsValidRpm(fan1Rpm) ? fan1Rpm : 0;
                            _cachedGpuFanRpm = HpWmiBios.IsValidRpm(fan2Rpm) ? fan2Rpm : 0;
                        }
                    }
                }
                
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 2: GPU metrics — Afterburner shared memory OR NVAPI
                // When Afterburner is running, read temp/clocks/power from its
                // shared memory (zero contention). NVAPI reduced to load+VRAM only.
                // ═══════════════════════════════════════════════════════════════
                
                bool afterburnerProvidedData = false;
                
                // Try Afterburner shared memory first (eliminates NVAPI contention)
                if (_afterburnerService?.IsMsiAfterburnerSharedMemoryAvailable == true)
                {
                    try
                    {
                        var abData = _afterburnerService.ReadAfterburnerGpuData();
                        // Sanity bound: Afterburner shared-memory float may contain garbage if the
                        // MAHM struct layout has changed; reject anything outside realistic GPU range.
                        if (abData != null && abData.GpuTemperature > 0 && abData.GpuTemperature < 150)
                        {
                            // GPU temp from Afterburner — same die sensor, no contention
                            _cachedGpuTemp = abData.GpuTemperature;
                            
                            // Clocks & power from Afterburner
                            if (abData.CoreClockMhz > 0) _cachedGpuClockMhz = abData.CoreClockMhz;
                            if (abData.MemoryClockMhz > 0) _cachedGpuMemClockMhz = abData.MemoryClockMhz;
                            // Afterburner reports power as percentage
                            var afterburnerGpuPower = abData.GpuPower > 0
                                ? (_nvapi?.DefaultPowerLimitWatts > 0
                                    ? (abData.GpuPower / 100.0) * _nvapi.DefaultPowerLimitWatts
                                    : abData.GpuPower)
                                : 0;
                            _cachedGpuPowerWatts = StabilizePowerReading(
                                afterburnerGpuPower,
                                ref _lastValidGpuPowerWatts,
                                ref _consecutiveZeroGpuPowerReads,
                                _cachedGpuLoad,
                                _cachedGpuTemp);
                            
                            // GPU load from Afterburner if available
                            if (abData.GpuLoadPercent > 0)
                                _cachedGpuLoad = abData.GpuLoadPercent;
                            
                            afterburnerProvidedData = true;
                            
                            if (!_afterburnerCoexistenceActive)
                            {
                                _afterburnerCoexistenceActive = true;
                                _afterburnerService.AfterburnerCoexistenceActive = true;
                                _logging?.Info("[WmiBiosMonitor] ✓ Afterburner coexistence active — GPU temp/clocks/power from shared memory, NVAPI reduced to load+VRAM");
                            }
                            
                            // Use lightweight NVAPI for load (if not from AB) + VRAM only
                            if (_nvapi?.IsAvailable == true && !_nvapiMonitoringDisabled)
                            {
                                try
                                {
                                    var lightSample = _nvapi.GetLoadAndVramOnly();
                                    
                                    // Only use NVAPI load if Afterburner didn't provide it
                                    if (abData.GpuLoadPercent <= 0)
                                        _cachedGpuLoad = lightSample.GpuLoadPercent;
                                    
                                    _cachedGpuVramUsedMb = lightSample.VramUsedMb;
                                    _cachedGpuVramTotalMb = lightSample.VramTotalMb;
                                    _nvapiConsecutiveFailures = 0;
                                }
                                catch { } // Non-critical — VRAM data is cosmetic
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging?.Debug($"[WmiBiosMonitor] Afterburner shared memory read failed: {ex.Message}");
                    }
                }
                
                // Detect Afterburner exit — fall back to full NVAPI
                if (!afterburnerProvidedData && _afterburnerCoexistenceActive)
                {
                    _afterburnerCoexistenceActive = false;
                    if (_afterburnerService != null)
                        _afterburnerService.AfterburnerCoexistenceActive = false;
                    _logging?.Info("[WmiBiosMonitor] Afterburner coexistence deactivated — returning to full NVAPI monitoring");
                }
                
                // Full NVAPI monitoring when Afterburner is NOT providing data
                // Auto-recover after cooldown period (RC-1 fix: no longer permanently disabled)
                if (_nvapiMonitoringDisabled && DateTime.Now >= _nvapiDisabledUntil)
                {
                    _nvapiMonitoringDisabled = false;
                    _nvapiConsecutiveFailures = 0;
                    _logging?.Info($"[WmiBiosMonitor] NVAPI monitoring re-enabled after {NvapiRecoveryCooldownSeconds}s cooldown");
                }

                if (!afterburnerProvidedData && _nvapi?.IsAvailable == true && !_nvapiMonitoringDisabled)
                {
                    try
                    {
                        var gpuSample = _nvapi.GetMonitoringSample();
                        
                        _cachedGpuLoad = gpuSample.GpuLoadPercent;
                        _cachedGpuPowerWatts = StabilizePowerReading(
                            gpuSample.GpuPowerWatts,
                            ref _lastValidGpuPowerWatts,
                            ref _consecutiveZeroGpuPowerReads,
                            gpuSample.GpuLoadPercent,
                            gpuSample.GpuTemperatureC > 0 ? gpuSample.GpuTemperatureC : _cachedGpuTemp);
                        _cachedGpuClockMhz = gpuSample.CoreClockMhz;
                        _cachedGpuMemClockMhz = gpuSample.MemoryClockMhz;
                        _cachedGpuVramUsedMb = gpuSample.VramUsedMb;
                        _cachedGpuVramTotalMb = gpuSample.VramTotalMb;
                        
                        // If NVAPI returns a GPU temp, prefer it over WMI BIOS
                        // (NVAPI reads directly from the GPU die sensor, higher precision)
                        if (gpuSample.GpuTemperatureC > 0)
                        {
                            _cachedGpuTemp = gpuSample.GpuTemperatureC;
                        }
                        
                        _nvapiConsecutiveFailures = 0; // Reset on success
                    }
                    catch (Exception ex)
                    {
                        _nvapiConsecutiveFailures++;
                        if (_nvapiConsecutiveFailures >= MaxNvapiFailuresBeforeDisable)
                        {
                            _nvapiMonitoringDisabled = true;
                            _nvapiDisabledUntil = DateTime.Now.AddSeconds(NvapiRecoveryCooldownSeconds);
                            _logging?.Warn($"[WmiBiosMonitor] NVAPI monitoring suspended for {NvapiRecoveryCooldownSeconds}s after {MaxNvapiFailuresBeforeDisable} consecutive failures: {ex.Message}");
                        }
                    }
                }
                
                // ═══════════════════════════════════════════════════════════════
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 3: Windows PerformanceCounter — CPU load
                // Uses a persistent counter (initialised in constructor) so no
                // Thread.Sleep() or per-poll allocation is needed. Each NextValue()
                // call returns the average CPU utilisation since the previous call,
                // which at 2-second poll intervals gives the correct interval average.
                // ═══════════════════════════════════════════════════════════════

                if (_cpuPerfCounterAvailable && _cpuPerfCounter != null)
                {
                    try
                    {
                        var load = _cpuPerfCounter.NextValue();
                        if (load >= 0 && load <= 100)
                            _cachedCpuLoad = load;
                    }
                    catch
                    {
                        // Counter became unavailable (e.g. performance counter service reset)
                        _cpuPerfCounterAvailable = false;
                    }
                }
                
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 3b: ACPI Thermal Zone — Higher-precision CPU temp
                // WMI BIOS returns integer-only temps; ACPI gives 0.1°C precision.
                // ═══════════════════════════════════════════════════════════════
                
                if (_acpiThermalAvailable)
                {
                    try
                    {
                        var acpiTemp = GetAcpiCpuTemperature();
                        if (acpiTemp > 0 && acpiTemp < 110)
                        {
                            // ACPI thermal zones can occasionally report unrelated/system zones.
                            // Reject large outliers against the current WMI/fallback reading unless
                            // we are explicitly in a frozen-sensor recovery path.
                            if (_cachedCpuTemp > 0 && !_cpuTempFrozen &&
                                Math.Abs(acpiTemp - _cachedCpuTemp) > MaxAcpiDeltaFromWmiC)
                            {
                                _logging?.Debug($"[WmiBiosMonitor] Ignoring ACPI CPU outlier {acpiTemp:F1}°C (current {_cachedCpuTemp:F1}°C)");
                            }
                            else
                            {
                                _cachedCpuTemp = acpiTemp;
                            }
                        }
                    }
                    catch
                    {
                        _acpiThermalAvailable = false;
                    }
                }

                TryApplyCpuTemperatureFallback();
                
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 3c: WMI Win32_Processor — CPU clock speed
                // Provides current CPU frequency in MHz.
                // ═══════════════════════════════════════════════════════════════
                
                try
                {
                    var clockMhz = GetCpuCurrentClockMhz();
                    if (clockMhz > 0) _cachedCpuClockMhz = clockMhz;
                }
                catch
                {
                    // WMI query may fail
                }
                
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 4: PawnIO MSR — CPU throttling detection
                // Only if PawnIO is available. NOT required for core monitoring.
                // ═══════════════════════════════════════════════════════════════
                
                if (_msrAccess != null)
                {
                    try
                    {
                        _cachedCpuThermalThrottling = _msrAccess.ReadThermalThrottlingStatus();
                        _cachedCpuPowerThrottling = _msrAccess.ReadPowerThrottlingStatus();
                        
                        // CPU package power via Intel RAPL MSR
                        double cpuPower = _msrAccess.ReadCpuPackagePowerWatts();
                        _cachedCpuPowerWatts = StabilizePowerReading(
                            cpuPower,
                            ref _lastValidCpuPowerWatts,
                            ref _consecutiveZeroCpuPowerReads,
                            _cachedCpuLoad,
                            _cachedCpuTemp);
                    }
                    catch
                    {
                        // MSR read failure is non-critical
                    }
                }

                TryApplyPowerFallback();
                SanitizeGpuTelemetry();
                
                // ═══════════════════════════════════════════════════════════════
                // SOURCE 5: WMI — SSD Temperature + Battery Discharge Rate
                // ═══════════════════════════════════════════════════════════════
                
                if (_ssdTempAvailable)
                {
                    try
                    {
                        _cachedSsdTemp = GetSsdTemperature();
                    }
                    catch
                    {
                        _ssdTempAvailable = false;
                    }
                }
                
                // Battery discharge rate (only when on battery)
                if (!_batteryMonitoringDisabled && !IsOnAcPower())
                {
                    _cachedBatteryDischargeRate = GetBatteryDischargeRate();
                }
                else
                {
                    _cachedBatteryDischargeRate = 0;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] Update failed: {ex.Message}");
            }
        }

        private static double StabilizePowerReading(
            double reading,
            ref double lastValidReading,
            ref int consecutiveZeroReads,
            double loadPercent,
            double temperatureC)
        {
            if (reading > 0)
            {
                lastValidReading = reading;
                consecutiveZeroReads = 0;
                return reading;
            }

            consecutiveZeroReads++;
            bool systemLikelyActive = loadPercent >= ActiveLoadThresholdPercent || temperatureC >= ActiveTempThresholdC;

            if (systemLikelyActive && lastValidReading > 0 && consecutiveZeroReads <= MaxTransientZeroPowerReads)
            {
                return lastValidReading;
            }

            if (consecutiveZeroReads > MaxTransientZeroPowerReads)
            {
                lastValidReading = 0;
            }

            return 0;
        }
        
        private MonitoringSample BuildSampleFromCache()
        {
            var cpuTempState = GetTemperatureState(_cachedCpuTemp, _cpuTempFrozen, false);
            var gpuTempState = _gpuInactive
                ? TelemetryDataState.Inactive
                : GetTemperatureState(_cachedGpuTemp, _gpuTempFrozen, false);

            return new MonitoringSample
            {
                Timestamp = DateTime.Now,
                
                // WMI BIOS — temps & fans
                CpuTemperatureC = _cachedCpuTemp,
                GpuTemperatureC = _cachedGpuTemp,
                FanRpm = _cachedCpuFanRpm,
                Fan1Rpm = _cachedCpuFanRpm,
                Fan2Rpm = _cachedGpuFanRpm,
                GpuFanPercent = EstimateFanPercent(_cachedGpuFanRpm),
                CpuTemperatureState = cpuTempState,
                GpuTemperatureState = gpuTempState,
                CpuPowerState = GetPowerState(_cachedCpuPowerWatts),
                Fan1RpmState = GetRpmState(_cachedCpuFanRpm),
                Fan2RpmState = GetRpmState(_cachedGpuFanRpm),
                
                // PerformanceCounter — CPU load
                CpuLoadPercent = _cachedCpuLoad,
                
                // PawnIO MSR — CPU package power (Intel RAPL)
                CpuPowerWatts = _cachedCpuPowerWatts,
                
                // WMI — CPU clock
                CpuCoreClocksMhz = _cachedCpuClockMhz > 0 
                    ? new System.Collections.Generic.List<double> { _cachedCpuClockMhz } 
                    : new System.Collections.Generic.List<double>(),
                
                // NVAPI — GPU metrics
                GpuLoadPercent = _cachedGpuLoad,
                GpuPowerWatts = _cachedGpuPowerWatts,
                GpuClockMhz = _cachedGpuClockMhz,
                GpuMemoryClockMhz = _cachedGpuMemClockMhz,
                GpuVramUsageMb = _cachedGpuVramUsedMb,
                GpuVramTotalMb = _cachedGpuVramTotalMb,
                GpuName = _cachedGpuName,
                
                // WMI — RAM
                RamUsageGb = GetUsedMemoryGB(),
                RamTotalGb = GetTotalPhysicalMemoryGB(),
                
                // WMI — Battery
                BatteryChargePercent = GetBatteryCharge(),
                IsOnAcPower = IsOnAcPower(),
                BatteryDischargeRateW = _cachedBatteryDischargeRate,
                
                // PawnIO MSR — Throttling
                IsCpuThermalThrottling = _cachedCpuThermalThrottling,
                IsCpuPowerThrottling = _cachedCpuPowerThrottling,
                
                // GPU throttling estimation (based on temp thresholds)
                IsGpuThermalThrottling = _cachedGpuTemp >= 87, // Typical laptop GPU throttle point
                
                // SSD
                SsdTemperatureC = _cachedSsdTemp,
            };
        }
        
        private static int EstimateFanPercent(int rpm)
        {
            // Estimate percentage based on typical laptop fan range (0-5500 RPM)
            if (rpm <= 0) return 0;
            if (rpm >= 5500) return 100;
            return (int)(rpm / 55.0);
        }

        private void TryApplyCpuTemperatureFallback()
        {
            if (_cachedCpuTemp <= 0 && !_preferWorkerCpuTempForModel)
            {
                return;
            }

            bool lowAndLoaded = _cachedCpuTemp <= ImplausiblyLowCpuTempThresholdC &&
                                _cachedCpuLoad >= CpuLoadThresholdForLowTempFallbackPercent;
            bool shouldFallback = _preferWorkerCpuTempForModel || _cpuTempFrozen || lowAndLoaded;

            if (!shouldFallback)
            {
                return;
            }

            var fallbackMonitor = EnsureTempFallbackMonitor();
            if (fallbackMonitor == null)
            {
                return;
            }

            try
            {
                double fallbackCpuTemp = fallbackMonitor.GetCpuTemperature();
                if (fallbackCpuTemp > 0 && fallbackCpuTemp < 110)
                {
                    bool shouldApplyFallback = _preferWorkerCpuTempForModel ||
                                               _cachedCpuTemp <= 0 ||
                                               Math.Abs(fallbackCpuTemp - _cachedCpuTemp) >= 1.0;

                    if (shouldApplyFallback)
                    {
                        double previous = _cachedCpuTemp;
                        _cachedCpuTemp = fallbackCpuTemp;
                        _lastCpuTempReading = fallbackCpuTemp;

                        if (_preferWorkerCpuTempForModel && !_modelCpuTempPreferenceLogged)
                        {
                            _logging?.Info($"[WmiBiosMonitor] CPU temp source override active for model '{_systemModel}': using worker sensor ({fallbackCpuTemp:F1}°C)");
                            _modelCpuTempPreferenceLogged = true;
                        }
                        else if (!_cpuTempFallbackLogged)
                        {
                            _logging?.Warn($"[WmiBiosMonitor] CPU temp fallback active: WMI/ACPI reading looked invalid ({previous:F1}°C), using LibreHardwareMonitor ({fallbackCpuTemp:F1}°C)");
                            _cpuTempFallbackLogged = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"[WmiBiosMonitor] CPU temp fallback read failed: {ex.Message}");
            }
        }

        private static string GetSystemModel()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var model = obj["Model"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(model))
                    {
                        return model.Trim();
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool ShouldPreferWorkerCpuTemp(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            return model.Contains("OMEN MAX 16", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("ah0000", StringComparison.OrdinalIgnoreCase);
        }

        private void TryApplyPowerFallback()
        {
            bool cpuNeedsFallback = _msrAccess == null || (_cachedCpuPowerWatts <= 0.1 &&
                                    (_cachedCpuLoad >= 8.0 || _cachedCpuTemp >= 45.0));
            bool gpuNeedsFallback = _cachedGpuPowerWatts <= 0.1 &&
                                    (_cachedGpuLoad >= 8.0 || _cachedGpuTemp >= 45.0);

            if (!cpuNeedsFallback && !gpuNeedsFallback)
            {
                return;
            }

            var fallbackMonitor = EnsureTempFallbackMonitor();
            if (fallbackMonitor == null)
            {
                return;
            }

            try
            {
                if (cpuNeedsFallback)
                {
                    double cpuPowerFallback = fallbackMonitor.GetCpuPowerWatts();
                    if (cpuPowerFallback > 0.1)
                    {
                        _cachedCpuPowerWatts = cpuPowerFallback;
                        _lastValidCpuPowerWatts = cpuPowerFallback;
                        _consecutiveZeroCpuPowerReads = 0;
                    }
                    else if (_msrAccess == null)
                    {
                        // On systems without MSR power support, avoid pinning a stale initial value forever.
                        _cachedCpuPowerWatts = 0;
                    }
                }

                if (gpuNeedsFallback)
                {
                    double gpuPowerFallback = fallbackMonitor.GetGpuPowerWatts();
                    if (gpuPowerFallback > 0.1)
                    {
                        _cachedGpuPowerWatts = gpuPowerFallback;
                        _lastValidGpuPowerWatts = gpuPowerFallback;
                        _consecutiveZeroGpuPowerReads = 0;
                    }
                }

                if (!_powerFallbackLogged && ((_cachedCpuPowerWatts > 0.1 && cpuNeedsFallback) || (_cachedGpuPowerWatts > 0.1 && gpuNeedsFallback)))
                {
                    _logging?.Info("[WmiBiosMonitor] Power fallback active: using LibreHardwareMonitor power sensors when primary sources report 0W under load");
                    _powerFallbackLogged = true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"[WmiBiosMonitor] Power fallback read failed: {ex.Message}");
            }
        }

        private void SanitizeGpuTelemetry()
        {
            bool hasLiveGpuTelemetry = _cachedGpuTemp > 0 || _cachedGpuClockMhz >= 300.0 || _cachedGpuPowerWatts >= 3.0;
            bool inactiveNow = !hasLiveGpuTelemetry && _cachedGpuLoad < 1.0;
            _consecutiveGpuInactiveReads = inactiveNow ? _consecutiveGpuInactiveReads + 1 : 0;
            _gpuInactive = _consecutiveGpuInactiveReads >= 3;

            if (_gpuInactive)
            {
                _cachedGpuTemp = 0;
                return;
            }

            if (_cachedGpuTemp <= 0)
            {
                return;
            }

            if (_cachedGpuTemp < 15 || _cachedGpuTemp > 110)
            {
                _cachedGpuTemp = 0;
                return;
            }

        }

        private static TelemetryDataState GetTemperatureState(double value, bool isFrozen, bool isInactive)
        {
            if (isInactive) return TelemetryDataState.Inactive;
            if (value <= 0) return TelemetryDataState.Unavailable;
            if (double.IsNaN(value) || double.IsInfinity(value)) return TelemetryDataState.Invalid;
            if (value < 0 || value > 120) return TelemetryDataState.Invalid;
            if (isFrozen) return TelemetryDataState.Stale;
            return TelemetryDataState.Valid;
        }

        private static TelemetryDataState GetPowerState(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return TelemetryDataState.Invalid;
            if (value < 0) return TelemetryDataState.Invalid;
            if (value == 0) return TelemetryDataState.Zero;
            return TelemetryDataState.Valid;
        }

        private static TelemetryDataState GetRpmState(int rpm)
        {
            if (rpm < 0) return TelemetryDataState.Invalid;
            if (rpm == 0) return TelemetryDataState.Zero;
            if (rpm > 8000) return TelemetryDataState.Invalid;
            return TelemetryDataState.Valid;
        }

        private LibreHardwareMonitorImpl? EnsureTempFallbackMonitor()
        {
            var disableLhm = Environment.GetEnvironmentVariable("OMENCORE_DISABLE_LHM");
            if (string.Equals(disableLhm, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(disableLhm, "true", StringComparison.OrdinalIgnoreCase))
            {
                if (!_lhmFallbackDisabledLogged)
                {
                    _logging?.Info("[WmiBiosMonitor] LibreHardwareMonitor fallback disabled via OMENCORE_DISABLE_LHM; using WMI/NVAPI/PawnIO-only telemetry");
                    _lhmFallbackDisabledLogged = true;
                }
                return null;
            }

            if (_tempFallbackMonitor != null)
            {
                return _tempFallbackMonitor;
            }

            if (_tempFallbackInitAttempted)
            {
                return null;
            }

            _tempFallbackInitAttempted = true;

            try
            {
                bool orphanTimeoutEnabled = true;
                int orphanTimeoutMinutes = 5;

                try
                {
                    var cfg = App.Configuration?.Config;
                    if (cfg != null)
                    {
                        orphanTimeoutEnabled = cfg.HardwareWorkerOrphanTimeoutEnabled;
                        orphanTimeoutMinutes = cfg.HardwareWorkerOrphanTimeoutMinutes;
                    }
                }
                catch
                {
                    // Keep safe defaults when configuration is unavailable during early startup.
                }

                _tempFallbackMonitor = new LibreHardwareMonitorImpl(
                    msg => _logging?.Debug($"[TempFallback] {msg}"),
                    useWorker: true,
                    msrAccess: null,
                    orphanTimeoutEnabled: orphanTimeoutEnabled,
                    orphanTimeoutMinutes: orphanTimeoutMinutes);

                _logging?.Info("[WmiBiosMonitor] Initialized LibreHardwareMonitor fallback worker for resilient telemetry recovery");
                return _tempFallbackMonitor;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] Failed to initialize CPU temp fallback monitor: {ex.Message}");
                return null;
            }
        }

        private void TryPrelaunchHardwareWorker()
        {
            if (Interlocked.Exchange(ref _workerPrelaunchAttempted, 1) != 0)
            {
                return;
            }

            try
            {
                var disableLhm = Environment.GetEnvironmentVariable("OMENCORE_DISABLE_LHM");
                if (string.Equals(disableLhm, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(disableLhm, "true", StringComparison.OrdinalIgnoreCase))
                {
                    _logging?.Info("[WmiBiosMonitor] Skipping worker prelaunch because OMENCORE_DISABLE_LHM is enabled");
                    return;
                }

                var workerPath = ResolveWorkerExecutablePath();
                if (string.IsNullOrEmpty(workerPath) || !File.Exists(workerPath))
                {
                    _logging?.Warn("[WmiBiosMonitor] Hardware worker prelaunch skipped: OmenCore.HardwareWorker.exe not found in expected paths");
                    return;
                }

                bool orphanTimeoutEnabled = true;
                int orphanTimeoutMinutes = 5;
                try
                {
                    var cfg = App.Configuration?.Config;
                    if (cfg != null)
                    {
                        orphanTimeoutEnabled = cfg.HardwareWorkerOrphanTimeoutEnabled;
                        orphanTimeoutMinutes = cfg.HardwareWorkerOrphanTimeoutMinutes;
                    }
                }
                catch
                {
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = workerPath,
                    Arguments = $"{Environment.ProcessId} {orphanTimeoutEnabled} {Math.Clamp(orphanTimeoutMinutes, 1, 60)}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                var started = Process.Start(startInfo);
                if (started != null)
                {
                    _logging?.Info($"[WmiBiosMonitor] Hardware worker prelaunch started (PID: {started.Id})");
                }
                else
                {
                    _logging?.Warn("[WmiBiosMonitor] Hardware worker prelaunch returned null process handle");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] Hardware worker prelaunch failed: {ex.Message}");
            }
        }

        private static string? ResolveWorkerExecutablePath()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var candidate in EnumerateWorkerExecutableCandidates(appDir))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static IEnumerable<string> EnumerateWorkerExecutableCandidates(string appDir)
        {
            yield return Path.Combine(appDir, "OmenCore.HardwareWorker.exe");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "OmenCore", "OmenCore.HardwareWorker.exe");

            var current = new DirectoryInfo(appDir);
            while (current != null)
            {
                yield return Path.Combine(current.FullName, "OmenCore.HardwareWorker.exe");
                yield return Path.Combine(current.FullName, "src", "OmenCore.HardwareWorker", "bin", "Release", "net8.0-windows", "OmenCore.HardwareWorker.exe");
                yield return Path.Combine(current.FullName, "src", "OmenCore.HardwareWorker", "bin", "Release", "net8.0-windows", "win-x64", "OmenCore.HardwareWorker.exe");
                yield return Path.Combine(current.FullName, "src", "OmenCore.HardwareWorker", "bin", "Debug", "net8.0-windows", "OmenCore.HardwareWorker.exe");
                yield return Path.Combine(current.FullName, "src", "OmenCore.HardwareWorker", "bin", "Debug", "net8.0-windows", "win-x64", "OmenCore.HardwareWorker.exe");
                yield return Path.Combine(current.FullName, "publish", "win-x64", "OmenCore.HardwareWorker.exe");
                current = current.Parent;
            }
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
        
        /// <summary>
        /// Get SSD/NVMe temperature via WMI storage driver.
        /// </summary>
        private double GetSsdTemperature()
        {
            try
            {
                // Try MSFT_PhysicalDisk first (Windows 10+)
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT Temperature FROM MSFT_StorageReliabilityCounter");
                foreach (var obj in searcher.Get())
                {
                    if (obj["Temperature"] is uint temp && temp > 0 && temp < 100)
                    {
                        return temp;
                    }
                }
            }
            catch
            {
                // Storage WMI namespace may not be available
            }
            
            _ssdTempAvailable = false;
            return 0;
        }
        
        /// <summary>
        /// Get battery discharge rate in watts via WMI.
        /// </summary>
        private double GetBatteryDischargeRate()
        {
            if (_batteryMonitoringDisabled) return 0;
            
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT DischargeRate FROM Win32_Battery");
                foreach (var obj in searcher.Get())
                {
                    if (obj["DischargeRate"] is uint rate && rate > 0 && rate < 500000)
                    {
                        // DischargeRate is in milliwatts
                        return rate / 1000.0;
                    }
                }
            }
            catch { }
            return 0;
        }
        
        /// <summary>
        /// Get CPU temperature from ACPI thermal zone via WMI.
        /// Returns temperature in °C with ~0.1°C precision (vs WMI BIOS integer-only).
        /// MSAcpi_ThermalZoneTemperature reports CurrentTemperature in tenths of Kelvin.
        /// </summary>
        private double GetAcpiCpuTemperature()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\wmi",
                    "SELECT CurrentTemperature, InstanceName FROM MSAcpi_ThermalZoneTemperature");
                
                double bestTemp = 0;
                
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    if (obj["CurrentTemperature"] is uint rawTemp && rawTemp > 0)
                    {
                        // Convert from tenths of Kelvin to Celsius
                        // Round to 1 decimal to avoid IEEE 754 float noise (e.g. 97.05000000000001)
                        double tempC = Math.Round((rawTemp / 10.0) - 273.15, 1);
                        
                        if (tempC > 0 && tempC < 110)
                        {
                            var instanceName = obj["InstanceName"]?.ToString() ?? "";
                            
                            // Prefer CPU-related thermal zones
                            if (_cpuThermalZoneInstance == null)
                            {
                                // First valid zone — use it as default
                                bestTemp = tempC;
                                _cpuThermalZoneInstance = instanceName;
                            }
                            else if (instanceName == _cpuThermalZoneInstance)
                            {
                                // Same zone as before — consistent
                                bestTemp = tempC;
                            }
                            else if (instanceName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                                     instanceName.Contains("CPUZ", StringComparison.OrdinalIgnoreCase) ||
                                     instanceName.Contains("TZ00", StringComparison.OrdinalIgnoreCase))
                            {
                                // CPU-specific zone found — switch to it
                                bestTemp = tempC;
                                _cpuThermalZoneInstance = instanceName;
                            }
                            else if (bestTemp == 0)
                            {
                                bestTemp = tempC;
                            }
                        }
                    }
                }
                
                return bestTemp;
            }
            catch
            {
                _acpiThermalAvailable = false;
                return 0;
            }
        }
        
        /// <summary>
        /// Get CPU current clock speed in MHz via WMI Win32_Processor.
        /// </summary>
        private static double GetCpuCurrentClockMhz()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT CurrentClockSpeed FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    if (obj["CurrentClockSpeed"] is uint clockMhz && clockMhz > 0)
                    {
                        return clockMhz;
                    }
                }
            }
            catch { }
            return 0;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _updateGate.Dispose();
            _wmiBios.Dispose();
            _cpuPerfCounter?.Dispose();
            _tempFallbackMonitor?.Dispose();
        }
    }
}
