using System;

namespace OmenCore.Services
{
    /// <summary>
    /// First-slice polling coordinator that centralizes cadence-affecting mode writes
    /// to HardwareMonitoringService.
    /// </summary>
    public sealed class RuntimePollingCoordinator
    {
        private readonly HardwareMonitoringService _monitoring;
        private readonly LoggingService _logging;

        private bool _lowOverheadMode;
        private bool _overlayRealtimeMode;
        private bool _uiWindowActive = true;
        private bool _trayOnlyMode;

        public RuntimePollingCoordinator(HardwareMonitoringService monitoring, LoggingService logging)
        {
            _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        public void SetLowOverheadMode(bool enabled)
        {
            if (_lowOverheadMode == enabled)
            {
                return;
            }

            _lowOverheadMode = enabled;
            _monitoring.SetLowOverheadMode(enabled);
            _logging.Debug($"[PollingCoordinator] LowOverheadMode={enabled}");
        }

        public void SetOverlayRealtimeMode(bool enabled)
        {
            if (_overlayRealtimeMode == enabled)
            {
                return;
            }

            _overlayRealtimeMode = enabled;
            _monitoring.SetOverlayRealtimeMode(enabled);
            _logging.Debug($"[PollingCoordinator] OverlayRealtimeMode={enabled}");
        }

        public void SetUiWindowActive(bool active)
        {
            if (_uiWindowActive == active)
            {
                return;
            }

            _uiWindowActive = active;
            _monitoring.SetUiWindowActive(active);
            _logging.Debug($"[PollingCoordinator] UiWindowActive={active}");
        }

        public void SetTrayOnlyMode(bool trayOnly)
        {
            if (_trayOnlyMode == trayOnly)
            {
                return;
            }

            _trayOnlyMode = trayOnly;
            _monitoring.SetTrayOnlyMode(trayOnly);
            _logging.Debug($"[PollingCoordinator] TrayOnlyMode={trayOnly}");
        }
    }
}
