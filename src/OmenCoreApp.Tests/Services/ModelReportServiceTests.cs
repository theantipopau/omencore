using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class ModelReportServiceTests
    {
        [Fact]
        public async Task CreateModelDiagnosticBundleAsync_CreatesZipAndReturnsPath()
        {
            var logging = new LoggingService();
            var cfg = new ConfigurationService();
            var sysInfo = new SystemInfoService(logging);
            var diagnosticsExport = new DiagnosticsExportService(logging, cfg);

            var path = await ModelReportService.CreateModelDiagnosticBundleAsync(sysInfo, diagnosticsExport, "test-version");

            path.Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeTrue();

            // Basic sanity: archive should be a .zip
            Path.GetExtension(path).Should().Be(".zip");

            // cleanup
            try { File.Delete(path); } catch { }
        }
    }
}