using System;

namespace OmenCore.Models
{
    public class ExternalUndervoltInfo
    {
        public string Source { get; set; } = string.Empty;
        public UndervoltOffset Offset { get; set; } = new();
    }

    public class UndervoltStatus
    {
        public double CurrentCoreOffsetMv { get; set; }
        public double CurrentCacheOffsetMv { get; set; }
        public bool ControlledByOmenCore { get; set; }
        public string? ExternalController { get; set; }
        public double ExternalCoreOffsetMv { get; set; }
        public double ExternalCacheOffsetMv { get; set; }
        public string? Warning { get; set; }
        public string? Error { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Per-core status information
        public double?[]? CurrentPerCoreOffsetsMv { get; set; }
        public double?[]? ExternalPerCoreOffsetsMv { get; set; }

        public bool HasExternalController => !string.IsNullOrWhiteSpace(ExternalController);
        public bool HasPerCoreOffsets => CurrentPerCoreOffsetsMv != null && CurrentPerCoreOffsetsMv.Length > 0;

        public static UndervoltStatus CreateUnknown(string? message = null) => new()
        {
            Warning = message ?? "Awaiting undervolt telemetry...",
            ControlledByOmenCore = false
        };
    }
}
