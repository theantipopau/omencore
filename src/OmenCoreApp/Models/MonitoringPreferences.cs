namespace OmenCore.Models
{
    public class MonitoringPreferences
    {
        public int PollIntervalMs { get; set; } = 1500;
        public int HistoryCount { get; set; } = 120;
        public bool LowOverheadMode { get; set; }
    }
}
