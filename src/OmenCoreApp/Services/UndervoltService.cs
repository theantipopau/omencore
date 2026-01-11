using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    public class UndervoltService : IDisposable
    {
        private readonly ICpuUndervoltProvider _provider;
        private readonly LoggingService _logging;
        private readonly TimeSpan _pollInterval;
        private readonly object _statusLock = new();
        private CancellationTokenSource? _cts;
        private UndervoltStatus _status = UndervoltStatus.CreateUnknown();

        public event EventHandler<UndervoltStatus>? StatusChanged;

        public ICpuUndervoltProvider Provider => _provider;

        public UndervoltService(ICpuUndervoltProvider provider, LoggingService logging, int pollIntervalMs)
        {
            _provider = provider;
            _logging = logging;
            _pollInterval = TimeSpan.FromMilliseconds(Math.Clamp(pollIntervalMs, 2000, 15000));
        }

        public UndervoltStatus CurrentStatus
        {
            get
            {
                lock (_statusLock)
                {
                    return _status;
                }
            }
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        public async Task ApplyAsync(UndervoltOffset offset, CancellationToken token = default)
        {
            try
            {
                await _provider.ApplyOffsetAsync(offset, token);
                _logging.Info($"Undervolt -> core {offset.CoreMv} mV cache {offset.CacheMv} mV");
                await RefreshAsync(token);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to apply undervolt", ex);
                UpdateStatus(UndervoltStatus.CreateUnknown("Undervolt apply failed"));
            }
        }

        public async Task ResetAsync(CancellationToken token = default)
        {
            try
            {
                await _provider.ResetAsync(token);
                _logging.Info("Undervolt offsets reset to default");
                await RefreshAsync(token);
            }
            catch (Exception ex)
            {
                _logging.Error("Failed to reset undervolt", ex);
                UpdateStatus(UndervoltStatus.CreateUnknown("Reset failed"));
            }
        }

        public async Task RefreshAsync(CancellationToken token = default)
        {
            try
            {
                var status = await _provider.ProbeAsync(token);
                UpdateStatus(status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logging.Error("Unable to read undervolt status", ex);
                UpdateStatus(UndervoltStatus.CreateUnknown("Telemetry unavailable"));
            }
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logging.Error("Undervolt monitor loop error", ex);
                }

                try
                {
                    await Task.Delay(_pollInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void UpdateStatus(UndervoltStatus status)
        {
            lock (_statusLock)
            {
                _status = status;
            }
            StatusChanged?.Invoke(this, status);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
