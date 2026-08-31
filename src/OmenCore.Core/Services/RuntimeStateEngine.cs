using System;

namespace OmenCore.Services
{
    /// <summary>
    /// Lightweight centralized runtime projection used by subscriber surfaces.
    /// Publishes runtime projection state for read-only subscribers.
    /// </summary>
    public sealed class RuntimeStateEngine
    {
        private readonly object _gate = new();

        private string _fanMode = "Auto";
        private string _performanceMode = "Balanced";
        private string _curvePresetName = "Auto";
        private bool _isFanPerformanceLinked;

        public event EventHandler<RuntimeStateSnapshot>? StateChanged;

        public RuntimeStateSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new RuntimeStateSnapshot(
                    _fanMode,
                    _performanceMode,
                    _curvePresetName,
                    _isFanPerformanceLinked);
            }
        }

        public void PublishProjection(string fanMode, string performanceMode, string? curvePresetName, bool isFanPerformanceLinked)
        {
            fanMode = string.IsNullOrWhiteSpace(fanMode) ? "Auto" : fanMode;
            performanceMode = string.IsNullOrWhiteSpace(performanceMode) ? "Balanced" : performanceMode;
            curvePresetName = string.IsNullOrWhiteSpace(curvePresetName) ? "Auto" : curvePresetName;

            RuntimeStateSnapshot? changedSnapshot = null;

            lock (_gate)
            {
                if (string.Equals(_fanMode, fanMode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_performanceMode, performanceMode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_curvePresetName, curvePresetName, StringComparison.OrdinalIgnoreCase) &&
                    _isFanPerformanceLinked == isFanPerformanceLinked)
                {
                    return;
                }

                _fanMode = fanMode;
                _performanceMode = performanceMode;
                _curvePresetName = curvePresetName;
                _isFanPerformanceLinked = isFanPerformanceLinked;
                changedSnapshot = new RuntimeStateSnapshot(
                    _fanMode,
                    _performanceMode,
                    _curvePresetName,
                    _isFanPerformanceLinked);
            }

            var handlers = StateChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<RuntimeStateSnapshot> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, changedSnapshot);
                }
                catch (Exception ex)
                {
                    // Projection subscribers are read-only surfaces. A broken tray/OSD/dashboard
                    // listener must not prevent other surfaces from receiving confirmed runtime state.
                    System.Diagnostics.Debug.WriteLine($"RuntimeStateEngine subscriber failed: {ex.Message}");
                }
            }
        }
    }

    public sealed record RuntimeStateSnapshot(
        string FanMode,
        string PerformanceMode,
        string CurvePresetName,
        bool IsFanPerformanceLinked);
}
