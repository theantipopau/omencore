using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Export comprehensive diagnostics bundle for troubleshooting.
    /// Includes logs, configuration, system info, and hardware status.
    /// </summary>
    public class DiagnosticsExportService
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;

        public DiagnosticsExportService(LoggingService logging, ConfigurationService configService)
        {
            _logging = logging;
            _configService = configService;
        }

        /// <summary>
        /// Export diagnostics as a ZIP archive
        /// </summary>
        public async Task<string?> ExportDiagnosticsAsync(Dictionary<string, string>? additionalInfo = null)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var tempDir = Path.Combine(Path.GetTempPath(), $"OmenCore_Diagnostics_{timestamp}");
                Directory.CreateDirectory(tempDir);

                // 1. System Info
                var systemInfo = new StringBuilder();
                systemInfo.AppendLine("=== OmenCore Diagnostics Export ===");
                systemInfo.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                systemInfo.AppendLine($"OmenCore Version: 2.6.0");
                systemInfo.AppendLine();
                systemInfo.AppendLine("=== System Information ===");
                systemInfo.AppendLine($"OS: {Environment.OSVersion}");
                systemInfo.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                systemInfo.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
                systemInfo.AppendLine($"Machine Name: {Environment.MachineName}");
                systemInfo.AppendLine($"User Name: {Environment.UserName}");
                systemInfo.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                systemInfo.AppendLine($"CPU: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown"}");
                systemInfo.AppendLine($"System Directory: {Environment.SystemDirectory}");
                systemInfo.AppendLine($".NET Version: {Environment.Version}");
                systemInfo.AppendLine();

                if (additionalInfo != null && additionalInfo.Any())
                {
                    systemInfo.AppendLine("=== Hardware Status ===");
                    foreach (var kv in additionalInfo)
                    {
                        systemInfo.AppendLine($"{kv.Key}: {kv.Value}");
                    }
                    systemInfo.AppendLine();
                }

                await File.WriteAllTextAsync(Path.Combine(tempDir, "system_info.txt"), systemInfo.ToString());

                // 2. Copy log files
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore");
                if (Directory.Exists(logDir))
                {
                    var logFiles = Directory.GetFiles(logDir, "*.log")
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .Take(5); // Last 5 log files

                    foreach (var logFile in logFiles)
                    {
                        var fileName = Path.GetFileName(logFile);
                        File.Copy(logFile, Path.Combine(tempDir, fileName), overwrite: true);
                    }
                }

                // 3. Export sanitized configuration (remove sensitive data)
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenCore", "config.json");
                if (File.Exists(configPath))
                {
                    var configText = await File.ReadAllTextAsync(configPath);

                    // Sanitize sensitive fields (if any)
                    // Currently no sensitive data, but good practice
                    var configSanitized = configText;

                    await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), configSanitized);
                }

                // 4. Create diagnostic report
                var report = new StringBuilder();
                report.AppendLine("=== Diagnostic Checklist ===");
                report.AppendLine();
                report.AppendLine("Please include the following information when reporting issues:");
                report.AppendLine("1. What were you trying to do?");
                report.AppendLine("2. What happened instead?");
                report.AppendLine("3. Can you reproduce the issue? If so, what are the steps?");
                report.AppendLine("4. Did the issue occur immediately after an update?");
                report.AppendLine("5. Any error messages or unusual behavior?");
                report.AppendLine();
                report.AppendLine("GitHub Issue Template: https://github.com/theantipopau/omencore/issues/new");
                report.AppendLine();

                await File.WriteAllTextAsync(Path.Combine(tempDir, "README.txt"), report.ToString());

                // 5. Create ZIP archive
                var zipPath = tempDir + ".zip";
                ZipFile.CreateFromDirectory(tempDir, zipPath);

                // Cleanup temp directory
                Directory.Delete(tempDir, recursive: true);

                _logging.Info($"📦 Diagnostics exported to: {zipPath}");
                return zipPath;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to export diagnostics: {ex.Message}", ex);
                return null;
            }
        }
    }
}
