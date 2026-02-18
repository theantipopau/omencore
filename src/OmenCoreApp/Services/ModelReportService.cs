using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Helper for creating a model diagnostics bundle used by the UI's "Report model" flow.
    /// Extracted to make the export logic testable and reusable.
    /// </summary>
    public static class ModelReportService
    {
        public static async Task<string?> CreateModelDiagnosticBundleAsync(SystemInfoService systemInfoService, DiagnosticsExportService diagnosticsExportService, string omencoreVersion)
        {
            var sysInfo = systemInfoService.GetSystemInfo();
            var model = !string.IsNullOrEmpty(sysInfo.Model) ? sysInfo.Model : (sysInfo.ProductName ?? "Unknown");
            var productName = sysInfo.ProductName ?? string.Empty;
            var sku = sysInfo.SystemSku ?? string.Empty;

            var additional = new Dictionary<string, string>
            {
                { "Model", model },
                { "ProductName", productName },
                { "SystemSku", sku },
                { "OmenCoreVersion", omencoreVersion ?? "unknown" }
            };

            return await diagnosticsExportService.ExportDiagnosticsAsync(additional);
        }
    }
}