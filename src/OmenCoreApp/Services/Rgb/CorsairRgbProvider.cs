using System;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    public class CorsairRgbProvider : IRgbProvider
    {
        private readonly LoggingService _logging;
        private readonly ConfigurationService _configService;
        private CorsairDeviceService? _service;

        public string ProviderName => "Corsair";
        public bool IsAvailable { get; private set; } = false;

        public CorsairRgbProvider(LoggingService logging, ConfigurationService configService)
        {
            _logging = logging;
            _configService = configService;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _service = await CorsairDeviceService.CreateAsync(_logging, _configService);
                await _service.DiscoverAsync();
                IsAvailable = _service.Devices.Any();
                _logging.Info($"CorsairRgbProvider initialized, available={IsAvailable}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"CorsairRgbProvider init failed: {ex.Message}");
                IsAvailable = false;
            }
        }

        public async Task ApplyEffectAsync(string effectId)
        {
            if (!IsAvailable || _service == null)
                return;

            if (string.IsNullOrWhiteSpace(effectId))
                return;

            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = effectId["color:".Length..];
                await _service.ApplyLightingToAllAsync(hex);
                return;
            }

            if (effectId.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
            {
                var presetName = effectId["preset:".Length..];

                // Look up preset from the ConfigurationService
                var cfgPreset = _configService.Config.CorsairLightingPresets?.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                if (cfgPreset == null)
                {
                    _logging.Warn($"Corsair preset '{presetName}' not found in configuration");
                    return;
                }

                // Apply the named preset to each device
                foreach (var device in _service.Devices)
                {
                    try
                    {
                        await _service.ApplyLightingPresetAsync(device, cfgPreset);
                    }
                    catch (Exception ex)
                    {
                        _logging.Error($"Failed to apply corsair preset '{presetName}' to {device.Name}: {ex.Message}", ex);
                    }
                }

                _logging.Info($"Applied Corsair preset '{presetName}' to {_service.Devices.Count} device(s)");
            }
        }
    }
}