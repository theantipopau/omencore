using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.Utils;
using System.Drawing;

namespace OmenCore.ViewModels
{
    public class FanDiagnosticsViewModel : ViewModelBase
    {
        private readonly IFanVerificationService _verifier;
        private readonly FanService _fanService;
        private readonly LoggingService _logging;
        private readonly ConfigurationService? _configService;
        private KeyboardLightingService? _keyboardLightingService;
        
        private bool _isDiagnosticActive;
        
        // v2.7.1: Remember preset before diagnostic to restore after
        private FanPreset? _preTestPreset;

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
            private set { _isDiagnosticActive = value; OnPropertyChanged(); RaiseTestCommandStates(); }
        }

        public bool IsVerificationAvailable => _verifier?.IsAvailable ?? false;

        /// <summary>
        /// True when the guided field-verification sequence can also exercise the
        /// integrated keyboard RGB path. The result is intentionally advisory:
        /// software can confirm that a safe write was accepted, but only the user
        /// can confirm that the physical keyboard changed.
        /// </summary>
        public bool IsRgbCheckAvailable => _keyboardLightingService?.IsAvailable == true;

        private string _rgbCheckStatus = "RGB check not run";
        public string RgbCheckStatus
        {
            get => _rgbCheckStatus;
            private set { _rgbCheckStatus = value; OnPropertyChanged(); }
        }

        public ICommand RefreshStateCommand { get; }
        public ICommand ApplyAndVerifyCommand { get; }

        public FanDiagnosticsViewModel(
            IFanVerificationService verifier,
            FanService fanService,
            LoggingService logging,
            ConfigurationService? configService = null)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _fanService = fanService ?? throw new ArgumentNullException(nameof(fanService));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _configService = configService;

            RefreshStateCommand = new RelayCommand(_ => _ = UpdateCurrentStateAsync());
            ApplyAndVerifyCommand = new AsyncRelayCommand(_ => ApplyAndVerifyAsync(), _ => IsVerificationAvailable && !IsDiagnosticActive);

            // Constructed once and kept. These were previously expression-bodied properties that
            // returned a new command on every get: the button bound to whatever instance existed
            // when the binding first evaluated - at load, with no results yet - and since nothing
            // held that instance, its CanExecute could never be re-run. Copy Results was disabled
            // for the life of the window.
            RunGuidedDiagnosticCommand = new AsyncRelayCommand(
                _ => RunGuidedDiagnosticAsync(),
                _ => IsVerificationAvailable && !IsDiagnosticActive && !IsGuidedTestRunning);
            CopyGuidedResultCommand = new RelayCommand(
                _ => CopyGuidedResult(),
                _ => !string.IsNullOrEmpty(GuidedTestResult) && !IsGuidedTestRunning);

            // Default to CPU fan
            SelectedFanIndex = 0;
            UpdateCurrentState();
        }

        /// <summary>
        /// Supplies the lighting service after the main hardware graph has been
        /// constructed. Fan diagnostics are initialized slightly earlier than the
        /// keyboard service during startup.
        /// </summary>
        public void AttachKeyboardLightingService(KeyboardLightingService? keyboardLightingService)
        {
            _keyboardLightingService = keyboardLightingService;
            OnPropertyChanged(nameof(IsRgbCheckAvailable));
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
                
                // v2.7.1: Save current preset before diagnostic
                _preTestPreset = _fanService.ActivePreset;
                
                // Enter diagnostic mode to suspend curve engine during test
                var testLease = _fanService.AcquireFanWriteLease("Single-fan test");
                
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
                    testLease.Dispose();
                    
                    // v2.7.1: Restore previous fan preset after diagnostic
                    if (_preTestPreset != null && !FanModeNameResolver.IsAutoAlias(_preTestPreset.Name))
                    {
                        _logging.Info($"[FanDiagnostic] Restoring preset: {_preTestPreset.Name}");
                        _fanService.ApplyPreset(_preTestPreset, immediate: true);
                        _preTestPreset = null;
                    }
                    else
                    {
                        // Auto (or no preset): the test wrote fan levels directly, which a plain Auto hand-back
                        // can leave latched (GitHub #143) - use the full OEM reset.
                        HandBackToAutoAfterDirectWrites("[FanDiagnostic]");
                        _preTestPreset = null;
                    }
                    
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
            private set { _isGuidedTestRunning = value; OnPropertyChanged(); RaiseTestCommandStates(); }
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
            private set { _guidedTestResult = value; OnPropertyChanged(); RaiseTestCommandStates(); }
        }
        
        /// <summary>
        /// Progress 0-100 for guided test (0=not started, 33=30% done, 66=60% done, 100=complete)
        /// </summary>
        public int GuidedTestProgress
        {
            get => _guidedTestProgress;
            private set { _guidedTestProgress = value; OnPropertyChanged(); }
        }
        
        public ICommand RunGuidedDiagnosticCommand { get; }

        /// <summary>
        /// Copy the guided diagnostic result summary to the clipboard.
        /// </summary>
        public ICommand CopyGuidedResultCommand { get; }

        /// <summary>
        /// Re-evaluate every command whose CanExecute depends on test state.
        ///
        /// This has to be explicit. RelayCommand raises CanExecuteChanged only when asked - it
        /// does not chain off CommandManager.RequerySuggested - so a command that is not held in
        /// a field and poked here can never change its enabled state after the binding first
        /// evaluates it.
        /// </summary>
        private void RaiseTestCommandStates()
        {
            (RunGuidedDiagnosticCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CopyGuidedResultCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyAndVerifyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        private void CopyGuidedResult()
        {
            try
            {
                Clipboard.SetText(GuidedTestResult);
                _logging.Info("Fan diagnostic results copied to clipboard");
            }
            catch (Exception ex)
            {
                _logging.Warn($"Clipboard unavailable for guided diagnostics: {ex.Message}");
            }
        }
        
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
            
            // A run that starts hot measures thermal protection, not the fans (8BC2, K.6): wait for it to cool first.
            if (!await WaitForCoolDownAsync())
            {
                GuidedTestResult = $"Run not started: the CPU or GPU is at {HottestTemperatureC():0} C. Let the laptop cool below " +
                                   $"{GuidedRunRules.StartBelowC:0} C and run the diagnostic again.";
                GuidedTestStatus = "Too hot to start - cool down and run again";
                IsGuidedTestRunning = false;
                IsDiagnosticActive = false;
                return;
            }

            var testLevels = new[] { 30, 60, 100 };
            var fanNames = new[] { "CPU", "GPU" };
            var interrupted = new System.Collections.Generic.List<string>();
            var results = new System.Collections.Generic.List<(string fan, int target, bool passed, string rpm, double deviation, int score, string rating, string evidence)>();
            var sourcesSeen = new System.Collections.Generic.List<RpmSource>();
            
            // v2.7.1: Save current preset before diagnostic
            _preTestPreset = _fanService.ActivePreset;
            
            _logging.Info("=== GUIDED FAN DIAGNOSTIC STARTED ===");
            var guidedLease = _fanService.AcquireFanWriteLease("Guided Fan Verification");
            
            try
            {
                for (int levelIndex = 0; levelIndex < testLevels.Length; levelIndex++)
                {
                    var targetPercent = testLevels[levelIndex];
                    var stepResults = new System.Collections.Generic.List<FanApplyResult>();
                    var stepRows = new System.Collections.Generic.List<int>(); // index in `results` of each stepResults entry
                    
                    for (int fanIndex = 0; fanIndex < fanNames.Length; fanIndex++)
                    {
                        var fanName = fanNames[fanIndex];
                        GuidedTestStatus = $"Testing {fanName} fan at {targetPercent}%...";
                        _logging.Info($"[GuidedDiagnostic] Testing {fanName} fan at {targetPercent}%");
                        
                        try
                        {
                            var result = await _verifier.ApplyAndVerifyFanSpeedAsync(fanIndex, targetPercent);

                            // Thermal protection took over the fans mid-step: that says nothing about the fan, so it is not a failure.
                            if (GuidedRunRules.IsInterruptedByThermalProtection(result.VerificationPassed, _fanService.IsThermalProtectionActive))
                            {
                                interrupted.Add($"{fanName} @ {targetPercent}%");
                                _logging.Warn($"[GuidedDiagnostic] {fanName} at {targetPercent}% interrupted by thermal protection - not counted as a failure");
                                await Task.Delay(1000);
                                continue;
                            }
                            
                            // Use the verifier's adaptive decision so slow/stair-stepped fan telemetry
                            // is judged consistently with the single-fan diagnostic.
                            var passed = result.VerificationPassed;
                            var evidence = string.IsNullOrWhiteSpace(result.VerificationEvidence) ? "None" : result.VerificationEvidence;
                            stepResults.Add(result);
                            stepRows.Add(results.Count);
                            results.Add((fanName, targetPercent, passed, result.RpmDisplay, result.DeviationPercent, result.VerificationScore, result.ScoreRating, evidence));
                            sourcesSeen.Add(result.RpmSource);
                            
                            // Add to history
                            History.Insert(0, result);
                            
                            _logging.Info($"[GuidedDiagnostic] {fanName} at {targetPercent}%: RPM={result.ActualRpmAfter}, Deviation={result.DeviationPercent:F1}%, Score={result.VerificationScore}/100 ({result.ScoreRating}), Evidence={evidence} → {(passed ? "PASS" : "FAIL")}");
                            
                            // Brief delay between tests
                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"[GuidedDiagnostic] {fanName} at {targetPercent}% FAILED: {ex.Message}");
                            results.Add((fanName, targetPercent, false, "Physical RPM unavailable", 0, 0, "Failed", "None"));
                        }
                    }
                    
                    // A fan held by the same firmware ceiling as its sibling passes with that evidence (#207).
                    if (FanVerificationService.ReconcileCeilingAcrossFans(stepResults) > 0)
                    {
                        for (int i = 0; i < stepResults.Count; i++)
                        {
                            var row = results[stepRows[i]];
                            if (!row.passed && stepResults[i].VerificationPassed)
                            {
                                results[stepRows[i]] = (row.Item1, row.Item2, true, row.Item4, row.Item5, row.Item6, row.Item7, stepResults[i].VerificationEvidence);
                                _logging.Info($"[GuidedDiagnostic] {row.Item1} at {row.Item2}% re-judged as firmware ceiling (settled within 90% of the other fan's best level)");
                            }
                        }
                    }

                    GuidedTestProgress = ((levelIndex + 1) * 100) / testLevels.Length;
                }

                // RGB is a physical check, not an electronically verifiable result.
                // Exercise only the safe keyboard backend and make the user-visible
                // limitation explicit in both the status and copied summary.
                var rgbSummary = await RunGuidedRgbCheckAsync();
                
                // Generate summary with scores (v2.7.0)
                var passCount = results.Count(r => r.passed);
                var totalTests = results.Count;
                var overallPassed = totalTests > 0 && passCount == totalTests;
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
                // Report the sources this run actually read from, not RpmSourceDisplay - that is
                // the "current state" panel's property, refreshed on its own schedule, and it read
                // "?" on a run whose every result was EcDirect. A header that disagrees with the
                // lines beneath it is worse than no header.
                var sourceLabel = sourcesSeen.Count == 0
                    ? "none"
                    : string.Join(" + ", sourcesSeen.Distinct().OrderBy(s => s.ToString()));
                summary.AppendLine($"Backend: {_fanService.Backend} | RPM source: {sourceLabel}");
                summary.AppendLine($"Tests: {passCount}/{totalTests} passed | Overall Score: {avgScore}/100 ({overallRating})");
                summary.AppendLine($"RGB: {rgbSummary}");
                summary.AppendLine();
                
                foreach (var r in results)
                {
                    var statusIcon = r.passed ? "✓" : "✗";
                    summary.AppendLine($"{statusIcon} {r.fan} @ {r.target}%: {r.rpm} - Score: {r.score} [evidence: {r.evidence}]");
                }
                
                if (interrupted.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine($"⚠ {interrupted.Count} step(s) interrupted by thermal protection and not counted as failures: {string.Join(", ", interrupted)}.");
                    summary.AppendLine($"Let the laptop cool below {GuidedRunRules.StartBelowC:0} C and run the diagnostic again to complete them.");
                }

                GuidedTestResult = summary.ToString();
                GuidedTestStatus = interrupted.Count > 0
                    ? $"Interrupted by thermal protection ({interrupted.Count} step(s)) - cool down and re-run"
                    : overallPassed
                        ? $"All tests passed! Score: {avgScore}/100 ({overallRating})"
                        : $"Some tests failed - Score: {avgScore}/100 ({overallRating})";
                
                _logging.Info(summary.ToString());
            }
            finally
            {
                guidedLease.Dispose();
                
                // v2.7.1: Restore previous fan preset after diagnostic
                if (_preTestPreset != null && !FanModeNameResolver.IsAutoAlias(_preTestPreset.Name))
                {
                    _logging.Info($"[GuidedDiagnostic] Restoring preset: {_preTestPreset.Name}");
                    _fanService.ApplyPreset(_preTestPreset, immediate: true);
                    _preTestPreset = null;
                }
                else
                {
                    // Auto (or no preset): see HandBackToAutoAfterDirectWrites (GitHub #143).
                    HandBackToAutoAfterDirectWrites("[GuidedDiagnostic]");
                    _preTestPreset = null;
                }
                
                IsGuidedTestRunning = false;
                IsDiagnosticActive = false;
                GuidedTestProgress = 100;
            }
        }

        /// <summary>
        /// GitHub #143 (8DCD, V1): a verification run ends with level 55 written directly, and the plain Auto
        /// hand-back (SetFanMode(Default), level hint skipped by the experimental setting) left the fans pinned
        /// there. Restore OEM Auto - clear Max, Default, countdown, Default again - always recovered them on that
        /// board, so a run that started in Auto ends with that sequence, then the Auto preset is re-applied so the
        /// UI shows what the user had.
        /// </summary>
        private void HandBackToAutoAfterDirectWrites(string tag)
        {
            var autoPreset = _preTestPreset;
            try
            {
                _logging.Info($"{tag} Run started in Auto - full OEM auto restore after direct test writes");
                if (!_fanService.RestoreOemAutoControl())
                    _fanService.RestoreAutoControl();
                if (autoPreset != null)
                    _fanService.ApplyPreset(autoPreset, immediate: true);
            }
            catch (Exception restoreEx)
            {
                _logging.Warn($"{tag} Auto restore failed: {restoreEx.Message}");
            }
        }

        private double HottestTemperatureC()
        {
            var hottest = 0.0;
            foreach (var reading in _fanService.ThermalProvider.ReadTemperatures())
                hottest = Math.Max(hottest, reading.Celsius);
            return hottest;
        }

        /// <summary>Waits up to three minutes for the machine to drop below the start limit. False if it never does.</summary>
        private async Task<bool> WaitForCoolDownAsync()
        {
            var waited = 0;
            while (GuidedRunRules.NeedsCoolDown(HottestTemperatureC()))
            {
                if (waited >= 180) return false;
                GuidedTestStatus = $"Waiting for the laptop to cool below {GuidedRunRules.StartBelowC:0} C ({HottestTemperatureC():0} C now)...";
                await Task.Delay(5000);
                waited += 5;
            }
            return true;
        }

        private async Task<string> RunGuidedRgbCheckAsync()
        {
            if (_keyboardLightingService?.IsAvailable != true)
            {
                RgbCheckStatus = "Skipped: integrated RGB backend unavailable";
                return RgbCheckStatus;
            }

            // Saved zone colours, not RestoreDefaults(). That call forces white at 80%
            // and would wipe a custom keyboard. Same firmware order LightingViewModel uses
            // when it reapplies KeyboardLighting: zone 4, 3, 2, 1. SetAllZoneColors still
            // applies InvertRgbZoneOrder on top of this when that option is on.
            var savedColors = ReadSavedZoneColorsForBackend();
            try
            {
                RgbCheckStatus = "Look at your keyboard now";
                await _keyboardLightingService.SetAllZoneColors(new[]
                {
                    Color.Red,
                    Color.Lime,
                    Color.Blue,
                    Color.Yellow
                });

                var patternStatus = _keyboardLightingService.LastApplyStatus;
                var patternAccepted = WasZoneWriteAccepted(patternStatus);
                if (patternAccepted)
                    await Task.Delay(3000);

                await _keyboardLightingService.SetAllZoneColors(savedColors);
                var restoreStatus = _keyboardLightingService.LastApplyStatus;
                var restoreAccepted = WasZoneWriteAccepted(restoreStatus);
                var backend = _keyboardLightingService.BackendType;

                if (!patternAccepted)
                {
                    RgbCheckStatus = $"Pattern write not accepted by {backend}";
                    _logging.Warn($"[GuidedDiagnostic] RGB pattern write not accepted by {backend}: {patternStatus}");
                    if (!restoreAccepted)
                        _logging.Warn($"[GuidedDiagnostic] Saved keyboard colors were not restored: {restoreStatus}");
                    return RgbCheckStatus;
                }

                if (!restoreAccepted)
                {
                    RgbCheckStatus = $"Pattern applied; saved colors were not restored by {backend}";
                    _logging.Warn($"[GuidedDiagnostic] Saved keyboard colors were not restored: {restoreStatus}");
                    return RgbCheckStatus;
                }

                RgbCheckStatus = "Pattern applied and saved colors restored; physical confirmation required";
                _logging.Info("[GuidedDiagnostic] RGB test pattern applied through the safe keyboard backend; physical confirmation is required");
                return RgbCheckStatus;
            }
            catch (Exception ex)
            {
                try
                {
                    await _keyboardLightingService.SetAllZoneColors(savedColors);
                }
                catch (Exception restoreEx)
                {
                    _logging.Warn($"[GuidedDiagnostic] Saved keyboard color restore failed: {restoreEx.Message}");
                }

                RgbCheckStatus = $"Failed: {ex.Message}";
                _logging.Warn($"[GuidedDiagnostic] RGB check failed: {ex.Message}");
                return RgbCheckStatus;
            }
        }

        /// <summary>
        /// SetAllZoneColors reports failure only through <see cref="KeyboardLightingService.LastApplyStatus"/>.
        /// "Accepted but did not verify" still counts: the backend took the write, and the
        /// physical keyboard is what the guided check asks the user to confirm.
        /// </summary>
        internal static bool WasZoneWriteAccepted(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            if (status.Contains("All keyboard lighting backends failed", StringComparison.OrdinalIgnoreCase))
                return false;
            if (status.Contains("Keyboard lighting apply failed", StringComparison.OrdinalIgnoreCase))
                return false;
            if (status.Contains("throttled", StringComparison.OrdinalIgnoreCase))
                return false;
            if (status.StartsWith("No keyboard lighting apply", StringComparison.OrdinalIgnoreCase))
                return false;
            if (status.Contains("did not verify", StringComparison.OrdinalIgnoreCase)
                && !status.Contains("accepted", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private Color[] ReadSavedZoneColorsForBackend()
        {
            var settings = _configService?.Config?.KeyboardLighting ?? new KeyboardLightingSettings();
            return new[]
            {
                ParseZoneColor(settings.Zone4Color),
                ParseZoneColor(settings.Zone3Color),
                ParseZoneColor(settings.Zone2Color),
                ParseZoneColor(settings.Zone1Color)
            };
        }

        private static Color ParseZoneColor(string? hex)
        {
            var fallback = Color.FromArgb(0xE6, 0x00, 0x2E);
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            var text = hex.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
                text = text[1..];
            if (text.Length != 6)
                return fallback;

            try
            {
                return Color.FromArgb(
                    Convert.ToByte(text.Substring(0, 2), 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
            {
                return fallback;
            }
        }
        
        #endregion
    }
}
