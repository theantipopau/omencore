using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Models;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Service that monitors for EDP (Electrical Design Point) throttling and applies mitigation strategies.
    /// </summary>
    public class EdpThrottlingMitigationService : IDisposable
    {
        private const string MonitorTimerRegistryName = "EdpThrottlingMitigationMonitor";

        private readonly IMsrAccess? _msrAccess;
        private readonly UndervoltService _undervoltService;
        private readonly LoggingService _logging;
        private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(5);
        private CancellationTokenSource? _cts;
        private bool _isMitigating;
        private UndervoltOffset? _originalOffset;

        public event EventHandler<EdpThrottlingEventArgs>? ThrottlingDetected;
        public event EventHandler<EdpThrottlingEventArgs>? MitigationApplied;
        public event EventHandler<EdpThrottlingEventArgs>? MitigationRemoved;

        public bool IsEnabled { get; set; } = true;
        public int MitigationUndervoltMv { get; set; } = -50; // Additional -50mV when throttling detected

        public EdpThrottlingMitigationService(
            IMsrAccess? msrAccess,
            UndervoltService undervoltService,
            LoggingService logging)
        {
            _msrAccess = msrAccess;
            _undervoltService = undervoltService;
            _logging = logging;
        }

        public void Start()
        {
            Stop();
            if (!IsEnabled || _msrAccess == null)
                return;

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorLoopAsync(_cts.Token));
            BackgroundTimerRegistry.Register(
                MonitorTimerRegistryName,
                nameof(EdpThrottlingMitigationService),
                "EDP/power-throttling mitigation monitor",
                (int)_monitorInterval.TotalMilliseconds,
                BackgroundTimerTier.Critical);
            _logging.Info("EDP throttling mitigation service started");
        }

        public void Stop()
        {
            if (_cts == null)
            {
                BackgroundTimerRegistry.Unregister(MonitorTimerRegistryName);
                return;
            }

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            BackgroundTimerRegistry.Unregister(MonitorTimerRegistryName);

            // Remove any active mitigation
            if (_isMitigating)
            {
                _ = RemoveMitigationAsync();
            }

            _logging.Info("EDP throttling mitigation service stopped");
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_msrAccess == null)
                        continue;

                    bool isThrottling = _msrAccess.ReadPowerThrottlingStatus();

                    if (isThrottling && !_isMitigating && IsEnabled)
                    {
                        await ApplyMitigationAsync(token);
                    }
                    else if (!isThrottling && _isMitigating)
                    {
                        await RemoveMitigationAsync(token);
                    }
                }
                catch (Exception ex)
                {
                    _logging.Error("EDP throttling monitor error", ex);
                }

                try
                {
                    await Task.Delay(_monitorInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ApplyMitigationAsync(CancellationToken token = default)
        {
            try
            {
                // Store original offset
                var currentStatus = _undervoltService.CurrentStatus;
                if (currentStatus.ControlledByOmenCore)
                {
                    _originalOffset = new UndervoltOffset
                    {
                        CoreMv = currentStatus.CurrentCoreOffsetMv,
                        CacheMv = currentStatus.CurrentCacheOffsetMv
                    };
                }

                // Apply additional undervolt
                var mitigationOffset = new UndervoltOffset
                {
                    CoreMv = (_originalOffset?.CoreMv ?? 0) + MitigationUndervoltMv,
                    CacheMv = (_originalOffset?.CacheMv ?? 0) + MitigationUndervoltMv
                };

                await _undervoltService.ApplyAsync(mitigationOffset, token);
                _isMitigating = true;

                var args = new EdpThrottlingEventArgs
                {
                    Timestamp = DateTime.Now,
                    IsThrottling = true,
                    MitigationApplied = true,
                    UndervoltOffsetMv = MitigationUndervoltMv
                };

                ThrottlingDetected?.Invoke(this, args);
                MitigationApplied?.Invoke(this, args);

                _logging.Info($"EDP throttling detected - applied {MitigationUndervoltMv}mV additional undervolt");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to apply EDP throttling mitigation", ex);
            }
        }

        private async Task RemoveMitigationAsync(CancellationToken token = default)
        {
            try
            {
                if (_originalOffset != null)
                {
                    // Restore original offset
                    await _undervoltService.ApplyAsync(_originalOffset, token);
                }
                else
                {
                    // Reset to defaults
                    await _undervoltService.ResetAsync(token);
                }

                _isMitigating = false;
                _originalOffset = null;

                var args = new EdpThrottlingEventArgs
                {
                    Timestamp = DateTime.Now,
                    IsThrottling = false,
                    MitigationApplied = false,
                    UndervoltOffsetMv = 0
                };

                MitigationRemoved?.Invoke(this, args);

                _logging.Info("EDP throttling ended - removed mitigation");
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to remove EDP throttling mitigation", ex);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class EdpThrottlingEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public bool IsThrottling { get; set; }
        public bool MitigationApplied { get; set; }
        public int UndervoltOffsetMv { get; set; }
    }
}
