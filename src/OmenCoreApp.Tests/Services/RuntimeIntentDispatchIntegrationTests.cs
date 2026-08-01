using System;
using System.Collections.Generic;
using System.Linq;
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
    public class RuntimeIntentDispatchIntegrationTests
    {
        private sealed class IntegrationFanController : IFanController
        {
            public bool IsAvailable => true;
            public string Status => "ok";
            public string Backend => "Test";
            public bool IsHoldActive => false;

            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) => true;
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) => true;
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public IEnumerable<FanTelemetry> ReadFanSpeeds() => new[] { new FanTelemetry() };
            public bool ApplyMaxCooling() {  return true; }
            public void ApplyAutoMode() { }
            public void ApplyQuietMode() { }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public bool VerifyMaxApplied(out string details)
            {
                details = string.Empty;
                return true;
            }
            public void Dispose() { }
        }

        private sealed class IntegrationMonitorBridge : IHardwareMonitorBridge
        {
            public string MonitoringSource => "Integration";

            public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new MonitoringSample
                {
                    CpuTemperatureC = 50,
                    GpuTemperatureC = 45,
                    CpuLoadPercent = 20,
                    GpuLoadPercent = 15
                });
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);
        }

        [Fact]
        public async Task CrossSurfaceRace_TrayHotkeyAutomation_ConvergesToDeterministicFinalMode()
        {
            using var logging = new LoggingService();
            var controller = new IntegrationFanController();
            var thermalProvider = new ThermalSensorProvider(new IntegrationMonitorBridge());
            var notifications = new NotificationService(logging);
            using var fanService = new FanService(controller, thermalProvider, logging, notifications, 1000, new ResumeRecoveryDiagnosticsService());
            var perfService = new PerformanceModeService(controller, new PowerPlanService(logging), null, logging);
            var config = new ConfigurationService();
            using var automation = new PowerAutomationService(logging, fanService, perfService, config);
            using var trayDispatcher = new RuntimeCommandDispatcher("IntegrationTray");
            using var hotkeyCoordinator = new RuntimeHotkeyCoordinator(() => null);

            automation.AcPerformanceMode = "Performance";
            automation.AcFanPreset = "Performance";

            var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalApplied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var appliedModes = new List<string>();

            perfService.ModeApplied += (_, mode) =>
            {
                lock (appliedModes)
                {
                    appliedModes.Add(mode);
                }

                if (string.Equals(mode, "Quiet", StringComparison.OrdinalIgnoreCase))
                {
                    finalApplied.TrySetResult(mode);
                }
            };

            trayDispatcher.EnqueueLatest("tray-blocking", async () =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
                perfService.SetPerformanceMode("Balanced");
            });

            var started = await Task.WhenAny(firstStarted.Task, Task.Delay(2000));
            started.Should().Be(firstStarted.Task, "initial tray action should enter running state");

            var concurrentBurst = new[]
            {
                Task.Run(() => trayDispatcher.EnqueueLatest("tray-performance", () =>
                {
                    perfService.SetPerformanceMode("Performance");
                    return Task.CompletedTask;
                })),
                Task.Run(() => hotkeyCoordinator.EnqueueUiAction("hotkey-boost", () => perfService.SetPerformanceMode("Performance"))),
                Task.Run(() => automation.ApplyPowerProfile(isOnAc: true, transitionContext: "integration-test"))
            };

            await Task.WhenAll(concurrentBurst);

            trayDispatcher.EnqueueLatest("final-quiet", () =>
            {
                perfService.SetPerformanceMode("Quiet");
                return Task.CompletedTask;
            });

            releaseFirst.TrySetResult(true);

            var finished = await Task.WhenAny(finalApplied.Task, Task.Delay(5000));
            finished.Should().Be(finalApplied.Task, "cross-surface overlap should converge to explicit final intent");
            (await finalApplied.Task).Should().Be("Quiet");
            perfService.GetCurrentMode().Should().Be("Quiet");

            List<string> snapshot;
            lock (appliedModes)
            {
                snapshot = appliedModes.ToList();
            }

            snapshot.Should().Contain("Balanced", "blocked tray action should eventually apply when released");
            snapshot.Should().Contain("Quiet", "explicit final intent should be observed");
            snapshot.Last().Should().Be("Quiet", "final deterministic enqueue should be the converged mode");
        }
    }
}
