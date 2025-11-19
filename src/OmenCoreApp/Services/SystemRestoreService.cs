using System;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task<SystemRestoreResult> CreateRestorePointAsync(string description, CancellationToken token = default)
        {
            return await Task.Run(() =>
            {
                var result = new SystemRestoreResult();
                try
                {
                    token.ThrowIfCancellationRequested();
                    var scope = new ManagementScope(@"\\.\root\default");
                    scope.Connect();
                    using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                    using var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 0; // APPLICATION_INSTALL
                    inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
                    var outParams = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
                    var returnValue = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1);
                    var sequenceNumber = Convert.ToUInt32(outParams?["SequenceNumber"] ?? 0);
                    result.SequenceNumber = sequenceNumber;
                    result.Success = returnValue == 0;
                    result.Message = returnValue == 0 ? "Restore point created" : $"SystemRestore returned {returnValue}";
                    if (result.Success)
                    {
                        _logging.Info($"System restore point '{description}' created (seq {sequenceNumber}).");
                    }
                    else
                    {
                        _logging.Warn($"Failed to create restore point '{description}': code {returnValue}");
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = ex.Message;
                    _logging.Error("System restore point creation failed", ex);
                }

                return result;
            }, token);
        }
    }
}
