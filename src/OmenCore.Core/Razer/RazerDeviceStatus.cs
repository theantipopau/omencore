namespace OmenCore.Razer
{
    public class RazerDeviceStatus
    {
        public int BatteryPercent { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = "USB";
        public bool IsConnected { get; set; } = true;
        
        /// <summary>
        /// Returns a user-friendly status string for display in UI.
        /// </summary>
        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            
            if (!string.IsNullOrEmpty(ConnectionType))
                parts.Add(ConnectionType);
            
            if (BatteryPercent > 0)
                parts.Add($"{BatteryPercent}% Battery");
            
            if (!string.IsNullOrEmpty(FirmwareVersion))
                parts.Add($"FW {FirmwareVersion}");

            return parts.Count > 0 ? string.Join(" • ", parts) : (IsConnected ? "Connected" : "Disconnected");
        }
    }
}
