using System;
using FluentAssertions;
using NvpwrControlBlackwell;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// The Prophecy Power Unlocker 1.2.9 driver-validation rules, ported with its self-tests: the no-op must replay the
    /// driver's own F7 request, never the public ceiling, and must refuse anything ambiguous or out of range.
    /// </summary>
    public class NvidiaDriverValidationTests
    {
        private static byte[] Buffer(uint source = 0xF7, uint value = 140000)
        {
            var b = new byte[F7ContributionReadback.BufferSize];
            BitConverter.GetBytes(0x106E0u).CopyTo(b, 0);
            BitConverter.GetBytes(4u).CopyTo(b, 4);
            BitConverter.GetBytes(4u).CopyTo(b, 8);
            BitConverter.GetBytes(1u).CopyTo(b, 260);
            BitConverter.GetBytes(source).CopyTo(b, 264);
            BitConverter.GetBytes(value).CopyTo(b, 268);
            return b;
        }

        [Fact]
        public void F7Decode_ReadsTheF7SourceOnly()
        {
            F7ContributionReadback.Decode(Buffer()).Should().Be(140000u);
            F7ContributionReadback.Decode(Buffer(source: 0xFE)).Should().BeNull();
            F7ContributionReadback.Decode(Buffer(value: uint.MaxValue)).Should().BeNull("a released source reads as all ones");
        }

        [Fact]
        public void F7Decode_RefusesUnknownVersionAndAmbiguousSource()
        {
            var wrongVersion = Buffer();
            wrongVersion[0] = 0;
            F7ContributionReadback.Decode(wrongVersion).Should().BeNull();

            var duplicate = Buffer();
            BitConverter.GetBytes(2u).CopyTo(duplicate, 260);
            BitConverter.GetBytes(0xF7u).CopyTo(duplicate, 272);
            BitConverter.GetBytes(115000u).CopyTo(duplicate, 276);
            F7ContributionReadback.Decode(duplicate).Should().BeNull();

            F7ContributionReadback.Decode(new byte[10]).Should().BeNull();
        }

        [Fact]
        public void NoopTarget_KeepsTheSourceRequest_NotThePublicCeiling()
        {
            // A 140 W public ceiling over a 115 W request: the no-op must send 115 W.
            DriverValidationGuard.NoopTarget(140000, 140).Should().Be(140000);
            DriverValidationGuard.NoopTarget(115000, 140).Should().Be(115000);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0u)]
        [InlineData(uint.MaxValue)]
        [InlineData(140011u)]   // above live MAX
        public void NoopTarget_RefusesMissingOrOutOfRangeSource(uint? source)
        {
            Action act = () => DriverValidationGuard.NoopTarget(source, 140);
            act.Should().Throw<InvalidOperationException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(1001.0)]
        [InlineData(100.0)]     // MAX below the source request
        public void NoopTarget_RefusesInvalidLiveMax(double? liveMax)
        {
            Action act = () => DriverValidationGuard.NoopTarget(140000, liveMax);
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void SafeError_UnwrapsWithoutLosingTheCause()
        {
            SafeError.Describe(new InvalidOperationException("boom")).Should().Be("boom");
            SafeError.Describe(new AggregateException(new Exception("a"), new Exception("b")))
                .Should().Be("Multiple errors: a; b");
            SafeError.Describe(new System.Reflection.TargetInvocationException(new Exception("inner")))
                .Should().Be("Invocation failed: inner");
        }
    }
}
