using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Hardware
{
    /// <summary>
    /// A user-facing capability a control depends on. Kept small and named after the controls that
    /// exist, so a disabled control can point at exactly one entry.
    /// </summary>
    public enum CapabilityFeature
    {
        FanControl,
        FanCurves,
        IndependentFanCurves,
        GpuPowerBoost,
        MuxSwitch,
        Lighting,
        PerKeyLighting,
        Undervolt,
        PerformanceModes
    }

    /// <summary>
    /// Why a feature is or is not available. The code is for grouping and diagnostics; the message
    /// is what the user reads, and <see cref="CapabilityReason.Source"/> says which evidence it came
    /// from so a "no" is never mistaken for a detection failure when the database said "no".
    /// </summary>
    public enum CapabilityReasonCode
    {
        /// <summary>The feature was detected and is usable.</summary>
        Available,
        /// <summary>Nothing on this board offers the feature, and no entry claims otherwise.</summary>
        NotPresent,
        /// <summary>The model database records this board as unsupported.</summary>
        ModelDatabase,
        /// <summary>The board has the interface but HP's firmware refuses or ignores the write.</summary>
        FirmwareDeclines,
        /// <summary>OmenCore blocks the write itself (for example, OMEN desktop fan writes).</summary>
        SafetyPolicy,
        /// <summary>A driver or provider is missing (PawnIO, HP WMI BIOS, a vendor API).</summary>
        DriverMissing,
        /// <summary>Supported in principle but not ready in this session.</summary>
        NotReady
    }

    /// <summary>
    /// One feature's availability and the reason behind it. <see cref="Message"/> is written for the
    /// screen: it names the board's own evidence rather than a generic "not supported".
    /// </summary>
    public sealed class CapabilityReason
    {
        public CapabilityReason(CapabilityFeature feature, CapabilityReasonCode code, string message, string source)
        {
            Feature = feature;
            Code = code;
            Message = message;
            Source = source;
        }

        public CapabilityFeature Feature { get; }
        public CapabilityReasonCode Code { get; }
        public string Message { get; }
        public string Source { get; }
        public bool Available => Code == CapabilityReasonCode.Available;

        /// <summary>One-line form for tooltips and the diagnostics export.</summary>
        public string Describe() =>
            $"{Feature}: {(Available ? "available" : "unavailable")} [{Code}] - {Message} (source: {Source})";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Builds the reason behind every gated feature from the state the app already detected.
    ///
    /// This replaces the old arrangement where a control simply vanished or greyed out and the only
    /// explanation lived in an unread log line: the same sentence the control shows is the one the
    /// diagnostics export carries (roadmap I.2 / WP-44). It deliberately derives from
    /// <see cref="DeviceCapabilities"/> only - no probing, no writes, no hardware access - so it is
    /// safe to call from the UI thread and from the exporter.
    /// </summary>
    public static class CapabilityReasonResolver
    {
        public static IReadOnlyList<CapabilityReason> Resolve(DeviceCapabilities caps)
        {
            if (caps == null) throw new ArgumentNullException(nameof(caps));

            return new[]
            {
                FanControl(caps),
                FanCurves(caps),
                IndependentFanCurves(caps),
                GpuPowerBoost(caps),
                MuxSwitch(caps),
                Lighting(caps),
                PerKeyLighting(caps),
                Undervolt(caps),
                PerformanceModes(caps)
            };
        }

        public static CapabilityReason For(DeviceCapabilities caps, CapabilityFeature feature) =>
            Resolve(caps).First(reason => reason.Feature == feature);

        /// <summary>
        /// The sentence a control should show, or empty when the feature is available. Kept here so
        /// the view model and the tests agree on exactly what "no reason" means.
        /// </summary>
        public static string ReasonText(DeviceCapabilities? caps, CapabilityFeature feature)
        {
            if (caps == null) return string.Empty;
            var reason = For(caps, feature);
            return reason.Available ? string.Empty : reason.Message;
        }

        /// <summary>Every unavailable feature, as one line each. Empty when nothing is gated.</summary>
        public static string DescribeUnavailable(DeviceCapabilities caps)
        {
            var unavailable = Resolve(caps).Where(reason => !reason.Available).ToList();
            return unavailable.Count == 0
                ? "Every capability OmenCore can gate is available on this board."
                : string.Join(Environment.NewLine, unavailable.Select(reason => reason.Describe()));
        }

        private static CapabilityReason FanControl(DeviceCapabilities caps)
        {
            if (caps.CanSetFanSpeed && !caps.FanWritesBlockedForSafety)
            {
                return Available(CapabilityFeature.FanControl,
                    $"Fan writes work through {caps.FanControl}.");
            }

            if (caps.FanWritesBlockedForSafety)
            {
                return new CapabilityReason(CapabilityFeature.FanControl, CapabilityReasonCode.SafetyPolicy,
                    "Fan writes are disabled on OMEN desktops until per-model validation; monitoring still works.",
                    "safety policy");
            }

            var model = caps.ModelConfig;
            if (model != null && !model.SupportsFanControlWmi && !model.SupportsFanControlEc)
            {
                return new CapabilityReason(CapabilityFeature.FanControl, CapabilityReasonCode.ModelDatabase,
                    $"The model database records that {(string.IsNullOrWhiteSpace(model.ModelName) ? "this board" : model.ModelName)} has no working software fan control.",
                    "model database");
            }

            if (caps.FanControl == FanControlMethod.MonitoringOnly || caps.FanControl == FanControlMethod.None)
            {
                return new CapabilityReason(CapabilityFeature.FanControl, CapabilityReasonCode.DriverMissing,
                    MissingBackendMessage(caps),
                    "runtime detection");
            }

            return new CapabilityReason(CapabilityFeature.FanControl, CapabilityReasonCode.NotPresent,
                "No working fan-control backend was detected for this board.", "runtime detection");
        }

        private static string MissingBackendMessage(DeviceCapabilities caps)
        {
            if (caps.SecureBootEnabled && !caps.PawnIOAvailable)
            {
                return "Secure Boot is enabled and PawnIO is not installed, so neither the EC nor MSR fan path can open.";
            }

            if (caps.OghInstalled && !caps.OghRunning)
            {
                return "HP WMI BIOS is unavailable and OMEN Gaming Hub (the only fallback here) is installed but not running.";
            }

            return "No fan-control backend is available: HP WMI BIOS did not answer and no EC driver is installed.";
        }

        private static CapabilityReason FanCurves(DeviceCapabilities caps)
        {
            if (caps.ShowFanCurveEditor)
            {
                return Available(CapabilityFeature.FanCurves, "Custom fan curves can be applied on this board.");
            }

            if (caps.FanWritesBlockedForSafety)
            {
                return new CapabilityReason(CapabilityFeature.FanCurves, CapabilityReasonCode.SafetyPolicy,
                    "Fan curves are blocked with fan writes on OMEN desktops; monitoring still works.", "safety policy");
            }

            if (caps.ModelConfig?.SupportsFanCurves == false)
            {
                return new CapabilityReason(CapabilityFeature.FanCurves, CapabilityReasonCode.ModelDatabase,
                    "The model database records that this board's firmware does not hold a custom fan curve.", "model database");
            }

            if (!caps.CanSetFanSpeed)
            {
                return new CapabilityReason(CapabilityFeature.FanCurves, CapabilityReasonCode.NotReady,
                    "A curve needs working fan writes, and this session has none: " + MissingBackendMessage(caps),
                    "runtime detection");
            }

            return new CapabilityReason(CapabilityFeature.FanCurves, CapabilityReasonCode.NotPresent,
                "Custom fan curves were not detected as usable on this board.", "runtime detection");
        }

        private static CapabilityReason IndependentFanCurves(DeviceCapabilities caps)
        {
            if (caps.ShowIndependentFanCurves)
            {
                return Available(CapabilityFeature.IndependentFanCurves,
                    "CPU and GPU can be given separate curves.");
            }

            if (!caps.ShowFanCurveEditor)
            {
                return new CapabilityReason(CapabilityFeature.IndependentFanCurves, CapabilityReasonCode.NotReady,
                    "Independent curves need the fan curve editor, which is unavailable here.", "runtime detection");
            }

            if (caps.ModelConfig?.SupportsIndependentFanCurves == false)
            {
                return new CapabilityReason(CapabilityFeature.IndependentFanCurves, CapabilityReasonCode.ModelDatabase,
                    "The model database records that this board drives both fans from one curve.", "model database");
            }

            return new CapabilityReason(CapabilityFeature.IndependentFanCurves, CapabilityReasonCode.NotPresent,
                "Independent CPU/GPU curves were not detected on this board.", "runtime detection");
        }

        private static CapabilityReason GpuPowerBoost(DeviceCapabilities caps)
        {
            if (caps.ShowGpuPowerBoost)
            {
                return Available(CapabilityFeature.GpuPowerBoost,
                    "HP's GPU power levels are available on this board.");
            }

            if (caps.ModelFamily == OmenModelFamily.Victus && caps.ModelConfig?.SupportsGpuPowerBoost != true)
            {
                return new CapabilityReason(CapabilityFeature.GpuPowerBoost, CapabilityReasonCode.ModelDatabase,
                    "Victus boards accept GPU Power Boost levels only where the model database has confirmed HP's firmware honours them.",
                    "model database");
            }

            if (!caps.HasGpuPowerControl)
            {
                return new CapabilityReason(CapabilityFeature.GpuPowerBoost, CapabilityReasonCode.DriverMissing,
                    "No HP WMI BIOS GPU power interface was found, so boost levels have nowhere to go.", "runtime detection");
            }

            return new CapabilityReason(CapabilityFeature.GpuPowerBoost, CapabilityReasonCode.NotPresent,
                "GPU Power Boost was not detected on this board.", "runtime detection");
        }

        private static CapabilityReason MuxSwitch(DeviceCapabilities caps)
        {
            if (caps.ShowMuxSwitch)
            {
                return Available(CapabilityFeature.MuxSwitch, "A MUX switch is present and switchable.");
            }

            if (caps.ModelConfig != null && !caps.ModelConfig.HasMuxSwitch)
            {
                return new CapabilityReason(CapabilityFeature.MuxSwitch, CapabilityReasonCode.ModelDatabase,
                    "The model database lists no MUX switch for this board; graphics are wired directly.", "model database");
            }

            return new CapabilityReason(CapabilityFeature.MuxSwitch, CapabilityReasonCode.NotPresent,
                "No MUX switch was detected on this board.", "runtime detection");
        }

        private static CapabilityReason Lighting(DeviceCapabilities caps)
        {
            if (caps.ShowRgbLighting)
            {
                return Available(CapabilityFeature.Lighting,
                    caps.HasPerKeyLighting ? "Per-key RGB is present." : "Zone RGB is present.");
            }

            if (caps.ModelConfig != null && !caps.ModelConfig.HasFourZoneRgb && !caps.ModelConfig.HasPerKeyRgb)
            {
                return new CapabilityReason(CapabilityFeature.Lighting, CapabilityReasonCode.ModelDatabase,
                    "The model database records no colour-addressable keyboard lighting on this board.", "model database");
            }

            return new CapabilityReason(CapabilityFeature.Lighting, CapabilityReasonCode.NotPresent,
                "Firmware reports no colour-addressable keyboard lighting; only a plain backlight, if any, is available.",
                "firmware");
        }

        private static CapabilityReason PerKeyLighting(DeviceCapabilities caps)
        {
            if (caps.HasPerKeyLighting || (caps.IsKnownModel && caps.ModelConfig?.HasPerKeyRgb == true))
            {
                return Available(CapabilityFeature.PerKeyLighting, "The keyboard is per-key addressable.");
            }

            if (caps.ModelConfig != null && caps.ModelConfig.HasFourZoneRgb)
            {
                return new CapabilityReason(CapabilityFeature.PerKeyLighting, CapabilityReasonCode.ModelDatabase,
                    "This board's keyboard is zone-addressable, not per-key; per-key effects do not apply.", "model database");
            }

            return new CapabilityReason(CapabilityFeature.PerKeyLighting, CapabilityReasonCode.NotPresent,
                "No per-key addressable keyboard was reported on this board.", "firmware");
        }

        private static CapabilityReason Undervolt(DeviceCapabilities caps)
        {
            if (caps.ShowUndervolt)
            {
                return Available(CapabilityFeature.Undervolt,
                    $"Undervolting is available through {caps.UndervoltMethod}.");
            }

            if (!string.IsNullOrWhiteSpace(caps.UndervoltBlockReason))
            {
                return new CapabilityReason(CapabilityFeature.Undervolt, CapabilityReasonCode.NotReady,
                    caps.UndervoltBlockReason!, "runtime API readiness");
            }

            if (caps.ModelConfig?.SupportsUndervolt == false)
            {
                return new CapabilityReason(CapabilityFeature.Undervolt, CapabilityReasonCode.ModelDatabase,
                    "The model database records that this board's firmware does not accept a CPU undervolt.", "model database");
            }

            if (caps.SecureBootEnabled && !caps.PawnIOAvailable)
            {
                return new CapabilityReason(CapabilityFeature.Undervolt, CapabilityReasonCode.DriverMissing,
                    "Secure Boot is enabled and PawnIO is not installed, so MSR access for undervolting is unavailable.",
                    "runtime detection");
            }

            return new CapabilityReason(CapabilityFeature.Undervolt, CapabilityReasonCode.NotPresent,
                "No CPU undervolt path (MSR, PawnIO or curve optimiser) was found on this board.", "runtime detection");
        }

        private static CapabilityReason PerformanceModes(DeviceCapabilities caps)
        {
            if (caps.ShowPerformanceModes)
            {
                var modes = caps.AvailablePerformanceModes is { Length: > 0 }
                    ? string.Join(", ", caps.AvailablePerformanceModes)
                    : "OEM modes";
                return Available(CapabilityFeature.PerformanceModes, $"OEM performance modes are available: {modes}.");
            }

            return new CapabilityReason(CapabilityFeature.PerformanceModes, CapabilityReasonCode.DriverMissing,
                "HP's performance-mode interface was not found, so the mode selector falls back to Windows power plans.",
                "runtime detection");
        }

        private static CapabilityReason Available(CapabilityFeature feature, string message) =>
            new(feature, CapabilityReasonCode.Available, message, "runtime detection");
    }
}
