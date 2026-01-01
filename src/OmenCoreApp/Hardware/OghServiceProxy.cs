using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using OmenCore.Services;

namespace OmenCore.Hardware
{
    /// <summary>
    /// OMEN Gaming Hub Service Proxy - provides hardware control through OGH's WMI interface.
    /// 
    /// Some 2023+ OMEN laptops require the OGH background services to be running for
    /// WMI BIOS commands to function. This proxy detects if OGH is available and uses
    /// the hpCpsPubGetSetCommand interface when direct BIOS commands fail.
    /// 
    /// Services detected:
    /// - HPOmenCap (HP Omen HSA Service)
    /// - OmenCommandCenterBackground (main command processor)
    /// - OmenCap (capability service)
    /// - omenmqtt (messaging service)
    /// </summary>
    public class OghServiceProxy : IDisposable
    {
        private readonly LoggingService? _logging;
        private bool _disposed;
        private ManagementObject? _oghInterface;
        
        private const string WmiNamespace = @"root\WMI";
        private const string OghCommandClass = "hpCpsPubGetSetCommand";
        
        // Error throttling to reduce log spam
        private readonly Dictionary<string, DateTime> _lastErrorLog = new();
        private readonly Dictionary<string, int> _errorCounts = new();
        private const int ErrorLogIntervalSeconds = 60; // Only log same error every 60 seconds
        
        // Known OGH service names
        private static readonly string[] OghServiceNames = new[]
        {
            "HPOmenCap",           // HP Omen HSA Service
            "HPOmenCommandCenter", // Command Center Service (older)
        };
        
        // Known OGH process names
        private static readonly string[] OghProcessNames = new[]
        {
            "OmenCommandCenterBackground",
            "OmenCap",
            "omenmqtt",
            "OmenInstallMonitor",
            "OMEN Command Center"
        };

        // OGH command signatures (reverse-engineered from OGH)
        private const uint OGH_SIGNATURE = 0x4F4D454E; // "OMEN"
        
        /// <summary>
        /// Thermal policies supported by OGH.
        /// </summary>
        public enum ThermalPolicy
        {
            Default = 0,      // Balanced thermal profile
            Performance = 1,  // High performance, higher temps allowed
            Cool = 2,         // Cooler operation, may throttle
            L5P = 3           // Legacy OMEN 5 Pro mode
        }

        /// <summary>
        /// Status of OMEN Gaming Hub services.
        /// </summary>
        public class OghStatus
        {
            public bool IsInstalled { get; set; }
            public bool IsRunning { get; set; }
            public bool WmiAvailable { get; set; }
            public bool CommandsWork { get; set; }
            public string[] RunningServices { get; set; } = Array.Empty<string>();
            public string[] RunningProcesses { get; set; } = Array.Empty<string>();
            public string Message { get; set; } = "";
        }

        public OghStatus Status { get; private set; } = new();
        public bool IsAvailable => Status.WmiAvailable && Status.CommandsWork;

        public OghServiceProxy(LoggingService? logging = null)
        {
            _logging = logging;
            DetectOghStatus();
        }

        /// <summary>
        /// Detect the status of OMEN Gaming Hub services and WMI interface.
        /// </summary>
        public void DetectOghStatus()
        {
            Status = new OghStatus();
            
            try
            {
                // Check for OGH services using ServiceController (more reliable than WMI)
                var runningServices = new System.Collections.Generic.List<string>();
                foreach (var serviceName in OghServiceNames)
                {
                    try
                    {
                        using var sc = new System.ServiceProcess.ServiceController(serviceName);
                        // Only count if service exists AND is running
                        if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                        {
                            Status.IsInstalled = true;
                            runningServices.Add(serviceName);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Service doesn't exist - this is expected after uninstall
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Service doesn't exist or access denied
                    }
                }
                Status.RunningServices = runningServices.ToArray();
                
                // Check for OGH processes (only if services were found running)
                var runningProcesses = new System.Collections.Generic.List<string>();
                if (runningServices.Count > 0)
                {
                    var allProcesses = Process.GetProcesses();
                    foreach (var processName in OghProcessNames)
                    {
                        if (allProcesses.Any(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                        {
                            runningProcesses.Add(processName);
                        }
                    }
                    foreach (var p in allProcesses) p.Dispose();
                }
                Status.RunningProcesses = runningProcesses.ToArray();
                
                // OGH is only truly "running" if services are actually running
                Status.IsRunning = runningServices.Count > 0;
                
                // Log detection results
                if (Status.IsRunning)
                {
                    _logging?.Info($"OMEN Gaming Hub detected:");
                    if (Status.RunningServices.Length > 0)
                        _logging?.Info($"  Services: {string.Join(", ", Status.RunningServices)}");
                    if (Status.RunningProcesses.Length > 0)
                        _logging?.Info($"  Processes: {string.Join(", ", Status.RunningProcesses)}");
                }
                else
                {
                    _logging?.Info("OMEN Gaming Hub not detected (services not running)");
                }
                
                // Check for OGH WMI interface
                if (Status.IsRunning)
                {
                    CheckOghWmiInterface();
                }
                
                // Build status message
                if (Status.CommandsWork)
                {
                    Status.Message = "OGH proxy available - fan control enabled through Gaming Hub services";
                }
                else if (Status.WmiAvailable)
                {
                    Status.Message = "OGH WMI interface found but commands not functional";
                }
                else if (Status.IsRunning)
                {
                    Status.Message = "OGH services running but WMI interface not accessible (need admin)";
                }
                else if (Status.IsInstalled)
                {
                    Status.Message = "OGH installed but services not running";
                }
                else
                {
                    Status.Message = "OGH not installed - direct BIOS control may be available";
                }
                
                _logging?.Info($"OGH Status: {Status.Message}");
            }
            catch (Exception ex)
            {
                _logging?.Error($"Error detecting OGH status: {ex.Message}", ex);
                Status.Message = $"Detection error: {ex.Message}";
            }
        }

        private void CheckOghWmiInterface()
        {
            try
            {
                // Check if the hpCpsPubGetSetCommand class exists
                using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {OghCommandClass}");
                var results = searcher.Get();
                
                foreach (ManagementObject obj in results)
                {
                    _oghInterface = obj;
                    Status.WmiAvailable = true;
                    _logging?.Info($"  OGH WMI interface found: {OghCommandClass}");
                    break;
                }
                
                if (Status.WmiAvailable && _oghInterface != null)
                {
                    // Test if commands work
                    Status.CommandsWork = TestOghCommands();
                    
                    if (!Status.CommandsWork)
                    {
                        _logging?.Info("");
                        _logging?.Info("╔══════════════════════════════════════════════════════════════════════╗");
                        _logging?.Info("║  OGH PROXY NOTE:                                                      ║");
                        _logging?.Info("║  The OGH WMI interface exists but commands are not working.           ║");
                        _logging?.Info("║  This is expected if OGH services were cleaned up.                    ║");
                        _logging?.Info("║                                                                       ║");
                        _logging?.Info("║  On 2023+ OMEN models (13th gen+), fan control options:               ║");
                        _logging?.Info("║  1. Install OMEN Gaming Hub (recommended)                             ║");
                        _logging?.Info("║  2. Use direct WMI BIOS (HpWmiBios - auto-detected)                   ║");
                        _logging?.Info("║  3. Use TCC offset for temperature limiting (available in v1.1.1+)    ║");
                        _logging?.Info("╚══════════════════════════════════════════════════════════════════════╝");
                        _logging?.Info("");
                    }
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
            {
                _logging?.Warn("OGH WMI interface requires administrator access");
                Status.Message = "OGH WMI requires administrator privileges";
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Could not access OGH WMI interface: {ex.Message}");
            }
        }

        private bool TestOghCommands()
        {
            // Test multiple commands to verify the interface works for this model.
            // Different OMEN models support different command sets.
            if (_oghInterface == null || !Status.IsRunning)
                return false;
                
            // Commands to try, in order of preference
            var testCommands = new[]
            {
                ("Fan:GetData", "Fan data"),
                ("SystemData:Get", "System data"),
                ("GPU:GetMode", "GPU mode"),
                ("Thermal:GetPolicy", "Thermal policy"),
                ("Backlight:Get", "Keyboard backlight")
            };
            
            foreach (var (cmd, _) in testCommands)
            {
                try
                {
                    var result = ExecuteOghCommandSilent(cmd);
                    if (result != null)
                    {
                        _logging?.Info($"  OGH command test successful ({cmd}) ✓");
                        return true;
                    }
                }
                catch
                {
                    // Continue to next command
                }
            }
            
            // Even if specific commands fail, the interface might still be usable
            // for other commands. Check if the WMI class exists and responds.
            try
            {
                var inParams = _oghInterface.GetMethodParameters("hpCpsPubGetCommand");
                if (inParams != null)
                {
                    _logging?.Info("  OGH WMI interface responsive (commands may vary by model)");
                    return true; // Interface works, just maybe not for fan commands
                }
            }
            catch { }
            
            _logging?.Warn("  OGH command tests failed - interface may not be functional");
            return false;
        }
        
        /// <summary>
        /// Execute OGH command without logging errors (for testing).
        /// </summary>
        private byte[]? ExecuteOghCommandSilent(string command)
        {
            if (!Status.WmiAvailable || _oghInterface == null)
                return null;
                
            try
            {
                var inParams = _oghInterface.GetMethodParameters("hpCpsPubGetCommand");
                inParams["Command"] = command;
                inParams["SignIn"] = OGH_SIGNATURE;
                
                var outParams = _oghInterface.InvokeMethod("hpCpsPubGetCommand", inParams, null);
                
                if (outParams != null)
                {
                    var returnCode = (uint)outParams["ReturnCode"];
                    if (returnCode == 0)
                    {
                        return outParams["hpqBDataOut"] as byte[];
                    }
                }
            }
            catch { }
            
            return null;
        }
        
        /// <summary>
        /// Log errors with throttling to prevent log spam during repeated failures.
        /// </summary>
        private void LogThrottledWarning(string command, string message)
        {
            var key = $"{command}:{message}";
            var now = DateTime.Now;
            
            if (!_errorCounts.ContainsKey(key))
                _errorCounts[key] = 0;
            if (!_lastErrorLog.ContainsKey(key))
                _lastErrorLog[key] = DateTime.MinValue;
                
            _errorCounts[key]++;
            
            if ((now - _lastErrorLog[key]).TotalSeconds >= ErrorLogIntervalSeconds)
            {
                if (_errorCounts[key] > 1)
                {
                    _logging?.Warn($"{message} (repeated {_errorCounts[key]}x)");
                }
                else
                {
                    _logging?.Warn(message);
                }
                _lastErrorLog[key] = now;
                _errorCounts[key] = 0;
            }
        }

        /// <summary>
        /// Execute a command through the OGH WMI interface.
        /// </summary>
        public byte[]? ExecuteOghCommand(string command, byte[]? inputData)
        {
            if (!Status.WmiAvailable || _oghInterface == null)
                return null;
                
            try
            {
                var inParams = _oghInterface.GetMethodParameters("hpCpsPubGetCommand");
                inParams["Command"] = command;
                inParams["SignIn"] = OGH_SIGNATURE;
                
                var outParams = _oghInterface.InvokeMethod("hpCpsPubGetCommand", inParams, null);
                
                if (outParams != null)
                {
                    var returnCode = (uint)outParams["ReturnCode"];
                    if (returnCode == 0)
                    {
                        return outParams["hpqBDataOut"] as byte[];
                    }
                    else
                    {
                        // Use throttled logging to avoid spam
                        LogThrottledWarning(command, $"OGH command '{command}' returned error code: {returnCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"OGH command execution failed: {ex.Message}", ex);
            }
            
            return null;
        }

        /// <summary>
        /// Set a value through the OGH WMI interface.
        /// </summary>
        public bool ExecuteOghSetCommand(string command, byte[] inputData)
        {
            if (!Status.WmiAvailable || _oghInterface == null)
                return false;
                
            try
            {
                var inParams = _oghInterface.GetMethodParameters("hpCpsPubSetCommand");
                inParams["Command"] = command;
                inParams["SignIn"] = OGH_SIGNATURE;
                inParams["DataSizeIn"] = (uint)inputData.Length;
                inParams["hpqBDataIn"] = inputData;
                
                var outParams = _oghInterface.InvokeMethod("hpCpsPubSetCommand", inParams, null);
                
                if (outParams != null)
                {
                    var returnCode = (uint)outParams["ReturnCode"];
                    if (returnCode == 0)
                    {
                        _logging?.Info($"OGH command '{command}' executed successfully");
                        return true;
                    }
                    else
                    {
                        // Use throttled logging for set command errors too
                        LogThrottledWarning(command, $"OGH command '{command}' returned error code: {returnCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Error($"OGH set command execution failed: {ex.Message}", ex);
            }
            
            return false;
        }

        /// <summary>
        /// Try to set fan mode through OGH if it's available.
        /// </summary>
        public bool SetFanMode(string mode)
        {
            // Map mode names to OGH command format
            var command = mode.ToLower() switch
            {
                "performance" => "FanControl:SetMode:Performance",
                "balanced" or "default" => "FanControl:SetMode:Balanced",
                "quiet" or "cool" => "FanControl:SetMode:Quiet",
                "max" or "maximum" => "FanControl:SetMode:Maximum",
                _ => $"FanControl:SetMode:{mode}"
            };
            
            return ExecuteOghSetCommand(command, new byte[0]);
        }

        /// <summary>
        /// Set thermal policy through OGH.
        /// </summary>
        public bool SetThermalPolicy(ThermalPolicy policy)
        {
            var policyName = policy switch
            {
                ThermalPolicy.Performance => "Performance",
                ThermalPolicy.Cool => "Cool",
                ThermalPolicy.L5P => "L5P",
                _ => "Default"
            };
            
            _logging?.Info($"Setting OGH thermal policy: {policyName}");
            
            // Try multiple command formats that OGH may support
            var commands = new[]
            {
                $"Thermal:SetPolicy:{policyName}",
                $"ThermalPolicy:Set:{policyName}",
                $"FanControl:SetMode:{policyName}"
            };
            
            foreach (var cmd in commands)
            {
                if (ExecuteOghSetCommand(cmd, new byte[] { (byte)policy }))
                    return true;
            }
            
            // Fallback: try SetFanMode
            return SetFanMode(policyName);
        }

        /// <summary>
        /// Enable or disable maximum fan speed through OGH.
        /// </summary>
        public bool SetMaxFan(bool enabled)
        {
            _logging?.Info($"Setting OGH max fan: {(enabled ? "enabled" : "disabled")}");
            
            var commands = new[]
            {
                enabled ? "FanControl:SetMax:Enable" : "FanControl:SetMax:Disable",
                enabled ? "Fan:MaxSpeed:On" : "Fan:MaxSpeed:Off",
                $"FanControl:SetMode:{(enabled ? "Maximum" : "Balanced")}"
            };
            
            foreach (var cmd in commands)
            {
                if (ExecuteOghSetCommand(cmd, new byte[] { enabled ? (byte)1 : (byte)0 }))
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Get fan telemetry data from OGH.
        /// </summary>
        public OmenCore.Models.FanTelemetry[]? GetFanData()
        {
            try
            {
                var result = ExecuteOghCommand("Fan:GetData", null);
                if (result != null && result.Length >= 8)
                {
                    // Parse OGH fan data format (varies by model)
                    // Typical format: [Fan1RPM_Low, Fan1RPM_High, Fan1Duty, Fan2RPM_Low, Fan2RPM_High, Fan2Duty, ...]
                    var fans = new System.Collections.Generic.List<OmenCore.Models.FanTelemetry>();
                    
                    for (int i = 0; i + 2 < result.Length; i += 3)
                    {
                        int rpm = result[i] | (result[i + 1] << 8);
                        int duty = result[i + 2];
                        
                        if (rpm > 0 || duty > 0)
                        {
                            fans.Add(new OmenCore.Models.FanTelemetry
                            {
                                Name = $"Fan {fans.Count + 1}",
                                SpeedRpm = rpm,
                                DutyCyclePercent = duty,
                                Temperature = 0 // Temperature not in this data
                            });
                        }
                    }
                    
                    if (fans.Count > 0)
                        return fans.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get OGH fan data: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Try to get GPU mode through OGH if it's available.
        /// </summary>
        public string? GetGpuMode()
        {
            var result = ExecuteOghCommand("GPU:GetMode", null);
            if (result != null && result.Length > 0)
            {
                return result[0] switch
                {
                    0 => "Hybrid",
                    1 => "Discrete",
                    2 => "Optimus",
                    _ => $"Unknown ({result[0]})"
                };
            }
            return null;
        }

        /// <summary>
        /// Get GPU power level through OGH.
        /// </summary>
        public (bool success, int level, string levelName) GetGpuPowerLevel()
        {
            try
            {
                // Try multiple command formats used by different OGH versions
                var commands = new[] 
                { 
                    "GPU:GetPower",           // Standard format
                    "GPUPower:Get",           // Alternative format  
                    "SystemData:GPUPower",    // System data format
                    "Gpu",                    // Simple format (OmenMon style)
                    "GpuPower",               // Alternative simple
                    "0x2F"                    // Direct BIOS command ID (CMD_GPU_GET_POWER)
                };
                
                foreach (var cmd in commands)
                {
                    var result = ExecuteOghCommandSilent(cmd);
                    if (result != null && result.Length > 0)
                    {
                        // Parse GPU power data structure:
                        // Byte 0: CustomTgp (0=off, 1=on)
                        // Byte 1: PPAB (0=off, 1=on)
                        // Byte 2: DState
                        // Byte 3: PeakTemperature
                        var customTgp = result[0] != 0;
                        var ppab = result.Length > 1 && result[1] != 0;
                        
                        int level;
                        string levelName;
                        
                        if (customTgp && ppab)
                        {
                            level = 2;
                            levelName = "Maximum";
                        }
                        else if (customTgp)
                        {
                            level = 1;
                            levelName = "Medium";
                        }
                        else
                        {
                            level = 0;
                            levelName = "Minimum";
                        }
                        
                        _logging?.Info($"OGH GPU power level via '{cmd}': {levelName} (CustomTGP={customTgp}, PPAB={ppab})");
                        return (true, level, levelName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get OGH GPU power level: {ex.Message}");
            }
            return (false, -1, "Unknown");
        }

        /// <summary>
        /// Set GPU power level through OGH.
        /// Level: 0=Minimum (base TGP), 1=Medium (custom TGP), 2=Maximum (custom TGP + PPAB)
        /// </summary>
        public bool SetGpuPowerLevel(int level)
        {
            _logging?.Info($"Setting OGH GPU power level: {level}");
            
            // Build the GPU power data structure according to OmenMon format
            // [CustomTgp, PPAB, DState, PeakTemperature]
            var data = new byte[4];
            switch (level)
            {
                case 0: // Minimum - base TGP only
                    data[0] = 0; // CustomTgp off
                    data[1] = 0; // PPAB off
                    break;
                case 1: // Medium - custom TGP
                    data[0] = 1; // CustomTgp on
                    data[1] = 0; // PPAB off
                    break;
                case 2: // Maximum - custom TGP + PPAB
                    data[0] = 1; // CustomTgp on
                    data[1] = 1; // PPAB on
                    break;
                default:
                    data[0] = 1; // Default to medium
                    data[1] = 0;
                    break;
            }
            data[2] = 0; // DState (0 = default)
            data[3] = 0; // PeakTemperature (0 = disabled threshold)
            
            // Try multiple command formats that OGH may support
            var commands = new[]
            {
                "GPU:SetPower",           // Standard format
                "GPUPower:Set",           // Alternative format
                "Gpu",                    // Simple format (for set operations)
                "SystemData:SetGPUPower", // System data format
                "0x30"                    // Direct BIOS command ID (CMD_GPU_SET_POWER)
            };
            
            foreach (var cmd in commands)
            {
                if (ExecuteOghSetCommand(cmd, data))
                {
                    _logging?.Info($"✓ OGH GPU power level set to {level} via '{cmd}'");
                    return true;
                }
            }
            
            // Also try the preset-style commands
            var presetName = level switch
            {
                0 => "Minimum",
                1 => "Medium", 
                2 => "Maximum",
                _ => "Medium"
            };
            
            var presetCommands = new[]
            {
                $"GPU:SetPower:{presetName}",
                $"GPUPower:{presetName}",
                $"Gpu={presetName}"
            };
            
            foreach (var cmd in presetCommands)
            {
                if (ExecuteOghSetCommand(cmd, new byte[] { (byte)level }))
                {
                    _logging?.Info($"✓ OGH GPU power level set to {presetName} via '{cmd}'");
                    return true;
                }
            }
            
            _logging?.Warn("Failed to set GPU power level via OGH - no commands succeeded");
            return false;
        }
        
        /// <summary>
        /// Set performance/thermal policy through OGH.
        /// This is equivalent to selecting Performance/Balanced/Quiet in OMEN Gaming Hub.
        /// </summary>
        public bool SetPerformancePolicy(string policy)
        {
            _logging?.Info($"Setting OGH performance policy: {policy}");
            
            // Map policy names to values based on OmenMon documentation
            // FanMode: Default=0x30, Performance=0x31, Cool=0x50
            byte policyValue = policy.ToLower() switch
            {
                "performance" or "high" => 0x31,
                "balanced" or "default" or "auto" => 0x30,
                "quiet" or "cool" or "silent" => 0x50,
                _ => 0x30
            };
            
            var data = new byte[] { policyValue };
            
            // Try multiple command formats
            var commands = new[]
            {
                $"FanMode:Set:{policy}",
                $"FanMode={policy}",
                "FanMode:Set",
                "Thermal:SetPolicy",
                $"Thermal:SetPolicy:{policy}",
                "SystemData:SetThermalPolicy",
                "0x1C"  // CMD_FAN_MODE_SET
            };
            
            foreach (var cmd in commands)
            {
                if (ExecuteOghSetCommand(cmd, data))
                {
                    _logging?.Info($"✓ OGH performance policy set to '{policy}' via '{cmd}'");
                    return true;
                }
            }
            
            // Also try the thermal policy enum values
            int thermalPolicyValue = policy.ToLower() switch
            {
                "performance" => 1,
                "balanced" or "default" => 0,
                "quiet" or "cool" => 2,
                _ => 0
            };
            
            if (SetThermalPolicy((ThermalPolicy)thermalPolicyValue))
                return true;
            
            _logging?.Warn($"Failed to set OGH performance policy '{policy}'");
            return false;
        }
        
        /// <summary>
        /// Try to read system information from OGH.
        /// Returns information about what capabilities are available.
        /// </summary>
        public (bool success, byte thermalPolicy, bool gpuModeSwitch, byte supportFlags) GetSystemInfo()
        {
            try
            {
                var commands = new[] { "SystemData:Get", "System", "0x28" };
                
                foreach (var cmd in commands)
                {
                    var result = ExecuteOghCommandSilent(cmd);
                    if (result != null && result.Length >= 9)
                    {
                        // SystemData structure from OmenMon:
                        // Bytes 0-1: StatusFlags
                        // Byte 2: Unknown
                        // Byte 3: ThermalPolicy version
                        // Byte 4: SupportFlags (bit 0 = SwFanCtl)
                        // Byte 5: DefaultCpuPowerLimit4
                        // Byte 6: BiosOc support
                        // Byte 7: GpuModeSwitch support
                        // Byte 8: DefaultCpuPowerLimitWithGpu
                        
                        var thermalPolicy = result[3];
                        var supportFlags = result[4];
                        var gpuModeSwitch = result[7] != 0;
                        
                        _logging?.Info($"OGH SystemInfo via '{cmd}': ThermalPolicy=V{thermalPolicy}, SwFanCtl={(supportFlags & 1) != 0}, GpuModeSwitch={gpuModeSwitch}");
                        return (true, thermalPolicy, gpuModeSwitch, supportFlags);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging?.Warn($"Failed to get OGH system info: {ex.Message}");
            }
            return (false, 0, false, 0);
        }

        /// <summary>
        /// Start OGH services if they're installed but not running.
        /// Returns true if services are now running.
        /// </summary>
        public bool TryStartOghServices()
        {
            if (!Status.IsInstalled || Status.IsRunning)
                return Status.IsRunning;
                
            try
            {
                foreach (var serviceName in OghServiceNames)
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{serviceName}'");
                        foreach (ManagementObject service in searcher.Get())
                        {
                            var state = service["State"]?.ToString();
                            if (state?.Equals("Stopped", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                _logging?.Info($"Starting OGH service: {serviceName}");
                                // Use WMI to start the service
                                var result = service.InvokeMethod("StartService", null);
                                if (result != null && (uint)result == 0)
                                {
                                    _logging?.Info($"Started {serviceName} successfully");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging?.Warn($"Could not start {serviceName}: {ex.Message}");
                    }
                }
                
                // Re-detect status after starting services
                System.Threading.Thread.Sleep(2000); // Give services time to initialize
                DetectOghStatus();
                return Status.IsRunning;
            }
            catch (Exception ex)
            {
                _logging?.Error($"Failed to start OGH services: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Run diagnostics to determine what OGH commands work on this system.
        /// Returns a summary of working and non-working commands.
        /// </summary>
        public string RunDiagnostics()
        {
            var results = new System.Text.StringBuilder();
            results.AppendLine("=== OGH Command Diagnostics ===");
            results.AppendLine($"OGH Status: {Status.Message}");
            results.AppendLine($"WMI Available: {Status.WmiAvailable}");
            results.AppendLine($"Commands Work: {Status.CommandsWork}");
            results.AppendLine();
            
            if (!Status.WmiAvailable)
            {
                results.AppendLine("WMI interface not available - cannot test commands");
                return results.ToString();
            }
            
            // Test various GET commands
            var getCommands = new[]
            {
                ("Fan:GetData", "Fan data"),
                ("SystemData:Get", "System info"),
                ("GPU:GetMode", "GPU mode"),
                ("GPU:GetPower", "GPU power"),
                ("Thermal:GetPolicy", "Thermal policy"),
                ("Backlight:Get", "Keyboard backlight"),
                ("FanCount", "Fan count"),
                ("FanLevel", "Fan level"),
                ("System", "System data"),
                ("Gpu", "GPU info"),
                ("Temp", "Temperature")
            };
            
            results.AppendLine("GET Commands:");
            foreach (var (cmd, _) in getCommands)
            {
                try
                {
                    var result = ExecuteOghCommandSilent(cmd);
                    if (result != null && result.Length > 0)
                    {
                        var dataPreview = result.Length <= 8 
                            ? BitConverter.ToString(result)
                            : BitConverter.ToString(result, 0, 8) + "...";
                        results.AppendLine($"  ✓ {cmd}: OK ({result.Length} bytes: {dataPreview})");
                    }
                    else
                    {
                        results.AppendLine($"  ✗ {cmd}: No data");
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"  ✗ {cmd}: Error - {ex.Message}");
                }
            }
            
            results.AppendLine();
            results.AppendLine("Note: SET commands not tested (would modify system state)");
            results.AppendLine();
            
            // Get system info if available
            var (success, thermalPolicy, gpuModeSwitch, supportFlags) = GetSystemInfo();
            if (success)
            {
                results.AppendLine($"System Info: ThermalPolicy=V{thermalPolicy}, " +
                    $"GpuModeSwitch={gpuModeSwitch}, SupportFlags=0x{supportFlags:X2}");
            }
            
            // Get GPU info
            var gpuInfo = GetGpuPowerLevel();
            if (gpuInfo.success)
            {
                results.AppendLine($"GPU Power: {gpuInfo.levelName} (level {gpuInfo.level})");
            }
            
            _logging?.Info(results.ToString());
            return results.ToString();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _oghInterface?.Dispose();
                _disposed = true;
            }
        }
    }
}
