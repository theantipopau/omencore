using System;
using System.Diagnostics;
using System.Linq;
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

    public class IntelUndervoltProvider : ICpuUndervoltProvider
    {
        private readonly object _stateLock = new();
        private UndervoltOffset _lastApplied = new() { CoreMv = 0, CacheMv = 0 };
        private WinRing0MsrAccess? _msrAccess;

        public IntelUndervoltProvider()
        {
            try
            {
                _msrAccess = new WinRing0MsrAccess();
            }
            catch
            {
                // WinRing0 driver not available - will operate in stub mode
                _msrAccess = null;
            }
        }

        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token)
        {
            lock (_stateLock)
            {
                _lastApplied = offset.Clone();
                
                if (_msrAccess != null && _msrAccess.IsAvailable)
                {
                    try
                    {
                        // Apply actual MSR writes
                        _msrAccess.ApplyCoreVoltageOffset((int)offset.CoreMv);
                        _msrAccess.ApplyCacheVoltageOffset((int)offset.CacheMv);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to apply voltage offset: {ex.Message}", ex);
                    }
                }
            }
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken token)
        {
            lock (_stateLock)
            {
                _lastApplied = new UndervoltOffset();
                
                if (_msrAccess != null && _msrAccess.IsAvailable)
                {
                    try
                    {
                        // Reset to 0mV offset
                        _msrAccess.ApplyCoreVoltageOffset(0);
                        _msrAccess.ApplyCacheVoltageOffset(0);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to reset voltage offset: {ex.Message}", ex);
                    }
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
                
                // Try to read actual MSR values if WinRing0 available
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
                status.Warning = $"External undervolt detected via {external.Source}. OmenCore may conflict with this application.";
            }
            else if (!canReadMsr && copy.CoreMv == 0 && copy.CacheMv == 0)
            {
                status.Warning = "WinRing0 driver not available. Install driver to enable CPU undervolting.";
            }
            else if (!canReadMsr)
            {
                status.Warning = "Cannot verify applied voltage offsets (WinRing0 unavailable). Showing last requested values.";
            }

            return Task.FromResult(status);
        }

        private ExternalUndervoltInfo? DetectExternalController()
        {
            var probes = new[] { "OmenGamingHub", "ThrottleStop", "XTUService", "IntelXtuService" };
            foreach (var probe in probes)
            {
                try
                {
                    var processes = Process.GetProcessesByName(probe);
                    if (processes.Any())
                    {
                        return new ExternalUndervoltInfo
                        {
                            Source = probe,
                            Offset = new UndervoltOffset { CoreMv = -80, CacheMv = -60 }
                        };
                    }
                }
                catch
                {
                    // Ignore Process enumeration failures; monitoring will continue.
                }
            }

            return null;
        }
    }
}
