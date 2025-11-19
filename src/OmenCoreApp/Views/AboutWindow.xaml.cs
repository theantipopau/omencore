using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using OmenCore.Services;

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
            VersionText.Text = $"Version {_updateService.GetCurrentVersion()}";
        }

        private async void CheckUpdatesClicked(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdates) return;
            
            _isCheckingUpdates = true;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates...";
            UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            
            try
            {
                var result = await _updateService.CheckForUpdatesAsync();
                
                if (result.UpdateAvailable && result.LatestVersion != null)
                {
                    UpdateStatusText.Text = $"Update available: v{result.LatestVersion.VersionString}";
                    UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                    
                    var msgResult = MessageBox.Show(
                        $"A new version is available!\n\nCurrent: {result.CurrentVersion.VersionString}\nLatest: {result.LatestVersion.VersionString}\n\nWould you like to download and install it now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                        
                    if (msgResult == MessageBoxResult.Yes)
                    {
                        UpdateStatusText.Text = "Downloading update...";
                        var installerPath = await _updateService.DownloadUpdateAsync(result.LatestVersion);
                        
                        if (installerPath != null)
                        {
                            UpdateStatusText.Text = "Installing update...";
                            await _updateService.InstallUpdateAsync(installerPath);
                        }
                        else
                        {
                            UpdateStatusText.Text = "Download failed.";
                            UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                        }
                    }
                }
                else
                {
                    UpdateStatusText.Text = result.Message;
                    UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"Update check failed: {ex.Message}";
                UpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
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
    }
}
