using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using OmenCore.Logitech;
using OmenCore.Services.Logitech;

namespace OmenCore.Services
{
    /// <summary>
    /// Logitech device service with SDK abstraction layer.
    /// Supports both stub and real G HUB SDK implementations (WIP).
    /// </summary>
    public class LogitechDeviceService : IDisposable
    {
        private readonly ILogitechSdkProvider _sdk;
        private readonly LoggingService _logging;
        private readonly ObservableCollection<LogitechDevice> _devices = new();
        private bool _initialized;

        public ReadOnlyObservableCollection<LogitechDevice> Devices { get; }

        /// <summary>
        /// Create service with specified SDK provider.
        /// </summary>
        public LogitechDeviceService(ILogitechSdkProvider sdkProvider, LoggingService logging)
        {
            _sdk = sdkProvider;
            _logging = logging;
            Devices = new ReadOnlyObservableCollection<LogitechDevice>(_devices);
        }

        /// <summary>
        /// Factory method to create service with auto-detection of SDK availability.
        /// Falls back to stub if G HUB SDK is not available.
        /// </summary>
        public static async Task<LogitechDeviceService> CreateAsync(LoggingService logging)
        {
            ILogitechSdkProvider sdk;

            try
            {
                // Try to use real G HUB SDK
                sdk = new LogitechGHubSdk(logging);
                var initialized = await sdk.InitializeAsync();

                if (!initialized)
                {
                    logging.Warn("Logitech G HUB SDK unavailable, falling back to stub");
                    sdk = new LogitechSdkStub(logging);
                    await sdk.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                logging.Error("Failed to initialize G HUB SDK, using stub", ex);
                sdk = new LogitechSdkStub(logging);
                await sdk.InitializeAsync();
            }

            var service = new LogitechDeviceService(sdk, logging);
            service._initialized = true;
            return service;
        }

        /// <summary>
        /// Discover and enumerate all Logitech G devices.
        /// </summary>
        public async Task DiscoverAsync()
        {
            if (!_initialized)
            {
                _logging.Warn("Logitech service not initialized");
                return;
            }

            try
            {
                _devices.Clear();
                var discovered = await _sdk.DiscoverDevicesAsync();

                foreach (var device in discovered)
                {
                    _devices.Add(device);
                }

                _logging.Info($"Discovered {_devices.Count} Logitech device(s)");
            }
            catch (Exception ex)
            {
                _logging.Error("Logitech device discovery failed", ex);
            }
        }

        /// <summary>
        /// Apply static RGB color to a device.
        /// </summary>
        public async Task ApplyStaticColorAsync(LogitechDevice device, string hexColor, int brightness)
        {
            if (device == null)
            {
                _logging.Warn("Cannot apply color: device is null");
                return;
            }

            try
            {
                await _sdk.ApplyStaticColorAsync(device, hexColor, brightness);
                _logging.Info($"Applied color {hexColor} @ {brightness}% to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply color to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Apply breathing RGB effect (WIP).
        /// </summary>
        public async Task ApplyBreathingEffectAsync(LogitechDevice device, string hexColor, int speed)
        {
            if (device == null)
                return;

            try
            {
                await _sdk.ApplyBreathingEffectAsync(device, hexColor, speed);
                _logging.Info($"Applied breathing effect to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply effect to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Get current DPI setting from a mouse.
        /// </summary>
        public async Task<int> GetDpiAsync(LogitechDevice device)
        {
            if (device == null || device.DeviceType != LogitechDeviceType.Mouse)
                return 0;

            try
            {
                return await _sdk.GetDpiAsync(device);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to get DPI from {device.Name}", ex);
                return 0;
            }
        }

        /// <summary>
        /// Set DPI on a mouse (WIP).
        /// </summary>
        public async Task SetDpiAsync(LogitechDevice device, int dpi)
        {
            if (device == null || device.DeviceType != LogitechDeviceType.Mouse)
                return;

            try
            {
                await _sdk.SetDpiAsync(device, dpi);
                _logging.Info($"Set DPI to {dpi} on {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to set DPI on {device.Name}", ex);
            }
        }

        /// <summary>
        /// Refresh device status (battery, connection, firmware).
        /// </summary>
        public async Task RefreshDeviceStatusAsync(LogitechDevice device)
        {
            if (device == null)
                return;

            try
            {
                var status = await _sdk.GetDeviceStatusAsync(device);
                device.Status = status;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to refresh status for {device.Name}", ex);
            }
        }

        public void Dispose()
        {
            _sdk?.Shutdown();
        }
    }
}
