using System;
using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Pins the AMD SMU power-limit contract: the message IDs, which families they apply to, and
    /// the acceptance semantics of the result types.
    ///
    /// These are all pure functions, deliberately. The parts that need silicon were verified on
    /// hardware instead - see tools/SmuProbe --limits and the measurement quoted in
    /// RyzenControl.GetMaxPowerLimitMw. A unit test cannot tell whether a limit reached the SMU;
    /// what it can do is stop the values that were measured from drifting unnoticed.
    /// </summary>
    public class AmdPowerLimitTests
    {
        // ── Message IDs ──────────────────────────────────────────────────────────────────────
        //
        // From RyzenAdj lib/api.c, FAM_STRIXPOINT. A wrong ID here is the worst kind of defect on
        // this path: the SMU returns Ok for message IDs that do nothing, so it fails silently.

        [Fact]
        public void MessageIds_MatchRyzenAdj()
        {
            AmdUndervoltProvider.Mp1SetStapmLimit.Should().Be(0x14, "RyzenAdj set_stapm_limit sends MP1 0x14");
            AmdUndervoltProvider.PsmuSetStapmLimit.Should().Be(0x31, "RyzenAdj retries set_stapm_limit on PSMU 0x31");
            AmdUndervoltProvider.Mp1SetFastLimit.Should().Be(0x15, "RyzenAdj set_fast_limit sends MP1 0x15");
            AmdUndervoltProvider.Mp1SetSlowLimit.Should().Be(0x16, "RyzenAdj set_slow_limit sends MP1 0x16");
            AmdUndervoltProvider.Mp1SetApuSlowLimit.Should().Be(0x23, "RyzenAdj set_apu_slow_limit sends MP1 0x23");
        }

        [Fact]
        public void MessageIds_AreDistinct()
        {
            var mp1 = new[]
            {
                AmdUndervoltProvider.Mp1SetStapmLimit,
                AmdUndervoltProvider.Mp1SetFastLimit,
                AmdUndervoltProvider.Mp1SetSlowLimit,
                AmdUndervoltProvider.Mp1SetApuSlowLimit
            };

            mp1.Should().OnlyHaveUniqueItems(
                "two limits sharing an MP1 message id would silently overwrite each other");
        }

        // ── Family coverage ──────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(RyzenFamily.StrixPoint)]
        [InlineData(RyzenFamily.StrixHalo)]
        [InlineData(RyzenFamily.Phoenix)]
        [InlineData(RyzenFamily.HawkPoint)]
        [InlineData(RyzenFamily.Rembrandt)]
        [InlineData(RyzenFamily.RenoirLucienne)]
        [InlineData(RyzenFamily.CezanneBarcelo)]
        [InlineData(RyzenFamily.VanGogh)]
        [InlineData(RyzenFamily.Mendocino)]
        public void FamilySupportsPptLimits_MatchesRyzenAdjCaseList(RyzenFamily family)
        {
            AmdUndervoltProvider.FamilySupportsPptLimits(family).Should().BeTrue();
        }

        [Theory]
        [InlineData(RyzenFamily.Raven)]
        [InlineData(RyzenFamily.Picasso)]
        [InlineData(RyzenFamily.Dali)]
        [InlineData(RyzenFamily.Zen1Plus)]
        [InlineData(RyzenFamily.Unknown)]
        public void FamilySupportsPptLimits_ExcludesFamiliesWithNoConfirmedId(RyzenFamily family)
        {
            AmdUndervoltProvider.FamilySupportsPptLimits(family).Should().BeFalse(
                "these use a different message scheme and no id has been confirmed for them");
        }

        [Theory]
        [InlineData(RyzenFamily.RenoirLucienne)]
        [InlineData(RyzenFamily.CezanneBarcelo)]
        [InlineData(RyzenFamily.VanGogh)]
        [InlineData(RyzenFamily.Rembrandt)]
        [InlineData(RyzenFamily.Phoenix)]
        [InlineData(RyzenFamily.HawkPoint)]
        public void FamilySupportsIgpuCurveOptimizer_MatchesRyzenAdjSetCogfxCaseList(RyzenFamily family)
        {
            AmdUndervoltProvider.FamilySupportsIgpuCurveOptimizer(family).Should().BeTrue();
        }

        [Theory]
        [InlineData(RyzenFamily.StrixPoint)]
        [InlineData(RyzenFamily.StrixHalo)]
        [InlineData(RyzenFamily.Mendocino)]
        [InlineData(RyzenFamily.RaphaelDragonRange)]
        [InlineData(RyzenFamily.FireRange)]
        public void FamilySupportsIgpuCurveOptimizer_ExcludesFamiliesSetCogfxDoesNotList(RyzenFamily family)
        {
            // RyzenAdj's set_cogfx has no case for any of these. Strix Halo has one carrying only
            // the comment "0xB7 is rejected on this architecture" - a measured refusal, not a gap.
            // The same file maps Strix Point for set_coall (0x4C) and set_coper (0x4b), so its
            // absence here is about the graphics curve specifically, not about the family.
            //
            // These were in the 0xB7 arm of SetIgpuCO. Curve Optimizer offsets that go to the wrong
            // mailbox message are the failure mode with no readback to catch it.
            AmdUndervoltProvider.FamilySupportsIgpuCurveOptimizer(family).Should().BeFalse(
                "set_cogfx does not list this family and no id has been measured for it here");
        }

        [Theory]
        [InlineData(RyzenFamily.RenoirLucienne)]
        [InlineData(RyzenFamily.CezanneBarcelo)]
        [InlineData(RyzenFamily.VanGogh)]
        [InlineData(RyzenFamily.Mendocino)]
        public void FamilySupportsApuSlowLimit_IsNarrowerThanPptLimits(RyzenFamily family)
        {
            // RyzenAdj's set_apu_slow_limit case list omits these, even though set_fast_limit and
            // set_slow_limit include them. Widening it for tidiness would send 0x23 to parts that
            // were never confirmed to take it.
            AmdUndervoltProvider.FamilySupportsPptLimits(family).Should().BeTrue();
            AmdUndervoltProvider.FamilySupportsApuSlowLimit(family).Should().BeFalse();
        }

        [Theory]
        [InlineData(RyzenFamily.Rembrandt)]
        [InlineData(RyzenFamily.Phoenix)]
        [InlineData(RyzenFamily.HawkPoint)]
        [InlineData(RyzenFamily.StrixPoint)]
        [InlineData(RyzenFamily.StrixHalo)]
        public void FamilySupportsApuSlowLimit_CoversTheFamiliesRyzenAdjLists(RyzenFamily family)
        {
            AmdUndervoltProvider.FamilySupportsApuSlowLimit(family).Should().BeTrue();
        }

        [Fact]
        public void FamilySupportsApuSlowLimit_NeverExceedsPptLimitCoverage()
        {
            foreach (RyzenFamily family in Enum.GetValues<RyzenFamily>())
            {
                if (AmdUndervoltProvider.FamilySupportsApuSlowLimit(family))
                {
                    AmdUndervoltProvider.FamilySupportsPptLimits(family).Should().BeTrue(
                        $"{family} claims the APU-slow domain without the PPT limits it sits alongside");
                }
            }
        }

        // ── Power-limit ceiling ──────────────────────────────────────────────────────────────

        [Fact]
        public void MaxPowerLimit_ForStrixPoint_ClearsTheMeasuredStockLimit()
        {
            // Measured on board 8D87 / Ryzen AI 9 HX 375: firmware stock is 45 W, the part is
            // genuinely pinned there under load, and 70 W was reached and sustained. A ceiling
            // that does not clear stock by a useful margin makes the control pointless.
            uint mw = RyzenControl.GetMaxPowerLimitMw(RyzenFamily.StrixPoint);

            mw.Should().BeGreaterThan(70_000, "70 W was measured as reachable on this silicon");
            mw.Should().Be(100_000);
        }

        [Fact]
        public void MaxPowerLimit_ForUncharacterisedFamilies_KeepsTheConservativeBound()
        {
            RyzenControl.GetMaxPowerLimitMw(RyzenFamily.Unknown).Should().Be(54_000);
            RyzenControl.GetMaxPowerLimitMw(RyzenFamily.Zen1Plus).Should().Be(54_000);
            RyzenControl.GetMaxPowerLimitMw(RyzenFamily.Matisse).Should().Be(54_000);
        }

        [Theory]
        [InlineData(RyzenFamily.VanGogh)]
        [InlineData(RyzenFamily.Mendocino)]
        public void MaxPowerLimit_ForHandheldClassParts_StaysLow(RyzenFamily family)
        {
            // A 15 W part in a handheld has no cooling for a mainstream APU's ceiling, and these
            // are the families most likely to be harmed by inheriting a generous default.
            RyzenControl.GetMaxPowerLimitMw(family).Should().Be(30_000);
        }

        [Fact]
        public void MaxPowerLimit_IsAlwaysAboveTheMinimum()
        {
            foreach (RyzenFamily family in Enum.GetValues<RyzenFamily>())
            {
                RyzenControl.GetMaxPowerLimitMw(family).Should().BeGreaterThan(15_000,
                    $"{family} would otherwise clamp every request to a single value");
            }
        }

        // ── Result semantics ─────────────────────────────────────────────────────────────────
        //
        // "Accepted" has to mean the right thing in both directions. An unrequested limit is not
        // a failure, and a requested one that the mailbox refused must not read as success.

        [Fact]
        public void Step_NotRequested_CountsAsAccepted()
        {
            var step = new AmdPowerLimitStep { Requested = false };

            step.Accepted.Should().BeTrue("a limit the caller did not ask to change cannot fail");
            step.ToString().Should().Be("unchanged");
        }

        [Fact]
        public void Step_RequestedAndRefused_IsNotAccepted()
        {
            var step = new AmdPowerLimitStep
            {
                Requested = true,
                RequestedMw = 45_000,
                Status = RyzenSmu.SmuStatus.UnknownCmd
            };

            step.Accepted.Should().BeFalse();
        }

        [Fact]
        public void Step_ReportsClampingInItsDescription()
        {
            var step = new AmdPowerLimitStep
            {
                Requested = true,
                RequestedMw = 54_000,
                WasClamped = true,
                Status = RyzenSmu.SmuStatus.Ok
            };

            step.ToString().Should().Contain("clamped",
                "a silently clamped request is how a user ends up believing a limit they never got");
        }

        [Fact]
        public void Report_AllAccepted_IsTrueWhenNothingWasRequested()
        {
            new AmdPowerLimitReport().AllAccepted.Should().BeTrue();
        }

        [Fact]
        public void Report_AnyAccepted_IsFalseWhenNothingWasRequested()
        {
            // The distinction matters: AllAccepted answers "did anything fail", AnyAccepted
            // answers "did anything happen". An empty request must answer no to the second.
            new AmdPowerLimitReport().AnyAccepted.Should().BeFalse();
        }

        [Fact]
        public void Report_OneRefusedLimit_FailsAllAcceptedButNotAnyAccepted()
        {
            var report = new AmdPowerLimitReport
            {
                Stapm = Ok(45_000),
                Fast = Ok(45_000),
                Slow = Ok(45_000),
                ApuSlow = new AmdPowerLimitStep
                {
                    Requested = true,
                    RequestedMw = 45_000,
                    Status = RyzenSmu.SmuStatus.UnknownCmd
                }
            };

            report.AllAccepted.Should().BeFalse();
            report.AnyAccepted.Should().BeTrue(
                "the other three landed - a part that refuses one limit may well accept the rest");
        }

        private static AmdPowerLimitStep Ok(uint mw) => new()
        {
            Requested = true,
            RequestedMw = mw,
            Status = RyzenSmu.SmuStatus.Ok
        };

        // ── The model ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RyzenPowerLimits_DefaultsToLeavingEveryLimitAlone()
        {
            // ApplyPowerLimits reads 0 as "do not touch". If any of these defaulted to a real
            // value, constructing the object would quietly request a limit change.
            var limits = new RyzenPowerLimits();

            limits.StapmLimit.Should().Be(0);
            limits.FastLimit.Should().Be(0);
            limits.SlowLimit.Should().Be(0);
            limits.ApuSlowLimit.Should().Be(0);
        }
    }
}
