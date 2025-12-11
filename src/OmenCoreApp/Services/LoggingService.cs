using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace OmenCore.Services
{
    public sealed class LoggingService : IDisposable
    {
        private readonly BlockingCollection<string> _queue = new();
        private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmenCore");
        private readonly string _fileName;
        private Thread? _writerThread;
        private bool _disposed;

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

        public void Info(string message) => Enqueue("INFO", message);
        public void Warn(string message) => Enqueue("WARN", message);
        public void Error(string message, Exception? ex = null) => Enqueue("ERROR", ex is null ? message : $"{message}: {ex}");

        private void Enqueue(string level, string message)
        {
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
