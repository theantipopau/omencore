using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// Composite provider that applies effects to all available providers registered with the RgbManager.
    /// Useful as a "generic" entrypoint for controlling desktop RGB broadly.
    /// </summary>
    public class SystemRgbProvider : IRgbProvider
    {
        private readonly RgbManager _manager;
        private readonly LoggingService _logging;

        public string ProviderName => "System";
        public string ProviderId => "system";
        public bool IsAvailable { get; private set; } = false;
        public bool IsConnected => _manager.HasAnyProvider;
        public int DeviceCount => _manager.TotalDeviceCount;

        public RgbProviderConnectionStatus ConnectionStatus
        {
            get
            {
                if (!IsAvailable) return RgbProviderConnectionStatus.Disabled;
                if (DeviceCount == 0) return RgbProviderConnectionStatus.NoDevices;
                return RgbProviderConnectionStatus.Connected;
            }
        }

        public string StatusDetail
        {
            get
            {
                if (!IsAvailable) return "No RGB providers available";
                if (DeviceCount == 0) return "Providers ready, no devices found";
                return $"{DeviceCount} total device(s) across all providers";
            }
        }
        
        public IReadOnlyList<RgbEffectType> SupportedEffects { get; } = new[]
        {
            RgbEffectType.Static,
            RgbEffectType.Breathing,
            RgbEffectType.Spectrum,
            RgbEffectType.Off
        };

        public SystemRgbProvider(RgbManager manager, LoggingService logging)
        {
            _manager = manager;
            _logging = logging;
        }

        public Task InitializeAsync()
        {
            // Consider available if any registered provider reports availability
            IsAvailable = _manager.Providers.Any(p => p.IsAvailable);
            _logging.Info($"SystemRgbProvider initialized, available={IsAvailable}, total devices={DeviceCount}");
            return Task.CompletedTask;
        }

        public Task ApplyEffectAsync(string effectId)
        {
            // Apply to all providers via RgbManager
            return _manager.ApplyEffectToAllAsync(effectId);
        }
        
        public Task SetStaticColorAsync(Color color)
        {
            return _manager.SyncStaticColorAsync(color);
        }
        
        public Task SetBreathingEffectAsync(Color color)
        {
            return _manager.SyncBreathingEffectAsync(color);
        }
        
        public Task SetSpectrumEffectAsync()
        {
            return _manager.SyncSpectrumEffectAsync();
        }
        
        public Task TurnOffAsync()
        {
            return _manager.TurnOffAllAsync();
        }
    }
}