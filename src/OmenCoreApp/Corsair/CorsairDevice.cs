using System.Collections.Generic;

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
    }
}
