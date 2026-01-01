using System;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    public class LogitechRgbProvider : IRgbProvider
    {
        private readonly LoggingService _logging;
        private LogitechDeviceService? _service;

        public string ProviderName => "Logitech";
        public bool IsAvailable { get; private set; } = false;

        public LogitechRgbProvider(LoggingService logging)
        {
            _logging = logging;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _service = await LogitechDeviceService.CreateAsync(_logging);
                await _service.DiscoverAsync();
                IsAvailable = _service.Devices.Any();
                _logging.Info($"LogitechRgbProvider initialized, available={IsAvailable}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"LogitechRgbProvider init failed: {ex.Message}");
                IsAvailable = false;
            }
        }

        public async Task ApplyEffectAsync(string effectId)
        {
            if (!IsAvailable || _service == null)
                return;

            if (string.IsNullOrWhiteSpace(effectId))
                return;

            // color:#RRGGBB or color:#RRGGBB@<brightness>
            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = effectId["color:".Length..];
                var hex = payload;
                var brightness = 100;

                if (payload.Contains("@"))
                {
                    var parts = payload.Split('@', 2);
                    hex = parts[0];
                    if (int.TryParse(parts[1], out var b)) brightness = Math.Clamp(b, 0, 100);
                }

                foreach (var dev in _service.Devices)
                {
                    await _service.ApplyStaticColorAsync(dev, hex, brightness);
                }

                return;
            }

            // breathing:#RRGGBB or breathing:#RRGGBB@<speed>
            if (effectId.StartsWith("breathing:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = effectId["breathing:".Length..];
                var hex = payload;
                var speed = 2; // default breathing speed

                if (payload.Contains("@"))
                {
                    var parts = payload.Split('@', 2);
                    hex = parts[0];
                    if (int.TryParse(parts[1], out var s)) speed = Math.Max(0, s);
                }

                foreach (var dev in _service.Devices)
                {
                    await _service.ApplyBreathingEffectAsync(dev, hex, speed);
                }

                return;
            }

            _logging.Info($"Logitech effect requested: {effectId}");
        }
    }
}