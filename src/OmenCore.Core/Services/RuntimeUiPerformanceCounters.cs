using System;
using System.Diagnostics;
using System.Threading;

namespace OmenCore.Services
{
    /// <summary>
    /// Lightweight process-wide counters for UI dispatcher and telemetry projection pressure.
    /// Values are safe to collect from any thread and can be exported in diagnostics bundles.
    /// </summary>
    public static class RuntimeUiPerformanceCounters
    {
        private static readonly object ResetGate = new();
        private static Stopwatch _uptime = Stopwatch.StartNew();

        private static long _dispatcherBeginInvokePosts;
        private static long _dispatcherInvokes;

        private static long _mainMonitoringSamplesQueued;
        private static long _mainMonitoringSamplesCoalesced;
        private static long _mainMonitoringSamplesProjected;

        private static long _dashboardSamplesReceived;
        private static long _dashboardSamplesProjected;
        private static long _dashboardSamplesSkipped;
        private static long _dashboardDispatcherPosts;
        private static long _dashboardProjectionRequeues;

        private static long _generalSamplesReceived;
        private static long _generalSamplesProjected;
        private static long _generalSamplesSkipped;

        // v3.6.2 Field Validation Counters
        private static long _dashboardDormancyActivations;
        private static long _dashboardDormancySamplesProjected;
        private static long _hiddenSurfaceSamplesSkipped;
        private static long _trayRenderCacheHits;
        private static long _trayRenderCacheMisses;
        private static long _popupRenderCacheHits;
        private static long _popupRenderCacheMisses;
        private static long _latestSampleReplacements;
        private static long _fanTelemetrySyncs;
        private static long _fanTelemetryCollectionResizes;
        private static long _fanTelemetryItemsUpdated;
        private static long _fanTelemetryPropertyOnlySyncs;

        public static void RecordDispatcherBeginInvokePost() => Interlocked.Increment(ref _dispatcherBeginInvokePosts);
        public static void RecordDispatcherInvoke() => Interlocked.Increment(ref _dispatcherInvokes);

        public static void RecordMainMonitoringSampleQueued() => Interlocked.Increment(ref _mainMonitoringSamplesQueued);
        public static void RecordMainMonitoringSampleCoalesced() => Interlocked.Increment(ref _mainMonitoringSamplesCoalesced);
        public static void RecordMainMonitoringSampleProjected() => Interlocked.Increment(ref _mainMonitoringSamplesProjected);

        public static void RecordDashboardSampleReceived() => Interlocked.Increment(ref _dashboardSamplesReceived);
        public static void RecordDashboardSampleProjected() => Interlocked.Increment(ref _dashboardSamplesProjected);
        public static void RecordDashboardSampleSkipped() => Interlocked.Increment(ref _dashboardSamplesSkipped);
        public static void RecordDashboardDispatcherPost() => Interlocked.Increment(ref _dashboardDispatcherPosts);
        public static void RecordDashboardProjectionRequeue() => Interlocked.Increment(ref _dashboardProjectionRequeues);

        public static void RecordGeneralSampleReceived() => Interlocked.Increment(ref _generalSamplesReceived);
        public static void RecordGeneralSampleProjected() => Interlocked.Increment(ref _generalSamplesProjected);
        public static void RecordGeneralSampleSkipped() => Interlocked.Increment(ref _generalSamplesSkipped);

        public static void RecordDashboardDormancyActivation() => Interlocked.Increment(ref _dashboardDormancyActivations);
        public static void RecordDashboardDormancySampleProjected() => Interlocked.Increment(ref _dashboardDormancySamplesProjected);
        public static void RecordHiddenSurfaceSampleSkipped() => Interlocked.Increment(ref _hiddenSurfaceSamplesSkipped);
        public static void RecordTrayRenderCacheHit() => Interlocked.Increment(ref _trayRenderCacheHits);
        public static void RecordTrayRenderCacheMiss() => Interlocked.Increment(ref _trayRenderCacheMisses);
        public static void RecordPopupRenderCacheHit() => Interlocked.Increment(ref _popupRenderCacheHits);
        public static void RecordPopupRenderCacheMiss() => Interlocked.Increment(ref _popupRenderCacheMisses);
        public static void RecordLatestSampleReplacement() => Interlocked.Increment(ref _latestSampleReplacements);
        public static void RecordFanTelemetrySync(bool collectionResized, int itemsUpdated)
        {
            Interlocked.Increment(ref _fanTelemetrySyncs);
            if (collectionResized)
            {
                Interlocked.Increment(ref _fanTelemetryCollectionResizes);
            }
            else if (itemsUpdated > 0)
            {
                Interlocked.Increment(ref _fanTelemetryPropertyOnlySyncs);
            }

            if (itemsUpdated > 0)
            {
                Interlocked.Add(ref _fanTelemetryItemsUpdated, itemsUpdated);
            }
        }

        public static RuntimeUiPerformanceCounterSnapshot GetSnapshot()
        {
            var uptimeSeconds = Math.Max(_uptime.Elapsed.TotalSeconds, 0.001);

            var dispatcherBeginInvokePosts = Interlocked.Read(ref _dispatcherBeginInvokePosts);
            var dispatcherInvokes = Interlocked.Read(ref _dispatcherInvokes);
            var mainMonitoringSamplesQueued = Interlocked.Read(ref _mainMonitoringSamplesQueued);
            var mainMonitoringSamplesCoalesced = Interlocked.Read(ref _mainMonitoringSamplesCoalesced);
            var mainMonitoringSamplesProjected = Interlocked.Read(ref _mainMonitoringSamplesProjected);
            var dashboardSamplesReceived = Interlocked.Read(ref _dashboardSamplesReceived);
            var dashboardSamplesProjected = Interlocked.Read(ref _dashboardSamplesProjected);
            var dashboardSamplesSkipped = Interlocked.Read(ref _dashboardSamplesSkipped);
            var dashboardDispatcherPosts = Interlocked.Read(ref _dashboardDispatcherPosts);
            var dashboardProjectionRequeues = Interlocked.Read(ref _dashboardProjectionRequeues);
            var generalSamplesReceived = Interlocked.Read(ref _generalSamplesReceived);
            var generalSamplesProjected = Interlocked.Read(ref _generalSamplesProjected);
            var generalSamplesSkipped = Interlocked.Read(ref _generalSamplesSkipped);

            var dashboardDormancyActivations = Interlocked.Read(ref _dashboardDormancyActivations);
            var dashboardDormancySamplesProjected = Interlocked.Read(ref _dashboardDormancySamplesProjected);
            var hiddenSurfaceSamplesSkipped = Interlocked.Read(ref _hiddenSurfaceSamplesSkipped);
            var trayRenderCacheHits = Interlocked.Read(ref _trayRenderCacheHits);
            var trayRenderCacheMisses = Interlocked.Read(ref _trayRenderCacheMisses);
            var popupRenderCacheHits = Interlocked.Read(ref _popupRenderCacheHits);
            var popupRenderCacheMisses = Interlocked.Read(ref _popupRenderCacheMisses);
            var latestSampleReplacements = Interlocked.Read(ref _latestSampleReplacements);
            var fanTelemetrySyncs = Interlocked.Read(ref _fanTelemetrySyncs);
            var fanTelemetryCollectionResizes = Interlocked.Read(ref _fanTelemetryCollectionResizes);
            var fanTelemetryItemsUpdated = Interlocked.Read(ref _fanTelemetryItemsUpdated);
            var fanTelemetryPropertyOnlySyncs = Interlocked.Read(ref _fanTelemetryPropertyOnlySyncs);

            return new RuntimeUiPerformanceCounterSnapshot(
                uptimeSeconds,
                dispatcherBeginInvokePosts,
                dispatcherInvokes,
                mainMonitoringSamplesQueued,
                mainMonitoringSamplesCoalesced,
                mainMonitoringSamplesProjected,
                dashboardSamplesReceived,
                dashboardSamplesProjected,
                dashboardSamplesSkipped,
                dashboardDispatcherPosts,
                dashboardProjectionRequeues,
                generalSamplesReceived,
                generalSamplesProjected,
                generalSamplesSkipped,
                mainMonitoringSamplesProjected + dashboardSamplesProjected + generalSamplesProjected,
                dispatcherBeginInvokePosts / uptimeSeconds,
                dispatcherInvokes / uptimeSeconds,
                dashboardSamplesProjected / uptimeSeconds,
                generalSamplesProjected / uptimeSeconds,
                mainMonitoringSamplesProjected / uptimeSeconds,
                SafeRatio(mainMonitoringSamplesProjected + dashboardSamplesProjected + generalSamplesProjected, mainMonitoringSamplesQueued),
                SafeRatio(dispatcherBeginInvokePosts, mainMonitoringSamplesQueued),
                SafeRatio(dashboardSamplesProjected, dashboardSamplesReceived),
                SafeRatio(generalSamplesProjected, generalSamplesReceived),
                SafeRatio(mainMonitoringSamplesProjected, mainMonitoringSamplesQueued),
                dashboardDormancyActivations,
                dashboardDormancySamplesProjected,
                hiddenSurfaceSamplesSkipped,
                trayRenderCacheHits,
                trayRenderCacheMisses,
                popupRenderCacheHits,
                popupRenderCacheMisses,
                latestSampleReplacements,
                fanTelemetrySyncs,
                fanTelemetryCollectionResizes,
                fanTelemetryItemsUpdated,
                fanTelemetryPropertyOnlySyncs,
                SafeRatio(trayRenderCacheHits, trayRenderCacheHits + trayRenderCacheMisses),
                SafeRatio(popupRenderCacheHits, popupRenderCacheHits + popupRenderCacheMisses),
                SafeRatio(fanTelemetryCollectionResizes, fanTelemetrySyncs),
                SafeRatio(fanTelemetryPropertyOnlySyncs, fanTelemetrySyncs));
        }

        private static double SafeRatio(long numerator, long denominator)
            => denominator <= 0 ? 0 : (double)numerator / denominator;

        // Intended for deterministic tests and explicit operator reset scenarios.
        public static void ResetForTests()
        {
            lock (ResetGate)
            {
                _uptime = Stopwatch.StartNew();
                Interlocked.Exchange(ref _dispatcherBeginInvokePosts, 0);
                Interlocked.Exchange(ref _dispatcherInvokes, 0);
                Interlocked.Exchange(ref _mainMonitoringSamplesQueued, 0);
                Interlocked.Exchange(ref _mainMonitoringSamplesCoalesced, 0);
                Interlocked.Exchange(ref _mainMonitoringSamplesProjected, 0);
                Interlocked.Exchange(ref _dashboardSamplesReceived, 0);
                Interlocked.Exchange(ref _dashboardSamplesProjected, 0);
                Interlocked.Exchange(ref _dashboardSamplesSkipped, 0);
                Interlocked.Exchange(ref _dashboardDispatcherPosts, 0);
                Interlocked.Exchange(ref _dashboardProjectionRequeues, 0);
                Interlocked.Exchange(ref _generalSamplesReceived, 0);
                Interlocked.Exchange(ref _generalSamplesProjected, 0);
                Interlocked.Exchange(ref _generalSamplesSkipped, 0);
                Interlocked.Exchange(ref _dashboardDormancyActivations, 0);
                Interlocked.Exchange(ref _dashboardDormancySamplesProjected, 0);
                Interlocked.Exchange(ref _hiddenSurfaceSamplesSkipped, 0);
                Interlocked.Exchange(ref _trayRenderCacheHits, 0);
                Interlocked.Exchange(ref _trayRenderCacheMisses, 0);
                Interlocked.Exchange(ref _popupRenderCacheHits, 0);
                Interlocked.Exchange(ref _popupRenderCacheMisses, 0);
                Interlocked.Exchange(ref _latestSampleReplacements, 0);
                Interlocked.Exchange(ref _fanTelemetrySyncs, 0);
                Interlocked.Exchange(ref _fanTelemetryCollectionResizes, 0);
                Interlocked.Exchange(ref _fanTelemetryItemsUpdated, 0);
                Interlocked.Exchange(ref _fanTelemetryPropertyOnlySyncs, 0);
            }
        }
    }

    public sealed record RuntimeUiPerformanceCounterSnapshot(
        double UptimeSeconds,
        long DispatcherBeginInvokePosts,
        long DispatcherInvokes,
        long MainMonitoringSamplesQueued,
        long MainMonitoringSamplesCoalesced,
        long MainMonitoringSamplesProjected,
        long DashboardSamplesReceived,
        long DashboardSamplesProjected,
        long DashboardSamplesSkipped,
        long DashboardDispatcherPosts,
        long DashboardProjectionRequeues,
        long GeneralSamplesReceived,
        long GeneralSamplesProjected,
        long GeneralSamplesSkipped,
        long TotalProjectedSamples,
        double DispatcherBeginInvokePostsPerSecond,
        double DispatcherInvokesPerSecond,
        double DashboardProjectedSamplesPerSecond,
        double GeneralProjectedSamplesPerSecond,
        double MainProjectedSamplesPerSecond,
        double ProjectionAmplificationRatio,
        double DispatcherAmplificationRatio,
        double DashboardProjectionAcceptanceRatio,
        double GeneralProjectionAcceptanceRatio,
        double MainProjectionAcceptanceRatio,
        // v3.6.2 Field Validation Counters
        long DashboardDormancyActivations,
        long DashboardDormancySamplesProjected,
        long HiddenSurfaceSamplesSkipped,
        long TrayRenderCacheHits,
        long TrayRenderCacheMisses,
        long PopupRenderCacheHits,
        long PopupRenderCacheMisses,
        long LatestSampleReplacements,
        long FanTelemetrySyncs,
        long FanTelemetryCollectionResizes,
        long FanTelemetryItemsUpdated,
        long FanTelemetryPropertyOnlySyncs,
        double TrayRenderCacheHitRatio,
        double PopupRenderCacheHitRatio,
        double FanTelemetryCollectionResizeRatio,
        double FanTelemetryPropertyOnlySyncRatio);
}
