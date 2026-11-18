using System;
using OmenCore.Models;

namespace OmenCore.ViewModels
{
    public static class TuningStatusFormatter
    {
        /// <summary>
        /// AMD Curve Optimizer offsets go to the SMU as whole counts of 4 mV-equivalent, truncated toward
        /// zero (AmdUndervoltProvider), so -15 is applied as -12. A difference under one step is the
        /// rounding, not a failed write (GitHub #222).
        /// </summary>
        public const double AmdCoStepMv = 4.0;

        /// <summary>True when the applied offset matches the request, allowing for AMD's step rounding.</summary>
        public static bool UndervoltOffsetMatches(double appliedMv, double requestedMv, bool isAmdCpu) =>
            Math.Abs(appliedMv - requestedMv) <= 0.5 ||
            (isAmdCpu && Math.Abs(appliedMv - requestedMv) < AmdCoStepMv && Math.Abs(appliedMv) <= Math.Abs(requestedMv));

        private static bool UndervoltOffsetIsExact(double appliedMv, double requestedMv) =>
            Math.Abs(appliedMv - requestedMv) <= 0.5;

        public static string BuildUndervoltStatusText(double requestedCoreMv, double requestedCacheMv, UndervoltStatus? status, bool isAmdCpu)
        {
            if (status == null)
            {
                return "Requested: n/a | Applied: n/a | Verified: awaiting telemetry";
            }

            var requested = isAmdCpu
                ? $"Requested: Core {requestedCoreMv:+0;-0;0} mV eq., iGPU {requestedCacheMv:+0;-0;0} mV eq."
                : $"Requested: Core {requestedCoreMv:+0;-0;0} mV, Cache {requestedCacheMv:+0;-0;0} mV";

            var applied = isAmdCpu
                ? $"Applied: Core {status.CurrentCoreOffsetMv:+0;-0;0} mV eq., iGPU {status.CurrentCacheOffsetMv:+0;-0;0} mV eq."
                : $"Applied: Core {status.CurrentCoreOffsetMv:+0;-0;0} mV, Cache {status.CurrentCacheOffsetMv:+0;-0;0} mV";

            string verified;
            if (status.HasExternalController)
            {
                verified = $"Verified: blocked by external controller ({status.ExternalController})";
            }
            else if (!string.IsNullOrWhiteSpace(status.Error))
            {
                verified = $"Verified: failed ({status.Error})";
            }
            else if (!string.IsNullOrWhiteSpace(status.Warning))
            {
                verified = $"Verified: warning ({status.Warning})";
            }
            else
            {
                var coreMatch = UndervoltOffsetMatches(status.CurrentCoreOffsetMv, requestedCoreMv, isAmdCpu);
                var cacheMatch = UndervoltOffsetMatches(status.CurrentCacheOffsetMv, requestedCacheMv, isAmdCpu);
                var rounded = !UndervoltOffsetIsExact(status.CurrentCoreOffsetMv, requestedCoreMv) ||
                              !UndervoltOffsetIsExact(status.CurrentCacheOffsetMv, requestedCacheMv);
                verified = coreMatch && cacheMatch
                    ? (rounded
                        ? $"Verified: applied at the nearest Curve Optimizer step ({AmdCoStepMv:0} mV eq. per step)"
                        : status.HasIndependentReadback
                        ? "Verified: readback matches requested"
                        : "Verified: write acknowledged (no independent hardware readback on this path)")
                    : status.ControlledByOmenCore
                        ? "Verified: mismatch between requested and readback"
                        : "Verified: not controlled by OmenCore";
            }

            return $"{requested} | {applied} | {verified}";
        }

        public static string BuildGpuOcStatusText(
            int requestedCore,
            int requestedMemory,
            int requestedPower,
            int requestedVoltage,
            int appliedCore,
            int appliedMemory,
            int appliedPower,
            int appliedVoltage,
            bool backendAvailable,
            bool allWritesSucceeded)
        {
            if (!backendAvailable)
            {
                return "Requested: n/a | Applied: n/a | Verified: GPU tuning backend unavailable";
            }

            var requested = $"Requested: Core {FormatSigned(requestedCore, "MHz")}, Mem {FormatSigned(requestedMemory, "MHz")}, Power {requestedPower}%, Voltage {FormatSigned(requestedVoltage, "mV")}";
            var applied = $"Applied: Core {FormatSigned(appliedCore, "MHz")}, Mem {FormatSigned(appliedMemory, "MHz")}, Power {appliedPower}%, Voltage {FormatSigned(appliedVoltage, "mV")}";

            string verified;
            if (!allWritesSucceeded)
            {
                verified = "Verified: one or more writes failed";
            }
            else if (requestedCore == appliedCore && requestedMemory == appliedMemory && requestedPower == appliedPower && requestedVoltage == appliedVoltage)
            {
                verified = "Verified: readback matches requested";
            }
            else
            {
                verified = "Verified: mismatch between requested and readback";
            }

            return $"{requested} | {applied} | {verified}";
        }

        private static string FormatSigned(int value, string unit)
        {
            return value >= 0 ? $"+{value} {unit}" : $"{value} {unit}";
        }
    }
}
