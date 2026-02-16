using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class HotkeyAndMonitoringTests
    {
        [Fact]
        public void StartWmiEventWatcher_IsSkipped_When_HookActive()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);

            // Simulate an active low-level keyboard hook by setting the private field
            var hookField = typeof(OmenKeyService).GetField("_hookHandle", BindingFlags.Instance | BindingFlags.NonPublic);
            hookField.Should().NotBeNull();
            hookField!.SetValue(svc, new IntPtr(1));

            // Sanity-check public property
            svc.IsHookActive.Should().BeTrue();

            // Invoke the private StartWmiEventWatcher method and verify it does not start the WMI watcher
            var startMethod = typeof(OmenKeyService).GetMethod("StartWmiEventWatcher", BindingFlags.Instance | BindingFlags.NonPublic);
            startMethod.Should().NotBeNull();
            startMethod!.Invoke(svc, null);

            var watcherField = typeof(OmenKeyService).GetField("_wmiEventWatcher", BindingFlags.Instance | BindingFlags.NonPublic);
            watcherField.Should().NotBeNull();
            watcherField!.GetValue(svc).Should().BeNull("WMI watcher must not be started when keyboard hook is active");
        }

        [Fact]
        public void ShouldUpdateUI_ReturnsTrue_When_PowerChangeExceedsThreshold()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences();
            var svc = new HardwareMonitoringService(bridge, logging, prefs);

            // Set a previous sample (private field) so ShouldUpdateUI compares against it
            var lastSample = new MonitoringSample
            {
                CpuTemperatureC = 50,
                CpuLoadPercent = 30,
                CpuPowerWatts = 10.0,
                GpuTemperatureC = 50,
                GpuLoadPercent = 30,
                GpuPowerWatts = 20.0
            };

            var lastSampleField = typeof(HardwareMonitoringService).GetField("_lastSample", BindingFlags.Instance | BindingFlags.NonPublic);
            lastSampleField.Should().NotBeNull();
            lastSampleField!.SetValue(svc, lastSample);

            // New sample where CPU power changed by >= 1.0W (power-change threshold)
            var newSample = new MonitoringSample
            {
                CpuTemperatureC = 50,
                CpuLoadPercent = 30,
                CpuPowerWatts = 11.5, // +1.5W
                GpuTemperatureC = 50,
                GpuLoadPercent = 30,
                GpuPowerWatts = 20.0
            };

            var shouldMethod = typeof(HardwareMonitoringService).GetMethod("ShouldUpdateUI", BindingFlags.Instance | BindingFlags.NonPublic);
            shouldMethod.Should().NotBeNull();
            var result = (bool)shouldMethod!.Invoke(svc, new object[] { newSample })!;

            result.Should().BeTrue("UI should refresh when CPU/GPU power changes exceed the configured watt threshold");
        }

        [Fact]
        public void ShouldUpdateUI_ReturnsFalse_When_PowerChangeBelowThresholdAndNoOtherChanges()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences();
            var svc = new HardwareMonitoringService(bridge, logging, prefs);

            var lastSample = new MonitoringSample
            {
                CpuTemperatureC = 50,
                CpuLoadPercent = 30,
                CpuPowerWatts = 10.0,
                GpuTemperatureC = 50,
                GpuLoadPercent = 30,
                GpuPowerWatts = 20.0
            };

            var lastSampleField = typeof(HardwareMonitoringService).GetField("_lastSample", BindingFlags.Instance | BindingFlags.NonPublic);
            lastSampleField.Should().NotBeNull();
            lastSampleField!.SetValue(svc, lastSample);

            // New sample with only small power differences (< 1.0W) and no other metric changes
            var newSample = new MonitoringSample
            {
                CpuTemperatureC = 50,
                CpuLoadPercent = 30,
                CpuPowerWatts = 10.5, // +0.5W (< threshold)
                GpuTemperatureC = 50,
                GpuLoadPercent = 30,
                GpuPowerWatts = 20.4  // +0.4W (< threshold)
            };

            var shouldMethod = typeof(HardwareMonitoringService).GetMethod("ShouldUpdateUI", BindingFlags.Instance | BindingFlags.NonPublic);
            shouldMethod.Should().NotBeNull();
            var result = (bool)shouldMethod!.Invoke(svc, new object[] { newSample })!;

            result.Should().BeFalse("UI should not refresh for sub-threshold power changes when no other metrics changed");
        }
    }
}
