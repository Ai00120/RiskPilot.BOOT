using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Fans normalized events out to registered <see cref="INormalizedEventConsumer"/>
    /// instances on the drain thread. One consumer throwing never aborts the others and
    /// never propagates into the drain loop: the failure is counted and reported through
    /// <see cref="ConsumerErrorCount"/>. Registration is expected before the drain loop
    /// starts, but is made safe against concurrent dispatch with a copy-on-write array.
    /// </summary>
    public sealed class NormalizedEventDispatcher
    {
        private readonly object _gate = new object();
        private INormalizedEventConsumer[] _consumers = new INormalizedEventConsumer[0];
        private long _consumed;
        private long _consumerErrors;

        /// <summary>Number of records delivered to at least one consumer.</summary>
        public long ConsumedCount { get { return Interlocked.Read(ref _consumed); } }

        /// <summary>Number of consumer invocations that threw and were contained.</summary>
        public long ConsumerErrorCount { get { return Interlocked.Read(ref _consumerErrors); } }

        public IReadOnlyList<INormalizedEventConsumer> Consumers
        {
            get { return Volatile.Read(ref _consumers); }
        }

        /// <summary>Registers a consumer. Duplicate registrations are ignored.</summary>
        public void Register(INormalizedEventConsumer consumer)
        {
            if (consumer == null)
            {
                throw new ArgumentNullException(nameof(consumer));
            }

            lock (_gate)
            {
                var current = _consumers;
                foreach (var existing in current)
                {
                    if (ReferenceEquals(existing, consumer))
                    {
                        return;
                    }
                }

                var next = new INormalizedEventConsumer[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = consumer;
                Volatile.Write(ref _consumers, next);
            }
        }

        /// <summary>
        /// Dispatches one record to every registered consumer. Returns the number of
        /// consumers that processed the record successfully. Exceptions are contained.
        /// </summary>
        public int Dispatch(NormalizedRecord record)
        {
            if (record == null)
            {
                return 0;
            }

            var consumers = Volatile.Read(ref _consumers);
            if (consumers.Length == 0)
            {
                return 0;
            }

            var delivered = 0;
            foreach (var consumer in consumers)
            {
                try
                {
                    consumer.Consume(record);
                    delivered++;
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _consumerErrors);
                }
            }

            if (delivered > 0)
            {
                Interlocked.Increment(ref _consumed);
            }

            return delivered;
        }
    }
}
