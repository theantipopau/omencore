using System;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class TemperatureSourceDiagnosticsViewModelTests : IDisposable
    {
        private sealed class FakeComparer : ICpuTemperatureSourceComparer
        {
            public CpuTemperatureSourceComparison Result { get; set; } = new();
            public Exception? ThrowOnNextCall { get; set; }
            public int CallCount { get; private set; }

            public Task<CpuTemperatureSourceComparison> GetCpuTemperatureSourceComparisonAsync()
            {
                CallCount++;
                if (ThrowOnNextCall != null)
                {
                    var ex = ThrowOnNextCall;
                    ThrowOnNextCall = null;
                    throw ex;
                }

                return Task.FromResult(Result);
            }
        }

        private readonly LoggingService _logging;

        public TemperatureSourceDiagnosticsViewModelTests()
        {
            _logging = new LoggingService();
            _logging.Initialize();
        }

        public void Dispose() => _logging.Dispose();

        [Fact]
        public void IsAvailable_NullComparer_IsFalse()
        {
            var vm = new TemperatureSourceDiagnosticsViewModel(null, _logging);

            vm.IsAvailable.Should().BeFalse();
        }

        [Fact]
        public void IsAvailable_RealComparer_IsTrue()
        {
            var vm = new TemperatureSourceDiagnosticsViewModel(new FakeComparer(), _logging);

            vm.IsAvailable.Should().BeTrue();
        }

        [Fact]
        public async Task RunComparisonAsync_NullComparer_DoesNothing()
        {
            var vm = new TemperatureSourceDiagnosticsViewModel(null, _logging);

            await vm.RunComparisonAsync();

            vm.Result.Should().BeEmpty();
            vm.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task RunComparisonAsync_AgreeingSources_PopulatesResultAndClearsDisagreeFlag()
        {
            var comparer = new FakeComparer
            {
                Result = new CpuTemperatureSourceComparison
                {
                    WmiBiosTempC = 78.0,
                    AcpiTempC = 79.0,
                    AcpiZoneName = "TZ00",
                    LhmTempC = 80.0,
                    CurrentAuthoritySource = "WMI BIOS",
                    CurrentAuthorityReason = "Startup default"
                }
            };
            var vm = new TemperatureSourceDiagnosticsViewModel(comparer, _logging);

            await vm.RunComparisonAsync();

            vm.SourcesDisagree.Should().BeFalse();
            vm.IsRunning.Should().BeFalse();
            vm.Result.Should().Contain("WMI BIOS: 78.0").And.Contain("TZ00").And.Contain("80.0");
        }

        [Fact]
        public async Task RunComparisonAsync_DisagreeingSources_SetsSourcesDisagreeTrue()
        {
            // Reproduces the exact field-reported shape this feature exists to catch.
            var comparer = new FakeComparer
            {
                Result = new CpuTemperatureSourceComparison
                {
                    WmiBiosTempC = 81.2,
                    AcpiTempC = 36.0,
                    AcpiZoneName = "TZ_AMBIENT",
                    CurrentAuthoritySource = "WMI BIOS"
                }
            };
            var vm = new TemperatureSourceDiagnosticsViewModel(comparer, _logging);

            await vm.RunComparisonAsync();

            vm.SourcesDisagree.Should().BeTrue();
            vm.Result.Should().Contain("disagree");
        }

        [Fact]
        public async Task RunComparisonAsync_ComparerThrows_ReportsFailureInsteadOfPropagating()
        {
            var comparer = new FakeComparer { ThrowOnNextCall = new InvalidOperationException("WMI unavailable") };
            var vm = new TemperatureSourceDiagnosticsViewModel(comparer, _logging);

            await vm.RunComparisonAsync();

            vm.Result.Should().Contain("Comparison failed").And.Contain("WMI unavailable");
            vm.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task RunButtonLabel_ReflectsRunningState()
        {
            var comparer = new FakeComparer();
            var vm = new TemperatureSourceDiagnosticsViewModel(comparer, _logging);

            vm.RunButtonLabel.Should().Be("Check Temperature Sources");

            await vm.RunComparisonAsync();

            vm.RunButtonLabel.Should().Be("Check Temperature Sources", "the command completes synchronously against the fake, so it should be back to idle");
        }

        [Fact]
        public async Task RunComparisonAsync_MultipleCalls_EachInvokesComparerOnce()
        {
            var comparer = new FakeComparer();
            var vm = new TemperatureSourceDiagnosticsViewModel(comparer, _logging);

            await vm.RunComparisonAsync();
            await vm.RunComparisonAsync();

            comparer.CallCount.Should().Be(2);
        }
    }
}
