using System.Linq;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Roadmap I.2 / WP-44: every gated control must be able to say why it is gated, in words a
    /// user can read, and the same words must reach the diagnostics export. These tests pin the
    /// evidence each reason is allowed to cite, so a "no" never gets blamed on detection when the
    /// model database is the source.
    /// </summary>
    public class CapabilityReasonResolverTests
    {
        private static DeviceCapabilities WorkingBoard()
        {
            return new DeviceCapabilities
            {
                ProductId = "8BAD",
                ModelName = "OMEN by HP Laptop 16-b1xxx",
                IsKnownModel = true,
                ModelConfig = new ModelCapabilities
                {
                    ProductId = "8BAD",
                    ModelName = "OMEN 16-b1",
                    ModelYear = 2022,
                    Family = OmenModelFamily.OMEN16,
                    UserVerified = true,
                    SupportsFanControlWmi = true,
                    SupportsFanCurves = true,
                    SupportsIndependentFanCurves = true,
                    HasMuxSwitch = true,
                    HasFourZoneRgb = true,
                    SupportsUndervolt = true,
                    SupportsGpuPowerBoost = true
                },
                Chassis = ChassisType.Notebook,
                ModelFamily = OmenModelFamily.OMEN16,
                FanControl = FanControlMethod.WmiBios,
                CanSetFanSpeed = true,
                CanReadRpm = true,
                HasGpuPowerControl = true,
                HasMuxSwitch = true,
                HasOemPerformanceModes = true,
                AvailablePerformanceModes = new[] { "Default", "Performance", "Cool" },
                HasKeyboardBacklight = true,
                HasZoneLighting = true,
                Lighting = LightingCapability.FourZone,
                CanUndervolt = true,
                UndervoltRuntimeReady = true,
                UndervoltMethod = UndervoltMethod.IntelMsrPawnIO
            };
        }

        [Fact]
        public void WorkingBoard_ReportsEveryApplicableFeatureAsAvailable()
        {
            var reasons = CapabilityReasonResolver.Resolve(WorkingBoard());

            reasons.Should().NotBeEmpty();
            // A zone-addressable keyboard is a real, working board, but per-key effects still do
            // not apply: that one feature is correctly unavailable and says why.
            reasons.Where(r => r.Feature != CapabilityFeature.PerKeyLighting)
                .Should().OnlyContain(r => r.Available);
            reasons.Single(r => r.Feature == CapabilityFeature.PerKeyLighting).Available.Should().BeFalse();
        }

        [Fact]
        public void PerKeyBoard_ReportsNothingUnavailable()
        {
            var caps = WorkingBoard();
            caps.HasZoneLighting = false;
            caps.HasPerKeyLighting = true;
            caps.Lighting = LightingCapability.PerKey;
            caps.ModelConfig!.HasFourZoneRgb = false;
            caps.ModelConfig.HasPerKeyRgb = true;

            CapabilityReasonResolver.DescribeUnavailable(caps).Should()
                .Be("Every capability OmenCore can gate is available on this board.");
        }

        [Fact]
        public void Desktop_BlocksFanWritesWithTheSafetyPolicyAsTheSource()
        {
            var caps = WorkingBoard();
            caps.Chassis = ChassisType.Desktop;
            caps.FanControl = FanControlMethod.MonitoringOnly;
            caps.CanSetFanSpeed = false;

            var fan = caps.GetCapabilityReason(CapabilityFeature.FanControl);

            fan.Available.Should().BeFalse();
            fan.Code.Should().Be(CapabilityReasonCode.SafetyPolicy);
            fan.Source.Should().Be("safety policy");
            fan.Message.Should().Contain("OMEN desktops");
        }

        [Fact]
        public void ModelDatabaseRefusal_IsNotBlamedOnDetection()
        {
            var caps = WorkingBoard();
            caps.ModelConfig!.SupportsFanCurves = false;

            var curves = caps.GetCapabilityReason(CapabilityFeature.FanCurves);

            curves.Code.Should().Be(CapabilityReasonCode.ModelDatabase);
            curves.Source.Should().Be("model database");
            curves.Message.Should().Contain("firmware does not hold a custom fan curve");
        }

        [Fact]
        public void SecureBootWithoutPawnIo_ExplainsTheMissingDriver()
        {
            var caps = WorkingBoard();
            caps.FanControl = FanControlMethod.MonitoringOnly;
            caps.CanSetFanSpeed = false;
            caps.SecureBootEnabled = true;
            caps.PawnIOAvailable = false;
            caps.CanUndervolt = false;
            caps.UndervoltMethod = UndervoltMethod.None;

            caps.GetCapabilityReason(CapabilityFeature.FanControl).Message.Should().Contain("PawnIO");
            caps.GetCapabilityReason(CapabilityFeature.FanControl).Code.Should().Be(CapabilityReasonCode.DriverMissing);

            var undervolt = caps.GetCapabilityReason(CapabilityFeature.Undervolt);
            undervolt.Code.Should().Be(CapabilityReasonCode.DriverMissing);
            undervolt.Message.Should().Contain("Secure Boot");
        }

        [Fact]
        public void UndervoltRuntimeBlockReason_WinsOverTheGenericAnswer()
        {
            var caps = WorkingBoard();
            caps.CanUndervolt = false;
            caps.UndervoltRuntimeReady = false;
            caps.UndervoltBlockReason = "MSR package limits unavailable (driver returned error 5)";

            var reason = caps.GetCapabilityReason(CapabilityFeature.Undervolt);

            reason.Code.Should().Be(CapabilityReasonCode.NotReady);
            reason.Message.Should().Contain("error 5");
            reason.Source.Should().Be("runtime API readiness");
        }

        [Fact]
        public void VictusWithoutDatabaseConfirmation_DeniesGpuPowerBoostAsADatabaseDecision()
        {
            var caps = WorkingBoard();
            caps.ModelFamily = OmenModelFamily.Victus;
            caps.ModelConfig!.Family = OmenModelFamily.Victus;
            caps.ModelConfig.SupportsGpuPowerBoost = false;

            var reason = caps.GetCapabilityReason(CapabilityFeature.GpuPowerBoost);

            reason.Available.Should().BeFalse();
            reason.Code.Should().Be(CapabilityReasonCode.ModelDatabase);
            reason.Message.Should().Contain("Victus");
        }

        [Fact]
        public void ZoneOnlyKeyboard_ExplainsWhyPerKeyIsOff()
        {
            var caps = WorkingBoard();

            caps.GetCapabilityReason(CapabilityFeature.PerKeyLighting).Code
                .Should().Be(CapabilityReasonCode.ModelDatabase);
            caps.GetCapabilityReason(CapabilityFeature.PerKeyLighting).Message
                .Should().Contain("not per-key");
        }

        [Fact]
        public void ExportText_ListsOnlyTheUnavailableFeatures()
        {
            var caps = WorkingBoard();
            caps.ModelConfig!.SupportsUndervolt = false;
            caps.CanUndervolt = false;
            caps.UndervoltMethod = UndervoltMethod.None;

            var text = CapabilityReasonResolver.DescribeUnavailable(caps);

            text.Should().Contain("Undervolt");
            text.Should().Contain("[ModelDatabase]");
            text.Should().NotContain("FanCurves");
        }

        [Fact]
        public void GetSummary_CarriesTheSameReasonsAsTheUi()
        {
            var caps = WorkingBoard();
            caps.ModelConfig!.SupportsFanCurves = false;

            var summary = caps.GetSummary();

            summary.Should().Contain("Capability Reasons:");
            summary.Should().Contain(caps.GetCapabilityReason(CapabilityFeature.FanCurves).Message);
        }

        [Fact]
        public void ReasonText_IsEmptyWhenAvailable_AndTheSentenceWhenGated()
        {
            var caps = WorkingBoard();

            CapabilityReasonResolver.ReasonText(caps, CapabilityFeature.FanCurves).Should().BeEmpty(
                "an available control shows no notice");
            CapabilityReasonResolver.ReasonText(null, CapabilityFeature.FanCurves).Should().BeEmpty(
                "before detection finishes there is nothing truthful to say");

            caps.ModelConfig!.SupportsFanCurves = false;

            CapabilityReasonResolver.ReasonText(caps, CapabilityFeature.FanCurves)
                .Should().Be(caps.GetCapabilityReason(CapabilityFeature.FanCurves).Message,
                    "the control and the export must show the same sentence");
        }

        [Fact]
        public void UnavailableReasons_AlwaysNameASource()
        {
            var caps = WorkingBoard();
            caps.ModelConfig!.SupportsFanCurves = false;
            caps.ModelConfig.SupportsUndervolt = false;
            caps.CanUndervolt = false;
            caps.CanSetFanSpeed = false;
            caps.FanControl = FanControlMethod.MonitoringOnly;

            var unavailable = CapabilityReasonResolver.Resolve(caps).Where(r => !r.Available).ToList();

            unavailable.Should().NotBeEmpty();
            unavailable.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Source));
            unavailable.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Message));
            unavailable.Should().OnlyContain(r => r.Code != CapabilityReasonCode.Available);
        }
    }
}
