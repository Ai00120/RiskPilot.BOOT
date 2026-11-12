using System;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Session
{
    /// <summary>
    /// Mutable, thread-safe counters for one diagnostic session. Session isolation is
    /// enforced by construction: a new context is created for every Start and all
    /// counters are scoped to it. Counter updates use <see cref="Interlocked"/>, so the
    /// ingest path updates counters atomically without acquiring a user-level lock and
    /// never blocks the host callback thread on another thread's progress.
    /// </summary>
    public sealed class SessionContext
    {
        private long _enqueued;
        private long _processed;
        private long _dropped;
        private long _duplicates;
        private long _sequenceGaps;
        private long _timestampInvalid;

        public SessionContext(string sessionId, DateTime startedAtUtc)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("sessionId must be provided", nameof(sessionId));
            }

            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified;
            Counters = new RuntimeCounters();
        }

        public string SessionId { get; private set; }

        public DateTime StartedAtUtc { get; private set; }

        /// <summary>
        /// Shared runtime counters populated by the M3–M8 event consumers on the drain
        /// thread and read by the probes on the diagnostic pass. Session-scoped, so a new
        /// session starts from zero.
        /// </summary>
        public RuntimeCounters Counters { get; private set; }

        /// <summary>Trust basis asserted for source timestamps in this session.</summary>
        public SourceTimeBasis SourceTimeBasis { get; set; }

        public long EnqueuedCount { get { return Interlocked.Read(ref _enqueued); } }

        public long ProcessedCount { get { return Interlocked.Read(ref _processed); } }

        public long DroppedCount { get { return Interlocked.Read(ref _dropped); } }

        public long DuplicateCount { get { return Interlocked.Read(ref _duplicates); } }

        public long SequenceGapCount { get { return Interlocked.Read(ref _sequenceGaps); } }

        public long TimestampInvalidCount { get { return Interlocked.Read(ref _timestampInvalid); } }

        public void RecordEnqueued() { Interlocked.Increment(ref _enqueued); }

        public void RecordProcessed() { Interlocked.Increment(ref _processed); }

        public void RecordDropped(long count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _dropped, count);
            }
        }

        public void RecordDuplicate() { Interlocked.Increment(ref _duplicates); }

        public void RecordSequenceGap(long count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _sequenceGaps, count);
            }
        }

        public void RecordTimestampInvalid() { Interlocked.Increment(ref _timestampInvalid); }

        /// <summary>True when any event was lost and the report must not claim a clean pass.</summary>
        public bool HasDataLoss
        {
            get { return DroppedCount > 0 || SequenceGapCount > 0; }
        }
    }
}
