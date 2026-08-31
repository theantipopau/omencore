using System;

namespace OmenCore.Services
{
    /// <summary>
    /// Serializes EC operation sections across runtime services to reduce write contention.
    /// </summary>
    public sealed class RuntimeEcOperationCoordinator
    {
        private readonly object _ecOperationGate = new();
        private readonly LoggingService _logging;

        public RuntimeEcOperationCoordinator(LoggingService logging)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        public T Execute<T>(string owner, string operationName, Func<T> action)
        {
            if (string.IsNullOrWhiteSpace(owner))
            {
                throw new ArgumentException("Owner is required", nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("Operation name is required", nameof(operationName));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_ecOperationGate)
            {
                _logging.Debug($"[EcCoordinator] Begin {owner}.{operationName}");
                try
                {
                    return action();
                }
                finally
                {
                    _logging.Debug($"[EcCoordinator] End {owner}.{operationName}");
                }
            }
        }

        public void Execute(string owner, string operationName, Action action)
        {
            Execute(owner, operationName, () =>
            {
                action();
                return true;
            });
        }
    }
}