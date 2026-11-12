using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Logging
{
    /// <summary>Severity of a diagnostic log entry.</summary>
    public enum LogLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>A single immutable, already-redacted log entry.</summary>
    public sealed class DiagnosticLogEntry
    {
        public DateTime TimestampUtc { get; set; }

        public LogLevel Level { get; set; }

        public string CorrelationId { get; set; }

        public string SessionId { get; set; }

        public string Message { get; set; }

        public DiagnosticLogEntry()
        {
            CorrelationId = string.Empty;
            SessionId = string.Empty;
            Message = string.Empty;
        }

        public string ToLine()
        {
            return string.Format(
                "{0:yyyy-MM-ddTHH:mm:ss.fffZ} [{1}] corr={2} session={3} {4}",
                TimestampUtc,
                Level.ToString().ToUpperInvariant(),
                CorrelationId,
                SessionId,
                Message);
        }
    }

    /// <summary>
    /// Bounded, thread-safe log sink. Every message is redacted on the way in, and the
    /// buffer is capped so logging can never grow without bound or block the caller.
    /// </summary>
    public sealed class DiagnosticLog
    {
        private readonly int _capacity;
        private readonly List<DiagnosticLogEntry> _entries = new List<DiagnosticLogEntry>();
        private readonly object _gate = new object();
        private long _dropped;

        public DiagnosticLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
            }

            _capacity = capacity;
        }

        public long DroppedCount { get { lock (_gate) { return _dropped; } } }

        public int Count { get { lock (_gate) { return _entries.Count; } } }

        public void Write(DateTime utcNow, LogLevel level, string correlationId, string sessionId, string message)
        {
            var entry = new DiagnosticLogEntry
            {
                TimestampUtc = utcNow,
                Level = level,
                CorrelationId = RiskPilot.Diagnostics.Nt8.Core.Identity.CorrelationId.Normalize(correlationId),
                SessionId = sessionId ?? string.Empty,
                Message = SecretRedactor.Redact(message)
            };

            lock (_gate)
            {
                if (_entries.Count >= _capacity)
                {
                    // Ring behaviour: drop the oldest so the most recent context survives.
                    _entries.RemoveAt(0);
                    _dropped++;
                }

                _entries.Add(entry);
            }
        }

        public IReadOnlyList<DiagnosticLogEntry> Snapshot()
        {
            lock (_gate)
            {
                return new List<DiagnosticLogEntry>(_entries);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }
    }
}
