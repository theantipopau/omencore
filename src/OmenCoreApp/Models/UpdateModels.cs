using System;

namespace OmenCore.Models
{
    /// <summary>
    /// Represents version information for the application
    /// </summary>
    public class VersionInfo
    {
        public Version Version { get; set; } = new Version(1, 0, 0);
        public string VersionString => Version.ToString();
        public DateTime ReleaseDate { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
        public string ChangelogUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public bool IsMandatory { get; set; }
        public string MinimumRequiredVersion { get; set; } = "1.0.0";
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Update check result
    /// </summary>
    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public VersionInfo? LatestVersion { get; set; }
        public VersionInfo CurrentVersion { get; set; } = new VersionInfo();
        public string Message { get; set; } = string.Empty;
        public UpdateStatus Status { get; set; }
    }

    public enum UpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        UpdateRequired,
        CheckFailed,
        NetworkError
    }

    /// <summary>
    /// Update download progress
    /// </summary>
    public class UpdateDownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercent => TotalBytes > 0 ? (BytesDownloaded * 100.0 / TotalBytes) : 0;
        public string StatusMessage { get; set; } = "Downloading...";
        public double DownloadSpeedMbps { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
    }

    /// <summary>
    /// Update installation result
    /// </summary>
    public class UpdateInstallResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string InstallerPath { get; set; } = string.Empty;
        public bool RequiresRestart { get; set; }
    }
}
