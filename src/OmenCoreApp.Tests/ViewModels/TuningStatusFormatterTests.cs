using FluentAssertions;
using OmenCore.Models;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class TuningStatusFormatterTests
    {
        [Fact]
        public void BuildUndervoltStatusText_WhenReadbackMatchesRequested_ReturnsVerifiedMatch()
        {
            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = -80,
                CurrentCacheOffsetMv = -80,
                ControlledByOmenCore = true
            };

            var text = TuningStatusFormatter.BuildUndervoltStatusText(-80, -80, status, isAmdCpu: false);

            text.Should().Contain("Requested:");
            text.Should().Contain("Applied:");
            text.Should().Contain("Verified: readback matches requested");
        }

        [Fact]
        public void BuildUndervoltStatusText_WhenExternalControllerPresent_ReturnsBlockedVerification()
        {
            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = 0,
                CurrentCacheOffsetMv = 0,
                ExternalController = "Intel XTU"
            };

            var text = TuningStatusFormatter.BuildUndervoltStatusText(-60, -60, status, isAmdCpu: false);

            text.Should().Contain("Verified: blocked by external controller (Intel XTU)");
        }

        [Fact]
        public void BuildUndervoltStatusText_OnAmd_NamesTheSecondChannelIgpuNotCache()
        {
            // The CacheMv model field carries the iGPU Curve Optimizer offset on AMD. Every
            // user-visible string for it has to say so - a status line reading "Cache -60 mV" on a
            // Ryzen part describes a domain OmenCore never writes there.
            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = -80,
                CurrentCacheOffsetMv = -60,
                ControlledByOmenCore = true
            };

            var text = TuningStatusFormatter.BuildUndervoltStatusText(-80, -60, status, isAmdCpu: true);

            text.Should().Contain("iGPU");
            text.Should().NotContain("Cache");
        }

        [Fact]
        public void BuildUndervoltStatusText_WhenIgpuOffsetWasDropped_ExplainsInsteadOfReportingBareMismatch()
        {
            // A requested iGPU offset that no SMU message was ever sent for reads back as 0, which
            // on its own is indistinguishable from a write that was attempted and failed. The
            // provider's warning is what tells the two apart, so it has to win over the mismatch.
            var status = new UndervoltStatus
            {
                CurrentCoreOffsetMv = -80,
                CurrentCacheOffsetMv = 0,
                ControlledByOmenCore = true,
                IgpuOffsetRequestedButNotApplied = true,
                Warning = "The iGPU Curve Optimizer offset was requested but not written: OmenCore has no confirmed iGPU CO message for this CPU."
            };

            var text = TuningStatusFormatter.BuildUndervoltStatusText(-80, -60, status, isAmdCpu: true);

            text.Should().Contain("Verified: warning");
            text.Should().Contain("no confirmed iGPU CO message");
            text.Should().NotContain("mismatch between requested and readback");
        }

        [Fact]
        public void BuildGpuOcStatusText_WhenReadbackDiffers_ReturnsMismatchVerification()
        {
            var text = TuningStatusFormatter.BuildGpuOcStatusText(
                requestedCore: 150,
                requestedMemory: 300,
                requestedPower: 110,
                requestedVoltage: 20,
                appliedCore: 100,
                appliedMemory: 250,
                appliedPower: 110,
                appliedVoltage: 20,
                backendAvailable: true,
                allWritesSucceeded: true);

            text.Should().Contain("Requested:");
            text.Should().Contain("Applied:");
            text.Should().Contain("Verified: mismatch between requested and readback");
        }
    }
}
