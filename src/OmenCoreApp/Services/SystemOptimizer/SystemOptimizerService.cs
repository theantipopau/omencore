using System;
using System.Collections.Generic;
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
