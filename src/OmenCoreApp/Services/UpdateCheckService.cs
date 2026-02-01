using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Non-intrusive auto-update checker using GitHub Releases API.
    /// Only checks once per session, respects user privacy.
    /// </summary>
    public class UpdateCheckService
    {
        private readonly LoggingService _logging;
        private readonly HttpClient _httpClient;
        private const string GitHubApiUrl = "https://api.github.com/repos/theantipopau/omencore/releases/latest";
        private const string CurrentVersion = "2.6.0";

        private bool _hasChecked = false;
        private UpdateInfo? _latestUpdate;

        public UpdateCheckService(LoggingService logging)
        {
            _logging = logging;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OmenCore-UpdateChecker");
        }

        /// <summary>
        /// Check for updates (only once per session)
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            if (_hasChecked)
            {
                return _latestUpdate;
            }

            try
            {
                _hasChecked = true;

                var response = await _httpClient.GetStringAsync(GitHubApiUrl);
                var release = JsonSerializer.Deserialize<GitHubRelease>(response);

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    _logging.Warn("Update check returned invalid response");
                    return null;
                }

                var latestVersion = ParseVersion(release.TagName);
                var currentVersion = ParseVersion(CurrentVersion);

                if (latestVersion > currentVersion)
                {
                    _latestUpdate = new UpdateInfo
                    {
                        Version = release.TagName,
                        ReleaseUrl = release.HtmlUrl ?? "https://github.com/theantipopau/omencore/releases",
                        PublishedAt = release.PublishedAt,
                        ReleaseNotes = release.Body ?? "View release notes on GitHub"
                    };

                    _logging.Info($"🔔 Update available: {CurrentVersion} → {release.TagName}");
                    return _latestUpdate;
                }

                _logging.Info($"✓ OmenCore is up to date ({CurrentVersion})");
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logging.Warn($"Update check failed (offline?): {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _logging.Error($"Update check error: {ex.Message}", ex);
                return null;
            }
        }

        private Version ParseVersion(string versionString)
        {
            // Handle v-prefixed versions like "v2.3.0"
            var cleaned = versionString.TrimStart('v');

            // Try to parse as Version
            if (Version.TryParse(cleaned, out var version))
            {
                return version;
            }

            // Fallback: assume 0.0.0
            return new Version(0, 0, 0);
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public string ReleaseNotes { get; set; } = "";
    }

    // GitHub API release response
    internal class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}
