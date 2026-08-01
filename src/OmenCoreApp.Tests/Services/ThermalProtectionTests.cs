using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class ThermalProtectionTests
    {
        public ThermalProtectionTests()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", tmp);
        }

        private class MockThermalProvider : OmenCore.Hardware.ThermalSensorProvider
        {
            public MockThermalProvider() : base(new MockHardwareMonitorBridge())
            {
            }

            private class MockHardwareMonitorBridge : OmenCore.Hardware.IHardwareMonitorBridge
            {
                public string MonitoringSource => "Mock";
                
                public System.Threading.Tasks.Task<OmenCore.Models.MonitoringSample> ReadSampleAsync(System.Threading.CancellationToken token)
                {
                    return System.Threading.Tasks.Task.FromResult(new OmenCore.Models.MonitoringSample 
                    { 
                        CpuTemperatureC = 50, 
                        GpuTemperatureC = 50 
                    });
                }
                
                public System.Threading.Tasks.Task<bool> TryRestartAsync() => System.Threading.Tasks.Task.FromResult(false);
            }
        }

        private class TrackingFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend { get; set; } = "Test";
            public bool IsHoldActive => false;
            public List<int> SetFanSpeedCalls { get; } = new();
            public List<string> ApplyMaxCoolingCalls { get; } = new();
            public List<string> RestoreAutoControlCalls { get; } = new();
            public int SetFanSpeedCallCount => SetFanSpeedCalls.Count;

            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent)
            {
                SetFanSpeedCalls.Add(percent);
                return true;
            }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl()
            {
                RestoreAutoControlCalls.Add("RestoreAutoControl");
                return true;
            }
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry { Name = "CPU Fan", SpeedRpm = 1000, DutyCyclePercent = 40 } };
            public bool ApplyMaxCoolingShouldFail { get; set; }
            public bool ApplyMaxCooling() { ApplyMaxCoolingCalls.Add("ApplyMaxCooling"); return !ApplyMaxCoolingShouldFail; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }

        [Fact]
        public void ThermalProtection_DisabledByDefault_DoesNotInterfere()
        {
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            // Disable thermal protection
            fanService.ThermalProtectionEnabled = false;

            // Even at emergency temp, thermal protection should not activate
            fanService.IsThermalProtectionActive.Should().BeFalse("thermal protection is disabled");
            controller.SetFanSpeedCallCount.Should().Be(0, "no fan commands should be issued when thermal protection is disabled");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_EmergencyThreshold_RaisesImmediately()
        {
            // At 95°C (emergency), thermal protection should activate immediately without debounce
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            // Enable thermal protection (default is enabled)
            fanService.ThermalProtectionEnabled = true;

            // Simulate a monitor cycle that triggers CheckThermalProtection
            // (using reflection since it's private)
            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Should().NotBeNull();
            method!.Invoke(fanService, new object[] { 95.0, 50.0 });

            fanService.IsThermalProtectionActive.Should().BeTrue("thermal protection should activate at 95°C emergency");
            controller.SetFanSpeedCalls.Should().Contain(100, "emergency should immediately command 100% fan speed");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_WarningThreshold_RequiresDebounce()
        {
            // At 90°C (warning), thermal protection should require 5s debounce
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // First call at 90°C - should not activate (needs 5s debounce)
            method!.Invoke(fanService, new object[] { 90.0, 50.0 });
            fanService.IsThermalProtectionActive.Should().BeFalse("single call at 90°C should not activate without debounce");

            // Backdate the threshold timer to simulate 5+ seconds passing
            var field = typeof(FanService).GetField("_thermalAboveThresholdSince",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull();
            field!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-6));

            // Second call after simulated debounce - should activate
            method.Invoke(fanService, new object[] { 90.0, 50.0 });
            fanService.IsThermalProtectionActive.Should().BeTrue("after 5s debounce at 90°C, thermal protection should activate");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_RateLimit_PreventsHammering()
        {
            // Thermal protection should rate-limit EC writes to prevent hammering
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // First call activates thermal protection
            method!.Invoke(fanService, new object[] { 95.0, 50.0 });
            int callsAfterFirst = controller.SetFanSpeedCallCount;
            callsAfterFirst.Should().Be(1, "first emergency should command fan speed");

            // Immediate second call should be rate-limited
            method.Invoke(fanService, new object[] { 95.0, 50.0 });
            int callsAfterSecond = controller.SetFanSpeedCallCount;
            callsAfterSecond.Should().Be(1, "second call within 15s should be rate-limited");

            // Simulate time passing (15+ seconds)
            var writeTimeField = typeof(FanService).GetField("_lastThermalFanWriteTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            writeTimeField.Should().NotBeNull();
            writeTimeField!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-16));

            // Third call after rate limit window should send keepalive
            method.Invoke(fanService, new object[] { 95.0, 50.0 });
            int callsAfterThird = controller.SetFanSpeedCallCount;
            callsAfterThird.Should().Be(2, "after 15s, keepalive should re-apply fan speed");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_InvalidTemps_Ignored()
        {
            // Thermal protection should ignore obviously invalid temperature readings
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Call with invalid CPU temp (200°C is above 150°C max sane)
            method!.Invoke(fanService, new object[] { 200.0, 50.0 });

            fanService.IsThermalProtectionActive.Should().BeFalse("invalid temps should not trigger thermal protection");
            controller.SetFanSpeedCallCount.Should().Be(0, "no fan commands should be issued for invalid temps");

            logging.Dispose();
        }

        [Fact]
        public void ApplyMaxCooling_ControllerReportsFailure_DoesNotClaimMaxModeActive()
        {
            // Regression test: ApplyMaxCooling() previously reported "Max cooling mode active"
            // and set the internal fan-mode field to "Max" unconditionally, even when the
            // underlying controller write failed (e.g. a transient WMI busy error). That left
            // callers reading a confirmed "Max" state back after a genuine write failure -
            // most importantly the thermal-critical safety-override path in MainViewModel,
            // which reads GetCurrentFanMode() right after calling this to decide whether the
            // emergency response actually took effect.
            var logging = new LoggingService();
            logging.Initialize();

            // WMI backend: FanService deliberately skips its defensive non-WMI SetFanSpeed(100)
            // re-write for WMI (relies on the controller's own keepalive), so this is the
            // specific path where a failed ApplyMaxCooling() write has no fallback recovery.
            var controller = new TrackingFanController { ApplyMaxCoolingShouldFail = true, Backend = "WMI BIOS" };
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());

            fanService.ApplyMaxCooling();

            controller.ApplyMaxCoolingCalls.Should().HaveCount(1, "the controller write should still have been attempted");
            fanService.GetCurrentFanMode().Should().NotBe("Max", "the controller reported failure, so Max mode must not be reported as active");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_DoesNotReduceFans_IfAlreadyHigher()
        {
            // Thermal protection should not reduce fan speed if already running higher
            // BUG FIX #32: Prevents Max mode from being reduced to 85% during thermal event
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            // Start in Max mode at 100%
            fanService.ApplyMaxCooling();
            controller.SetFanSpeedCalls.Clear(); // Clear the Max apply

            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Backdate the threshold timer to simulate 5+ seconds at 92°C
            var thresholdField = typeof(FanService).GetField("_thermalAboveThresholdSince",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            thresholdField!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-6));

            // Call thermal protection at 92°C
            method!.Invoke(fanService, new object[] { 92.0, 50.0 });

            // Thermal protection should not reduce from 100% to 85%
            controller.SetFanSpeedCalls.Should().BeEmpty("thermal protection should not reduce from 100% to 85%");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_ReleasesWithHysteresis()
        {
            // Thermal protection should require 10°C hysteresis for release (90°C threshold - 10°C = 80°C)
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            var method = typeof(FanService).GetMethod("CheckThermalProtection", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Activate thermal protection at 95°C
            method!.Invoke(fanService, new object[] { 95.0, 50.0 });
            fanService.IsThermalProtectionActive.Should().BeTrue();

            // Drop to 88°C - should still be active (need 80°C for release)
            method.Invoke(fanService, new object[] { 88.0, 50.0 });
            fanService.IsThermalProtectionActive.Should().BeTrue("88°C is above 80°C release threshold");

            // Drop to 78°C (below 80°C threshold) - backdate the release timer to simulate 15+ seconds at safe temp
            var releaseField = typeof(FanService).GetField("_thermalBelowReleaseSince",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            releaseField!.SetValue(fanService, DateTime.UtcNow.AddSeconds(-16));

            // Final call at 78°C after debounce - should release
            method.Invoke(fanService, new object[] { 78.0, 50.0 });
            fanService.IsThermalProtectionActive.Should().BeFalse("after 15s at 78°C (below 80°C release threshold), thermal protection should release");

            logging.Dispose();
        }

        [Fact]
        public void ThermalProtection_EmergencyActivation_RecordsBothIndividualReadings()
        {
            // GitHub Discord reports (2026-06-23): fans spike to ~5000 RPM "for no reason" at idle
            // temps on Victus 16-s0xxx and OMEN 16-xd0xxx, then drop back. The emergency tier is
            // deliberately no-debounce (safety critical — see ThermalProtection_EmergencyThreshold_
            // RaisesImmediately), so a single transient/glitchy sample can visibly spike fans. This
            // does not change that behavior; it ensures the command history captures both individual
            // readings (not just maxTemp) so a future report can show whether an activation was a
            // sustained real event or a single-sample spike.
            var logging = new LoggingService();
            logging.Initialize();

            var controller = new TrackingFanController();
            var thermalProvider = new MockThermalProvider();
            var notificationService = new NotificationService(logging);

            var fanService = new FanService(controller, thermalProvider, logging, notificationService, 1000, new ResumeRecoveryDiagnosticsService());
            fanService.ThermalProtectionEnabled = true;

            var method = typeof(FanService).GetMethod("CheckThermalProtection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(fanService, new object[] { 96.0, 42.0 });

            var entry = fanService.GetCommandHistorySnapshot()
                .FirstOrDefault(e => e.Command == "ThermalProtection.Emergency");
            entry.Should().NotBeNull("emergency activation should be queryable in diagnostics, not just the app log");
            entry!.Details.Should().Contain("cpu=96").And.Contain("gpu=42",
                "both individual readings must be recorded, not just the combined maxTemp");
        }
    }
}
