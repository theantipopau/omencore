using FluentAssertions;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Models
{
    public class TuningGuardrailsTests
    {
        [Theory]
        [InlineData(-250, -150)]
        [InlineData(-75, -75)]
        [InlineData(25, 0)]
        public void ClampCpuUndervoltMv_Intel_StaysWithinSafeUndervoltRange(double requested, double expected)
        {
            TuningGuardrails.ClampCpuUndervoltMv(requested, amdCurveOptimizer: false)
                .Should().Be(expected);
        }

        [Theory]
        [InlineData(-250, -120)]
        [InlineData(-96, -96)]
        [InlineData(10, 0)]
        public void ClampCpuUndervoltMv_AmdCurveOptimizer_StaysWithinCoEquivalentRange(double requested, double expected)
        {
            TuningGuardrails.ClampCpuUndervoltMv(requested, amdCurveOptimizer: true)
                .Should().Be(expected);
        }

        [Fact]
        public void ClampCpuUndervoltOffset_ClampsGlobalAndPerCoreOffsets()
        {
            var offset = new UndervoltOffset
            {
                CoreMv = -300,
                CacheMv = 15,
                PerCoreOffsetsMv = new int?[] { -250, -100, 20, null }
            };

            var safe = TuningGuardrails.ClampCpuUndervoltOffset(offset, amdCurveOptimizer: false);

            safe.CoreMv.Should().Be(-150);
            safe.CacheMv.Should().Be(0);
            safe.PerCoreOffsetsMv.Should().Equal(-150, -100, 0, null);
        }

        [Theory]
        [InlineData(-500, -200)]
        [InlineData(-75, -75)]
        [InlineData(250, 100)]
        public void ClampGpuVoltageOffsetMv_StaysWithinProviderRange(int requested, int expected)
        {
            TuningGuardrails.ClampGpuVoltageOffsetMv(requested).Should().Be(expected);
        }

        // ── NarrowToPolicy (finding F7: driver-reported GPU limits may only narrow the app's
        // policy ceiling, never widen past it) ─────────────────────────────────────────────

        [Fact]
        public void NarrowToPolicy_DriverRangeInsidePolicy_PassesThroughUnchanged()
        {
            var (min, max) = TuningGuardrails.NarrowToPolicy(
                reportedMin: 60, reportedMax: 110, policyMin: 50, policyMax: 125);

            min.Should().Be(60);
            max.Should().Be(110);
        }

        [Fact]
        public void NarrowToPolicy_DriverRangeWiderThanPolicy_IsClampedToPolicy()
        {
            // A driver reporting a wider range than policy allows must never widen the effective
            // bound - this is the exact gap that let NvapiService trust an unclamped NVAPI
            // power-policy query.
            var (min, max) = TuningGuardrails.NarrowToPolicy(
                reportedMin: 10, reportedMax: 200, policyMin: 50, policyMax: 125);

            min.Should().Be(50, "the driver's lower min must not widen past the policy floor");
            max.Should().Be(125, "the driver's higher max must not widen past the policy ceiling");
        }

        [Fact]
        public void NarrowToPolicy_DriverRangeNarrowerThanPolicy_KeepsTheNarrowerBound()
        {
            var (min, max) = TuningGuardrails.NarrowToPolicy(
                reportedMin: 70, reportedMax: 100, policyMin: 50, policyMax: 125);

            min.Should().Be(70);
            max.Should().Be(100);
        }

        [Fact]
        public void NarrowToPolicy_InvertedDriverRange_DoesNotThrowAndStaysWithinPolicy()
        {
            // Finding F12: Math.Clamp throws if min > max. A caller that feeds this result
            // straight into Math.Clamp must never receive an inverted pair.
            var act = () => TuningGuardrails.NarrowToPolicy(
                reportedMin: 200, reportedMax: 10, policyMin: 50, policyMax: 125);

            act.Should().NotThrow();

            var (min, max) = act();
            min.Should().BeLessThanOrEqualTo(max);
            min.Should().BeGreaterThanOrEqualTo(50);
            max.Should().BeLessThanOrEqualTo(125);
        }

        // ── GPU clock/power/CO clamp constants (finding F7 consolidation - these pin the exact
        // values that were previously separate hardcoded literals in NvapiService and
        // AmdGpuService, so consolidating them into one source of truth doesn't silently change
        // any of them) ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void NvidiaGpuClockOffsetConstants_MatchPreviousNvapiServiceDefaults()
        {
            TuningGuardrails.NvidiaGpuCoreClockOffsetMinMHz.Should().Be(-500);
            TuningGuardrails.NvidiaGpuCoreClockOffsetMaxMHz.Should().Be(300);
            TuningGuardrails.NvidiaGpuCoreClockOffsetMaxMHzLaptop.Should().Be(200);
            TuningGuardrails.NvidiaGpuMemoryClockOffsetMinMHz.Should().Be(-500);
            TuningGuardrails.NvidiaGpuMemoryClockOffsetMaxMHz.Should().Be(2000);
        }

        [Fact]
        public void NvidiaGpuPowerLimitConstants_MatchPreviousNvapiServiceDefaults()
        {
            TuningGuardrails.NvidiaGpuPowerLimitMinPercent.Should().Be(50);
            TuningGuardrails.NvidiaGpuPowerLimitMaxPercent.Should().Be(125);
            TuningGuardrails.NvidiaGpuPowerLimitMaxPercentLaptop.Should().Be(115);
        }

        [Fact]
        public void AmdGpuClockOffsetConstants_MatchPreviousAmdGpuServiceHardcodedRange()
        {
            TuningGuardrails.AmdGpuClockOffsetMinMHz.Should().Be(-500);
            TuningGuardrails.AmdGpuClockOffsetMaxMHz.Should().Be(500);
        }

        [Fact]
        public void AmdCurveOptimizerConstants_MatchPreviousAmdUndervoltProviderHardcodedRange()
        {
            TuningGuardrails.AmdCurveOptimizerMinCount.Should().Be(-30);
            TuningGuardrails.AmdCurveOptimizerMaxCount.Should().Be(30);
        }
    }
}
