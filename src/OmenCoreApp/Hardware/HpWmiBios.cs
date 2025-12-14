using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Management.Infrastructure;
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
    /// </summary>
    public class HpWmiBios : IDisposable
    {
        private readonly LoggingService? _logging;
        private bool _isAvailable;
        private bool _disposed;
        
        // CIM session for WMI access (same as OmenMon)
        private CimSession? _cimSession;
        private CimInstance? _biosData;
        private CimInstance? _biosMethods;
        
        // Error throttling to reduce log spam
        private DateTime _lastErrorLog = DateTime.MinValue;
        private int _errorCount = 0;
        private const int ErrorLogIntervalSeconds = 30;
        
        // Track WMI command failures
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 5;
        private bool _wmiCommandsDisabled = false;
        
        // Heartbeat timer for 2023+ models
        private Timer? _heartbeatTimer;
        private const int HeartbeatIntervalMs = 60000; // 60 seconds
        private bool _heartbeatEnabled = false;

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
        private const uint CMD_FAN_GET_TYPE = 0x2C;   // GetFanType
        private const uint CMD_FAN_MODE_SET = 0x1A;   // SetFanMode (OmenMon 0x1A)
        private const uint CMD_FAN_MAX_GET = 0x26;    // GetMaxFan (OmenMon 0x26)
        private const uint CMD_FAN_MAX_SET = 0x27;    // SetMaxFan (OmenMon 0x27)
        private const uint CMD_FAN_GET_TABLE = 0x2F;  // GetFanTable
        private const uint CMD_FAN_SET_TABLE = 0x32;  // SetFanTable
        private const uint CMD_SYSTEM_GET_DATA = 0x28;
        private const uint CMD_GPU_GET_POWER = 0x21;  // GetGpuPower (OmenMon 0x21)
        private const uint CMD_GPU_SET_POWER = 0x22;  // SetGpuPower (OmenMon 0x22)
        private const uint CMD_GPU_GET_MODE = 0x52;   // GetGpuMode - uses Legacy cmd
        private const uint CMD_GPU_SET_MODE = 0x52;   // SetGpuMode - uses GpuMode cmd
        private const uint CMD_TEMP_GET = 0x23;       // GetTemperature (OmenMon 0x23)
        private const uint CMD_BACKLIGHT_GET = 0x04;  // GetBacklight - uses Keyboard cmd
        private const uint CMD_BACKLIGHT_SET = 0x05;  // SetBacklight - uses Keyboard cmd
        private const uint CMD_COLOR_GET = 0x02;      // GetColorTable - uses Keyboard cmd
        private const uint CMD_COLOR_SET = 0x03;      // SetColorTable - uses Keyboard cmd
        private const uint CMD_THROTTLE_GET = 0x35;
        private const uint CMD_IDLE_SET = 0x31;       // SetIdle (OmenMon 0x31)
        
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
        /// </summary>
        public enum GpuPowerLevel : byte
        {
            Minimum = 0x00,  // Base TGP only
            Medium = 0x01,   // Custom TGP
            Maximum = 0x02   // Custom TGP + PPAB
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
            V1 = 0x01   // Current devices (Default/Performance/Cool)
        }

        public bool IsAvailable => _isAvailable;
        public string Status { get; private set; } = "Not initialized";
        public ThermalPolicyVersion ThermalPolicy { get; private set; } = ThermalPolicyVersion.V1;
        public int FanCount { get; private set; } = 2;
        public bool HeartbeatEnabled => _heartbeatEnabled;

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
        /// </summary>
        public void StartHeartbeat()
        {
            if (_heartbeatEnabled || !_isAvailable) return;
            
            _heartbeatTimer = new Timer(HeartbeatCallback, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
            _heartbeatEnabled = true;
            _logging?.Info($"✓ WMI BIOS heartbeat started (every {HeartbeatIntervalMs/1000}s)");
        }
        
        /// <summary>
        /// Stop the heartbeat timer.
        /// </summary>
        public void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _heartbeatEnabled = false;
            _logging?.Info("WMI BIOS heartbeat stopped");
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
                    _logging?.Warn("WMI BIOS heartbeat failed - commands may stop working");
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"WMI BIOS heartbeat error: {ex.Message}");
            }
        }

        /// <summary>
        /// Query system data and validate WMI commands work.
        /// Returns true if commands work, false otherwise.
        /// </summary>
        private bool QuerySystemData()
        {
            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_SYSTEM_GET_DATA, null, 128);
                if (result != null && result.Length >= 9)
                {
                    ThermalPolicy = (ThermalPolicyVersion)result[3];
                    _logging?.Info($"  Thermal Policy: V{(int)ThermalPolicy}");

                    // Query fan count
                    var fanResult = SendBiosCommand(BiosCmd.Default, CMD_FAN_GET_COUNT, new byte[4], 4);
                    if (fanResult != null && fanResult.Length >= 1)
                    {
                        FanCount = fanResult[0];
                        _logging?.Info($"  Fan Count: {FanCount}");
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
        /// OmenMon: Cmd.Default, 0x2D
        /// </summary>
        public (byte fan1, byte fan2)? GetFanLevel()
        {
            if (!_isAvailable) return null;

            try
            {
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
        /// Get BIOS temperature sensor reading.
        /// OmenMon: Cmd.Default, 0x23, {0x01, 0, 0, 0}
        /// </summary>
        public int? GetTemperature()
        {
            if (!_isAvailable) return null;

            try
            {
                var result = SendBiosCommand(BiosCmd.Default, CMD_TEMP_GET, new byte[4] { 0x01, 0x00, 0x00, 0x00 }, 4);
                if (result != null && result.Length >= 1)
                {
                    return result[0];
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get temperature: {ex.Message}");
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
                        data[1] = 1; // PPAB on
                        break;
                }
                data[2] = 0x01; // DState = D1
                data[3] = 0x00; // PeakTemperature

                var result = SendBiosCommand(BiosCmd.Default, CMD_GPU_SET_POWER, data, 0);
                if (result != null)
                {
                    _logging?.Info($"✓ GPU power set to: {level}");
                    return true;
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
            if (_cimSession == null || _biosMethods == null || _wmiCommandsDisabled)
                return null;
                
            // Initialize the output variable
            var outData = new byte[outDataSize];
            
            try
            {
                using (CimInstance input = new CimInstance(_biosData!))
                {
                    // Define the input arguments for the request
                    input.CimInstanceProperties["Command"].Value = command;
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
                    
                    // Call the pertinent method depending on the data size
                    CimMethodResult result = _cimSession.InvokeMethod(
                        _biosMethods, BIOS_METHOD + Convert.ToString(outDataSize), methodParams);
                    
                    // Retrieve the resulting data
                    using (CimInstance? resultData = result.OutParameters["OutData"].Value as CimInstance)
                    {
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
                                return outData;
                            }
                            else
                            {
                                // Log error but don't spam
                                LogThrottledError($"BIOS command {command}:{commandType:X2} returned code {returnCode}");
                            }
                        }
                    }
                }
            }
            catch (CimException ex)
            {
                LogThrottledError($"CIM command failed: {ex.Message}");
                _consecutiveFailures++;
            }
            catch (Exception ex)
            {
                LogThrottledError($"BIOS command failed: {ex.Message}");
                _consecutiveFailures++;
            }
            
            // Check if we should disable WMI commands
            if (_consecutiveFailures >= MaxConsecutiveFailures && !_wmiCommandsDisabled)
            {
                _wmiCommandsDisabled = true;
                _logging?.Warn($"WMI BIOS commands disabled after {MaxConsecutiveFailures} consecutive failures.");
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
        /// <returns>True if successful</returns>
        public bool ExtendFanCountdown()
        {
            if (!_isAvailable) return false;
            
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
                        return true;
                    }
                }
                
                // Method 2: If that fails, try SetIdle(false) which can also reset the timer
                return SetIdleMode(false);
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to extend fan countdown: {ex.Message}");
                return false;
            }
        }

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
}
