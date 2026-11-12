using System.Collections.Concurrent;
using System.Threading;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Tracks per-stream sequence numbers and reports missing ranges using a lock-free
    /// compare-and-swap loop. A gap means the source skipped events, so a run with lost
    /// events can never be reported as a clean pass. The observe path never blocks: it
    /// either succeeds or retries on contention without waiting on another thread.
    /// </summary>
    public sealed class SequenceGapDetector
    {
        private readonly ConcurrentDictionary<string, long> _lastSeen = new ConcurrentDictionary<string, long>();
        private long _totalGaps;
        private long _totalMissing;

        public long TotalGaps { get { return Interlocked.Read(ref _totalGaps); } }

        public long TotalMissing { get { return Interlocked.Read(ref _totalMissing); } }

        public bool HasGaps { get { return Interlocked.Read(ref _totalGaps) > 0; } }

        /// <summary>
        /// Observes a sequence number for a named stream. Returns the number of missing
        /// events since the previous observation (0 when contiguous or first-seen).
        /// A non-positive <paramref name="sequence"/> is ignored.
        /// </summary>
        public long Observe(string streamKey, long sequence)
        {
            if (sequence <= 0)
            {
                return 0;
            }

            var key = streamKey ?? "default";

            while (true)
            {
                if (!_lastSeen.TryGetValue(key, out var previous))
                {
                    if (_lastSeen.TryAdd(key, sequence))
                    {
                        return 0;
                    }

                    continue;
                }

                if (sequence <= previous)
                {
                    // Replay, duplicate, or out-of-order; not a forward gap.
                    return 0;
                }

                var missing = sequence - previous - 1;
                if (_lastSeen.TryUpdate(key, sequence, previous))
                {
                    // Exactly one thread wins this transition, so a gap is counted once.
                    if (missing > 0)
                    {
                        Interlocked.Increment(ref _totalGaps);
                        Interlocked.Add(ref _totalMissing, missing);
                    }

                    return missing;
                }

                // Lost the race; retry against the new previous value.
            }
        }

        public void Reset()
        {
            _lastSeen.Clear();
            Interlocked.Exchange(ref _totalGaps, 0);
            Interlocked.Exchange(ref _totalMissing, 0);
        }
    }
}
