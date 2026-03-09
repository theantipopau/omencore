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
        private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore", "logs");
        private readonly string _fileName;
        private Thread? _writerThread;
        private bool _disposed;
        private readonly bool _fileLoggingEnabled; // allow disabling file logging via env var (useful for tests)
        
        /// <summary>
        /// Current log verbosity level. Can be changed at runtime.
        /// </summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>
        /// Directory where log files are written. Used by diagnostics export.
        /// </summary>
        public string LogDirectory => _logDirectory;

        public event Action<string>? LogEmitted;

        public LoggingService()
        {
            Directory.CreateDirectory(_logDirectory);
            _fileName = Path.Combine(_logDirectory, $"OmenCore_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // Allow tests or environments to disable file logging
            var env = Environment.GetEnvironmentVariable("OMENCORE_DISABLE_FILE_LOG");
            _fileLoggingEnabled = !string.Equals(env, "1", StringComparison.OrdinalIgnoreCase);
        }

        public void Initialize()
        {
            if (!_fileLoggingEnabled)
            {
                // File logging disabled for this session (tests or user preference)
                return;
            }

            _writerThread = new Thread(FlushLoop)
            {
                IsBackground = true,
                Name = "OmenCore.Logging"
            };
            _writerThread.Start();
            
            // Clean up old logs asynchronously
            Task.Run(CleanupOldLogs);
        }

        private void CleanupOldLogs()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-7);
                var files = Directory.GetFiles(_logDirectory, "OmenCore_*.log");
                
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    if (fi.CreationTime < cutoff)
                    {
                        fi.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't log this error to avoid recursion if logging fails
                System.Diagnostics.Debug.WriteLine($"Failed to clean up logs: {ex.Message}");
            }
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
            try
            {
                // Try to open the configured log file; if it's locked, fall back to a per-process temp log file
                string pathToUse = _fileName;

                FileStream? stream = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        // Allow other readers/writers where possible (ReadWrite sharing)
                        stream = new FileStream(pathToUse, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        break;
                    }
                    catch (IOException)
                    {
                        // If the primary file is locked, fall back to a temporary file in local appdata
                        if (attempt == 0)
                        {
                            var fallbackName = Path.Combine(_logDirectory, $"OmenCore_{Environment.ProcessId}_fallback.log");
                            pathToUse = fallbackName;
                        }
                        else
                        {
                            // Last resort: system temp file
                            pathToUse = Path.GetTempFileName();
                        }
                    }
                }

                if (stream == null)
                {
                    // If all attempts failed, emit to Debug and discard incoming entries
                    System.Diagnostics.Debug.WriteLine("LoggingService: Failed to open any log file; logging disabled for this session.");
                    foreach (var entry in _queue.GetConsumingEnumerable())
                    {
                        // No-op: discard
                    }
                    return;
                }

                using var writer = new StreamWriter(stream, Encoding.UTF8);
                foreach (var entry in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        writer.WriteLine(entry);
                        writer.Flush();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoggingService: Failed to write log entry: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Swallow any fatal exception in logging thread to avoid crashing the host process
                System.Diagnostics.Debug.WriteLine($"LoggingService: Fatal error in FlushLoop: {ex.Message}");
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
