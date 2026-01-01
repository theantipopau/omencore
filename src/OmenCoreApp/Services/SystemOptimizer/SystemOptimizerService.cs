using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
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
        
        // Individual optimizers
        private readonly PowerOptimizer _powerOptimizer;
        private readonly ServiceOptimizer _serviceOptimizer;
        private readonly NetworkOptimizer _networkOptimizer;
        private readonly InputOptimizer _inputOptimizer;
        private readonly VisualEffectsOptimizer _visualOptimizer;
        private readonly StorageOptimizer _storageOptimizer;

        public event Action<string>? StatusChanged;
        public event Action<OptimizationResult>? OptimizationCompleted;

        public SystemOptimizerService(LoggingService logger)
        {
            _logger = logger;
            _backupService = new RegistryBackupService(logger);
            _ = new OptimizationVerifier(logger);
            
            // Initialize all optimizers
            _powerOptimizer = new PowerOptimizer(logger, _backupService);
            _serviceOptimizer = new ServiceOptimizer(logger, _backupService);
            _networkOptimizer = new NetworkOptimizer(logger, _backupService);
            _inputOptimizer = new InputOptimizer(logger, _backupService);
            _visualOptimizer = new VisualEffectsOptimizer(logger, _backupService);
            _storageOptimizer = new StorageOptimizer(logger, _backupService);
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
        public async Task<List<OptimizationResult>> RevertAllAsync()
        {
            var results = new List<OptimizationResult>();
            
            _logger.Info("Reverting all optimizations to defaults...");
            StatusChanged?.Invoke("Reverting optimizations...");
            
            results.AddRange(await _powerOptimizer.RevertAllAsync());
            results.AddRange(await _serviceOptimizer.RevertAllAsync());
            results.AddRange(await _networkOptimizer.RevertAllAsync());
            results.AddRange(await _inputOptimizer.RevertAllAsync());
            results.AddRange(await _visualOptimizer.RevertAllAsync());
            results.AddRange(await _storageOptimizer.RevertAllAsync());
            
            var successCount = results.FindAll(r => r.Success).Count;
            _logger.Info($"Revert complete: {successCount}/{results.Count} settings restored");
            StatusChanged?.Invoke($"Revert complete: {successCount}/{results.Count} settings restored");
            
            return results;
        }

        /// <summary>
        /// Applies a single optimization by ID.
        /// </summary>
        public async Task<OptimizationResult> ApplyOptimizationAsync(string optimizationId)
        {
            var result = new OptimizationResult { Id = optimizationId };
            
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
    }
}
