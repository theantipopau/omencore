using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    [Collection("Config Isolation")]
    public class MainViewModelTests : IDisposable
    {
        private readonly string _tempDir;

        public MainViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OmenCoreTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", _tempDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OMENCORE_CONFIG_DIR", null);
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }

        private class FakeTelemetry : ITelemetryService
        {
            public bool Called { get; private set; }
            public string? ExportTelemetry()
            {
                Called = true;
                // create dummy file
                var tmp = Path.Combine(Path.GetTempPath(), "telemetry_test.json");
                File.WriteAllText(tmp, "{}\n");
                return tmp;
            }
        }

        [Fact]
        public async Task ExportTelemetryCommand_InvokesService_AndLogs()
        {
            // nothing throws during viewmodel construction, so just build one
            using var vm = new MainViewModel();
            var fake = new FakeTelemetry();
            // replace private field via reflection
            var field = typeof(MainViewModel).GetField("_telemetryService", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(vm, fake);

            vm.ExportTelemetryCommand.Execute(null);
            fake.Called.Should().BeTrue();
        }

        [Fact]
        public void Dashboard_DoesNotForceSystemControlLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsSystemControlLoaded.Should().BeFalse();

            _ = vm.Dashboard;

            vm.IsSystemControlLoaded.Should().BeFalse(
                because: "the dashboard/sidebar summary can use lightweight MainViewModel state at startup");
        }

        [Fact]
        public void General_DoesNotForceSystemControlLazyLoad()
        {
            using var vm = new MainViewModel();

            vm.IsSystemControlLoaded.Should().BeFalse();

            _ = vm.General;

            vm.IsSystemControlLoaded.Should().BeFalse(
                because: "the General tab should not initialize tuning/GPU-power providers before the OMEN/Tuning paths need them");
        }
    }
}
