using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        
        private bool _isDiagnosticActive;

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
        
        private string _rpmSourceDisplay = "?";
        /// <summary>
        /// Display string for RPM data source (EC, HWMon, MAB, Est).
        /// </summary>
        public string RpmSourceDisplay 
        { 
            get => _rpmSourceDisplay; 
            set { _rpmSourceDisplay = value; OnPropertyChanged(); } 
        }
        
        /// <summary>
        /// Whether a diagnostic test is currently running.
        /// </summary>
        public bool IsDiagnosticActive
        {
            get => _isDiagnosticActive;
            private set { _isDiagnosticActive = value; OnPropertyChanged(); }
        }

        public bool IsVerificationAvailable => _verifier?.IsAvailable ?? false;

        public ICommand RefreshStateCommand { get; }
        public ICommand ApplyAndVerifyCommand { get; }

        public FanDiagnosticsViewModel(IFanVerificationService verifier, FanService fanService, LoggingService logging)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _fanService = fanService ?? throw new ArgumentNullException(nameof(fanService));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));

            RefreshStateCommand = new RelayCommand(_ => _ = UpdateCurrentStateAsync());
            ApplyAndVerifyCommand = new AsyncRelayCommand(_ => ApplyAndVerifyAsync(), _ => IsVerificationAvailable && !IsDiagnosticActive);

            // Default to CPU fan
            SelectedFanIndex = 0;
            UpdateCurrentState();
        }

        private void UpdateCurrentState()
        {
            try
            {
                var (rpm, level, source) = _verifier.GetCurrentFanStateWithSource(SelectedFanIndex);
                CurrentRpm = rpm;
                CurrentLevel = level;
                RpmSourceDisplay = source switch
                {
                    RpmSource.EcDirect => "EC",
                    RpmSource.HardwareMonitor => "HWMon",
                    RpmSource.Afterburner => "MAB",
                    RpmSource.WmiBios => "WMI",
                    RpmSource.Estimated => "Est",
                    _ => "?"
                };
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
                var (avg, min, max) = await _verifier.GetStableFanRpmAsync(SelectedFanIndex, 3, ct);
                CurrentRpm = avg;
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
                IsDiagnosticActive = true;
                
                // Enter diagnostic mode to suspend curve engine during test
                _fanService.EnterDiagnosticMode();
                
                try
                {
                    var result = await _verifier.ApplyAndVerifyFanSpeedAsync(SelectedFanIndex, TargetPercent);
                    History.Insert(0, result);
                    
                    // Update state after apply - force UI refresh
                    await UpdateCurrentStateAsync();
                    OnPropertyChanged(nameof(CurrentRpm));
                    OnPropertyChanged(nameof(CurrentLevel));
                }
                finally
                {
                    // Always exit diagnostic mode when done
                    _fanService.ExitDiagnosticMode();
                    IsDiagnosticActive = false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error("Apply and verify failed", ex);
                IsDiagnosticActive = false;
            }
        }
        
        #region Guided Diagnostic Script (v2.7.0)
        
        private bool _isGuidedTestRunning;
        private string _guidedTestStatus = "";
        private string _guidedTestResult = "";
        private int _guidedTestProgress;
        
        /// <summary>
        /// Whether the guided diagnostic test sequence is running.
        /// </summary>
        public bool IsGuidedTestRunning
        {
            get => _isGuidedTestRunning;
            private set { _isGuidedTestRunning = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Current status message for guided test (e.g., "Testing 30%...")
        /// </summary>
        public string GuidedTestStatus
        {
            get => _guidedTestStatus;
            private set { _guidedTestStatus = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Final result summary after guided test completes.
        /// </summary>
        public string GuidedTestResult
        {
            get => _guidedTestResult;
            private set { _guidedTestResult = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Progress 0-100 for guided test (0=not started, 33=30% done, 66=60% done, 100=complete)
        /// </summary>
        public int GuidedTestProgress
        {
            get => _guidedTestProgress;
            private set { _guidedTestProgress = value; OnPropertyChanged(); }
        }
        
        public ICommand RunGuidedDiagnosticCommand => new AsyncRelayCommand(_ => RunGuidedDiagnosticAsync(), _ => IsVerificationAvailable && !IsDiagnosticActive && !IsGuidedTestRunning);
        
        /// <summary>
        /// Run a guided fan diagnostic sequence: 30% → 60% → 100%
        /// Tests both CPU and GPU fans at each level.
        /// </summary>
        public async Task RunGuidedDiagnosticAsync()
        {
            if (_verifier == null || !_verifier.IsAvailable) return;
            
            IsGuidedTestRunning = true;
            IsDiagnosticActive = true;
            GuidedTestProgress = 0;
            GuidedTestResult = "";
            
            var testLevels = new[] { 30, 60, 100 };
            var fanNames = new[] { "CPU", "GPU" };
            var results = new System.Collections.Generic.List<(string fan, int target, bool passed, int rpm, double deviation, int score, string rating)>();
            
            _logging.Info("=== GUIDED FAN DIAGNOSTIC STARTED ===");
            _fanService.EnterDiagnosticMode();
            
            try
            {
                for (int levelIndex = 0; levelIndex < testLevels.Length; levelIndex++)
                {
                    var targetPercent = testLevels[levelIndex];
                    
                    for (int fanIndex = 0; fanIndex < fanNames.Length; fanIndex++)
                    {
                        var fanName = fanNames[fanIndex];
                        GuidedTestStatus = $"Testing {fanName} fan at {targetPercent}%...";
                        _logging.Info($"[GuidedDiagnostic] Testing {fanName} fan at {targetPercent}%");
                        
                        try
                        {
                            var result = await _verifier.ApplyAndVerifyFanSpeedAsync(fanIndex, targetPercent);
                            
                            // Consider ≤15% deviation as passing
                            var passed = Math.Abs(result.DeviationPercent) <= 15;
                            results.Add((fanName, targetPercent, passed, result.ActualRpmAfter, result.DeviationPercent, result.VerificationScore, result.ScoreRating));
                            
                            // Add to history
                            History.Insert(0, result);
                            
                            _logging.Info($"[GuidedDiagnostic] {fanName} at {targetPercent}%: RPM={result.ActualRpmAfter}, Deviation={result.DeviationPercent:F1}%, Score={result.VerificationScore}/100 ({result.ScoreRating}) → {(passed ? "PASS" : "FAIL")}");
                            
                            // Brief delay between tests
                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"[GuidedDiagnostic] {fanName} at {targetPercent}% FAILED: {ex.Message}");
                            results.Add((fanName, targetPercent, false, 0, 0, 0, "Failed"));
                        }
                    }
                    
                    GuidedTestProgress = ((levelIndex + 1) * 100) / testLevels.Length;
                }
                
                // Generate summary with scores (v2.7.0)
                var passCount = results.Count(r => r.passed);
                var totalTests = results.Count;
                var overallPassed = passCount == totalTests;
                var avgScore = results.Any() ? (int)results.Average(r => r.score) : 0;
                var overallRating = avgScore switch
                {
                    >= 90 => "Excellent",
                    >= 70 => "Good",
                    >= 50 => "Fair",
                    >= 25 => "Poor",
                    _ => "Failed"
                };
                
                var summary = new System.Text.StringBuilder();
                summary.AppendLine($"=== DIAGNOSTIC COMPLETE: {(overallPassed ? "✅ PASS" : "❌ FAIL")} ===");
                summary.AppendLine($"Tests: {passCount}/{totalTests} passed | Overall Score: {avgScore}/100 ({overallRating})");
                summary.AppendLine();
                
                foreach (var r in results)
                {
                    var statusIcon = r.passed ? "✓" : "✗";
                    summary.AppendLine($"{statusIcon} {r.fan} @ {r.target}%: {r.rpm} RPM ({r.deviation:+0.0;-0.0}%) - Score: {r.score}");
                }
                
                GuidedTestResult = summary.ToString();
                GuidedTestStatus = overallPassed 
                    ? $"All tests passed! Score: {avgScore}/100 ({overallRating})" 
                    : $"Some tests failed - Score: {avgScore}/100 ({overallRating})";
                
                _logging.Info(summary.ToString());
            }
            finally
            {
                _fanService.ExitDiagnosticMode();
                IsGuidedTestRunning = false;
                IsDiagnosticActive = false;
                GuidedTestProgress = 100;
            }
        }
        
        #endregion
    }
}
