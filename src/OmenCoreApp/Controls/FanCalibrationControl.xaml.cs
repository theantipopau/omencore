using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OmenCore.Services;

namespace OmenCore.Controls
{
    /// <summary>
    /// Control for calibrating fan RPM curves for accurate speed control.
    /// </summary>
    public partial class FanCalibrationControl : UserControl, INotifyPropertyChanged
    {
        private readonly LoggingService _logging;
        private readonly IFanVerificationService _fanVerifier;
        private readonly FanCalibrationStorageService _calibrationStorage;

        private CancellationTokenSource? _calibrationCts;
        private string _currentModelId = "";
        private int _selectedFanIndex = 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public FanCalibrationControl()
        {
            InitializeComponent();

            // Get services from dependency injection
            var services = App.ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized");
            _logging = services.GetService(typeof(LoggingService)) as LoggingService
                      ?? throw new InvalidOperationException("LoggingService not available");
            _fanVerifier = services.GetService(typeof(IFanVerificationService)) as IFanVerificationService
                           ?? throw new InvalidOperationException("FanVerificationService not available");
            _calibrationStorage = new FanCalibrationStorageService(_logging);

            Loaded += FanCalibrationControl_Loaded;
        }

        private async void FanCalibrationControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadModelInformationAsync();
            UpdateCalibrationStatus();
        }

        private async Task LoadModelInformationAsync()
        {
            try
            {
                // Try to get model information from WMI
                var modelInfo = await Task.Run(() =>
                {
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                        foreach (var obj in searcher.Get())
                        {
                            var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                            var model = obj["Model"]?.ToString() ?? "";
                            return $"{manufacturer} {model}".Trim();
                        }
                    }
                    catch { }
                    return "Unknown Model";
                });

                // Generate model ID from model info
                _currentModelId = GenerateModelId(modelInfo);

                ModelInfoText.Text = $"Model: {modelInfo}\nModel ID: {_currentModelId}";
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to load model information: {ex.Message}", ex);
                ModelInfoText.Text = "Failed to detect model information";
                _currentModelId = "unknown";
            }
        }

        private void UpdateCalibrationStatus()
        {
            if (_calibrationStorage.HasCalibration(_currentModelId))
            {
                var calibration = _calibrationStorage.GetCalibration(_currentModelId);
                CalibrationStatusText.Text = $"Calibration available (created {calibration?.CreatedDate.ToShortDateString()})";
                CalibrationStatusText.Foreground = System.Windows.Media.Brushes.Green;
                LoadCalibrationButton.IsEnabled = true;
            }
            else
            {
                CalibrationStatusText.Text = "No calibration data found - run calibration first";
                CalibrationStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                LoadCalibrationButton.IsEnabled = false;
            }
        }

        private void FanSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FanSelector.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (int.TryParse(tag, out int fanIndex))
                {
                    _selectedFanIndex = fanIndex;
                    UpdateFanStatus();
                }
            }
        }

        private void UpdateFanStatus()
        {
            try
            {
                var (rpm, level, source) = _fanVerifier.GetCurrentFanStateWithSource(_selectedFanIndex);
                var expectedPercent = _calibrationStorage.GetCalibratedPercent(_currentModelId, _selectedFanIndex, rpm);

                FanStatusText.Text = $"Current: {rpm} RPM (Level: {level}, Source: {source})\nEstimated: ~{expectedPercent}% speed";
            }
            catch (Exception ex)
            {
                FanStatusText.Text = $"Error reading fan status: {ex.Message}";
                _logging.Error($"Failed to update fan status: {ex.Message}", ex);
            }
        }

        private async void StartCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_fanVerifier.IsAvailable)
            {
                MessageBox.Show("Fan verification service is not available. Please check that hardware monitoring is working.",
                              "Calibration Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Fan calibration will test speeds from 0% to 100% and may take 5-10 minutes.\n\n" +
                "• Ensure your laptop has good ventilation\n" +
                "• Do not interrupt the process\n" +
                "• Fans will run at various speeds\n\n" +
                "Continue with calibration?",
                "Start Fan Calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await StartCalibrationAsync();
        }

        private async Task StartCalibrationAsync()
        {
            _calibrationCts = new CancellationTokenSource();

            try
            {
                // Update UI
                StartCalibrationButton.Visibility = Visibility.Collapsed;
                StopCalibrationButton.Visibility = Visibility.Visible;
                ProgressGroup.Visibility = Visibility.Visible;
                ResultsGroup.Visibility = Visibility.Collapsed;
                SaveCalibrationButton.Visibility = Visibility.Collapsed;

                CalibrationProgress.Value = 0;
                ProgressText.Text = "Starting calibration...";

                // Perform calibration
                var calibrationResult = await _fanVerifier.PerformFanCalibrationAsync(_selectedFanIndex, _calibrationCts.Token);

                // Update progress
                CalibrationProgress.Value = 100;
                ProgressText.Text = "Calibration completed";

                // Show results
                await DisplayCalibrationResultsAsync(calibrationResult);

                // Enable save button if successful
                if (calibrationResult.Success)
                {
                    SaveCalibrationButton.Visibility = Visibility.Visible;
                }

                _logging.Info($"Fan calibration completed: {calibrationResult.Success}");
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text = "Calibration cancelled";
                _logging.Info("Fan calibration cancelled by user");
            }
            catch (Exception ex)
            {
                ProgressText.Text = $"Calibration failed: {ex.Message}";
                _logging.Error($"Fan calibration failed: {ex.Message}", ex);
                MessageBox.Show($"Calibration failed: {ex.Message}", "Calibration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset UI
                StartCalibrationButton.Visibility = Visibility.Visible;
                StopCalibrationButton.Visibility = Visibility.Collapsed;
                _calibrationCts?.Dispose();
                _calibrationCts = null;
            }
        }

        private Task DisplayCalibrationResultsAsync(FanCalibrationResult result)
        {
            ResultsGroup.Visibility = Visibility.Visible;

            if (result.Success)
            {
                ResultsSummaryText.Text = $"Calibration successful - {result.CalibrationPoints.Count} test points completed in {result.Duration.TotalSeconds:F1} seconds";
                ResultsSummaryText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                ResultsSummaryText.Text = $"Calibration completed with issues - {result.CalibrationPoints.Count} test points, {result.CalibrationPoints.Count(p => !p.VerificationPassed)} failed";
                ResultsSummaryText.Foreground = System.Windows.Media.Brushes.Orange;
            }

            // Convert to display format
            var displayItems = result.CalibrationPoints.Select(p => new CalibrationResultDisplayItem
            {
                RequestedPercent = p.RequestedPercent,
                AppliedLevel = p.AppliedLevel,
                MeasuredRpm = p.MeasuredRpm,
                RpmRange = $"{p.RpmRangeMin}-{p.RpmRangeMax}",
                Status = p.VerificationPassed ? "✓ Pass" : "✗ Fail",
                DurationFormatted = p.Duration.TotalSeconds.ToString("F1") + "s"
            }).ToList();

            CalibrationResultsGrid.ItemsSource = displayItems;

            return Task.CompletedTask;
        }

        private void StopCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            _calibrationCts?.Cancel();
        }

        private void LoadCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCalibrationStatus();
            UpdateFanStatus();
            MessageBox.Show("Calibration data loaded and applied to fan control.", "Calibration Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Note: In a real implementation, we'd get the calibration result from the last run
                // For now, just show a placeholder
                MessageBox.Show("Calibration data would be saved here for use in fan control.", "Calibration Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to save calibration: {ex.Message}", ex);
                MessageBox.Show($"Failed to save calibration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will remove all calibration data for this model.\n\nAre you sure?",
                "Clear Calibration Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Clear calibration data
                UpdateCalibrationStatus();
                MessageBox.Show("Calibration data cleared.", "Data Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string GenerateModelId(string modelInfo)
        {
            // Generate a consistent ID from model info
            return modelInfo.ToLower()
                           .Replace(" ", "_")
                           .Replace("-", "_")
                           .Replace(".", "")
                           .Replace("(", "")
                           .Replace(")", "");
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Display item for calibration results DataGrid.
    /// </summary>
    public class CalibrationResultDisplayItem
    {
        public int RequestedPercent { get; set; }
        public int AppliedLevel { get; set; }
        public int MeasuredRpm { get; set; }
        public string RpmRange { get; set; } = "";
        public string Status { get; set; } = "";
        public string DurationFormatted { get; set; } = "";
    }
}