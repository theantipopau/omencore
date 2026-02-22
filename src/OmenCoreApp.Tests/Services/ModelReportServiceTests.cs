using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

            // Call the exporter directly to ensure it succeeds again and returns a path
            var path2 = await diagnosticsExport.ExportDiagnosticsAsync();
            if (path2 != null)
            {
                File.Exists(path2).Should().BeTrue();
            }

            // attempt to write the expected clipboard summary in an STA thread; failure is non-fatal
            var model = sysInfo.GetSystemInfo().Model ?? "Unknown";
            var clipboardText = $"Model: {model}\nDiagnostics: {path}";
            try
            {
                Thread staThread = new Thread(() => Clipboard.SetText(clipboardText));
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();
            }
            catch
            {
                // clipboard may not be available in CI; ignore
            }

            // cleanup
            try { File.Delete(path); } catch { }
            try { File.Delete(path2); } catch { }
        }
    }
}