using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Services.FanCalibration;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// ViewModel for the fan calibration wizard UI.
    /// </summary>
    public class FanCalibrationViewModel : INotifyPropertyChanged
    {
        private readonly FanCalibrationService _calibrationService;
        private readonly LoggingService _logging;
        private CancellationTokenSource? _cts;

        private string _status = "Not started";
        private int _progress = 0;
        private bool _isCalibrating = false;
        private string _calibrationInfo = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public FanCalibrationViewModel(FanCalibrationService calibrationService, LoggingService logging)
        {
            _calibrationService = calibrationService;
            _logging = logging;

            // Commands
            StartCalibrationCommand = new RelayCommand(async _ => await StartCalibrationAsync(), _ => CanStartCalibration);
            CancelCalibrationCommand = new RelayCommand(_ => CancelCalibration(), _ => IsCalibrating);

            // Subscribe to calibration events
            _calibrationService.CalibrationStepCompleted += OnCalibrationStepCompleted;
            _calibrationService.CalibrationCompleted += OnCalibrationCompleted;
            _calibrationService.CalibrationError += OnCalibrationError;

            // Initialize
            UpdateCalibrationInfo();
        }

        #region Properties

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public bool IsCalibrating
        {
            get => _isCalibrating;
            set
            {
                _isCalibrating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartCalibration));
            }
        }

        public bool CanStartCalibration => !_isCalibrating;

        public string CalibrationInfo
        {
            get => _calibrationInfo;
            set { _calibrationInfo = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CalibrationStepItem> CalibrationSteps { get; } = new();

        public FanCalibrationProfile? Profile => _calibrationService.ActiveProfile;

        #endregion

        #region Commands

        public ICommand StartCalibrationCommand { get; }
        public ICommand CancelCalibrationCommand { get; }

        #endregion

        #region Calibration

        private async Task StartCalibrationAsync()
        {
            IsCalibrating = true;
            Status = "Preparing calibration...";
            Progress = 0;
            CalibrationSteps.Clear();

            _cts = new CancellationTokenSource();

            try
            {
                Status = "Running calibration wizard...";
                await _calibrationService.StartCalibrationAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                Status = "Calibration cancelled";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                _logging.Error($"Calibration error: {ex.Message}", ex);
            }
            finally
            {
                IsCalibrating = false;
                _cts = null;
                UpdateCalibrationInfo();
            }
        }

        private void CancelCalibration()
        {
            _cts?.Cancel();
            _calibrationService.CancelCalibration();
            Status = "Cancelling...";
        }

        private void OnCalibrationStepCompleted(object? sender, CalibrationStep step)
        {
            // Update UI on UI thread
            App.Current?.Dispatcher.Invoke(() =>
            {
                CalibrationSteps.Add(new CalibrationStepItem
                {
                    Level = step.Level,
                    Fan0Rpm = step.Fan0Rpm,
                    Fan1Rpm = step.Fan1Rpm,
                    Status = "✓ Complete"
                });

                Progress = _calibrationService.CalibrationProgress;
                Status = $"Testing level {step.Level}... ({Progress}% complete)";
            });
        }

        private void OnCalibrationCompleted(object? sender, FanCalibrationProfile profile)
        {
            App.Current?.Dispatcher.Invoke(() =>
            {
                Status = "✓ Calibration complete!";
                Progress = 100;
                IsCalibrating = false;
                UpdateCalibrationInfo();
                OnPropertyChanged(nameof(Profile));
            });
        }

        private void OnCalibrationError(object? sender, string error)
        {
            App.Current?.Dispatcher.Invoke(() =>
            {
                Status = $"⚠ Error: {error}";
                IsCalibrating = false;
            });
        }

        private void UpdateCalibrationInfo()
        {
            var profile = _calibrationService.ActiveProfile;
            if (profile == null || !profile.IsValid)
            {
                CalibrationInfo = "No calibration data available. Run the calibration wizard to measure your fan's actual performance.";
            }
            else
            {
                var age = DateTime.Now - profile.CalibratedAt;
                CalibrationInfo = $"Calibrated for: {profile.ModelName}\n" +
                                  $"Max Level: {profile.MaxLevel}\n" +
                                  $"Min Spin Level: {profile.MinSpinLevel}\n" +
                                  $"CPU Fan Max: {profile.Fan0MaxRpm} RPM\n" +
                                  $"GPU Fan Max: {profile.Fan1MaxRpm} RPM\n" +
                                  $"Last calibrated: {profile.CalibratedAt:g} ({age.TotalDays:F0} days ago)";
            }
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Cleanup()
        {
            _calibrationService.CalibrationStepCompleted -= OnCalibrationStepCompleted;
            _calibrationService.CalibrationCompleted -= OnCalibrationCompleted;
            _calibrationService.CalibrationError -= OnCalibrationError;
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// UI item for a calibration step.
    /// </summary>
    public class CalibrationStepItem
    {
        public int Level { get; set; }
        public int Fan0Rpm { get; set; }
        public int Fan1Rpm { get; set; }
        public string Status { get; set; } = "";
    }
}
