using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Covers <c>WmiBiosMonitor.IsIdenticalTempSuspicious</c>, the load-variance gate that stops
    /// thermal equilibrium being misreported as a frozen sensor.
    ///
    /// Field evidence this gate was added for (GitHub #152/#153, board 8A18): one diagnostics bundle
    /// contained 48 "🥶 appears frozen" warnings in a single session, including a GPU pinned at
    /// 100% load steadily reading 48°C. HP WMI BIOS reports whole degrees, so an unchanging integer
    /// at equilibrium is a healthy sensor — and users reasonably read the warning flood as proof that
    /// temperature reporting was broken.
    /// </summary>
    public class WmiBiosMonitorFreezeHeuristicTests
    {
        private static bool IsSuspicious(int consecutiveIdenticalReads, double loadMin, double loadMax)
        {
            var method = typeof(WmiBiosMonitor).GetMethod(
                "IsIdenticalTempSuspicious",
                BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull("IsIdenticalTempSuspicious should exist on WmiBiosMonitor");

            return (bool)method!.Invoke(null, new object[] { consecutiveIdenticalReads, loadMin, loadMax })!;
        }

        [Fact]
        public void SteadyLoadHoldingSteadyTemp_IsNotReportedAsFrozen()
        {
            // The exact shape from the #153 log: GPU pinned near 100% load, temperature parked on one
            // integer for 21 consecutive reads. Load barely moved, so equilibrium fully explains it.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 93, loadMax: 100)
                .Should().BeFalse("a GPU at sustained full load sitting at equilibrium is not a stuck sensor");
        }

        [Fact]
        public void IdleMachineHoldingSteadyTemp_IsNotReportedAsFrozen()
        {
            // Also from the field logs: idle GPU, 41 identical reads, load flat near zero.
            IsSuspicious(consecutiveIdenticalReads: 41, loadMin: 0, loadMax: 2)
                .Should().BeFalse("an idle sensor at equilibrium is not a stuck sensor");
        }

        [Fact]
        public void TempUnchangedWhileLoadSwingsWidely_IsStillReportedAsFrozen()
        {
            // The signal that actually indicates a wedged sensor: load moved a long way and the
            // temperature did not shift by even one quantization step.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 5, loadMax: 85)
                .Should().BeTrue("an 80-point load swing with zero temperature movement is genuinely suspicious");
        }

        [Fact]
        public void LoadSwingExactlyAtThreshold_IsReportedAsFrozen()
        {
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 10, loadMax: 25)
                .Should().BeTrue("a swing meeting the 15-point threshold should trip detection");
        }

        [Fact]
        public void LoadSwingJustUnderThreshold_IsNotReportedAsFrozen()
        {
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: 10, loadMax: 24)
                .Should().BeFalse("a swing under the threshold is ordinary equilibrium drift");
        }

        [Fact]
        public void AbsoluteReadCeiling_StillCatchesSensorWedgedUnderConstantLoad()
        {
            // Backstop: if load genuinely never varies, the load gate can never fire, so a very long
            // identical run must still be surfaced rather than hidden forever.
            IsSuspicious(consecutiveIdenticalReads: 150, loadMin: 50, loadMax: 50)
                .Should().BeTrue("a sensor identical for 150 reads should trip the absolute ceiling");

            IsSuspicious(consecutiveIdenticalReads: 149, loadMin: 50, loadMax: 50)
                .Should().BeFalse("just below the ceiling, constant load still explains a constant temperature");
        }

        [Fact]
        public void NoLoadObservationsRecorded_IsTreatedAsNoSwing()
        {
            // Sentinel state before any observation (min > max) must not be read as an enormous swing.
            IsSuspicious(consecutiveIdenticalReads: 21, loadMin: double.MaxValue, loadMax: double.MinValue)
                .Should().BeFalse("an unpopulated load range must not be interpreted as a load swing");
        }
    }
}
