using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The override restarts a display device, so what is worth testing without one is the part that
    /// decides whether to offer it and how it reports what happened. The restart itself needs
    /// hardware and is verified by running it, not by a unit test.
    /// </summary>
    public class AdapterPowerOverrideServiceTests
    {
        // BuildMessage is private and static, and it is the whole reporting surface. Reaching it by
        // reflection beats making it public purely so a test can see it.
        private static string BuildMessage(double? before, double? after) =>
            (string)typeof(AdapterPowerOverrideService)
                .GetMethod("BuildMessage", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { before, after })!;

        [Fact]
        public void A_Limit_That_Rose_Is_Reported_With_Both_Numbers_And_Its_Expiry()
        {
            // The measured case on board 8D87: 35 W clamped, 80 W once the driver restarts without
            // the verdict.
            var message = BuildMessage(35.0, 80.0);

            message.Should().Contain("35 W");
            message.Should().Contain("80 W");
            message.Should().Contain("not a permanent change",
                because: "the firmware re-evaluates the adapter on its own schedule and the clamp " +
                         "comes back; a user told only the new number will think it stuck");
        }

        [Fact]
        public void A_Limit_That_Did_Not_Move_Is_Reported_As_A_Failure_Not_A_Success()
        {
            // A restart that changes nothing is the expected outcome on a board with no adapter
            // clamp. Reporting it as success would teach the user to believe the next one too.
            var message = BuildMessage(80.0, 80.0);

            message.Should().Contain("unchanged");
            message.Should().NotContain("went from");
        }

        [Fact]
        public void An_Unreadable_Limit_Says_So_Rather_Than_Claiming_Nothing_Happened()
        {
            var message = BuildMessage(null, null);

            message.Should().Contain("unknown");
            message.Should().Contain("nvidia-smi");
        }

        [Fact]
        public void LimitRose_Requires_A_Real_Rise_On_Both_Readings()
        {
            new AdapterPowerOverrideService.OverrideResult(true, "", 35.0, 80.0).LimitRose.Should().BeTrue();
            new AdapterPowerOverrideService.OverrideResult(true, "", 35.0, 35.0).LimitRose.Should().BeFalse();

            // A limit that fell is not a rise. The 20 W ConnectedTypeC rung on this board is below
            // the 35 W clamp, so "it changed" and "it improved" are genuinely different questions.
            new AdapterPowerOverrideService.OverrideResult(true, "", 35.0, 20.0).LimitRose.Should().BeFalse();

            // Half a watt of jitter either way is not a result.
            new AdapterPowerOverrideService.OverrideResult(true, "", 35.0, 35.4).LimitRose.Should().BeFalse();

            new AdapterPowerOverrideService.OverrideResult(true, "", null, 80.0).LimitRose.Should().BeFalse();
            new AdapterPowerOverrideService.OverrideResult(true, "", 35.0, null).LimitRose.Should().BeFalse();
        }

        [Fact]
        public void IsAvailable_Gives_A_Reason_When_It_Refuses()
        {
            var service = new AdapterPowerOverrideService(new LoggingService());

            // The answer depends on the machine running the suite - elevated CI agents with no NVIDIA
            // GPU refuse for a different reason than an unelevated desktop with one. What must hold
            // either way is that a refusal is explained: a disabled button with no reason is the
            // failure this property exists to prevent.
            var available = service.IsAvailable(out var reason);

            if (available)
            {
                reason.Should().BeEmpty();
            }
            else
            {
                reason.Should().NotBeNullOrWhiteSpace();
                reason.Should().EndWith(".", because: "it is shown to a user as a sentence");
            }
        }
    }
}
