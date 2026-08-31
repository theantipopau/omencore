using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmenCore;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class PerformanceModeService
    {
        private readonly object _applyLock = new();
        private readonly IFanController _fanController;
        private readonly PowerPlanService _powerPlanService;
        private readonly PowerLimitController? _powerLimitController;
        private readonly IPowerVerificationService? _powerVerificationService;
        private readonly LoggingService _logging;
        private readonly ModelCapabilities? _modelCapabilities;
        private readonly RuntimeEcOperationCoordinator _ecOperationCoordinator;
        private PerformanceMode? _currentMode;
        private const int MaxApplyTraceEntries = 40;
        private readonly Queue<PerformanceModeApplyTraceEntry> _applyTrace = new();
        private readonly object _applyTraceLock = new();

        /// <summary>
        /// When false (default), switching performance modes does NOT write fan policy.
        /// Users who manage fan curves or presets manually are unaffected by profile switches.
        /// Set to true to restore legacy coupled behavior where each profile also sets a fan policy.
        /// </summary>
        public bool LinkFanToPerformanceMode { get; set; } = false;

        /// <summary>
        /// Legacy compatibility escape hatch for machines where the WMI BIOS performance policy is
        /// the only known way to hold boost behavior. Keep disabled by default so the v3.2.5
        /// decoupled fan/performance contract is preserved.
        /// </summary>
        public bool AllowDecoupledWmiThermalPolicyFallback { get; set; } = false;

        private bool FanPolicyWritesBlocked =>
            _modelCapabilities?.Family == OmenModelFamily.Desktop ||
            (_modelCapabilities != null &&
             !_modelCapabilities.SupportsFanControlWmi &&
             !_modelCapabilities.SupportsFanControlEc);

        private bool FanPolicyAvailable => _fanController.IsAvailable && !FanPolicyWritesBlocked;

        // Deliberately checks SupportsEcPowerLimits, NOT SupportsFanControlEc - the latter also
        // gates real, working EC fan control on many boards and defaults to true, while
        // PowerLimitController's EC register addresses are unconfirmed placeholders per-model
        // (see ModelCapabilities.SupportsEcPowerLimits' doc comment). Blocked by default
        // (SupportsEcPowerLimits defaults false) until a model has confirmed-correct addresses.
        private bool DirectEcPowerLimitWritesBlocked =>
            _modelCapabilities == null || !_modelCapabilities.SupportsEcPowerLimits;

        /// <summary>
        /// Event raised when a performance mode is applied (for UI synchronization).
        /// </summary>
        public event EventHandler<string>? ModeApplied;

        public PerformanceModeService(
            IFanController fanController,
            PowerPlanService powerPlanService,
            PowerLimitController? powerLimitController,
            LoggingService logging,
            IPowerVerificationService? powerVerificationService = null,
            ModelCapabilities? modelCapabilities = null,
            RuntimeEcOperationCoordinator? ecOperationCoordinator = null)
        {
            _fanController = fanController;
            _powerPlanService = powerPlanService;
            _powerLimitController = powerLimitController;
            _powerVerificationService = powerVerificationService;
            _logging = logging;
            _modelCapabilities = modelCapabilities;
            _ecOperationCoordinator = ecOperationCoordinator ?? new RuntimeEcOperationCoordinator(logging);
            AllowDecoupledWmiThermalPolicyFallback = modelCapabilities?.AllowDecoupledWmiThermalPolicyFallback ?? false;
        }

        public void Apply(PerformanceMode mode)
        {
            lock (_applyLock)
            {
                // Apply model-specific TDP overrides if the database has values for this model/mode.
                var effectiveMode = ApplyModelCapabilityOverrides(mode);
                _currentMode = effectiveMode;
                var hasValidCpuLimit = effectiveMode.CpuPowerLimitWatts > 0;
                var hasValidGpuLimit = effectiveMode.GpuPowerLimitWatts > 0;
                var hasAnyValidEcLimit = hasValidCpuLimit || hasValidGpuLimit;
                var directEcPowerLimitWritesBlocked = DirectEcPowerLimitWritesBlocked;
                var ecPowerLimitAvailable = _powerLimitController != null &&
                    _powerLimitController.IsAvailable &&
                    !directEcPowerLimitWritesBlocked;
                var ecPowerLimitApplied = false;
                var ecPowerLimitSkipReason = string.Empty;
                var wmiPolicyFallbackAttempted = false;
                var wmiPolicyFallbackApplied = false;
                var fanPolicyAction = "Unchanged";
                var modeInfo = $"⚡ Applying performance mode: '{effectiveMode.Name}'";
                if (!string.IsNullOrEmpty(effectiveMode.LinkedPowerPlanGuid))
                {
                    modeInfo += $" (Power Plan: {effectiveMode.LinkedPowerPlanGuid})";
                }
                _logging.Info(modeInfo);

                // Step 1: Apply Windows power plan
                _powerPlanService.Apply(effectiveMode);

                // Step 2: Apply EC-level power limits (CPU PL1/PL2, GPU TGP)
                if (ecPowerLimitAvailable)
                {
                    try
                    {
                        // Defensive guard: never push non-positive limits to EC.
                        // Misconfigured profiles or bad override data can produce 0W values,
                        // which may severely cap performance on some platforms.
                        if (!hasValidCpuLimit && !hasValidGpuLimit)
                        {
                            _logging.Warn($"⚠️ Skipping EC power-limit apply for '{effectiveMode.Name}' because both limits are non-positive (CPU={effectiveMode.CpuPowerLimitWatts}W, GPU={effectiveMode.GpuPowerLimitWatts}W)");
                            ecPowerLimitSkipReason = "CPU/GPU limits are non-positive";
                        }
                        else
                        {
                            _ecOperationCoordinator.Execute("PerformanceModeService", "ApplyPerformanceLimits", () =>
                            {
                                if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                                {
                                    var before = _powerVerificationService.GetCurrentPowerLimits();
                                    _logging.Info($"⚡ Power limits before apply ({effectiveMode.Name}): PL1={before.cpuPl1}W, PL2={before.cpuPl2}W, GPU={before.gpuTgp}W, ModeReg={before.performanceMode}");
                                }

                                _powerLimitController!.ApplyPerformanceLimits(effectiveMode);
                                ecPowerLimitApplied = true;
                                _logging.Info($"⚡ Power limits applied: CPU={effectiveMode.CpuPowerLimitWatts}W, GPU={effectiveMode.GpuPowerLimitWatts}W");

                                if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                                {
                                    var after = _powerVerificationService.GetCurrentPowerLimits();
                                    _logging.Info($"⚡ Power limits after apply ({effectiveMode.Name}): PL1={after.cpuPl1}W, PL2={after.cpuPl2}W, GPU={after.gpuTgp}W, ModeReg={after.performanceMode}");
                                }
                            });

                            // Verify the power limits were applied correctly (non-blocking, outside EC gate)
                            if (_powerVerificationService != null && _powerVerificationService.IsAvailable)
                            {
                                _ = VerifyPowerLimitsAndLogAsync(effectiveMode);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Type-safe contention check: both TimeoutException (PawnIO mutex timeout) and
                        // AbandonedMutexException (.NET mutex abandoned by dying thread) indicate EC
                        // contention and should be treated identically. Do NOT use ex.Message.Contains
                        // — exception messages are locale-dependent.
                        if (ex is TimeoutException or AbandonedMutexException)
                        {
                            if (!Hardware.PawnIOEcAccess.EcContentionWarningLogged)
                            {
                                Hardware.PawnIOEcAccess.EcContentionWarningLogged = true;
                                _logging.Warn($"⚠️ Could not apply EC power limits due to contention: {ex.Message}. This warning will only appear once per session.");
                            }
                        }
                        else
                        {
                            _logging.Warn($"⚠️ Could not apply EC power limits: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (directEcPowerLimitWritesBlocked)
                    {
                        _logging.Info($"Direct EC power-limit writes skipped for '{effectiveMode.Name}' on model '{_modelCapabilities?.ModelName}' - using WMI thermal policy fallback when available");
                        ecPowerLimitSkipReason = $"Direct EC writes disabled for model '{_modelCapabilities?.ModelName ?? "unknown"}'";
                    }
                    else if (_powerLimitController == null)
                    {
                        ecPowerLimitSkipReason = "PowerLimitController unavailable";
                    }
                    else if (!_powerLimitController.IsAvailable)
                    {
                        ecPowerLimitSkipReason = "PowerLimitController not available";
                    }

                    _logging.Info("ℹ️ EC power limit control not available - using Windows power plan only");
                }

                // Step 3: Adjust fan curve based on power profile.
                // Only runs when LinkFanToPerformanceMode is true (opt-in, default off).
                // By default, performance mode switches only affect power plan and EC power limits;
                // existing fan presets or curves set by the user are left untouched.
                if (LinkFanToPerformanceMode)
                {
                    if (FanPolicyAvailable)
                    {
                        // Try to set performance mode via WMI BIOS first
                        if (SetFanPerformanceModeSerialized(effectiveMode.Name))
                        {
                            fanPolicyAction = "Linked fan policy";
                            _logging.Info($"🌀 Fan mode set to '{effectiveMode.Name}' via {_fanController.Backend}");
                        }
                        else
                        {
                            // Fallback to custom curve
                            var fanPercent = Math.Max(20, effectiveMode.CpuPowerLimitWatts / 2);
                            ApplyLinkedFanCurveSerialized(new[]
                            {
                                new FanCurvePoint { TemperatureC = 0, FanPercent = fanPercent }
                            });
                            fanPolicyAction = "Linked fallback curve";
                            _logging.Info($"🌀 Fan speed set to {fanPercent}% for '{effectiveMode.Name}' mode");
                        }
                    }
                    else
                    {
                        _logging.Warn("⚠️ Fan control unavailable");
                    }
                }
                else
                {
                    // WMI BIOS thermal policy is not purely a fan concern on some OMEN models.
                    // When EC limits are unavailable/non-positive, the WMI Performance/Cool policy
                    // keepalive is the only path that holds the requested TDP/boost behavior.
                    // Preserve the user's decoupled fan preset model by using this only as a narrow
                    // fallback when no valid EC limits exist.
                    var shouldUseWmiThermalPolicyFallback =
                        AllowDecoupledWmiThermalPolicyFallback &&
                        FanPolicyAvailable &&
                        string.Equals(_fanController.Backend, "WMI BIOS", StringComparison.OrdinalIgnoreCase) &&
                        (!hasAnyValidEcLimit || directEcPowerLimitWritesBlocked);

                    if (shouldUseWmiThermalPolicyFallback)
                    {
                        wmiPolicyFallbackAttempted = true;
                        if (SetFanPerformanceModeSerialized(effectiveMode.Name))
                        {
                            wmiPolicyFallbackApplied = true;
                            fanPolicyAction = "Decoupled WMI thermal policy fallback";
                            _logging.Info($"🌀 Applied decoupled WMI thermal policy hold for '{effectiveMode.Name}' because EC power limits are unavailable");
                        }
                        else
                        {
                            fanPolicyAction = "Decoupled WMI thermal policy fallback failed";
                            _logging.Warn($"⚠️ WMI thermal policy fallback failed for '{effectiveMode.Name}' while EC power limits were unavailable");
                        }
                    }
                    else
                    {
                        _logging.Info("ℹ️ Fan policy unchanged — LinkFanToPerformanceMode is off");
                    }
                }

                // Say what actually happened. Every path above can decline to do anything - EC writes
                // blocked for the model, the WMI policy fallback not permitted, fan policy decoupled
                // by default - and on a board where all three decline, the only real effect of an
                // "applied successfully" is the Windows power plan. Reporting success there trains a
                // user to believe a mode switch did something it did not, and hides the one line that
                // would tell them why: the skip reason. The flags are already tracked for the trace
                // entry below; this just stops discarding them on the way to the log.
                var appliedEffects = new List<string>();
                if (!string.IsNullOrEmpty(effectiveMode.LinkedPowerPlanGuid)) appliedEffects.Add("Windows power plan");
                if (ecPowerLimitApplied) appliedEffects.Add("EC power limits");
                if (wmiPolicyFallbackApplied) appliedEffects.Add("WMI thermal policy");
                if (LinkFanToPerformanceMode && fanPolicyAction.StartsWith("Linked", StringComparison.Ordinal))
                    appliedEffects.Add(fanPolicyAction == "Linked fan policy" ? "fan policy" : "fan curve");

                var hardwareEffectApplied = ecPowerLimitApplied || wmiPolicyFallbackApplied ||
                    (LinkFanToPerformanceMode && fanPolicyAction.StartsWith("Linked", StringComparison.Ordinal));

                if (appliedEffects.Count == 0)
                {
                    _logging.Warn($"⚠️ Performance mode '{effectiveMode.Name}': nothing was applied" +
                        (string.IsNullOrEmpty(ecPowerLimitSkipReason) ? "" : $" ({ecPowerLimitSkipReason})"));
                }
                else if (!hardwareEffectApplied)
                {
                    _logging.Info($"✓ Performance mode '{effectiveMode.Name}' applied: {string.Join(", ", appliedEffects)}" +
                        " — no hardware power or fan change" +
                        (string.IsNullOrEmpty(ecPowerLimitSkipReason) ? "" : $" ({ecPowerLimitSkipReason})"));
                }
                else
                {
                    _logging.Info($"✓ Performance mode '{effectiveMode.Name}' applied: {string.Join(", ", appliedEffects)}");
                }

                RecordApplyTrace(new PerformanceModeApplyTraceEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    RequestedModeName = mode.Name,
                    EffectiveModeName = effectiveMode.Name,
                    CpuPowerLimitWatts = effectiveMode.CpuPowerLimitWatts,
                    CpuBoostPowerLimitWatts = effectiveMode.CpuBoostPowerLimitWatts,
                    GpuPowerLimitWatts = effectiveMode.GpuPowerLimitWatts,
                    LinkedPowerPlanGuid = effectiveMode.LinkedPowerPlanGuid,
                    ModelName = _modelCapabilities?.ModelName,
                    ProductId = _modelCapabilities?.ProductId,
                    FanBackend = _fanController.Backend,
                    LinkFanToPerformanceMode = LinkFanToPerformanceMode,
                    AllowWmiThermalPolicyFallback = AllowDecoupledWmiThermalPolicyFallback,
                    EcPowerLimitAvailable = ecPowerLimitAvailable,
                    EcPowerLimitApplied = ecPowerLimitApplied,
                    EcPowerLimitSkipReason = ecPowerLimitSkipReason,
                    WmiPolicyFallbackAttempted = wmiPolicyFallbackAttempted,
                    WmiPolicyFallbackApplied = wmiPolicyFallbackApplied,
                    FanPolicyAction = fanPolicyAction
                });

                // Raise event for UI synchronization (sidebar, tray, etc.)
                PublishModeApplied(effectiveMode.Name);
            }
        }

        private void PublishModeApplied(string modeName)
        {
            var handlers = ModeApplied;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, modeName);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Performance mode subscriber failed for '{modeName}': {ex.Message}");
                }
            }
        }

        private void RecordApplyTrace(PerformanceModeApplyTraceEntry entry)
        {
            lock (_applyTraceLock)
            {
                while (_applyTrace.Count >= MaxApplyTraceEntries)
                {
                    _applyTrace.Dequeue();
                }

                _applyTrace.Enqueue(entry);
            }
        }

        public IReadOnlyList<PerformanceModeApplyTraceEntry> GetApplyTraceSnapshot()
        {
            lock (_applyTraceLock)
            {
                return _applyTrace.ToList();
            }
        }

        public string GetApplyTraceReport()
        {
            var entries = GetApplyTraceSnapshot();
            var lines = new System.Text.StringBuilder();

            lines.AppendLine("Performance Mode Apply Trace");
            lines.AppendLine($"CurrentMode: {_currentMode?.Name ?? "<none>"}");
            lines.AppendLine($"Controls: {ControlCapabilityDescription}");
            lines.AppendLine($"Entries: {entries.Count}");
            lines.AppendLine(new string('-', 80));

            foreach (var entry in entries)
            {
                lines.AppendLine($"{entry.TimestampUtc:O} | requested={entry.RequestedModeName} | effective={entry.EffectiveModeName}");
                lines.AppendLine($"  model={entry.ModelName ?? "<unknown>"} ({entry.ProductId ?? "unknown"}); fanBackend={entry.FanBackend}");
                lines.AppendLine($"  limits: PL1={entry.CpuPowerLimitWatts}W; PL2={(entry.CpuBoostPowerLimitWatts?.ToString() ?? "auto")}W; GPU={entry.GpuPowerLimitWatts}W; plan={entry.LinkedPowerPlanGuid ?? "<none>"}");
                lines.AppendLine($"  ecPower: available={entry.EcPowerLimitAvailable}; applied={entry.EcPowerLimitApplied}; skip={FormatTraceValue(entry.EcPowerLimitSkipReason)}");
                lines.AppendLine($"  fanPolicy: linked={entry.LinkFanToPerformanceMode}; fallbackAllowed={entry.AllowWmiThermalPolicyFallback}; fallbackAttempted={entry.WmiPolicyFallbackAttempted}; fallbackApplied={entry.WmiPolicyFallbackApplied}; action={entry.FanPolicyAction}");
            }

            return lines.ToString();
        }

        private static string FormatTraceValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "<none>" : value;

        private bool SetFanPerformanceModeSerialized(string modeName) =>
            _ecOperationCoordinator.Execute(
                "PerformanceModeService",
                "SetFanPerformanceMode",
                () => _fanController.SetPerformanceMode(modeName));

        private bool ApplyLinkedFanCurveSerialized(IEnumerable<FanCurvePoint> curve) =>
            _ecOperationCoordinator.Execute(
                "PerformanceModeService",
                "ApplyLinkedFanCurve",
                () => _fanController.ApplyCustomCurve(curve));

        /// <summary>
        /// Returns a copy of <paramref name="mode"/> with TDP values overridden by model-specific
        /// capability database entries when available.  Global config values are used as fallback
        /// so existing behaviour is preserved for all other models.
        /// </summary>
        private PerformanceMode ApplyModelCapabilityOverrides(PerformanceMode mode)
        {
            return ResolveEffectiveMode(mode);
        }

        /// <summary>
        /// Resolves the effective <see cref="PerformanceMode"/> that will actually be applied,
        /// incorporating any model-specific TDP overrides from the capability database.
        /// Exposed publicly for diagnostics and unit tests.
        /// </summary>
        public PerformanceMode ResolveEffectiveMode(PerformanceMode mode)
        {
            if (_modelCapabilities == null)
            {
                return mode;
            }

            var modeName = mode.Name.ToLowerInvariant().Trim();
            int? cpuOverride = null;
            int? boostOverride = null;
            int? gpuOverride = null;

            if (modeName is "performance" or "extreme" or "turbo")
            {
                cpuOverride = _modelCapabilities.PerformanceCpuPl1Watts;
                boostOverride = _modelCapabilities.PerformanceCpuPl2Watts;
                gpuOverride = _modelCapabilities.PerformanceGpuTgpWatts;
            }
            else if (modeName is "balanced" or "default" or "normal")
            {
                cpuOverride = _modelCapabilities.BalancedCpuPl1Watts;
                gpuOverride = _modelCapabilities.BalancedGpuTgpWatts;
            }
            else if (modeName is "eco" or "quiet" or "silent" or "powersaver")
            {
                cpuOverride = _modelCapabilities.EcoCpuPl1Watts;
            }

            cpuOverride = NormalizePositiveOverride(cpuOverride, mode.Name, "CPU PL1");
            boostOverride = NormalizePositiveOverride(boostOverride, mode.Name, "CPU PL2");
            gpuOverride = NormalizePositiveOverride(gpuOverride, mode.Name, "GPU");

            if (cpuOverride == null && gpuOverride == null && boostOverride == null)
            {
                return mode;
            }

            var overriddenCpu = cpuOverride ?? mode.CpuPowerLimitWatts;
            var overriddenBoost = boostOverride ?? mode.CpuBoostPowerLimitWatts;
            var overriddenGpu = gpuOverride ?? mode.GpuPowerLimitWatts;

            _logging.Info(
                $"⚡ Model capability override for '{mode.Name}': " +
                $"CPU PL1 {mode.CpuPowerLimitWatts}W → {overriddenCpu}W, " +
                $"CPU PL2 {(mode.CpuBoostPowerLimitWatts?.ToString() ?? "auto")}W → {(overriddenBoost?.ToString() ?? "auto")}W, " +
                $"GPU {mode.GpuPowerLimitWatts}W → {overriddenGpu}W " +
                $"(model: {_modelCapabilities.ModelName})");

            return new PerformanceMode
            {
                Name = mode.Name,
                CpuPowerLimitWatts = overriddenCpu,
                CpuBoostPowerLimitWatts = overriddenBoost,
                GpuPowerLimitWatts = overriddenGpu,
                LinkedPowerPlanGuid = mode.LinkedPowerPlanGuid,
                Description = mode.Description
            };
        }

        private int? NormalizePositiveOverride(int? overrideValue, string modeName, string fieldName)
        {
            if (!overrideValue.HasValue)
            {
                return null;
            }

            if (overrideValue.Value > 0)
            {
                return overrideValue;
            }

            _logging.Warn($"⚠️ Ignoring invalid model capability override for '{modeName}' {fieldName}: {overrideValue.Value}W (expected > 0)");
            return null;
        }

        private async Task VerifyPowerLimitsAndLogAsync(PerformanceMode mode)
        {
            try
            {
                if (_powerVerificationService == null || !_powerVerificationService.IsAvailable)
                {
                    return;
                }

                var verified = await _powerVerificationService.VerifyPowerLimitsAsync(mode).ConfigureAwait(false);
                if (verified)
                {
                    _logging.Info("✓ Power limits verified successfully");
                }
                else
                {
                    _logging.Warn("⚠️ Power limits verification failed - values may not have been applied");
                }

                var observed = _powerVerificationService.GetCurrentPowerLimits();
                _logging.Info(
                    $"⚡ Power verify snapshot ({mode.Name}): ExpectedCPU={mode.CpuPowerLimitWatts}W, ExpectedGPU={mode.GpuPowerLimitWatts}W, " +
                    $"ObservedPL1={observed.cpuPl1}W, ObservedPL2={observed.cpuPl2}W, ObservedGPU={observed.gpuTgp}W, ModeReg={observed.performanceMode}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"⚠️ Power limits verification threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Set performance mode by name (for GeneralView quick profiles).
        /// </summary>
        public void SetPerformanceMode(string modeName)
        {
            // Map common names to default modes
            PerformanceMode? mode = modeName.ToLowerInvariant() switch
            {
                "performance" => new PerformanceMode
                {
                    Name = "Performance",
                    CpuPowerLimitWatts = 95,
                    GpuPowerLimitWatts = 140
                },
                "quiet" or "silent" or "powersaver" => new PerformanceMode
                {
                    Name = "Quiet",
                    CpuPowerLimitWatts = 35,
                    GpuPowerLimitWatts = 60
                },
                "balanced" or "default" => new PerformanceMode
                {
                    Name = "Balanced",
                    CpuPowerLimitWatts = 65,
                    GpuPowerLimitWatts = 100
                },
                _ => new PerformanceMode
                {
                    Name = "Balanced",
                    CpuPowerLimitWatts = 65,
                    GpuPowerLimitWatts = 100
                }
            };

            Apply(mode);
        }

        /// <summary>
        /// Expose the currently applied performance mode instance.
        /// </summary>
        public PerformanceMode? CurrentMode => _currentMode;

        /// <summary>
        /// Get the current performance mode name.
        /// </summary>
        public string? GetCurrentMode() => _currentMode?.Name;

        /// <summary>
        /// Whether EC-level power limit control is available.
        /// When false, performance modes only change Windows power plan and fan policy.
        /// </summary>
        public bool EcPowerControlAvailable => _powerLimitController != null && _powerLimitController.IsAvailable && !DirectEcPowerLimitWritesBlocked;

        /// <summary>
        /// Get a human-readable description of what controls are available.
        /// Useful for UI to show users what changing performance mode actually does.
        /// </summary>
        public string ControlCapabilityDescription
        {
            get
            {
                var capabilities = new List<string> { "Windows Power Plan" };

                if (FanPolicyAvailable)
                    capabilities.Add("Fan Policy");

                if (AllowDecoupledWmiThermalPolicyFallback && FanPolicyAvailable)
                    capabilities.Add("WMI Performance Policy Fallback");

                if (_powerLimitController != null && _powerLimitController.IsAvailable && !DirectEcPowerLimitWritesBlocked)
                    capabilities.Add("CPU/GPU Power Limits");

                return string.Join(", ", capabilities);
            }
        }

        /// <summary>
        /// Return the configured performance modes available to the UI.
        /// </summary>
        public IEnumerable<PerformanceMode> GetAvailableModes()
        {
            return AppHost.Configuration?.Config.PerformanceModes ?? new List<PerformanceMode>();
        }

        public IReadOnlyList<PerformanceMode> GetModes(AppConfig config) => config.PerformanceModes;
    }

    public sealed class PerformanceModeApplyTraceEntry
    {
        public DateTime TimestampUtc { get; init; }
        public string RequestedModeName { get; init; } = string.Empty;
        public string EffectiveModeName { get; init; } = string.Empty;
        public int CpuPowerLimitWatts { get; init; }
        public int? CpuBoostPowerLimitWatts { get; init; }
        public int GpuPowerLimitWatts { get; init; }
        public string? LinkedPowerPlanGuid { get; init; }
        public string? ModelName { get; init; }
        public string? ProductId { get; init; }
        public string FanBackend { get; init; } = string.Empty;
        public bool LinkFanToPerformanceMode { get; init; }
        public bool AllowWmiThermalPolicyFallback { get; init; }
        public bool EcPowerLimitAvailable { get; init; }
        public bool EcPowerLimitApplied { get; init; }
        public string EcPowerLimitSkipReason { get; init; } = string.Empty;
        public bool WmiPolicyFallbackAttempted { get; init; }
        public bool WmiPolicyFallbackApplied { get; init; }
        public string FanPolicyAction { get; init; } = string.Empty;
    }
}
