using System;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class MemoryOptimizerServiceAutoCleanTests
    {
        [Fact]
        public void EvaluateAutoCleanDecision_WaitsForSustainedPressure()
        {
            var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            var info = CreatePressureInfo();

            var decision = MemoryOptimizerService.EvaluateAutoCleanDecision(
                info,
                thresholdPercent: 80,
                MemoryAutoCleanProfile.Balanced,
                now,
                pressureSinceUtc: DateTime.MinValue,
                lastCleanAtUtc: DateTime.MinValue);

            decision.ShouldClean.Should().BeFalse();
            decision.IsThrottled.Should().BeTrue();
            decision.PressureSinceUtc.Should().Be(now);
            decision.Reason.Should().Contain("sustained memory pressure");
        }

        [Fact]
        public void EvaluateAutoCleanDecision_CleansAfterPressureGrace()
        {
            var now = new DateTime(2026, 5, 1, 12, 1, 0, DateTimeKind.Utc);
            var pressureSince = now.AddSeconds(-25);
            var info = CreatePressureInfo();

            var decision = MemoryOptimizerService.EvaluateAutoCleanDecision(
                info,
                thresholdPercent: 80,
                MemoryAutoCleanProfile.Balanced,
                now,
                pressureSince,
                lastCleanAtUtc: DateTime.MinValue);

            decision.ShouldClean.Should().BeTrue();
            decision.IsThrottled.Should().BeFalse();
            decision.PressureSinceUtc.Should().Be(pressureSince);
            decision.Reason.Should().Contain("memory 88% load");
        }

        [Fact]
        public void EvaluateAutoCleanDecision_SkipsDuringCooldown()
        {
            var now = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc);
            var info = CreatePressureInfo();

            var decision = MemoryOptimizerService.EvaluateAutoCleanDecision(
                info,
                thresholdPercent: 80,
                MemoryAutoCleanProfile.Balanced,
                now,
                pressureSinceUtc: now.AddMinutes(-2),
                lastCleanAtUtc: now.AddMinutes(-1));

            decision.ShouldClean.Should().BeFalse();
            decision.IsThrottled.Should().BeTrue();
            decision.Reason.Should().Contain("cooldown active");
        }

        [Fact]
        public void EvaluateAutoCleanDecision_UsesCooldownOverride()
        {
            var now = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc);
            var info = CreatePressureInfo();

            var decision = MemoryOptimizerService.EvaluateAutoCleanDecision(
                info,
                thresholdPercent: 80,
                MemoryAutoCleanProfile.Balanced,
                now,
                pressureSinceUtc: now.AddMinutes(-2),
                lastCleanAtUtc: now.AddMinutes(-2),
                cooldownOverride: TimeSpan.FromMinutes(1));

            decision.ShouldClean.Should().BeTrue();
            decision.IsThrottled.Should().BeFalse();
            decision.Reason.Should().Contain("memory 88% load");
        }

        [Fact]
        public void EvaluateAutoCleanDecision_RequiresActionablePressureBeyondLoadSnapshot()
        {
            var now = new DateTime(2026, 5, 1, 12, 10, 0, DateTimeKind.Utc);
            var info = new MemoryInfo
            {
                TotalPhysicalMB = 32768,
                AvailablePhysicalMB = 8192,
                MemoryLoadPercent = 82,
                CommitTotalMB = 16000,
                CommitLimitMB = 32768
            };

            var decision = MemoryOptimizerService.EvaluateAutoCleanDecision(
                info,
                thresholdPercent: 80,
                MemoryAutoCleanProfile.Balanced,
                now,
                pressureSinceUtc: now.AddMinutes(-2),
                lastCleanAtUtc: DateTime.MinValue);

            decision.ShouldClean.Should().BeFalse();
            decision.IsThrottled.Should().BeFalse();
            decision.PressureSinceUtc.Should().Be(DateTime.MinValue);
            decision.Reason.Should().Contain("below actionable threshold");
        }

        [Fact]
        public void PreviewMemoryCleaning_UsesProvidedSnapshot()
        {
            using var logger = new LoggingService();
            using var service = new MemoryOptimizerService(logger);
            var info = new MemoryInfo
            {
                TotalPhysicalMB = 16384,
                UsedPhysicalMB = 8000,
                AvailablePhysicalMB = 4000,
                SystemCacheMB = 2000,
                ProcessCount = 123
            };

            var preview = service.PreviewMemoryCleaning(MemoryCleanFlags.WorkingSets | MemoryCleanFlags.SystemFileCache, info);

            preview.EstimatedFreeMB.Should().Be(1800);
            preview.EnumeratedProcesses.Should().Be(123);
        }

        [Fact]
        public void GetTopMemoryHogs_DoesNotResolveExecutablePathsByDefault()
        {
            using var logger = new LoggingService();
            using var service = new MemoryOptimizerService(logger);

            var hogs = service.GetTopMemoryHogs(5, totalPhysicalMB: 32768);

            hogs.Should().OnlyContain(process => string.IsNullOrWhiteSpace(process.ExecutablePath));
        }

        [Fact]
        public void TryResolveExecutablePath_ReturnsNullForInvalidProcessId()
        {
            using var logger = new LoggingService();
            using var service = new MemoryOptimizerService(logger);

            service.TryResolveExecutablePath(-1).Should().BeNull();
        }

        [Fact]
        public void MemoryCleanResult_DeltaSummary_IncludesBeforeAfterMemoryCategories()
        {
            var result = new MemoryCleanResult
            {
                BeforeUsedMB = 12000,
                AfterUsedMB = 11000,
                BeforeAvailableMB = 4000,
                AfterAvailableMB = 5000,
                BeforeStandbyListMB = 2500,
                AfterStandbyListMB = 700,
                BeforeSystemCacheMB = 3200,
                AfterSystemCacheMB = 1600,
                BeforeCommitTotalMB = 15000,
                AfterCommitTotalMB = 14950,
                BeforePageFileUsedMB = 2000,
                AfterPageFileUsedMB = 2100,
                BeforeModifiedPageListMB = 300,
                AfterModifiedPageListMB = 0
            };

            var summary = result.GetDeltaSummary();

            summary.Should().Contain("Physical used 12000 -> 11000 MB (reduced 1000 MB)");
            summary.Should().Contain("Available 4000 -> 5000 MB (increased 1000 MB)");
            summary.Should().Contain("Standby 2500 -> 700 MB (reduced 1800 MB)");
            summary.Should().Contain("Cache 3200 -> 1600 MB (reduced 1600 MB)");
            summary.Should().Contain("Commit 15000 -> 14950 MB (reduced 50 MB)");
            summary.Should().Contain("Page file 2000 -> 2100 MB (increased 100 MB)");
            summary.Should().Contain("Modified 300 -> 0 MB (reduced 300 MB)");
        }

        [Fact]
        public void SelectAutoCleanFlags_GameForeground_UsesWorkingSetTrimUnlessCritical()
        {
            var normalPressure = CreatePressureInfo();
            var criticalPressure = CreatePressureInfo();
            criticalPressure.MemoryLoadPercent = 96;

            MemoryOptimizerService.SelectAutoCleanFlags(normalPressure, gameLikelyInForeground: true, quietWindowEnabled: true)
                .Should().Be(MemoryCleanFlags.WorkingSets);
            MemoryOptimizerService.SelectAutoCleanFlags(criticalPressure, gameLikelyInForeground: true, quietWindowEnabled: true)
                .Should().Be(MemoryCleanFlags.AllSafe);
            MemoryOptimizerService.SelectAutoCleanFlags(normalPressure, gameLikelyInForeground: false, quietWindowEnabled: true)
                .Should().Be(MemoryCleanFlags.AllSafe);
        }

        [Fact]
        public void CoversBounds_AcceptsBorderlessFullscreenToleranceAndSecondaryMonitorOffsets()
        {
            MemoryOptimizerService.CoversBounds(1919, -1, 3841, 1081, 1920, 0, 3840, 1080)
                .Should().BeTrue("borderless fullscreen windows can land one or two pixels outside monitor bounds");

            MemoryOptimizerService.CoversBounds(1920, 0, 3500, 1000, 1920, 0, 3840, 1080)
                .Should().BeFalse("ordinary windowed games should not trigger the quiet-window path");
        }

        private static MemoryInfo CreatePressureInfo()
        {
            return new MemoryInfo
            {
                TotalPhysicalMB = 16384,
                AvailablePhysicalMB = 1536,
                MemoryLoadPercent = 88,
                CommitTotalMB = 14500,
                CommitLimitMB = 16384
            };
        }
    }
}
