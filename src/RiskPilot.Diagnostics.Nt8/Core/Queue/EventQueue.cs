using System.Collections.Concurrent;
using System.Threading;

namespace RiskPilot.Diagnostics.Nt8.Core.Queue
{
    /// <summary>
    /// Bounded, thread-safe, non-blocking event queue. Enqueue never blocks the caller:
    /// when the queue is at capacity the item is dropped and counted instead of applying
    /// backpressure to the host callback thread. This is the single mechanism that keeps
    /// NinjaTrader market-data callbacks from stalling.
    /// </summary>
    /// <typeparam name="T">Event payload type.</typeparam>
    public sealed class EventQueue<T>
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly int _capacity;
        private int _count;
        private long _enqueued;
        private long _dequeued;
        private long _dropped;

        public EventQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
            }

            _capacity = capacity;
        }

        public int Capacity { get { return _capacity; } }

        public int Count { get { return Volatile.Read(ref _count); } }

        public long EnqueuedCount { get { return Interlocked.Read(ref _enqueued); } }

        public long DequeuedCount { get { return Interlocked.Read(ref _dequeued); } }

        public long DroppedCount { get { return Interlocked.Read(ref _dropped); } }

        public bool IsFull { get { return Volatile.Read(ref _count) >= _capacity; } }

        /// <summary>
        /// Attempts to enqueue without blocking. Returns false when the item was dropped
        /// because the queue was full.
        /// </summary>
        public bool TryEnqueue(T item)
        {
            var current = Interlocked.Increment(ref _count);
            if (current > _capacity)
            {
                Interlocked.Decrement(ref _count);
                Interlocked.Increment(ref _dropped);
                return false;
            }

            _queue.Enqueue(item);
            Interlocked.Increment(ref _enqueued);
            return true;
        }

        /// <summary>Attempts to dequeue without blocking.</summary>
        public bool TryDequeue(out T item)
        {
            if (_queue.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _count);
                Interlocked.Increment(ref _dequeued);
                return true;
            }

            item = default(T);
            return false;
        }

        /// <summary>Drains up to <paramref name="maxItems"/> events into the supplied buffer.</summary>
        public int Drain(T[] buffer, int maxItems)
        {
            if (buffer == null)
            {
                throw new System.ArgumentNullException(nameof(buffer));
            }

            var limit = maxItems < buffer.Length ? maxItems : buffer.Length;
            var drained = 0;
            while (drained < limit && TryDequeue(out var item))
            {
                buffer[drained] = item;
                drained++;
            }

            return drained;
        }
    }
}
