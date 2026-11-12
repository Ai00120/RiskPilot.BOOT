using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Bounded duplicate detector. It keeps the most recent keys in a sliding window so
    /// memory stays flat under sustained load; keys older than the window may be treated
    /// as new. Duplicates are counted, never silently merged.
    /// </summary>
    public sealed class DuplicateEventDetector
    {
        private readonly int _window;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<string> _order = new Queue<string>();
        private readonly object _gate = new object();

        public DuplicateEventDetector(int window)
        {
            if (window <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(window), "window must be positive");
            }

            _window = window;
        }

        public long DuplicateCount { get; private set; }

        /// <summary>
        /// Registers a key. Returns true when the key was already present (a duplicate).
        /// A null or empty key is never treated as a duplicate.
        /// </summary>
        public bool Observe(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            lock (_gate)
            {
                if (_seen.Contains(key))
                {
                    DuplicateCount++;
                    return true;
                }

                _seen.Add(key);
                _order.Enqueue(key);
                if (_order.Count > _window)
                {
                    var evicted = _order.Dequeue();
                    _seen.Remove(evicted);
                }

                return false;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _seen.Clear();
                _order.Clear();
                DuplicateCount = 0;
            }
        }
    }
}
