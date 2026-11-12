using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Tracks per-stream sequence numbers and reports missing ranges. Gaps are recorded
    /// as an <see cref="ErrorClass.SequenceGap"/>-worthy condition so that a run with
    /// lost events can never be reported as a clean pass.
    /// </summary>
    public sealed class SequenceGapDetector
    {
        private readonly Dictionary<string, long> _lastSeen = new Dictionary<string, long>();
        private readonly object _gate = new object();

        public long TotalGaps { get; private set; }

        public long TotalMissing { get; private set; }

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
            lock (_gate)
            {
                if (!_lastSeen.TryGetValue(key, out var previous))
                {
                    _lastSeen[key] = sequence;
                    return 0;
                }

                if (sequence <= previous)
                {
                    // Replay or duplicate; not a forward gap.
                    return 0;
                }

                var missing = sequence - previous - 1;
                _lastSeen[key] = sequence;
                if (missing > 0)
                {
                    TotalGaps++;
                    TotalMissing += missing;
                }

                return missing;
            }
        }

        public bool HasGaps { get { return TotalGaps > 0; } }

        public void Reset()
        {
            lock (_gate)
            {
                _lastSeen.Clear();
                TotalGaps = 0;
                TotalMissing = 0;
            }
        }
    }
}
