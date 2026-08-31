namespace OmenCore.Corsair
{
    public class CorsairDeviceStatus
    {
        public int BatteryPercent { get; set; }
        public int PollingRateHz { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = "USB";
        
        /// <summary>
        /// Additional notes or limitations for this device (e.g., "Wireless mouse connects through this receiver")
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Returns a user-friendly status string for display in UI.
        /// </summary>
        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            
            if (!string.IsNullOrEmpty(ConnectionType))
                parts.Add(ConnectionType);
            
            if (PollingRateHz > 0)
                parts.Add($"{PollingRateHz}Hz");
            
            if (BatteryPercent > 0)
                parts.Add($"{BatteryPercent}% Battery");
            
            if (!string.IsNullOrEmpty(FirmwareVersion))
                parts.Add($"FW {FirmwareVersion}");

            return parts.Count > 0 ? string.Join(" • ", parts) : "Connected";
        }
    }
}
