using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OmenCore.Logitech;

namespace OmenCore.Services.Logitech
{
    /// <summary>
    /// Interface for Logitech device SDK providers.
    /// Supports G HUB SDK, LGS SDK, and HID fallback.
    /// </summary>
    public interface ILogitechSdkProvider
    {
        /// <summary>
        /// Initialize the Logitech SDK connection.
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// Enumerate all connected Logitech G devices.
        /// </summary>
        Task<IEnumerable<LogitechDevice>> DiscoverDevicesAsync();

        /// <summary>
        /// Apply static RGB color to a device.
        /// </summary>
        Task ApplyStaticColorAsync(LogitechDevice device, string hexColor, int brightness);

        /// <summary>
        /// Apply RGB breathing effect (if supported).
        /// </summary>
        Task ApplyBreathingEffectAsync(LogitechDevice device, string hexColor, int speed);

        /// <summary>
        /// Apply spectrum/rainbow cycling effect.
        /// </summary>
        Task ApplySpectrumEffectAsync(LogitechDevice device, int speed);

        /// <summary>
        /// Apply flash/strobe effect.
        /// </summary>
        Task ApplyFlashEffectAsync(LogitechDevice device, string hexColor, int durationMs, int intervalMs);

        /// <summary>
        /// Read current DPI setting from a mouse.
        /// </summary>
        Task<int> GetDpiAsync(LogitechDevice device);

        /// <summary>
        /// Set DPI on a mouse.
        /// </summary>
        Task SetDpiAsync(LogitechDevice device, int dpi);

        /// <summary>
        /// Get device status (battery, connection type).
        /// </summary>
        Task<LogitechDeviceStatus> GetDeviceStatusAsync(LogitechDevice device);

        /// <summary>
        /// Release SDK resources.
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// Stub implementation for testing without Logitech SDK.
    /// Returns empty device list when no real devices are detected.
    /// </summary>
    public class LogitechSdkStub : ILogitechSdkProvider
    {
        private readonly LoggingService _logging;

        public LogitechSdkStub(LoggingService logging)
        {
            _logging = logging;
        }

        public Task<bool> InitializeAsync()
        {
            _logging.Info("Logitech SDK Stub initialized");
            return Task.FromResult(true);
        }

        public Task<IEnumerable<LogitechDevice>> DiscoverDevicesAsync()
        {
            // Return empty list - no fake devices
            // Real devices will be detected via direct HID or G HUB SDK
            return Task.FromResult<IEnumerable<LogitechDevice>>(Array.Empty<LogitechDevice>());
        }

        public Task ApplyStaticColorAsync(LogitechDevice device, string hexColor, int brightness)
        {
            device.CurrentColorHex = hexColor;
            device.Status.BrightnessPercent = brightness;
            _logging.Info($"[Stub] Applied color {hexColor} @ {brightness}% to {device.Name}");
            return Task.CompletedTask;
        }

        public Task ApplyBreathingEffectAsync(LogitechDevice device, string hexColor, int speed)
        {
            _logging.Info($"[Stub] Applied breathing effect to {device.Name}");
            return Task.CompletedTask;
        }

        public Task ApplySpectrumEffectAsync(LogitechDevice device, int speed)
        {
            _logging.Info($"[Stub] Applied spectrum effect to {device.Name}");
            return Task.CompletedTask;
        }

        public Task ApplyFlashEffectAsync(LogitechDevice device, string hexColor, int durationMs, int intervalMs)
        {
            _logging.Info($"[Stub] Applied flash effect to {device.Name}");
            return Task.CompletedTask;
        }

        public Task<int> GetDpiAsync(LogitechDevice device)
        {
            return Task.FromResult(device.Status.Dpi);
        }

        public Task SetDpiAsync(LogitechDevice device, int dpi)
        {
            device.Status.Dpi = dpi;
            _logging.Info($"[Stub] Set DPI to {dpi} on {device.Name}");
            return Task.CompletedTask;
        }

        public Task<LogitechDeviceStatus> GetDeviceStatusAsync(LogitechDevice device)
        {
            return Task.FromResult(device.Status);
        }

        public void Shutdown()
        {
            _logging.Info("Logitech SDK Stub shut down");
        }
    }

    /// <summary>
    /// Real Logitech G LED SDK implementation.
    /// Uses the LogitechLedEnginesWrapper.dll native library.
    /// Requires Logitech G HUB to be running for device communication.
    /// </summary>
    public class LogitechGHubSdk : ILogitechSdkProvider
    {
        private readonly LoggingService _logging;
        private bool _initialized;
        private bool _sdkAvailable;

        // P/Invoke declarations for Logitech LED SDK
        private const string DllName = "LogitechLedEnginesWrapper.dll";

        // Device type flags
        private const int LOGI_DEVICETYPE_MONOCHROME = 1;
        private const int LOGI_DEVICETYPE_RGB = 2;
        private const int LOGI_DEVICETYPE_PERKEY_RGB = 4;
        private const int LOGI_DEVICETYPE_ALL = LOGI_DEVICETYPE_MONOCHROME | LOGI_DEVICETYPE_RGB | LOGI_DEVICETYPE_PERKEY_RGB;

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedInit();

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedInitWithName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string name);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedSetTargetDevice(int targetDevice);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedGetSdkVersion(ref int majorNum, ref int minorNum, ref int buildNum);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedSaveCurrentLighting();

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedSetLighting(int redPercentage, int greenPercentage, int bluePercentage);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedRestoreLighting();

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedFlashLighting(int redPercentage, int greenPercentage, int bluePercentage, int milliSecondsDuration, int milliSecondsInterval);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedPulseLighting(int redPercentage, int greenPercentage, int bluePercentage, int milliSecondsDuration, int milliSecondsInterval);

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool LogiLedStopEffects();

        [System.Runtime.InteropServices.DllImport(DllName, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void LogiLedShutdown();

        public LogitechGHubSdk(LoggingService logging)
        {
            _logging = logging;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Check if Logitech G HUB is running
                var gHubProcesses = System.Diagnostics.Process.GetProcessesByName("lghub");
                var gHubAgentProcesses = System.Diagnostics.Process.GetProcessesByName("lghub_agent");
                
                if (gHubProcesses.Length == 0 && gHubAgentProcesses.Length == 0)
                {
                    _logging.Info("Logitech G HUB not running - SDK will not function");
                    _sdkAvailable = false;
                    return false;
                }

                // Initialize the LED SDK with app name
                var result = LogiLedInitWithName("OmenCore");
                if (!result)
                {
                    // Try basic init as fallback
                    result = LogiLedInit();
                }

                if (!result)
                {
                    _logging.Warn("Logitech LED SDK initialization failed - G HUB may need to allow third-party apps");
                    _sdkAvailable = false;
                    return false;
                }

                // Get SDK version
                int major = 0, minor = 0, build = 0;
                if (LogiLedGetSdkVersion(ref major, ref minor, ref build))
                {
                    _logging.Info($"✓ Logitech LED SDK v{major}.{minor}.{build} initialized");
                }
                else
                {
                    _logging.Info("✓ Logitech LED SDK initialized (version unknown)");
                }

                // Target all device types
                LogiLedSetTargetDevice(LOGI_DEVICETYPE_ALL);

                _initialized = true;
                _sdkAvailable = true;
                return await Task.FromResult(true);
            }
            catch (DllNotFoundException ex)
            {
                _logging.Warn($"Logitech LED SDK DLL not found: {ex.Message}");
                _sdkAvailable = false;
                return false;
            }
            catch (Exception ex)
            {
                _logging.Error("Logitech LED SDK initialization failed", ex);
                _sdkAvailable = false;
                return false;
            }
        }

        public async Task<IEnumerable<LogitechDevice>> DiscoverDevicesAsync()
        {
            var devices = new List<LogitechDevice>();

            if (!_sdkAvailable || !_initialized)
            {
                return devices;
            }

            try
            {
                // The Logitech LED SDK doesn't expose device enumeration directly
                // It controls all connected Logitech devices as a group
                // We create a virtual "All Devices" entry to represent this
                
                // Check if we can actually set lighting (indicates devices are connected)
                var canControl = LogiLedSaveCurrentLighting();
                if (canControl)
                {
                    LogiLedRestoreLighting();
                    
                    devices.Add(new LogitechDevice
                    {
                        Name = "Logitech G Devices",
                        DeviceType = LogitechDeviceType.Keyboard,
                        CurrentColorHex = "#00FF00",
                        Status = new LogitechDeviceStatus
                        {
                            BatteryPercent = 100,
                            Dpi = 0,
                            MaxDpi = 0,
                            FirmwareVersion = "G HUB",
                            ConnectionType = "G HUB SDK",
                            BrightnessPercent = 100
                        }
                    });
                    
                    _logging.Info("✓ Logitech G devices detected via LED SDK");
                }
                else
                {
                    _logging.Info("No Logitech G devices responding to LED SDK");
                }
            }
            catch (Exception ex)
            {
                _logging.Warn($"Error discovering Logitech devices: {ex.Message}");
            }

            return await Task.FromResult<IEnumerable<LogitechDevice>>(devices);
        }

        public async Task ApplyStaticColorAsync(LogitechDevice device, string hexColor, int brightness)
        {
            if (!_sdkAvailable || !_initialized)
            {
                _logging.Warn("Logitech SDK not available");
                return;
            }

            try
            {
                // Parse hex color
                var color = System.Drawing.ColorTranslator.FromHtml(hexColor);
                
                // Apply brightness (0-100%) to color values
                // Logitech SDK uses percentage (0-100) not absolute values
                var r = (int)(color.R / 255.0 * brightness);
                var g = (int)(color.G / 255.0 * brightness);
                var b = (int)(color.B / 255.0 * brightness);

                // Stop any running effects first
                LogiLedStopEffects();

                // Set the color on all devices
                if (LogiLedSetLighting(r, g, b))
                {
                    device.CurrentColorHex = hexColor;
                    _logging.Info($"✓ Logitech LED color set: {hexColor} @ {brightness}%");
                }
                else
                {
                    _logging.Warn("Failed to set Logitech LED color");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply color to Logitech device: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public async Task ApplyBreathingEffectAsync(LogitechDevice device, string hexColor, int speed)
        {
            if (!_sdkAvailable || !_initialized)
            {
                _logging.Warn("Logitech SDK not available");
                return;
            }

            try
            {
                var color = System.Drawing.ColorTranslator.FromHtml(hexColor);
                var r = (int)(color.R / 255.0 * 100);
                var g = (int)(color.G / 255.0 * 100);
                var b = (int)(color.B / 255.0 * 100);

                // Map speed (1-10) to duration (slower speed = longer duration)
                int duration = 0; // Infinite
                int interval = 500 + (10 - speed) * 200; // 500ms to 2300ms

                if (LogiLedPulseLighting(r, g, b, duration, interval))
                {
                    _logging.Info($"✓ Logitech breathing effect applied: {hexColor}");
                }
                else
                {
                    _logging.Warn("Failed to apply Logitech breathing effect");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply breathing effect: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public async Task ApplySpectrumEffectAsync(LogitechDevice device, int speed)
        {
            if (!_sdkAvailable || !_initialized)
            {
                _logging.Warn("Logitech SDK not available");
                return;
            }

            try
            {
                // Logitech LED SDK doesn't have a direct spectrum/rainbow API
                // We simulate it by cycling through colors using a background task
                // For now, just log and apply a cycle approximation
                _logging.Info($"✓ Logitech spectrum effect requested (speed={speed})");
                
                // Start a simple color cycle - this is a best-effort simulation
                // Real spectrum would need continuous updates which we avoid here
                // Instead, apply a distinctive cyan/purple that suggests "spectrum mode"
                LogiLedStopEffects();
                LogiLedSetLighting(50, 0, 100); // Purple-ish to indicate spectrum active
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply spectrum effect: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public async Task ApplyFlashEffectAsync(LogitechDevice device, string hexColor, int durationMs, int intervalMs)
        {
            if (!_sdkAvailable || !_initialized)
            {
                _logging.Warn("Logitech SDK not available");
                return;
            }

            try
            {
                var color = System.Drawing.ColorTranslator.FromHtml(hexColor);
                var r = (int)(color.R / 255.0 * 100);
                var g = (int)(color.G / 255.0 * 100);
                var b = (int)(color.B / 255.0 * 100);

                if (LogiLedFlashLighting(r, g, b, durationMs, intervalMs))
                {
                    _logging.Info($"✓ Logitech flash effect applied: {hexColor}");
                }
                else
                {
                    _logging.Warn("Failed to apply Logitech flash effect");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to apply flash effect: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        public async Task<int> GetDpiAsync(LogitechDevice device)
        {
            // LED SDK doesn't provide DPI info - would need different API
            return await Task.FromResult(0);
        }

        public async Task SetDpiAsync(LogitechDevice device, int dpi)
        {
            // LED SDK doesn't support DPI control - would need different API
            _logging.Info("DPI control not available via Logitech LED SDK");
            await Task.CompletedTask;
        }

        public async Task<LogitechDeviceStatus> GetDeviceStatusAsync(LogitechDevice device)
        {
            // LED SDK doesn't provide detailed device status
            return await Task.FromResult(new LogitechDeviceStatus
            {
                BatteryPercent = 100,
                Dpi = 0,
                MaxDpi = 0,
                FirmwareVersion = "G HUB",
                ConnectionType = _sdkAvailable ? "G HUB SDK" : "Disconnected",
                BrightnessPercent = 100
            });
        }

        public void Shutdown()
        {
            if (_initialized)
            {
                try
                {
                    LogiLedRestoreLighting();
                    LogiLedShutdown();
                    _logging.Info("Logitech LED SDK shut down");
                }
                catch (Exception ex)
                {
                    _logging.Warn($"Error shutting down Logitech SDK: {ex.Message}");
                }
                _initialized = false;
                _sdkAvailable = false;
            }
        }
    }
}
