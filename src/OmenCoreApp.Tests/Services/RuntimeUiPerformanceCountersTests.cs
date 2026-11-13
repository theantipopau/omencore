using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    // The counters are process-wide statics that view-model tests also bump; running beside them made the exact
    // counts flaky (DashboardSamplesReceived 2 instead of 1).
    [Collection("NonParallel")]
    public class RuntimeUiPerformanceCountersTests
    {
        [Fact]
        public void Snapshot_ReflectsRecordedCounts()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            RuntimeUiPerformanceCounters.RecordDispatcherBeginInvokePost();
            RuntimeUiPerformanceCounters.RecordDispatcherBeginInvokePost();
            RuntimeUiPerformanceCounters.RecordDispatcherInvoke();

            RuntimeUiPerformanceCounters.RecordMainMonitoringSampleQueued();
            RuntimeUiPerformanceCounters.RecordMainMonitoringSampleCoalesced();
            RuntimeUiPerformanceCounters.RecordMainMonitoringSampleProjected();

            RuntimeUiPerformanceCounters.RecordDashboardSampleReceived();
            RuntimeUiPerformanceCounters.RecordDashboardSampleProjected();
            RuntimeUiPerformanceCounters.RecordDashboardSampleSkipped();
            RuntimeUiPerformanceCounters.RecordDashboardDispatcherPost();
            RuntimeUiPerformanceCounters.RecordDashboardProjectionRequeue();

            RuntimeUiPerformanceCounters.RecordGeneralSampleReceived();
            RuntimeUiPerformanceCounters.RecordGeneralSampleProjected();
            RuntimeUiPerformanceCounters.RecordGeneralSampleSkipped();
            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized: true, itemsUpdated: 2);
            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized: false, itemsUpdated: 2);

            var snapshot = RuntimeUiPerformanceCounters.GetSnapshot();

            snapshot.DispatcherBeginInvokePosts.Should().Be(2);
            snapshot.DispatcherInvokes.Should().Be(1);
            snapshot.MainMonitoringSamplesQueued.Should().Be(1);
            snapshot.MainMonitoringSamplesCoalesced.Should().Be(1);
            snapshot.MainMonitoringSamplesProjected.Should().Be(1);
            snapshot.DashboardSamplesReceived.Should().Be(1);
            snapshot.DashboardSamplesProjected.Should().Be(1);
            snapshot.DashboardSamplesSkipped.Should().Be(1);
            snapshot.DashboardDispatcherPosts.Should().Be(1);
            snapshot.DashboardProjectionRequeues.Should().Be(1);
            snapshot.GeneralSamplesReceived.Should().Be(1);
            snapshot.GeneralSamplesProjected.Should().Be(1);
            snapshot.GeneralSamplesSkipped.Should().Be(1);
            snapshot.TotalProjectedSamples.Should().Be(3);
            snapshot.ProjectionAmplificationRatio.Should().Be(3);
            snapshot.DispatcherAmplificationRatio.Should().Be(2);
            snapshot.MainProjectionAcceptanceRatio.Should().Be(1);
            snapshot.DashboardProjectionAcceptanceRatio.Should().Be(1);
            snapshot.GeneralProjectionAcceptanceRatio.Should().Be(1);
            snapshot.FanTelemetrySyncs.Should().Be(2);
            snapshot.FanTelemetryCollectionResizes.Should().Be(1);
            snapshot.FanTelemetryItemsUpdated.Should().Be(4);
            snapshot.FanTelemetryPropertyOnlySyncs.Should().Be(1);
            snapshot.FanTelemetryCollectionResizeRatio.Should().Be(0.5d, "1 resize out of 2 fan telemetry syncs");
            snapshot.FanTelemetryPropertyOnlySyncRatio.Should().Be(0.5d, "1 property-only update out of 2 fan telemetry syncs");
        }

        [Fact]
        public void ResetForTests_ClearsAllCounters()
        {
            RuntimeUiPerformanceCounters.RecordDispatcherBeginInvokePost();
            RuntimeUiPerformanceCounters.RecordMainMonitoringSampleQueued();
            RuntimeUiPerformanceCounters.RecordDashboardSampleReceived();
            RuntimeUiPerformanceCounters.RecordGeneralSampleReceived();
            RuntimeUiPerformanceCounters.RecordFanTelemetrySync(collectionResized: true, itemsUpdated: 1);

            RuntimeUiPerformanceCounters.ResetForTests();
            var snapshot = RuntimeUiPerformanceCounters.GetSnapshot();

            snapshot.DispatcherBeginInvokePosts.Should().Be(0);
            snapshot.MainMonitoringSamplesQueued.Should().Be(0);
            snapshot.DashboardSamplesReceived.Should().Be(0);
            snapshot.GeneralSamplesReceived.Should().Be(0);
            snapshot.FanTelemetrySyncs.Should().Be(0);
            snapshot.FanTelemetryCollectionResizes.Should().Be(0);
            snapshot.FanTelemetryItemsUpdated.Should().Be(0);
            snapshot.FanTelemetryPropertyOnlySyncs.Should().Be(0);
        }

        /// <summary>
        /// Issue #129/#128/#130 + RC Diagnostics: v3.6.2 field validation counters
        /// (dormancy, cache hit, hidden surface) must be recorded and available in snapshots.
        /// </summary>
        [Fact]
        public void Snapshot_ReflectsV362FieldValidationCounters()
        {
            RuntimeUiPerformanceCounters.ResetForTests();

            RuntimeUiPerformanceCounters.RecordDashboardDormancyActivation();
            RuntimeUiPerformanceCounters.RecordDashboardDormancyActivation();
            RuntimeUiPerformanceCounters.RecordDashboardDormancySampleProjected();
            RuntimeUiPerformanceCounters.RecordHiddenSurfaceSampleSkipped();
            RuntimeUiPerformanceCounters.RecordTrayRenderCacheHit();
            RuntimeUiPerformanceCounters.RecordTrayRenderCacheMiss();
            RuntimeUiPerformanceCounters.RecordPopupRenderCacheHit();
            RuntimeUiPerformanceCounters.RecordPopupRenderCacheMiss();
            RuntimeUiPerformanceCounters.RecordLatestSampleReplacement();

            var snapshot = RuntimeUiPerformanceCounters.GetSnapshot();

            snapshot.DashboardDormancyActivations.Should().Be(2);
            snapshot.DashboardDormancySamplesProjected.Should().Be(1);
            snapshot.HiddenSurfaceSamplesSkipped.Should().Be(1);
            snapshot.TrayRenderCacheHits.Should().Be(1);
            snapshot.TrayRenderCacheMisses.Should().Be(1);
            snapshot.PopupRenderCacheHits.Should().Be(1);
            snapshot.PopupRenderCacheMisses.Should().Be(1);
            snapshot.LatestSampleReplacements.Should().Be(1);
            snapshot.TrayRenderCacheHitRatio.Should().Be(0.5d, "1 hit out of 2 total");
            snapshot.PopupRenderCacheHitRatio.Should().Be(0.5d, "1 hit out of 2 total");
        }
    }
}
