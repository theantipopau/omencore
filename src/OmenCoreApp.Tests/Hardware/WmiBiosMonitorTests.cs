using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    [Collection("Config Isolation")]
    public class WmiBiosMonitorTests
    {
        private static void SetPrivateField<T>(object instance, string fieldName, T value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull(fieldName);
            field!.SetValue(instance, value);
        }

        private static double InvokeGetBatteryCharge(WmiBiosMonitor monitor)
        {
            var method = typeof(WmiBiosMonitor).GetMethod("GetBatteryCharge", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            return (double)method!.Invoke(monitor, null)!;
        }

        [Fact]
        public void GetBatteryCharge_DuringCooldown_ReturnsCachedValue()
        {
            var monitor = new WmiBiosMonitor();
            SetPrivateField(monitor, "_cachedBatteryChargePercent", 73.5d);
            SetPrivateField(monitor, "_lastBatteryQuery", DateTime.Now);

            InvokeGetBatteryCharge(monitor).Should().Be(73.5d);
        }

        [Fact]
        public void GetBatteryCharge_WhenMonitoringDisabled_ReturnsCachedValue()
        {
            var monitor = new WmiBiosMonitor();
            SetPrivateField(monitor, "_cachedBatteryChargePercent", 41.0d);
            SetPrivateField(monitor, "_batteryMonitoringDisabled", true);

            InvokeGetBatteryCharge(monitor).Should().Be(41.0d);
        }

        /// <summary>
        /// Issue #129: CPU thermal authority must track authority source and transitions
        /// when low-temp + high-load mismatch is detected.
        /// </summary>
        [Fact]
        public void CpuTemperatureAuthoritySource_InitializesToWmiBios()
        {
            var monitor = new WmiBiosMonitor();
            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS");
            monitor.CpuTemperatureAuthorityReason.Should().Contain("Startup default");
            monitor.CpuTemperatureAuthoritySwitchCount.Should().Be(0);
        }

        /// <summary>
        /// Issue #129: MonitoringSource property includes current CPU authority suffix
        /// so diagnostics can capture active authority at collection time.
        /// </summary>
        [Fact]
        public void MonitoringSource_IncludesCpuAuthorityState()
        {
            var monitor = new WmiBiosMonitor();
            var source = monitor.MonitoringSource;
            source.Should().Contain("CPU Authority:");
            source.Should().Contain("WMI BIOS");
        }

        /// <summary>
        /// Issue #129: Authority reason is recorded for field diagnostics.
        /// </summary>
        [Fact]
        public void CpuTemperatureAuthorityReason_RecordsDecisionContext()
        {
            var monitor = new WmiBiosMonitor();
            var reason = monitor.CpuTemperatureAuthorityReason;
            reason.Should().NotBeNullOrWhiteSpace();
            // Should describe why this authority is active, even if "Startup default"
            reason.Length.Should().BeGreaterThan(5);
        }

        /// <summary>
        /// Issue #129: LastSwitchUtc is set when authority transitions and available for timing diagnostics.
        /// </summary>
        [Fact]
        public void CpuTemperatureAuthorityLastSwitchUtc_TracksSwitchTiming()
        {
            var monitor = new WmiBiosMonitor();
            // At startup, no switch has occurred yet; it should be MinValue
            monitor.CpuTemperatureAuthorityLastSwitchUtc.Should().Be(DateTime.MinValue);
            
            // The property is available and can be inspected by field diagnostics
            var switchTime = monitor.CpuTemperatureAuthorityLastSwitchUtc;
            switchTime.Should().NotBe(DateTime.MaxValue);
        }

        private static void InvokeRequestCpuTemperatureAuthority(WmiBiosMonitor monitor, string source, string reason)
        {
            var method = typeof(WmiBiosMonitor).GetMethod("RequestCpuTemperatureAuthority", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            method!.Invoke(monitor, new object[] { source, reason });
        }

        private static void InvokeResetPendingCpuAuthorityIfMatches(WmiBiosMonitor monitor, params string[] sources)
        {
            var method = typeof(WmiBiosMonitor).GetMethod("ResetPendingCpuAuthorityIfMatches", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            method!.Invoke(monitor, new object[] { sources });
        }

        // Real field logs (2026-07-26) showed one session with ~192 CPU-thermal-authority
        // switches, the large majority ACPI Thermal Zone <-> LHM Fallback flip-flops, because
        // only the "returning to WMI BIOS" direction required confirmed consecutive readings -
        // every other transition switched instantly on a single reading. These tests pin the
        // generalized, symmetric debounce that replaced the WMI-only one.
        [Fact]
        public void RequestCpuTemperatureAuthority_DoesNotSwitch_BeforeThreeConsecutiveConfirmations()
        {
            var monitor = new WmiBiosMonitor();

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 1");
            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS", "one proposal must not switch immediately");

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 2");
            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS", "two proposals must still not be enough");
        }

        [Fact]
        public void RequestCpuTemperatureAuthority_Switches_AfterThreeConsecutiveConfirmations()
        {
            var monitor = new WmiBiosMonitor();

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 1");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 2");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 3");

            monitor.CpuTemperatureAuthoritySource.Should().Be("ACPI Thermal Zone",
                "three consecutive confirmations of the same candidate must commit the switch");
        }

        [Fact]
        public void RequestCpuTemperatureAuthority_NonMatchingProposal_ResetsConfirmationCount()
        {
            var monitor = new WmiBiosMonitor();

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 1");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 2");
            // A different candidate interrupts the run - this must not let "ACPI Thermal Zone"
            // resume from count 2 afterward.
            InvokeRequestCpuTemperatureAuthority(monitor, "LHM Fallback", "reading 3");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 4");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 5");

            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS",
                "the interrupted run must restart from zero, so only 2 consecutive ACPI proposals have accumulated here, not enough to switch");
        }

        [Fact]
        public void RequestCpuTemperatureAuthority_AlreadyActiveSource_UpdatesReasonImmediately()
        {
            var monitor = new WmiBiosMonitor();

            // WMI BIOS is already the active source at startup - refreshing its reason must not
            // need any debounce, since no actual switch is happening.
            InvokeRequestCpuTemperatureAuthority(monitor, "WMI BIOS", "Primary WMI CPU temperature accepted");

            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS");
            monitor.CpuTemperatureAuthorityReason.Should().Be("Primary WMI CPU temperature accepted");
        }

        [Fact]
        public void ResetPendingCpuAuthorityIfMatches_ClearsInProgressConfirmation_WhenSourceMatches()
        {
            var monitor = new WmiBiosMonitor();

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 1");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 2");

            // Simulates a fallback-applied tick explicitly clearing in-progress primary-source
            // confirmation, matching how the real call site resets it.
            InvokeResetPendingCpuAuthorityIfMatches(monitor, "WMI BIOS", "ACPI Thermal Zone");

            // Only two more proposals after the reset - if the reset didn't work, this would be
            // confirmation #4 overall and would incorrectly switch.
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 3");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 4");

            monitor.CpuTemperatureAuthoritySource.Should().Be("WMI BIOS",
                "the reset must have discarded the first two confirmations, leaving only 2 of the required 3");
        }

        [Fact]
        public void ResetPendingCpuAuthorityIfMatches_DoesNotClear_WhenSourceDoesNotMatch()
        {
            var monitor = new WmiBiosMonitor();

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 1");
            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 2");

            // Reset targets an unrelated source - the pending "ACPI Thermal Zone" confirmation
            // must survive untouched.
            InvokeResetPendingCpuAuthorityIfMatches(monitor, "LHM Fallback", "LHM Worker Override");

            InvokeRequestCpuTemperatureAuthority(monitor, "ACPI Thermal Zone", "reading 3");

            monitor.CpuTemperatureAuthoritySource.Should().Be("ACPI Thermal Zone",
                "a reset for a non-matching source must not discard the real in-progress confirmation");
        }
    }
}