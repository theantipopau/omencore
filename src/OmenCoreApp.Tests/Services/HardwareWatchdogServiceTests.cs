using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class HardwareWatchdogServiceTests
    {
        private static HardwareWatchdogService CreateService()
        {
            var logging = new LoggingService();
            logging.Initialize();

            // The watchdog tests only exercise watchdog state. A completely uninitialized
            // FanService is sufficient because any attempted hardware call is caught by
            // HardwareWatchdogService.
            var fanService = (FanService)RuntimeHelpers.GetUninitializedObject(typeof(FanService));

            return new HardwareWatchdogService(
                logging,
                fanService,
                new ResumeRecoveryDiagnosticsService());
        }

        private static void SetField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);

            field.Should().NotBeNull($"private watchdog field '{name}' must exist");
            field!.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);

            field.Should().NotBeNull($"private watchdog field '{name}' must exist");
            return (T)field!.GetValue(target)!;
        }

        [Fact]
        public void Failsafe_DoesNotRelease_WhileTemperatureIsAboveSafeThreshold()
        {
            var sut = CreateService();

            SetField(sut, "_failsafeActive", true);
            SetField(sut, "_isWatchdogArmed", false);
            SetField(sut, "_failsafeSafeSinceUtc", DateTime.UtcNow.AddSeconds(-60));

            sut.UpdateTemperature(70, 60);

            GetField<bool>(sut, "_failsafeActive").Should().BeTrue();
            GetField<bool>(sut, "_isWatchdogArmed").Should().BeFalse();
            GetField<DateTime>(sut, "_failsafeSafeSinceUtc")
                .Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void Failsafe_DoesNotRelease_WhenTemperaturesAreInvalid()
        {
            var sut = CreateService();

            SetField(sut, "_failsafeActive", true);
            SetField(sut, "_isWatchdogArmed", false);
            SetField(sut, "_failsafeSafeSinceUtc", DateTime.UtcNow.AddSeconds(-60));

            sut.UpdateTemperature(0, 0);

            GetField<bool>(sut, "_failsafeActive").Should().BeTrue();
            GetField<bool>(sut, "_isWatchdogArmed").Should().BeFalse();
            GetField<DateTime>(sut, "_failsafeSafeSinceUtc")
                .Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void Failsafe_Releases_AfterSustainedSafeTemperatures()
        {
            var sut = CreateService();

            SetField(sut, "_failsafeActive", true);
            SetField(sut, "_isWatchdogArmed", false);
            SetField(
                sut,
                "_failsafeSafeSinceUtc",
                DateTime.UtcNow.AddSeconds(-16));

            sut.UpdateTemperature(60, 55);

            GetField<bool>(sut, "_failsafeActive").Should().BeFalse();
            GetField<bool>(sut, "_isWatchdogArmed").Should().BeTrue();
        }
    }
}
