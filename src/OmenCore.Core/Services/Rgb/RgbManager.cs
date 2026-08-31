using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// Unified RGB manager that coordinates all RGB providers for cross-brand sync.
    /// </summary>
    public class RgbManager
    {
        private readonly List<IRgbProvider> _providers = new();
        private readonly HashSet<string> _initializedProviderIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _initializeLock = new(1, 1);
        private readonly LoggingService? _logging;

        public RgbManager(LoggingService? logging = null)
        {
            _logging = logging;
        }
        
        public event EventHandler<RgbSyncEventArgs>? SyncCompleted;

        public IEnumerable<IRgbProvider> Providers => _providers;
        public RgbSyncEventArgs? LastSyncResult { get; private set; }
        
        /// <summary>
        /// Get all available providers (initialized and with devices).
        /// </summary>
        public IEnumerable<IRgbProvider> AvailableProviders => _providers.Where(p => p.IsAvailable);
        
        /// <summary>
        /// Get total number of connected devices across all providers.
        /// Excludes "system" provider to avoid circular reference.
        /// </summary>
        public int TotalDeviceCount => _providers.Where(p => p.ProviderId != "system").Sum(p => p.DeviceCount);
        
        /// <summary>
        /// Check if any provider is available.
        /// Excludes "system" provider to avoid circular reference.
        /// </summary>
        public bool HasAnyProvider => _providers.Any(p => p.IsAvailable && p.ProviderId != "system");

        public void RegisterProvider(IRgbProvider provider)
        {
            if (!_providers.Contains(provider)) 
            {
                _providers.Add(provider);
            }
        }
        
        public void UnregisterProvider(IRgbProvider provider)
        {
            _providers.Remove(provider);
        }
        
        public IRgbProvider? GetProvider(string providerId)
        {
            return _providers.FirstOrDefault(p => 
                string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }

        public async Task InitializeAllAsync()
        {
            await EnsureProvidersInitializedAsync();
        }

        private async Task EnsureProvidersInitializedAsync()
        {
            await _initializeLock.WaitAsync();
            try
            {
                var providersToInitialize = _providers
                    .Where(p => !_initializedProviderIds.Contains(p.ProviderId))
                    .ToList();

                if (providersToInitialize.Count == 0)
                {
                    return;
                }

                foreach (var provider in providersToInitialize.Where(p => p.ProviderId != "system"))
                {
                    await SafeInitializeAsync(provider);
                    _initializedProviderIds.Add(provider.ProviderId);
                }

                foreach (var provider in providersToInitialize.Where(p => p.ProviderId == "system"))
                {
                    await SafeInitializeAsync(provider);
                    _initializedProviderIds.Add(provider.ProviderId);
                }
            }
            finally
            {
                _initializeLock.Release();
            }
        }
        
        private async Task SafeInitializeAsync(IRgbProvider provider)
        {
            try
            {
                await provider.InitializeAsync();
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, "initialize", ex);
            }
        }

        /// <summary>
        /// Apply an effect string to all available providers using a two-phase prepare/commit
        /// so that providers finish staging before the first hardware write begins.
        /// </summary>
        public async Task ApplyEffectToAllAsync(string effectId)
        {
            await EnsureProvidersInitializedAsync();

            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system").ToList();

            if (TryResolveEffectType(effectId, out var effectType))
            {
                var unsupportedProviders = available
                    .Where(p => !p.SupportedEffects.Contains(effectType))
                    .Select(p => p.ProviderName)
                    .ToList();

                if (unsupportedProviders.Count > 0)
                {
                    _logging?.Info($"RGB effect '{effectType}' unsupported for provider(s): {string.Join(", ", unsupportedProviders)}; skipping unsupported endpoints");
                }

                available = available
                    .Where(p => p.SupportedEffects.Contains(effectType))
                    .ToList();
            }

            if (available.Count == 0)
            {
                _logging?.Warn($"RGB sync '{effectId}' skipped: no available providers support this effect");
                PublishSyncCompleted(new RgbSyncEventArgs(effectId, 0, 0, 0));
                return;
            }

            // Phase 1: prepare — providers may pre-serialise payloads or acquire handles
            var prepareResults = await Task.WhenAll(available.Select(async p => new
            {
                Provider = p,
                Prepared = await SafePrepareEffectAsync(p, effectId)
            }));
            var preparedProviders = prepareResults
                .Where(result => result.Prepared)
                .Select(result => result.Provider)
                .ToList();

            // Phase 2: commit — all providers begin their hardware/network write simultaneously
            var applyResults = await Task.WhenAll(preparedProviders.Select(p => SafeApplyEffectAsync(p, effectId)));
            var appliedByProvider = preparedProviders
                .Zip(applyResults, (provider, applied) => new { provider, applied })
                .ToDictionary(result => result.provider.ProviderId, result => result.applied, StringComparer.OrdinalIgnoreCase);
            var results = prepareResults
                .Select(result => result.Prepared && appliedByProvider.TryGetValue(result.Provider.ProviderId, out var applied) && applied)
                .ToList();
            
            PublishSyncCompleted(CreateSyncEvent(effectId, available.Count, results));
        }
        
        private async Task<bool> SafePrepareEffectAsync(IRgbProvider provider, string effectId)
        {
            try
            {
                await provider.PrepareEffectAsync(effectId);
                return true;
            }
            catch (Exception ex)
            {
                LogProviderDebug(provider, $"prepare '{effectId}'", ex);
                return false;
            }
        }
        
        private async Task<bool> SafeApplyEffectAsync(IRgbProvider provider, string effectId)
        {
            try
            {
                await provider.ApplyEffectAsync(effectId);
                return true;
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, $"apply '{effectId}'", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Sync a static color across all available providers.
        /// </summary>
        public async Task SyncStaticColorAsync(Color color)
        {
            await EnsureProvidersInitializedAsync();

            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Static)).ToList();

            var effectId = $"color:#{color.R:X2}{color.G:X2}{color.B:X2}";

            // Phase 1: prepare
            await Task.WhenAll(available.Select(p => SafePrepareEffectAsync(p, effectId)));

            // Phase 2: commit
            var results = await Task.WhenAll(available.Select(p => SafeSetStaticColorAsync(p, color)));
            
            PublishSyncCompleted(CreateSyncEvent(effectId, available.Count, results));
        }
        
        private async Task<bool> SafeSetStaticColorAsync(IRgbProvider provider, Color color)
        {
            try
            {
                await provider.SetStaticColorAsync(color);
                return true;
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, $"set static color #{color.R:X2}{color.G:X2}{color.B:X2}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Sync a breathing effect across all available providers.
        /// </summary>
        public async Task SyncBreathingEffectAsync(Color color)
        {
            await EnsureProvidersInitializedAsync();

            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Breathing)).ToList();
            var results = await Task.WhenAll(available.Select(p => SafeSetBreathingAsync(p, color)));
            
            PublishSyncCompleted(CreateSyncEvent("effect:breathing", available.Count, results));
        }
        
        private async Task<bool> SafeSetBreathingAsync(IRgbProvider provider, Color color)
        {
            try
            {
                await provider.SetBreathingEffectAsync(color);
                return true;
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, "set breathing effect", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Sync spectrum/rainbow effect across all available providers.
        /// </summary>
        public async Task SyncSpectrumEffectAsync()
        {
            await EnsureProvidersInitializedAsync();

            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Spectrum)).ToList();
            var results = await Task.WhenAll(available.Select(p => SafeSetSpectrumAsync(p)));
            
            PublishSyncCompleted(CreateSyncEvent("effect:spectrum", available.Count, results));
        }
        
        private async Task<bool> SafeSetSpectrumAsync(IRgbProvider provider)
        {
            try
            {
                await provider.SetSpectrumEffectAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, "set spectrum effect", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Turn off RGB on all providers.
        /// </summary>
        public async Task TurnOffAllAsync()
        {
            await EnsureProvidersInitializedAsync();

            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = GetAvailableProvidersForEffect(RgbEffectType.Off).ToList();
            var results = await Task.WhenAll(available.Select(p => SafeTurnOffAsync(p)));
            
            PublishSyncCompleted(CreateSyncEvent("off", available.Count, results));
        }
        
        private async Task<bool> SafeTurnOffAsync(IRgbProvider provider)
        {
            try
            {
                await provider.TurnOffAsync();
                return true;
            }
            catch (Exception ex)
            {
                LogProviderWarning(provider, "turn off RGB", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Get a summary of all providers and their status.
        /// </summary>
        public RgbSyncStatus GetStatus()
        {
            return new RgbSyncStatus
            {
                TotalProviders = _providers.Count,
                AvailableProviders = _providers.Count(p => p.IsAvailable),
                TotalDevices = TotalDeviceCount,
                ProviderStatuses = _providers.Select(p => new RgbProviderStatus
                {
                    ProviderId = p.ProviderId,
                    ProviderName = p.ProviderName,
                    IsAvailable = p.IsAvailable,
                    IsConnected = p.IsConnected,
                    DeviceCount = p.DeviceCount,
                    ConnectionStatus = p.ConnectionStatus,
                    StatusDetail = p.StatusDetail
                }).ToList()
            };
        }

        private RgbSyncEventArgs CreateSyncEvent(string effectId, int attempted, IReadOnlyCollection<bool> results)
        {
            var succeeded = results.Count(result => result);
            var failed = attempted - succeeded;

            if (failed > 0)
            {
                _logging?.Warn($"RGB sync '{effectId}' completed with {failed}/{attempted} provider failure(s)");
            }

            return new RgbSyncEventArgs(effectId, attempted, succeeded, failed);
        }

        private void PublishSyncCompleted(RgbSyncEventArgs args)
        {
            LastSyncResult = args;
            var handlers = SyncCompleted;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<RgbSyncEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    _logging?.Warn($"RGB sync subscriber failed for '{args.EffectId}': {ex.Message}");
                }
            }
        }

        private void LogProviderWarning(IRgbProvider provider, string action, Exception ex)
        {
            _logging?.Warn($"RGB provider '{provider.ProviderName}' failed to {action}: {ex.Message}");
        }

        private void LogProviderDebug(IRgbProvider provider, string action, Exception ex)
        {
            _logging?.Debug($"RGB provider '{provider.ProviderName}' failed to {action}: {ex.Message}");
        }

        private static bool TryResolveEffectType(string effectId, out RgbEffectType effectType)
        {
            effectType = RgbEffectType.Static;
            if (string.IsNullOrWhiteSpace(effectId))
            {
                return false;
            }

            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                effectType = RgbEffectType.Static;
                return true;
            }

            var normalized = effectId.Trim().ToLowerInvariant();
            if (normalized.StartsWith("breathing:", StringComparison.Ordinal))
            {
                effectType = RgbEffectType.Breathing;
                return true;
            }

            if (normalized.StartsWith("pulse:", StringComparison.Ordinal))
            {
                effectType = RgbEffectType.Breathing;
                return true;
            }

            switch (normalized)
            {
                case "effect:static":
                case "static":
                case "color":
                    effectType = RgbEffectType.Static;
                    return true;
                case "effect:breathing":
                case "breathing":
                    effectType = RgbEffectType.Breathing;
                    return true;
                case "effect:spectrum":
                case "effect:rainbow":
                case "spectrum":
                case "rainbow":
                    effectType = RgbEffectType.Spectrum;
                    return true;
                case "effect:wave":
                case "wave":
                    effectType = RgbEffectType.Wave;
                    return true;
                case "effect:off":
                case "off":
                    effectType = RgbEffectType.Off;
                    return true;
                default:
                    return false;
            }
        }

        private IEnumerable<IRgbProvider> GetAvailableProvidersForEffect(RgbEffectType effectType)
        {
            return _providers.Where(p =>
                p.IsAvailable &&
                p.ProviderId != "system" &&
                p.SupportedEffects.Contains(effectType));
        }
    }
    
    /// <summary>
    /// Event args for RGB sync completion.
    /// </summary>
    public class RgbSyncEventArgs : EventArgs
    {
        public string EffectId { get; }
        public int ProvidersAffected { get; }
        public int ProvidersSucceeded { get; }
        public int ProvidersFailed { get; }
        
        public RgbSyncEventArgs(string effectId, int providersAffected)
            : this(effectId, providersAffected, providersAffected, 0)
        {
        }

        public RgbSyncEventArgs(string effectId, int providersAffected, int providersSucceeded, int providersFailed)
        {
            EffectId = effectId;
            ProvidersAffected = providersAffected;
            ProvidersSucceeded = providersSucceeded;
            ProvidersFailed = providersFailed;
        }
    }
    
    /// <summary>
    /// Overall RGB sync status.
    /// </summary>
    public class RgbSyncStatus
    {
        public int TotalProviders { get; set; }
        public int AvailableProviders { get; set; }
        public int TotalDevices { get; set; }
        public List<RgbProviderStatus> ProviderStatuses { get; set; } = new();
    }
    
    /// <summary>
    /// Status of a single RGB provider.
    /// </summary>
    public class RgbProviderStatus
    {
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public bool IsConnected { get; set; }
        public int DeviceCount { get; set; }
        public RgbProviderConnectionStatus ConnectionStatus { get; set; }
        public string StatusDetail { get; set; } = string.Empty;
    }
}
