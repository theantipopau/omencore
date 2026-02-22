namespace OmenCore.Models
{
    public class MonitoringPreferences
    {
        public int PollIntervalMs { get; set; } = 1000;  // 1 second for responsive temp updates
        public int HistoryCount { get; set; } = 120;
        public bool LowOverheadMode { get; set; }
        
        // Hotkey and notification settings
        public bool HotkeysEnabled { get; set; } = true;
        
        /// <summary>
        /// When true, global hotkeys are only active while the main window has focus.
        /// This avoids conflicts with other applications when using common shortcuts.
        /// Default: true (window-focused). Setting to false reverts to the legacy global behavior.
        /// </summary>
        public bool WindowFocusedHotkeys { get; set; } = true;

        public bool NotificationsEnabled { get; set; } = true;
        public bool GameNotificationsEnabled { get; set; } = true;
        public bool ModeChangeNotificationsEnabled { get; set; } = true;
        public bool TemperatureWarningsEnabled { get; set; } = true;
        
        // UI preferences
        public bool StartMinimized { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
    }
}
