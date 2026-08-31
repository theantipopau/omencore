using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Result of a BIOS update check
    /// </summary>
    public class BiosUpdateResult
    {
        public string CurrentBiosVersion { get; set; } = string.Empty;
        public string CurrentBiosDate { get; set; } = string.Empty;
        public string? LatestBiosVersion { get; set; }
        public string? LatestBiosDate { get; set; }
        public bool UpdateAvailable { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? FileSize { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CheckTimestamp { get; set; }
    }

    /// <summary>
    /// BIOS information from HP
    /// </summary>
    public class BiosInfo
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? FileSize { get; set; }
        public string? SoftpaqNumber { get; set; }
    }

    /// <summary>
    /// Service to check for HP BIOS updates using the HP Support API.
    /// </summary>
    public class BiosUpdateService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public BiosUpdateService(LoggingService logging)
        {
            _logging = logging;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OmenCore/1.1 (Windows NT; HP BIOS Check)");
        }

        /// <summary>
        /// Check for BIOS updates for the given system
        /// </summary>
        public async Task<BiosUpdateResult> CheckForUpdatesAsync(SystemInfo systemInfo)
        {
            var result = new BiosUpdateResult
            {
                CurrentBiosVersion = systemInfo.BiosVersion,
                CurrentBiosDate = systemInfo.BiosDate,
                CheckTimestamp = DateTime.Now
            };

            if (string.IsNullOrEmpty(systemInfo.SystemSku) && string.IsNullOrEmpty(systemInfo.ProductName))
            {
                result.Message = "Unable to determine HP product ID for BIOS lookup";
                _logging.Warn(result.Message);
                return result;
            }

            try
            {
                // Use Model for display (full name like "HP OMEN by HP Laptop 17-ck2xxx")
                // and ProductName for HP API lookup (baseboard ID like "8BAD")
                var displayName = !string.IsNullOrEmpty(systemInfo.Model) ? systemInfo.Model : systemInfo.ProductName;
                _logging.Info($"Checking BIOS updates for: {displayName} (SKU: {systemInfo.SystemSku})");

                // Try to get BIOS info from HP's softpaq catalog
                var biosInfo = await GetLatestBiosInfoAsync(systemInfo);

                if (biosInfo != null)
                {
                    result.LatestBiosVersion = biosInfo.Version;
                    result.LatestBiosDate = biosInfo.ReleaseDate;
                    result.DownloadUrl = biosInfo.DownloadUrl;
                    result.ReleaseNotes = biosInfo.ReleaseNotes;
                    result.FileSize = biosInfo.FileSize;

                    // Compare versions
                    result.UpdateAvailable = CompareBiosVersions(systemInfo.BiosVersion, biosInfo.Version);

                    if (result.UpdateAvailable)
                    {
                        result.Message = $"BIOS update available: {biosInfo.Version} (current: {systemInfo.BiosVersion})";
                        _logging.Info(result.Message);
                    }
                    else
                    {
                        result.Message = "Your BIOS is up to date";
                        _logging.Info($"BIOS is current: {systemInfo.BiosVersion}");
                    }
                }
                else
                {
                    // Construct helpful fallback message with support link
                    var supportUrl = ConstructSupportUrl(systemInfo);
                    result.Message = "HP's API doesn't provide direct BIOS lookup. Click 'Check HP Support' to visit your system's driver page.";
                    result.DownloadUrl = supportUrl; // Provide direct link to HP support page
                    _logging.Info($"BIOS check: API unavailable, providing support URL: {supportUrl}");
                }
            }
            catch (HttpRequestException ex)
            {
                result.Message = $"Network error checking for BIOS updates: {ex.Message}";
                _logging.Error(result.Message, ex);
            }
            catch (TaskCanceledException)
            {
                result.Message = "BIOS update check timed out";
                _logging.Warn(result.Message);
            }
            catch (Exception ex)
            {
                result.Message = $"Error checking for BIOS updates: {ex.Message}";
                _logging.Error(result.Message, ex);
            }

            return result;
        }

        /// <summary>
        /// Try to get the latest BIOS info from HP's softpaq catalog
        /// </summary>
        private async Task<BiosInfo?> GetLatestBiosInfoAsync(SystemInfo systemInfo)
        {
            // HP uses different approaches for BIOS lookup:
            // 1. Direct softpaq catalog API
            // 2. Product page scraping
            // 3. HP Support Assistant protocol

            // First, try the HP FTP catalog which has structured data
            var catalogResult = await TryHpCatalogLookupAsync(systemInfo);
            if (catalogResult != null) return catalogResult;

            // Fallback: construct HP support page URL
            var supportPageUrl = ConstructSupportUrl(systemInfo);
            _logging.Info($"HP Support page: {supportPageUrl}");

            return null;
        }

        /// <summary>
        /// Try to lookup BIOS from HP's softpaq catalog
        /// Note: HP's API requires complex authentication and product ID mapping.
        /// This attempts a basic lookup but may not always succeed.
        /// </summary>
        private async Task<BiosInfo?> TryHpCatalogLookupAsync(SystemInfo systemInfo)
        {
            try
            {
                // HP doesn't have a simple public API for BIOS lookups.
                // The most reliable approach is to use the serial number lookup
                // which returns product-specific driver information.
                
                if (!string.IsNullOrEmpty(systemInfo.SerialNumber))
                {
                    // Try HP's driver lookup API by serial number
                    var serialLookupUrl = $"https://support.hp.com/wcc-services/drivers/bySerial?sn={Uri.EscapeDataString(systemInfo.SerialNumber)}&cc=us&lang=en";
                    
                    try
                    {
                        var response = await _httpClient.GetAsync(serialLookupUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            
                            // Check if response is HTML (HP redirects to webpage for some queries)
                            if (content.TrimStart().StartsWith("<") || content.Contains("<!DOCTYPE"))
                            {
                                _logging.Info("HP serial lookup returned HTML (webpage redirect), not JSON - skipping parse");
                                // HP returned a webpage instead of JSON API response
                                // This happens when the serial lookup redirects to the support page
                            }
                            else
                            {
                                _logging.Info("HP serial lookup returned data, parsing BIOS info...");
                                
                                // Try to parse the JSON for BIOS softpaq
                                var biosInfo = ParseHpDriverResponse(content, systemInfo);
                                if (biosInfo != null)
                                {
                                    return biosInfo;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Info($"HP serial lookup failed: {ex.Message}");
                    }
                }
                
                // Try product ID based lookup
                var productId = ExtractProductId(systemInfo.SystemSku);
                if (string.IsNullOrEmpty(productId)) 
                {
                    // Try extracting from product name
                    productId = ExtractProductId(systemInfo.ProductName);
                }
                
                if (!string.IsNullOrEmpty(productId))
                {
                    // HP's product search API
                    var productUrl = $"https://support.hp.com/wcc-services/searchApi/products/en_US/{Uri.EscapeDataString(productId)}";
                    
                    try
                    {
                        var response = await _httpClient.GetAsync(productUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            _logging.Info($"HP product lookup responded for ID: {productId}");
                            
                            // Note: This API returns product info, not BIOS versions
                            // We'd need to follow up with a specific softpaq query
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.Info($"HP product lookup failed: {ex.Message}");
                    }
                }

                // HP's public APIs require complex state management and don't directly expose BIOS versions
                // The most reliable method is directing users to their support page
                _logging.Info("HP BIOS lookup: API does not provide direct BIOS version data. Use HP Support Assistant or hp.com/support for updates.");
                return null;
            }
            catch (Exception ex)
            {
                _logging.Info($"HP catalog lookup failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Try to parse BIOS info from HP's driver API response
        /// </summary>
        private BiosInfo? ParseHpDriverResponse(string json, SystemInfo systemInfo)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // HP's response structure varies, try common paths
                if (root.TryGetProperty("softpaqs", out var softpaqs) && softpaqs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var softpaq in softpaqs.EnumerateArray())
                    {
                        // Look for BIOS updates
                        var name = softpaq.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                        var category = softpaq.TryGetProperty("category", out var catProp) ? catProp.GetString() : "";
                        
                        if (!string.IsNullOrEmpty(name) && 
                            (name.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
                             category?.Contains("BIOS", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            var biosInfo = new BiosInfo
                            {
                                Version = softpaq.TryGetProperty("version", out var verProp) ? verProp.GetString() ?? "" : "",
                                ReleaseDate = softpaq.TryGetProperty("releaseDate", out var dateProp) ? dateProp.GetString() ?? "" : "",
                                SoftpaqNumber = softpaq.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                                DownloadUrl = softpaq.TryGetProperty("downloadUrl", out var urlProp) ? urlProp.GetString() : null,
                            };
                            
                            if (!string.IsNullOrEmpty(biosInfo.Version))
                            {
                                _logging.Info($"Found BIOS softpaq: {biosInfo.SoftpaqNumber} version {biosInfo.Version}");
                                return biosInfo;
                            }
                        }
                    }
                }
                
                // Try alternate response structures
                if (root.TryGetProperty("drivers", out var drivers) && drivers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var driver in drivers.EnumerateArray())
                    {
                        var type = driver.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "";
                        if (type?.Contains("BIOS", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return new BiosInfo
                            {
                                Version = driver.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                                DownloadUrl = driver.TryGetProperty("url", out var u) ? u.GetString() : null
                            };
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logging.Info($"Failed to parse HP response: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Extract HP product ID from SKU (e.g., "6Y7K8PA#ABG" -> "6Y7K8PA")
        /// </summary>
        private string? ExtractProductId(string? sku)
        {
            if (string.IsNullOrEmpty(sku)) return null;

            // HP SKUs often have format: XXXXX#YYY or XXXXX-YYY
            var match = Regex.Match(sku, @"^([A-Z0-9]+)");
            return match.Success ? match.Groups[1].Value : sku;
        }

        /// <summary>
        /// Construct URL to HP support page for the product
        /// </summary>
        private string ConstructSupportUrl(SystemInfo systemInfo)
        {
            // HP Support page works best with serial number for product lookup
            // Format: https://support.hp.com/drivers/selfservice/hp-omen-17-ck2000-laptop-pc-series/2101473526
            // Or by serial: https://support.hp.com/drivers?serialnumber=XXXX
            
            if (!string.IsNullOrEmpty(systemInfo.SerialNumber))
            {
                return $"https://support.hp.com/drivers?serialnumber={Uri.EscapeDataString(systemInfo.SerialNumber)}";
            }
            
            // Fallback to model name search
            var modelName = systemInfo.Model ?? systemInfo.ProductName ?? "hp-omen";
            return $"https://support.hp.com/drivers/selfservice/{Uri.EscapeDataString(modelName)}";
        }

        /// <summary>
        /// Compare BIOS versions to determine if update is available
        /// </summary>
        private bool CompareBiosVersions(string? currentVersion, string? latestVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion))
                return false;

            // HP BIOS versions can be in various formats:
            // - F.xx (e.g., F.20, F.21)
            // - Date-based (e.g., 01/15/2024)
            // - Numeric (e.g., 1.15.0)

            // Extract version numbers for comparison
            var currentNums = ExtractVersionNumbers(currentVersion);
            var latestNums = ExtractVersionNumbers(latestVersion);

            if (currentNums.Count == 0 || latestNums.Count == 0)
            {
                // Fall back to string comparison
                return string.Compare(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase) < 0;
            }

            // Compare version components
            for (int i = 0; i < Math.Min(currentNums.Count, latestNums.Count); i++)
            {
                if (latestNums[i] > currentNums[i]) return true;
                if (latestNums[i] < currentNums[i]) return false;
            }

            return latestNums.Count > currentNums.Count;
        }

        /// <summary>
        /// Extract numeric version components from version string
        /// </summary>
        private List<int> ExtractVersionNumbers(string version)
        {
            var matches = Regex.Matches(version, @"\d+");
            return matches.Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToList();
        }

        /// <summary>
        /// Open the HP Support page for the current system
        /// </summary>
        public void OpenSupportPage(SystemInfo systemInfo)
        {
            try
            {
                var url = ConstructSupportUrl(systemInfo);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                _logging.Info($"Opened HP Support page: {url}");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to open HP Support page: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Open HP Support Assistant download page
        /// </summary>
        public void OpenHpSupportAssistant()
        {
            try
            {
                const string url = "https://support.hp.com/us-en/help/hp-support-assistant";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                _logging.Info("Opened HP Support Assistant page");
            }
            catch (Exception ex)
            {
                _logging.Error($"Failed to open HP Support Assistant page: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
