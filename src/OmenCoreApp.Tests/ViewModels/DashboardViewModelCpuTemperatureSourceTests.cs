using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Services.Diagnostics;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// Covers DashboardViewModel.CpuTemperatureSourceTooltip / IsCpuTemperatureSourceFallback —
    /// the surfacing of WmiBiosMonitor's internal CpuTemperatureAuthoritySource/Reason, which
    /// used to be computed and then discarded with no path to the UI. Users had no way to answer
    /// "where is this number coming from?" without exporting a diagnostics bundle.
    /// </summary>
    public class DashboardViewModelCpuTemperatureSourceTests : IDisposable
    {
        private sealed class MonitoringBridgeStub : IHardwareMonitorBridge
        {
            public string MonitoringSource => "DashboardTestStub";

            public Task<MonitoringSample> ReadSampleAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new MonitoringSample());
            }

            public Task<bool> TryRestartAsync() => Task.FromResult(true);
        }

        private readonly string _tmpConfigDir;
        private readonly LoggingService _logging;
        private readonly HardwareMonitoringService _monitoring;
        private readonly MethodInfo _onSampleUpdated;

        public DashboardViewModelCpuTemperatureSourceTests()
        {
            _tmpConfigDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tmpConfigDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tmpConfigDir);

            _logging = new LoggingService();
            _logging.Initialize();

            _monitoring = new HardwareMonitoringService(
                new MonitoringBridgeStub(),
                _logging,
                new MonitoringPreferences(),
                new ResumeRecoveryDiagnosticsService());

            _onSampleUpdated = typeof(DashboardViewModel).GetMethod("OnSampleUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _onSampleUpdated.Should().NotBeNull();
        }

        public void Dispose()
        {
            _monitoring.Dispose();
            _logging.Dispose();
        }

        private void Push(DashboardViewModel vm, MonitoringSample sample) =>
            _onSampleUpdated.Invoke(vm, new object?[] { null, sample });

        [Fact]
        public void CpuTemperatureSourceTooltip_NoSample_ReturnsGenericFallback()
        {
            using var vm = new DashboardViewModel(_monitoring);

            vm.CpuTemperatureSourceTooltip.Should().Be("Temperature status");
        }

        [Fact]
        public void CpuTemperatureSourceTooltip_SourceOnly_OmitsReasonLine()
        {
            using var vm = new DashboardViewModel(_monitoring);
            Push(vm, new MonitoringSample
            {
                CpuTemperatureSource = "WMI BIOS",
                CpuTemperatureSourceReason = ""
            });

            vm.CpuTemperatureSourceTooltip.Should().Be("CPU temperature source: WMI BIOS");
        }

        [Fact]
        public void CpuTemperatureSourceTooltip_SourceAndReason_IncludesBoth()
        {
            using var vm = new DashboardViewModel(_monitoring);
            Push(vm, new MonitoringSample
            {
                CpuTemperatureSource = "LHM Fallback",
                CpuTemperatureSourceReason = "WMI/ACPI authority rejected (36.0C) vs fallback (81.2C), load=45%, power=28.3W"
            });

            vm.CpuTemperatureSourceTooltip.Should().Be(
                "CPU temperature source: LHM Fallback\nWMI/ACPI authority rejected (36.0C) vs fallback (81.2C), load=45%, power=28.3W");
        }

        [Theory]
        [InlineData("WMI BIOS", false)]
        [InlineData("ACPI Thermal Zone", false)]
        [InlineData("LHM Fallback", true)]
        [InlineData("lhm fallback", true)] // case-insensitive: the source string is human-authored, not an enum
        public void IsCpuTemperatureSourceFallback_TrueOnlyForLhmFallback(string source, bool expected)
        {
            using var vm = new DashboardViewModel(_monitoring);
            Push(vm, new MonitoringSample { CpuTemperatureSource = source });

            vm.IsCpuTemperatureSourceFallback.Should().Be(expected);
        }

        [Fact]
        public void IsCpuTemperatureSourceFallback_NoSample_IsFalse()
        {
            using var vm = new DashboardViewModel(_monitoring);

            vm.IsCpuTemperatureSourceFallback.Should().BeFalse();
        }
    }
}
