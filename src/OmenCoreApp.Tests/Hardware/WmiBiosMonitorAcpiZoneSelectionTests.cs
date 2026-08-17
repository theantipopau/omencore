using System.Collections.Generic;
using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// Covers WmiBiosMonitor.SelectCpuThermalZone — the pure zone-selection logic behind
    /// GetAcpiCpuTemperature. Extracted specifically so this could be tested without a live
    /// WMI namespace: the field bug it replaces ("shows 36°C when it's over 80°C") was a
    /// selection-logic defect, not a WMI query defect, so the selection logic is what needs
    /// direct coverage.
    /// </summary>
    public class WmiBiosMonitorAcpiZoneSelectionTests
    {
        private static WmiBiosMonitor.AcpiZoneReading Zone(string name, double tempC) =>
            new WmiBiosMonitor.AcpiZoneReading(name, tempC);

        [Fact]
        public void NoZones_ReturnsUnselected()
        {
            var result = WmiBiosMonitor.SelectCpuThermalZone(new List<WmiBiosMonitor.AcpiZoneReading>(), latchedInstance: null);

            result.SelectedInstance.Should().BeNull();
            result.TempC.Should().Be(0);
            result.LatchedInstance.Should().BeNull();
            result.IsConfirmed.Should().BeFalse();
        }

        [Fact]
        public void SingleZone_IsSelectedAndLatchedRegardlessOfName()
        {
            var zones = new List<WmiBiosMonitor.AcpiZoneReading> { Zone("\\_TZ.TZ01", 42.3) };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: null);

            result.SelectedInstance.Should().Be("\\_TZ.TZ01");
            result.TempC.Should().Be(42.3);
            result.LatchedInstance.Should().Be("\\_TZ.TZ01");
            result.IsConfirmed.Should().BeTrue();
        }

        [Fact]
        public void MultipleZones_PrefersNameMatchOverFirstEnumerated()
        {
            // Regression for the exact field-reported bug: a cool ambient/skin zone enumerates
            // first, a hot CPU-named zone enumerates second. The old logic latched the first
            // zone permanently and only displaced it via a name match on a LATER zone — this
            // covers that exact displacement still working, from a clean (unlatched) start.
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.SKIN0", 36.0),
                Zone("\\_TZ.CPUZ", 82.5),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: null);

            result.SelectedInstance.Should().Be("\\_TZ.CPUZ");
            result.TempC.Should().Be(82.5);
            result.IsConfirmed.Should().BeTrue();
        }

        [Theory]
        [InlineData("CPU")]
        [InlineData("cpu")]
        [InlineData("Cpu_Pkg")]
        [InlineData("CPUZ")]
        [InlineData("TZ00")]
        [InlineData("\\_TZ.TZ00")]
        public void NameHintMatching_IsCaseInsensitiveAndSubstring(string cpuZoneName)
        {
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("SomeOtherZone", 30.0),
                Zone(cpuZoneName, 75.0),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: null);

            result.SelectedInstance.Should().Be(cpuZoneName);
            result.IsConfirmed.Should().BeTrue();
        }

        [Fact]
        public void NoNameMatchAnywhere_FallsBackToHottestZone_ReproducesFieldBugFix()
        {
            // The actual bug: neither zone's name hints at CPU at all. The old code would have
            // latched whichever zone WMI enumerated first — here, the cool one — forever. The
            // fix must pick the hotter, physically-plausible-as-CPU zone instead.
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.TZ_AMBIENT", 36.0),
                Zone("\\_TZ.TZ_UNKNOWN1", 81.2),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: null);

            result.SelectedInstance.Should().Be("\\_TZ.TZ_UNKNOWN1");
            result.TempC.Should().Be(81.2);
        }

        [Fact]
        public void AmbiguousFallback_IsNotLatched()
        {
            // The ambiguous "hottest of several unnamed zones" guess must never be persisted as
            // a confirmed latch — otherwise a single unlucky poll (e.g. CPU briefly idle while a
            // charging battery zone reads warmer) would wrongly lock in the wrong zone forever,
            // the same failure mode as the original bug just with a different trigger.
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.TZ_UNKNOWN1", 40.0),
                Zone("\\_TZ.TZ_UNKNOWN2", 38.0),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: null);

            result.IsConfirmed.Should().BeFalse();
            result.LatchedInstance.Should().BeNull();
        }

        [Fact]
        public void PreviouslyLatchedZone_IsPreferredWhenStillPresent()
        {
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.TZ_UNKNOWN1", 90.0), // hotter, but not the confirmed zone
                Zone("\\_TZ.CPUZ", 70.0),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: "\\_TZ.CPUZ");

            result.SelectedInstance.Should().Be("\\_TZ.CPUZ");
            result.TempC.Should().Be(70.0);
            result.IsConfirmed.Should().BeTrue();
        }

        [Fact]
        public void PreviouslyLatchedZone_MissingThisPoll_ReEvaluatesFromScratch()
        {
            // The confirmed zone vanished (transient WMI enumeration gap). Must not silently
            // keep returning stale data for a name that no longer exists in the current poll —
            // re-run full selection against what's actually present now.
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.SKIN0", 33.0),
                Zone("\\_TZ.CPU_PKG", 77.0),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: "\\_TZ.CPUZ");

            result.SelectedInstance.Should().Be("\\_TZ.CPU_PKG");
            result.IsConfirmed.Should().BeTrue();
        }

        [Fact]
        public void PreviouslyLatchedAmbiguousZone_IsNotStickyAcrossPolls()
        {
            // If a prior poll's latchedInstance somehow carries an ambiguous pick forward (should
            // never happen given AmbiguousFallback_IsNotLatched above, but this locks in the
            // defense-in-depth behavior): a name not matching any hint and not present should
            // fall through to fresh ambiguous re-evaluation, not be trusted as confirmed.
            var zones = new List<WmiBiosMonitor.AcpiZoneReading>
            {
                Zone("\\_TZ.TZ_UNKNOWN1", 50.0),
                Zone("\\_TZ.TZ_UNKNOWN2", 65.0),
            };

            var result = WmiBiosMonitor.SelectCpuThermalZone(zones, latchedInstance: "\\_TZ.TZ_STALE");

            result.SelectedInstance.Should().Be("\\_TZ.TZ_UNKNOWN2");
            result.IsConfirmed.Should().BeFalse();
            result.LatchedInstance.Should().BeNull();
        }
    }
}
