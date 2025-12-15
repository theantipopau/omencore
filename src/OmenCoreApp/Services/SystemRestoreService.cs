using System;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class SystemRestoreService
    {
        private readonly LoggingService _logging;

        public SystemRestoreService(LoggingService logging)
        {
            _logging = logging;
        }

        /// <summary>
        /// Checks if System Restore is enabled on the system.
        /// </summary>
        public bool IsSystemRestoreEnabled()
        {
            try
            {
                // Check registry for System Restore status
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                if (key == null)
                {
                    _logging.Warn("System Restore registry key not found");
                    return false;
                }

                var disableValue = key.GetValue("RPSessionInterval");
                if (disableValue is int interval && interval == 0)
                {
                    _logging.Warn("System Restore is disabled via registry (RPSessionInterval=0)");
                    return false;
                }

                // Also check if disabled via group policy
                using var policyKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
                if (policyKey != null)
                {
                    var disableSR = policyKey.GetValue("DisableSR");
                    if (disableSR is int disabled && disabled == 1)
                    {
                        _logging.Warn("System Restore is disabled via Group Policy");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Could not check System Restore status: {ex.Message}");
                return true; // Assume enabled if we can't check
            }
        }

        public async Task<SystemRestoreResult> CreateRestorePointAsync(string description, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                var result = new SystemRestoreResult();
                try
                {
                    token.ThrowIfCancellationRequested();

                    // Check if System Restore is enabled first
                    if (!IsSystemRestoreEnabled())
                    {
                        result.Success = false;
                        result.Message = "System Restore is disabled on this system. Enable it in System Properties → System Protection to create restore points.";
                        _logging.Warn("Cannot create restore point: System Restore is disabled");
                        return result;
                    }

                    var scope = new ManagementScope(@"\\.\root\default");
                    scope.Connect();
                    
                    // Check if the SystemRestore class exists
                    using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                    if (systemRestore == null)
                    {
                        result.Success = false;
                        result.Message = "System Restore WMI class not available. System Restore may not be installed.";
                        return result;
                    }

                    using var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 0; // APPLICATION_INSTALL
                    inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
                    var outParams = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
                    var returnValue = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1);
                    var sequenceNumber = Convert.ToUInt32(outParams?["SequenceNumber"] ?? 0);
                    result.SequenceNumber = sequenceNumber;
                    result.Success = returnValue == 0;
                    
                    // Provide more helpful error messages for common error codes
                    result.Message = returnValue switch
                    {
                        0 => "Restore point created successfully",
                        1 => "Access denied. Try running OmenCore as Administrator.",
                        2 => "System Restore is disabled. Enable it in System Properties → System Protection.",
                        3 => "Restore point creation is currently in progress.",
                        4 => "Not enough disk space for restore point.",
                        5 => "A restore point was created recently. Windows limits restore point frequency.",
                        _ => $"System Restore failed with error code {returnValue}"
                    };

                    if (result.Success)
                    {
                        _logging.Info($"System restore point '{description}' created (seq {sequenceNumber}).");
                    }
                    else
                    {
                        _logging.Warn($"Failed to create restore point '{description}': {result.Message}");
                    }
                }
                catch (ManagementException mex)
                {
                    // Handle localized "Not found" messages from Windows
                    // Common translations: "No encontrado" (Spanish), "Nicht gefunden" (German), etc.
                    string lowerMsg = mex.Message.ToLowerInvariant();
                    bool isNotFound = lowerMsg.Contains("not found") || 
                                      lowerMsg.Contains("no encontrad") ||  // Spanish
                                      lowerMsg.Contains("nicht gefunden") || // German
                                      lowerMsg.Contains("non trouvé") ||     // French
                                      lowerMsg.Contains("não encontrado") || // Portuguese
                                      lowerMsg.Contains("未找到") ||         // Chinese
                                      mex.ErrorCode == ManagementStatus.NotFound;
                    
                    if (isNotFound)
                    {
                        result.Success = false;
                        result.Message = "System Restore is not available. This can happen if:\n" +
                                       "• System Restore is disabled in System Properties → System Protection\n" +
                                       "• The Volume Shadow Copy service is stopped\n" +
                                       "• Group Policy has disabled System Restore\n\n" +
                                       "To enable: Control Panel → System → System Protection → Configure";
                        _logging.Warn($"System Restore WMI class not found: {mex.Message}");
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = $"System Restore error: {mex.Message}";
                        _logging.Error($"System Restore WMI error: {mex.Message}");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    result.Success = false;
                    result.Message = "Access denied. Please run OmenCore as Administrator to create restore points.";
                    _logging.Error("System restore point creation failed: Access denied");
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Failed to create restore point: {ex.Message}";
                    _logging.Error("System restore point creation failed", ex);
                }

                return result;
            }, token);
        }
    }
}
