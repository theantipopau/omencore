using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// HP OMEN WMI BIOS interface for fan control and system management.
    /// Uses the direct hpqBIntM WMI interface like OmenMon project.
    /// 
    /// Based on HP ACPI\PNP0C14 driver interface documented by OmenMon project.
    /// Uses CIM (Common Information Model) for WMI communication.
    /// 
    /// NOTE: 2023+ OMEN models (13th gen Intel and newer) require periodic "heartbeat"
    /// WMI queries to keep fan control unlocked.
    /// 
    /// This class uses a singleton pattern to avoid multiple heartbeat timers.
    /// </summary>
    public class HpWmiBios : IHpWmiBios, IDisposable
    {
        private readonly LoggingService? _logging;
        private bool _isAvailable;
        private bool _disposed;
        
        // Singleton instance tracking
        private static HpWmiBios? _sharedInstance;
        private static readonly object _instanceLock = new();
        private static int _instanceCount = 0;
        
        // CIM session for WMI access (same as OmenMon)
        private CimSession? _cimSession;
        private CimInstance? _biosData;
        private CimInstance? _biosMethods;
        
        // Legacy WMI fallback for BIOS F.15+
        private bool _useLegacyWmi = false;
        
        // Error throttling to reduce log spam
        private DateTime _lastErrorLog = DateTime.MinValue;
        private int _errorCount = 0;
        private const int ErrorLogIntervalSeconds = 30;
        
        // Track WMI command failures
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 5;
        private bool _wmiCommandsDisabled = false;
        
        // BIOS Reliability Tracking (v2.7.0)
        private long _totalCommandAttempts = 0;
        private long _totalCommandSuccesses = 0;
        private long _totalCommandFailures = 0;
        private readonly object _statsLock = new();
        private DateTime _statsResetTime = DateTime.Now;
        
        // Heartbeat timer for 2023+ models
        private Timer? _heartbeatTimer;
        private const int HeartbeatIntervalMs = 60000; // 60 seconds
        private bool _heartbeatEnabled = false;
        
        // Heartbeat health tracking (v2.7.0)
        private int _consecutiveHeartbeatFailures = 0;
        private DateTime _lastSuccessfulHeartbeat = DateTime.MinValue;
        private volatile WmiHeartbeatHealth _heartbeatHealth = WmiHeartbeatHealth.Unknown;

        // HP WMI constants (from OmenMon)
        private const string BIOS_NAMESPACE = @"root\wmi";
        private const string BIOS_DATA = "hpqBDataIn";
        private const string BIOS_DATA_FIELD = "hpqBData";
        private const string BIOS_METHOD = "hpqBIOSInt";
        private const string BIOS_METHOD_CLASS = "hpqBIntM";
        private const string BIOS_METHOD_INSTANCE = @"ACPI\PNP0C14\0_0";
        private const string BIOS_RETURN_CODE_FIELD = "rwReturnCode";
        
        // Pre-defined shared secret for BIOS authorization (from OmenMon)
        private static readonly byte[] BiosSign = new byte[4] { 0x53, 0x45, 0x43, 0x55 }; // "SECU"
        
        /// <summary>
        /// BIOS command identifiers (from OmenMon BiosData.cs).
        /// </summary>
        public enum BiosCmd : uint
        {
            Default = 0x20008,   // Most commands (131080)
            Keyboard = 0x20009,  // Keyboard-related (131081)
            Legacy = 0x00001,    // Earliest implemented (1)
            GpuMode = 0x00002    // Graphics mode switch (2)
        }
        
        // Command type IDs (second parameter in Send method)
        private const uint CMD_FAN_GET_COUNT = 0x10;
        private const uint CMD_FAN_SET_LEVEL = 0x2E;  // SetFanLevel (OmenMon 0x2E)
        private const uint CMD_FAN_GET_LEVEL = 0x2D;  // GetFanLevel (OmenMon 0x2D)
        private const uint CMD_FAN_GET_LEVEL_V2 = 0x37; // GetFanLevel V2 (OMEN Max 2025+)
        private const uint CMD_FAN_GET_RPM = 0x38;    // GetFanRPM direct (OMEN Max 2025+)
        private const uint CMD_FAN_MODE_SET = 0x1A;   // SetFanMode (OmenMon 0x1A)
        private const uint CMD_FAN_MAX_GET = 0x26;    // GetMaxFan (OmenMon 0x26)
        private const uint CMD_FAN_MAX_SET = 0x27;    // SetMaxFan (OmenMon 0x27)
        private const uint CMD_SYSTEM_GET_DATA = 0x28;
        private const uint CMD_GPU_GET_POWER = 0x21;  // GetGpuPower (OmenMon 0x21)
        private const uint CMD_GPU_SET_POWER = 0x22;  // SetGpuPower (OmenMon 0x22)
        private const uint CMD_GPU_GET_MODE = 0x52;   // GetGpuMode - uses Legacy cmd
        private const uint CMD_GPU_SET_MODE = 0x52;   // SetGpuMode - uses GpuMode cmd
        private const uint CMD_TEMP_GET = 0x23;       // GetTemperature (OmenMon 0x23)
        private const uint CMD_BACKLIGHT_SET = 0x05;  // SetBacklight - uses Keyboard cmd
        private const uint CMD_COLOR_GET = 0x02;      // GetColorTable - uses Keyboard cmd
        private const uint CMD_COLOR_SET = 0x03;      // SetColorTable - uses Keyboard cmd
        private const uint CMD_KBD_TYPE_GET = 0x01;   // GetKbdType - uses Keyboard cmd
        private const uint CMD_BRIGHTNESS_GET = 0x04; // GetBrightness - uses Keyboard cmd (v2.9.0)
        private const uint CMD_HAS_BACKLIGHT = 0x06;  // HasBacklight / GetLedAnimation - uses Keyboard cmd
        private const uint CMD_ANIMATION_SET = 0x07;  // SetLedAnimation - uses Keyboard cmd (v2.9.0)
        private const uint CMD_IDLE_SET = 0x31;       // SetIdle (OmenMon 0x31)
        private const uint CMD_BATTERY_CARE = 0x24;   // Battery care mode (charge limit)
        private const uint CMD_OVERDRIVE_GET = 0x35;  // GetOverdrive - display overdrive status
        private const uint CMD_OVERDRIVE_SET = 0x36;  // SetOverdrive - enable/disable display overdrive

        /// <summary>
        /// Fan performance mode enumeration.
        /// On Thermal Policy Version 1 systems, only Default, Performance, and Cool are used.
        /// </summary>
        public enum FanMode : byte
        {
            LegacyDefault = 0x00,
            LegacyPerformance = 0x01,
            LegacyCool = 0x02,
            LegacyQuiet = 0x03,
            Default = 0x30,        // Balanced
            Performance = 0x31,   // High performance
            Cool = 0x50           // Quiet/Cool mode
        }

        /// <summary>
        /// GPU power preset levels.
        /// Standard levels work on most models. Extended levels may be available on RTX 4080/5080.
        /// </summary>
        public enum GpuPowerLevel : byte
        {
            /// <summary>Base TGP only (no boost).</summary>
            Minimum = 0x00,
            
            /// <summary>Custom TGP enabled (+15W on most models).</summary>
            Medium = 0x01,
            
            /// <summary>Custom TGP + PPAB enabled (Dynamic Boost, +15-25W depending on model).</summary>
            Maximum = 0x02,
            
            /// <summary>Extended boost level 3 (RTX 4080/5080 may support +25W or more).</summary>
            Extended3 = 0x03,
            
            /// <summary>Extended boost level 4 (future models).</summary>
            Extended4 = 0x04
        }

        /// <summary>
        /// GPU mode (not Advanced Optimus, requires reboot).
        /// </summary>
        public enum GpuMode : byte
        {
            Hybrid = 0x00,
            Discrete = 0x01,
            Optimus = 0x02
        }

        /// <summary>
        /// Thermal policy version - determines which fan modes are available.
        /// </summary>
        public enum ThermalPolicyVersion : byte
        {
            V0 = 0x00,  // Legacy devices
            V1 = 0x01,  // Current devices (Default/Performance/Cool)
            V2 = 0x02   // OMEN Max 2025+ (new fan commands)
        }

        public bool IsAvailable => _isAvailable;
        public bool IsConnected => _isAvailable; // Alias for IsAvailable
        public string Status { get; private set; } = "Not initialized";
        public ThermalPolicyVersion ThermalPolicy { get; private set; } = ThermalPolicyVersion.V1;
        public int FanCount { get; private set; } = 2;
        
        /// <summary>
        /// Maximum fan level value for this hardware.
        /// Classic OMEN (V0/V1): 55 (krpm, 0-5500 RPM)
        /// Newer OMEN (V2+): 100 (percentage)
        /// Auto-detected during initialization.
        /// </summary>
        public int MaxFanLevel { get; private set; } = 55;
        public bool HeartbeatEnabled => _heartbeatEnabled;
        
        // Heartbeat health properties (v2.7.0)
        /// <summary>
        /// Current WMI heartbeat health status.
        /// </summary>
        public WmiHeartbeatHealth HeartbeatHealth => _heartbeatHealth;
        
        /// <summary>
        /// Time since last successful WMI heartbeat.
        /// </summary>
        public TimeSpan HeartbeatAge => _lastSuccessfulHeartbeat == DateTime.MinValue 
            ? TimeSpan.MaxValue 
            : DateTime.Now - _lastSuccessfulHeartbeat;
        
        /// <summary>
        /// Number of consecutive heartbeat failures.
        /// </summary>
        public int ConsecutiveHeartbeatFailures => _consecutiveHeartbeatFailures;
        
        /// <summary>
        /// Event fired when heartbeat health status changes.
        /// </summary>
        public event EventHandler<WmiHeartbeatHealth>? HeartbeatHealthChanged;
        
        // Cache static WMI data to avoid repeated queries
        private bool _staticDataCached = false;
        private readonly object _cacheLock = new();

        public HpWmiBios(LoggingService? logging = null)
        {
            _logging = logging;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Create CIM session (like OmenMon)
                _cimSession = CimSession.Create(null);
                
                // Set up the BIOS data structure with shared secret
                _biosData = new CimInstance(_cimSession.GetClass(BIOS_NAMESPACE, BIOS_DATA));
                _biosData.CimInstanceProperties["Sign"].Value = BiosSign;
                
                // Retrieve the BIOS methods instance
                _biosMethods = new CimInstance(BIOS_METHOD_CLASS, BIOS_NAMESPACE);
                _biosMethods.CimInstanceProperties.Add(CimProperty.Create("InstanceName", BIOS_METHOD_INSTANCE, CimFlags.Key));
                _biosMethods = _cimSession.GetInstance(BIOS_NAMESPACE, _biosMethods);
                
                if (_biosMethods != null)
                {
                    _isAvailable = true;
                    Status = "HP WMI BIOS interface available (CIM)";
                    _logging?.Info($"✓ {Status}");

                    // Query system data to validate and get thermal policy
                    if (!QuerySystemData())
                    {
                        _logging?.Info("WMI BIOS: Initial query failed, attempting heartbeat sequence...");
                        
                        if (TryHeartbeatSequence())
                        {
                            _logging?.Info("✓ WMI BIOS initialized via heartbeat sequence");
                            StartHeartbeat();
                        }
                        else
                        {
                            _isAvailable = false;
                            Status = "HP WMI BIOS found but commands not functional";
                            _logging?.Warn($"⚠️ {Status}");
                        }
                    }
                    else
                    {
                        StartHeartbeat();
                    }
                    
                    // Auto-detect max fan level after system data is available
                    if (_isAvailable)
                    {
                        DetectMaxFanLevel();
                    }
                }
                else
                {
                    _isAvailable = false;
                    Status = "HP WMI BIOS interface not found";
                    _logging?.Info($"HP WMI BIOS: {Status}");
                }
            }
            catch (CimException ex)
            {
                _isAvailable = false;
                Status = $"CIM query failed: {ex.Message}";
                _logging?.Info($"HP WMI BIOS: {Status}");
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                Status = $"Initialization failed: {ex.Message}";
                _logging?.Error($"HP WMI BIOS: {Status}", ex);
            }
        }
        
        /// <summary>
        /// Try a series of heartbeat queries to "wake up" the WMI interface on 2023+ models.
        /// </summary>
        private bool TryHeartbeatSequence()
        {
            // Send multiple heartbeat queries with small delays
            for (int i = 0; i < 3; i++)
            {
                // Try fan count query (minimal command that usually works)
                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_COUNT, new byte[4], 4);
                if (result != null && result.Length >= 1)
                {
                    FanCount = result[0];
                    _logging?.Info($"  Heartbeat #{i+1}: Fan count = {FanCount}");
                    
                    // Try full system data query now
                    if (QuerySystemData())
                    {
                        return true;
                    }
                }
                
                // Small delay between attempts
                System.Threading.Thread.Sleep(200);
            }
            
            return false;
        }
        
        /// <summary>
        /// Start the heartbeat timer to keep WMI commands working on 2023+ models.
        /// Only one heartbeat timer runs globally, even if multiple HpWmiBios instances exist.
        /// </summary>
        public void StartHeartbeat()
        {
            if (_heartbeatEnabled || !_isAvailable) return;
            
            lock (_instanceLock)
            {
                // Track this instance
                _instanceCount++;
                
                // Only start heartbeat timer once globally
                if (_sharedInstance == null)
                {
                    _sharedInstance = this;
                    _heartbeatTimer = new Timer(HeartbeatCallback, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
                    _heartbeatEnabled = true;
                    _logging?.Info($"✓ WMI BIOS heartbeat started (every {HeartbeatIntervalMs/1000}s)");
                }
                else
                {
                    // Another instance already has heartbeat running - just mark as enabled
                    _heartbeatEnabled = true;
                    _logging?.Debug($"WMI BIOS heartbeat already running (instance #{_instanceCount})");
                }
            }
        }
        
        /// <summary>
        /// Stop the heartbeat timer.
        /// </summary>
        public void StopHeartbeat()
        {
            lock (_instanceLock)
            {
                _heartbeatEnabled = false;
                _instanceCount = Math.Max(0, _instanceCount - 1);
                
                // Only stop the actual timer if this is the shared instance and no other instances
                if (_sharedInstance == this && _instanceCount == 0)
                {
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = null;
                    _sharedInstance = null;
                    _logging?.Info("WMI BIOS heartbeat stopped");
                }
            }
        }
        
        /// <summary>
        /// Heartbeat callback - sends periodic query to keep WMI commands active.
        /// </summary>
        private void HeartbeatCallback(object? state)
        {
            if (!_isAvailable || _wmiCommandsDisabled) return;
            
            try
            {
                // Send a simple fan count query to keep the interface active
                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_COUNT, new byte[4], 4);
                if (result == null)
                {
                    _consecutiveHeartbeatFailures++;
                    _logging?.Warn($"WMI BIOS heartbeat failed (consecutive: {_consecutiveHeartbeatFailures})");
                    
                    if (_consecutiveHeartbeatFailures >= 3)
                    {
                        UpdateHeartbeatHealth(WmiHeartbeatHealth.Failing);
                    }
                    else
                    {
                        UpdateHeartbeatHealth(WmiHeartbeatHealth.Degraded);
                    }
                }
                else
                {
                    _consecutiveHeartbeatFailures = 0;
                    _lastSuccessfulHeartbeat = DateTime.Now;
                    UpdateHeartbeatHealth(WmiHeartbeatHealth.Healthy);
                }
            }
            catch (Exception ex)
            {
                _consecutiveHeartbeatFailures++;
                _logging?.Warn($"WMI BIOS heartbeat error: {ex.Message}");
                UpdateHeartbeatHealth(_consecutiveHeartbeatFailures >= 3 
                    ? WmiHeartbeatHealth.Failing 
                    : WmiHeartbeatHealth.Degraded);
            }
        }
        
        /// <summary>
        /// Update heartbeat health status and fire event if changed.
        /// </summary>
        private void UpdateHeartbeatHealth(WmiHeartbeatHealth newHealth)
        {
            if (_heartbeatHealth != newHealth)
            {
                var oldHealth = _heartbeatHealth;
                _heartbeatHealth = newHealth;
                _logging?.Info($"WMI heartbeat health changed: {oldHealth} → {newHealth}");
                
                try
                {
                    HeartbeatHealthChanged?.Invoke(this, newHealth);
                }
                catch (Exception ex)
                {
                    _logging?.Error($"Error invoking HeartbeatHealthChanged: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Query system data and validate WMI commands work.
        /// Returns true if commands work, false otherwise.
        /// Results are cached after first successful query.
        /// </summary>
        private bool QuerySystemData()
        {
            // Return cached data if available
            lock (_cacheLock)
            {
                if (_staticDataCached)
                {
                    return true;
                }
            }
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_SYSTEM_GET_DATA, null, 128);
                if (result != null && result.Length >= 9)
                {
                    ThermalPolicy = (ThermalPolicyVersion)result[3];
                    _logging?.Info($"  Thermal Policy: V{(int)ThermalPolicy}");
                    
                    // OMEN Max 2025+ detection - check model name as well
                    // Some OMEN Max models report V1 but need V2 commands for fan reading
                    if (ThermalPolicy >= ThermalPolicyVersion.V2)
                    {
                        _logging?.Info($"  ✓ OMEN Max 2025+ detected (V2 thermal policy) - using enhanced fan commands");
                    }
                    else
                    {
                        // Check model name for OMEN Max which may report V1 but need V2
                        var modelName = GetModelName();
                        if (!string.IsNullOrEmpty(modelName) && 
                            modelName.Contains("MAX", StringComparison.OrdinalIgnoreCase) &&
                            modelName.Contains("OMEN", StringComparison.OrdinalIgnoreCase))
                        {
                            _logging?.Info($"  ⚠️ OMEN Max detected by name but reports V1 - forcing V2 for fan commands");
                            ThermalPolicy = ThermalPolicyVersion.V2;
                        }
                    }

                    // Query fan count
                    var fanResult = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_COUNT, new byte[4], 4);
                    if (fanResult != null && fanResult.Length >= 1)
                    {
                        FanCount = fanResult[0];
                        _logging?.Info($"  Fan Count: {FanCount}");
                    }
                    
                    // Cache successful query
                    lock (_cacheLock)
                    {
                        _staticDataCached = true;
                    }
                    
                    return true; // Commands are working
                }
                
                _logging?.Warn("WMI BIOS: System data query returned empty result");
                return false;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to query system data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Auto-detect the maximum fan level for this hardware.
        /// Some models use 0-55 (krpm, 5500 RPM), others use 0-100 (percentage).
        /// Called automatically during initialization after system data is available.
        /// </summary>
        /// <param name="userOverride">User-configured override (0 = auto-detect)</param>
        /// <param name="modelMaxFanLevel">Model database override (null = auto-detect)</param>
        public void DetectMaxFanLevel(int userOverride = 0, int? modelMaxFanLevel = null)
        {
            try
            {
                // User override takes priority (if set to a valid value > 0)
                if (userOverride > 0 && userOverride <= 100)
                {
                    MaxFanLevel = userOverride;
                    _logging?.Info($"Max fan level set to {MaxFanLevel} (user override)");
                    return;
                }

                // Model database override (only when explicitly defined)
                if (modelMaxFanLevel.HasValue && modelMaxFanLevel.Value > 0 && modelMaxFanLevel.Value <= 100)
                {
                    MaxFanLevel = modelMaxFanLevel.Value;
                    _logging?.Info($"Max fan level set to {MaxFanLevel} (model database)");
                    return;
                }

                // v2.8.6: Removed current-fan-level auto-detection.
                // The previous heuristic (if current level > 55, assume MaxFanLevel=100)
                // caused false positives when OMEN Gaming Hub or other software was
                // running and had set fans to elevated levels at startup.
                // This led to the "RPM glitch" where fans would show inflated RPM values
                // and the slider would allow setting levels above the actual hardware max.

                // V2 thermal policy (OMEN Max 2025+) uses percentage range
                if (ThermalPolicy >= ThermalPolicyVersion.V2)
                {
                    MaxFanLevel = 100;
                    _logging?.Info($"Max fan level set to {MaxFanLevel} (ThermalPolicy V2+)");
                    return;
                }

                // Default to classic 0-55 krpm range for V1 models
                MaxFanLevel = 55;
                _logging?.Info($"Max fan level: {MaxFanLevel} (classic krpm range)");
            }
            catch (Exception ex)
            {
                MaxFanLevel = 55;
                _logging?.Warn($"Failed to detect max fan level, using default {MaxFanLevel}: {ex.Message}");
            }
        }

        /// <summary>
        /// Set fan performance mode via WMI BIOS.
        /// Uses OmenMon's exact command format: Cmd.Default (0x20008), CommandType 0x1A, data {0xFF, mode, 0, 0}
        /// </summary>
        public bool SetFanMode(FanMode mode)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set fan mode: WMI BIOS not available");
                return false;
            }

            try
            {
                // OmenMon format: {0xFF, (byte)mode, 0x00, 0x00}
                var data = new byte[4];
                data[0] = 0xFF;  // Constant required by HP BIOS
                data[1] = (byte)mode;
                data[2] = 0x00;
                data[3] = 0x00;

                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_MODE_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Fan mode set to: {mode} (0x{(byte)mode:X2})");
                    return true;
                }
                else
                {
                    _logging?.Warn($"Fan mode command returned null for: {mode}");
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan mode: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Set fan speed levels directly (0-255, in krpm units).
        /// OmenMon: Cmd.Default, 0x2E, {fan1Level, fan2Level, 0x00, 0x00}
        /// Note: This call will always check for BIOS error and throw an exception if it occurred.
        /// </summary>
        public bool SetFanLevel(byte fan1Level, byte fan2Level)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set fan level: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = fan1Level;
                data[1] = fan2Level;
                data[2] = 0x00;
                data[3] = 0x00;

                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_SET_LEVEL, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Fan levels set: CPU={fan1Level}, GPU={fan2Level}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan level: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Get current fan speed levels.
        /// OmenMon: Cmd.Default, 0x2D (V1), 0x37 (V2 OMEN Max 2025+)
        /// Includes fallback logic when V2 commands fail on certain models.
        /// </summary>
        public (byte fan1, byte fan2)? GetFanLevel()
        {
            if (!_isAvailable) return null;

            try
            {
                // Try V2 command first for OMEN Max 2025+ (ThermalPolicy V2)
                if (ThermalPolicy >= ThermalPolicyVersion.V2)
                {
                    var v2Result = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_LEVEL_V2, new byte[4], 128);
                    if (v2Result != null && v2Result.Length >= 2 && (v2Result[0] > 0 || v2Result[1] > 0))
                    {
                        _logging?.Debug($"Fan level via V2 (0x37): Fan1={v2Result[0]}, Fan2={v2Result[1]}");
                        return (v2Result[0], v2Result[1]);
                    }
                    
                    // V2 level command failed — try direct RPM command and convert.
                    // Only do this on confirmed V2 systems to avoid circular phantom data.
                    var rpmResult = GetFanRpmDirect();
                    if (rpmResult.HasValue && (rpmResult.Value.fan1Rpm > 0 || rpmResult.Value.fan2Rpm > 0))
                    {
                        // Convert validated RPM to level (divide by 100 for krpm-style value)
                        byte f1 = (byte)Math.Clamp(rpmResult.Value.fan1Rpm / 100, 0, 255);
                        byte f2 = (byte)Math.Clamp(rpmResult.Value.fan2Rpm / 100, 0, 255);
                        return (f1, f2);
                    }
                    
                    // V2 commands not working - fall through to V1 commands
                    _logging?.Debug("V2 fan commands failed, falling back to V1");
                }
                
                // Standard V1 command (fallback for all systems)
                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_LEVEL, new byte[4], 128);
                if (result != null && result.Length >= 2)
                {
                    return (result[0], result[1]);
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get fan level: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Parse a 4-byte RPM buffer returned by the BIOS and return validated RPMs.
        /// Accepts either little-endian or big-endian 16-bit words and performs sanity checks.
        /// </summary>
        public static (int fan1Rpm, int fan2Rpm)? ParseFanRpmBuffer(byte[] rpmResult)
        {
            if (rpmResult == null || rpmResult.Length < 4) return null;

            // Try little-endian first (most common)
            int fan1Rpm = rpmResult[0] | (rpmResult[1] << 8);
            int fan2Rpm = rpmResult[2] | (rpmResult[3] << 8);
            bool le_valid = IsValidRpm(fan1Rpm) && IsValidRpm(fan2Rpm);
            if (le_valid) return (fan1Rpm, fan2Rpm);

            // Try big-endian (some BIOS versions)
            int fan1Rpm_be = (rpmResult[0] << 8) | rpmResult[1];
            int fan2Rpm_be = (rpmResult[2] << 8) | rpmResult[3];
            bool be_valid = IsValidRpm(fan1Rpm_be) && IsValidRpm(fan2Rpm_be);
            if (be_valid) return (fan1Rpm_be, fan2Rpm_be);

            return null;
        }

        /// <summary>
        /// Get raw fan RPM directly from BIOS (V2 command 0x38).
        /// Returns validated RPM values with sanity checks.
        /// v2.6.0: Added endianness detection and sanity validation.
        /// </summary>
        public (int fan1Rpm, int fan2Rpm)? GetFanRpmDirect()
        {
            if (!_isAvailable) return null;

            try
            {
                var rpmResult = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_RPM, new byte[4], 128);
                if (rpmResult == null || rpmResult.Length < 4)
                {
                    _logging?.Debug("GetFanRpmDirect: No data from BIOS");
                    return null;
                }

                // Log raw bytes for debugging
                _logging?.Debug($"GetFanRpmDirect raw: [{rpmResult[0]:X2} {rpmResult[1]:X2} {rpmResult[2]:X2} {rpmResult[3]:X2}]");

                var parsed = ParseFanRpmBuffer(rpmResult);
                if (parsed.HasValue)
                {
                    _logging?.Debug($"GetFanRpmDirect: Parsed RPMs - CPU={parsed.Value.fan1Rpm}, GPU={parsed.Value.fan2Rpm}");
                    return parsed.Value;
                }

                _logging?.Warn($"GetFanRpmDirect: Invalid RPM buffer returned by BIOS");
                return null;
            }
            catch (Exception ex)
            {
                _logging?.Warn($"GetFanRpmDirect failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if an RPM value is within valid range.
        /// Valid range: 0-8000 RPM (typical laptop fans)
        /// </summary>
        public static bool IsValidRpm(int rpm)
        {
            return rpm >= 0 && rpm <= 8000;
        }

        /// <summary>
        /// Enable or disable maximum fan speed mode.
        /// OmenMon: Cmd.Default, 0x27, {enabled ? 1 : 0, 0, 0, 0}
        /// </summary>
        public bool SetFanMax(bool enabled)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set fan max: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = (byte)(enabled ? 1 : 0);
                data[1] = 0x00;
                data[2] = 0x00;
                data[3] = 0x00;

                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_MAX_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Fan max mode: {(enabled ? "enabled" : "disabled")}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set fan max: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Get current fan max mode status.
        /// OmenMon: Cmd.Default, 0x26
        /// </summary>
        public bool? GetFanMax()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_MAX_GET, new byte[4], 4);
                if (result != null && result.Length >= 1)
                {
                    return result[0] != 0;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get fan max status: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Get BIOS temperature sensor reading (CPU).
        /// OmenMon: Cmd.Default, 0x23, {0x01, 0, 0, 0}
        /// </summary>
        public double? GetTemperature()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_TEMP_GET, new byte[4] { 0x01, 0x00, 0x00, 0x00 }, 4);
                if (result != null && result.Length >= 1 && result[0] > 0 && result[0] < 110)
                {
                    return (double)result[0];
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get temperature: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Get GPU temperature from BIOS.
        /// OmenMon: Cmd.Default, 0x23, {0x02, 0, 0, 0}
        /// v2.6.0: Added GPU temperature support.
        /// </summary>
        public double? GetGpuTemperature()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_TEMP_GET, new byte[4] { 0x02, 0x00, 0x00, 0x00 }, 4);
                if (result != null && result.Length >= 1 && result[0] > 0 && result[0] < 110)
                {
                    return (double)result[0];
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"Failed to get GPU temperature from BIOS: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Get both CPU and GPU temperatures from BIOS.
        /// v2.6.0: Provides reliable temperature source independent of LibreHardwareMonitor.
        /// </summary>
        public (double cpuTemp, double gpuTemp)? GetBothTemperatures()
        {
            var cpu = GetTemperature();
            var gpu = GetGpuTemperature();
            
            if (cpu.HasValue || gpu.HasValue)
            {
                return (cpu ?? 0.0, gpu ?? 0.0);
            }
            return null;
        }

        /// <summary>
        /// Set GPU power preset.
        /// OmenMon: Cmd.Default, 0x22
        /// </summary>
        public bool SetGpuPower(GpuPowerLevel level)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set GPU power: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                // GpuCustomTgp, GpuPpab, GpuDState, PeakTemperature
                switch (level)
                {
                    case GpuPowerLevel.Minimum:
                        data[0] = 0; // CustomTgp off
                        data[1] = 0; // PPAB off
                        break;
                    case GpuPowerLevel.Medium:
                        data[0] = 1; // CustomTgp on
                        data[1] = 0; // PPAB off
                        break;
                    case GpuPowerLevel.Maximum:
                        data[0] = 1; // CustomTgp on
                        data[1] = 1; // PPAB on (Dynamic Boost)
                        break;
                    case GpuPowerLevel.Extended3:
                    case GpuPowerLevel.Extended4:
                        // Extended levels: try raw byte value for PPAB
                        // RTX 5080 may support PPAB values > 1 for +25W boost
                        data[0] = 1; // CustomTgp on
                        data[1] = (byte)(level - GpuPowerLevel.Maximum + 1); // PPAB = 2, 3, etc.
                        break;
                }
                data[2] = 0x01; // DState = D1
                data[3] = 0x00; // PeakTemperature

                _logging?.Info($"Sending GPU power command: Level={level}, CustomTgp={data[0]}, PPAB={data[1]}, DState={data[2]}");
                
                var result = SendBiosCommand(BiosCmd.Default, CMD_GPU_SET_POWER, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ GPU power set to: {level} (CustomTgp={data[0]}, PPAB={data[1]})");
                    return true;
                }
                else
                {
                    _logging?.Warn($"GPU power command returned null - BIOS may not support this command or returned error");
                    
                    // Try getting current GPU power to verify if the command was partially successful
                    var currentPower = GetGpuPower();
                    if (currentPower.HasValue)
                    {
                        _logging?.Info($"Current GPU power after command: CustomTgp={currentPower.Value.customTgp}, PPAB={currentPower.Value.ppab}");
                        
                        // Check if the values match what we tried to set
                        bool customTgpMatch = (level == GpuPowerLevel.Minimum) ? !currentPower.Value.customTgp : currentPower.Value.customTgp;
                        bool ppabMatch = (level == GpuPowerLevel.Maximum || level == GpuPowerLevel.Extended3 || level == GpuPowerLevel.Extended4) ? currentPower.Value.ppab : !currentPower.Value.ppab;
                        
                        if (customTgpMatch && ppabMatch)
                        {
                            _logging?.Info($"✓ GPU power appears to be set correctly despite command returning null");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set GPU power: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Get current GPU power settings.
        /// OmenMon: Cmd.Default, 0x21
        /// </summary>
        public (bool customTgp, bool ppab, int dState)? GetGpuPower()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_GPU_GET_POWER, new byte[4], 4);
                if (result != null && result.Length >= 3)
                {
                    return (result[0] != 0, result[1] != 0, result[2]);
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get GPU power: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Get GPU mode (Hybrid/Discrete/Optimus).
        /// OmenMon: Cmd.Legacy, 0x52 (returns BIOS error 4 on unsupported)
        /// </summary>
        public GpuMode? GetGpuMode()
        {
            if (!_isAvailable) return null;

            try
            {
                // Use Legacy command for GPU mode get (OmenMon)
                var result = SendBiosCommand(BiosCmd.Legacy, CMD_GPU_GET_MODE, null, 4);
                if (result != null && result.Length >= 1)
                {
                    return (GpuMode)result[0];
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get GPU mode: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Set GPU mode (requires reboot to take effect).
        /// OmenMon: Cmd.GpuMode, 0x52
        /// </summary>
        public bool SetGpuMode(GpuMode mode)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set GPU mode: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = (byte)mode;

                // Use GpuMode command for setting (OmenMon)
                var result = SendBiosCommand(BiosCmd.GpuMode, CMD_GPU_SET_MODE, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ GPU mode set to: {mode} (reboot required)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set GPU mode: {ex.Message}", ex);
            }
            return false;
        }

        #region Keyboard
        
        /// <summary>
        /// Keyboard type enumeration (per OmenMon).
        /// </summary>
        public enum KbdType : byte
        {
            Standard = 0x00,    // Standard layout
            WithNumPad = 0x01,  // Standard layout with numerical block
            TenKeyLess = 0x02,  // Extra navigation keys but no numerical block (most OMEN laptops)
            PerKeyRgb = 0x03    // Per-key RGB (not supported for zone control)
        }
        
        /// <summary>
        /// Get keyboard type.
        /// OmenMon: Cmd.Keyboard, 0x01
        /// </summary>
        public KbdType? GetKeyboardType()
        {
            if (!_isAvailable) return null;
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_KBD_TYPE_GET, new byte[4], 4);
                if (result != null && result.Length > 0)
                {
                    var kbdType = (KbdType)result[0];
                    _logging?.Info($"Keyboard type: {kbdType} (0x{result[0]:X2})");
                    return kbdType;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get keyboard type: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Check if keyboard backlight is supported.
        /// OmenMon: Cmd.Keyboard, 0x06
        /// </summary>
        public bool HasBacklight()
        {
            if (!_isAvailable) return false;
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_HAS_BACKLIGHT, new byte[4], 4);
                if (result != null && result.Length > 0)
                {
                    var supported = result[0] != 0;
                    _logging?.Info($"Keyboard backlight supported: {supported}");
                    return supported;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to check backlight support: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Set keyboard backlight on/off.
        /// OmenMon: Cmd.Keyboard, 0x05
        /// </summary>
        public bool SetBacklight(bool enabled)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set backlight: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = (byte)(enabled ? 0xE4 : 0x64);

                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_BACKLIGHT_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Keyboard backlight: {(enabled ? "on" : "off")}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set backlight: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Get current keyboard brightness level via WMI BIOS command type 4.
        /// Returns the raw brightness byte from BIOS, or -1 on failure.
        /// Known values: 0xE4 (228) = bright/on, 0x64 (100) = dim/off.
        /// Reference: OmenHubLighter OmenHsaClient.GetKeyboardBrightness()
        /// </summary>
        public int GetBrightness()
        {
            if (!_isAvailable) return -1;
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_BRIGHTNESS_GET, new byte[4], 4);
                if (result != null && result.Length >= 1)
                {
                    var value = result[0];
                    _logging?.Info($"Keyboard brightness raw value: 0x{value:X2} ({value})");
                    return value;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get keyboard brightness: {ex.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Set keyboard brightness level via WMI BIOS command type 5.
        /// This is a more fine-grained version of SetBacklight that sends the raw brightness byte.
        /// Known values: 0xE4 (228) = bright/on, 0x64 (100) = off, 50 = dim.
        /// Reference: OmenHubLighter OmenHsaClient.SetKeyboardBrightness()
        /// </summary>
        /// <param name="brightness">Raw brightness byte to send to BIOS</param>
        public bool SetBrightnessLevel(byte brightness)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set brightness: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = brightness;

                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_BACKLIGHT_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Keyboard brightness set to 0x{brightness:X2} ({brightness})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set brightness: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Get current LED animation settings via WMI BIOS command type 6.
        /// Returns the raw animation data array, or null on failure.
        /// The animation data contains zone, color mode, timing, brightness, and color count info.
        /// Reference: OmenHubLighter OmenHsaConsts.WMILedAnimation enum
        /// </summary>
        public byte[]? GetLedAnimation()
        {
            if (!_isAvailable) return null;
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_HAS_BACKLIGHT, new byte[4], 128);
                if (result != null && result.Length > 0)
                {
                    _logging?.Info($"LED animation data ({result.Length} bytes): " +
                        $"[{string.Join(", ", result.Take(12).Select(b => $"0x{b:X2}"))}...]");
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get LED animation: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Set LED animation via WMI BIOS command type 7.
        /// Controls keyboard lighting effects (static, breathing, color cycle, wave).
        /// 
        /// Animation data structure (from OmenHubLighter WMILedAnimation enum):
        ///   Byte 0: Zone (which zone(s) to apply to)
        ///   Byte 1: ColorMode (effect type: 0=static, 1=breathing, 2=color cycle, etc.)
        ///   Byte 2-3: Time/speed (effect speed, lower = faster)
        ///   Byte 4: Brightness (0-100)
        ///   Byte 5: ColorCount (number of colors in the animation)
        ///   Bytes 6+: Color data (RGB values for animation colors)
        /// 
        /// Reference: OmenHubLighter OmenHsaConsts.WMI_CMD_TYPE_SET_LED_ANIMATION = 7
        /// </summary>
        /// <param name="animationData">Raw animation data to send to BIOS</param>
        public bool SetLedAnimation(byte[] animationData)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set LED animation: WMI BIOS not available");
                return false;
            }

            try
            {
                _logging?.Info($"Setting LED animation ({animationData.Length} bytes): " +
                    $"[{string.Join(", ", animationData.Take(12).Select(b => $"0x{b:X2}"))}...]");
                    
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_ANIMATION_SET, animationData, 0);
                if (result != null)
                {
                    _logging?.Info("✓ LED animation applied successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set LED animation: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Set keyboard color table for 4-zone keyboards.
        /// OmenMon: Cmd.Keyboard, 0x03
        /// 
        /// ColorTable structure (128 bytes total, matching OmenMon):
        /// Byte 0:      ZoneCount (4)
        /// Bytes 1-24:  Padding (24 bytes, must be 0)
        /// Bytes 25-36: RGB colors for each zone (3 bytes per zone x 4 zones = 12 bytes)
        /// Bytes 37+:   Unused (0)
        /// 
        /// Zone order (per OmenMon):
        /// Zone 0 (Right):       Arrows, nav block, right modifiers
        /// Zone 1 (Middle):      Right QWERTY (F6-F12), T/G/B boundary  
        /// Zone 2 (Left):        Left QWERTY (F1-F5), R/F/V boundary
        /// Zone 3 (WASD):        W/A/S/D keys
        /// </summary>
        /// <param name="zoneColors">12-byte array: [R0,G0,B0,R1,G1,B1,R2,G2,B2,R3,G3,B3]</param>
        /// <param name="ensureBacklightOn">If true, ensures backlight is enabled first</param>
        public bool SetColorTable(byte[] zoneColors, bool ensureBacklightOn = true)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set color table: WMI BIOS not available");
                return false;
            }

            try
            {
                // Ensure backlight is on first (OmenMon behavior)
                if (ensureBacklightOn)
                {
                    _logging?.Info("SetColorTable: Ensuring backlight is ON before setting colors...");
                    SetBacklight(true);
                    System.Threading.Thread.Sleep(50); // Brief delay for hardware
                }
                
                // Build proper 128-byte ColorTable structure per OmenMon format
                var data = new byte[128];
                
                // Byte 0: Zone count (always 4 for standard 4-zone keyboards)
                data[0] = 4;
                
                // Bytes 1-24: Padding (leave as zeros)
                const int COLOR_TABLE_PAD = 24;
                
                // Bytes 25+: Zone colors (RGB per zone)
                // Input zoneColors should be 12 bytes: [R1,G1,B1,R2,G2,B2,R3,G3,B3,R4,G4,B4]
                int colorOffset = 1 + COLOR_TABLE_PAD; // Byte 25
                int colorsToCopy = Math.Min(zoneColors.Length, 12); // Max 4 zones x 3 bytes
                Array.Copy(zoneColors, 0, data, colorOffset, colorsToCopy);
                
                _logging?.Info($"SetColorTable: ZoneCount={data[0]}, Colors at offset {colorOffset}: " +
                    $"Z0=#{data[colorOffset]:X2}{data[colorOffset+1]:X2}{data[colorOffset+2]:X2}, " +
                    $"Z1=#{data[colorOffset+3]:X2}{data[colorOffset+4]:X2}{data[colorOffset+5]:X2}, " +
                    $"Z2=#{data[colorOffset+6]:X2}{data[colorOffset+7]:X2}{data[colorOffset+8]:X2}, " +
                    $"Z3=#{data[colorOffset+9]:X2}{data[colorOffset+10]:X2}{data[colorOffset+11]:X2}");

                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_COLOR_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Keyboard color table set (128-byte OmenMon format)");
                    return true;
                }
                else
                {
                    _logging?.Warn("SetColorTable: BIOS command returned null (may indicate failure)");
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set color table: {ex.Message}", ex);
            }
            return false;
        }
        
        /// <summary>
        /// Set a single keyboard zone color.
        /// Uses the same 128-byte ColorTable format as SetColorTable.
        /// </summary>
        public bool SetZoneColor(int zone, byte r, byte g, byte b)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set zone color: WMI BIOS not available");
                return false;
            }

            if (zone < 0 || zone > 3)
            {
                _logging?.Warn($"Invalid zone {zone}, must be 0-3");
                return false;
            }

            try
            {
                // Build proper 128-byte ColorTable structure
                // First get current colors so we only change the requested zone
                var currentColors = GetColorTable();
                
                var data = new byte[128];
                data[0] = 4; // Zone count
                
                const int COLOR_TABLE_PAD = 24;
                int colorOffset = 1 + COLOR_TABLE_PAD; // Byte 25
                
                // Copy existing colors if available
                if (currentColors != null && currentColors.Length >= 37)
                {
                    // Colors start at byte 25 in existing data
                    Array.Copy(currentColors, colorOffset, data, colorOffset, 12);
                }
                
                // Set the specific zone color
                int zoneOffset = colorOffset + (zone * 3);
                data[zoneOffset] = r;
                data[zoneOffset + 1] = g;
                data[zoneOffset + 2] = b;
                
                _logging?.Info($"SetZoneColor: Zone {zone} = R:{r} G:{g} B:{b} at offset {zoneOffset}");

                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_COLOR_SET, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Zone {zone} color set to R:{r} G:{g} B:{b}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set zone color: {ex.Message}", ex);
            }
            return false;
        }
        
        /// <summary>
        /// Get keyboard color table (for detecting current settings).
        /// OmenMon: Cmd.Keyboard, 0x02
        /// </summary>
        public byte[]? GetColorTable()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Keyboard, CMD_COLOR_GET, new byte[4], 128);
                if (result != null && result.Length > 0)
                {
                    _logging?.Info($"Got keyboard color table ({result.Length} bytes)");
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get color table: {ex.Message}");
            }
            return null;
        }
        
        #endregion

        /// <summary>
        /// Set idle mode (affects power management).
        /// OmenMon: Cmd.Default, 0x31
        /// </summary>
        public bool SetIdleMode(bool enabled)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set idle mode: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = (byte)(enabled ? 1 : 0);

                var result = SendBiosCommand(BiosCmd.Default, CMD_IDLE_SET, data, 4);
                if (result != null)
                {
                    _logging?.Info($"✓ Idle mode: {(enabled ? "enabled" : "disabled")}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set idle mode: {ex.Message}", ex);
            }
            return false;
        }

        #region Display Overdrive

        /// <summary>
        /// Get display overdrive status.
        /// Returns true if overdrive is currently enabled, false if disabled.
        /// Returns null if the command is not supported on this system.
        /// </summary>
        public bool? GetDisplayOverdrive()
        {
            if (!_isAvailable)
                return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_OVERDRIVE_GET, null, 4);
                if (result != null && result.Length >= 1)
                {
                    bool enabled = result[0] != 0;
                    _logging?.Info($"Display overdrive status: {(enabled ? "enabled" : "disabled")}");
                    return enabled;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Display overdrive query not supported: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Enable or disable display overdrive (panel response time optimization).
        /// Supported on OMEN laptops with 165Hz+ displays.
        /// </summary>
        public bool SetDisplayOverdrive(bool enabled)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set display overdrive: WMI BIOS not available");
                return false;
            }

            try
            {
                var data = new byte[4];
                data[0] = (byte)(enabled ? 1 : 0);

                var result = SendBiosCommand(BiosCmd.Default, CMD_OVERDRIVE_SET, data, 4);
                if (result != null)
                {
                    _logging?.Info($"✓ Display overdrive: {(enabled ? "enabled" : "disabled")}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set display overdrive: {ex.Message}", ex);
            }
            return false;
        }

        /// <summary>
        /// Check if the current system supports display overdrive control.
        /// </summary>
        public bool SupportsDisplayOverdrive()
        {
            var result = GetDisplayOverdrive();
            return result.HasValue;
        }

        #endregion

        /// <summary>
        /// Send a command to the BIOS via CIM/WMI using OmenMon's exact implementation.
        /// </summary>
        /// <param name="command">The BIOS command class (Default, Keyboard, Legacy, GpuMode)</param>
        /// <param name="commandType">The specific command type/ID</param>
        /// <param name="inData">Input data bytes (can be null)</param>
        /// <param name="outDataSize">Expected output size: 0, 4, 128, 1024, or 4096</param>
        /// <returns>Output data bytes or empty array on success, null on failure</returns>
        private byte[]? SendBiosCommand(BiosCmd command, uint commandType, byte[]? inData, byte outDataSize)
        {
            // Use legacy WMI if it was already determined to work
            if (_useLegacyWmi)
            {
                return SendBiosCommandLegacy(command, commandType, inData, outDataSize);
            }
            
            if (_cimSession == null || _biosMethods == null || _wmiCommandsDisabled)
                return null;
                
            // Initialize the output variable
            var outData = new byte[outDataSize];
            
            try
            {
                using CimInstance input = new(_biosData!);
                // Ensure Sign is set on each command (some BIOS versions require this)
                input.CimInstanceProperties["Sign"].Value = BiosSign;
                // Define the input arguments for the request
                // Must cast to uint for BIOS F.15+ compatibility (enum type causes "Invalid method Parameter(s)")
                input.CimInstanceProperties["Command"].Value = (uint)command;
                input.CimInstanceProperties["CommandType"].Value = commandType;

                if (inData == null)
                {
                    // Allow for a call with no data payload
                    input.CimInstanceProperties["Size"].Value = 0;
                }
                else
                {
                    input.CimInstanceProperties[BIOS_DATA_FIELD].Value = inData;
                    input.CimInstanceProperties["Size"].Value = inData.Length;
                }

                // Prepare the method parameters
                CimMethodParametersCollection methodParams = new();
                methodParams.Add(CimMethodParameter.Create("InData", input, Microsoft.Management.Infrastructure.CimType.Instance, CimFlags.In));

                // Set 5-second timeout to prevent UI freeze from WMI hangs
                var options = new CimOperationOptions
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                // Call the pertinent method depending on the data size
                // Note: InvokeMethod with CimInstance requires namespace-less overload
                // The options parameter works with the (namespace, class, method, params, options) signature
                CimMethodResult result = _cimSession.InvokeMethod(
                    BIOS_NAMESPACE, BIOS_METHOD_CLASS, BIOS_METHOD + Convert.ToString(outDataSize), methodParams, options);

                // Retrieve the resulting data
                using CimInstance? resultData = result.OutParameters["OutData"].Value as CimInstance;
                if (resultData != null)
                {
                    // Populate the output data variable
                    if (outDataSize != 0)
                    {
                        outData = resultData.CimInstanceProperties["Data"].Value as byte[] ?? outData;
                    }

                    // Get return code
                    var returnCode = Convert.ToInt32(resultData.CimInstanceProperties[BIOS_RETURN_CODE_FIELD].Value);

                    if (returnCode == 0)
                    {
                        _consecutiveFailures = 0; // Reset on success
                        TrackCommandResult(true);
                        return outData;
                    }
                    else
                    {
                        // Log error with more detail for GPU commands
                        string errorMsg = $"BIOS command {command}:{commandType:X2} returned code {returnCode}";
                        
                        // Add GPU power specific hints
                        if (commandType == CMD_GPU_SET_POWER)
                        {
                            errorMsg += " (GPU SetPower). Return codes: 1=NotImplemented, 2=InvalidArgs, 3=HardwareError, 4=NotSupported";
                        }
                        
                        LogThrottledError(errorMsg);
                        TrackCommandResult(false);
                    }
                }
            }
            catch (CimException ex)
            {
                // Log detailed CIM error for debugging BIOS F.15+ compatibility issues
                LogThrottledError($"CIM command failed: {ex.Message} (NativeErrorCode: {ex.NativeErrorCode}, StatusCode: {ex.StatusCode})");
                _consecutiveFailures++;
                TrackCommandResult(false);
            }
            catch (Exception ex)
            {
                LogThrottledError($"BIOS command failed: {ex.Message}");
                _consecutiveFailures++;
                TrackCommandResult(false);
            }
            
            // Check if we should disable WMI commands
            if (_consecutiveFailures >= MaxConsecutiveFailures && !_wmiCommandsDisabled)
            {
                _wmiCommandsDisabled = true;
                _logging?.Warn($"WMI BIOS commands disabled after {MaxConsecutiveFailures} consecutive failures.");
            }
            
            // Try legacy WMI as fallback for BIOS F.15+ compatibility
            if (!_useLegacyWmi)
            {
                _logging?.Info("CIM failed, trying legacy System.Management WMI...");
                var legacyResult = SendBiosCommandLegacy(command, commandType, inData, outDataSize);
                if (legacyResult != null)
                {
                    _useLegacyWmi = true; // Use legacy from now on
                    _wmiCommandsDisabled = false;
                    _consecutiveFailures = 0;
                    _logging?.Info("✓ Legacy WMI fallback successful - switching to legacy mode");
                    return legacyResult;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Legacy System.Management WMI fallback for BIOS F.15+ compatibility.
        /// Some newer BIOS versions don't work with Microsoft.Management.Infrastructure CIM.
        /// </summary>
        private byte[]? SendBiosCommandLegacy(BiosCmd command, uint commandType, byte[]? inData, byte outDataSize)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\wmi");
                scope.Connect();
                
                // Get the BIOS methods instance
                using var methodClass = new ManagementClass(scope, new ManagementPath("hpqBIntM"), null);
                using var instances = methodClass.GetInstances();
                
                ManagementObject? biosInstance = null;
                foreach (ManagementObject obj in instances)
                {
                    if (obj["InstanceName"]?.ToString()?.Contains(BIOS_METHOD_INSTANCE) == true)
                    {
                        biosInstance = obj;
                        break;
                    }
                    obj.Dispose();
                }
                
                if (biosInstance == null)
                {
                    _logging?.Debug("Legacy WMI: BIOS instance not found");
                    return null;
                }
                
                using (biosInstance)
                {
                    // Create input data object
                    using var dataClass = new ManagementClass(scope, new ManagementPath("hpqBDataIn"), null);
                    using var inputData = dataClass.CreateInstance();
                    
                    inputData["Sign"] = BiosSign;
                    inputData["Command"] = (uint)command;
                    inputData["CommandType"] = commandType;
                    inputData["Size"] = (uint)(inData?.Length ?? 0);
                    if (inData != null)
                    {
                        inputData["hpqBData"] = inData;
                    }
                    
                    // Invoke method
                    string methodName = $"hpqBIOSInt{outDataSize}";
                    var inParams = biosInstance.GetMethodParameters(methodName);
                    inParams["InData"] = inputData;
                    
                    var outParams = biosInstance.InvokeMethod(methodName, inParams, null);
                    
                    if (outParams != null)
                    {
                        var outDataObj = outParams["OutData"] as ManagementBaseObject;
                        if (outDataObj != null)
                        {
                            var returnCode = Convert.ToInt32(outDataObj["rwReturnCode"]);
                            if (returnCode == 0)
                            {
                                TrackCommandResult(true);
                                if (outDataSize > 0)
                                {
                                    return outDataObj["Data"] as byte[] ?? new byte[outDataSize];
                                }
                                return new byte[0];
                            }
                            _logging?.Debug($"Legacy WMI: Command returned code {returnCode}");
                            TrackCommandResult(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Debug($"Legacy WMI fallback failed: {ex.Message}");
                TrackCommandResult(false);
            }
            
            return null;
        }
        
        /// <summary>
        /// Log errors with throttling to prevent log spam during repeated failures.
        /// </summary>
        private void LogThrottledError(string message)
        {
            _errorCount++;
            var now = DateTime.Now;
            if ((now - _lastErrorLog).TotalSeconds >= ErrorLogIntervalSeconds)
            {
                if (_errorCount > 1)
                {
                    _logging?.Warn($"{message} (repeated {_errorCount}x in last {ErrorLogIntervalSeconds}s)");
                }
                else
                {
                    _logging?.Warn(message);
                }
                _lastErrorLog = now;
                _errorCount = 0;
            }
        }

        /// <summary>
        /// Extend the fan countdown timer to prevent BIOS from reverting to default mode.
        /// HP OMEN BIOS automatically reverts fan settings after 120 seconds without this.
        /// This method should be called periodically (every 60-90 seconds) when using custom fan modes.
        /// 
        /// Uses WMI BIOS idle command to reset the countdown.
        /// </summary>
        public void ExtendFanCountdown()
        {
            if (!_isAvailable) return;
            
            try
            {
                // OmenMon extends countdown by either:
                // 1. Writing to EC register 0x63 (XFCD) directly
                // 2. Or by re-issuing the SetFanLevel command with current values
                
                // Method 1: Try to get current fan level and re-apply it
                var currentLevel = GetFanLevel();
                if (currentLevel.HasValue)
                {
                    var data = new byte[4];
                    data[0] = currentLevel.Value.fan1;
                    data[1] = currentLevel.Value.fan2;
                    data[2] = 0x00;
                    data[3] = 0x00;
                    
                    var result = SendBiosCommand(BiosCmd.Default, CMD_FAN_SET_LEVEL, data, 0);
                    if (result != null)
                    {
                        _logging?.Debug("Extended fan countdown via SetFanLevel");
                        return;
                    }
                }
                
                // Method 2: If that fails, try SetIdle(false) which can also reset the timer
                var idleResult = SetIdleMode(false);
                if (idleResult)
                {
                    _logging?.Debug("Extended fan countdown via SetIdleMode(false)");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan countdown: {ex.Message}");
            }
        }

        #region Battery Care (Charge Limit)
        
        /// <summary>
        /// Battery care mode setting for limiting charge to preserve battery health.
        /// </summary>
        public enum BatteryCareMode : byte
        {
            /// <summary>Charge to 100% (default)</summary>
            Disabled = 0x00,
            /// <summary>Limit charge to ~80% for longevity</summary>
            Enabled = 0x01
        }
        
        /// <summary>
        /// Get current battery care mode (charge limit status).
        /// </summary>
        /// <returns>True if charge limit is enabled (~80%), False if full charge, null if unavailable</returns>
        public bool? GetBatteryCareMode()
        {
            if (!_isAvailable) return null;
            
            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_BATTERY_CARE, new byte[4], 4);
                if (result != null && result.Length >= 1)
                {
                    var enabled = result[0] == (byte)BatteryCareMode.Enabled;
                    _logging?.Info($"Battery care mode: {(enabled ? "Enabled (80%)" : "Disabled (100%)")}");
                    return enabled;
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get battery care mode: {ex.Message}");
            }
            return null;
        }
        
        /// <summary>
        /// Set battery care mode (charge limit).
        /// When enabled, battery will only charge to ~80% to preserve longevity.
        /// </summary>
        /// <param name="enabled">True to limit to 80%, False for full charge</param>
        /// <returns>True if successful</returns>
        public bool SetBatteryCareMode(bool enabled)
        {
            if (!_isAvailable)
            {
                _logging?.Warn("Cannot set battery care mode: WMI BIOS not available");
                return false;
            }
            
            try
            {
                var data = new byte[4];
                data[0] = enabled ? (byte)BatteryCareMode.Enabled : (byte)BatteryCareMode.Disabled;
                data[1] = 0x00;
                data[2] = 0x00;
                data[3] = 0x00;
                
                var result = SendBiosCommand(BiosCmd.Default, CMD_BATTERY_CARE, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ Battery care mode set: {(enabled ? "Enabled (80%)" : "Disabled (100%)")}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to set battery care mode: {ex.Message}", ex);
            }
            return false;
        }
        
        #endregion

        #region Helper Methods

        /// <summary>
        /// Get system model name from WMI for model-based detection.
        /// </summary>
        private string? GetModelName()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    return obj["Model"]?.ToString();
                }
            }
            catch
            {
                // Ignore errors - fallback to BIOS-reported version
            }
            return null;
        }

        #endregion
        
        #region BIOS Reliability Tracking (v2.7.0)
        
        /// <summary>
        /// Track a command result for reliability statistics.
        /// </summary>
        private void TrackCommandResult(bool success)
        {
            lock (_statsLock)
            {
                _totalCommandAttempts++;
                if (success)
                    _totalCommandSuccesses++;
                else
                    _totalCommandFailures++;
            }
        }
        
        /// <summary>
        /// Get BIOS query reliability statistics.
        /// </summary>
        public BiosReliabilityStats GetReliabilityStats()
        {
            lock (_statsLock)
            {
                return new BiosReliabilityStats
                {
                    TotalAttempts = _totalCommandAttempts,
                    Successes = _totalCommandSuccesses,
                    Failures = _totalCommandFailures,
                    SuccessRate = _totalCommandAttempts > 0 
                        ? (double)_totalCommandSuccesses / _totalCommandAttempts * 100.0 
                        : 0,
                    ConsecutiveFailures = _consecutiveFailures,
                    HeartbeatHealth = _heartbeatHealth,
                    HeartbeatAge = HeartbeatAge,
                    StatsResetTime = _statsResetTime,
                    StatsDuration = DateTime.Now - _statsResetTime,
                    IsUsingLegacyWmi = _useLegacyWmi,
                    WmiCommandsDisabled = _wmiCommandsDisabled
                };
            }
        }
        
        /// <summary>
        /// Reset reliability statistics (e.g., after a manual reset or mode change).
        /// </summary>
        public void ResetReliabilityStats()
        {
            lock (_statsLock)
            {
                _totalCommandAttempts = 0;
                _totalCommandSuccesses = 0;
                _totalCommandFailures = 0;
                _statsResetTime = DateTime.Now;
                _logging?.Info("BIOS reliability stats reset");
            }
        }
        
        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                StopHeartbeat();
                _biosData?.Dispose();
                _biosMethods?.Dispose();
                _cimSession?.Dispose();
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// WMI heartbeat health status.
    /// </summary>
    public enum WmiHeartbeatHealth
    {
        /// <summary>Status not yet determined.</summary>
        Unknown,
        
        /// <summary>Heartbeat working normally.</summary>
        Healthy,
        
        /// <summary>Occasional failures but still functional.</summary>
        Degraded,
        
        /// <summary>Multiple consecutive failures - WMI commands may not work.</summary>
        Failing
    }
    
    /// <summary>
    /// BIOS WMI query reliability statistics (v2.7.0).
    /// </summary>
    public class BiosReliabilityStats
    {
        /// <summary>Total number of WMI command attempts.</summary>
        public long TotalAttempts { get; set; }
        
        /// <summary>Number of successful commands.</summary>
        public long Successes { get; set; }
        
        /// <summary>Number of failed commands.</summary>
        public long Failures { get; set; }
        
        /// <summary>Success rate as percentage (0-100).</summary>
        public double SuccessRate { get; set; }
        
        /// <summary>Current consecutive failure count.</summary>
        public int ConsecutiveFailures { get; set; }
        
        /// <summary>Heartbeat health status.</summary>
        public WmiHeartbeatHealth HeartbeatHealth { get; set; }
        
        /// <summary>Time since last successful heartbeat.</summary>
        public TimeSpan HeartbeatAge { get; set; }
        
        /// <summary>When statistics tracking started.</summary>
        public DateTime StatsResetTime { get; set; }
        
        /// <summary>Duration of statistics tracking.</summary>
        public TimeSpan StatsDuration { get; set; }
        
        /// <summary>Whether legacy WMI is being used.</summary>
        public bool IsUsingLegacyWmi { get; set; }
        
        /// <summary>Whether WMI commands have been disabled due to failures.</summary>
        public bool WmiCommandsDisabled { get; set; }
        
        /// <summary>Overall health rating based on success rate.</summary>
        public string HealthRating => SuccessRate switch
        {
            >= 99 => "Excellent",
            >= 95 => "Good",
            >= 80 => "Fair",
            >= 50 => "Poor",
            _ => "Critical"
        };
        
        /// <summary>Summary text for UI display.</summary>
        public string Summary => $"{Successes}/{TotalAttempts} commands ({SuccessRate:F1}% success, {HealthRating})";
    }
}
