using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.ViewModels
{
    public class FanDiagnosticsViewModel : ViewModelBase
    {
        private readonly IFanVerificationService _verifier;
        private readonly FanService _fanService;
        private readonly LoggingService _logging;

        public ObservableCollection<FanApplyResult> History { get; } = new();

        private int _selectedFanIndex;
        public int SelectedFanIndex
        {
            get => _selectedFanIndex;
            set { _selectedFanIndex = value; OnPropertyChanged(); UpdateCurrentState(); }
        }

        private int _targetPercent = 50;
        public int TargetPercent
        {
            get => _targetPercent;
            set { _targetPercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private int _currentRpm;
        public int CurrentRpm { get => _currentRpm; set { _currentRpm = value; OnPropertyChanged(); } }

        private int _currentLevel;
        public int CurrentLevel { get => _currentLevel; set { _currentLevel = value; OnPropertyChanged(); } }

        public bool IsVerificationAvailable => _verifier?.IsAvailable ?? false;

        public ICommand RefreshStateCommand { get; }
        public ICommand ApplyAndVerifyCommand { get; }

        public FanDiagnosticsViewModel(IFanVerificationService verifier, FanService fanService, LoggingService logging)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _fanService = fanService ?? throw new ArgumentNullException(nameof(fanService));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));

            RefreshStateCommand = new RelayCommand(_ => _ = UpdateCurrentStateAsync());
            ApplyAndVerifyCommand = new AsyncRelayCommand(_ => ApplyAndVerifyAsync(), _ => IsVerificationAvailable);

            // Default to CPU fan
            SelectedFanIndex = 0;
            UpdateCurrentState();
        }

        private void UpdateCurrentState()
        {
            try
            {
                var state = _verifier.GetCurrentFanState(SelectedFanIndex);
                CurrentRpm = state.rpm;
                CurrentLevel = state.level;
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to read fan state", ex);
            }
        }

        public async Task UpdateCurrentStateAsync(CancellationToken ct = default)
        {
            try
            {
                var stable = await _verifier.GetStableFanRpmAsync(SelectedFanIndex, 3, ct);
                CurrentRpm = stable.avg;
                CurrentLevel = _verifier.GetCurrentFanState(SelectedFanIndex).level;
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to refresh fan state", ex);
            }
        }

        public async Task ApplyAndVerifyAsync()
        {
            try
            {
                var result = await _verifier.ApplyAndVerifyFanSpeedAsync(SelectedFanIndex, TargetPercent);
                History.Insert(0, result);
                // Update state after apply
                await UpdateCurrentStateAsync();
            }
            catch (Exception ex)
            {
                _logging.Error("Apply and verify failed", ex);
            }
        }
    }
}
