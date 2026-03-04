using System.Collections.Generic;
using System.Threading.Tasks;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Helper for creating a model diagnostics bundle used by the UI's "Report model" flow.
    /// Extracted to make the export logic testable and reusable.
    /// </summary>
    public static class ModelReportService
    {
        public static async Task<string?> CreateModelDiagnosticBundleAsync(SystemInfoService systemInfoService, DiagnosticExportService diagnosticExportService, string omencoreVersion)
        {
            var sysInfo = systemInfoService.GetSystemInfo();
            var model = !string.IsNullOrEmpty(sysInfo.Model) ? sysInfo.Model : (sysInfo.ProductName ?? "Unknown");

            // Collect using the canonical DiagnosticExportService, no additional args required
            return await diagnosticExportService.CollectAndExportAsync();
        }
    }
}