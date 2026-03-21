using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmenCore.Desktop.Views;

public partial class SettingsView : UserControl
{
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsView()
    {
        InitializeComponent();
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        var (configDirectory, configFile) = GetSettingsPaths();
        ConfigPathText.Text = configDirectory;
        VersionText.Text = "Version 3.2.0";

        if (!File.Exists(configFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(configFile);
            var settings = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);
            if (settings == null)
            {
                return;
            }

            StartWithSystemToggle.IsChecked = settings.StartWithSystem;
            StartMinimizedToggle.IsChecked = settings.StartMinimized;
            CloseToTrayToggle.IsChecked = settings.CloseToTray;
            CheckUpdatesToggle.IsChecked = settings.CheckUpdates;
            PollingIntervalCombo.SelectedIndex = Math.Clamp(settings.PollingInterval, 0, 3);
            ApplyFanOnStartToggle.IsChecked = settings.ApplyFanOnStart;
            ApplyLightingOnStartToggle.IsChecked = settings.ApplyLightingOnStart;
            ThemeCombo.SelectedIndex = Math.Clamp(settings.Theme, 0, 2);
            DebugLoggingToggle.IsChecked = settings.DebugLogging;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
    }
    
    private void AccentColor_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorHex)
        {
            // TODO: Apply accent color to theme
            System.Diagnostics.Debug.WriteLine($"Accent color selected: {colorHex}");
        }
    }
    
    private void ReinstallDriver_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Trigger driver reinstallation
        System.Diagnostics.Debug.WriteLine("Reinstall driver clicked");
    }
    
    private void OpenConfigFolder_Click(object? sender, RoutedEventArgs e)
    {
        // Open config folder in file manager
        var configPath = Environment.OSVersion.Platform == PlatformID.Unix
            ? Environment.GetEnvironmentVariable("HOME") + "/.config/omencore"
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\OmenCore";
        
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open config folder: {ex.Message}");
        }
    }
    
    private void OpenGitHub_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/theantipopau/omencore");
    }
    
    private void OpenIssues_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/theantipopau/omencore/issues");
    }
    
    private void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        _ = CheckUpdatesAsync();
    }
    
    private void ResetSettings_Click(object? sender, RoutedEventArgs e)
    {
        // Reset all toggles and combos to defaults
        StartWithSystemToggle.IsChecked = false;
        StartMinimizedToggle.IsChecked = false;
        CloseToTrayToggle.IsChecked = true;
        CheckUpdatesToggle.IsChecked = true;
        PollingIntervalCombo.SelectedIndex = 1;
        ApplyFanOnStartToggle.IsChecked = true;
        ApplyLightingOnStartToggle.IsChecked = true;
        ThemeCombo.SelectedIndex = 0;
        DebugLoggingToggle.IsChecked = false;
    }
    
    private void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var (configDirectory, configFile) = GetSettingsPaths();
            Directory.CreateDirectory(configDirectory);

            var settings = new SettingsData
            {
                StartWithSystem = StartWithSystemToggle.IsChecked ?? false,
                StartMinimized = StartMinimizedToggle.IsChecked ?? false,
                CloseToTray = CloseToTrayToggle.IsChecked ?? true,
                CheckUpdates = CheckUpdatesToggle.IsChecked ?? true,
                PollingInterval = PollingIntervalCombo.SelectedIndex,
                ApplyFanOnStart = ApplyFanOnStartToggle.IsChecked ?? true,
                ApplyLightingOnStart = ApplyLightingOnStartToggle.IsChecked ?? true,
                Theme = ThemeCombo.SelectedIndex,
                DebugLogging = DebugLoggingToggle.IsChecked ?? false
            };

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(configFile, json);
            System.Diagnostics.Debug.WriteLine($"Saved settings to {configFile}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/theantipopau/omencore/releases/latest");
            request.Headers.UserAgent.ParseAdd("OmenCore-Desktop");

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {(int)response.StatusCode}");
                return;
            }

            var payload = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("tag_name", out var tag))
            {
                var latestTag = tag.GetString();
                if (!string.IsNullOrWhiteSpace(latestTag))
                {
                    VersionText.Text = $"Version 3.2.0 (latest: {latestTag})";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    private static (string configDirectory, string configFile) GetSettingsPaths()
    {
        var configDirectory = Environment.OSVersion.Platform == PlatformID.Unix
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "omencore")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenCore");

        var configFile = Path.Combine(configDirectory, "settings.desktop.json");
        return (configDirectory, configFile);
    }
    
    private static void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public bool StartWithSystem { get; set; }
        public bool StartMinimized { get; set; }
        public bool CloseToTray { get; set; } = true;
        public bool CheckUpdates { get; set; } = true;
        public int PollingInterval { get; set; } = 1;
        public bool ApplyFanOnStart { get; set; } = true;
        public bool ApplyLightingOnStart { get; set; } = true;
        public int Theme { get; set; }
        public bool DebugLogging { get; set; }
    }
}
