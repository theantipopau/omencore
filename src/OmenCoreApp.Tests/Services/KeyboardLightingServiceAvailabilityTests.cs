using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Regression tests for KeyboardLightingService.IsAvailable vs. BackendType agreement.
    ///
    /// Found via a live-machine RGB-page look at a desktop PC with no HP hardware at all
    /// (AMD Ryzen desktop, PawnIO installed for its EC access module): the "Control ownership"
    /// card showed "HP Keyboard (None)" next to a green "Confirmed" ownership badge. Traced to
    /// IsAvailable including _ecAvailable unconditionally, while BackendType only ever reports
    /// an EC backend (never "None" for that reason) when the user has explicitly opted into the
    /// experimental EC keyboard-write path (IsExperimentalEcEnabled). _ecAvailable only means
    /// "we can talk to *an* embedded controller via PawnIO" - true on almost any modern PC for
    /// basic power management, regardless of whether it's an HP OMEN keyboard-controlling EC.
    ///
    /// Uses RuntimeHelpers.GetUninitializedObject to set the relevant private fields directly,
    /// bypassing the constructor's real WMI/EC hardware probing - matching the pattern used
    /// elsewhere in this test project for classes with hardware-dependent constructors.
    /// </summary>
    public class KeyboardLightingServiceAvailabilityTests
    {
        [Fact]
        public void IsAvailable_EcAvailableButExperimentalEcNotEnabled_IsFalse()
        {
            // The exact scenario from the field look: EC access technically works (generic
            // embedded controller, not necessarily an HP keyboard one), but the user hasn't
            // opted into the experimental/riskier EC keyboard-write path. IsAvailable must
            // agree with BackendType's own "None" verdict here, not contradict it.
            var service = CreateUninitialized();
            SetField(service, "_ecAvailable", true);
            // _configService left null -> IsExperimentalEcEnabled's "?? false" fallback applies.

            service.IsAvailable.Should().BeFalse(
                "EC access alone (without the user opting into experimental EC keyboard writes) " +
                "must not be reported as an available keyboard lighting backend - BackendType " +
                "would report \"None\" for this exact state, and the two must agree");
            service.BackendType.Should().Be("None",
                "with no real backend selected, BackendType should say so plainly");
        }

        [Fact]
        public void IsAvailable_EcAvailableAndExperimentalEcEnabled_IsTrue()
        {
            var service = CreateUninitialized();
            SetField(service, "_ecAvailable", true);

            var configService = new ConfigurationService();
            configService.Config.ExperimentalEcKeyboardEnabled = true;
            SetField(service, "_configService", configService);

            service.IsAvailable.Should().BeTrue(
                "once the user has explicitly opted into experimental EC keyboard writes, " +
                "EC availability should count - this is the one case where EC-backed lighting " +
                "is real");
            service.BackendType.Should().Be("EC");
        }

        [Fact]
        public void IsAvailable_NoBackendAtAll_IsFalse()
        {
            var service = CreateUninitialized();

            service.IsAvailable.Should().BeFalse("no WMI, EC, V2, or OGH backend is available");
            service.BackendType.Should().Be("None");
        }

        private static KeyboardLightingService CreateUninitialized()
        {
            return (KeyboardLightingService)RuntimeHelpers.GetUninitializedObject(typeof(KeyboardLightingService));
        }

        private static void SetField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull($"private field {fieldName} should exist");
            field!.SetValue(target, value);
        }
    }
}
