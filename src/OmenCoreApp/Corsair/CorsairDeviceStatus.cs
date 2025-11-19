namespace OmenCore.Corsair
{
    public class CorsairDeviceStatus
    {
        public int BatteryPercent { get; set; }
        public int PollingRateHz { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = "USB";
    }
}
