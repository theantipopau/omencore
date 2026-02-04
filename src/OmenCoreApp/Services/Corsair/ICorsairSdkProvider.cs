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
        /// Flash device LEDs to help identify which physical device this is.
        /// Typically flashes white a few times then returns to original color.
        /// </summary>
        Task FlashDeviceAsync(CorsairDevice device, int flashCount = 3, int intervalMs = 300);

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
            // Return empty list - no fake devices (iCUE SDK not available)
            // Users will see "No devices found" which is accurate
            return Task.FromResult<IEnumerable<CorsairDevice>>(Array.Empty<CorsairDevice>());
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

        public Task FlashDeviceAsync(CorsairDevice device, int flashCount = 3, int intervalMs = 300)
        {
            _logging.Info($"[Stub] Flash device {device.Name} ({flashCount}x, {intervalMs}ms)");
            return Task.CompletedTask;
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
        private CorsairDeviceProvider? _provider;
        private bool _initialized;

        public CorsairICueSdk(LoggingService logging)
        {
            _logging = logging;
            _surface = new RGBSurface();
            // Provider will be created in InitializeAsync to get better error handling
        }

        private bool IsIcueRunning()
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName("iCUE").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Check if iCUE is running
                var icueRunning = IsIcueRunning();
                if (!icueRunning)
                {
                    _logging.Warn("Corsair iCUE software not detected - Corsair device discovery requires iCUE to be running");
                    _logging.Info("💡 To enable Corsair device support: Install and run Corsair iCUE from https://www.corsair.com/icue");
                    return false;
                }
                
                _logging.Info("Corsair iCUE detected, initializing SDK...");
                
                // Set explicit SDK paths - RGB.NET looks in x64 subfolder relative to app location
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var x64SdkPath = System.IO.Path.Combine(appDir, "x64");
                
                // Configure provider to look in the correct location (static property)
                CorsairDeviceProvider.PossibleX64NativePaths.Clear();
                CorsairDeviceProvider.PossibleX64NativePaths.Add(x64SdkPath);
                
                _logging.Info($"Corsair SDK search path: {x64SdkPath}");
                
                // Create provider with specific settings for better compatibility
                _provider = new CorsairDeviceProvider();
                
                // Subscribe to exception events on the provider
                Exception? loadException = null;
                _provider.Exception += (sender, args) => 
                {
                    loadException = args.Exception;
                    _logging.Warn($"RGB.NET Corsair provider exception: {args.Exception.Message}");
                };
                
                // Try to load the provider
                _logging.Info("Loading Corsair device provider...");
                
                try
                {
                    _surface.Load(_provider, throwExceptions: true);
                }
                catch (Exception loadEx)
                {
                    _logging.Warn($"RGB.NET provider load error: {loadEx.Message}");
                    // Continue anyway - some devices might still work
                }
                
                // Give time for device enumeration (wireless devices may need longer)
                await Task.Delay(1000);
                
                // Log detailed info about what was found
                var deviceCount = _surface.Devices.Count();
                _logging.Info($"RGB.NET Corsair provider loaded - Found {deviceCount} device(s)");
                
                // Also check the provider's device list directly
                var providerDevices = _provider.Devices?.Count() ?? 0;
                _logging.Info($"Provider reports {providerDevices} device(s)");
                
                if (deviceCount > 0 || providerDevices > 0)
                {
                    foreach (var device in _surface.Devices)
                    {
                        _logging.Info($"  Found: {device.DeviceInfo.Model} ({device.DeviceInfo.DeviceType}) - {device.DeviceInfo.Manufacturer}");
                    }
                    _initialized = true;
                    return true;
                }
                else
                {
                    _logging.Warn("Corsair iCUE SDK initialized but no devices found");
                    
                    // More detailed troubleshooting
                    if (loadException != null)
                    {
                        _logging.Warn($"SDK Load error: {loadException.Message}");
                    }
                    
                    _logging.Info("💡 Troubleshooting steps:");
                    _logging.Info("   1. Ensure 'Enable SDK' is ON in iCUE Settings → General");
                    _logging.Info("   2. Close and restart iCUE after enabling SDK");
                    _logging.Info("   3. iCUE v4 or v5 required for SDK support");
                    _logging.Info("   4. Wireless devices: Ensure receiver is connected and device is powered on");
                    _logging.Info("   5. Try running OmenCore as Administrator");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"iCUE SDK initialization failed: {ex.Message}", ex);
                _logging.Info("This may indicate iCUE SDK is not properly installed or is a version incompatibility");
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

        public async Task FlashDeviceAsync(CorsairDevice device, int flashCount = 3, int intervalMs = 300)
        {
            if (!_initialized)
                return;

            try
            {
                var rgbDevice = _surface.Devices.FirstOrDefault(d => d.GetHashCode().ToString() == device.DeviceId);
                if (rgbDevice == null)
                {
                    _logging.Warn($"Device {device.Name} not found for flash");
                    return;
                }

                // Store original colors
                var originalColors = new Dictionary<Led, Color>();
                foreach (var led in rgbDevice)
                {
                    originalColors[led] = led.Color;
                }

                // Flash white/off pattern
                var white = new Color(255, 255, 255);
                var off = new Color(0, 0, 0);

                for (int i = 0; i < flashCount; i++)
                {
                    // Flash white
                    foreach (var led in rgbDevice)
                        led.Color = white;
                    _surface.Update();
                    await Task.Delay(intervalMs);

                    // Flash off
                    foreach (var led in rgbDevice)
                        led.Color = off;
                    _surface.Update();
                    await Task.Delay(intervalMs);
                }

                // Restore original colors
                foreach (var kvp in originalColors)
                    kvp.Key.Color = kvp.Value;
                _surface.Update();

                _logging.Info($"Flashed device {device.Name} {flashCount} times");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to flash device {device.Name}", ex);
            }
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
