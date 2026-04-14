using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;
using RGB.NET.Core;
using DrawingColor = System.Drawing.Color;

namespace OmenCore.Services.Rgb
{
    /// <summary>
    /// Experimental provider that uses RGB.NET Core to control any supported desktop RGB devices.
    /// This is a spike-style implementation: it initializes RGB.NET and will attempt to set a static color
    /// across all devices when asked to apply a "color:#RRGGBB" effect.
    /// </summary>
    public class RgbNetSystemProvider : IRgbProvider, IDisposable
    {
        private readonly LoggingService _logging;
        private RGBSurface? _surface;

        public string ProviderName => "RGB.NET";
        public string ProviderId => "rgbnet";
        public bool IsAvailable { get; private set; } = false;
        public bool IsConnected => IsAvailable && (_surface?.Devices.Count() ?? 0) > 0;
        public int DeviceCount => _surface?.Devices.Count() ?? 0;
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
                if (!IsAvailable) return "No RGB.NET-compatible devices found";
                if (DeviceCount == 0) return "Surface created, no devices enumerated";
                return $"{DeviceCount} device(s) managed by RGB.NET";
            }
        }
        
        public IReadOnlyList<RgbEffectType> SupportedEffects { get; } = new[]
        {
            RgbEffectType.Static,
            RgbEffectType.Off
        };

        public RgbNetSystemProvider(LoggingService logging)
        {
            _logging = logging;
        }

        public Task InitializeAsync()
        {
            try
            {
                _surface = new RGBSurface();
                _logging.Info("RGB.NET surface created for system provider");

                // Load common providers implicitly by enumerating (providers are loaded by NuGet packages)
                // Give short time for device enumeration - non-blocking
                Task.Delay(250).Wait();

                var count = _surface.Devices.Count();
                IsAvailable = count > 0;
                _logging.Info($"RgbNetSystemProvider initialized - Found {count} device(s)");
            }
            catch (Exception ex)
            {
                _logging.Warn($"RgbNetSystemProvider initialization failed: {ex.Message}");
                IsAvailable = false;
                _initFailed = true;
                _initError = ex.Message;
            }

            return Task.CompletedTask;
        }

        public Task ApplyEffectAsync(string effectId)
        {
            if (!IsAvailable || _surface == null)
                return Task.CompletedTask;

            if (string.IsNullOrWhiteSpace(effectId))
                return Task.CompletedTask;

            if (effectId.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = effectId["color:".Length..];
                if (hex.StartsWith("#")) hex = hex[1..];
                if (hex.Length != 6)
                {
                    _logging.Warn($"Invalid color hex '{hex}'");
                    return Task.CompletedTask;
                }

                try
                {
                    var r = Convert.ToByte(hex.Substring(0, 2), 16);
                    var g = Convert.ToByte(hex.Substring(2, 2), 16);
                    var b = Convert.ToByte(hex.Substring(4, 2), 16);

                    ApplyColorToAllDevices(r, g, b);
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to parse/apply color '{hex}': {ex.Message}");
                }
            }
            
            if (effectId.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                ApplyColorToAllDevices(0, 0, 0);
            }

            return Task.CompletedTask;
        }
        
        public Task SetStaticColorAsync(DrawingColor color)
        {
            if (!IsAvailable || _surface == null)
                return Task.CompletedTask;
                
            ApplyColorToAllDevices(color.R, color.G, color.B);
            return Task.CompletedTask;
        }
        
        public Task SetBreathingEffectAsync(DrawingColor color)
        {
            // RGB.NET doesn't natively support effects, just set static
            return SetStaticColorAsync(color);
        }
        
        public Task SetSpectrumEffectAsync()
        {
            // RGB.NET doesn't natively support effects
            return Task.CompletedTask;
        }
        
        public Task TurnOffAsync()
        {
            if (!IsAvailable || _surface == null)
                return Task.CompletedTask;
                
            ApplyColorToAllDevices(0, 0, 0);
            return Task.CompletedTask;
        }
        
        private void ApplyColorToAllDevices(byte r, byte g, byte b)
        {
            if (_surface == null)
                return;
                
            // Attempt to set each device's buffer to the requested color
            foreach (var dev in _surface.Devices)
            {
                try
                {
                    foreach (var led in dev)
                    {
                        led.Color = new Color(r, g, b);
                    }
                }
                catch (Exception dex)
                {
                    _logging.Warn($"Failed to set color on device {dev.DeviceInfo.Model}: {dex.Message}");
                }
            }

            // Flush to devices
            try
            {
                _surface.Update();
                _logging.Info($"RgbNetSystemProvider applied color R={r},G={g},B={b} to {_surface.Devices.Count()} devices");
            }
            catch (Exception uex)
            {
                _logging.Warn($"RgbNetSystemProvider surface update failed: {uex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                (_surface as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                _logging.Warn($"Failed to dispose RGB surface: {ex.Message}");
            }

            _surface = null;
        }
    }
}