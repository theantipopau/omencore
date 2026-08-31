using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading.Tasks;
using OmenCore.Services.SystemOptimizer.Optimizations;

namespace OmenCore.Services.SystemOptimizer
{
    /// <summary>
    /// Main orchestration service for all Windows gaming optimizations.
    /// Coordinates individual optimizers and manages state.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SystemOptimizerService : IDisposable
    {
        private readonly LoggingService _logger;
        private readonly RegistryBackupService _backupService;
        private readonly Func<bool> _isAdminChecker;
        private readonly OptimizationVerifier _verifier;
        
        // Individual optimizers
        private readonly PowerOptimizer _powerOptimizer;
        private readonly ServiceOptimizer _serviceOptimizer;
        private readonly NetworkOptimizer _networkOptimizer;
        private readonly InputOptimizer _inputOptimizer;
        private readonly VisualEffectsOptimizer _visualOptimizer;
        private readonly StorageOptimizer _storageOptimizer;

        public event Action<string>? StatusChanged;
        public event Action<OptimizationResult>? OptimizationCompleted;

        public SystemOptimizerService(LoggingService logger, Func<bool>? isAdminChecker = null)
        {
            _logger = logger;
            _isAdminChecker = isAdminChecker ?? IsRunningAsAdmin;
            _backupService = new RegistryBackupService(logger);
            _verifier = new OptimizationVerifier(logger);
            
            // Initialize all optimizers
            _powerOptimizer = new PowerOptimizer(logger, _backupService);
            _serviceOptimizer = new ServiceOptimizer(logger, _backupService);
            _networkOptimizer = new NetworkOptimizer(logger, _backupService);
            _inputOptimizer = new InputOptimizer(logger, _backupService);
            _visualOptimizer = new VisualEffectsOptimizer(logger, _backupService);
            _storageOptimizer = new StorageOptimizer(logger, _backupService);
        }

        /// <summary>
        /// Performs an authoritative verification pass against the live system state.
        /// </summary>
        public async Task<OptimizationState> VerifyStateAsync()
        {
            try
            {
                StatusChanged?.Invoke("Verifying optimization state...");
                var state = await _verifier.VerifyAllAsync();
                state.LastChecked = DateTime.Now;
                StatusChanged?.Invoke($"Verification complete: {state.ActiveCount}/{state.TotalCount} optimizations active");
                return state;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to verify optimization state: {ex.Message}");
                return new OptimizationState { LastChecked = DateTime.Now };
            }
        }

        /// <summary>
        /// Re-applies a small set of low-risk service toggles if they drift away from the expected state.
        /// </summary>
        public async Task<List<OptimizationResult>> CorrectMinorDriftAsync(OptimizationState expectedState, OptimizationState actualState)
        {
            var results = new List<OptimizationResult>();

            if (!_isAdminChecker())
            {
                return results;
            }

            if (expectedState.Services.SysMainDisabled && !actualState.Services.SysMainDisabled)
            {
                results.Add(await ApplyOptimizationAsync("service_sysmain"));
            }

            if (expectedState.Services.SearchIndexingDisabled && !actualState.Services.SearchIndexingDisabled)
            {
                results.Add(await ApplyOptimizationAsync("service_search"));
            }

            if (expectedState.Services.DiagTrackDisabled && !actualState.Services.DiagTrackDisabled)
            {
                results.Add(await ApplyOptimizationAsync("service_diagtrack"));
            }

            return results;
        }

        /// <summary>
        /// Gets the current state of all optimizations.
        /// </summary>
        public async Task<OptimizationState> GetCurrentStateAsync()
        {
            var state = new OptimizationState();
            
            try
            {
                StatusChanged?.Invoke("Checking optimization status...");
                
                // Check each category
                state.Power = await _powerOptimizer.GetStateAsync();
                state.Services = await _serviceOptimizer.GetStateAsync();
                state.Network = await _networkOptimizer.GetStateAsync();
                state.Input = await _inputOptimizer.GetStateAsync();
                state.Visual = await _visualOptimizer.GetStateAsync();
                state.Storage = await _storageOptimizer.GetStateAsync();
                
                state.LastChecked = DateTime.Now;
                StatusChanged?.Invoke($"Status check complete: {state.ActiveCount}/{state.TotalCount} optimizations active");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get optimization state: {ex.Message}");
            }
            
            return state;
        }

        /// <summary>
        /// Applies the "Gaming Maximum" preset - all gaming optimizations.
        /// </summary>
        public async Task<List<OptimizationResult>> ApplyGamingMaximumAsync()
        {
            var results = new List<OptimizationResult>();

            if (!_isAdminChecker())
            {
                const string msg = "Administrator privileges are required to apply optimizer changes.";
                StatusChanged?.Invoke(msg);
                _logger.Warn(msg);
                results.Add(new OptimizationResult
                {
                    Id = "optimizer_admin_preflight",
                    Name = "Administrator preflight",
                    Success = false,
                    ErrorMessage = msg
                });
                return results;
            }
            
            _logger.Info("Applying Gaming Maximum optimization profile...");
            StatusChanged?.Invoke("Creating system restore point...");
            
            // Create restore point first
            await _backupService.CreateRestorePointAsync("OmenCore Gaming Optimization");
            
            StatusChanged?.Invoke("Applying power optimizations...");
            results.AddRange(await _powerOptimizer.ApplyAllAsync());
            
            StatusChanged?.Invoke("Optimizing services...");
            results.AddRange(await _serviceOptimizer.ApplyAllAsync());
            
            StatusChanged?.Invoke("Applying network tweaks...");
            results.AddRange(await _networkOptimizer.ApplyAllAsync());
            
            StatusChanged?.Invoke("Optimizing input settings...");
            results.AddRange(await _inputOptimizer.ApplyAllAsync());
            
            StatusChanged?.Invoke("Adjusting visual effects...");
            results.AddRange(await _visualOptimizer.ApplyAllAsync());
            
            StatusChanged?.Invoke("Configuring storage...");
            results.AddRange(await _storageOptimizer.ApplyAllAsync());
            
            var successCount = results.FindAll(r => r.Success).Count;
            _logger.Info($"Gaming Maximum applied: {successCount}/{results.Count} optimizations successful");
            StatusChanged?.Invoke($"Complete: {successCount}/{results.Count} optimizations applied");
            
            return results;
        }

        /// <summary>
        /// Applies the "Balanced" preset - recommended optimizations without aggressive tweaks.
        /// </summary>
        public async Task<List<OptimizationResult>> ApplyBalancedAsync()
        {
            var results = new List<OptimizationResult>();

            if (!_isAdminChecker())
            {
                const string msg = "Administrator privileges are required to apply optimizer changes.";
                StatusChanged?.Invoke(msg);
                _logger.Warn(msg);
                results.Add(new OptimizationResult
                {
                    Id = "optimizer_admin_preflight",
                    Name = "Administrator preflight",
                    Success = false,
                    ErrorMessage = msg
                });
                return results;
            }
            
            _logger.Info("Applying Balanced optimization profile...");
            StatusChanged?.Invoke("Creating backup...");
            
            await _backupService.CreateRestorePointAsync("OmenCore Balanced Optimization");
            
            // Apply only safe/recommended optimizations
            StatusChanged?.Invoke("Applying recommended optimizations...");
            results.AddRange(await _powerOptimizer.ApplyRecommendedAsync());
            results.AddRange(await _serviceOptimizer.ApplyRecommendedAsync());
            results.AddRange(await _networkOptimizer.ApplyRecommendedAsync());
            results.AddRange(await _inputOptimizer.ApplyRecommendedAsync());
            results.AddRange(await _visualOptimizer.ApplyRecommendedAsync());
            results.AddRange(await _storageOptimizer.ApplyRecommendedAsync());
            
            var successCount = results.FindAll(r => r.Success).Count;
            _logger.Info($"Balanced profile applied: {successCount}/{results.Count} optimizations successful");
            StatusChanged?.Invoke($"Complete: {successCount}/{results.Count} optimizations applied");
            
            return results;
        }

        /// <summary>
        /// Reverts all optimizations to Windows defaults.
        /// </summary>
        public async Task<List<OptimizationResult>> RevertAllAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var results = new List<OptimizationResult>();

            if (!_isAdminChecker())
            {
                const string msg = "Administrator privileges are required to revert optimizer changes.";
                StatusChanged?.Invoke(msg);
                _logger.Warn(msg);
                results.Add(new OptimizationResult
                {
                    Id = "optimizer_admin_preflight",
                    Name = "Administrator preflight",
                    Success = false,
                    ErrorMessage = msg
                });
                return results;
            }
            
            _logger.Info("Reverting all optimizations to defaults...");

            cancellationToken.ThrowIfCancellationRequested();

            // Stage 1-2 are kept sequential because they are the heaviest and can affect
            // downstream command execution behavior.
            results.AddRange(await RunRevertStageSafeAsync(
                stageId: "revert_power",
                stageName: "power settings",
                stageIndex: 1,
                stageTotal: 6,
                revertAction: () => _powerOptimizer.RevertAllAsync(),
                cancellationToken: cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            results.AddRange(await RunRevertStageSafeAsync(
                stageId: "revert_services",
                stageName: "services",
                stageIndex: 2,
                stageTotal: 6,
                revertAction: () => _serviceOptimizer.RevertAllAsync(),
                cancellationToken: cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            // Stage 3-6 are compatible and can safely run in parallel.
            StatusChanged?.Invoke("[3-6/6] Reverting network, input, visual, and storage settings in parallel...");

            var parallelStages = new[]
            {
                RunRevertStageSafeAsync(
                    stageId: "revert_network",
                    stageName: "network tweaks",
                    stageIndex: 3,
                    stageTotal: 6,
                    revertAction: () => _networkOptimizer.RevertAllAsync(),
                    cancellationToken: cancellationToken),
                RunRevertStageSafeAsync(
                    stageId: "revert_input",
                    stageName: "input settings",
                    stageIndex: 4,
                    stageTotal: 6,
                    revertAction: () => _inputOptimizer.RevertAllAsync(),
                    cancellationToken: cancellationToken),
                RunRevertStageSafeAsync(
                    stageId: "revert_visual",
                    stageName: "visual effects",
                    stageIndex: 5,
                    stageTotal: 6,
                    revertAction: () => _visualOptimizer.RevertAllAsync(),
                    cancellationToken: cancellationToken),
                RunRevertStageSafeAsync(
                    stageId: "revert_storage",
                    stageName: "storage settings",
                    stageIndex: 6,
                    stageTotal: 6,
                    revertAction: () => _storageOptimizer.RevertAllAsync(),
                    cancellationToken: cancellationToken)
            };

            var parallelResults = await Task.WhenAll(parallelStages);
            foreach (var stageResults in parallelResults)
            {
                results.AddRange(stageResults);
            }

            cancellationToken.ThrowIfCancellationRequested();
            
            var successCount = results.FindAll(r => r.Success).Count;
            _logger.Info($"Revert complete: {successCount}/{results.Count} settings restored");
            StatusChanged?.Invoke($"Revert complete: {successCount}/{results.Count} settings restored");
            
            return results;
        }

        private async Task<List<OptimizationResult>> RunRevertStageSafeAsync(
            string stageId,
            string stageName,
            int stageIndex,
            int stageTotal,
            Func<Task<List<OptimizationResult>>> revertAction,
            System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusChanged?.Invoke($"[{stageIndex}/{stageTotal}] Reverting {stageName}...");
                var stageResults = await revertAction();
                cancellationToken.ThrowIfCancellationRequested();
                return stageResults;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to revert {stageName}: {ex.Message}", ex);
                StatusChanged?.Invoke($"[{stageIndex}/{stageTotal}] Failed to revert {stageName}: {ex.Message}");
                return new List<OptimizationResult>
                {
                    new OptimizationResult
                    {
                        Id = stageId,
                        Name = $"Revert {stageName}",
                        Success = false,
                        ErrorMessage = ex.Message
                    }
                };
            }
        }

        /// <summary>
        /// Applies a single optimization by ID.
        /// </summary>
        public async Task<OptimizationResult> ApplyOptimizationAsync(string optimizationId)
        {
            var result = new OptimizationResult { Id = optimizationId };

            if (!_isAdminChecker())
            {
                result.Success = false;
                result.ErrorMessage = "Administrator privileges are required to apply optimizer changes.";
                StatusChanged?.Invoke(result.ErrorMessage);
                OptimizationCompleted?.Invoke(result);
                return result;
            }
            
            try
            {
                // Route to appropriate optimizer based on prefix
                if (optimizationId.StartsWith("power_"))
                    result = await _powerOptimizer.ApplyAsync(optimizationId);
                else if (optimizationId.StartsWith("service_"))
                    result = await _serviceOptimizer.ApplyAsync(optimizationId);
                else if (optimizationId.StartsWith("network_"))
                    result = await _networkOptimizer.ApplyAsync(optimizationId);
                else if (optimizationId.StartsWith("input_"))
                    result = await _inputOptimizer.ApplyAsync(optimizationId);
                else if (optimizationId.StartsWith("visual_"))
                    result = await _visualOptimizer.ApplyAsync(optimizationId);
                else if (optimizationId.StartsWith("storage_"))
                    result = await _storageOptimizer.ApplyAsync(optimizationId);
                else
                    result.ErrorMessage = $"Unknown optimization: {optimizationId}";
                    
                OptimizationCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.Error($"Failed to apply {optimizationId}: {ex.Message}");
            }
            
            return result;
        }

        /// <summary>
        /// Reverts a single optimization by ID.
        /// </summary>
        public async Task<OptimizationResult> RevertOptimizationAsync(string optimizationId)
        {
            var result = new OptimizationResult { Id = optimizationId };

            if (!_isAdminChecker())
            {
                result.Success = false;
                result.ErrorMessage = "Administrator privileges are required to revert optimizer changes.";
                StatusChanged?.Invoke(result.ErrorMessage);
                OptimizationCompleted?.Invoke(result);
                return result;
            }
            
            try
            {
                if (optimizationId.StartsWith("power_"))
                    result = await _powerOptimizer.RevertAsync(optimizationId);
                else if (optimizationId.StartsWith("service_"))
                    result = await _serviceOptimizer.RevertAsync(optimizationId);
                else if (optimizationId.StartsWith("network_"))
                    result = await _networkOptimizer.RevertAsync(optimizationId);
                else if (optimizationId.StartsWith("input_"))
                    result = await _inputOptimizer.RevertAsync(optimizationId);
                else if (optimizationId.StartsWith("visual_"))
                    result = await _visualOptimizer.RevertAsync(optimizationId);
                else if (optimizationId.StartsWith("storage_"))
                    result = await _storageOptimizer.RevertAsync(optimizationId);
                else
                    result.ErrorMessage = $"Unknown optimization: {optimizationId}";
                    
                OptimizationCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.Error($"Failed to revert {optimizationId}: {ex.Message}");
            }
            
            return result;
        }

        public void Dispose()
        {
            _powerOptimizer?.Dispose();
            _serviceOptimizer?.Dispose();
        }

        /// <summary>
        /// Generate a preflight report listing every planned operation with its risk tier,
        /// reboot requirement, and any advisory warnings — before any changes are applied.
        /// Callers should present high-risk items to the user and require explicit opt-in.
        /// </summary>
        /// <param name="includeHighRisk">
        /// When false only Low and Medium risk operations are included.
        /// Use false for "Balanced" profile previews; true for "Gaming Maximum" previews.
        /// </param>
        public Task<PreflightReport> GeneratePreflightReportAsync(bool includeHighRisk = true)
        {
            var items = BuildOperationCatalog();
            if (!includeHighRisk)
            {
                items = items.Where(i => i.Risk != OptimizationRisk.High).ToList();
            }

            return Task.FromResult(new PreflightReport
            {
                Items = items,
                GeneratedUtc = DateTime.UtcNow
            });
        }

        // Build the canonical list of all known operations and their risk metadata.
        // This is intentionally a static catalogue mirroring the individual optimizer apply paths.
        // When a new optimizer operation is added, add its entry here as well.
        private static List<PreflightItem> BuildOperationCatalog()
        {
            return new List<PreflightItem>
            {
                // Power
                new() { Id = "power_ultimate_performance", Name = "Ultimate Performance power plan", Category = "Power",
                    Risk = OptimizationRisk.Medium, IsRecommended = false, RequiresReboot = false,
                    Warning = "Increases idle power draw; not recommended on battery." },
                new() { Id = "power_high_performance", Name = "High Performance power plan", Category = "Power",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "power_gpu_scheduling", Name = "Hardware-accelerated GPU scheduling (HAGS)", Category = "Power",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = true },
                new() { Id = "power_game_mode", Name = "Windows Game Mode", Category = "Power",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "power_foreground_priority", Name = "Foreground process priority boost", Category = "Power",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },

                // Services
                new() { Id = "service_telemetry", Name = "Disable Windows telemetry service", Category = "Services",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "service_sysmain", Name = "Disable SysMain (Superfetch)", Category = "Services",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false,
                    Warning = "May slow app-launch times on HDDs; beneficial on SSDs." },
                new() { Id = "service_search_indexing", Name = "Disable search indexing", Category = "Services",
                    Risk = OptimizationRisk.Medium, IsRecommended = false, RequiresReboot = false,
                    Warning = "Windows Search will be slower after this change." },
                new() { Id = "service_diagtrack", Name = "Disable DiagTrack (Connected User Experiences)", Category = "Services",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },

                // Network
                new() { Id = "network_tcp_nodelay", Name = "Disable Nagle algorithm (TCP no-delay)", Category = "Network",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "network_tcp_ack_frequency", Name = "TCP ACK frequency tuning", Category = "Network",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "network_delivery_optimization", Name = "Disable Delivery Optimization", Category = "Network",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "network_nagle", Name = "Additional Nagle algorithm disable", Category = "Network",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },

                // Input
                new() { Id = "input_mouse_acceleration", Name = "Disable mouse pointer acceleration (Enhance Pointer Precision)", Category = "Input",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "input_game_dvr", Name = "Disable Game DVR background recording", Category = "Input",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "input_game_bar", Name = "Disable Game Bar overlay", Category = "Input",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "input_fullscreen_optimizations", Name = "Disable fullscreen optimizations", Category = "Input",
                    Risk = OptimizationRisk.Medium, IsRecommended = false, RequiresReboot = false,
                    Warning = "Some HDR and variable-refresh features may not work in affected apps." },

                // Visual Effects
                new() { Id = "visual_animations", Name = "Disable UI animations", Category = "Visual",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "visual_transparency", Name = "Disable transparency effects", Category = "Visual",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },

                // Storage
                new() { Id = "storage_trim", Name = "Enable SSD TRIM", Category = "Storage",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "storage_defrag_disable", Name = "Disable scheduled defragmentation (SSD)", Category = "Storage",
                    Risk = OptimizationRisk.Low, IsRecommended = true, RequiresReboot = false },
                new() { Id = "storage_short_names", Name = "Disable 8.3 filename creation", Category = "Storage",
                    Risk = OptimizationRisk.High, IsRecommended = false, RequiresReboot = false,
                    Warning = "Some legacy 16-bit or very old programs may fail to launch after this change." },
                new() { Id = "storage_last_access", Name = "Disable NTFS last-access timestamp update", Category = "Storage",
                    Risk = OptimizationRisk.Medium, IsRecommended = false, RequiresReboot = false,
                    Warning = "Backup tools that rely on last-access timestamps may not detect unchanged files correctly." },
            };
        }

        /// <summary>
        /// Compare an expected (previously applied) optimizer state against a freshly read actual
        /// state and return human-readable explanations for each setting that has drifted.
        /// Typical callers: optimizer VM, drift-correction notifications.
        /// </summary>
        public static OptimizationDriftSummary GetDriftExplanations(
            OptimizationState expected,
            OptimizationState actual)
        {
            var items = new List<OptimizationDriftItem>();

            // Power
            if (expected.Power.UltimatePerformancePlan && !actual.Power.UltimatePerformancePlan)
                items.Add(new OptimizationDriftItem { Id = "power_ultimate_performance", Name = "Ultimate Performance power plan",
                    Category = "Power", Explanation = "Ultimate Performance power plan is no longer the active plan. Windows or a power event (e.g. sleep/wake, Windows Update) may have switched to a different plan." });

            if (expected.Power.HardwareGpuScheduling && !actual.Power.HardwareGpuScheduling)
                items.Add(new OptimizationDriftItem { Id = "power_gpu_scheduling", Name = "Hardware-accelerated GPU scheduling",
                    Category = "Power", Explanation = "HAGS registry key is no longer set. A GPU driver update or Windows feature reset may have cleared this." });

            if (expected.Power.GameModeEnabled && !actual.Power.GameModeEnabled)
                items.Add(new OptimizationDriftItem { Id = "power_game_mode", Name = "Windows Game Mode",
                    Category = "Power", Explanation = "Windows Game Mode has been turned off. A Windows update or settings reset may have changed this." });

            if (expected.Power.ForegroundPriority && !actual.Power.ForegroundPriority)
                items.Add(new OptimizationDriftItem { Id = "power_foreground_priority", Name = "Foreground process priority",
                    Category = "Power", Explanation = "Foreground process priority boost registry key was changed. Windows Update sometimes resets this." });

            // Services
            if (expected.Services.TelemetryDisabled && !actual.Services.TelemetryDisabled)
                items.Add(new OptimizationDriftItem { Id = "service_telemetry", Name = "Windows telemetry service",
                    Category = "Services", Explanation = "DiagTrack/telemetry service was re-enabled, likely by a Windows Feature Update or cumulative update." });

            if (expected.Services.SysMainDisabled && !actual.Services.SysMainDisabled)
                items.Add(new OptimizationDriftItem { Id = "service_sysmain", Name = "SysMain (Superfetch)",
                    Category = "Services", Explanation = "SysMain (Superfetch) service was re-enabled. Windows Update commonly restores this service to its default running state." });

            if (expected.Services.SearchIndexingDisabled && !actual.Services.SearchIndexingDisabled)
                items.Add(new OptimizationDriftItem { Id = "service_search_indexing", Name = "Windows Search indexing",
                    Category = "Services", Explanation = "Windows Search indexing service was re-enabled. Windows Updates that affect the search stack often restart this service." });

            if (expected.Services.DiagTrackDisabled && !actual.Services.DiagTrackDisabled)
                items.Add(new OptimizationDriftItem { Id = "service_diagtrack", Name = "DiagTrack (Connected User Experiences)",
                    Category = "Services", Explanation = "DiagTrack was re-enabled. This service is sometimes restored by Windows Update or by policy enforcement on managed systems." });

            // Network
            if (expected.Network.TcpNoDelay && !actual.Network.TcpNoDelay)
                items.Add(new OptimizationDriftItem { Id = "network_tcp_nodelay", Name = "TCP no-delay (Nagle off)",
                    Category = "Network", Explanation = "TcpAckFrequency/TCPNoDelay registry value was removed or reset. A network adapter driver update or Windows Update may have reset TCP parameters." });

            if (expected.Network.TcpAckFrequency && !actual.Network.TcpAckFrequency)
                items.Add(new OptimizationDriftItem { Id = "network_tcp_ack_frequency", Name = "TCP ACK frequency tuning",
                    Category = "Network", Explanation = "TcpAckFrequency registry value was removed or reset. This can happen after adapter driver reinstall or Windows Update." });

            if (expected.Network.DeliveryOptimizationDisabled && !actual.Network.DeliveryOptimizationDisabled)
                items.Add(new OptimizationDriftItem { Id = "network_delivery_optimization", Name = "Delivery Optimization",
                    Category = "Network", Explanation = "Delivery Optimization was re-enabled. Windows Updates that include new optional features sometimes re-enable this service." });

            if (expected.Network.NagleDisabled && !actual.Network.NagleDisabled)
                items.Add(new OptimizationDriftItem { Id = "network_nagle", Name = "Nagle algorithm (additional disable)",
                    Category = "Network", Explanation = "Nagle algorithm re-enabled. A network stack reset or Windows Update may have cleared the per-adapter TcpNoDelay key." });

            // Input
            if (expected.Input.MouseAccelerationDisabled && !actual.Input.MouseAccelerationDisabled)
                items.Add(new OptimizationDriftItem { Id = "input_mouse_acceleration", Name = "Mouse pointer acceleration",
                    Category = "Input", Explanation = "Enhance Pointer Precision was re-enabled. This can happen after a Windows user profile sync, Windows Update to mouse driver, or if a peripheral tool reset it." });

            if (expected.Input.GameDvrDisabled && !actual.Input.GameDvrDisabled)
                items.Add(new OptimizationDriftItem { Id = "input_game_dvr", Name = "Game DVR background recording",
                    Category = "Input", Explanation = "Game DVR was re-enabled. Windows Feature Updates to the Xbox app or Gaming Services often restore Game DVR to the enabled state." });

            if (expected.Input.GameBarDisabled && !actual.Input.GameBarDisabled)
                items.Add(new OptimizationDriftItem { Id = "input_game_bar", Name = "Game Bar overlay",
                    Category = "Input", Explanation = "Game Bar overlay was re-enabled. Windows Gaming Services updates or Xbox Game Pass installs often re-enable Game Bar." });

            if (expected.Input.FullscreenOptimizationsDisabled && !actual.Input.FullscreenOptimizationsDisabled)
                items.Add(new OptimizationDriftItem { Id = "input_fullscreen_optimizations", Name = "Fullscreen optimizations",
                    Category = "Input", Explanation = "Fullscreen optimizations were re-enabled. A Windows Update or Graphics driver update may have reset the DisableFullscreenOptimizations registry key." });

            // Visual
            if (expected.Visual.AnimationsDisabled && !actual.Visual.AnimationsDisabled)
                items.Add(new OptimizationDriftItem { Id = "visual_animations", Name = "UI animations",
                    Category = "Visual", Explanation = "Windows UI animations were re-enabled. A Windows Update to Accessibility or DWM stack can reset animation preferences." });

            if (expected.Visual.TransparencyDisabled && !actual.Visual.TransparencyDisabled)
                items.Add(new OptimizationDriftItem { Id = "visual_transparency", Name = "Transparency effects",
                    Category = "Visual", Explanation = "Transparency effects were re-enabled. A Windows Personalization update or theme restore can reset transparency settings." });

            // Storage
            if (expected.Storage.TrimEnabled && !actual.Storage.TrimEnabled)
                items.Add(new OptimizationDriftItem { Id = "storage_trim", Name = "SSD TRIM",
                    Category = "Storage", Explanation = "SSD TRIM appears to have been disabled. This is uncommon unless a storage driver update changed the FSUTIL DisableDeleteNotify setting." });

            if (expected.Storage.DefragDisabled && !actual.Storage.DefragDisabled)
                items.Add(new OptimizationDriftItem { Id = "storage_defrag_disable", Name = "Scheduled defragmentation",
                    Category = "Storage", Explanation = "Scheduled defragmentation task was re-enabled. Windows Updates to the SSD detection stack or Defrag task sometimes restore its original schedule." });

            if (expected.Storage.ShortNamesDisabled && !actual.Storage.ShortNamesDisabled)
                items.Add(new OptimizationDriftItem { Id = "storage_short_names", Name = "8.3 short filename creation",
                    Category = "Storage", Explanation = "8.3 filename creation was re-enabled on one or more volumes. A CHKDSK run or storage stack reset can restore this to the default." });

            if (expected.Storage.LastAccessDisabled && !actual.Storage.LastAccessDisabled)
                items.Add(new OptimizationDriftItem { Id = "storage_last_access", Name = "NTFS last-access timestamp",
                    Category = "Storage", Explanation = "NTFS last-access timestamp updates were re-enabled. A Windows storage policy update or CHKDSK can restore the default NtfsDisableLastAccessUpdate value." });

            return new OptimizationDriftSummary
            {
                DriftedItems = items,
                CheckedUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Export a one-click diagnostic optimization report bundling current state, drift
        /// from expected state, and re-apply instructions suitable for GitHub support reports.
        /// Returns the export path, or null if the export failed.
        /// </summary>
        public async Task<string?> ExportOptimizationReportAsync(
            OptimizationState? expectedState = null,
            string? exportDirectory = null)
        {
            try
            {
                var currentState = await GetCurrentStateAsync();
                var dir = exportDirectory
                    ?? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OmenCore", "Reports");

                System.IO.Directory.CreateDirectory(dir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = System.IO.Path.Combine(dir, $"optimizer-report-{timestamp}.txt");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("OmenCore Optimization Report");
                sb.AppendLine($"Generated: {DateTime.Now:O}");
                sb.AppendLine(new string('=', 70));
                sb.AppendLine();

                sb.AppendLine("CURRENT STATE");
                sb.AppendLine(new string('-', 40));
                AppendStateSection(sb, "Power", new[]
                {
                    ("Ultimate Performance plan", currentState.Power.UltimatePerformancePlan),
                    ("Hardware GPU scheduling (HAGS)", currentState.Power.HardwareGpuScheduling),
                    ("Windows Game Mode", currentState.Power.GameModeEnabled),
                    ("Foreground process priority boost", currentState.Power.ForegroundPriority),
                });
                AppendStateSection(sb, "Services", new[]
                {
                    ("Telemetry disabled", currentState.Services.TelemetryDisabled),
                    ("SysMain (Superfetch) disabled", currentState.Services.SysMainDisabled),
                    ("Search indexing disabled", currentState.Services.SearchIndexingDisabled),
                    ("DiagTrack disabled", currentState.Services.DiagTrackDisabled),
                });
                AppendStateSection(sb, "Network", new[]
                {
                    ("TCP no-delay (Nagle off)", currentState.Network.TcpNoDelay),
                    ("TCP ACK frequency tuned", currentState.Network.TcpAckFrequency),
                    ("Delivery Optimization disabled", currentState.Network.DeliveryOptimizationDisabled),
                    ("Nagle (additional) disabled", currentState.Network.NagleDisabled),
                });
                AppendStateSection(sb, "Input", new[]
                {
                    ("Mouse acceleration disabled", currentState.Input.MouseAccelerationDisabled),
                    ("Game DVR disabled", currentState.Input.GameDvrDisabled),
                    ("Game Bar disabled", currentState.Input.GameBarDisabled),
                    ("Fullscreen optimizations disabled", currentState.Input.FullscreenOptimizationsDisabled),
                });
                AppendStateSection(sb, "Visual", new[]
                {
                    ("UI animations disabled", currentState.Visual.AnimationsDisabled),
                    ("Transparency effects disabled", currentState.Visual.TransparencyDisabled),
                });
                AppendStateSection(sb, "Storage", new[]
                {
                    ("SSD TRIM enabled", currentState.Storage.TrimEnabled),
                    ("Scheduled defragmentation disabled", currentState.Storage.DefragDisabled),
                    ("8.3 short filenames disabled", currentState.Storage.ShortNamesDisabled),
                    ("NTFS last-access timestamp disabled", currentState.Storage.LastAccessDisabled),
                });

                sb.AppendLine();
                sb.AppendLine($"ACTIVE OPTIMIZATIONS: {currentState.ActiveCount}/{currentState.TotalCount}");
                sb.AppendLine();

                if (expectedState != null)
                {
                    var drift = GetDriftExplanations(expectedState, currentState);
                    sb.AppendLine("DRIFT ANALYSIS");
                    sb.AppendLine(new string('-', 40));
                    if (drift.HasDrift)
                    {
                        foreach (var item in drift.DriftedItems)
                        {
                            sb.AppendLine($"[DRIFTED] {item.Name} ({item.Category})");
                            sb.AppendLine($"  {item.Explanation}");
                            sb.AppendLine($"  Suggestion: {item.Suggestion}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("No drift detected — all expected optimizations are active.");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("END OF REPORT");

                await System.IO.File.WriteAllTextAsync(path, sb.ToString());
                _logger.Info($"Optimization report exported: {path}");
                return path;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to export optimization report: {ex.Message}", ex);
                return null;
            }
        }

        private static void AppendStateSection(System.Text.StringBuilder sb, string section, (string label, bool active)[] items)
        {
            sb.AppendLine($"  [{section}]");
            foreach (var (label, active) in items)
            {
                sb.AppendLine($"    {(active ? "[x]" : "[ ]")} {label}");
            }
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
