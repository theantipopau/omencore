using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// Backs the "does this temperature look right?" guided diagnostic on the Diagnostics tab.
    /// Runs a one-shot, read-only comparison of every CPU temperature source and reports whether
    /// they agree. Before this existed, answering "where is this number coming from?" required a
    /// maintainer reading raw log files by hand — every field report behind the ACPI zone-
    /// selection fix took several round-trips for exactly that reason.
    /// </summary>
    public class TemperatureSourceDiagnosticsViewModel : ViewModelBase
    {
        private readonly ICpuTemperatureSourceComparer? _comparer;
        private readonly LoggingService _logging;

        public bool IsAvailable => _comparer != null;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(RunButtonLabel)); RaiseCommandStates(); }
        }

        private string _result = "";
        public string Result
        {
            get => _result;
            private set { _result = value; OnPropertyChanged(); }
        }

        private bool _sourcesDisagree;
        public bool SourcesDisagree
        {
            get => _sourcesDisagree;
            private set { _sourcesDisagree = value; OnPropertyChanged(); }
        }

        public ICommand RunComparisonCommand { get; }

        public string RunButtonLabel => IsRunning ? "Checking..." : "Check Temperature Sources";

        public TemperatureSourceDiagnosticsViewModel(ICpuTemperatureSourceComparer? comparer, LoggingService logging)
        {
            _comparer = comparer;
            _logging = logging;

            RunComparisonCommand = new AsyncRelayCommand(_ => RunComparisonAsync(), _ => IsAvailable && !IsRunning);
        }

        private void RaiseCommandStates()
        {
            (RunComparisonCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        public async Task RunComparisonAsync()
        {
            if (_comparer == null || IsRunning)
            {
                return;
            }

            IsRunning = true;
            Result = "";
            SourcesDisagree = false;

            try
            {
                var comparison = await _comparer.GetCpuTemperatureSourceComparisonAsync();
                Result = FormatResult(comparison);
                SourcesDisagree = comparison.SourcesDisagreeMaterially;

                if (comparison.SourcesDisagreeMaterially)
                {
                    _logging.Warn($"[TemperatureSourceDiagnostic] Sources disagree materially: {Result.Replace(Environment.NewLine, " | ")}");
                }
                else
                {
                    _logging.Info($"[TemperatureSourceDiagnostic] {Result.Replace(Environment.NewLine, " | ")}");
                }
            }
            catch (Exception ex)
            {
                Result = $"Comparison failed: {ex.Message}";
                _logging.Warn($"[TemperatureSourceDiagnostic] Comparison failed: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static string FormatResult(CpuTemperatureSourceComparison comparison)
        {
            var sb = new StringBuilder();

            sb.AppendLine(comparison.AvailableSourceCount == 0
                ? "No CPU temperature source responded."
                : $"{comparison.AvailableSourceCount} source(s) responded.");

            sb.AppendLine(comparison.WmiBiosTempC.HasValue
                ? $"WMI BIOS: {comparison.WmiBiosTempC.Value:F1}°C"
                : "WMI BIOS: unavailable");

            sb.AppendLine(comparison.AcpiTempC.HasValue
                ? $"ACPI Thermal Zone ({comparison.AcpiZoneName}): {comparison.AcpiTempC.Value:F1}°C"
                : "ACPI Thermal Zone: unavailable");

            sb.AppendLine(comparison.LhmTempC.HasValue
                ? $"LibreHardwareMonitor: {comparison.LhmTempC.Value:F1}°C"
                : "LibreHardwareMonitor: unavailable");

            sb.AppendLine($"Currently trusted: {comparison.CurrentAuthoritySource}" +
                (string.IsNullOrEmpty(comparison.CurrentAuthorityReason) ? "" : $" ({comparison.CurrentAuthorityReason})"));

            sb.Append(comparison.SourcesDisagreeMaterially
                ? $"⚠ Sources disagree by more than {CpuTemperatureSourceComparison.DisagreementThresholdC:F0}°C — the trusted reading may not be the right one for this board."
                : "✓ Available sources agree within a plausible margin.");

            return sb.ToString();
        }
    }
}
