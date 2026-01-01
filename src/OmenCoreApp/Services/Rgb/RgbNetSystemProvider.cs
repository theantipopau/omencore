using System;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Services;
using RGB.NET.Core;

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

        public string ProviderName => "RgbNetSystem";
        public bool IsAvailable { get; private set; } = false;

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

                    // Attempt to set each device's buffer to the requested color
                    foreach (var dev in _surface.Devices)
                    {
                        try
                        {
                            foreach (var led in dev)
                            {
                                led.Color = new RGB.NET.Core.Color(r, g, b);
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
                        _logging.Info($"RgbNetSystemProvider applied color #{hex} to {_surface.Devices.Count()} devices");
                    }
                    catch (Exception uex)
                    {
                        _logging.Warn($"RgbNetSystemProvider surface update failed: {uex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Failed to parse/apply color '{hex}': {ex.Message}");
                }
            }

            return Task.CompletedTask;
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