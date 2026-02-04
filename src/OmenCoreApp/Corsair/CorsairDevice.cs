using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Corsair
{
    public class CorsairDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CorsairDeviceType DeviceType { get; set; }
        public List<string> Zones { get; set; } = new();
        public CorsairDeviceStatus Status { get; set; } = new();
        public List<CorsairDpiStage> DpiStages { get; set; } = new();
        
        /// <summary>
        /// Current RGB color hex code (e.g., "#FF0000"). Used to restore after flashing.
        /// </summary>
        public string? CurrentColorHex { get; set; }
        
        public bool IsMouse => DeviceType == CorsairDeviceType.Mouse;
        public int CurrentDpi => DpiStages.FirstOrDefault()?.Dpi ?? 0;
    }
}
