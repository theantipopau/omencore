using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// WP-31: the startup timeline is the only durable record of where startup time goes, so its
    /// contract (order, first-occurrence-wins, per-phase delta) has to be pinned rather than
    /// inferred from whatever a particular run happened to print.
    /// </summary>
    [Collection("NonParallel")]
    public class StartupTimelineTests : IDisposable
    {
        public StartupTimelineTests()
        {
            StartupTimeline.ResetForTests();
            StartupTimeline.Sink = null;
        }

        public void Dispose()
        {
            StartupTimeline.ResetForTests();
            StartupTimeline.Sink = null;
        }

        [Fact]
        public void GetMarks_BeforeAnyMark_IsEmpty()
        {
            StartupTimeline.GetMarks().Should().BeEmpty();
            StartupTimeline.GetTotalElapsedMs().Should().BeNull();
        }

        [Fact]
        public void Mark_RecordsPhaseNameAndNonNegativeTimings()
        {
            StartupTimeline.Mark("first_log_line");

            var marks = StartupTimeline.GetMarks();

            marks.Should().ContainSingle();
            marks[0].Phase.Should().Be("first_log_line");
            marks[0].ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
            marks[0].SincePreviousMs.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void Mark_PreservesRecordOrder()
        {
            StartupTimeline.Mark("a");
            StartupTimeline.Mark("b");
            StartupTimeline.Mark("c");

            StartupTimeline.GetMarks().Select(m => m.Phase).Should().ContainInOrder("a", "b", "c");
        }

        [Fact]
        public void Mark_SecondCallForSamePhase_IsIgnored()
        {
            // Callers do not guard against repeats - a view constructed twice, or a method entered
            // again after a retry, must not make the timeline ambiguous or double-count the phase.
            StartupTimeline.Mark("tray_initialized");
            StartupTimeline.Mark("worker_bootstrap");
            StartupTimeline.Mark("tray_initialized");

            var marks = StartupTimeline.GetMarks();

            marks.Should().HaveCount(2);
            marks.Count(m => m.Phase == "tray_initialized").Should().Be(1);
            marks[1].Phase.Should().Be("worker_bootstrap");
        }

        [Fact]
        public void Mark_EmptyOrNullPhase_IsIgnored()
        {
            StartupTimeline.Mark("");
            StartupTimeline.Mark("   ");
            StartupTimeline.Mark(null!);

            StartupTimeline.GetMarks().Should().BeEmpty();
        }

        [Fact]
        public void Mark_WithSink_EmitsOneMessagePerPhase()
        {
            var messages = new List<string>();
            StartupTimeline.Sink = messages.Add;

            StartupTimeline.Mark("first_render");
            StartupTimeline.Mark("first_render");

            messages.Should().ContainSingle();
            messages[0].Should().StartWith("[startup] first_render:");
            messages[0].Should().Contain("phase +");
        }

        [Fact]
        public void Mark_WithoutSink_StillRecords()
        {
            StartupTimeline.Sink = null;

            StartupTimeline.Mark("first_render");

            StartupTimeline.GetMarks().Should().ContainSingle(m => m.Phase == "first_render");
        }

        [Fact]
        public void SincePreviousMs_IsThePhaseCostNotTheRunningTotal()
        {
            StartupTimeline.Mark("a");
            System.Threading.Thread.Sleep(20);
            StartupTimeline.Mark("b");

            var marks = StartupTimeline.GetMarks();

            marks[0].SincePreviousMs.Should().Be(marks[0].ElapsedMs);
            marks[1].ElapsedMs.Should().BeGreaterThan(marks[0].ElapsedMs);
            marks[1].SincePreviousMs.Should().Be(marks[1].ElapsedMs - marks[0].ElapsedMs);
            // The delta is what makes the number attributable, so it must be materially smaller
            // than the running total once a real delay sits between two phases.
            marks[1].SincePreviousMs.Should().BeLessThan(marks[1].ElapsedMs);
        }

        [Fact]
        public void GetTotalElapsedMs_ReturnsLastPhaseElapsed()
        {
            StartupTimeline.Mark("a");
            StartupTimeline.Mark("b");

            var marks = StartupTimeline.GetMarks();

            StartupTimeline.GetTotalElapsedMs().Should().Be(marks[^1].ElapsedMs);
        }

        [Fact]
        public void ResetForTests_ClearsMarksAndAllowsSamePhaseAgain()
        {
            StartupTimeline.Mark("first_render");

            StartupTimeline.ResetForTests();
            StartupTimeline.Mark("first_render");

            StartupTimeline.GetMarks().Should().ContainSingle(m => m.Phase == "first_render");
        }
    }
}
