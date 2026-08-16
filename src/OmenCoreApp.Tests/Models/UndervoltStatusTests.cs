using FluentAssertions;
using OmenCore.Models;
using Xunit;

namespace OmenCoreApp.Tests.Models
{
    /// <summary>
    /// Regression tests locking in the honesty fixes from docs/TUNING-SUBSYSTEMS-REVIEW.md,
    /// findings F1 (per-core offsets silently dropped) and F9 (external-controller offset
    /// reported as a fabricated zero instead of "not read").
    /// </summary>
    public class UndervoltStatusTests
    {
        [Fact]
        public void PerCoreOffsetsRequestedButNotApplied_DefaultsToFalse()
        {
            // F1: must be explicitly set true by a provider, never true by accident.
            new UndervoltStatus().PerCoreOffsetsRequestedButNotApplied.Should().BeFalse();
        }

        [Fact]
        public void IgpuOffsetRequestedButNotApplied_DefaultsToFalse()
        {
            // Same rule as F1: a dropped iGPU Curve Optimizer offset is a claim a provider has to
            // make deliberately, so an unset status never accuses a backend of dropping one.
            new UndervoltStatus().IgpuOffsetRequestedButNotApplied.Should().BeFalse();
        }

        [Fact]
        public void ExternalCoreAndCacheOffsetMv_DefaultToNull()
        {
            // F9: null must mean "not read", distinct from a confirmed 0 mV reading.
            var status = new UndervoltStatus();

            status.ExternalCoreOffsetMv.Should().BeNull();
            status.ExternalCacheOffsetMv.Should().BeNull();
        }

        [Fact]
        public void ExternalUndervoltInfo_Offset_DefaultsToNull()
        {
            // F9: detection here is presence-only (a service/process is running) - it must not
            // default to a fabricated UndervoltOffset { CoreMv = 0, CacheMv = 0 } that reads as
            // "confirmed zero" to a caller.
            new ExternalUndervoltInfo().Offset.Should().BeNull();
        }
    }
}
