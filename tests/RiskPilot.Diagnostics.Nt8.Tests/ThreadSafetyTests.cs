using System;
using System.Threading;
using System.Threading.Tasks;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class ThreadSafetyTests
    {
        private static SessionContext NewSession()
        {
            return new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
        }

        [Fact]
        public void ConcurrentEnqueue_UnderCapacity_LosesNoEvents()
        {
            const int capacity = 100000;
            const int producers = 8;
            const int perProducer = 5000;

            var clock = new FakeClock(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc));
            var pipeline = new EventIngestPipeline(capacity, capacity, clock);
            var session = NewSession();

            var threads = new Thread[producers];
            for (var p = 0; p < producers; p++)
            {
                var producerIndex = p;
                threads[p] = new Thread(() =>
                {
                    for (var i = 0; i < perProducer; i++)
                    {
                        pipeline.Ingest(session, new MarketDataRecord
                        {
                            InstrumentId = "ES 12-26",
                            Kind = MarketDataKind.Quote,
                            SourceTimestampUtc = clock.UtcNow.AddTicks(producerIndex * perProducer + i),
                            Bid = 5000.25,
                            Ask = 5000.5
                        });
                    }
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.Equal(producers * perProducer, session.EnqueuedCount);
            Assert.Equal(0, session.DroppedCount);
            Assert.Equal(producers * perProducer, pipeline.Queue.EnqueuedCount);
        }

        [Fact]
        public void ConcurrentEnqueueAndDrain_CountsRemainConsistent()
        {
            const int capacity = 200000;
            var clock = new FakeClock(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc));
            var pipeline = new EventIngestPipeline(capacity, capacity, clock);
            var session = NewSession();

            var producerDone = false;
            var consumer = new Thread(() =>
            {
                var buffer = new NormalizedRecord[128];
                long drained = 0;
                while (!Volatile.Read(ref producerDone) || drained < session.EnqueuedCount)
                {
                    var n = pipeline.Queue.Drain(buffer, buffer.Length);
                    drained += n;
                    if (n == 0) Thread.Sleep(1);
                }
            });
            consumer.Start();

            Parallel.For(0, 20000, i =>
            {
                pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade });
            });

            Volatile.Write(ref producerDone, true);
            consumer.Join(TimeSpan.FromSeconds(10));

            Assert.Equal(session.EnqueuedCount, pipeline.Queue.DequeuedCount);
            Assert.Equal(0, session.DroppedCount);
        }

        [Fact]
        public void DuplicateDetector_ConcurrentDistinctKeys_CountsNoFalseDuplicates()
        {
            // Every key is unique, so no key may be reported as a duplicate regardless of
            // interleaving across producer threads.
            const int producers = 8;
            const int perProducer = 20000;
            var detector = new DuplicateEventDetector(producers * perProducer + 16);

            var duplicates = 0;
            var threads = new Thread[producers];
            for (var p = 0; p < producers; p++)
            {
                var producerIndex = p;
                threads[p] = new Thread(() =>
                {
                    var local = 0;
                    for (var i = 0; i < perProducer; i++)
                    {
                        if (detector.Observe("k:" + producerIndex + ":" + i))
                        {
                            local++;
                        }
                    }

                    Interlocked.Add(ref duplicates, local);
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.Equal(0, duplicates);
            Assert.Equal(0, detector.DuplicateCount);
        }

        [Fact]
        public void DuplicateDetector_ConcurrentIdenticalKey_CountsEachRepeatOnce()
        {
            const int threads = 8;
            const int perThread = 20000;
            var detector = new DuplicateEventDetector(1024);

            var threadsArr = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                threadsArr[t] = new Thread(() =>
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        detector.Observe("shared-key");
                    }
                });
            }

            foreach (var t in threadsArr) t.Start();
            foreach (var t in threadsArr) t.Join();

            // First observation is new; every subsequent one is a duplicate.
            Assert.Equal((threads * perThread) - 1, detector.DuplicateCount);
        }

        [Fact]
        public void SequenceGapDetector_ConcurrentContiguousStream_ReportsNoGaps()
        {
            const int producers = 4;
            const int perProducer = 10000;
            var detector = new SequenceGapDetector();

            var threads = new Thread[producers];
            for (var p = 0; p < producers; p++)
            {
                var producerIndex = p;
                threads[p] = new Thread(() =>
                {
                    // Each producer owns its own stream; sequences are contiguous within it.
                    for (var i = 1; i <= perProducer; i++)
                    {
                        detector.Observe("stream-" + producerIndex, i);
                    }
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.Equal(0, detector.TotalGaps);
            Assert.False(detector.HasGaps);
        }
    }
}
