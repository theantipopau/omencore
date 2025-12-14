using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace OmenCore.Services
{
    /// <summary>
    /// Log verbosity levels. Higher values include more detail.
    /// </summary>
    public enum LogLevel
    {
        Error = 0,   // Only errors
        Warning = 1, // Errors + warnings
        Info = 2,    // Normal logging (default)
        Debug = 3    // Verbose with diagnostic info
    }

    public sealed class LoggingService : IDisposable
    {
        private readonly BlockingCollection<string> _queue = new();
        private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore");
        private readonly string _fileName;
        private Thread? _writerThread;
        private bool _disposed;
        
        /// <summary>
        /// Current log verbosity level. Can be changed at runtime.
        /// </summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        public event Action<string>? LogEmitted;

        public LoggingService()
        {
            Directory.CreateDirectory(_logDirectory);
            _fileName = Path.Combine(_logDirectory, $"OmenCore_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public void Initialize()
        {
            _writerThread = new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "OmenCore.Logging"
            };
            _writerThread.Start();
        }

        /// <summary>
        /// Log an informational message.
        /// </summary>
        public void Info(string message) => Enqueue("INFO", message, LogLevel.Info);
        
        /// <summary>
        /// Log a warning message.
        /// </summary>
        public void Warn(string message) => Enqueue("WARN", message, LogLevel.Warning);
        
        /// <summary>
        /// Log an error message, optionally with exception details.
        /// </summary>
        public void Error(string message, Exception? ex = null) => Enqueue("ERROR", ex is null ? message : $"{message}: {ex}", LogLevel.Error);

        /// <summary>
        /// Log a debug/verbose message. Only shown when Level is Debug.
        /// </summary>
        public void Debug(string message) => Enqueue("DEBUG", message, LogLevel.Debug);

        private void Enqueue(string level, string message, LogLevel msgLevel)
        {
            // Filter messages based on current verbosity level
            if (msgLevel > Level) return;
            
            // Skip empty messages when not in Debug mode
            if (string.IsNullOrWhiteSpace(message) && Level < LogLevel.Debug) return;
            
            var line = $"{DateTime.Now:O} [{level}] {message}";
            _queue.Add(line);
            Dispatch(line);
        }

        private void Dispatch(string entry) => LogEmitted?.Invoke(entry);

        private void FlushLoop()
        {
            using var stream = new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            foreach (var entry in _queue.GetConsumingEnumerable())
            {
                writer.WriteLine(entry);
                writer.Flush();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _queue.CompleteAdding();
            // Give the writer thread a moment to flush the remaining log lines
            _writerThread?.Join(TimeSpan.FromSeconds(2));
            _disposed = true;
        }
    }
}
