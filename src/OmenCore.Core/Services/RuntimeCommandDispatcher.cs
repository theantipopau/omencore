using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmenCore.Services
{
    /// <summary>
    /// Serializes runtime commands using a latest-wins queue to avoid overlapping
    /// orchestration writes from tray/hotkey/UI triggers.
    /// </summary>
    public sealed class RuntimeCommandDispatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _contextName;
        private readonly Action<string>? _logDebug;
        private readonly Action<string>? _logWarn;
        private readonly Action<string>? _logInfo;
        private readonly CancellationTokenSource _workerCts = new();

        private Func<Task>? _pendingCommand;
        private string _pendingName = string.Empty;
        private bool _workerRunning;

        public RuntimeCommandDispatcher(
            string contextName,
            Action<string>? logDebug = null,
            Action<string>? logWarn = null,
            Action<string>? logInfo = null)
        {
            _contextName = string.IsNullOrWhiteSpace(contextName) ? "RuntimeCommand" : contextName;
            _logDebug = logDebug;
            _logWarn = logWarn;
            _logInfo = logInfo;
        }

        public void EnqueueLatest(string commandName, Func<Task> command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            lock (_gate)
            {
                _pendingName = commandName;
                _pendingCommand = command;

                if (_workerRunning)
                {
                    _logDebug?.Invoke($"{_contextName}: command '{commandName}' queued as latest (last-write-wins)");
                    return;
                }

                _workerRunning = true;
            }

            _ = Task.Run(ProcessQueueAsync);
        }

        private async Task ProcessQueueAsync()
        {
            var ct = _workerCts.Token;
            while (!ct.IsCancellationRequested)
            {
                Func<Task>? next;
                string name;

                lock (_gate)
                {
                    next = _pendingCommand;
                    name = _pendingName;
                    _pendingCommand = null;
                    _pendingName = string.Empty;

                    if (next == null)
                    {
                        _workerRunning = false;
                        return;
                    }
                }

                try
                {
                    await next();
                }
                catch (OperationCanceledException)
                {
                    _logInfo?.Invoke($"{_contextName}: worker cancelled during shutdown");
                    break;
                }
                catch (Exception ex)
                {
                    _logWarn?.Invoke($"{_contextName}: command '{name}' failed: {ex.Message}");
                }
            }

            lock (_gate)
            {
                _workerRunning = false;
            }
        }

        public void Dispose()
        {
            _workerCts.Cancel();
            _workerCts.Dispose();
        }
    }
}
