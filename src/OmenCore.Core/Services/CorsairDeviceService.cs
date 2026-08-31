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
        /// Prioritizes direct HID access (no iCUE required), falls back to iCUE SDK, then stub.
        /// </summary>
        public static async Task<CorsairDeviceService> CreateAsync(LoggingService logging, ConfigurationService? configService = null)
        {
            ICorsairSdkProvider sdk;

            try
            {
                // Priority 1: Try direct HID access (no iCUE required)
                logging.Info("Attempting Corsair direct HID access...");
                // Initialize telemetry for Corsair HID writes (shared instance)
                var telemetry = new TelemetryService(logging, configService ?? new ConfigurationService());
                sdk = new CorsairHidDirect(logging, telemetry);
                var initialized = await sdk.InitializeAsync();

                if (initialized)
                {
                    logging.Info("✓ Using Corsair direct HID - no iCUE required");
                }
                else
                {
                    // Check config - if iCUE fallback is disabled, skip trying iCUE
                    if (configService?.Config?.CorsairDisableIcueFallback == true)
                    {
                        logging.Info("Corsair direct HID failed and iCUE fallback disabled via config; using stub provider");
                        sdk = new CorsairSdkStub(logging);
                        await sdk.InitializeAsync();
                    }
                    else
                    {
                        // Priority 2: Try iCUE SDK (requires iCUE running)
                        logging.Info("No devices via direct HID, trying iCUE SDK...");
                        sdk = new CorsairICueSdk(logging);
                        initialized = await sdk.InitializeAsync();

                        if (!initialized)
                        {
                            logging.Info("No Corsair devices found via any method");
                            sdk = new CorsairSdkStub(logging);
                            await sdk.InitializeAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logging.Error($"Failed to initialize Corsair access: {ex.Message}");
                sdk = new CorsairSdkStub(logging);
                await sdk.InitializeAsync();
            }

            var service = new CorsairDeviceService(sdk, logging)
            {
                _initialized = true
            };
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
                var discovered = (await _sdk.DiscoverDevicesAsync()).ToList();
                await ReplaceDevicesAsync(discovered);

                _logging.Info($"Discovered {_devices.Count} Corsair device(s)");
            }
            catch (Exception ex)
            {
                _logging.Error("Corsair device discovery failed", ex);
            }
        }

        private async Task ReplaceDevicesAsync(IReadOnlyCollection<CorsairDevice> discovered)
        {
            await OmenCore.Utils.UiThreadMarshaller.InvokeAsync(() => ReplaceDevices(discovered));
        }

        private void ReplaceDevices(IReadOnlyCollection<CorsairDevice> discovered)
        {
            _devices.Clear();

            foreach (var device in discovered)
            {
                _devices.Add(device);
            }
        }

        /// <summary>
        /// Apply RGB lighting preset to a device.
        /// </summary>
        public async Task<bool> ApplyLightingPresetAsync(CorsairDevice device, CorsairLightingPreset preset)
        {
            if (device == null || preset == null)
            {
                _logging.Warn("Cannot apply lighting: device or preset is null");
                return false;
            }

            try
            {
                var applied = await _sdk.ApplyLightingAsync(device, preset);
                if (applied)
                {
                    _logging.Info($"Applied lighting preset '{preset.Name}' to {device.Name}");
                }
                else
                {
                    _logging.Warn($"Lighting preset '{preset.Name}' was not applied to {device.Name}");
                }
                return applied;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply lighting to {device.Name}", ex);
                return false;
            }
        }

        /// <summary>
        /// Apply a solid color to all Corsair devices.
        /// Returns true if at least one device accepted the write; previously this always
        /// logged "Applied ... to N device(s)" even when every per-device write threw, so a
        /// fully-failed sync was indistinguishable from a fully-successful one in the log
        /// and in RgbManager's sync summary.
        /// </summary>
        public async Task<bool> ApplyLightingToAllAsync(string colorHex)
        {
            var preset = new CorsairLightingPreset
            {
                Name = "Custom Color",
                Effect = LightingEffectType.Static,
                ColorHex = colorHex,
                PrimaryColor = colorHex
            };

            var succeeded = 0;
            foreach (var device in _devices)
            {
                try
                {
                    if (await _sdk.ApplyLightingAsync(device, preset))
                    {
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply color to {device.Name}", ex);
                }
            }

            LogApplyResult("color " + colorHex, succeeded, _devices.Count);
            return succeeded > 0;
        }

        /// <summary>
        /// Apply a breathing / pulse effect to all Corsair devices.
        /// </summary>
        public async Task<bool> ApplyBreathingToAllAsync(string hexColor, string? secondaryHex = null, double speed = 1.0)
        {
            var preset = new CorsairLightingPreset
            {
                Name = "Breathing",
                Effect = LightingEffectType.Breathing,
                PrimaryColor = hexColor,
                ColorHex = hexColor,
                SecondaryColor = secondaryHex ?? "#000000",
                Speed = speed
            };

            var succeeded = 0;
            foreach (var device in _devices)
            {
                try
                {
                    if (await _sdk.ApplyLightingAsync(device, preset))
                    {
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply breathing to {device.Name}", ex);
                }
            }

            LogApplyResult($"breathing {hexColor} (speed {speed:F1})", succeeded, _devices.Count);
            return succeeded > 0;
        }

        /// <summary>
        /// Apply spectrum / rainbow cycle to all Corsair devices.
        /// </summary>
        public async Task<bool> ApplySpectrumToAllAsync(double speed = 1.0)
        {
            var preset = new CorsairLightingPreset
            {
                Name = "Spectrum Cycle",
                Effect = LightingEffectType.ColorCycle,
                Speed = speed
            };

            var succeeded = 0;
            foreach (var device in _devices)
            {
                try
                {
                    if (await _sdk.ApplyLightingAsync(device, preset))
                    {
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply spectrum to {device.Name}", ex);
                }
            }

            LogApplyResult($"spectrum cycle (speed {speed:F1})", succeeded, _devices.Count);
            return succeeded > 0;
        }

        /// <summary>
        /// Apply wave / sweep effect to all Corsair devices.
        /// </summary>
        public async Task<bool> ApplyWaveToAllAsync(double speed = 1.0)
        {
            var preset = new CorsairLightingPreset
            {
                Name = "Wave",
                Effect = LightingEffectType.Wave,
                Speed = speed
            };

            var succeeded = 0;
            foreach (var device in _devices)
            {
                try
                {
                    if (await _sdk.ApplyLightingAsync(device, preset))
                    {
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply wave to {device.Name}", ex);
                }
            }

            LogApplyResult($"wave (speed {speed:F1})", succeeded, _devices.Count);
            return succeeded > 0;
        }

        private void LogApplyResult(string what, int succeeded, int total)
        {
            if (total == 0)
            {
                _logging.Warn($"Applied {what} to no devices - no Corsair devices discovered");
                return;
            }

            if (succeeded == total)
            {
                _logging.Info($"Applied {what} to {succeeded}/{total} device(s)");
            }
            else if (succeeded > 0)
            {
                _logging.Warn($"Applied {what} to only {succeeded}/{total} device(s) - {total - succeeded} write(s) failed");
            }
            else
            {
                _logging.Warn($"Failed to apply {what} to any of {total} device(s) - all writes failed");
            }
        }

        /// <summary>
        /// Apply a performance-mode synchronized Corsair pattern.
        /// Keyboards receive a wave/gradient pattern while other device types use static color.
        /// </summary>
        public async Task ApplyPerformanceModePatternAsync(string modeName, string primaryHex)
        {
            if (_devices.Count == 0)
            {
                return;
            }

            var normalizedPrimary = NormalizeHex(primaryHex, "#FFFFFF");
            var secondaryHex = CreateDarkerGradientHex(normalizedPrimary, 0.45);
            var speed = GetPatternSpeedForMode(modeName);

            var keyboardPreset = new CorsairLightingPreset
            {
                Name = $"{modeName} Gradient",
                Effect = LightingEffectType.Wave,
                PrimaryColor = normalizedPrimary,
                SecondaryColor = secondaryHex,
                ColorHex = normalizedPrimary,
                Speed = speed
            };

            var staticPreset = new CorsairLightingPreset
            {
                Name = $"{modeName} Static",
                Effect = LightingEffectType.Static,
                PrimaryColor = normalizedPrimary,
                ColorHex = normalizedPrimary
            };

            var modeSyncedSucceeded = 0;
            foreach (var device in _devices)
            {
                try
                {
                    var preset = device.DeviceType == CorsairDeviceType.Keyboard ? keyboardPreset : staticPreset;
                    if (await _sdk.ApplyLightingAsync(device, preset))
                    {
                        modeSyncedSucceeded++;
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error($"Failed to apply mode-synced Corsair lighting to {device.Name}", ex);
                }
            }

            LogApplyResult($"mode-synced pattern for '{modeName}' using {normalizedPrimary}/{secondaryHex}", modeSyncedSucceeded, _devices.Count);
        }

        private static string NormalizeHex(string? hex, string fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            var value = hex.Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                value = $"#{value}";
            }

            if (value.Length != 7)
            {
                return fallback;
            }

            return value;
        }

        private static string CreateDarkerGradientHex(string primaryHex, double factor)
        {
            try
            {
                var hex = primaryHex.TrimStart('#');
                var r = Convert.ToInt32(hex.Substring(0, 2), 16);
                var g = Convert.ToInt32(hex.Substring(2, 2), 16);
                var b = Convert.ToInt32(hex.Substring(4, 2), 16);

                var gradientR = (int)Math.Clamp(r * factor, 0, 255);
                var gradientG = (int)Math.Clamp(g * factor, 0, 255);
                var gradientB = (int)Math.Clamp(b * factor, 0, 255);
                return $"#{gradientR:X2}{gradientG:X2}{gradientB:X2}";
            }
            catch
            {
                return "#222222";
            }
        }

        private static double GetPatternSpeedForMode(string modeName)
        {
            var normalized = modeName?.Trim().ToLowerInvariant() ?? string.Empty;
            return normalized switch
            {
                "performance" or "high performance" => 1.4,
                "quiet" or "power saver" => 0.7,
                _ => 1.0
            };
        }

        /// <summary>
        /// Configure DPI stages for a mouse. Returns true only if the write actually reached
        /// the device - callers must not update UI/config state as if it succeeded when this
        /// returns false (e.g. the active backend has no DPI write path at all, like RGB.NET).
        /// </summary>
        public async Task<bool> ApplyDpiStagesAsync(CorsairDevice device, IEnumerable<CorsairDpiStage> stages)
        {
            if (device == null)
            {
                _logging.Warn("Cannot apply DPI: device is null");
                return false;
            }

            try
            {
                var applied = await _sdk.ApplyDpiStagesAsync(device, stages);
                if (applied)
                {
                    device.DpiStages = stages.ToList();
                    _logging.Info($"Updated DPI stages for {device.Name}");
                }
                else
                {
                    _logging.Warn($"DPI stages were not applied to {device.Name} - active Corsair backend does not support writing DPI");
                }

                return applied;
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply DPI stages to {device.Name}", ex);
                return false;
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

        /// <summary>
        /// Flash device LEDs to help identify which physical device is which.
        /// Useful when user has multiple similar Corsair devices.
        /// </summary>
        public async Task FlashDeviceAsync(CorsairDevice device, int flashCount = 3)
        {
            if (device == null)
            {
                _logging.Warn("Cannot flash device: device is null");
                return;
            }

            try
            {
                _logging.Info($"Flashing device {device.Name} for identification...");
                await _sdk.FlashDeviceAsync(device, flashCount, 300);
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to flash device {device.Name}", ex);
            }
        }

        public void Dispose()
        {
            _sdk?.Shutdown();
        }
    }
}
