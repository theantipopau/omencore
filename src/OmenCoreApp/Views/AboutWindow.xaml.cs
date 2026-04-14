using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using OmenCore.Services;
using OmenCore.Utils;

namespace OmenCore.Views
{
    public partial class AboutWindow : Window
    {
        private readonly AutoUpdateService _updateService;
        private bool _isCheckingUpdates;

        public AboutWindow()
        {
            InitializeComponent();
            _updateService = new AutoUpdateService(App.Logging, "https://api.github.com/repos/theantipopau/omencore/releases/latest");
            VersionText.Text = $"Version {AppVersionProvider.GetVersionString()}";
        }

        private async void CheckUpdatesClicked(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdates) return;
            
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            SetStatus("Checking for updates...", "TextSecondaryBrush");
            
            try
            {
                var result = await _updateService.CheckForUpdatesAsync();
                
                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    var latest = result.LatestVersion;
                    SetStatus($"Update available: v{latest.VersionString}", "SuccessBrush");

                    if (string.IsNullOrWhiteSpace(latest.DownloadUrl))
                    {
                        var openRelease = MessageBox.Show(
                            "A new version is available on GitHub. Open the release page now?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (openRelease == MessageBoxResult.Yes)
                        {
                            OpenUrl(latest.ChangelogUrl);
                        }

                        return;
                    }

                    var msgResult = MessageBox.Show(
                        $"A new version is available!\n\nCurrent: {result.CurrentVersion.VersionString}\nLatest: {latest.VersionString}\n\nWould you like to download and install it now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (msgResult == MessageBoxResult.Yes)
                    {
                        SetStatus("Downloading update...", "TextSecondaryBrush");
                        var installerPath = await _updateService.DownloadUpdateAsync(latest);

                        if (installerPath != null)
                        {
                            SetStatus("Installing update...", "TextSecondaryBrush");
                            await _updateService.InstallUpdateAsync(installerPath);
                        }
                        else
                        {
                                SetStatus("Download failed. Open GitHub releases to install manually.", "AccentBrush");
                                if (!string.IsNullOrWhiteSpace(latest.ChangelogUrl))
                                {
                                    var openGitHub = MessageBox.Show(
                                        "The update could not be downloaded or verified.\n\nOpen GitHub releases to download manually?",
                                        "Update Download Failed",
                                        MessageBoxButton.YesNo,
                                        MessageBoxImage.Warning);
                                    if (openGitHub == MessageBoxResult.Yes)
                                        OpenUrl(latest.ChangelogUrl);
                                }
                        }
                    }
                }
                else
                {
                    SetStatus(result.Message, "TextSecondaryBrush");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Update check failed: {ex.Message}", "AccentBrush");
            }
            finally
            {
                _isCheckingUpdates = false;
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void CloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void SetStatus(string message, string brushResourceKey)
        {
            UpdateStatusText.Text = message;
            if (FindResource(brushResourceKey) is Brush brush)
            {
                UpdateStatusText.Foreground = brush;
            }
        }

        private void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
