using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Hardware;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class FanDiagnosticsViewModelTests
    {
        private class TestVerifier : IFanVerificationService
        {
            public bool IsAvailable => true;
            public int LastAppliedPercent { get; private set; } = -1;
            public int LastCalledFanIndex { get; private set; } = -1;

            public Task<FanApplyResult> ApplyAndVerifyFanSpeedAsync(int fanIndex, int targetPercent, CancellationToken ct = default)
            {
                LastCalledFanIndex = fanIndex;
                LastAppliedPercent = targetPercent;
                var result = new FanApplyResult
                {
                    FanIndex = fanIndex,
                    FanName = fanIndex == 0 ? "CPU Fan" : "GPU Fan",
                    RequestedPercent = targetPercent,
                    ActualRpmAfter = targetPercent * 50, // predictable
                    ExpectedRpm = targetPercent * 50,
                    AppliedLevel = targetPercent,
                    WmiCallSucceeded = true,
                    VerificationPassed = true
                };

                return Task.FromResult(result);
            }

            public (int rpm, int level) GetCurrentFanState(int fanIndex)
            {
                return (rpm: 800 + fanIndex * 100, level: 10 + fanIndex);
            }
            
            public (int rpm, int level, RpmSource source) GetCurrentFanStateWithSource(int fanIndex)
            {
                return (rpm: 800 + fanIndex * 100, level: 10 + fanIndex, source: RpmSource.Estimated);
            }

            public Task<(int avg, int min, int max)> GetStableFanRpmAsync(int fanIndex, int samples = 3, CancellationToken ct = default)
            {
                return Task.FromResult((avg: 820 + fanIndex * 100, min: 800 + fanIndex * 100, max: 860 + fanIndex * 100));
            }

            public Task<FanApplyResult> ApplyWithEnhancedVerificationAsync(int fanIndex, int targetPercent, bool autoRevertOnFailure = true, CancellationToken ct = default)
            {
                LastCalledFanIndex = fanIndex;
                LastAppliedPercent = targetPercent;
                var result = new FanApplyResult
                {
                    FanIndex = fanIndex,
                    FanName = fanIndex == 0 ? "CPU Fan" : "GPU Fan",
                    RequestedPercent = targetPercent,
                    ActualRpmAfter = targetPercent * 50,
                    ExpectedRpm = targetPercent * 50,
                    AppliedLevel = targetPercent,
                    WmiCallSucceeded = true,
                    VerificationPassed = true
                };

                return Task.FromResult(result);
            }

            public Task<FanCalibrationResult> PerformFanCalibrationAsync(int fanIndex, CancellationToken ct = default)
            {
                var result = new FanCalibrationResult
                {
                    FanIndex = fanIndex,
                    FanName = fanIndex == 0 ? "CPU Fan" : "GPU Fan",
                    Success = true,
                    CalibrationPoints = new List<FanCalibrationPoint>
                    {
                        new FanCalibrationPoint { RequestedPercent = 0, MeasuredRpm = 0, AppliedLevel = 0, VerificationPassed = true },
                        new FanCalibrationPoint { RequestedPercent = 50, MeasuredRpm = 2500, AppliedLevel = 50, VerificationPassed = true },
                        new FanCalibrationPoint { RequestedPercent = 100, MeasuredRpm = 5000, AppliedLevel = 100, VerificationPassed = true }
                    }
                };

                return Task.FromResult(result);
            }
        }

        [Fact]
        public async Task ApplyAndVerify_AddsHistory_AndUpdatesState()
        {
            var logging = new LoggingService(); logging.Initialize();
            var notificationService = new NotificationService(logging);
            var fakeFanService = new FanService(new DummyFanController(), new ThermalSensorProvider(new LibreHardwareMonitorImpl()), logging, notificationService, 1000);
            var verifier = new TestVerifier();

            var vm = new FanDiagnosticsViewModel(verifier, fakeFanService, logging)
            {
                TargetPercent = 42
            };

            await vm.ApplyAndVerifyAsync();

            vm.History.Should().HaveCount(1);
            vm.History[0].RequestedPercent.Should().Be(42);
            verifier.LastAppliedPercent.Should().Be(42);

            // Current state updated from verifier GetStableFanRpmAsync
            vm.CurrentRpm.Should().Be(820);

            logging.Dispose();
        }

        [Fact]
        public void RefreshState_SetsCurrentValues()
        {
            var logging = new LoggingService(); logging.Initialize();
            var notificationService = new NotificationService(logging);
            var fakeFanService = new FanService(new DummyFanController(), new ThermalSensorProvider(new LibreHardwareMonitorImpl()), logging, notificationService, 1000);
            var verifier = new TestVerifier();

            var vm = new FanDiagnosticsViewModel(verifier, fakeFanService, logging)
            {
                SelectedFanIndex = 1
            };

            // Trigger sync
            vm.RefreshStateCommand.Execute(null);

            vm.CurrentRpm.Should().Be(920); // TestVerifier returns avg 820 + 100 for fanIndex=1 (avg value)
            vm.CurrentLevel.Should().Be(11);

            logging.Dispose();
        }

        // Provides a minimal IFanController implementation for the FanService ctor in tests
        private class DummyFanController : OmenCore.Hardware.IFanController
        {
            public bool IsAvailable => true;
            public string Status => "Test";
            public string Backend => "Test";
            public string? LastAppliedPreset { get; private set; }
            public int LastSetPercent { get; private set; } = -1;
            public int SetCallCount { get; private set; } = 0;

            public bool ApplyPreset(FanPreset preset) => true;
            public bool ApplyCustomCurve(System.Collections.Generic.IEnumerable<FanCurvePoint> curve) => true;
            public bool SetFanSpeed(int percent) { LastSetPercent = percent; SetCallCount++; return true; }
            public bool SetFanSpeeds(int cpuPercent, int gpuPercent) { LastSetPercent = System.Math.Max(cpuPercent, gpuPercent); SetCallCount++; return true; }
            public bool SetMaxFanSpeed(bool enabled) => true;
            public bool SetPerformanceMode(string modeName) => true;
            public bool RestoreAutoControl() => true;
            public System.Collections.Generic.IEnumerable<FanTelemetry> ReadFanSpeeds() => new System.Collections.Generic.List<FanTelemetry>();
            public void ApplyMaxCooling() { LastAppliedPreset = "Max"; LastSetPercent = 100; SetCallCount++; }
            public void ApplyAutoMode() { LastAppliedPreset = "Auto"; }
            public void ApplyQuietMode() { LastAppliedPreset = "Quiet"; }
            public bool ResetEcToDefaults() => true;
            public bool ApplyThrottlingMitigation() => true;
            public void Dispose() { }
            public bool VerifyMaxApplied(out string details) { details = ""; return true; }
        }
    }
}
