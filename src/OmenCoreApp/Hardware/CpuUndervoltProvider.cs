using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Models;

namespace OmenCore.Hardware
{
    public interface ICpuUndervoltProvider
    {
        Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token);
        Task ResetAsync(CancellationToken token);
        Task<UndervoltStatus> ProbeAsync(CancellationToken token);
    }

    /// <summary>
    /// Factory for creating the appropriate undervolt provider based on CPU type.
    /// </summary>
    public static class CpuUndervoltProviderFactory
    {
        public enum CpuVendor { Unknown, Intel, AMD }

        public static CpuVendor DetectedVendor { get; private set; } = CpuVendor.Unknown;
        public static string CpuName { get; private set; } = string.Empty;

        /// <summary>
        /// Detect CPU vendor and create appropriate undervolt provider.
        /// </summary>
        public static ICpuUndervoltProvider Create(out string backendInfo)
        {
            DetectCpu();

            if (DetectedVendor == CpuVendor.AMD)
            {
                var amdProvider = new AmdUndervoltProvider();
                backendInfo = $"AMD Ryzen ({amdProvider.Family}) - {amdProvider.ActiveBackend}";
                return amdProvider;
            }
            else
            {
                var intelProvider = new IntelUndervoltProvider();
                backendInfo = $"Intel - {intelProvider.ActiveBackend}";
                return intelProvider;
            }
        }

        private static void DetectCpu()
        {
            if (DetectedVendor != CpuVendor.Unknown) return;

            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    CpuName = obj["Name"]?.ToString() ?? string.Empty;
                    string manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;

                    if (manufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                        CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                        CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
                    {
                        DetectedVendor = CpuVendor.AMD;
                        RyzenControl.Init(); // Initialize AMD-specific detection
                    }
                    else if (manufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                             CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                             CpuName.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    {
                        DetectedVendor = CpuVendor.Intel;
                    }
                    else
                    {
                        // Default to Intel for unknown CPUs (more common in gaming laptops)
                        DetectedVendor = CpuVendor.Intel;
                    }
                    break;
                }
            }
            catch
            {
                DetectedVendor = CpuVendor.Intel; // Default fallback
            }
        }
    }

    public class IntelUndervoltProvider : ICpuUndervoltProvider
    {
        private readonly object _stateLock = new();
        private UndervoltOffset _lastApplied = new() { CoreMv = 0, CacheMv = 0 };
        private readonly IMsrAccess? _msrAccess;

        public string ActiveBackend { get; private set; } = "None";

        public IntelUndervoltProvider()
        {
            // Use MsrAccessFactory to get best available backend
            _msrAccess = MsrAccessFactory.Create(null);
            if (_msrAccess != null && _msrAccess.IsAvailable)
            {
                ActiveBackend = MsrAccessFactory.ActiveBackend.ToString();
            }
        }

        private bool HasMsrAccess => _msrAccess?.IsAvailable ?? false;

        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token)
        {
            lock (_stateLock)
            {
                if (_msrAccess == null || !_msrAccess.IsAvailable)
                {
                    throw new InvalidOperationException(
                        "Cannot apply undervolt: No MSR access available.\n\n" +
                        "Please ensure PawnIO is installed and running.\n" +
                        "PawnIO is required for CPU voltage control and is included in the OmenCore installer.");
                }
                
                try
                {
                    _msrAccess.ApplyCoreVoltageOffset((int)offset.CoreMv);
                    _msrAccess.ApplyCacheVoltageOffset((int)offset.CacheMv);
                    _lastApplied = offset.Clone();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to apply voltage offset via {ActiveBackend}: {ex.Message}", ex);
                }
            }
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken token)
        {
            lock (_stateLock)
            {
                if (_msrAccess == null || !_msrAccess.IsAvailable)
                {
                    throw new InvalidOperationException(
                        "Cannot reset undervolt: No MSR access available.\n\n" +
                        "Please ensure PawnIO is installed and running.");
                }
                
                try
                {
                    _msrAccess.ApplyCoreVoltageOffset(0);
                    _msrAccess.ApplyCacheVoltageOffset(0);
                    _lastApplied = new UndervoltOffset();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to reset voltage offset via {ActiveBackend}: {ex.Message}", ex);
                }
            }
            return Task.CompletedTask;
        }

        public Task<UndervoltStatus> ProbeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            
            UndervoltOffset copy;
            int actualCore = 0;
            int actualCache = 0;
            bool canReadMsr = false;

            lock (_stateLock)
            {
                copy = _lastApplied.Clone();
                
                // Try to read actual MSR values via unified IMsrAccess interface
                if (_msrAccess != null && _msrAccess.IsAvailable)
                {
                    try
                    {
                        actualCore = _msrAccess.ReadCoreVoltageOffset();
                        actualCache = _msrAccess.ReadCacheVoltageOffset();
                        canReadMsr = true;
                    }
                    catch
                    {
                        // MSR read failed, fall back to tracking last applied values
                        canReadMsr = false;
                    }
                }
            }

            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = canReadMsr ? actualCore : copy.CoreMv,
                CurrentCacheOffsetMv = canReadMsr ? actualCache : copy.CacheMv,
                ControlledByOmenCore = true,
                Timestamp = DateTime.Now
            };

            var external = DetectExternalController();
            if (external != null)
            {
                status.ControlledByOmenCore = false;
                status.ExternalController = external.Source;
                status.ExternalCoreOffsetMv = external.Offset.CoreMv;
                status.ExternalCacheOffsetMv = external.Offset.CacheMv;
                
                // Provide specific guidance based on the detected controller
                if (external.Source.Contains("XTU", StringComparison.OrdinalIgnoreCase))
                {
                    status.Warning = $"Intel XTU service detected. XTU blocks MSR access for other applications. " +
                        "To use OmenCore undervolting:\n" +
                        "1. Open Services (services.msc)\n" +
                        "2. Find 'Intel(R) Extreme Tuning Utility' service\n" +
                        "3. Stop the service and set to 'Disabled'\n" +
                        "4. Restart OmenCore";
                }
                else
                {
                    status.Warning = $"External undervolt detected via {external.Source}. OmenCore may conflict with this application.";
                }
            }
            else if (!HasMsrAccess && copy.CoreMv == 0 && copy.CacheMv == 0)
            {
                status.Warning = "No MSR access backend available. Install PawnIO (Secure Boot compatible) to enable CPU undervolting.";
            }
            else if (!canReadMsr)
            {
                status.Warning = "Cannot verify applied voltage offsets (WinRing0 unavailable). Showing last requested values.";
            }

            return Task.FromResult(status);
        }

        private ExternalUndervoltInfo? DetectExternalController()
        {
            // Check for SERVICES that may control MSR (XTU, DTT run as services, not processes)
            var serviceProbes = new[] 
            { 
                ("XTU3SERVICE", "Intel XTU"),
                ("XtuService", "Intel XTU"),
                ("IntelXtuService", "Intel XTU"),
                ("esif_uf", "Intel DTT") // Intel Dynamic Tuning Technology
            };
            
            // Check services using ServiceController (not process names)
            foreach (var (serviceName, displayName) in serviceProbes)
            {
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController(serviceName);
                    // Only flag if service actually exists AND is running
                    if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        return new ExternalUndervoltInfo
                        {
                            Source = displayName,
                            Offset = new UndervoltOffset { CoreMv = 0, CacheMv = 0 }
                        };
                    }
                }
                catch (InvalidOperationException)
                {
                    // Service doesn't exist - this is fine, continue
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Service doesn't exist or access denied - continue
                }
            }
            
            // Check for PROCESSES (ThrottleStop runs as process, not service)
            var processProbes = new[] 
            { 
                ("ThrottleStop", "ThrottleStop"),
                ("OmenCap", "HP OmenCap (DriverStore)")  // HP component that blocks MSR access
            };
            
            foreach (var (processName, displayName) in processProbes)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Any())
                    {
                        foreach (var p in processes) p.Dispose();
                        return new ExternalUndervoltInfo
                        {
                            Source = displayName,
                            Offset = new UndervoltOffset { CoreMv = 0, CacheMv = 0 }
                        };
                    }
                }
                catch
                {
                    // Ignore Process enumeration failures
                }
            }

            return null;
        }
    }
}
