using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// Unified RGB manager that coordinates all RGB providers for cross-brand sync.
    /// </summary>
    public class RgbManager
    {
        private readonly List<IRgbProvider> _providers = new();
        
        public event EventHandler<RgbSyncEventArgs>? SyncCompleted;

        public IEnumerable<IRgbProvider> Providers => _providers;
        
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
            var tasks = _providers.Select(p => SafeInitializeAsync(p));
            await Task.WhenAll(tasks);
        }
        
        private async Task SafeInitializeAsync(IRgbProvider provider)
        {
            try
            {
                await provider.InitializeAsync();
            }
            catch
            {
                // Silently ignore initialization failures
            }
        }

        /// <summary>
        /// Apply an effect string to all available providers using a two-phase prepare/commit
        /// so that providers finish staging before the first hardware write begins.
        /// </summary>
        public async Task ApplyEffectToAllAsync(string effectId)
        {
            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system").ToList();

            // Phase 1: prepare — providers may pre-serialise payloads or acquire handles
            await Task.WhenAll(available.Select(p => SafePrepareEffectAsync(p, effectId)));

            // Phase 2: commit — all providers begin their hardware/network write simultaneously
            await Task.WhenAll(available.Select(p => SafeApplyEffectAsync(p, effectId)));
            
            SyncCompleted?.Invoke(this, new RgbSyncEventArgs(effectId, available.Count));
        }
        
        private async Task SafePrepareEffectAsync(IRgbProvider provider, string effectId)
        {
            try
            {
                await provider.PrepareEffectAsync(effectId);
            }
            catch { }
        }
        
        private async Task SafeApplyEffectAsync(IRgbProvider provider, string effectId)
        {
            try
            {
                await provider.ApplyEffectAsync(effectId);
            }
            catch
            {
                // Silently ignore apply failures
            }
        }
        
        /// <summary>
        /// Sync a static color across all available providers.
        /// </summary>
        public async Task SyncStaticColorAsync(Color color)
        {
            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Static)).ToList();

            var effectId = $"color:#{color.R:X2}{color.G:X2}{color.B:X2}";

            // Phase 1: prepare
            await Task.WhenAll(available.Select(p => SafePrepareEffectAsync(p, effectId)));

            // Phase 2: commit
            var tasks = available.Select(p => SafeSetStaticColorAsync(p, color));
            await Task.WhenAll(tasks);
            
            SyncCompleted?.Invoke(this, new RgbSyncEventArgs(effectId, available.Count));
        }
        
        private async Task SafeSetStaticColorAsync(IRgbProvider provider, Color color)
        {
            try
            {
                await provider.SetStaticColorAsync(color);
            }
            catch
            {
                // Silently ignore apply failures
            }
        }
        
        /// <summary>
        /// Sync a breathing effect across all available providers.
        /// </summary>
        public async Task SyncBreathingEffectAsync(Color color)
        {
            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Breathing));
            var tasks = available.Select(p => SafeSetBreathingAsync(p, color));
            await Task.WhenAll(tasks);
            
            SyncCompleted?.Invoke(this, new RgbSyncEventArgs("effect:breathing", available.Count()));
        }
        
        private async Task SafeSetBreathingAsync(IRgbProvider provider, Color color)
        {
            try
            {
                await provider.SetBreathingEffectAsync(color);
            }
            catch { }
        }
        
        /// <summary>
        /// Sync spectrum/rainbow effect across all available providers.
        /// </summary>
        public async Task SyncSpectrumEffectAsync()
        {
            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system" && p.SupportedEffects.Contains(RgbEffectType.Spectrum));
            var tasks = available.Select(p => SafeSetSpectrumAsync(p));
            await Task.WhenAll(tasks);
            
            SyncCompleted?.Invoke(this, new RgbSyncEventArgs("effect:spectrum", available.Count()));
        }
        
        private async Task SafeSetSpectrumAsync(IRgbProvider provider)
        {
            try
            {
                await provider.SetSpectrumEffectAsync();
            }
            catch { }
        }
        
        /// <summary>
        /// Turn off RGB on all providers.
        /// </summary>
        public async Task TurnOffAllAsync()
        {
            // Exclude "system" provider to avoid infinite recursion (it delegates back to this manager)
            var available = _providers.Where(p => p.IsAvailable && p.ProviderId != "system");
            var tasks = available.Select(p => SafeTurnOffAsync(p));
            await Task.WhenAll(tasks);
            
            SyncCompleted?.Invoke(this, new RgbSyncEventArgs("off", available.Count()));
        }
        
        private async Task SafeTurnOffAsync(IRgbProvider provider)
        {
            try
            {
                await provider.TurnOffAsync();
            }
            catch { }
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
                    DeviceCount = p.DeviceCount
                }).ToList()
            };
        }
    }
    
    /// <summary>
    /// Event args for RGB sync completion.
    /// </summary>
    public class RgbSyncEventArgs : EventArgs
    {
        public string EffectId { get; }
        public int ProvidersAffected { get; }
        
        public RgbSyncEventArgs(string effectId, int providersAffected)
        {
            EffectId = effectId;
            ProvidersAffected = providersAffected;
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
    }
}