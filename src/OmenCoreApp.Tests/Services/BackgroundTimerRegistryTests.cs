using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services;

/// <summary>
/// Regression coverage for BackgroundTimerRegistry.
/// Verifies that UpdateDescription mutates the description in-place without
/// changing the registration timestamp or interval, and that Unregister/Register
/// is still required to change the interval.
/// </summary>
[Collection("NonParallel")]
public class BackgroundTimerRegistryTests : IDisposable
{
    // Clean up any test-created entries after each test.
    private const string TimerName = "Test_BackgroundTimerRegistry_Timer";
    private const string UndervoltMonitorTimerName = "UndervoltStatusMonitor";
    private const string EdpMonitorTimerName = "EdpThrottlingMitigationMonitor";
    private const string TemperatureRgbMonitorTimerName = "TemperatureRgbMonitor";

    public void Dispose()
    {
        BackgroundTimerRegistry.Unregister(TimerName);
        BackgroundTimerRegistry.Unregister(UndervoltMonitorTimerName);
        BackgroundTimerRegistry.Unregister(EdpMonitorTimerName);
        BackgroundTimerRegistry.Unregister(TemperatureRgbMonitorTimerName);
    }

    [Fact]
    public void UpdateDescription_ChangesDescriptionWithoutReRegister()
    {
        BackgroundTimerRegistry.Register(TimerName, "TestService", "original", 1000, BackgroundTimerTier.Optional);
        var before = BackgroundTimerRegistry.GetAll().First(t => t.Name == TimerName);

        BackgroundTimerRegistry.UpdateDescription(TimerName, "updated");

        var after = BackgroundTimerRegistry.GetAll().First(t => t.Name == TimerName);
        after.Description.Should().Be("updated");
        after.IntervalMs.Should().Be(before.IntervalMs);
        after.RegisteredUtc.Should().Be(before.RegisteredUtc);
    }

    [Fact]
    public void UpdateDescription_NoOp_WhenTimerNotRegistered()
    {
        // Should not throw even if the timer name does not exist.
        var act = () => BackgroundTimerRegistry.UpdateDescription("does_not_exist_xyz", "anything");
        act.Should().NotThrow();
    }

    [Fact]
    public void Register_OverwritesPreviousEntry()
    {
        BackgroundTimerRegistry.Register(TimerName, "SvcA", "desc1", 500, BackgroundTimerTier.Critical);
        BackgroundTimerRegistry.Register(TimerName, "SvcA", "desc2", 2000, BackgroundTimerTier.Critical);

        var entry = BackgroundTimerRegistry.GetAll().First(t => t.Name == TimerName);
        entry.IntervalMs.Should().Be(2000);
        entry.Description.Should().Be("desc2");
    }

    [Fact]
    public void Unregister_RemovesEntry()
    {
        BackgroundTimerRegistry.Register(TimerName, "SvcA", "desc", 1000, BackgroundTimerTier.Optional);
        BackgroundTimerRegistry.Unregister(TimerName);

        BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == TimerName);
    }

    [Fact]
    public void UndervoltService_RegistersAndUnregistersStatusMonitor()
    {
        using var logging = new LoggingService();
        using var service = new UndervoltService(new FakeUndervoltProvider(), logging, pollIntervalMs: 4000);

        service.Start();

        BackgroundTimerRegistry.GetAll().Should().ContainSingle(t =>
            t.Name == UndervoltMonitorTimerName &&
            t.OwnerService == nameof(UndervoltService) &&
            t.IntervalMs == 4000 &&
            t.Tier == BackgroundTimerTier.Optional);

        service.Stop();

        BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == UndervoltMonitorTimerName);
    }

    [Fact]
    public void EdpMitigationService_RegistersAndUnregistersCriticalMonitor()
    {
        using var logging = new LoggingService();
        using var undervolt = new UndervoltService(new FakeUndervoltProvider(), logging, pollIntervalMs: 4000);
        using var msr = new FakeMsrAccess();
        using var service = new EdpThrottlingMitigationService(msr, undervolt, logging);

        service.Start();

        BackgroundTimerRegistry.GetAll().Should().ContainSingle(t =>
            t.Name == EdpMonitorTimerName &&
            t.OwnerService == nameof(EdpThrottlingMitigationService) &&
            t.IntervalMs == 5000 &&
            t.Tier == BackgroundTimerTier.Critical);

        service.Stop();

        BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == EdpMonitorTimerName);
    }

    [Fact]
    public void TemperatureRgbService_RegistersAndUnregistersOptionalMonitor()
    {
        using var logging = new LoggingService();
        var settings = new RgbLightingSettingsService(logging);
        using var service = new TemperatureRgbService(logging, settings);

        service.Start();

        BackgroundTimerRegistry.GetAll().Should().ContainSingle(t =>
            t.Name == TemperatureRgbMonitorTimerName &&
            t.OwnerService == nameof(TemperatureRgbService) &&
            t.IntervalMs == 2000 &&
            t.Tier == BackgroundTimerTier.Optional);

        service.Stop();

        BackgroundTimerRegistry.GetAll().Should().NotContain(t => t.Name == TemperatureRgbMonitorTimerName);
    }

    private sealed class FakeUndervoltProvider : ICpuUndervoltProvider
    {
        public Task ApplyOffsetAsync(UndervoltOffset offset, CancellationToken token) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken token) => Task.CompletedTask;

        public Task<UndervoltStatus> ProbeAsync(CancellationToken token) =>
            Task.FromResult(UndervoltStatus.CreateUnknown("test"));
    }

    private sealed class FakeMsrAccess : IMsrAccess
    {
        public bool IsAvailable => true;

        public void ApplyCoreVoltageOffset(int offsetMv) { }

        public void ApplyCacheVoltageOffset(int offsetMv) { }

        public int ReadCoreVoltageOffset() => 0;

        public int ReadCacheVoltageOffset() => 0;

        public int ReadTccOffset() => 0;

        public int ReadTjMax() => 100;

        public void SetTccOffset(int offset) { }

        public int GetEffectiveTempLimit() => 100;

        public bool ReadThermalThrottlingStatus() => false;

        public bool ReadPowerThrottlingStatus() => false;

        public bool IsPowerLimitLocked() => false;

        public (double Pl1Watts, double Pl2Watts, bool Pl1Enabled, bool Pl2Enabled, bool IsLocked) GetPowerLimitStatus() =>
            (0, 0, false, false, false);

        public bool SetPowerLimits(double pl1Watts, double pl2Watts) => true;

        public double ReadPackagePowerLimit() => 0;

        public void SetPackagePowerLimit(double watts) { }

        public double ReadPackagePowerTimeWindow() => 0;

        public void SetPackagePowerTimeWindow(double seconds) { }

        public void Dispose() { }
    }
}
