using System;
using System.Threading.Tasks;
using OmenCore.Razer;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    public class RazerRgbProvider : IRgbProvider
    {
        private readonly LoggingService _logging;
        private readonly RazerService _razerService;

        public string ProviderName => "Razer";
        public bool IsAvailable { get; private set; } = false;

        public RazerRgbProvider(LoggingService logging, RazerService razerService)
        {
            _logging = logging;
            _razerService = razerService;
        }

        public Task InitializeAsync()
        {
            try
            {
                var available = _razerService.Initialize();
                IsAvailable = available;
                _logging.Info($"RazerRgbProvider initialized, available={IsAvailable}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"RazerRgbProvider init failed: {ex.Message}");
                IsAvailable = false;
            }

            return Task.CompletedTask;
        }

        public Task ApplyEffectAsync(string effectId)
        {
            if (!IsAvailable)
                return Task.CompletedTask;

            if (string.IsNullOrWhiteSpace(effectId))
                return Task.CompletedTask;

            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = effectId.Substring("color:".Length);
                // parse hex to RGB
                if (byte.TryParse(hex.Substring(1,2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                    byte.TryParse(hex.Substring(3,2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                    byte.TryParse(hex.Substring(5,2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    _razerService.SetStaticColor(r, g, b);
                }
                return Task.CompletedTask;
            }

            _logging.Info($"Razer effect requested: {effectId}");
            return Task.CompletedTask;
        }
    }
}