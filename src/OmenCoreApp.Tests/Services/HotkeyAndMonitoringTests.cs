using System;
using System.Reflection;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
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
        public void IsOmenKey_ReturnsFalse_ForF11()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { (uint)0x7A, (uint)0x0057 })!;

            result.Should().BeFalse("F11 must never be intercepted as an OMEN key");
        }

        [Fact]
        public void IsOmenKey_RejectsF24_WhenStrictModeAndScanDoesNotMatch()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { (uint)0x87, (uint)0x0057 })!;

            result.Should().BeFalse("software-generated F24 without an OMEN scan code must be rejected in strict mode");
        }

        [Theory]
        [InlineData(0xB7u, 0xE046u)]
        [InlineData(0xB7u, 0x0046u)]
        [InlineData(0xB7u, 0x009Du)]
        [InlineData(0xB6u, 0xE046u)]
        [InlineData(0xB6u, 0x0046u)]
        [InlineData(0xB6u, 0x009Du)]
        public void IsOmenKey_RejectsLaunchAppBrightnessConflictScanCodes(uint vkCode, uint scanCode)
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { vkCode, scanCode })!;

            result.Should().BeFalse("LaunchApp VK events with known brightness-conflict scans must never trigger OMEN key actions");
        }

        [Theory]
        [InlineData(0xB7u)]
        [InlineData(0xB6u)]
        public void IsOmenKey_AcceptsLaunchAppWithDedicatedOmenScan(uint vkCode)
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { vkCode, 0xE045u })!;

            result.Should().BeTrue("the dedicated OMEN LaunchApp scan must remain recognized");
        }

        [Fact]
        public void ShouldSuppressWmiEventFromRecentNeverInterceptKey_ReturnsTrue_ForRecentF11Activity()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            typeof(OmenKeyService).GetField("_lastNeverInterceptKeyTicks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(svc, DateTime.UtcNow.Ticks);
            typeof(OmenKeyService).GetField("_lastNeverInterceptVkCode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(svc, 0x7A);
            typeof(OmenKeyService).GetField("_lastNeverInterceptScanCode", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(svc, 0x0057);

            var suppressMethod = typeof(OmenKeyService).GetMethod("ShouldSuppressWmiEventFromRecentNeverInterceptKey", BindingFlags.Instance | BindingFlags.NonPublic);

            suppressMethod.Should().NotBeNull();

            var result = (bool)suppressMethod!.Invoke(svc, null)!;

            result.Should().BeTrue("recent F11 activity should suppress a matching WMI OMEN-key false positive window");
        }

        [Fact]
        public void ShouldUpdateUI_ReturnsTrue_When_PowerChangeExceedsThreshold()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences();
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

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
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

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

        [Fact]
        public void HardwareWorkerClient_FormatsLogs_WithSessionAndCorrelationContext()
        {
            var client = new HardwareWorkerClient();
            var formatMethod = typeof(HardwareWorkerClient).GetMethod("FormatWorkerLog", BindingFlags.Instance | BindingFlags.NonPublic);

            formatMethod.Should().NotBeNull();

            var withoutCorrelation = (string)formatMethod!.Invoke(client, new object[] { "startup", null! })!;
            var withCorrelation = (string)formatMethod.Invoke(client, new object[] { "recover", "recover-1234abcd" })!;

            withoutCorrelation.Should().Contain("[Worker][session=");
            withoutCorrelation.Should().NotContain("[correlation=");

            withCorrelation.Should().Contain("[Worker][session=");
            withCorrelation.Should().Contain("[correlation=recover-1234abcd]");
            withCorrelation.Should().EndWith("recover");
        }

        [Theory]
        [InlineData(false, false, false, true)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public void HardwareWorkerClient_ShouldRecoverConnection_UsesWorkerOwnershipState(bool isConnected, bool ownsWorkerProcess, bool workerProcessExited, bool expected)
        {
            var shouldRecoverMethod = typeof(HardwareWorkerClient).GetMethod("ShouldRecoverConnection", BindingFlags.Static | BindingFlags.NonPublic);

            shouldRecoverMethod.Should().NotBeNull();

            var result = (bool)shouldRecoverMethod!.Invoke(null, new object[] { isConnected, ownsWorkerProcess, workerProcessExited })!;

            result.Should().Be(expected);
        }
    }
}
