using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class CpuTemperatureSourceComparisonTests
    {
        [Fact]
        public void SourcesDisagreeMaterially_NoSourcesAvailable_IsFalse()
        {
            var comparison = new CpuTemperatureSourceComparison();

            comparison.SourcesDisagreeMaterially.Should().BeFalse();
            comparison.AvailableSourceCount.Should().Be(0);
        }

        [Fact]
        public void SourcesDisagreeMaterially_OnlyOneSourceAvailable_IsFalse()
        {
            // Can't disagree with itself - one reading is never "materially disagreeing".
            var comparison = new CpuTemperatureSourceComparison { WmiBiosTempC = 45.0 };

            comparison.SourcesDisagreeMaterially.Should().BeFalse();
            comparison.AvailableSourceCount.Should().Be(1);
        }

        [Fact]
        public void SourcesDisagreeMaterially_AllSourcesClose_IsFalse()
        {
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 78.0,
                AcpiTempC = 80.5,
                LhmTempC = 81.2
            };

            comparison.SourcesDisagreeMaterially.Should().BeFalse();
            comparison.AvailableSourceCount.Should().Be(3);
        }

        [Fact]
        public void SourcesDisagreeMaterially_ExactlyAtThreshold_IsFalse()
        {
            // Spread == threshold, not > threshold, so this must not trip - matches
            // WmiBiosMonitor.MaxAcpiDeltaFromWmiC's own strict-greater-than comparison.
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 40.0,
                AcpiTempC = 40.0 + CpuTemperatureSourceComparison.DisagreementThresholdC
            };

            comparison.SourcesDisagreeMaterially.Should().BeFalse();
        }

        [Fact]
        public void SourcesDisagreeMaterially_JustOverThreshold_IsTrue()
        {
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 40.0,
                AcpiTempC = 40.0 + CpuTemperatureSourceComparison.DisagreementThresholdC + 0.1
            };

            comparison.SourcesDisagreeMaterially.Should().BeTrue();
        }

        [Fact]
        public void SourcesDisagreeMaterially_ReproducesFieldReportedShape()
        {
            // The exact shape from the r/HPOmen report this whole investigation started from:
            // ACPI zone latched onto a cool sensor while WMI BIOS reports the real temperature.
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 81.2,
                AcpiTempC = 36.0
            };

            comparison.SourcesDisagreeMaterially.Should().BeTrue();
        }

        [Fact]
        public void SourcesDisagreeMaterially_TwoAgreeOneOutlier_IsTrue()
        {
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 80.0,
                AcpiTempC = 82.0,
                LhmTempC = 35.0
            };

            comparison.SourcesDisagreeMaterially.Should().BeTrue();
        }

        [Fact]
        public void AvailableSourceCount_CountsOnlyNonNullSources()
        {
            var comparison = new CpuTemperatureSourceComparison
            {
                WmiBiosTempC = 75.0,
                AcpiTempC = null,
                LhmTempC = 76.0
            };

            comparison.AvailableSourceCount.Should().Be(2);
        }
    }
}
