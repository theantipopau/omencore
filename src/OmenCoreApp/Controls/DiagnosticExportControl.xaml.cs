using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OmenCore;
using OmenCore.Services;

namespace OmenCore.Controls
{
    /// <summary>
    /// Control for exporting diagnostic data to help troubleshoot issues.
    /// </summary>
    public partial class DiagnosticExportControl : UserControl
    {
        private readonly LoggingService _logging;
        private readonly HardwareMonitoringService? _hardwareService;
        private readonly PerformanceModeService? _performanceService;
        private readonly FanCalibrationStorageService? _calibrationService;
        private readonly FanService? _fanService;

        private CancellationTokenSource? _exportCts;
        private string? _lastExportPath;

        public DiagnosticExportControl()
        {
            InitializeComponent();

            // Get services from dependency injection
            var serviceProvider = App.ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized");
            _logging = serviceProvider.GetService(typeof(LoggingService)) as LoggingService
                      ?? throw new InvalidOperationException("LoggingService not available");

            // Optional services
            _hardwareService = serviceProvider.GetService(typeof(HardwareMonitoringService)) as HardwareMonitoringService;
            _performanceService = serviceProvider.GetService(typeof(PerformanceModeService)) as PerformanceModeService;
            _calibrationService = new FanCalibrationStorageService(_logging);

            // Optional fan service for verification
            _fanService = serviceProvider.GetService(typeof(FanService)) as FanService;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Save Diagnostic Export",
                Filter = GetFileFilter(),
                FileName = $"OmenCore_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = GetDefaultExtension()
            };

            if (saveDialog.ShowDialog() != true)
                return;

            await StartExportAsync(saveDialog.FileName);
        }

        private async Task StartExportAsync(string filePath)
        {
            _exportCts = new CancellationTokenSource();

            try
            {
                // Update UI
                ExportButton.Visibility = Visibility.Collapsed;
                CancelExportButton.Visibility = Visibility.Visible;
                ProgressGroup.Visibility = Visibility.Visible;
                ResultsGroup.Visibility = Visibility.Collapsed;
                OpenExportFolderButton.Visibility = Visibility.Collapsed;
                CopyPathButton.Visibility = Visibility.Collapsed;

                ExportProgress.Value = 0;
                ProgressText.Text = "Starting diagnostic export...";

                // Perform export
                var exportResult = await PerformDiagnosticExportAsync(filePath, _exportCts.Token);

                // Update progress
                ExportProgress.Value = 100;
                ProgressText.Text = "Export completed";

                // Show results
                DisplayExportResults(exportResult);

                _lastExportPath = filePath;
                _logging.Info($"Diagnostic export completed: {filePath}");
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text = "Export cancelled";
                _logging.Info("Diagnostic export cancelled by user");
            }
            catch (Exception ex)
            {
                ProgressText.Text = $"Export failed: {ex.Message}";
                _logging.Error($"Diagnostic export failed: {ex.Message}", ex);
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset UI
                ExportButton.Visibility = Visibility.Visible;
                CancelExportButton.Visibility = Visibility.Collapsed;
                _exportCts?.Dispose();
                _exportCts = null;
            }
        }

        private async Task<DiagnosticExportResult> PerformDiagnosticExportAsync(string filePath, CancellationToken ct)
        {
            var result = new DiagnosticExportResult
            {
                ExportPath = filePath,
                StartTime = DateTime.Now,
                Sections = new List<string>()
            };

            try
            {
                var diagnosticData = new DiagnosticData
                {
                    ExportTimestamp = DateTime.Now,
                    OmenCoreVersion = GetOmenCoreVersion(),
                    Sections = new Dictionary<string, object>()
                };

                // Collect system information
                if (IncludeSystemInfoCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting system information...", 10);
                    diagnosticData.Sections["SystemInfo"] = await CollectSystemInfoAsync(ct);
                    result.Sections.Add("System Information");
                }

                // Collect hardware data
                if (IncludeHardwareDataCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting hardware monitoring data...", 30);
                    diagnosticData.Sections["HardwareData"] = await CollectHardwareDataAsync(ct);
                    result.Sections.Add("Hardware Monitoring Data");
                }

                // Collect logs
                if (IncludeLogsCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting application logs...", 50);
                    diagnosticData.Sections["Logs"] = await CollectLogsAsync(ct);
                    result.Sections.Add("Application Logs");
                }

                // Collect settings
                if (IncludeSettingsCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting settings and configuration...", 70);
                    diagnosticData.Sections["Settings"] = CollectSettings();
                    result.Sections.Add("Settings & Configuration");
                }

                // Collect performance data
                if (IncludePerformanceDataCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting performance mode data...", 85);
                    diagnosticData.Sections["PerformanceData"] = CollectPerformanceData();
                    result.Sections.Add("Performance Mode Data");
                }

                // Collect fan calibration
                if (IncludeFanCalibrationCheck.IsChecked == true)
                {
                    UpdateProgress("Collecting fan calibration data...", 95);
                    diagnosticData.Sections["FanCalibration"] = CollectFanCalibrationData();
                    result.Sections.Add("Fan Calibration Data");
                }

                // Optional: Run quick fan verification to check 'Max' preset application
                if (IncludeFanVerificationCheck.IsChecked == true)
                {
                    UpdateProgress("Running fan Max verification...", 96);
                    try
                    {
                        if (_fanService != null)
                        {
                            var (success, details) = _fanService.VerifyMaxApplied();
                            diagnosticData.Sections["FanVerification"] = new FanVerificationData
                            {
                                CollectionTimestamp = DateTime.Now,
                                Success = success,
                                Details = details
                            };
                            result.Sections.Add("Fan Verification");
                        }
                        else
                        {
                            diagnosticData.Sections["FanVerification"] = new FanVerificationData
                            {
                                CollectionTimestamp = DateTime.Now,
                                Success = false,
                                Details = "Fan service not available"
                            };
                            result.Sections.Add("Fan Verification");
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnosticData.Sections["FanVerification"] = new FanVerificationData
                        {
                            CollectionTimestamp = DateTime.Now,
                            Success = false,
                            Details = $"Verification failed: {ex.Message}"
                        };
                        result.Sections.Add("Fan Verification");
                    }
                }

                // Anonymize if requested
                if (AnonymizeDataCheck.IsChecked == true)
                {
                    UpdateProgress("Anonymizing data...", 98);
                    AnonymizeData(diagnosticData);
                }

                // Save to file
                UpdateProgress("Saving diagnostic file...", 99);
                await SaveDiagnosticDataAsync(diagnosticData, filePath, ct);

                result.EndTime = DateTime.Now;
                result.Success = true;
                result.FileSize = new FileInfo(filePath).Length;

                UpdateProgress("Export completed successfully", 100);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                throw;
            }

            return result;
        }

        private void UpdateProgress(string message, int percentage)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = message;
                ExportProgress.Value = percentage;
            });
        }

        private Task<SystemInfoData> CollectSystemInfoAsync(CancellationToken ct)
        {
            var systemInfo = new SystemInfoData();

            try
            {
                // OS Information
                systemInfo.OperatingSystem = Environment.OSVersion.ToString();
                systemInfo.Platform = RuntimeInformation.OSDescription;
                systemInfo.Is64Bit = Environment.Is64BitOperatingSystem;

                // Hardware Information
                systemInfo.ProcessorCount = Environment.ProcessorCount;
                systemInfo.MachineName = AnonymizeDataCheck.IsChecked == true ? "[REDACTED]" : Environment.MachineName;
                systemInfo.UserName = AnonymizeDataCheck.IsChecked == true ? "[REDACTED]" : Environment.UserName;

                // .NET Information
                systemInfo.DotNetVersion = RuntimeInformation.FrameworkDescription;
                systemInfo.RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier;

                // Process Information
                var currentProcess = Process.GetCurrentProcess();
                systemInfo.ProcessId = currentProcess.Id;
                systemInfo.ProcessStartTime = currentProcess.StartTime;
                systemInfo.WorkingSet = currentProcess.WorkingSet64;

                // Drive Information
                var drives = DriveInfo.GetDrives();
                systemInfo.Drives = drives.Select(d => new DriveData
                {
                    Name = d.Name,
                    DriveType = d.DriveType.ToString(),
                    TotalSize = d.TotalSize,
                    AvailableFreeSpace = d.AvailableFreeSpace,
                    IsReady = d.IsReady
                }).ToList();

                // Environment Variables (filtered for privacy)
                var safeEnvVars = new[] { "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS" };
                systemInfo.EnvironmentVariables = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .Where(e => safeEnvVars.Contains(e.Key.ToString()))
                    .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString() ?? "");

            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect system info: {ex.Message}");
                systemInfo.ErrorMessage = ex.Message;
            }

            return Task.FromResult(systemInfo);
        }

        private async Task<HardwareData> CollectHardwareDataAsync(CancellationToken ct)
        {
            var hardwareData = new HardwareData
            {
                CollectionTimestamp = DateTime.Now,
                Sensors = new List<SensorData>()
            };

            try
            {
                if (_hardwareService != null)
                {
                    // Collect current sensor readings
                    var sensors = await _hardwareService.GetAllSensorDataAsync();

                    foreach (var sensor in sensors)
                    {
                        hardwareData.Sensors.Add(new SensorData
                        {
                            Name = sensor.Name,
                            Type = sensor.Type.ToString(),
                            Value = (float)sensor.Value,
                            MinValue = (float)sensor.MinValue,
                            MaxValue = (float)sensor.MaxValue,
                            Unit = sensor.Unit
                        });
                    }
                }
                else
                {
                    hardwareData.ErrorMessage = "Hardware monitoring service not available";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect hardware data: {ex.Message}");
                hardwareData.ErrorMessage = ex.Message;
            }

            return hardwareData;
        }

        private Task<LogData> CollectLogsAsync(CancellationToken ct)
        {
            var logData = new LogData
            {
                CollectionTimestamp = DateTime.Now,
                Entries = new List<LogEntry>()
            };

            try
            {
                // This would need access to the logging system's stored logs
                // For now, return a placeholder
                logData.Entries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "INFO",
                    Message = "Log collection not yet implemented - would collect last 1000 log entries"
                });
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect logs: {ex.Message}");
                logData.ErrorMessage = ex.Message;
            }

            return Task.FromResult(logData);
        }

        private SettingsData CollectSettings()
        {
            var settingsData = new SettingsData
            {
                CollectionTimestamp = DateTime.Now,
                Settings = new Dictionary<string, object>()
            };

            try
            {
                // Collect basic settings - this would need access to the actual settings storage
                settingsData.Settings["ExportTimestamp"] = DateTime.Now;
                settingsData.Settings["Anonymized"] = AnonymizeDataCheck.IsChecked ?? false;
                settingsData.Settings["IncludedSections"] = new[]
                {
                    IncludeSystemInfoCheck.IsChecked ?? false,
                    IncludeHardwareDataCheck.IsChecked ?? false,
                    IncludeLogsCheck.IsChecked ?? false,
                    IncludeSettingsCheck.IsChecked ?? false,
                    IncludePerformanceDataCheck.IsChecked ?? false,
                    IncludeFanCalibrationCheck.IsChecked ?? false
                };
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect settings: {ex.Message}");
                settingsData.ErrorMessage = ex.Message;
            }

            return settingsData;
        }

        private PerformanceData CollectPerformanceData()
        {
            var performanceData = new PerformanceData
            {
                CollectionTimestamp = DateTime.Now
            };

            try
            {
                if (_performanceService != null)
                {
                    performanceData.CurrentMode = _performanceService.CurrentMode?.ToString() ?? "Unknown";
                    performanceData.AvailableModes = _performanceService.GetAvailableModes()
                        .Select(m => m.ToString())
                        .ToArray();
                }
                else
                {
                    performanceData.ErrorMessage = "Performance mode service not available";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect performance data: {ex.Message}");
                performanceData.ErrorMessage = ex.Message;
            }

            return performanceData;
        }

        private FanCalibrationData CollectFanCalibrationData()
        {
            var calibrationData = new FanCalibrationData
            {
                CollectionTimestamp = DateTime.Now,
                CalibratedModels = new List<string>()
            };

            try
            {
                if (_calibrationService != null)
                {
                    calibrationData.CalibratedModels = _calibrationService.GetCalibratedModels().ToList();
                    calibrationData.HasCalibrations = calibrationData.CalibratedModels.Count > 0;
                }
                else
                {
                    calibrationData.ErrorMessage = "Fan calibration service not available";
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to collect fan calibration data: {ex.Message}");
                calibrationData.ErrorMessage = ex.Message;
            }

            return calibrationData;
        }

        private void AnonymizeData(DiagnosticData data)
        {
            // Remove or redact sensitive information
            if (data.Sections.ContainsKey("SystemInfo"))
            {
                var systemInfo = data.Sections["SystemInfo"] as SystemInfoData;
                if (systemInfo != null)
                {
                    systemInfo.MachineName = "[REDACTED]";
                    systemInfo.UserName = "[REDACTED]";
                }
            }
        }

        private async Task SaveDiagnosticDataAsync(DiagnosticData data, string filePath, CancellationToken ct)
        {
            if (CompressedFormatRadio.IsChecked == true)
            {
                // Save as compressed JSON
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // For now, just save as JSON - compression would require additional libraries
                await File.WriteAllTextAsync(filePath + ".json", json, ct);
            }
            else if (TextFormatRadio.IsChecked == true)
            {
                // Save as human-readable text
                var textContent = GenerateTextReport(data);
                await File.WriteAllTextAsync(filePath + ".txt", textContent, ct);
            }
            else
            {
                // Save as JSON (default)
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(filePath + ".json", json, ct);
            }
        }

        private string GenerateTextReport(DiagnosticData data)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("OmenCore Diagnostic Report");
            report.AppendLine($"Generated: {data.ExportTimestamp}");
            report.AppendLine($"Version: {data.OmenCoreVersion}");
            report.AppendLine(new string('=', 50));
            report.AppendLine();

            // Add sections...
            report.AppendLine("This is a placeholder for the text report format.");
            report.AppendLine("Full implementation would format each data section as readable text.");

            return report.ToString();
        }

        private void DisplayExportResults(DiagnosticExportResult result)
        {
            ResultsGroup.Visibility = Visibility.Visible;
            OpenExportFolderButton.Visibility = Visibility.Visible;
            CopyPathButton.Visibility = Visibility.Visible;

            if (result.Success)
            {
                ResultsText.Text = "Export completed successfully";
                ResultsText.Foreground = System.Windows.Media.Brushes.Green;
                FilePathText.Text = $"File saved to: {result.ExportPath}";
                FileSizeText.Text = $"File size: {FormatFileSize(result.FileSize)}";
                ExportSummaryText.Text = $"Contains: {string.Join(", ", result.Sections)}";
            }
            else
            {
                ResultsText.Text = $"Export failed: {result.ErrorMessage}";
                ResultsText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private string GetFileFilter()
        {
            if (CompressedFormatRadio.IsChecked == true)
                return "ZIP files (*.zip)|*.zip";
            else if (TextFormatRadio.IsChecked == true)
                return "Text files (*.txt)|*.txt";
            else
                return "JSON files (*.json)|*.json";
        }

        private string GetDefaultExtension()
        {
            if (CompressedFormatRadio.IsChecked == true)
                return ".zip";
            else if (TextFormatRadio.IsChecked == true)
                return ".txt";
            else
                return ".json";
        }

        private string GetOmenCoreVersion()
        {
            try
            {
                return System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "2.6.0";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:F1} {sizes[order]}";
        }

        private void CancelExportButton_Click(object sender, RoutedEventArgs e)
        {
            _exportCts?.Cancel();
        }

        private void OpenExportFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastExportPath))
            {
                var directory = Path.GetDirectoryName(_lastExportPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void CopyPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastExportPath))
            {
                Clipboard.SetText(_lastExportPath);
                MessageBox.Show("File path copied to clipboard", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CreateGitHubIssueButton_Click(object sender, RoutedEventArgs e)
        {
            var issueUrl = "https://github.com/theantipopau/omencore/issues/new?" +
                "title=" + Uri.EscapeDataString("[BUG] Issue with OmenCore") +
                "&body=" + Uri.EscapeDataString(GenerateGitHubIssueTemplate());

            Process.Start(new ProcessStartInfo
            {
                FileName = issueUrl,
                UseShellExecute = true
            });
        }

        private string GenerateGitHubIssueTemplate()
        {
            return $@"**Describe the issue**
A clear and concise description of what the problem is.

**Steps to reproduce**
1. Go to '...'
2. Click on '....'
3. See error

**Expected behavior**
A clear and concise description of what you expected to happen.

**Screenshots**
If applicable, add screenshots to help explain your problem.

**Diagnostic Information**
Please attach a diagnostic export file created using OmenCore's diagnostic export feature.

**System Information**
- OmenCore Version: {GetOmenCoreVersion()}
- OS: {Environment.OSVersion}
- Hardware: [Your laptop model]

**Additional context**
Add any other context about the problem here.

---
*This issue was created using OmenCore's diagnostic export feature.*";
        }
    }

    // Data classes for diagnostic export
    public class DiagnosticData
    {
        public DateTime ExportTimestamp { get; set; }
        public string OmenCoreVersion { get; set; } = "";
        public Dictionary<string, object> Sections { get; set; } = new();
    }

    public class DiagnosticExportResult
    {
        public string ExportPath { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public long FileSize { get; set; }
        public List<string> Sections { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class SystemInfoData
    {
        public string OperatingSystem { get; set; } = "";
        public string Platform { get; set; } = "";
        public bool Is64Bit { get; set; }
        public int ProcessorCount { get; set; }
        public string MachineName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
        public string RuntimeIdentifier { get; set; } = "";
        public int ProcessId { get; set; }
        public DateTime ProcessStartTime { get; set; }
        public long WorkingSet { get; set; }
        public List<DriveData> Drives { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class DriveData
    {
        public string Name { get; set; } = "";
        public string DriveType { get; set; } = "";
        public long TotalSize { get; set; }
        public long AvailableFreeSpace { get; set; }
        public bool IsReady { get; set; }
    }

    public class HardwareData
    {
        public DateTime CollectionTimestamp { get; set; }
        public List<SensorData> Sensors { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class SensorData
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public float Value { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public string Unit { get; set; } = "";
    }

    public class LogData
    {
        public DateTime CollectionTimestamp { get; set; }
        public List<LogEntry> Entries { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class SettingsData
    {
        public DateTime CollectionTimestamp { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class PerformanceData
    {
        public DateTime CollectionTimestamp { get; set; }
        public string CurrentMode { get; set; } = "";
        public string[] AvailableModes { get; set; } = Array.Empty<string>();
        public string? ErrorMessage { get; set; }
    }

    public class FanCalibrationData
    {
        public DateTime CollectionTimestamp { get; set; }
        public List<string> CalibratedModels { get; set; } = new();
        public bool HasCalibrations { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class FanVerificationData
    {
        public DateTime CollectionTimestamp { get; set; }
        public bool Success { get; set; }
        public string Details { get; set; } = "";
    }
}