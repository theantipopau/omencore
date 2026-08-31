namespace OmenCore.Services
{
    /// <summary>
    /// Abstraction for telemetry export (used by UI for one-click export).
    /// Allows unit tests to stub the behaviour.
    /// </summary>
    public interface ITelemetryService
    {
        /// <summary>
        /// Export the current telemetry file (copy) to a timestamped location and return the path.
        /// Returns null if no telemetry data exists or export fails.
        /// </summary>
        string? ExportTelemetry();
    }
}