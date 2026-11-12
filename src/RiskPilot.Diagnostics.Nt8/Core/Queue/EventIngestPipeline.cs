using System;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Bounded ingest path between the NinjaTrader callbacks and the diagnostic core.
    /// <see cref="Ingest"/> is designed to run on the host callback thread and must
    /// return immediately: it stamps the record, enqueues it without blocking, and
    /// updates counters. All heavy processing happens later on the drain thread.
    /// </summary>
    public sealed class EventIngestPipeline
    {
        private readonly EventQueue<NormalizedRecord> _queue;
        private readonly DuplicateEventDetector _duplicates;
        private readonly SequenceGapDetector _gaps = new SequenceGapDetector();
        private readonly IClock _clock;
        private long _sequence;

        public EventIngestPipeline(int capacity, int duplicateWindow, IClock clock)
        {
            _queue = new EventQueue<NormalizedRecord>(capacity);
            _duplicates = new DuplicateEventDetector(duplicateWindow);
            _clock = clock ?? SystemClock.Instance;
        }

        public EventQueue<NormalizedRecord> Queue { get { return _queue; } }

        public long DuplicateCount { get { return _duplicates.DuplicateCount; } }

        public SequenceGapDetector GapDetector { get { return _gaps; } }

        /// <summary>
        /// Stamps and enqueues a record for the given session. Returns false when the
        /// record was dropped (queue full) or suppressed as a duplicate; the caller can
        /// distinguish the two by inspecting the session counters.
        /// </summary>
        public bool Ingest(SessionContext session, NormalizedRecord record)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (record == null)
            {
                return false;
            }

            var now = _clock.UtcNow;
            record.SessionId = session.SessionId;
            record.IngestedAtUtc = now;
            record.Sequence = System.Threading.Interlocked.Increment(ref _sequence);
            if (record.SourceTimeBasis == SourceTimeBasis.Unknown)
            {
                record.SourceTimeBasis = session.SourceTimeBasis;
            }

            // An implausible future source timestamp (beyond a small clock-skew tolerance)
            // is counted rather than trusted; it never silently counts as clean data.
            if (record.SourceTimestampUtc.HasValue
                && record.SourceTimestampUtc.Value.ToUniversalTime() > now.AddMinutes(5))
            {
                session.RecordTimestampInvalid();
            }

            var duplicateKey = record.DuplicateIdentity();
            if (_duplicates.Observe(duplicateKey))
            {
                session.RecordDuplicate();
                return false;
            }

            // Sequence gaps are detected per stream, independently of the internal
            // sequence, so lost source events can never be reported as a clean pass.
            if (record.SourceSequence.HasValue)
            {
                var missing = _gaps.Observe(record.StreamKey, record.SourceSequence.Value);
                if (missing > 0)
                {
                    session.RecordSequenceGap(missing);
                    AttributeGap(session, record.StreamKey, missing);
                }
            }

            session.RecordEnqueued();
            if (!_queue.TryEnqueue(record))
            {
                session.RecordDropped(1);
                AttributeDrop(session, record.StreamKey);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attributes a per-stream gap to the data domain it belongs to, so M5 (DOM/market
        /// data) and M6 (order-flow trade stream) each report the gaps that affect them
        /// instead of a single merged number. The streams themselves are never merged.
        /// </summary>
        private static void AttributeGap(SessionContext session, string streamKey, long missing)
        {
            var counters = session.Counters;
            if (IsMarketDataStream(streamKey))
            {
                counters.Increment(RuntimeCounterKeys.MarketDataSequenceGaps, missing);
            }

            if (IsTradeStream(streamKey))
            {
                counters.Increment(RuntimeCounterKeys.TradeDeltaSequenceGaps, missing);
            }
        }

        /// <summary>Attributes a queue-overflow drop to its data domain.</summary>
        private static void AttributeDrop(SessionContext session, string streamKey)
        {
            if (IsMarketDataStream(streamKey))
            {
                session.Counters.Increment(RuntimeCounterKeys.MarketDataDroppedEvents);
            }
        }

        private static bool IsMarketDataStream(string streamKey)
        {
            return streamKey != null
                && (streamKey.StartsWith("MarketData", System.StringComparison.Ordinal)
                    || streamKey.StartsWith("Dom", System.StringComparison.Ordinal));
        }

        private static bool IsTradeStream(string streamKey)
        {
            return streamKey != null
                && streamKey.StartsWith("MarketData|Trade", System.StringComparison.Ordinal);
        }
    }
}
