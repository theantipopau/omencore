using System;
using System.Collections.ObjectModel;
using OmenCore.Logitech;

namespace OmenCore.Services
{
    public class LogitechDeviceService
    {
        private readonly LoggingService _logging;
        private readonly ObservableCollection<LogitechDevice> _devices = new();
        public ReadOnlyObservableCollection<LogitechDevice> Devices { get; }

        public LogitechDeviceService(LoggingService logging)
        {
            _logging = logging;
            Devices = new ReadOnlyObservableCollection<LogitechDevice>(_devices);
        }

        public void Discover()
        {
            _devices.Clear();
            // TODO: Hook Logitech G HUB SDK / HID APIs for real devices.
            // No devices added - only detect real connected hardware
            _logging.Info($"Discovered {_devices.Count} Logitech device(s)");
        }

        public void ApplyColor(LogitechDevice device, string hexColor, int brightnessPercent)
        {
            device.CurrentColorHex = hexColor;
            device.Status.BrightnessPercent = brightnessPercent;
            _logging.Info($"Logitech {device.Name} -> color {hexColor} @ {brightnessPercent}% brightness");
        }
    }
}
