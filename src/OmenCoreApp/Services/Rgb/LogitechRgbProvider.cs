using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;

namespace OmenCore.Services.Rgb
{
    public class LogitechRgbProvider : IRgbProvider
    {
        private readonly LoggingService _logging;
        private LogitechDeviceService? _service;

        // Lazy connection-health check: re-discover devices if last check was > 30 s ago
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private const int HealthCheckIntervalSeconds = 30;
        private bool _reconnectInProgress;

        public string ProviderName => "Logitech";
        public string ProviderId => "logitech";
        public bool IsAvailable { get; private set; } = false;
        public bool IsConnected => IsAvailable && (_service?.Devices.Count ?? 0) > 0;
        public int DeviceCount => _service?.Devices.Count ?? 0;
        private bool _initFailed;
        private string _initError = string.Empty;

        public RgbProviderConnectionStatus ConnectionStatus
        {
            get
            {
                if (_initFailed) return RgbProviderConnectionStatus.Error;
                if (!IsAvailable) return RgbProviderConnectionStatus.Disabled;
                if (DeviceCount == 0) return RgbProviderConnectionStatus.NoDevices;
                return RgbProviderConnectionStatus.Connected;
            }
        }

        public string StatusDetail
        {
            get
            {
                if (_initFailed) return _initError;
                if (!IsAvailable) return "Logitech G HUB not detected";
                if (DeviceCount == 0) return "G HUB running, no devices found";
                return $"{DeviceCount} device(s) connected";
            }
        }
        
        public IReadOnlyList<RgbEffectType> SupportedEffects { get; } = new[]
        {
            RgbEffectType.Static,
            RgbEffectType.Breathing,
            RgbEffectType.Spectrum,
            RgbEffectType.Off
        };

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
                _logging.Info($"LogitechRgbProvider initialized, available={IsAvailable}, devices={DeviceCount}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"LogitechRgbProvider init failed: {ex.Message}");
                IsAvailable = false;
                _initFailed = true;
                _initError = ex.Message;
            }
        }

        public async Task ApplyEffectAsync(string effectId)
        {
            if (!await EnsureConnectionAsync() || _service == null)
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
        
        public async Task SetStaticColorAsync(Color color)
        {
            if (!await EnsureConnectionAsync() || _service == null)
                return;
                
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            foreach (var dev in _service.Devices)
            {
                await _service.ApplyStaticColorAsync(dev, hex, 100);
            }
        }
        
        public async Task SetBreathingEffectAsync(Color color)
        {
            if (!await EnsureConnectionAsync() || _service == null)
                return;
                
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            foreach (var dev in _service.Devices)
            {
                await _service.ApplyBreathingEffectAsync(dev, hex, 2);
            }
        }
        
        public async Task SetSpectrumEffectAsync()
        {
            if (!await EnsureConnectionAsync() || _service == null)
                return;
                
            foreach (var dev in _service.Devices)
            {
                await _service.ApplySpectrumEffectAsync(dev, 5); // Medium speed
            }
        }
        
        public async Task TurnOffAsync()
        {
            if (!await EnsureConnectionAsync() || _service == null)
                return;
                
            foreach (var dev in _service.Devices)
            {
                await _service.ApplyStaticColorAsync(dev, "#000000", 0);
            }
        }

        /// <summary>
        /// Checks whether the Logitech service still has live devices.
        /// If the device list is empty (G HUB may have restarted), attempts one re-initialization.
        /// Rate-limited to once every <see cref="HealthCheckIntervalSeconds"/> seconds.
        /// </summary>
        private async Task<bool> EnsureConnectionAsync()
        {
            if (!IsAvailable || _service == null)
                return false;

            var now = DateTime.UtcNow;
            if (_reconnectInProgress ||
                (now - _lastHealthCheck).TotalSeconds < HealthCheckIntervalSeconds)
                return IsAvailable && DeviceCount > 0;

            _lastHealthCheck = now;

            // Re-discover; if all devices disappeared, try a full re-init
            await _service.DiscoverAsync();

            if (_service.Devices.Count == 0)
            {
                _logging.Warn("LogitechRgbProvider: all devices lost — attempting service reconnect");
                _reconnectInProgress = true;
                try
                {
                    await InitializeAsync();
                }
                finally
                {
                    _reconnectInProgress = false;
                }
            }

            return IsAvailable && DeviceCount > 0;
        }
    }
}