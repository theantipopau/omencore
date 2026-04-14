using System;
using System.Collections.Generic;
using System.Drawing;
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
        public string ProviderId => "razer";
        public bool IsAvailable { get; private set; } = false;
        public bool IsConnected => _razerService.IsSessionActive;
        public int DeviceCount => _razerService.Devices.Count;
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
                if (!IsAvailable) return "Razer Synapse not detected";
                if (DeviceCount == 0) return "Synapse running, no devices found";
                return $"{DeviceCount} device(s) connected";
            }
        }
        
        public IReadOnlyList<RgbEffectType> SupportedEffects { get; } = new[]
        {
            RgbEffectType.Static,
            RgbEffectType.Breathing,
            RgbEffectType.Spectrum,
            RgbEffectType.Wave,
            RgbEffectType.Reactive,
            RgbEffectType.Custom,
            RgbEffectType.Off
        };

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
                
                if (available)
                {
                    _razerService.DiscoverDevices();
                }
                
                _logging.Info($"RazerRgbProvider initialized, available={IsAvailable}, devices={DeviceCount}");
            }
            catch (Exception ex)
            {
                _logging.Warn($"RazerRgbProvider init failed: {ex.Message}");
                IsAvailable = false;
                _initFailed = true;
                _initError = ex.Message;
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
                var hex = effectId["color:".Length..];
                if (TryParseHexColor(hex, out var r, out var g, out var b))
                {
                    _razerService.SetStaticColor(r, g, b);
                }
                return Task.CompletedTask;
            }
            
            if (effectId.StartsWith("breathing:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = effectId["breathing:".Length..];
                if (TryParseHexColor(hex, out var r, out var g, out var b))
                {
                    _razerService.SetBreathingEffect(r, g, b);
                }
                return Task.CompletedTask;
            }
            
            if (effectId.Equals("effect:spectrum", StringComparison.OrdinalIgnoreCase))
            {
                _razerService.SetSpectrumEffect();
                return Task.CompletedTask;
            }
            
            if (effectId.Equals("effect:wave", StringComparison.OrdinalIgnoreCase))
            {
                _razerService.SetWaveEffect();
                return Task.CompletedTask;
            }
            
            if (effectId.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _razerService.SetStaticColor(0, 0, 0);
                return Task.CompletedTask;
            }

            _logging.Info($"Razer effect requested: {effectId}");
            return Task.CompletedTask;
        }
        
        public Task SetStaticColorAsync(Color color)
        {
            if (!IsAvailable)
                return Task.CompletedTask;
                
            _razerService.SetStaticColor(color.R, color.G, color.B);
            return Task.CompletedTask;
        }
        
        public Task SetBreathingEffectAsync(Color color)
        {
            if (!IsAvailable)
                return Task.CompletedTask;
                
            _razerService.SetBreathingEffect(color.R, color.G, color.B);
            return Task.CompletedTask;
        }
        
        public Task SetSpectrumEffectAsync()
        {
            if (!IsAvailable)
                return Task.CompletedTask;
                
            _razerService.SetSpectrumEffect();
            return Task.CompletedTask;
        }
        
        public Task TurnOffAsync()
        {
            if (!IsAvailable)
                return Task.CompletedTask;
                
            _razerService.SetStaticColor(0, 0, 0);
            return Task.CompletedTask;
        }
        
        private static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length < 7)
                return false;
                
            if (hex.StartsWith("#"))
                hex = hex[1..];
                
            return byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
                   byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
                   byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
        }
    }
}