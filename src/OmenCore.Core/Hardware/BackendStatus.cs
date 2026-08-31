using System;

namespace OmenCore.Hardware
{
    public enum BackendCapability
    {
        Telemetry,
        FanControl,
        PerformanceProfiles,
        Undervolt,
        ECAccess
    }

    /// <summary>
    /// Structured backend/provider health snapshot used for diagnostics and status projection.
    /// This is additive and does not change backend behavior.
    /// </summary>
    public sealed class BackendStatus
    {
        public string Name { get; set; } = string.Empty;
        public bool Required { get; set; }
        public bool Available { get; set; }
        public bool Healthy { get; set; }
        public BackendCapability[] Capabilities { get; set; } = Array.Empty<BackendCapability>();
        public string? FailureReason { get; set; }
        public string? RecommendedAction { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}