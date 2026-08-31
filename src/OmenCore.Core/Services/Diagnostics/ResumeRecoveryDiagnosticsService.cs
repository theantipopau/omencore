using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Services.Diagnostics
{
    public sealed class ResumeRecoveryDiagnosticsService
    {
        private readonly object _sync = new();
        private readonly List<ResumeRecoveryEntry> _entries = new();
        private int _cycleId;
        private bool _recoveryInProgress;
        private DateTime _lastUpdatedUtc = DateTime.MinValue;

        public event EventHandler? Updated;

        public int CurrentCycleId
        {
            get
            {
                lock (_sync)
                {
                    return _cycleId;
                }
            }
        }

        public string Status { get; private set; } = "No recent resume activity";
        public string Summary { get; private set; } = "No suspend/resume recovery timeline recorded yet.";
        public bool RecoveryInProgress
        {
            get
            {
                lock (_sync)
                {
                    return _recoveryInProgress;
                }
            }
        }

        public string LastUpdatedText
        {
            get
            {
                lock (_sync)
                {
                    return _lastUpdatedUtc == DateTime.MinValue
                        ? "No recent resume event"
                        : $"Last update: {_lastUpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                }
            }
        }

        public string TimelineText
        {
            get
            {
                lock (_sync)
                {
                    if (_entries.Count == 0)
                    {
                        return "No timeline recorded yet.";
                    }

                    return string.Join(Environment.NewLine, _entries.Select(entry => entry.ToDisplayString()));
                }
            }
        }

        public void BeginSuspend()
        {
            lock (_sync)
            {
                _cycleId++;
                _entries.Clear();
                _recoveryInProgress = false;
                Status = "Suspended";
                Summary = "System suspend detected. Waiting for resume recovery to begin.";
                AddEntryNoLock("power", "Suspend detected");
            }

            NotifyUpdated();
        }

        public void BeginResume()
        {
            lock (_sync)
            {
                if (_entries.Count == 0)
                {
                    _cycleId++;
                }

                _recoveryInProgress = true;
                Status = "Recovering";
                Summary = "Resume detected. Recovery timeline is being collected.";
                AddEntryNoLock("power", "Resume detected");
            }

            NotifyUpdated();
        }

        public void RecordStep(string source, string message)
        {
            lock (_sync)
            {
                AddEntryNoLock(source, message);
            }

            NotifyUpdated();
        }

        public void Complete(string summary)
        {
            lock (_sync)
            {
                _recoveryInProgress = false;
                Status = "Healthy";
                Summary = summary;
                AddEntryNoLock("self-check", summary);
            }

            NotifyUpdated();
        }

        public void Attention(string summary)
        {
            lock (_sync)
            {
                _recoveryInProgress = false;
                Status = "Attention Needed";
                Summary = summary;
                AddEntryNoLock("self-check", summary);
            }

            NotifyUpdated();
        }

        public string BuildExportReport()
        {
            lock (_sync)
            {
                return string.Join(Environment.NewLine, new[]
                {
                    "=== RESUME RECOVERY DIAGNOSTICS ===",
                    $"Status: {Status}",
                    $"Summary: {Summary}",
                    LastUpdatedText,
                    string.Empty,
                    "Timeline:",
                    _entries.Count == 0 ? "(no entries recorded)" : string.Join(Environment.NewLine, _entries.Select(entry => entry.ToDisplayString()))
                });
            }
        }

        private void AddEntryNoLock(string source, string message)
        {
            _entries.Add(new ResumeRecoveryEntry(DateTime.UtcNow, source, message));
            if (_entries.Count > 20)
            {
                _entries.RemoveAt(0);
            }

            _lastUpdatedUtc = DateTime.UtcNow;
        }

        private void NotifyUpdated()
        {
            Updated?.Invoke(this, EventArgs.Empty);
        }

        private sealed record ResumeRecoveryEntry(DateTime TimestampUtc, string Source, string Message)
        {
            public string ToDisplayString()
            {
                return $"{TimestampUtc.ToLocalTime():HH:mm:ss} | {Source} | {Message}";
            }
        }
    }
}