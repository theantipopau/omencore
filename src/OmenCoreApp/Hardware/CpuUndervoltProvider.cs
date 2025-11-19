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

        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token)
        {
            lock (_stateLock)
            {
                _lastApplied = offset.Clone();
            }
            // TODO: Replace with Intel voltage plane writes via WinRing0 / XTU service interop.
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken token)
        {
            lock (_stateLock)
            {
                _lastApplied = new UndervoltOffset();
            }
            // TODO: Push neutral offsets to the CPU voltage planes.
            return Task.CompletedTask;
        }

        public Task<UndervoltStatus> ProbeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            UndervoltOffset copy;
            lock (_stateLock)
            {
                copy = _lastApplied.Clone();
            }

            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = copy.CoreMv,
                CurrentCacheOffsetMv = copy.CacheMv,
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
                status.Warning = $"External undervolt detected via {external.Source}.";
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
