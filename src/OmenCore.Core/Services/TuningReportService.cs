using System;
using System.IO;
using System.Text;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for exporting GPU and CPU tuning session reports.
    /// Captures requested settings, applied values, verification outcomes, conflicts, and recovery state.
    /// Reports are saved to %LOCALAPPDATA%\OmenCore\Reports\ for GitHub issue reporting.
    /// </summary>
    public class TuningReportService
    {
        private readonly LoggingService _logging;
        private readonly string _reportsDirectory;

        public TuningReportService(LoggingService logging)
        {
            _logging = logging;
            _reportsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmenCore", "Reports");
            Directory.CreateDirectory(_reportsDirectory);
        }

        /// <summary>
        /// Export GPU OC tuning session report with requested, applied, and verified states.
        /// </summary>
        public string? ExportGpuOcReport(
            object? requestedSettings,
            object? appliedSettings,
            object? verifiedSettings,
            bool testApplyPending,
            bool hasConflicts,
            string? conflictDescription,
            double? gpuTempC,
            bool onBatteryPower,
            string? additionalNotes = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("OmenCore GPU Overclocking (OC) Tuning Session Report");
                sb.AppendLine($"Generated: {DateTime.Now:O}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                // State Summary
                sb.AppendLine("[SESSION STATE]");
                sb.AppendLine($"Test Apply Pending: {testApplyPending}");
                sb.AppendLine($"Has Conflicts: {hasConflicts}");
                sb.AppendLine($"Power State: {(onBatteryPower ? "Battery (BLOCKED for increases)" : "AC Power")}");
                sb.AppendLine($"GPU Temperature: {(gpuTempC.HasValue ? $"{gpuTempC:F1}°C" : "Unavailable")}");
                sb.AppendLine();

                // Requested Settings
                sb.AppendLine("[REQUESTED SETTINGS]");
                if (requestedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(requestedSettings));
                }
                else
                {
                    sb.AppendLine("(No settings requested)");
                }
                sb.AppendLine();

                // Applied Settings
                sb.AppendLine("[APPLIED SETTINGS]");
                if (appliedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(appliedSettings));
                }
                else
                {
                    sb.AppendLine("(No settings applied)");
                }
                sb.AppendLine();

                // Verified Settings
                sb.AppendLine("[VERIFIED/READBACK SETTINGS]");
                if (verifiedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(verifiedSettings));
                }
                else
                {
                    sb.AppendLine("(No readback available)");
                }
                sb.AppendLine();

                // Conflicts
                if (hasConflicts)
                {
                    sb.AppendLine("[CONFLICTS DETECTED]");
                    sb.AppendLine(conflictDescription ?? "(Conflict details unavailable)");
                    sb.AppendLine();
                }

                // Additional Notes
                if (!string.IsNullOrWhiteSpace(additionalNotes))
                {
                    sb.AppendLine("[ADDITIONAL NOTES]");
                    sb.AppendLine(additionalNotes);
                    sb.AppendLine();
                }

                sb.AppendLine("================================================================================");

                var fileName = $"tuning-gpu-oc-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var reportPath = Path.Combine(_reportsDirectory, fileName);
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
                _logging.Info($"GPU OC report exported: {reportPath}");
                return reportPath;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to export GPU OC report: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Export CPU undervolting tuning session report with requested, applied, and verified states.
        /// </summary>
        public string? ExportCpuUndervoltReport(
            object? requestedSettings,
            object? appliedSettings,
            object? verifiedSettings,
            bool testApplyPending,
            bool hasConflicts,
            string? conflictDescription,
            double? cpuTempC,
            bool onBatteryPower,
            bool startupRecoveryPending,
            string? additionalNotes = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("OmenCore CPU Undervolting Tuning Session Report");
                sb.AppendLine($"Generated: {DateTime.Now:O}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                // State Summary
                sb.AppendLine("[SESSION STATE]");
                sb.AppendLine($"Test Apply Pending: {testApplyPending}");
                sb.AppendLine($"Has Conflicts: {hasConflicts}");
                sb.AppendLine($"Power State: {(onBatteryPower ? "Battery" : "AC Power")}");
                sb.AppendLine($"CPU Temperature: {(cpuTempC.HasValue ? $"{cpuTempC:F1}°C" : "Unavailable")}");
                sb.AppendLine($"Startup Recovery Pending: {startupRecoveryPending}");
                sb.AppendLine();

                // Requested Settings
                sb.AppendLine("[REQUESTED SETTINGS]");
                if (requestedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(requestedSettings));
                }
                else
                {
                    sb.AppendLine("(No settings requested)");
                }
                sb.AppendLine();

                // Applied Settings
                sb.AppendLine("[APPLIED SETTINGS]");
                if (appliedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(appliedSettings));
                }
                else
                {
                    sb.AppendLine("(No settings applied)");
                }
                sb.AppendLine();

                // Verified Settings
                sb.AppendLine("[VERIFIED/READBACK SETTINGS]");
                if (verifiedSettings != null)
                {
                    sb.AppendLine(FormatObjectProperties(verifiedSettings));
                }
                else
                {
                    sb.AppendLine("(No readback available)");
                }
                sb.AppendLine();

                // Conflicts
                if (hasConflicts)
                {
                    sb.AppendLine("[CONFLICTS DETECTED]");
                    sb.AppendLine(conflictDescription ?? "(Conflict details unavailable)");
                    sb.AppendLine();
                }

                // Additional Notes
                if (!string.IsNullOrWhiteSpace(additionalNotes))
                {
                    sb.AppendLine("[ADDITIONAL NOTES]");
                    sb.AppendLine(additionalNotes);
                    sb.AppendLine();
                }

                sb.AppendLine("================================================================================");

                var fileName = $"tuning-cpu-uv-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var reportPath = Path.Combine(_reportsDirectory, fileName);
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
                _logging.Info($"CPU undervolting report exported: {reportPath}");
                return reportPath;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to export CPU undervolting report: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Export combined GPU and CPU tuning conflict summary report.
        /// </summary>
        public string? ExportConflictSummaryReport(
            string gpuConflicts,
            string cpuConflicts,
            string systemState)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("OmenCore Tuning Conflict Summary Report");
                sb.AppendLine($"Generated: {DateTime.Now:O}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                sb.AppendLine("[GPU CONFLICTS]");
                sb.AppendLine(gpuConflicts);
                sb.AppendLine();

                sb.AppendLine("[CPU CONFLICTS]");
                sb.AppendLine(cpuConflicts);
                sb.AppendLine();

                sb.AppendLine("[SYSTEM STATE]");
                sb.AppendLine(systemState);
                sb.AppendLine();

                sb.AppendLine("================================================================================");

                var fileName = $"tuning-conflicts-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var reportPath = Path.Combine(_reportsDirectory, fileName);
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
                _logging.Info($"Tuning conflict summary exported: {reportPath}");
                return reportPath;
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to export tuning conflict summary: {ex.Message}");
                return null;
            }
        }

        private static string FormatObjectProperties(object obj)
        {
            var sb = new StringBuilder();
            var properties = obj.GetType().GetProperties();
            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(obj);
                    sb.AppendLine($"  {prop.Name}: {FormatValue(value)}");
                }
                catch
                {
                    sb.AppendLine($"  {prop.Name}: (error reading value)");
                }
            }
            return sb.ToString();
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "(null)";

            if (value is bool b)
                return b ? "True" : "False";

            if (value is double d)
                return d.ToString("F2");

            if (value is int i)
                return i.ToString();

            return value.ToString() ?? "(empty)";
        }
    }
}
