using System;

namespace OmenCore.Models
{
    public class ExternalUndervoltInfo
    {
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// The external controller's actual offset, when it was actually read. Null means the
        /// external controller was detected (e.g. its service/process is running) but its
        /// applied values were not read - this is the common case, since most detection here is
        /// presence-only. Do not default this to a zero offset: that reads as "confirmed 0 mV",
        /// not "unknown". See docs/TUNING-SUBSYSTEMS-REVIEW.md, finding F9.
        /// </summary>
        public UndervoltOffset? Offset { get; set; }
    }

    public class UndervoltStatus
    {
        public double CurrentCoreOffsetMv { get; set; }
        public double CurrentCacheOffsetMv { get; set; }
        public bool ControlledByOmenCore { get; set; }
        public bool IsRuntimeReady { get; set; } = true;
        public string? RuntimeBlockReason { get; set; }
        public string? ExternalController { get; set; }

        /// <summary>
        /// Null when an external controller was detected but its actual offset was not read
        /// (the common case - detection here is presence-only). See finding F9.
        /// </summary>
        public double? ExternalCoreOffsetMv { get; set; }
        public double? ExternalCacheOffsetMv { get; set; }
        public string? Warning { get; set; }
        public string? Error { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Per-core status information
        public double?[]? CurrentPerCoreOffsetsMv { get; set; }
        public double?[]? ExternalPerCoreOffsetsMv { get; set; }

        /// <summary>
        /// True when the caller most recently requested per-core offsets
        /// (<see cref="UndervoltOffset.PerCoreOffsetsMv"/>) but the active backend has no
        /// per-core write path and only applied the global Core/Cache offsets instead.
        /// Neither the Intel MSR path nor the AMD SMU path currently implement per-core writes.
        /// See docs/TUNING-SUBSYSTEMS-REVIEW.md, finding F1.
        /// </summary>
        public bool PerCoreOffsetsRequestedButNotApplied { get; set; }

        /// <summary>
        /// True when <see cref="CurrentCoreOffsetMv"/>/<see cref="CurrentCacheOffsetMv"/> were
        /// polled from an independent hardware register (e.g. the Intel MSR path). False when
        /// they are only the backend's own record of the last value it successfully wrote (the
        /// AMD Curve Optimizer/SMU path has no independent readback register to poll - a
        /// mailbox ack means the SMU accepted the write, not that a separate read confirms it).
        /// Defaults true so callers that never set it (external-controller/error/unknown states,
        /// which never reach the "matches requested" comparison anyway) keep prior behavior.
        /// </summary>
        public bool HasIndependentReadback { get; set; } = true;

        public bool HasExternalController => !string.IsNullOrWhiteSpace(ExternalController);
        public bool HasPerCoreOffsets => CurrentPerCoreOffsetsMv != null && CurrentPerCoreOffsetsMv.Length > 0;

        public static UndervoltStatus CreateUnknown(string? message = null) => new()
        {
            Warning = message ?? "Awaiting undervolt telemetry...",
            IsRuntimeReady = false,
            RuntimeBlockReason = message ?? "Undervolt runtime readiness has not been established yet.",
            ControlledByOmenCore = false
        };

        public static UndervoltStatus CreateError(string? message = null) => new()
        {
            Error = message ?? "Undervolt operation failed.",
            IsRuntimeReady = false,
            RuntimeBlockReason = message ?? "Undervolt operation failed.",
            ControlledByOmenCore = false
        };
    }
}
