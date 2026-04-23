// Area B — Resume/recovery behavior hardening (no-hardware session 2026-04-16)
// Tests that ResumeRecoveryDiagnosticsService state transitions are correct
// and that concurrent access does not corrupt cycle ID or timeline state.
// None of these tests require physical OMEN hardware.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OmenCore.Services.Diagnostics;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    public class ResumeRecoveryDiagnosticsServiceTests
    {
        // ─── Initial state ───────────────────────────────────────────────────────

        [Fact]
        public void InitialState_CycleIdIsZero_StatusIsNoActivity()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            svc.CurrentCycleId.Should().Be(0);
            svc.RecoveryInProgress.Should().BeFalse();
            svc.Status.Should().Contain("No recent resume");
        }

        [Fact]
        public void InitialState_TimelineText_ContainsNoEntries()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            svc.TimelineText.Should().Contain("No timeline");
        }

        [Fact]
        public void InitialState_BuildExportReport_DoesNotThrow()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            var report = svc.BuildExportReport();

            report.Should().Contain("RESUME RECOVERY DIAGNOSTICS");
        }

        // ─── BeginSuspend ────────────────────────────────────────────────────────

        [Fact]
        public void BeginSuspend_IncrementsCycleId()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            var before = svc.CurrentCycleId;

            svc.BeginSuspend();

            svc.CurrentCycleId.Should().BeGreaterThan(before);
        }

        [Fact]
        public void BeginSuspend_SetsStatusToSuspended()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            svc.BeginSuspend();

            svc.Status.Should().Be("Suspended");
            svc.RecoveryInProgress.Should().BeFalse();
        }

        [Fact]
        public void BeginSuspend_RaisesUpdatedEvent()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            var eventRaised = false;
            svc.Updated += (_, _) => eventRaised = true;

            svc.BeginSuspend();

            eventRaised.Should().BeTrue();
        }

        // ─── BeginResume ─────────────────────────────────────────────────────────

        [Fact]
        public void BeginResume_SetsRecoveryInProgressTrue()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            svc.BeginResume();

            svc.RecoveryInProgress.Should().BeTrue();
            svc.Status.Should().Be("Recovering");
        }

        [Fact]
        public void BeginResume_AfterBeginSuspend_DoesNotIncrementCycleIdAgain()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            svc.BeginSuspend();
            var cycleAfterSuspend = svc.CurrentCycleId;

            svc.BeginResume(); // entries exist from BeginSuspend — should NOT bump ID again

            svc.CurrentCycleId.Should().Be(cycleAfterSuspend,
                "CycleId must not increment again when entries already exist in the current cycle");
        }

        // ─── RecordStep ──────────────────────────────────────────────────────────

        [Fact]
        public void RecordStep_WithoutBeginSuspend_DoesNotThrow()
        {
            // RISK: services call RecordStep unconditionally after STEP-12.
            // Must be safe even with no active suspend/resume cycle.
            var svc = new ResumeRecoveryDiagnosticsService();

            var act = () => svc.RecordStep("monitoring", "poll loop started");

            act.Should().NotThrow();
        }

        [Fact]
        public void RecordStep_AppearsInTimelineText()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            svc.BeginResume();

            svc.RecordStep("monitoring", "unique-marker-XYZ");

            svc.TimelineText.Should().Contain("unique-marker-XYZ");
        }

        [Fact]
        public void RecordStep_CapsTotalEntriesAtTwenty()
        {
            var svc = new ResumeRecoveryDiagnosticsService();

            for (var i = 0; i < 25; i++)
            {
                svc.RecordStep("test", $"step-{i}");
            }

            // Timeline must not grow unbounded (capped at 20 by AddEntryNoLock)
            var lines = svc.TimelineText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.Should().BeLessThanOrEqualTo(20,
                "RecordStep must cap the timeline at 20 entries to prevent memory growth");
        }

        // ─── Complete / Attention ─────────────────────────────────────────────

        [Fact]
        public void Complete_SetsStatusHealthy_AndClearsRecoveryInProgress()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            svc.BeginResume();

            svc.Complete("All services recovered successfully.");

            svc.Status.Should().Be("Healthy");
            svc.RecoveryInProgress.Should().BeFalse();
            svc.Summary.Should().Contain("All services recovered");
        }

        [Fact]
        public void Attention_SetsStatusAttentionNeeded()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            svc.BeginResume();

            svc.Attention("Monitoring loop did not restart within 10 s.");

            svc.Status.Should().Be("Attention Needed");
            svc.RecoveryInProgress.Should().BeFalse();
        }

        // ─── CycleId snapshot consistency (Area D — concurrency) ─────────────

        [Fact]
        public void CurrentCycleId_IsStable_DuringConcurrentRecordStepCalls()
        {
            // Verifies that the lock in CurrentCycleId and AddEntryNoLock prevent
            // torn reads across concurrent RecordStep calls.
            var svc = new ResumeRecoveryDiagnosticsService();
            svc.BeginSuspend();
            svc.BeginResume();

            var capturedIds = new List<int>();
            var lockObj = new object();

            Parallel.For(0, 50, _ =>
            {
                svc.RecordStep("concurrent", "stress");
                var id = svc.CurrentCycleId;
                lock (lockObj) { capturedIds.Add(id); }
            });

            // All captured IDs must equal the single cycle started by BeginSuspend
            capturedIds.Should().AllSatisfy(id => id.Should().Be(capturedIds[0]),
                "CycleId must not change during parallel RecordStep calls with no intervening BeginSuspend");
        }

        [Fact]
        public void Updated_EventFires_OnEveryStateChange()
        {
            var svc = new ResumeRecoveryDiagnosticsService();
            var count = 0;
            svc.Updated += (_, _) => Interlocked.Increment(ref count);

            svc.BeginSuspend();
            svc.BeginResume();
            svc.RecordStep("test", "ping");
            svc.Complete("done");

            count.Should().Be(4, "Updated must fire once for each state-mutating call");
        }
    }
}
