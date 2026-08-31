using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services.Telemetry
{
    /// <summary>
    /// Lightweight, safe wrapper for MSI Afterburner/RTSS shared memory.
    /// This stub focuses on graceful fallback: if Afterburner is not running
    /// or shared memory cannot be opened, it simply reports unavailable.
    /// </summary>
    public sealed class AfterburnerProvider : IDisposable
    {
        public bool IsAvailable { get; private set; }
        public bool HasConflict { get; private set; }
        public string? LastError { get; private set; }

        public AfterburnerProvider()
        {
            // Keep constructor side-effect free; real detection is deferred.
            Initialize();
        }

        private void Initialize()
        {
            // Minimal detection: shared memory not probed in this stub to avoid native calls.
            IsAvailable = false;
            HasConflict = false;
            LastError = "Afterburner shared memory not probed (stub provider)";
        }

        /// <summary>
        /// Attempt to read GPU telemetry. Returns null when Afterburner is unavailable.
        /// </summary>
        public Task<AfterburnerTelemetry?> ReadTelemetryAsync(CancellationToken ct = default)
        {
            return Task.FromResult<AfterburnerTelemetry?>(null);
        }

        public void Dispose()
        {
            // No unmanaged resources in stub
        }
    }

    public sealed class AfterburnerTelemetry
    {
        public int GpuTemperatureC { get; set; }
        public int GpuCoreClock { get; set; }
        public int GpuMemoryClock { get; set; }
        public int GpuFanSpeed { get; set; }
        public int GpuPowerDraw { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.Now;
    }
}
