using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Bounded duplicate detector whose observe path is non-blocking. The host callback
    /// thread performs only atomic operations and concurrent-collection calls: it never
    /// acquires a user-level lock held across other threads' work and never waits on the
    /// drain thread. If another thread is currently trimming the window, the caller returns
    /// immediately instead of waiting.
    /// <para>
    /// The window is a bounded, documented approximation: a key whose entry has already
    /// been evicted may be reported as new again. A key is only ever reported as a duplicate
    /// while an identical identity is still inside the window. Duplicates are counted,
    /// never silently merged.
    /// </para>
    /// </summary>
    public sealed class DuplicateEventDetector
    {
        private readonly int _window;
        private readonly ConcurrentDictionary<string, byte> _seen = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentQueue<string> _order = new ConcurrentQueue<string>();
        private int _orderCount;
        private int _trimming;
        private long _duplicateCount;

        public DuplicateEventDetector(int window)
        {
            if (window <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(window), "window must be positive");
            }

            _window = window;
        }

        public long DuplicateCount { get { return Interlocked.Read(ref _duplicateCount); } }

        /// <summary>
        /// Registers a key. Returns true when an identical key is still inside the window
        /// (a duplicate). A null or empty key is never treated as a duplicate.
        /// </summary>
        public bool Observe(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (!_seen.TryAdd(key, 0))
            {
                Interlocked.Increment(ref _duplicateCount);
                return true;
            }

            _order.Enqueue(key);
            Interlocked.Increment(ref _orderCount);
            Trim();
            return false;
        }

        private void Trim()
        {
            // Try-acquire: a contended caller returns immediately rather than blocking, so
            // the callback path is never gated on another thread finishing its trim.
            if (Interlocked.CompareExchange(ref _trimming, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (Volatile.Read(ref _orderCount) > _window)
                {
                    if (!_order.TryDequeue(out var evicted))
                    {
                        break;
                    }

                    Interlocked.Decrement(ref _orderCount);
                    _seen.TryRemove(evicted, out _);
                }
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        }

        public void Reset()
        {
            while (_order.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _orderCount, 0);
            _seen.Clear();
            Interlocked.Exchange(ref _duplicateCount, 0);
        }
    }
}
