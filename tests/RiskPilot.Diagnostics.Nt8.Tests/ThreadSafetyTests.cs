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
    }
}
