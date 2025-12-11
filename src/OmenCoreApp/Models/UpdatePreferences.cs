using System;

namespace OmenCore.Models
{
    public class UpdatePreferences
    {
        /// <summary>
        /// Check for updates when application starts
        /// </summary>
        public bool CheckOnStartup { get; set; } = true;
        
        /// <summary>
        /// Enable automatic periodic update checks
        /// </summary>
        public bool AutoCheckEnabled { get; set; } = true;
        
        /// <summary>
        /// Interval between automatic update checks (in hours)
        /// </summary>
        public int CheckIntervalHours { get; set; } = 12;
        
        /// <summary>
        /// Last time an update check was performed
        /// </summary>
        public DateTime? LastCheckTime { get; set; }
        
        /// <summary>
        /// Version number the user chose to skip
        /// </summary>
        public string? SkippedVersion { get; set; }
        
        /// <summary>
        /// Show notification when update is available
        /// </summary>
        public bool ShowUpdateNotifications { get; set; } = true;
    }
}
