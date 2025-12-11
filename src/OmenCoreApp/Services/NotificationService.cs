using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
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
        
        private readonly string _appIconPath;
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
            _appIconPath = Path.Combine(appDir, "Assets", "omen-icon.ico");
            _altIconPath = Path.Combine(appDir, "Assets", "omen-alt.png");
            
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
                    .AddText("🎮 Game Profile Activated")
                    .AddText($"{gameName}")
                    .AddText($"Applied profile: {profileName}")
                    .SetToastDuration(ToastDuration.Short);
                
                if (File.Exists(_altIconPath))
                {
                    builder.AddAppLogoOverride(new Uri(_altIconPath), ToastGenericAppLogoCrop.Circle);
                }

                builder.Show();
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
                    .AddText("🎮 Game Closed")
                    .AddText($"{gameName}")
                    .AddText("Restored default settings")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
                    
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
                string icon = modeName.ToLower() switch
                {
                    "performance" or "boost" => "🔥",
                    "quiet" or "silent" => "🤫",
                    "balanced" or "auto" => "⚖️",
                    "max" or "turbo" => "🚀",
                    _ => "🌀"
                };

                new ToastContentBuilder()
                    .AddText($"{icon} Fan Mode: {modeName}")
                    .AddText($"Changed via {triggeredBy}")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
                    
                _logging.Info($"Notification: Fan mode changed to '{modeName}'");
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
                string icon = modeName.ToLower() switch
                {
                    "performance" => "⚡",
                    "balanced" => "⚖️",
                    "quiet" or "power saver" => "🔋",
                    _ => "💻"
                };

                new ToastContentBuilder()
                    .AddText($"{icon} Performance: {modeName}")
                    .AddText($"Changed via {triggeredBy}")
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
                    
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
                    .AddText($"🌡️ High Temperature Warning")
                    .AddText($"{component}: {temperature:F0}°C")
                    .AddText($"Threshold: {threshold:F0}°C")
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
                    
                _logging.Info($"Notification: Temperature warning - {component} at {temperature}°C");
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
                    .AddText("🔄 Update Available")
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
                    .AddText($"✅ {title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
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
                    .AddText($"❌ {title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
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
                    .AddText($"⚠️ {title}")
                    .AddText(message)
                    .SetToastDuration(ToastDuration.Long)
                    .Show();
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
                    .AddText($"⌨️ {action}")
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
            catch { }
        }
    }
}
