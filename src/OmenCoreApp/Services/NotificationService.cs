using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using OmenCore.Utils;
using Windows.UI.Notifications;

namespace OmenCore.Services
{
    /// <summary>
    /// Service for showing Windows toast notifications
    /// </summary>
    public class NotificationService : IDisposable
    {
        private readonly LoggingService _logging;
        private bool _isEnabled = true;
        private bool _showGameNotifications = true;
        private bool _showModeChangeNotifications = true;
        private bool _showTemperatureWarnings = true;
        private bool _showUpdateNotifications = true;
        
        private readonly string _altIconPath;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                _logging.Info($"Notifications {(_isEnabled ? "enabled" : "disabled")}");
            }
        }

        public bool ShowGameNotifications
        {
            get => _showGameNotifications;
            set => _showGameNotifications = value;
        }

        public bool ShowModeChangeNotifications
        {
            get => _showModeChangeNotifications;
            set => _showModeChangeNotifications = value;
        }

        public bool ShowTemperatureWarnings
        {
            get => _showTemperatureWarnings;
            set => _showTemperatureWarnings = value;
        }

        public bool ShowUpdateNotifications
        {
            get => _showUpdateNotifications;
            set => _showUpdateNotifications = value;
        }

        public NotificationService(LoggingService logging)
        {
            _logging = logging;

            // Get icon paths
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _altIconPath = Path.Combine(appDir, "Assets", "omen-alt.png");

            // Opt-out for automated runs. ThermalProtectionTests constructs a real NotificationService
            // and drives the thermal check with a mock 95 C, so running the suite fires genuine Windows
            // emergency-temperature toasts on the developer's own machine - alarming, and about
            // temperatures that do not exist. No hardware is involved either way; only the toast is
            // real. This mirrors OMENCORE_DISABLE_FILE_LOG, which CI already sets for the same class of
            // reason, and keeps the opt-out here rather than in the thirteen test files that construct
            // this service.
            if (Environment.GetEnvironmentVariable("OMENCORE_DISABLE_NOTIFICATIONS") == "1")
            {
                _isEnabled = false;
                _logging.Info("NotificationService initialized (disabled by OMENCORE_DISABLE_NOTIFICATIONS)");
                return;
            }

            _logging.Info("NotificationService initialized");
        }

        /// <summary>
        /// Show a game profile activated notification
        /// </summary>
        public void ShowGameProfileActivated(string gameName, string profileName)
        {
            if (!_isEnabled || !_showGameNotifications) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText("Game Profile Activated")
                    .AddText($"{gameName}")
                    .AddText($"Applied profile: {profileName}")
                    .SetToastDuration(ToastDuration.Short);
                
                if (File.Exists(_altIconPath))
                {
                    builder.AddAppLogoOverride(new Uri(_altIconPath), ToastGenericAppLogoCrop.Circle);
                }

                builder.Show();
                AddInfo("Game Profile Activated", $"{gameName}: {profileName}");
                _logging.Info($"Notification: Game profile '{profileName}' activated for '{gameName}'");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a game exited notification
        /// </summary>
        public void ShowGameProfileDeactivated(string gameName)
        {
            if (!_isEnabled || !_showGameNotifications) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("Game Closed")
                    .AddText($"{gameName}")
                    .AddText("Restored default settings")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddInfo("Game Closed", $"{gameName}: restored default settings");
                _logging.Info($"Notification: Game '{gameName}' closed, defaults restored");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a fan mode changed notification
        /// </summary>
        public void ShowFanModeChanged(string modeName, string triggeredBy = "Hotkey")
        {
            if (!_isEnabled || !_showModeChangeNotifications) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"Fan Mode: {modeName}")
                    .AddText($"Changed via {triggeredBy}")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddInfo($"Fan Mode: {modeName}", $"Changed via {triggeredBy}");
                _logging.Info($"Notification: Fan mode changed to '{modeName}'");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a memory-clean completion notification. Callers should skip trivial/no-op frees
        /// (see MemoryOptimizerViewModel) so a background auto-clean firing every few minutes
        /// doesn't turn into a stream of toasts for amounts too small to matter.
        /// </summary>
        public void ShowMemoryCleaned(long freedMB, int operationsSucceeded)
        {
            if (!_isEnabled) return;

            try
            {
                var freedDisplay = freedMB >= 1024 ? $"{freedMB / 1024.0:F1} GB" : $"{freedMB} MB";

                new ToastContentBuilder()
                    .AddText($"Freed {freedDisplay}")
                    .AddText($"{operationsSucceeded} cleanup operation{(operationsSucceeded == 1 ? "" : "s")} completed")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddInfo($"Freed {freedDisplay}", $"{operationsSucceeded} cleanup operation{(operationsSucceeded == 1 ? "" : "s")} completed");
                _logging.Info($"Notification: memory clean freed {freedMB} MB ({operationsSucceeded} operations)");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a performance mode changed notification
        /// </summary>
        public void ShowPerformanceModeChanged(string modeName, string triggeredBy = "Hotkey")
        {
            if (!_isEnabled || !_showModeChangeNotifications) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"Performance: {modeName}")
                    .AddText($"Changed via {triggeredBy}")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddInfo($"Performance: {modeName}", $"Changed via {triggeredBy}");
                _logging.Info($"Notification: Performance mode changed to '{modeName}'");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a temperature warning notification
        /// </summary>
        public void ShowTemperatureWarning(string component, double temperature, double threshold)
        {
            if (!_isEnabled || !_showTemperatureWarnings) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"High Temperature Warning")
                    .AddText($"{component}: {temperature:F0}°C")
                    .AddText($"Threshold: {threshold:F0}°C")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();

                AddWarning("High Temperature Warning", $"{component}: {temperature:F0}°C (threshold {threshold:F0}°C)");
                _logging.Info($"Notification: Temperature warning - {component} at {temperature}°C");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// The adapter's GPU power clamp has come back after being dropped.
        ///
        /// Announces rather than acts. Dropping the clamp again means restarting the display
        /// device, which destroys every graphics and compute context on it - and the only way this
        /// can be noticed at all is by reading a GPU that is awake, which means something is using
        /// it. Taking that away from whoever is using it, because a number changed, is not a
        /// decision to make on their behalf.
        /// </summary>
        public void ShowAdapterClampReturned(double enforcedWatts, double defaultWatts)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("GPU power clamp is back")
                    .AddText($"The GPU is limited to {enforcedWatts:F0} W again, of its own {defaultWatts:F0} W.")
                    .AddText("Restart the GPU from Diagnostics when nothing needs it.")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();

                AddWarning("GPU power clamp is back",
                    $"Limited to {enforcedWatts:F0} W of {defaultWatts:F0} W. The firmware told the " +
                    "driver about the adapter again - restarting the GPU drops it.");

                _logging.Info($"Notification: adapter GPU clamp returned - enforced {enforcedWatts:F2} W " +
                              $"against a {defaultWatts:F2} W default");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a critical temperature notification (throttling imminent)
        /// </summary>
        public void ShowCriticalTemperature(string component, double temperature)
        {
            if (!_isEnabled) return; // Always show critical warnings if enabled

            try
            {
                new ToastContentBuilder()
                    .AddText($"CRITICAL: {component} Overheating!")
                    .AddText($"Temperature: {temperature:F0}°C")
                    .AddText("Thermal throttling may occur. Consider reducing load or improving cooling.")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();

                AddError($"CRITICAL: {component} Overheating!", $"{temperature:F0}°C — thermal throttling may occur");
                _logging.Warn($"Critical temperature warning - {component} at {temperature}°C");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show thermal protection activated notification
        /// </summary>
        public void ShowThermalProtectionActivated(double temperature, string protectionLevel)
        {
            if (!_isEnabled || !_showTemperatureWarnings) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"Thermal Protection: {protectionLevel}")
                    .AddText($"{temperature:F0}°C - Fans boosted to max")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddWarning($"Thermal Protection: {protectionLevel}", $"{temperature:F0}°C — fans boosted to max");
                _logging.Info($"Thermal protection notification: {protectionLevel} at {temperature}°C");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a driver issue notification
        /// </summary>
        public void ShowDriverIssue(string driverName, string issue)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"Driver Issue: {driverName}")
                    .AddText(issue)
                    .AddText("Some features may be unavailable.")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();

                AddWarning($"Driver Issue: {driverName}", $"{issue} — some features may be unavailable");
                _logging.Warn($"Driver issue notification: {driverName} - {issue}");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a fan failure or anomaly notification
        /// </summary>
        public void ShowFanAlert(string message, bool isCritical = false)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("Fan Alert")
                    .AddText(message)
                    .SetToastDuration(isCritical ? ToastDuration.Long : ToastDuration.Short)
                    .Show();

                AddInAppNotification(isCritical ? InAppNotificationType.Error : InAppNotificationType.Warning, "Fan Alert", message);
                _logging.Warn($"Fan alert: {message}");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show EC bridge unavailable notification
        /// </summary>
        public void ShowEcBridgeUnavailable()
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("EC Bridge Unavailable")
                    .AddText("Fan control features are disabled.")
                    .AddText("Install LibreHardwareMonitor to enable EC access.")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();

                AddWarning("EC Bridge Unavailable", "Fan control features are disabled. Install LibreHardwareMonitor to enable EC access.");
                _logging.Warn("EC bridge unavailable notification shown");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show power profile changed notification
        /// </summary>
        public void ShowPowerProfileChanged(string profileName, bool isOnBattery)
        {
            if (!_isEnabled || !_showModeChangeNotifications) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"Power Profile: {profileName}")
                    .AddText(isOnBattery ? "Running on battery" : "Connected to AC power")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();

                AddInfo($"Power Profile: {profileName}", isOnBattery ? "Running on battery" : "Connected to AC power");
                _logging.Info($"Power profile changed: {profileName}");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show an update available notification
        /// </summary>
        public void ShowUpdateAvailable(string currentVersion, string newVersion, string releaseNotes = "")
        {
            if (!_isEnabled || !_showUpdateNotifications) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText("Update Available")
                    .AddText($"OmenCore {newVersion} is ready")
                    .AddText($"Current version: {currentVersion}");

                if (!string.IsNullOrEmpty(releaseNotes))
                {
                    builder.AddText(releaseNotes);
                }

                builder.AddButton(new ToastButton()
                    .SetContent("View Update")
                    .AddArgument("action", "viewUpdate"));

                builder.AddButton(new ToastButton()
                    .SetContent("Later")
                    .AddArgument("action", "dismiss"));

                builder.Show();
                AddInAppNotification(InAppNotificationType.Update, "Update Available", $"OmenCore {newVersion} is ready (current: {currentVersion})");
                _logging.Info($"Notification: Update available - {newVersion}");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a generic info notification
        /// </summary>
        public void ShowInfo(string title, string message)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
                AddInfo(title, message);
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a success notification
        /// </summary>
        public void ShowSuccess(string title, string message)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"{title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
                AddSuccess(title, message);
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show an error notification
        /// </summary>
        public void ShowError(string title, string message)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"{title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
                AddError(title, message);
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a warning notification
        /// </summary>
        public void ShowWarning(string title, string message)
        {
            if (!_isEnabled) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"{title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
                AddWarning(title, message);
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a hotkey triggered notification (brief)
        /// </summary>
        public void ShowHotkeyTriggered(string action)
        {
            if (!_isEnabled || !_showModeChangeNotifications) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"⌨{action}")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all notifications from the action center
        /// </summary>
        public void ClearAllNotifications()
        {
            try
            {
                ToastNotificationManagerCompat.History.Clear();
                _inAppNotifications.Clear();
                InAppNotificationsCleared?.Invoke(this, EventArgs.Empty);
                UnreadCountChanged?.Invoke(this, EventArgs.Empty);
                _logging.Info("Cleared all notifications");
            }
            catch (Exception ex)
            {
                _logging.Info($"Failed to clear notifications: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                // Unregister the app from toast notifications
                ToastNotificationManagerCompat.Uninstall();
            }
            catch (Exception ex)
            {
                _logging.Debug($"Toast notification unregister failed during dispose: {ex.Message}");
            }
        }
        
        #region In-App Notification Center
        
        private readonly ObservableCollection<InAppNotification> _inAppNotifications = new();
        private const int MaxInAppNotifications = 50;
        
        public event EventHandler<InAppNotification>? InAppNotificationAdded;
        public event EventHandler? InAppNotificationsCleared;
        public event EventHandler? UnreadCountChanged;
        
        public ReadOnlyObservableCollection<InAppNotification> InAppNotifications => 
            new(_inAppNotifications);
        
        public int UnreadCount => _inAppNotifications.Count(n => !n.IsRead);
        public bool HasUnread => UnreadCount > 0;
        
        /// <summary>
        /// Add an in-app notification.
        /// </summary>
        public void AddInAppNotification(InAppNotificationType type, string title, string message, string? actionTarget = null)
        {
            DispatcherHelper.RunOnUiThread(() =>
            {
                var notification = new InAppNotification
                {
                    Id = Guid.NewGuid(),
                    Type = type,
                    Title = title,
                    Message = message,
                    ActionTarget = actionTarget,
                    Timestamp = DateTime.Now,
                    IsRead = false
                };
                
                _inAppNotifications.Insert(0, notification);
                
                // Trim old notifications
                while (_inAppNotifications.Count > MaxInAppNotifications)
                {
                    _inAppNotifications.RemoveAt(_inAppNotifications.Count - 1);
                }
                
                InAppNotificationAdded?.Invoke(this, notification);
                UnreadCountChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        
        /// <summary>
        /// Add info notification to in-app center.
        /// </summary>
        public void AddInfo(string title, string message, string? actionTarget = null)
        {
            AddInAppNotification(InAppNotificationType.Info, title, message, actionTarget);
        }
        
        /// <summary>
        /// Add success notification to in-app center.
        /// </summary>
        public void AddSuccess(string title, string message, string? actionTarget = null)
        {
            AddInAppNotification(InAppNotificationType.Success, title, message, actionTarget);
        }
        
        /// <summary>
        /// Add warning notification to in-app center.
        /// </summary>
        public void AddWarning(string title, string message, string? actionTarget = null)
        {
            AddInAppNotification(InAppNotificationType.Warning, title, message, actionTarget);
        }
        
        /// <summary>
        /// Add error notification to in-app center.
        /// </summary>
        public void AddError(string title, string message, string? actionTarget = null)
        {
            AddInAppNotification(InAppNotificationType.Error, title, message, actionTarget);
        }
        
        /// <summary>
        /// Mark a notification as read.
        /// </summary>
        public void MarkAsRead(Guid notificationId)
        {
            var notification = _inAppNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                UnreadCountChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        /// <summary>
        /// Mark all notifications as read.
        /// </summary>
        public void MarkAllAsRead()
        {
            foreach (var notification in _inAppNotifications)
            {
                notification.IsRead = true;
            }
            UnreadCountChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Dismiss a specific notification.
        /// </summary>
        public void DismissNotification(Guid notificationId)
        {
            DispatcherHelper.RunOnUiThread(() =>
            {
                var notification = _inAppNotifications.FirstOrDefault(n => n.Id == notificationId);
                if (notification != null)
                {
                    _inAppNotifications.Remove(notification);
                    UnreadCountChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }
        
        /// <summary>
        /// Get recent notifications for display.
        /// </summary>
        public IEnumerable<InAppNotification> GetRecentNotifications(int count = 5)
        {
            return _inAppNotifications.Take(count);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents an in-app notification for the notification center.
    /// </summary>
    public class InAppNotification
    {
        public Guid Id { get; set; }
        public InAppNotificationType Type { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? ActionTarget { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsRead { get; set; }
        
        public string TimeAgo
        {
            get
            {
                var elapsed = DateTime.Now - Timestamp;
                if (elapsed.TotalMinutes < 1) return "Just now";
                if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
                if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
                if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
                return Timestamp.ToString("MMM d");
            }
        }
        
    }
    
    /// <summary>
    /// Types of in-app notifications.
    /// </summary>
    public enum InAppNotificationType
    {
        Info,
        Success,
        Warning,
        Error,
        Update
    }
}
