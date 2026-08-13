using System;
using System.Linq;

namespace OmenCore.Models
{
    /// <summary>
    /// Central safety limits for CPU/GPU tuning requests.
    /// </summary>
    public static class TuningGuardrails
    {
        public const double IntelCpuUndervoltMinMv = -150;
        public const double AmdCurveOptimizerEquivalentMinMv = -120;
        public const double CpuUndervoltMaxMv = 0;
        public const int GpuVoltageOffsetMinMv = -200;
        public const int GpuVoltageOffsetMaxMv = 100;

        // ─── GPU clock/power policy ceilings ───────────────────────────────────────────
        // These are the app-wide bounds any GPU tuning backend will ever request, regardless
        // of what the driver or a heuristic reports. Driver-reported values (e.g. NvapiService's
        // NVAPI power-policy query) may only narrow these further at runtime, never widen past
        // them. Consolidated from what were previously separate hardcoded literals in
        // NvapiService and AmdGpuService - see docs/TUNING-SUBSYSTEMS-REVIEW.md, finding F7.

        /// <summary>NVIDIA GPU core clock offset ceiling, in MHz (NvapiService). Desktop-class
        /// default before DetectLimits()'s laptop narrowing and any live driver query.</summary>
        public const int NvidiaGpuCoreClockOffsetMinMHz = -500;
        public const int NvidiaGpuCoreClockOffsetMaxMHz = 300;

        /// <summary>Narrower core clock ceiling NvapiService applies for laptop/Max-Q/Mobile GPUs.</summary>
        public const int NvidiaGpuCoreClockOffsetMaxMHzLaptop = 200;

        public const int NvidiaGpuMemoryClockOffsetMinMHz = -500;
        public const int NvidiaGpuMemoryClockOffsetMaxMHz = 2000;

        public const int NvidiaGpuPowerLimitMinPercent = 50;
        public const int NvidiaGpuPowerLimitMaxPercent = 125;

        /// <summary>Narrower power-limit ceiling NvapiService applies for laptop/Max-Q/Mobile GPUs.</summary>
        public const int NvidiaGpuPowerLimitMaxPercentLaptop = 115;

        /// <summary>
        /// AMD dGPU (AmdGpuService) core AND memory clock offset bounds, in MHz. The current
        /// implementation does not distinguish core from memory offset - both are clamped to
        /// this same range - so this is deliberately one constant pair, not two, to avoid
        /// implying a distinction the code doesn't make.
        /// </summary>
        public const int AmdGpuClockOffsetMinMHz = -500;
        public const int AmdGpuClockOffsetMaxMHz = 500;

        /// <summary>AMD Curve Optimizer safe range, in CO counts (not millivolts - the mV figure
        /// shown in the UI is a derived approximation, see AmdUndervoltProvider.ApplyOffsetAsync).</summary>
        public const int AmdCurveOptimizerMinCount = -30;
        public const int AmdCurveOptimizerMaxCount = 30;

        public static double ClampCpuUndervoltMv(double value, bool amdCurveOptimizer)
        {
            var min = amdCurveOptimizer ? AmdCurveOptimizerEquivalentMinMv : IntelCpuUndervoltMinMv;
            return Math.Clamp(value, min, CpuUndervoltMaxMv);
        }

        public static int ClampCpuUndervoltMv(int value, bool amdCurveOptimizer)
        {
            var min = amdCurveOptimizer ? (int)AmdCurveOptimizerEquivalentMinMv : (int)IntelCpuUndervoltMinMv;
            return Math.Clamp(value, min, (int)CpuUndervoltMaxMv);
        }

        public static UndervoltOffset ClampCpuUndervoltOffset(UndervoltOffset offset, bool amdCurveOptimizer)
        {
            return new UndervoltOffset
            {
                CoreMv = ClampCpuUndervoltMv(offset.CoreMv, amdCurveOptimizer),
                CacheMv = ClampCpuUndervoltMv(offset.CacheMv, amdCurveOptimizer),
                PerCoreOffsetsMv = offset.PerCoreOffsetsMv?
                    .Select(x => x.HasValue ? ClampCpuUndervoltMv(x.Value, amdCurveOptimizer) : (int?)null)
                    .ToArray()
            };
        }

        public static int ClampGpuVoltageOffsetMv(int value)
        {
            return Math.Clamp(value, GpuVoltageOffsetMinMv, GpuVoltageOffsetMaxMv);
        }

        /// <summary>
        /// Narrows a driver/hardware-reported (min, max) range so it never exceeds the given
        /// policy ceiling - the range can only get tighter than (policyMin, policyMax), never
        /// wider. Also tolerates a driver reporting an inverted or degenerate range (min &gt;
        /// max) by collapsing to the policy minimum rather than throwing, since callers
        /// typically feed this straight into Math.Clamp. See finding F7 (narrow-only) and F12
        /// (defend against a driver-reported inverted range).
        /// </summary>
        public static (int Min, int Max) NarrowToPolicy(int reportedMin, int reportedMax, int policyMin, int policyMax)
        {
            var min = Math.Max(reportedMin, policyMin);
            var max = Math.Min(reportedMax, policyMax);
            if (min <= max)
            {
                return (min, max);
            }

            // Degenerate/inverted driver range - collapse to the policy floor rather than to
            // `min`, which (as reportedMin=200/policyMax=125 demonstrates) is not itself
            // guaranteed to fall within [policyMin, policyMax].
            return (policyMin, policyMin);
        }
    }
}
