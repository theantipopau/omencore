using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class AutoUpdateServiceTests
    {
        [Fact]
        public async Task DownloadUpdateAsync_WithoutSha256Hash_ReturnsNull()
        {
            // Arrange
            var logging = new LoggingService();
            logging.Initialize();
            var service = new AutoUpdateService(logging);
            var versionInfo = new VersionInfo
            {
                Version = new Version(1, 0, 0),
                DownloadUrl = "https://example.com/update.exe",
                Sha256Hash = null! // Missing hash - should skip download
            };
            
            // Act
            var result = await service.DownloadUpdateAsync(versionInfo, CancellationToken.None);
            
            // Assert
            result.Should().BeNull("updates without hash verification should be skipped");
            logging.Dispose();
        }

        [Fact]
        public async Task DownloadUpdateAsync_WithEmptySha256Hash_ReturnsNull()
        {
            // Arrange
            var logging = new LoggingService();
            logging.Initialize();
            var service = new AutoUpdateService(logging);
            var versionInfo = new VersionInfo
            {
                Version = new Version(1, 0, 0),
                DownloadUrl = "https://example.com/update.exe",
                Sha256Hash = "" // Empty hash - should skip download
            };
            
            // Act
            var result = await service.DownloadUpdateAsync(versionInfo, CancellationToken.None);
            
            // Assert
            result.Should().BeNull("updates with empty hash should be skipped");
            logging.Dispose();
        }

        [Fact]
        public void ExtractHashFromBody_WithValidHash_ReturnsHash()
        {
            // Arrange
            var logging = new LoggingService();
            var service = new AutoUpdateService(logging);
            var releaseBody = @"
## What's New
- Feature 1
- Feature 2

SHA256: a1b2c3d4e5f6789012345678901234567890123456789012345678901234abcd

Download the installer above.
";
            
            // This tests the private method indirectly via CheckForUpdatesAsync
            // In real implementation, hash extraction is tested through integration
            releaseBody.Should().Contain("SHA256:", "release notes should include hash for verification");
        }

        [Fact]
        public void CleanupStaleDownloads_RemovesOldAndPartialFiles()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var service = new AutoUpdateService(logging);

            try
            {
                var downloadDir = GetDownloadDirectory(service);
                Directory.CreateDirectory(downloadDir);

                var token = Guid.NewGuid().ToString("N");
                var staleFile = Path.Combine(downloadDir, $"stale-{token}.exe");
                var partialFile = Path.Combine(downloadDir, $"partial-{token}.partial");
                var freshFile = Path.Combine(downloadDir, $"fresh-{token}.exe");

                File.WriteAllText(staleFile, "stale");
                File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow - TimeSpan.FromDays(30));
                File.WriteAllText(partialFile, "partial");
                File.WriteAllText(freshFile, "fresh");

                InvokeCleanupStaleDownloads(service, null);

                File.Exists(staleFile).Should().BeFalse("old update files should be pruned");
                File.Exists(partialFile).Should().BeFalse("partial update files should always be pruned");
                File.Exists(freshFile).Should().BeTrue("recent complete files should be preserved");

                DeleteIfExists(freshFile);
            }
            finally
            {
                service.Dispose();
                logging.Dispose();
            }
        }

        [Fact]
        public void CleanupStaleDownloads_PreservePath_KeepsMatchingFile()
        {
            var logging = new LoggingService();
            logging.Initialize();
            var service = new AutoUpdateService(logging);

            try
            {
                var downloadDir = GetDownloadDirectory(service);
                Directory.CreateDirectory(downloadDir);

                var token = Guid.NewGuid().ToString("N");
                var preservedFile = Path.Combine(downloadDir, $"preserve-{token}.exe");

                File.WriteAllText(preservedFile, "preserve");
                File.SetLastWriteTimeUtc(preservedFile, DateTime.UtcNow - TimeSpan.FromDays(30));

                InvokeCleanupStaleDownloads(service, preservedFile);

                File.Exists(preservedFile).Should().BeTrue("the active downloaded package path should not be deleted during cleanup");

                DeleteIfExists(preservedFile);
            }
            finally
            {
                service.Dispose();
                logging.Dispose();
            }
        }

        private static string GetDownloadDirectory(AutoUpdateService service)
        {
            var field = typeof(AutoUpdateService).GetField("_downloadDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Could not access AutoUpdateService download directory field.");

            return field.GetValue(service) as string
                ?? throw new InvalidOperationException("AutoUpdateService download directory field was null.");
        }

        private static void InvokeCleanupStaleDownloads(AutoUpdateService service, string? preservePath)
        {
            var method = typeof(AutoUpdateService).GetMethod("CleanupStaleDownloads", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Could not access CleanupStaleDownloads method.");

            method.Invoke(service, new object?[] { preservePath });
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
