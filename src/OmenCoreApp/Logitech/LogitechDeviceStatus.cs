namespace OmenCore.Logitech
{
    public class LogitechDeviceStatus
    {
        public int BatteryPercent { get; set; }
        public int Dpi { get; set; }
        public int MaxDpi { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = "USB";
        public int BrightnessPercent { get; set; } = 80;
    }
}
