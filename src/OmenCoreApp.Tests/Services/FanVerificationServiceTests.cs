using System.Reflection;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class FanVerificationServiceTests
    {
        [Fact]
        public void VerifyAppliedState_Passes_WhenLevelReadbackMatches_ButRpmCurveDoesNot()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var service = new FanVerificationService(wmiBios: null, fanService: null, logging);

            var result = new FanApplyResult
            {
                RequestedPercent = 60,
                ExpectedRpm = 3600,
                ActualRpmAfter = 2000,
                ExpectedLevel = 33,
                ActualLevelAfter = 33,
                LevelReadbackMatched = true,
                WmiCallSucceeded = true
            };

            var method = typeof(FanVerificationService).GetMethod("VerifyAppliedState", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var verified = (bool)method!.Invoke(service, new object[] { result })!;
            verified.Should().BeTrue("matching WMI level readback should count as successful application even when the shared RPM curve is off for this model");
        }

        [Fact]
        public void VerificationScore_UsesLevelReadbackFloor_WhenCommandApplied()
        {
            var result = new FanApplyResult
            {
                RequestedPercent = 60,
                ExpectedRpm = 3600,
                ActualRpmAfter = 2000,
                ExpectedLevel = 33,
                ActualLevelAfter = 33,
                LevelReadbackMatched = true,
                WmiCallSucceeded = true,
                VerificationPassed = true,
                RpmStandardDeviation = 50,
                ActualRpmBefore = 2000
            };

            result.VerificationScore.Should().BeGreaterThanOrEqualTo(60,
                "a matched firmware level should not be reported as a Poor/near-failed result solely because the generic expected RPM curve was too aggressive");
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(0, 2, true)]
        [InlineData(33, 33, true)]
        [InlineData(33, 35, true)]
        [InlineData(33, 36, true)]   // actualLevel > expectedLevel: fan coasting down, not a failure
        [InlineData(55, 53, true)]
        [InlineData(55, 51, false)]
        public void IsLevelReadbackMatch_UsesSmallToleranceWindow(int expectedLevel, int actualLevel, bool expectedMatch)
        {
            var result = new FanApplyResult
            {
                RequestedPercent = expectedLevel >= 55 ? 100 : 60,
                ExpectedLevel = expectedLevel,
                ActualLevelAfter = actualLevel
            };

            var method = typeof(FanVerificationService).GetMethod("IsLevelReadbackMatch", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var matched = (bool)method!.Invoke(null, new object[] { result })!;
            matched.Should().Be(expectedMatch);
        }

        [Fact]
        public void DeviationPercent_Is100_WhenExpectedRpmIsZeroButActualIsNonZero()
        {
            var result = new FanApplyResult
            {
                ExpectedRpm = 0,
                ActualRpmAfter = 3100
            };

            result.DeviationPercent.Should().Be(100,
                "expected 0 RPM with non-zero measured RPM should report a clear mismatch instead of 0% deviation");
        }

        [Fact]
        public void RestoreFanControlAfterCalibration_ReturnsFalse_WhenNoBackendAvailable()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var service = new FanVerificationService(wmiBios: null, fanService: null, logging);

            service.RestoreFanControlAfterCalibration().Should().BeFalse();
        }
    }
}
