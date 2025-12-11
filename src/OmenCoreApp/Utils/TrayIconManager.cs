using System;
using Hardcodet.Wpf.TaskbarNotification;

namespace OmenCore.Utils
{
    /// <summary>
    /// Manages the system tray icon.
    /// Future enhancement: Add live temperature monitoring when services are fully DI-integrated.
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private readonly TaskbarIcon _trayIcon;
        private bool _disposed;

        public TrayIconManager(TaskbarIcon trayIcon)
        {
            _trayIcon = trayIcon;
            _trayIcon.ToolTipText = "OmenCore - Gaming Laptop Control";
        }

        /// <summary>
        /// Updates the tray tooltip with current hardware status.
        /// </summary>
        public void UpdateStatus(string cpuInfo, string gpuInfo)
        {
            if (_disposed) return;

            try
            {
                _trayIcon.ToolTipText = $"OmenCore - Gaming Laptop Control\n{cpuInfo}\n{gpuInfo}";
            }
            catch (Exception ex)
            {
                App.Logging.Warn($"Failed to update tray tooltip: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
