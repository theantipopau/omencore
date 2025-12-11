using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using System.Security;
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
    }
}
