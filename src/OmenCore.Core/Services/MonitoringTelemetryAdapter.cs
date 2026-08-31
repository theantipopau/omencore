using System;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Compatibility shim that enriches legacy MonitoringSample scalar/state fields
    /// with explicit TelemetryValue envelopes for incremental migration.
    /// </summary>
    public static class MonitoringTelemetryAdapter
    {
        public static TelemetryValue<double> BuildCpuTemperatureTelemetry(
            MonitoringSample sample,
            string backendSource,
            bool monitoringStale,
            string? lastError)
        {
            var telemetry = new TelemetryValue<double>
            {
                BackendSource = backendSource ?? string.Empty,
                LastUpdatedUtc = sample.Timestamp == default
                    ? DateTime.UtcNow
                    : sample.Timestamp.ToUniversalTime(),
                IsStale = monitoringStale || sample.CpuTemperatureState == TelemetryDataState.Stale,
                LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError
            };

            var state = sample.CpuTemperatureState;
            var hasPositiveValue = sample.CpuTemperatureC > 0;

            telemetry.IsSupported = state switch
            {
                TelemetryDataState.Unavailable => false,
                TelemetryDataState.Invalid => false,
                TelemetryDataState.Unknown => hasPositiveValue,
                _ => true
            };

            if (telemetry.IsSupported && hasPositiveValue)
            {
                telemetry.Value = sample.CpuTemperatureC;
            }

            return telemetry;
        }

        public static TelemetryValue<double> BuildCpuPowerTelemetry(
            MonitoringSample sample,
            string backendSource,
            bool monitoringStale,
            string? lastError)
        {
            var telemetry = new TelemetryValue<double>
            {
                BackendSource = backendSource ?? string.Empty,
                LastUpdatedUtc = sample.Timestamp == default
                    ? DateTime.UtcNow
                    : sample.Timestamp.ToUniversalTime(),
                IsStale = monitoringStale,
                LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError
            };

            var state = sample.CpuPowerState;
            var hasPositiveValue = sample.CpuPowerWatts > 0;

            telemetry.IsSupported = state switch
            {
                TelemetryDataState.Unavailable => false,
                TelemetryDataState.Invalid => false,
                TelemetryDataState.Unknown => hasPositiveValue,
                _ => true
            };

            if (!telemetry.IsSupported)
            {
                return telemetry;
            }

            telemetry.Value = state switch
            {
                TelemetryDataState.Zero => 0,
                _ when hasPositiveValue => sample.CpuPowerWatts,
                _ => null
            };

            return telemetry;
        }
    }
}