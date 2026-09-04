// LidSwitchMonitor.InterpretLidBroadcast is the one piece of the LidState automation trigger's
// native WM_POWERBROADCAST plumbing that doesn't need a real window/message pump to exercise -
// everything else (RegisterClassEx/CreateWindowEx/RegisterPowerSettingNotification/the
// GetMessage loop) genuinely needs a live Win32 message queue and isn't something this
// environment can safely fake, matching the same "test what's pure, verify the rest by trace and
// full-suite green" pattern used for NvmlInterop/WlanSsidHelper elsewhere this cycle.

using System;
using FluentAssertions;
using OmenCore.Utils;
using Xunit;

namespace OmenCoreApp.Tests.Utils
{
    public class LidSwitchMonitorTests
    {
        private static readonly Guid LidSwitchGuid = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
        private static readonly Guid UnrelatedGuid = new("12345678-1234-1234-1234-123456789012");

        [Fact]
        public void InterpretLidBroadcast_DataZero_MeansClosed()
        {
            LidSwitchMonitor.InterpretLidBroadcast(LidSwitchGuid, dataLength: 4, dataValue: 0)
                .Should().BeTrue();
        }

        [Fact]
        public void InterpretLidBroadcast_DataOne_MeansOpen()
        {
            LidSwitchMonitor.InterpretLidBroadcast(LidSwitchGuid, dataLength: 4, dataValue: 1)
                .Should().BeFalse();
        }

        [Fact]
        public void InterpretLidBroadcast_UnrelatedGuid_ReturnsNull()
        {
            LidSwitchMonitor.InterpretLidBroadcast(UnrelatedGuid, dataLength: 4, dataValue: 0)
                .Should().BeNull();
        }

        [Fact]
        public void InterpretLidBroadcast_DataLengthTooShort_ReturnsNull()
        {
            LidSwitchMonitor.InterpretLidBroadcast(LidSwitchGuid, dataLength: 0, dataValue: 0)
                .Should().BeNull();
        }
    }
}
