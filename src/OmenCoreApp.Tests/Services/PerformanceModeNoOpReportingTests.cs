using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Pins the case where applying a performance mode changes nothing about the hardware.
    ///
    /// On board 8D87 all three effect paths decline: direct EC power-limit writes are disabled for the
    /// model (that EC block is a mirror the SMU never reads back), the decoupled WMI thermal-policy
    /// fallback is not permitted for it, and fan policy is decoupled from performance mode by default.
    /// The mode switch therefore changes only the Windows power plan - which is a legitimate outcome,
    /// but was being reported as an unqualified "applied successfully".
    ///
    /// These tests assert on the apply trace rather than on log text, so they pin the state the
    /// summary line is derived from without coupling to its wording.
    /// </summary>
    public class PerformanceModeNoOpReportingTests
    {
        private sealed class NullFanController : IFanController
        {
            public bool IsAvailable => false;
            public string Status => "null";
            public string Backend => "null";
            public bool ApplyPreset(FanPreset preset) => false;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => false;
            public bool SetFanSpeed(int percent) => false;
            public bool SetFanSpeeds(int cpu, int gpu) => false;
            public bool SetMaxFanSpeed(bool enabled) => false;
            public bool SetPerformanceMode(string modeName) => false;
            public bool RestoreAutoControl() => false;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry() };
            public bool ApplyMaxCooling() => true;
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => false;
            public bool ApplyThrottlingMitigation() => false;
            public bool VerifyMaxApplied(out string details) { details = ""; return false; }
            public void Dispose() { }
        }

        private static PerformanceModeService BuildService(ModelCapabilities? caps)
        {
            var log = new LoggingService();
            return new PerformanceModeService(new NullFanController(), new PowerPlanService(log), null, log,
                modelCapabilities: caps);
        }

        [Fact]
        public void Board8D87_AppliesNoHardwareEffect_AndTheTraceSaysSo()
        {
            var caps = ModelCapabilityDatabase.GetCapabilities("8D87");
            caps.Should().NotBeNull();

            var service = BuildService(caps);
            service.LinkFanToPerformanceMode = false;

            service.Apply(new PerformanceMode
            {
                Name = "Quiet",
                CpuPowerLimitWatts = 35,
                GpuPowerLimitWatts = 60
            });

            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;

            trace.EffectiveModeName.Should().Be("Quiet");
            trace.EcPowerLimitApplied.Should().BeFalse("SupportsEcPowerLimits is false for this board");
            trace.WmiPolicyFallbackApplied.Should().BeFalse(
                "AllowDecoupledWmiThermalPolicyFallback is false for this board, so the fallback the " +
                "skip message advertises does not actually run");
            trace.FanPolicyAction.Should().Be("Unchanged",
                "LinkFanToPerformanceMode defaults off, so fan policy is untouched");
        }

        [Fact]
        public void Board8D87_DoesNotClaimTheWmiFallbackWasEvenAttempted()
        {
            // The skip message says "using WMI thermal policy fallback when available". On this board
            // it is not available, and the distinction matters: attempted-and-failed is a hardware
            // problem worth chasing, never-attempted is a capability flag worth knowing about.
            var service = BuildService(ModelCapabilityDatabase.GetCapabilities("8D87"));
            service.LinkFanToPerformanceMode = false;

            service.Apply(new PerformanceMode { Name = "Quiet", CpuPowerLimitWatts = 35, GpuPowerLimitWatts = 60 });

            var trace = service.GetApplyTraceSnapshot().Should().ContainSingle().Subject;
            trace.WmiPolicyFallbackAttempted.Should().BeFalse();
            trace.AllowWmiThermalPolicyFallback.Should().BeFalse();
        }
    }
}
