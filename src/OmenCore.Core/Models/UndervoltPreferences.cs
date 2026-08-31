using System;

namespace OmenCore.Models
{
    public class UndervoltPreferences
    {
        public UndervoltOffset DefaultOffset { get; set; } = new() { CoreMv = -75, CacheMv = -50 };
        public bool RespectExternalControllers { get; set; } = true;
        public int ProbeIntervalMs { get; set; } = 4000;

        // Enable per-core undervolting
        public bool EnablePerCoreUndervolt { get; set; } = false;

        // Per-core offsets (indexed by logical core, null means disabled)
        public int?[]? PerCoreOffsetsMv { get; set; }

        /// <summary>
        /// Reapply the saved undervolt offset automatically on startup.
        /// Only set to true after the Test Apply -> Keep flow completes successfully.
        /// Direct Apply saves offset values but does not enable startup reapply.
        /// </summary>
        public bool ApplyOnStartup { get; set; } = false;

        /// <summary>
        /// True while a CPU undervolt Test Apply session is active.
        /// Persisted so the next launch can detect interrupted/unconfirmed test state.
        /// </summary>
        public bool PendingTestApply { get; set; } = false;

        /// <summary>
        /// True when a tuning profile is waiting for user confirmation and should be
        /// treated as unconfirmed after an app crash/forced close.
        /// </summary>
        public bool StartupPendingConfirmation { get; set; } = false;

        /// <summary>
        /// Set to true when startup recovery detects an unconfirmed tuning profile and
        /// automatically performs a safe reset.
        /// </summary>
        public bool LastStartupHadUnconfirmedState { get; set; } = false;

        /// <summary>
        /// Last CPU undervolt values explicitly confirmed by the user.
        /// </summary>
        public UndervoltOffset? LastConfirmedOffset { get; set; }

        /// <summary>
        /// UTC timestamp of the last explicit user confirmation.
        /// </summary>
        public DateTime? LastConfirmedAtUtc { get; set; }
    }
}
