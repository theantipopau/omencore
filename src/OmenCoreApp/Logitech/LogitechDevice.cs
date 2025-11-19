namespace OmenCore.Logitech
{
    public class LogitechDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public LogitechDeviceType DeviceType { get; set; }
        public LogitechDeviceStatus Status { get; set; } = new();
        public string CurrentColorHex { get; set; } = "#E6002E";
    }
}
