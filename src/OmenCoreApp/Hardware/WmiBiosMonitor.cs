using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Self-sustaining WMI BIOS + NVAPI hardware monitor - NO LHM/WinRing0 dependency.
    ///
    /// This is OmenCore's PRIMARY monitoring bridge. It reads all sensor data using
    /// native Windows APIs and NVIDIA's NVAPI - no external kernel drivers required.
    ///
    /// Data Sources:
    /// - CPU/GPU Temperature: HP WMI BIOS (command 0x23) - same as OmenMon
    /// - Fan RPM: HP WMI BIOS (command 0x38) - hardware-accurate
    /// - CPU Load: Windows PerformanceCounter
    /// - GPU Load/Temp/Clocks/VRAM/Power: NVAPI (via NvAPIWrapper)
    /// - CPU Throttling: PawnIO MSR 0x19C (if available)
    /// - RAM: WMI Win32_OperatingSystem / Win32_ComputerSystem
    /// - Battery: WMI Win32_Battery + SystemInformation.PowerStatus
    /// - SSD Temperature: WMI MSStorageDriver (if available)
    ///
    /// PawnIO is used ONLY for MSR-based throttling detection - NOT for core monitoring.
    /// </summary>
    public class WmiBiosMonitor : IHardwareMonitorBridge, IDisposable
    {
        private readonly LoggingService? _logging;
        private readonly HpWmiBios _wmiBios;
        private readonly NvapiService? _nvapi;
        private readonly PawnIOMsrAccess? _msrAccess;
        private readonly string _systemModel;
        private readonly bool _preferWorkerCpuTempForModel;
        private readonly bool _workerBackedCpuTempOverrideEnabled;
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
        // ACPI thermal zone freeze detection (for non-HP devices where WMI BIOS is unavailable)
        private double _lastAcpiCpuTempReading;
        private int _consecutiveIdenticalAcpiTempReads;
        private double _lastValidCpuTempBeforeFreeze;
        private const int MaxConsecutiveIdenticalTempReads = 20;
        private const int IdleConsecutiveIdenticalTempReads = 40;
        private const int FreezeWarnCooldownSeconds = 60;
        private bool _cpuTempFrozen;
        private DateTime _cpuTempFrozeAt = DateTime.MinValue;
        private DateTime _lastCpuFreezeWarnAt = DateTime.MinValue;
        private bool _cpuTempFallbackLogged;
        private bool _modelCpuTempPreferenceLogged;
        private LibreHardwareMonitorImpl? _tempFallbackMonitor;
        private bool _tempFallbackInitAttempted;
        private bool _lhmFallbackDisabledLogged;
        private const double ImplausiblyLowCpuTempThresholdC = 33.0;
        private const double CpuLoadThresholdForLowTempFallbackPercent = 20.0;
        private const double CpuPowerThresholdForLowTempFallbackWatts = 20.0;
        private const int CpuFallbackHoldSeconds = 120;
        private const double MaxAcpiDeltaFromWmiC = 18.0;
        private const int CpuFallbackReadTimeoutMs = 500;
        private const int CpuFallbackReadCooldownSeconds = 30;
        private DateTime _cpuFallbackHoldUntilUtc = DateTime.MinValue;
        private DateTime _cpuFallbackReadDisabledUntilUtc = DateTime.MinValue;
        private bool _cpuFallbackTimeoutLogged;
        
        // GPU temperature freeze detection (similar to CPU temp)
        private double _lastGpuTempReading;
        private int _consecutiveIdenticalGpuTempReads;
        private double _lastValidGpuTempBeforeFreeze;
        private bool _gpuTempFrozen;
        private DateTime _gpuTempFrozeAt = DateTime.MinValue;
        private DateTime _lastGpuFreezeWarnAt = DateTime.MinValue;
        private int _consecutiveGpuInactiveReads;
        private bool _gpuInactive;
        private int _updateReadingsCount;
        
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

        // Windows GPU engine counters fallback (self-reliant, no worker dependency)
        private readonly Dictionary<string, PerformanceCounter> _gpuEngineCounters = new(StringComparer.OrdinalIgnoreCase);
        private bool _gpuEngineCountersPrimed;
        private DateTime _lastGpuEngineCounterRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan GpuEngineCounterRefreshInterval = TimeSpan.FromSeconds(20);
        private bool _gpuEngineFallbackLogged;
        private string _gpuLoadSource = "NVAPI";
        
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
            // For models where WMI BIOS reports coarse/slow-updating CPU temps (e.g. 17-ck2xxx),
            // always use the LHM-backed temp source — either via the worker process when available,
            // or in-process LibreHardwareMonitor when the worker isn't found.
            // Without this, WMI BIOS integer temps (e.g. stuck at 44°C idle) never update accurately
            // and fan curves make decisions on stale data.
            _workerBackedCpuTempOverrideEnabled = _preferWorkerCpuTempForModel;
            
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

            if (_workerBackedCpuTempOverrideEnabled)
            {
                var workerFound = !string.IsNullOrEmpty(ResolveWorkerExecutablePath());
                if (workerFound)
                    _logging?.Warn($"[WmiBiosMonitor] Model '{_systemModel}' detected — prioritizing worker-backed LHM CPU temperature for accuracy");
                else
                    _logging?.Warn($"[WmiBiosMonitor] Model '{_systemModel}' detected — worker not found; using in-process LHM CPU temperature (WMI BIOS reads coarse on this model)");
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
        /// Called by HardwareMonitoringService after consecutive timeout errors, and also
        /// by RecoverAfterResumeAsync after the system wakes from sleep.
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
                _logging?.Info("[WmiBiosMonitor] TryRestartAsync: NVAPI monitoring active — no NVAPI state to reset");
            }

            // After sleep/wake, the hardware worker process may have exited (Windows can kill
            // background processes during sleep, or the orphan-timeout fires before the system
            // resumes). The cached _tempFallbackMonitor still holds a reference to the dead
            // worker's IPC channel, so GetCpuTemperature() returns the last stale value
            // (e.g. 44°C frozen) instead of live data. Disposing and nulling the monitor
            // forces EnsureTempFallbackMonitor() to create a fresh instance that connects
            // to the newly-started worker on the next poll cycle.
            if (_tempFallbackMonitor != null)
            {
                try { _tempFallbackMonitor.Dispose(); } catch { } // Intentional: dispose-path cleanup; swallowing prevents masking the outer exception
                _tempFallbackMonitor = null;
                _tempFallbackInitAttempted = false;
                _modelCpuTempPreferenceLogged = false;
                _cpuTempFallbackLogged = false;
                _logging?.Info("[WmiBiosMonitor] TryRestartAsync: worker-backed temp monitor reset — will reconnect on next poll");
            }

            // Allow TryPrelaunchHardwareWorker() to run again so the worker process is
            // restarted if it died during sleep. Interlocked reset lets the guard pass once.
            Interlocked.Exchange(ref _workerPrelaunchAttempted, 0);
            TryPrelaunchHardwareWorker();

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
                        var cpuFreezeThreshold = (_cachedCpuLoad < 20 && _cachedCpuPowerWatts <= 15)
                            ? IdleConsecutiveIdenticalTempReads
                            : MaxConsecutiveIdenticalTempReads;
                        var gpuFreezeThreshold = _cachedGpuLoad < 10
                            ? IdleConsecutiveIdenticalTempReads
                            : MaxConsecutiveIdenticalTempReads;

                        if (cpuTemp > 0)
                        {
                            // Detect CPU temperature freeze (AMD WMI sensor sometimes stops updating)
                            if (Math.Abs(cpuTemp - _lastCpuTempReading) < 0.1) // Same temp (within 0.1°C)
                            {
                                _consecutiveIdenticalCpuTempReads++;
                                if (_consecutiveIdenticalCpuTempReads > cpuFreezeThreshold && !_cpuTempFrozen)
                                {
                                    var nowUtc = DateTime.UtcNow;
                                    if ((nowUtc - _lastCpuFreezeWarnAt).TotalSeconds >= FreezeWarnCooldownSeconds)
                                    {
                                        _cpuTempFrozen = true;
                                        _cpuTempFrozeAt = nowUtc;
                                        _lastCpuFreezeWarnAt = nowUtc;
                                        _logging?.Warn($"🥶 CPU temperature appears frozen at {cpuTemp:F1}°C for {_consecutiveIdenticalCpuTempReads} readings (load={_cachedCpuLoad:F0}%, power={_cachedCpuPowerWatts:F1}W)");
                                    }
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
                                if (_consecutiveIdenticalGpuTempReads > gpuFreezeThreshold && !_gpuTempFrozen)
                                {
                                    var nowUtc = DateTime.UtcNow;
                                    if ((nowUtc - _lastGpuFreezeWarnAt).TotalSeconds >= FreezeWarnCooldownSeconds)
                                    {
                                        _gpuTempFrozen = true;
                                        _gpuTempFrozeAt = nowUtc;
                                        _lastGpuFreezeWarnAt = nowUtc;
                                        _logging?.Warn($"🥶 GPU temperature appears frozen at {gpuTemp:F1}°C for {_consecutiveIdenticalGpuTempReads} readings (load={_cachedGpuLoad:F0}%)");
                                    }
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
                        if (abData != null)
                        {
                            // Keep coexistence conservative: prefer NVAPI + engine counters, but
                            // allow MAHM as fallback when NVAPI power/load endpoints degrade.
                            if (abData.CoreClockMhz > 0) _cachedGpuClockMhz = abData.CoreClockMhz;
                            if (abData.MemoryClockMhz > 0) _cachedGpuMemClockMhz = abData.MemoryClockMhz;

                            afterburnerProvidedData = true;

                            if (!_afterburnerCoexistenceActive)
                            {
                                _afterburnerCoexistenceActive = true;
                                _afterburnerService.AfterburnerCoexistenceActive = true;
                                _logging?.Info("[WmiBiosMonitor] ✓ Afterburner coexistence active — clocks from shared memory, NVAPI used for temp+load, worker fallback for power/load");
                            }

                            // In coexistence mode: NVAPI remains primary for temp/load/VRAM,
                            // Windows GPU Engine is secondary for load, and MAHM can recover
                            // load/power if NVAPI reports invalid or zero values.
                            if (_nvapi?.IsAvailable == true && !_nvapiMonitoringDisabled)
                            {
                                try
                                {
                                    var lightSample = _nvapi.GetLoadAndVramOnly();
                                    double nvapiLoad = -1;

                                    if (lightSample.GpuTemperatureC > 0)
                                        _cachedGpuTemp = lightSample.GpuTemperatureC;

                                    if (lightSample.GpuLoadPercent >= 0 && lightSample.GpuLoadPercent <= 100)
                                        nvapiLoad = lightSample.GpuLoadPercent;

                                    double mahmLoad = (abData.GpuLoadPercent >= 0 && abData.GpuLoadPercent <= 100)
                                        ? abData.GpuLoadPercent
                                        : -1;

                                    var engineLoad = TryReadWindowsGpuEngineLoadPercent();
                                    if (engineLoad > 0)
                                    {
                                        double chosenLoad = nvapiLoad >= 0 ? Math.Max(nvapiLoad, engineLoad) : engineLoad;
                                        string chosenSource = engineLoad >= nvapiLoad + 1.0 ? "WinGpuEngine" : "NVAPI";

                                        if (mahmLoad >= 0 && mahmLoad > chosenLoad + 1.0 && Math.Abs(mahmLoad - chosenLoad) <= 25.0)
                                        {
                                            chosenLoad = mahmLoad;
                                            chosenSource = "MAHM";
                                        }

                                        _cachedGpuLoad = chosenLoad;
                                        _gpuLoadSource = chosenSource;

                                        if (!_gpuEngineFallbackLogged && _gpuLoadSource == "WinGpuEngine")
                                        {
                                            _logging?.Info("[WmiBiosMonitor] GPU load source switched to Windows GPU Engine counters during Afterburner coexistence");
                                            _gpuEngineFallbackLogged = true;
                                        }
                                    }
                                    else if (nvapiLoad >= 0)
                                    {
                                        _cachedGpuLoad = nvapiLoad;
                                        _gpuLoadSource = "NVAPI";
                                    }
                                    else if (mahmLoad >= 0)
                                    {
                                        _cachedGpuLoad = mahmLoad;
                                        _gpuLoadSource = "MAHM";
                                    }

                                    _cachedGpuVramUsedMb = lightSample.VramUsedMb;
                                    _cachedGpuVramTotalMb = lightSample.VramTotalMb;

                                    var nvapiPowerWatts = _nvapi.GetGpuPowerWatts();
                                    var normalizedMahmPowerWatts = NormalizeAfterburnerPowerToWatts(abData.GpuPower, abData.GpuPowerUnit);
                                    double chosenPowerWatts = nvapiPowerWatts > 0.1 ? nvapiPowerWatts : normalizedMahmPowerWatts;

                                    _cachedGpuPowerWatts = StabilizePowerReading(
                                        chosenPowerWatts,
                                        ref _lastValidGpuPowerWatts,
                                        ref _consecutiveZeroGpuPowerReads,
                                        _cachedGpuLoad,
                                        _cachedGpuTemp);

                                    _nvapiConsecutiveFailures = 0;
                                }
                                catch { } // Non-critical
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
                        _gpuLoadSource = "NVAPI";
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

                                // Freeze detection for ACPI-sourced temps (fires when WMI BIOS is
                                // unavailable, e.g. non-HP devices with only ACPI thermal zones).
                                var acpiFreezeThreshold = (_cachedCpuLoad < 20 && _cachedCpuPowerWatts <= 15)
                                    ? IdleConsecutiveIdenticalTempReads
                                    : MaxConsecutiveIdenticalTempReads;

                                if (Math.Abs(acpiTemp - _lastAcpiCpuTempReading) < 0.1)
                                {
                                    _consecutiveIdenticalAcpiTempReads++;
                                    if (_consecutiveIdenticalAcpiTempReads > acpiFreezeThreshold && !_cpuTempFrozen)
                                    {
                                        var nowUtc = DateTime.UtcNow;
                                        if ((nowUtc - _lastCpuFreezeWarnAt).TotalSeconds >= FreezeWarnCooldownSeconds)
                                        {
                                            _cpuTempFrozen = true;
                                            _cpuTempFrozeAt = nowUtc;
                                            _lastCpuFreezeWarnAt = nowUtc;
                                            _logging?.Warn($"🥶 CPU temperature appears frozen at {acpiTemp:F1}°C for {_consecutiveIdenticalAcpiTempReads} readings (load={_cachedCpuLoad:F0}%, power={_cachedCpuPowerWatts:F1}W)");
                                        }
                                    }
                                }
                                else
                                {
                                    _consecutiveIdenticalAcpiTempReads = 0;
                                    if (_cpuTempFrozen)
                                    {
                                        TimeSpan frozenDuration = DateTime.UtcNow - _cpuTempFrozeAt;
                                        _logging?.Info($"✓ CPU temperature sensor recovered after {frozenDuration.TotalSeconds:F0}s freeze");
                                        _cpuTempFrozen = false;
                                    }
                                }
                                _lastAcpiCpuTempReading = acpiTemp;
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

                TryApplyLoadFallback();
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

            // Periodic diagnostic: log CPU/GPU load every 10 samples (~10s at 1s cadence)
            _updateReadingsCount++;
            if (_updateReadingsCount % 10 == 0)
            {
                _logging?.Info($"[WmiBiosMonitor] Periodic telemetry — CPU load: {_cachedCpuLoad:F0}% ({(_cpuPerfCounter != null ? "PerfCounter" : "unavailable")}), GPU load: {_cachedGpuLoad:F0}% ({_gpuLoadSource}), PCtr active: {_cpuPerfCounterAvailable}");
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
            bool lowAndLoaded = _cachedCpuTemp <= ImplausiblyLowCpuTempThresholdC &&
                                _cachedCpuLoad >= CpuLoadThresholdForLowTempFallbackPercent;
            bool lowAndHighPower = _cachedCpuTemp <= ImplausiblyLowCpuTempThresholdC &&
                                   _cachedCpuPowerWatts >= CpuPowerThresholdForLowTempFallbackWatts;
            bool fallbackHoldActive = DateTime.UtcNow < _cpuFallbackHoldUntilUtc;
            if (DateTime.UtcNow < _cpuFallbackReadDisabledUntilUtc)
            {
                return;
            }

            bool shouldFallback = _workerBackedCpuTempOverrideEnabled ||
                                  _cpuTempFrozen ||
                                  lowAndLoaded ||
                                  lowAndHighPower ||
                                  fallbackHoldActive;

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
                var fallbackTask = Task.Run(() => fallbackMonitor.GetCpuTemperature());
                if (!fallbackTask.Wait(TimeSpan.FromMilliseconds(CpuFallbackReadTimeoutMs)))
                {
                    _cpuFallbackReadDisabledUntilUtc = DateTime.UtcNow.AddSeconds(CpuFallbackReadCooldownSeconds);
                    if (!_cpuFallbackTimeoutLogged)
                    {
                        _logging?.Warn($"[WmiBiosMonitor] CPU temp fallback timed out after {CpuFallbackReadTimeoutMs}ms — disabling fallback for {CpuFallbackReadCooldownSeconds}s to keep monitoring responsive");
                        _cpuFallbackTimeoutLogged = true;
                    }
                    return;
                }

                double fallbackCpuTemp = fallbackTask.GetAwaiter().GetResult();
                if (fallbackCpuTemp > 0 && fallbackCpuTemp < 110)
                {
                    _cpuFallbackTimeoutLogged = false;
                    _cpuFallbackReadDisabledUntilUtc = DateTime.MinValue;

                    bool shouldApplyFallback = _workerBackedCpuTempOverrideEnabled ||
                                               _cachedCpuTemp <= 0 ||
                                               Math.Abs(fallbackCpuTemp - _cachedCpuTemp) >= 1.0;

                    if (shouldApplyFallback)
                    {
                        double previous = _cachedCpuTemp;
                        _cachedCpuTemp = fallbackCpuTemp;
                        _lastCpuTempReading = fallbackCpuTemp;

                        if (lowAndLoaded || lowAndHighPower || _cpuTempFrozen)
                        {
                            _cpuFallbackHoldUntilUtc = DateTime.UtcNow.AddSeconds(CpuFallbackHoldSeconds);
                        }

                        if (_workerBackedCpuTempOverrideEnabled && !_modelCpuTempPreferenceLogged)
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
                   model.Contains("ah0000", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("16-xd0", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("16-ap0", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("8E35", StringComparison.OrdinalIgnoreCase) ||
                   // Discord report 2026-03-26: OMEN 17-ck1xxx random CPU temp drop to 40°C
                   // Same WMI sensor arbitration issue as ck2xxx/xd0xxx — prefer worker-backed temp.
                   model.Contains("17-ck1", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("17-ck2", StringComparison.OrdinalIgnoreCase);
        }

        private bool _loadFallbackLogged;

        private void TryApplyLoadFallback()
        {
            bool cpuNeedsFallback = _cachedCpuLoad <= 2.0 && (_cachedCpuPowerWatts >= 10.0 || _cachedCpuTemp >= 48.0);
            bool gpuNeedsFallback = _cachedGpuLoad <= 2.0 && (
                _cachedGpuPowerWatts >= 15.0 ||
                _cachedGpuTemp >= 50.0 ||
                _cachedGpuClockMhz >= 1200.0 ||
                _cachedGpuMemClockMhz >= 3000.0);

            if (!cpuNeedsFallback && !gpuNeedsFallback)
            {
                return;
            }

            try
            {
                bool cpuRecovered = false;
                bool gpuRecovered = false;

                LibreHardwareMonitorImpl? fallbackMonitor = null;

                if (cpuNeedsFallback)
                {
                    fallbackMonitor ??= EnsureTempFallbackMonitor();
                    if (fallbackMonitor == null)
                    {
                        cpuNeedsFallback = false;
                    }
                }

                if (cpuNeedsFallback)
                {
                    double cpuLoadFallback = fallbackMonitor!.GetCpuLoadPercent();
                    if (cpuLoadFallback > 0 && cpuLoadFallback <= 100)
                    {
                        _cachedCpuLoad = cpuLoadFallback;
                        cpuRecovered = true;
                    }
                }

                if (gpuNeedsFallback)
                {
                    // First try Windows GPU engine counters (no worker dependency).
                    var gpuEngineLoad = TryReadWindowsGpuEngineLoadPercent();
                    if (gpuEngineLoad > _cachedGpuLoad + 1.0 && gpuEngineLoad <= 100)
                    {
                        _cachedGpuLoad = gpuEngineLoad;
                        gpuRecovered = true;

                        if (!_gpuEngineFallbackLogged)
                        {
                            _logging?.Info("[WmiBiosMonitor] GPU load fallback source: Windows GPU Engine counters");
                            _gpuEngineFallbackLogged = true;
                        }
                    }
                    else
                    {
                        fallbackMonitor ??= EnsureTempFallbackMonitor();
                        if (fallbackMonitor == null)
                        {
                            goto FinishFallback;
                        }

                        double gpuLoadFallback = fallbackMonitor.GetGpuLoadPercent();
                        if (gpuLoadFallback > _cachedGpuLoad + 1.0 && gpuLoadFallback <= 100)
                        {
                            _cachedGpuLoad = gpuLoadFallback;
                            gpuRecovered = true;
                        }
                    }

                    // Final safety net: infer a minimum plausible load from sustained GPU power.
                    // Prevents persistent low/stale percentages when vendor counters desync under coexistence.
                    if (_cachedGpuLoad < 15.0 && _cachedGpuPowerWatts >= 45.0)
                    {
                        var defaultTdp = _nvapi?.DefaultPowerLimitWatts > 0 ? _nvapi.DefaultPowerLimitWatts : 150;
                        var inferredLoad = Math.Clamp((_cachedGpuPowerWatts / defaultTdp) * 100.0, 15.0, 100.0);
                        if (inferredLoad > _cachedGpuLoad + 1.0)
                        {
                            _cachedGpuLoad = inferredLoad;
                            _gpuLoadSource = "PowerInferred";
                            gpuRecovered = true;
                        }
                    }
                }

            FinishFallback:

                if (!_loadFallbackLogged && (cpuRecovered || gpuRecovered))
                {
                    _logging?.Info("[WmiBiosMonitor] Load fallback active: using alternate sensors when primary sources report near-zero load");
                    _loadFallbackLogged = true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"[WmiBiosMonitor] Load fallback read failed: {ex.Message}");
            }
        }

        private double NormalizeAfterburnerPowerToWatts(double rawPower, string? unit)
        {
            if (!double.IsFinite(rawPower) || rawPower <= 0)
            {
                return 0;
            }

            int defaultTdp = _nvapi?.DefaultPowerLimitWatts > 0 ? _nvapi.DefaultPowerLimitWatts : 150;
            string normalizedUnit = (unit ?? string.Empty).Trim();

            if (normalizedUnit.Contains("W", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Clamp(rawPower, 0, defaultTdp * 1.5);
            }

            if (normalizedUnit.Contains("%", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Clamp((rawPower / 100.0) * defaultTdp, 0, defaultTdp * 1.5);
            }

            // Unknown MAHM unit: treat <=110 as percent, otherwise assume watts if plausible.
            if (rawPower <= 110)
            {
                return Math.Clamp((rawPower / 100.0) * defaultTdp, 0, defaultTdp * 1.5);
            }

            if (rawPower <= defaultTdp * 1.5)
            {
                return rawPower;
            }

            // Last-resort for suspiciously large values (e.g. percent-like without unit):
            // clamp to a plausible upper bound instead of surfacing 0W.
            return defaultTdp * 1.5;
        }

        private double TryReadWindowsGpuEngineLoadPercent()
        {
            try
            {
                RefreshGpuEngineCountersIfNeeded();
                if (_gpuEngineCounters.Count == 0)
                {
                    return 0;
                }

                if (!_gpuEngineCountersPrimed)
                {
                    foreach (var counter in _gpuEngineCounters.Values)
                    {
                        _ = counter.NextValue();
                    }

                    _gpuEngineCountersPrimed = true;
                    return 0;
                }

                double totalLoad = 0;
                double preferredEnginePeak = 0;
                foreach (var kvp in _gpuEngineCounters)
                {
                    var instanceName = kvp.Key;
                    var value = kvp.Value.NextValue();
                    if (double.IsFinite(value) && value > 0)
                    {
                        totalLoad += value;

                        if (instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                            instanceName.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) ||
                            instanceName.Contains("engtype_Cuda", StringComparison.OrdinalIgnoreCase))
                        {
                            preferredEnginePeak = Math.Max(preferredEnginePeak, value);
                        }
                    }
                }

                var effectiveLoad = preferredEnginePeak > 0 ? preferredEnginePeak : totalLoad;
                return Math.Round(Math.Clamp(effectiveLoad, 0, 100), 1);
            }
            catch
            {
                return 0;
            }
        }

        private void RefreshGpuEngineCountersIfNeeded()
        {
            if (DateTime.UtcNow - _lastGpuEngineCounterRefreshUtc < GpuEngineCounterRefreshInterval && _gpuEngineCounters.Count > 0)
            {
                return;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames()
                .Where(name =>
                    name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_Cuda", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_VideoProcessing", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var staleInstances = _gpuEngineCounters.Keys.Except(instanceNames, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var stale in staleInstances)
            {
                _gpuEngineCounters[stale].Dispose();
                _gpuEngineCounters.Remove(stale);
            }

            foreach (var instanceName in instanceNames)
            {
                if (_gpuEngineCounters.ContainsKey(instanceName))
                {
                    continue;
                }

                _gpuEngineCounters[instanceName] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, true);
                _gpuEngineCountersPrimed = false;
            }

            _lastGpuEngineCounterRefreshUtc = DateTime.UtcNow;
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
            // Hybrid iGPU+dGPU systems can surface a small non-zero GPU power value even when
            // the discrete GPU is effectively inactive (Optimus power-saving path). Treat dGPU
            // as active only when we see stronger discrete-activity signals.
            bool hasDiscreteGpuActivity = _cachedGpuTemp > 0 ||
                                          _cachedGpuClockMhz >= 300.0 ||
                                          _cachedGpuMemClockMhz >= 500.0 ||
                                          _cachedGpuLoad >= 3.0 ||
                                          _cachedGpuVramUsedMb >= 128.0 ||
                                          _cachedGpuPowerWatts >= 15.0;

            bool inactiveNow = !hasDiscreteGpuActivity;
            _consecutiveGpuInactiveReads = inactiveNow ? _consecutiveGpuInactiveReads + 1 : 0;
            _gpuInactive = _consecutiveGpuInactiveReads >= 3;

            if (_gpuInactive)
            {
                _cachedGpuTemp = 0;
                _cachedGpuLoad = 0;
                _cachedGpuPowerWatts = 0;
                _cachedGpuClockMhz = 0;
                _cachedGpuMemClockMhz = 0;
                _cachedGpuVramUsedMb = 0;
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

                var useWorker = !string.IsNullOrEmpty(ResolveWorkerExecutablePath());

                _tempFallbackMonitor = new LibreHardwareMonitorImpl(
                    msg => _logging?.Debug($"[TempFallback] {msg}"),
                    useWorker: useWorker,
                    msrAccess: null,
                    orphanTimeoutEnabled: orphanTimeoutEnabled,
                    orphanTimeoutMinutes: orphanTimeoutMinutes);

                _logging?.Info(useWorker
                    ? "[WmiBiosMonitor] Initialized LibreHardwareMonitor fallback worker for resilient telemetry recovery"
                    : "[WmiBiosMonitor] Initialized in-process LibreHardwareMonitor fallback for on-demand recovery only");
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
                    if (IsLikelyPortableRuntime())
                    {
                        _logging?.Info("[WmiBiosMonitor] Hardware worker prelaunch skipped in portable mode: OmenCore.HardwareWorker.exe not found in expected paths");
                    }
                    else
                    {
                        _logging?.Warn("[WmiBiosMonitor] Hardware worker prelaunch skipped: OmenCore.HardwareWorker.exe not found in expected paths");
                    }
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
                catch (Exception ex)
                {
                    _logging?.Debug($"[WmiBiosMonitor] Using default worker orphan timeout settings due to config read failure: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WmiBiosMonitor] ResolveWorkerExecutablePath failed: {ex.Message}");
            }

            return null;
        }

        private static bool IsLikelyPortableRuntime()
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    return false;
                }

                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OmenCore");
                if (key != null)
                {
                    return false;
                }

                using var keyUser = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OmenCore");
                if (keyUser != null)
                {
                    return false;
                }

                var baseDir = AppContext.BaseDirectory;
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (!string.IsNullOrEmpty(baseDir) &&
                    (baseDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
                     baseDir.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(baseDir) &&
                    (File.Exists(Path.Combine(baseDir, "unins000.exe")) ||
                     File.Exists(Path.Combine(baseDir, "Uninstall.exe"))))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WmiBiosMonitor] IsLikelyPortableRuntime fallback due to exception: {ex.Message}");
                return true;
            }
        }

        private static IEnumerable<string> EnumerateWorkerExecutableCandidates(string appDir)
        {
            // Check the directory of the running exe first — most reliable for single-file self-contained
            // builds where AppDomain.CurrentDomain.BaseDirectory may differ from the exe's location.
            var processDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(processDir) &&
                !string.Equals(processDir, appDir, StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(processDir, "OmenCore.HardwareWorker.exe");
            }

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
            catch
            {
                // WMI query failed — return safe default; static method has no access to instance logger
            }
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
            catch
            {
                // WMI query failed — return safe default; static method has no access to instance logger
            }
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
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] {nameof(IsOnAcPower)} SystemInformation query failed: {ex.Message}");
            }
            
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
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] {nameof(IsOnAcPower)} Win32_Battery query failed: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                _logging?.Warn($"[WmiBiosMonitor] {nameof(GetBatteryDischargeRate)} failed: {ex.Message}");
            }
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
            catch
            {
                // WMI query failed — return safe default; static method has no access to instance logger
            }
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
            foreach (var counter in _gpuEngineCounters.Values)
            {
                counter.Dispose();
            }
            _gpuEngineCounters.Clear();
        }
    }
}
