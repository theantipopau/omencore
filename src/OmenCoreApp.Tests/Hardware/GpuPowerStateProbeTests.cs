using FluentAssertions;
using OmenCore.Hardware;
using System;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    public class GpuPowerStateProbeTests
    {
        /// <summary>
        /// A CM_POWER_DATA blob: PD_Size, then PD_MostRecentPowerState, then fields this
        /// code does not read.
        /// </summary>
        private static byte[] PowerData(uint mostRecentPowerState)
        {
            var buffer = new byte[56];
            BitConverter.GetBytes(56u).CopyTo(buffer, 0);
            BitConverter.GetBytes(mostRecentPowerState).CopyTo(buffer, 4);
            return buffer;
        }

        [Theory]
        [InlineData(1u, GpuPowerState.D0)]
        [InlineData(2u, GpuPowerState.D1)]
        [InlineData(3u, GpuPowerState.D2)]
        [InlineData(4u, GpuPowerState.D3)]
        public void ParseMostRecentPowerState_DecodesEachDeviceState(uint raw, GpuPowerState expected)
        {
            var buffer = PowerData(raw);

            GpuPowerStateProbe.ParseMostRecentPowerState(buffer, buffer.Length)
                .Should().Be(expected);
        }

        [Fact]
        public void ParseMostRecentPowerState_ReadsOffsetFour_NotOffsetZero()
        {
            // PD_Size sits at offset 0 and is 56 on a real blob. Reading the wrong DWORD
            // would decode that as a device state, and 56 is not one -- but a probe that
            // read offset 0 would also report Unknown for every awake GPU, which fails
            // open and looks like it works. Pin the offset explicitly.
            var buffer = PowerData(4);
            BitConverter.GetBytes(1u).CopyTo(buffer, 0);

            GpuPowerStateProbe.ParseMostRecentPowerState(buffer, buffer.Length)
                .Should().Be(GpuPowerState.D3, "the state is the second DWORD, not the first");
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(5u)]
        [InlineData(uint.MaxValue)]
        public void ParseMostRecentPowerState_RejectsValuesOutsideTheEnum(uint raw)
        {
            var buffer = PowerData(raw);

            GpuPowerStateProbe.ParseMostRecentPowerState(buffer, buffer.Length)
                .Should().Be(GpuPowerState.Unknown);
        }

        [Fact]
        public void ParseMostRecentPowerState_ReturnsUnknown_ForShortBuffer()
        {
            // A truncated reply must not be decoded as D3: that would suspend GPU telemetry
            // on a GPU that is wide awake.
            GpuPowerStateProbe.ParseMostRecentPowerState(new byte[4], 4)
                .Should().Be(GpuPowerState.Unknown);
        }

        [Fact]
        public void ParseMostRecentPowerState_HonoursLength_NotBufferSize()
        {
            // The buffer is oversized by design; only the returned length is meaningful.
            GpuPowerStateProbe.ParseMostRecentPowerState(PowerData(4), 4)
                .Should().Be(GpuPowerState.Unknown);
        }

        [Fact]
        public void ParseMostRecentPowerState_ReturnsUnknown_ForNullBuffer()
        {
            GpuPowerStateProbe.ParseMostRecentPowerState(null!, 56)
                .Should().Be(GpuPowerState.Unknown);
        }

        [Fact]
        public void Read_FailsOpen_WhenNoMatchingAdapterExists()
        {
            // Fail-open is the whole safety property: an unreadable probe must report
            // Unknown so callers keep polling, not D3 so they stop.
            var probe = new GpuPowerStateProbe(instanceIdPrefix: @"PCI\VEN_NOSUCHVENDOR");

            probe.Read().Should().Be(GpuPowerState.Unknown);
            probe.IsAsleep().Should().BeFalse();
        }

        [Fact]
        public void Read_DoesNotThrow_OnAnyMachine()
        {
            // Runs on CI without an NVIDIA adapter and on hardware with one.
            var probe = new GpuPowerStateProbe();

            var act = () => probe.Read();

            act.Should().NotThrow();
        }

        [Fact]
        public void GpuMonitoringSample_IsNotAsleepByDefault()
        {
            new GpuMonitoringSample().GpuAsleep.Should().BeFalse();
        }
    }
}
