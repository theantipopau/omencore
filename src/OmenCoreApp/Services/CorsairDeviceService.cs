using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using OmenCore.Corsair;
using OmenCore.Models;
using OmenCore.Services.Corsair;

namespace OmenCore.Services
{
    /// <summary>
    /// Corsair device service with SDK abstraction layer.
    /// Supports both stub and real iCUE SDK implementations.
    /// </summary>
    public class CorsairDeviceService : IDisposable
    {
        private readonly ICorsairSdkProvider _sdk;
        private readonly LoggingService _logging;
        private readonly ObservableCollection<CorsairDevice> _devices = new();
        private bool _initialized;

        public ReadOnlyObservableCollection<CorsairDevice> Devices { get; }

        /// <summary>
        /// Create service with specified SDK provider.
        /// </summary>
        public CorsairDeviceService(ICorsairSdkProvider sdkProvider, LoggingService logging)
        {
            _sdk = sdkProvider;
            _logging = logging;
            Devices = new ReadOnlyObservableCollection<CorsairDevice>(_devices);
        }

        /// <summary>
        /// Factory method to create service with auto-detection of SDK availability.
        /// Falls back to stub if iCUE SDK is not available.
        /// </summary>
        public static async Task<CorsairDeviceService> CreateAsync(LoggingService logging)
        {
            ICorsairSdkProvider sdk;

            try
            {
                // Try to use real iCUE SDK
                sdk = new CorsairICueSdk(logging);
                var initialized = await sdk.InitializeAsync();

                if (!initialized)
                {
                    logging.Warn("iCUE SDK unavailable, falling back to stub implementation");
                    sdk = new CorsairSdkStub(logging);
                    await sdk.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                logging.Error("Failed to initialize iCUE SDK, using stub", ex);
                sdk = new CorsairSdkStub(logging);
                await sdk.InitializeAsync();
            }

            var service = new CorsairDeviceService(sdk, logging);
            service._initialized = true;
            return service;
        }

        /// <summary>
        /// Discover and enumerate all Corsair devices.
        /// </summary>
        public async Task DiscoverAsync()
        {
            if (!_initialized)
            {
                _logging.Warn("Corsair service not initialized");
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

                _logging.Info($"Discovered {_devices.Count} Corsair device(s)");
            }
            catch (Exception ex)
            {
                _logging.Error("Corsair device discovery failed", ex);
            }
        }

        /// <summary>
        /// Apply RGB lighting preset to a device.
        /// </summary>
        public async Task ApplyLightingPresetAsync(CorsairDevice device, CorsairLightingPreset preset)
        {
            if (device == null || preset == null)
            {
                _logging.Warn("Cannot apply lighting: device or preset is null");
                return;
            }

            try
            {
                await _sdk.ApplyLightingAsync(device, preset);
                _logging.Info($"Applied lighting preset '{preset.Name}' to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply lighting to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Apply a solid color to all Corsair devices.
        /// </summary>
        public async Task ApplyLightingToAllAsync(string colorHex)
        {
            var preset = new CorsairLightingPreset
            {
                Name = "Custom Color",
                ColorHex = colorHex
            };

            foreach (var device in _devices)
            {
                try
                {
                    await _sdk.ApplyLightingAsync(device, preset);
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply color to {device.Name}", ex);
                }
            }

            _logging.Info($"Applied color {colorHex} to {_devices.Count} device(s)");
        }

        /// <summary>
        /// Configure DPI stages for a mouse.
        /// </summary>
        public async Task ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
        {
            if (device == null)
            {
                _logging.Warn("Cannot apply DPI: device is null");
                return;
            }

            try
            {
                await _sdk.ApplyDpiStagesAsync(device, stages);
                device.DpiStages = stages.ToList();
                _logging.Info($"Updated DPI stages for {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply DPI stages to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Upload and apply a macro profile to a device.
        /// </summary>
        public async Task ApplyMacroProfileAsync(CorsairDevice device, MacroProfile macro)
        {
            if (device == null || macro == null)
            {
                _logging.Warn("Cannot apply macro: device or macro is null");
                return;
            }

            try
            {
                await _sdk.ApplyMacroAsync(device, macro);
                _logging.Info($"Applied macro '{macro.Name}' to {device.Name}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply macro to {device.Name}", ex);
            }
        }

        /// <summary>
        /// Sync all Corsair devices with laptop lighting theme.
        /// </summary>
        public async Task SyncWithThemeAsync(LightingProfile profile)
        {
            if (profile == null)
            {
                _logging.Warn("Cannot sync theme: profile is null");
                return;
            }

            try
            {
                await _sdk.SyncWithThemeAsync(_devices, profile);
                _logging.Info($"Synced {_devices.Count} device(s) with theme '{profile.Name}'");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to sync devices with theme", ex);
            }
        }

        /// <summary>
        /// Refresh device status (battery, polling rate, firmware).
        /// </summary>
        public async Task RefreshDeviceStatusAsync(CorsairDevice device)
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
