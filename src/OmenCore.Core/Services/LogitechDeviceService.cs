using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        /// Prioritizes direct HID access (no G HUB required), falls back to G HUB SDK, then stub.
        /// </summary>
        public static async Task<LogitechDeviceService> CreateAsync(LoggingService logging)
        {
            ILogitechSdkProvider sdk;

            try
            {
                // Priority 1: Try direct HID access (no G HUB required)
                logging.Info("Attempting Logitech direct HID access...");
                sdk = new LogitechHidDirect(logging);
                var initialized = await sdk.InitializeAsync();

                if (initialized)
                {
                    logging.Info("✓ Using Logitech direct HID - no G HUB required");
                }
                else
                {
                    // Priority 2: Try G HUB SDK (requires G HUB running)
                    logging.Info("No devices via direct HID, trying G HUB SDK...");
                    sdk = new LogitechGHubSdk(logging);
                    initialized = await sdk.InitializeAsync();

                    if (!initialized)
                    {
                        logging.Info("No Logitech devices found via any method");
                        sdk = new LogitechSdkStub(logging);
                        await sdk.InitializeAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                logging.Error($"Failed to initialize Logitech access: {ex.Message}");
                sdk = new LogitechSdkStub(logging);
                await sdk.InitializeAsync();
            }

            var service = new LogitechDeviceService(sdk, logging)
            {
                _initialized = true
            };
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
                var discovered = (await _sdk.DiscoverDevicesAsync()).ToList();
                await ReplaceDevicesAsync(discovered);

                _logging.Info($"Discovered {_devices.Count} Logitech device(s)");
            }
            catch (Exception ex)
            {
                _logging.Error("Logitech device discovery failed", ex);
            }
        }

        private async Task ReplaceDevicesAsync(IReadOnlyCollection<LogitechDevice> discovered)
        {
            await OmenCore.Utils.UiThreadMarshaller.InvokeAsync(() => ReplaceDevices(discovered));
        }

        private void ReplaceDevices(IReadOnlyCollection<LogitechDevice> discovered)
        {
            _devices.Clear();

            foreach (var device in discovered)
            {
                _devices.Add(device);
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
        /// Apply spectrum/rainbow cycling effect.
        /// </summary>
        public async Task ApplySpectrumEffectAsync(LogitechDevice device, int speed)
        {
            if (device == null)
                return;

            try
            {
                await _sdk.ApplySpectrumEffectAsync(device, speed);
                _logging.Info($"Applied spectrum effect to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply spectrum effect to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Apply flash/strobe effect.
        /// </summary>
        public async Task ApplyFlashEffectAsync(LogitechDevice device, string hexColor, int durationMs = 5000, int intervalMs = 200)
        {
            if (device == null)
                return;

            try
            {
                await _sdk.ApplyFlashEffectAsync(device, hexColor, durationMs, intervalMs);
                _logging.Info($"Applied flash effect to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply flash effect to {device.Name}", ex);
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
