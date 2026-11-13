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
        public void ExtractHashForAsset_WithPlainLabelFormat_ReturnsHash()
        {
            var logging = new LoggingService();
            using var service = new AutoUpdateService(logging);
            var releaseBody = @"
## What's New
- Feature 1
- Feature 2

SHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef

Download the installer above.
";
            var hash = InvokeExtractHashForAsset(service, releaseBody, "OmenCoreSetup-4.3.0.exe");

            hash.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        }

        [Fact]
        public void ExtractHashForAsset_WithMarkdownTableFormat_ReturnsHash()
        {
            // GitHub #192: every real release since 3.4.1 has shipped hashes as a two-column
            // markdown table ("| Artifact | SHA256 |"), not the plain "name: hash" line the old
            // regex assumed - it matched nothing against real release notes, so the auto-updater
            // always reported "missing SHA256" and refused to install, even with a valid hash
            // sitting right there in the table. This is the exact shape of a real release body.
            var logging = new LoggingService();
            using var service = new AutoUpdateService(logging);
            var releaseBody = @"
## Release Artifacts

| Artifact | SHA256 |
|---|---|
| `OmenCoreSetup-4.3.0.exe` | `6F1AE6AB29F07C55B27BFE59FFAA2828131177735281119480FE8A47C1B4C6B8` |
| `OmenCore-4.3.0-win-x64.zip` | `14116B8C542B7FB77DB08C06F5889660D344CD3B2925FAA3D4E6E8DE4A0053F6` |
| `OmenCore-4.3.0-linux-x64.zip` | `E75DA1A26C0C087D5432555D0937274F33F85585190478CAF74D4557D6FE087A` |
";
            var exeHash = InvokeExtractHashForAsset(service, releaseBody, "OmenCoreSetup-4.3.0.exe");
            var zipHash = InvokeExtractHashForAsset(service, releaseBody, "OmenCore-4.3.0-win-x64.zip");

            exeHash.Should().Be("6F1AE6AB29F07C55B27BFE59FFAA2828131177735281119480FE8A47C1B4C6B8",
                because: "the per-asset row must resolve to that asset's own hash, not the first one in the table");
            zipHash.Should().Be("14116B8C542B7FB77DB08C06F5889660D344CD3B2925FAA3D4E6E8DE4A0053F6");
        }

        private static string? InvokeExtractHashForAsset(AutoUpdateService service, string body, string assetFileName)
        {
            var method = typeof(AutoUpdateService).GetMethod("ExtractHashForAsset", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Could not access ExtractHashForAsset method.");

            return method.Invoke(service, new object?[] { body, assetFileName }) as string;
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

        // The v4.4.0 release's real asset list, in the order GitHub returns it.
        private static readonly string[] V440Assets =
        {
            "OmenCore-4.4.0-linux-x64.zip",
            "OmenCore-4.4.0-linux-x64.zip.sha256",
            "OmenCore-4.4.0-win-x64.zip",
            "OmenCore-4.4.0-win-x64.zip.sha256",
            "OmenCoreSetup-4.4.0.exe",
            "OmenCoreSetup-4.4.0.exe.sha256",
        };

        [Fact]
        public void InstalledBuild_UnderProgramFiles_IsDetectedAsInstaller()
        {
            // The 4.3.1 -> 4.4.0 field failure: detection read AppContext.BaseDirectory, which for an
            // IncludeAllContentForSelfExtract build is the %TEMP% extraction folder, so an installed
            // copy looked portable. Detection now takes the running exe's own folder.
            AutoUpdateService.DetectInstallationTypeFromDirectory(
                    @"C:\Program Files\OmenCore", @"C:\Program Files", @"C:\Program Files (x86)", _ => false)
                .Should().Be(InstallationType.Installer);
        }

        [Fact]
        public void TempExtractionFolder_LooksPortable_WhichIsWhyDetectionMustUseTheExeFolder()
        {
            var extracted = @"C:\Users\u\AppData\Local\Temp\.net\OmenCore\abc123";
            AutoUpdateService.DetectInstallationTypeFromDirectory(
                    extracted, @"C:\Program Files", @"C:\Program Files (x86)", _ => false)
                .Should().Be(InstallationType.Portable, "this is what the old code saw for every installed build");
        }

        [Fact]
        public void CustomInstallFolder_WithInnoUninstaller_IsDetectedAsInstaller()
        {
            AutoUpdateService.DetectInstallationTypeFromDirectory(
                    @"D:\Apps\OmenCore", @"C:\Program Files", @"C:\Program Files (x86)",
                    path => path.EndsWith("unins000.exe", StringComparison.OrdinalIgnoreCase))
                .Should().Be(InstallationType.Installer);
        }

        [Fact]
        public void InstallerBuild_SelectsTheSetupExe_NeverAZipOrAChecksum()
        {
            AutoUpdateService.SelectAssetName(V440Assets, InstallationType.Installer)
                .Should().Be("OmenCoreSetup-4.4.0.exe");
        }

        [Fact]
        public void PortableBuild_SelectsTheWindowsZip_NeverTheLinuxOne()
        {
            AutoUpdateService.SelectAssetName(V440Assets, InstallationType.Portable)
                .Should().Be("OmenCore-4.4.0-win-x64.zip");

            // Order-independent: the old rule took whichever zip came last.
            var reversed = (string[])V440Assets.Clone();
            Array.Reverse(reversed);
            AutoUpdateService.SelectAssetName(reversed, InstallationType.Portable)
                .Should().Be("OmenCore-4.4.0-win-x64.zip");
        }

        [Fact]
        public void PortableUpdateMessage_TellsTheUserToExtract_NotThatTheFileIsCorrupt()
        {
            var message = AutoUpdateService.PortableUpdateMessage(@"C:\t\OmenCore-4.4.0-win-x64.zip");
            message.Should().Contain("extract");
            message.Should().NotContain("corrupt");
        }

        private const string H = "12191d386066cb357cce41e928ba873efa4f7c8d9596865a2b093c789d754cb5";

        [Theory]
        [InlineData(H + "  OmenCore-4.5.1-linux-x64.zip\n", "OmenCore-4.5.1-linux-x64.zip")]    // sha256sum text mode
        [InlineData(H + " *OmenCore-4.5.1-linux-x64.zip\r\n", "OmenCore-4.5.1-linux-x64.zip")]  // binary-mode marker
        [InlineData(H + "\n", "OmenCore-4.5.1-linux-x64.zip")]                                   // a bare hash
        [InlineData("12191D386066CB357CCE41E928BA873EFA4F7C8D9596865A2B093C789D754CB5  x.zip", "x.zip")]
        public void ParseSha256Sidecar_ReadsTheReleaseWorkflowsChecksumFile(string text, string asset)
        {
            AutoUpdateService.ParseSha256Sidecar(text, asset).Should().Be(H);
        }

        [Theory]
        [InlineData(H + "  OmenCoreSetup-4.5.1.exe", "OmenCore-4.5.1-win-x64.zip")]   // names a different file
        [InlineData("not-a-hash  x.zip", "x.zip")]
        [InlineData("", "x.zip")]
        [InlineData(null, "x.zip")]
        [InlineData(H + "\n" + H + "  other.zip", "x.zip")]                             // a bare hash among others is ambiguous
        public void ParseSha256Sidecar_RefusesAnythingThatDoesNotNameThisAsset(string? text, string asset)
        {
            AutoUpdateService.ParseSha256Sidecar(text, asset).Should().BeNull();
        }
    }
}
