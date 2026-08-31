using System;

namespace OmenCore.Models
{
    /// <summary>
    /// Minimal telemetry envelope for reliability-aware sensor projection.
    /// Value is null when unsupported/unavailable instead of coercing fake defaults.
    /// </summary>
    public sealed class TelemetryValue<T> where T : struct
    {
        public T? Value { get; set; }
        public bool IsSupported { get; set; }
        public bool IsStale { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
        public string BackendSource { get; set; } = string.Empty;
        public string? LastError { get; set; }

        public TelemetryValue() { }

        public TelemetryValue(TelemetryValue<T>? source)
        {
            if (source == null)
            {
                return;
            }

            Value = source.Value;
            IsSupported = source.IsSupported;
            IsStale = source.IsStale;
            LastUpdatedUtc = source.LastUpdatedUtc;
            BackendSource = source.BackendSource;
            LastError = source.LastError;
        }
    }
}