using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services;
using OmenCore.Services.BloatwareManager;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    [Collection("Config Isolation")]
    public class BloatwareManagerServiceTests : IDisposable
    {
        private readonly string _tempLocalAppData;
        private readonly string? _originalLocalAppData;

        public BloatwareManagerServiceTests()
        {
            _originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            _tempLocalAppData = Path.Combine(Path.GetTempPath(), $"omen_bloatware_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempLocalAppData);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _tempLocalAppData);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);
            try
            {
                if (Directory.Exists(_tempLocalAppData))
                {
                    Directory.Delete(_tempLocalAppData, true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        [Fact]
        public async Task RemoveAppAsync_WhenAppAlreadyRemoved_MarksSkippedAndPreservesDetail()
        {
            var logger = new LoggingService();
            logger.Initialize();

            try
            {
                var service = new BloatwareManagerService(logger);
                var app = new BloatwareApp
                {
                    Name = "Victus Hub",
                    PackageId = "victus-hub",
                    Type = BloatwareType.Win32App,
                    IsRemoved = true,
                    CanRestore = false
                };

                string? statusMessage = null;
                service.StatusChanged += message => statusMessage = message;

                var removed = await service.RemoveAppAsync(app, CancellationToken.None);

                removed.Should().BeTrue();
                app.LastRemovalStatus.Should().Be(RemovalStatus.Skipped);
                app.LastRemovalDetail.Should().Be("Item was already removed in this session.");
                app.LastFailureReason.Should().Be(app.LastRemovalDetail);
                statusMessage.Should().Contain("Skipped");
            }
            finally
            {
                logger.Dispose();
            }
        }

        [Fact]
        public async Task RemoveAppsWithRollbackAsync_WhenAppsAreNoOp_SetsSkippedWithoutFailures()
        {
            var logger = new LoggingService();
            logger.Initialize();

            try
            {
                var service = new BloatwareManagerService(logger);
                var apps = new List<BloatwareApp>
                {
                    new()
                    {
                        Name = "Victus Telemetry",
                        PackageId = "victus-telemetry",
                        Type = BloatwareType.ScheduledTask,
                        IsRemoved = true,
                        CanRestore = true
                    },
                    new()
                    {
                        Name = "HP Promotions",
                        PackageId = "hp-promotions",
                        Type = BloatwareType.AppxPackage,
                        IsRemoved = true,
                        CanRestore = true
                    }
                };

                var result = await service.RemoveAppsWithRollbackAsync(apps, cancellationToken: CancellationToken.None);

                result.Completed.Should().BeTrue();
                result.Skipped.Should().HaveCount(2);
                result.Succeeded.Should().BeEmpty();
                result.Failed.Should().BeEmpty();
                result.RollbackSucceeded.Should().BeEmpty();
                result.RollbackFailed.Should().BeEmpty();

                apps.Should().OnlyContain(app => app.LastRemovalStatus == RemovalStatus.Skipped);
                apps.Should().OnlyContain(app => !string.IsNullOrWhiteSpace(app.LastRemovalDetail));
            }
            finally
            {
                logger.Dispose();
            }
        }

        [Fact]
        public void BloatwareApp_WhenOutcomePropertiesUpdated_RaisesPropertyChanged()
        {
            var app = new BloatwareApp();
            var changedProperties = new List<string>();
            app.PropertyChanged += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.PropertyName))
                {
                    changedProperties.Add(args.PropertyName!);
                }
            };

            app.LastRemovalStatus = RemovalStatus.Skipped;
            app.LastRemovalDetail = "Already absent";
            app.LastFailureReason = "Already absent";

            changedProperties.Should().Contain(nameof(BloatwareApp.LastRemovalStatus));
            changedProperties.Should().Contain(nameof(BloatwareApp.LastRemovalDetail));
            changedProperties.Should().Contain(nameof(BloatwareApp.LastFailureReason));
        }
    }
}