using System.Collections.Generic;

namespace OmenCore.Razer
{
    public class RazerDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public RazerDeviceType DeviceType { get; set; }
        public List<string> Zones { get; set; } = new();
        public RazerDeviceStatus Status { get; set; } = new();
        
        public bool IsMouse => DeviceType == RazerDeviceType.Mouse;
        public bool IsKeyboard => DeviceType == RazerDeviceType.Keyboard;
    }
}
