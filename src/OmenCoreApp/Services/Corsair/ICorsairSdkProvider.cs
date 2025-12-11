using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Corsair;
using OmenCore.Models;
using RGB.NET.Core;
using RGB.NET.Devices.Corsair;

namespace OmenCore.Services.Corsair
{
    /// <summary>
    /// Interface for Corsair device SDK providers.
    /// Allows swapping between stub, iCUE SDK, or custom implementations.
    /// </summary>
    public interface ICorsairSdkProvider
    {
        /// <summary>
        /// Initialize the Corsair SDK connection.
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// Enumerate all connected Corsair devices.
        /// </summary>
        Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync();

        /// <summary>
        /// Apply RGB lighting effect to a device.
        /// </summary>
        Task ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset);

        /// <summary>
        /// Configure DPI stages for a mouse.
        /// </summary>
        Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages);

        /// <summary>
        /// Upload and apply a macro profile to a device.
        /// </summary>
        Task ApplyMacroAsync(CorsairDevice device, MacroProfile macro);

        /// <summary>
        /// Sync device lighting with a laptop theme.
        /// </summary>
        Task SyncWithThemeAsync(IEnumerable<CorsairDevice> devices, LightingProfile theme);

        /// <summary>
        /// Read current device status (battery, polling rate, etc.).
        /// </summary>
        Task<CorsairDeviceStatus> GetDeviceStatusAsync(CorsairDevice device);

        /// <summary>
        /// Release SDK resources.
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// Stub implementation for testing without Corsair SDK.
    /// </summary>
    public class CorsairSdkStub : ICorsairSdkProvider
    {
        private readonly LoggingService _logging;
#pragma warning disable CS0414 // Field assigned but never used - stub implementation
        private bool _initialized = false;
#pragma warning restore CS0414

        public CorsairSdkStub(LoggingService logging)
        {
            _logging = logging;
        }

        public Task<bool> InitializeAsync()
        {
            _logging.Info("Corsair SDK Stub initialized");
            _initialized = true;
            return Task.FromResult(true);
        }

        public Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync()
        {
            var devices = new List<CorsairDevice>
            {
                new CorsairDevice
                {
                    DeviceId = Guid.NewGuid().ToString(),
                    Name = "K95 RGB Platinum (Stub)",
                    DeviceType = OmenCore.Corsair.CorsairDeviceType.Keyboard,
                    Zones = new List<string> { "Keys", "Media", "Logo" },
                    Status = new CorsairDeviceStatus
                    {
                        BatteryPercent = 100,
                        PollingRateHz = 1000,
                        FirmwareVersion = "5.6.0",
                        ConnectionType = "USB"
                    },
                    DpiStages = new List<CorsairDpiStage>()
                },
                new CorsairDevice
                {
                    DeviceId = Guid.NewGuid().ToString(),
                    Name = "Dark Core RGB Pro (Stub)",
                    DeviceType = OmenCore.Corsair.CorsairDeviceType.Mouse,
                    Zones = new List<string> { "Logo", "Scroll", "Underglow" },
                    Status = new CorsairDeviceStatus
                    {
                        BatteryPercent = 85,
                        PollingRateHz = 2000,
                        FirmwareVersion = "3.0.12",
                        ConnectionType = "2.4GHz Wireless"
                    },
                    DpiStages = new List<CorsairDpiStage>
                    {
                        new CorsairDpiStage { Name = "Sniper", Dpi = 800, IsDefault = false, LiftOffDistanceMm = 1.0 },
                        new CorsairDpiStage { Name = "Default", Dpi = 1600, IsDefault = true, LiftOffDistanceMm = 1.5 },
                        new CorsairDpiStage { Name = "High", Dpi = 3200, IsDefault = false, LiftOffDistanceMm = 2.0 }
                    }
                }
            };

            return Task.FromResult<IEnumerable<CorsairDevice>>(devices);
        }

        public Task ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset)
        {
            _logging.Info($"[Stub] Applied lighting preset '{preset.Name}' to {device.Name}");
            return Task.CompletedTask;
        }

        public Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
        {
            _logging.Info($"[Stub] Updated DPI stages for {device.Name}");
            return Task.CompletedTask;
        }

        public Task ApplyMacroAsync(CorsairDevice device, MacroProfile macro)
        {
            _logging.Info($"[Stub] Applied macro '{macro.Name}' to {device.Name}");
            return Task.CompletedTask;
        }

        public Task SyncWithThemeAsync(IEnumerable<CorsairDevice> devices, LightingProfile theme)
        {
            foreach (var device in devices)
            {
                _logging.Info($"[Stub] Syncing {device.Name} with theme '{theme.Name}'");
            }
            return Task.CompletedTask;
        }

        public Task<CorsairDeviceStatus> GetDeviceStatusAsync(CorsairDevice device)
        {
            return Task.FromResult(device.Status);
        }

        public void Shutdown()
        {
            _logging.Info("Corsair SDK Stub shut down");
            _initialized = false;
        }
    }

    /// <summary>
    /// Real iCUE SDK implementation using RGB.NET library.
    /// </summary>
    public class CorsairICueSdk : ICorsairSdkProvider
    {
        private readonly LoggingService _logging;
        private readonly RGBSurface _surface;
        private readonly CorsairDeviceProvider _provider;
        private bool _initialized;

        public CorsairICueSdk(LoggingService logging)
        {
            _logging = logging;
            _surface = new RGBSurface();
            _provider = new CorsairDeviceProvider();
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Initialize RGB.NET Corsair device provider
                _surface.Load(_provider, throwExceptions: false);
                
                if (_surface.Devices.Any())
                {
                    _logging.Info($"Corsair iCUE SDK initialized successfully - {_surface.Devices.Count()} device(s) found");
                    _initialized = true;
                    return await Task.FromResult(true);
                }
                else
                {
                    _logging.Warn("Corsair iCUE SDK initialized but no devices found");
                    return await Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                _logging.Error("iCUE SDK initialization failed", ex);
                return false;
            }
        }

        public async Task<IEnumerable<CorsairDevice>> DiscoverDevicesAsync()
        {
            var devices = new List<CorsairDevice>();

            if (!_initialized)
                return await Task.FromResult<IEnumerable<CorsairDevice>>(devices);

            foreach (var rgbDevice in _surface.Devices)
            {
                var device = new CorsairDevice
                {
                    DeviceId = rgbDevice.GetHashCode().ToString(), // Use hash as unique ID
                    Name = rgbDevice.DeviceInfo.Model,
                    DeviceType = MapDeviceType(rgbDevice.DeviceInfo.DeviceType),
                    Zones = new List<string> { "RGB" }, // RGB.NET handles LED zones internally
                    Status = new CorsairDeviceStatus
                    {
                        BatteryPercent = 100, // RGB.NET doesn't expose battery info
                        PollingRateHz = 1000,
                        FirmwareVersion = "Unknown",
                        ConnectionType = "USB"
                    },
                    DpiStages = new List<CorsairDpiStage>()
                };

                // Add mouse-specific DPI stages if it's a mouse
                if (device.DeviceType == OmenCore.Corsair.CorsairDeviceType.Mouse)
                {
                    device.DpiStages = new List<CorsairDpiStage>
                    {
                        new CorsairDpiStage { Name = "Low", Dpi = 800, IsDefault = false, LiftOffDistanceMm = 1.0 },
                        new CorsairDpiStage { Name = "Medium", Dpi = 1600, IsDefault = true, LiftOffDistanceMm = 1.5 },
                        new CorsairDpiStage { Name = "High", Dpi = 3200, IsDefault = false, LiftOffDistanceMm = 2.0 }
                    };
                }

                devices.Add(device);
            }

            return await Task.FromResult<IEnumerable<CorsairDevice>>(devices);
        }

        private OmenCore.Corsair.CorsairDeviceType MapDeviceType(RGBDeviceType rgbType)
        {
            return rgbType switch
            {
                RGBDeviceType.Keyboard => OmenCore.Corsair.CorsairDeviceType.Keyboard,
                RGBDeviceType.Mouse => OmenCore.Corsair.CorsairDeviceType.Mouse,
                RGBDeviceType.Headset => OmenCore.Corsair.CorsairDeviceType.Headset,
                RGBDeviceType.Mousepad => OmenCore.Corsair.CorsairDeviceType.MouseMat,
                _ => OmenCore.Corsair.CorsairDeviceType.Accessory
            };
        }

        public async Task ApplyLightingAsync(CorsairDevice device, CorsairLightingPreset preset)
        {
            if (!_initialized)
                return;

            try
            {
                // Find the RGB.NET device
                var rgbDevice = _surface.Devices.FirstOrDefault(d => d.GetHashCode().ToString() == device.DeviceId);
                if (rgbDevice == null)
                {
                    _logging.Warn($"Device {device.Name} not found in RGB surface");
                    return;
                }

                // Parse hex color string
                var hex = preset.PrimaryColor.TrimStart('#');
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                var color = new Color(r, g, b);
                
                // Apply color to all LEDs
                foreach (var led in rgbDevice)
                {
                    led.Color = color;
                }

                _surface.Update();
                _logging.Info($"Applied lighting preset '{preset.Name}' to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply lighting to {device.Name}", ex);
            }

            await Task.CompletedTask;
        }

        public async Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
        {
            // NOTE: RGB.NET is primarily for lighting control, not device configuration
            // DPI settings require direct Corsair SDK which is not available in RGB.NET
            _logging.Warn($"DPI configuration not supported via RGB.NET for {device.Name}");
            await Task.CompletedTask;
        }

        public async Task ApplyMacroAsync(CorsairDevice device, MacroProfile macro)
        {
            // NOTE: RGB.NET is primarily for lighting control, not macro programming
            // Macro upload requires direct Corsair SDK which is not available in RGB.NET
            _logging.Warn($"Macro upload not supported via RGB.NET for {device.Name}");
            await Task.CompletedTask;
        }

        public async Task SyncWithThemeAsync(IEnumerable<CorsairDevice> devices, LightingProfile theme)
        {
            if (!_initialized)
                return;

            try
            {
                // Parse theme color
                var hex = theme.PrimaryColor.TrimStart('#');
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                var color = new Color(r, g, b);

                // Apply to all devices
                foreach (var rgbDevice in _surface.Devices)
                {
                    foreach (var led in rgbDevice)
                    {
                        led.Color = color;
                    }
                }

                _surface.Update();
                _logging.Info($"Synced {devices.Count()} device(s) with theme '{theme.Name}'");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to sync devices with theme", ex);
            }

            await Task.CompletedTask;
        }

        public async Task<CorsairDeviceStatus> GetDeviceStatusAsync(CorsairDevice device)
        {
            // TODO: Query device status via iCUE SDK
            return await Task.FromResult(new CorsairDeviceStatus
            {
                BatteryPercent = 100,
                PollingRateHz = 1000,
                FirmwareVersion = "Unknown",
                ConnectionType = "USB"
            });
        }

        public void Shutdown()
        {
            if (_initialized)
            {
                // TODO: Clean up iCUE SDK
                // CueSDK.Uninitialize();
                _logging.Info("iCUE SDK shut down");
                _initialized = false;
            }
        }
    }
}
