using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        private sealed class AdaptiveBridgeStub : IHardwareMonitorBridge, IAdaptiveSamplingBridge
        {
            public bool StaticTraySamplingEnabled { get; private set; }
            public string MonitoringSource => "AdaptiveBridgeStub";

            public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new MonitoringSample
                {
                    CpuTemperatureC = 50,
                    GpuTemperatureC = 55,
                    CpuLoadPercent = 20,
                    GpuLoadPercent = 25
                });
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);

            public void SetStaticTraySamplingMode(bool enabled)
            {
                StaticTraySamplingEnabled = enabled;
            }
        }

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
        public void IsOmenKey_RejectsAmbiguousLaunchAppWithDedicatedOmenScan_InStrictMode(uint vkCode)
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { vkCode, 0xE045u })!;

            result.Should().BeFalse("strict mode must not treat ambiguous LaunchApp events as OMEN because Fn brightness keys can emit the same path");
        }

        [Fact]
        public void IsOmenKey_AcceptsF12WithDedicatedOmenScan()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { 0x7Bu, 0xE045u })!;

            result.Should().BeTrue("Fn+F12 is the dedicated OMEN launch chord on Transcend 14-style keyboards");
        }

        [Fact]
        public void IsOmenKey_RejectsOemOmenVkWithBrightnessFalsePositiveScan()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { 0xFFu, 0x002Bu })!;

            result.Should().BeFalse("Transcend 14 Fn+F2/F3 can emit VK=0xFF scan=0x002B and must not toggle OmenCore");
        }

        [Theory]
        [InlineData(0x71u)]
        [InlineData(0x72u)]
        public void IsOmenKey_RejectsBrightnessFunctionKeys_EvenWithDedicatedOmenScan(uint vkCode)
        {
            var logging = new LoggingService();
            logging.Initialize();

            var svc = new OmenKeyService(logging);
            var isOmenKeyMethod = typeof(OmenKeyService).GetMethod("IsOmenKey", BindingFlags.Instance | BindingFlags.NonPublic);

            isOmenKeyMethod.Should().NotBeNull();

            var result = (bool)isOmenKeyMethod!.Invoke(svc, new object[] { vkCode, 0xE045u })!;

            result.Should().BeFalse("Fn+F2/F3 brightness keys must always pass through and never open OmenCore");
        }

        [Fact]
        public void FeaturePreferences_EnablesFirmwareFnPProfileCycle_ByDefault()
        {
            var prefs = new FeaturePreferences();

            prefs.EnableFirmwareFnPProfileCycle.Should().BeTrue("Fn+P profile cycling should work out of the box when firmware exposes the narrow WMI event");
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
        public void GetEffectiveCadenceInterval_UsesActiveCadence_WhenOverlayRealtimeModeEnabledInTray()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences { LowOverheadMode = false };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(true);

            var method = typeof(HardwareMonitoringService).GetMethod("GetEffectiveCadenceInterval", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var cadence = (TimeSpan)method!.Invoke(svc, null)!;

            cadence.Should().Be(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void GetEffectiveCadenceInterval_UsesTrayCadence_WhenOverlayRealtimeModeDisabledInTray()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences { LowOverheadMode = false };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);

            var method = typeof(HardwareMonitoringService).GetMethod("GetEffectiveCadenceInterval", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var cadence = (TimeSpan)method!.Invoke(svc, null)!;

            cadence.Should().Be(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void GetEffectiveCadenceInterval_UsesTrayCadence_WhenLowOverheadModeEnabledInTray()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences { LowOverheadMode = true };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);

            var method = typeof(HardwareMonitoringService).GetMethod("GetEffectiveCadenceInterval", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var cadence = (TimeSpan)method!.Invoke(svc, null)!;

            cadence.Should().Be(TimeSpan.FromSeconds(10),
                "tray-only with no active fan/OSD blockers should settle to the lowest safe cadence even when low-overhead mode is enabled");
        }

        [Fact]
        public void UpdateCadenceTelemetry_RecordsReasonAndTransitionSnapshot()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences { LowOverheadMode = false };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);

            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateCadenceTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
            updateMethod.Should().NotBeNull();
            updateMethod!.Invoke(svc, new object[] { TimeSpan.FromSeconds(10) });

            svc.CurrentCadenceReason.Should().Contain("tray-only");
            var transitions = svc.GetCadenceTransitionsSnapshot();
            transitions.Should().NotBeEmpty();
            transitions[^1].CadenceMs.Should().Be(10000);
            transitions[^1].Reason.Should().Contain("tray-only");
        }

        [Fact]
        public void UpdateCadenceTelemetry_RecordsLowOverheadTrayReason()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new LibreHardwareMonitorBridge();
            var prefs = new MonitoringPreferences { LowOverheadMode = true };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);

            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateCadenceTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
            updateMethod.Should().NotBeNull();
            updateMethod!.Invoke(svc, new object[] { TimeSpan.FromSeconds(10) });

            svc.CurrentCadenceReason.Should().Contain("low-overhead/tray-only");
            var transitions = svc.GetCadenceTransitionsSnapshot();
            transitions[^1].CadenceMs.Should().Be(10000);
            transitions[^1].LowOverheadMode.Should().BeTrue();
            transitions[^1].TrayOnlyMode.Should().BeTrue();
        }

        [Fact]
        public void AdaptiveSamplingPolicy_EnablesStaticTrayMode_WhenLowOverheadTrayOnlyWithoutOverlay()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new AdaptiveBridgeStub();
            var prefs = new MonitoringPreferences { LowOverheadMode = false };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            bridge.StaticTraySamplingEnabled.Should().BeFalse();

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);
            svc.SetLowOverheadMode(true);

            bridge.StaticTraySamplingEnabled.Should().BeTrue();
        }

        [Fact]
        public void AdaptiveSamplingPolicy_DisablesStaticTrayMode_WhenOverlayRealtimeIsEnabled()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new AdaptiveBridgeStub();
            var prefs = new MonitoringPreferences { LowOverheadMode = true };
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());

            svc.SetUiWindowActive(false);
            svc.SetTrayOnlyMode(true);
            svc.SetOverlayRealtimeMode(false);
            bridge.StaticTraySamplingEnabled.Should().BeTrue();

            svc.SetOverlayRealtimeMode(true);

            bridge.StaticTraySamplingEnabled.Should().BeFalse();
        }

        [Fact]
        public void UpdateDashboardMetrics_CapsMetricHistoryByCount()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new AdaptiveBridgeStub();
            var prefs = new MonitoringPreferences();
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());
            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateDashboardMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
            var historyField = typeof(HardwareMonitoringService).GetField("_metricsHistory", BindingFlags.Instance | BindingFlags.NonPublic);

            updateMethod.Should().NotBeNull();
            historyField.Should().NotBeNull();

            var history = (System.Collections.Generic.List<HardwareMetrics>)historyField!.GetValue(svc)!;
            for (var i = 0; i < 7210; i++)
            {
                history.Add(new HardwareMetrics
                {
                    Timestamp = DateTime.Now,
                    PowerConsumption = 40 + (i % 5)
                });
            }

            updateMethod!.Invoke(svc, new object[]
            {
                new MonitoringSample
                {
                    CpuTemperatureC = 50,
                    GpuTemperatureC = 55,
                    CpuLoadPercent = 20,
                    GpuLoadPercent = 25
                }
            });

            history.Count.Should().Be(7200, "dashboard metrics should be count-capped even at active 1s cadence");
        }

        [Fact]
        public void UpdateDashboardMetrics_PrunesMetricHistoryByAge()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var bridge = new AdaptiveBridgeStub();
            var prefs = new MonitoringPreferences();
            var svc = new HardwareMonitoringService(bridge, logging, prefs, new ResumeRecoveryDiagnosticsService());
            var updateMethod = typeof(HardwareMonitoringService).GetMethod("UpdateDashboardMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
            var historyField = typeof(HardwareMonitoringService).GetField("_metricsHistory", BindingFlags.Instance | BindingFlags.NonPublic);

            updateMethod.Should().NotBeNull();
            historyField.Should().NotBeNull();

            var history = (System.Collections.Generic.List<HardwareMetrics>)historyField!.GetValue(svc)!;
            history.Add(new HardwareMetrics
            {
                Timestamp = DateTime.Now.AddHours(-25),
                PowerConsumption = 42
            });

            updateMethod!.Invoke(svc, new object[]
            {
                new MonitoringSample
                {
                    CpuTemperatureC = 50,
                    GpuTemperatureC = 55,
                    CpuLoadPercent = 20,
                    GpuLoadPercent = 25
                }
            });

            history.Should().OnlyContain(metric => metric.Timestamp >= DateTime.Now.AddHours(-24));
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
