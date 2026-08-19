using System;
using OmenCore.Models;

namespace OmenCore.ViewModels
{
    public static class TuningStatusFormatter
    {
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
                var coreMatch = Math.Abs(status.CurrentCoreOffsetMv - requestedCoreMv) <= 0.5;
                var cacheMatch = Math.Abs(status.CurrentCacheOffsetMv - requestedCacheMv) <= 0.5;
                verified = coreMatch && cacheMatch
                    ? (status.HasIndependentReadback
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
