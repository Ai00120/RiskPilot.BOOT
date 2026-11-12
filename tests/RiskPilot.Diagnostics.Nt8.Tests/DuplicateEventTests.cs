using System;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class DuplicateEventTests
    {
        private static SessionContext NewSession()
        {
            return new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
        }

        private static MarketDataRecord Quote(long ticks)
        {
            return new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Quote,
                SourceTimestampUtc = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticks)
            };
        }

        [Fact]
        public void Detector_FlagsRepeatedKey()
        {
            var detector = new DuplicateEventDetector(100);
            Assert.False(detector.Observe("a"));
            Assert.True(detector.Observe("a"));
            Assert.False(detector.Observe("b"));
            Assert.Equal(1, detector.DuplicateCount);
        }

        [Fact]
        public void Detector_IgnoresEmptyKey()
        {
            var detector = new DuplicateEventDetector(10);
            Assert.False(detector.Observe(null));
            Assert.False(detector.Observe(string.Empty));
            Assert.Equal(0, detector.DuplicateCount);
        }

        [Fact]
        public void Detector_EvictsBeyondWindow()
        {
            var detector = new DuplicateEventDetector(2);
            Assert.False(detector.Observe("a"));
            Assert.False(detector.Observe("b"));
            Assert.False(detector.Observe("c")); // evicts "a"
            Assert.False(detector.Observe("a")); // no longer tracked
        }

        [Fact]
        public void Pipeline_SuppressesDuplicateAndCountsIt()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();

            Assert.True(pipeline.Ingest(session, Quote(0)));
            Assert.False(pipeline.Ingest(session, Quote(0)));

            Assert.Equal(1, session.EnqueuedCount);
            Assert.Equal(1, session.DuplicateCount);
            Assert.Equal(0, session.DroppedCount);
        }

        [Fact]
        public void SameTimestamp_DifferentInstruments_AreNotDuplicates()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var es = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, Bid = 5000.25, Ask = 5000.5, SourceTimestampUtc = ts };
            var nq = new MarketDataRecord { InstrumentId = "NQ 12-26", Kind = MarketDataKind.Quote, Bid = 20000.25, Ask = 20000.5, SourceTimestampUtc = ts };

            Assert.True(pipeline.Ingest(session, es));
            Assert.True(pipeline.Ingest(session, nq));

            Assert.Equal(2, session.EnqueuedCount);
            Assert.Equal(0, session.DuplicateCount);
        }

        [Fact]
        public void SameTimestamp_DifferentStreams_AreNotDuplicates()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var quote = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, Bid = 5000.25, Ask = 5000.5, SourceTimestampUtc = ts };
            var trade = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 3, SourceTimestampUtc = ts };

            Assert.True(pipeline.Ingest(session, quote));
            Assert.True(pipeline.Ingest(session, trade));

            Assert.Equal(2, session.EnqueuedCount);
            Assert.Equal(0, session.DuplicateCount);
        }

        [Fact]
        public void SameTimestampSameStream_DifferentPayload_AreNotDuplicates()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            // Two distinct trades printed on the same instrument at the same timestamp but
            // with different size: legitimate simultaneous market events.
            var first = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 1, SourceTimestampUtc = ts };
            var second = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 7, SourceTimestampUtc = ts };

            Assert.True(pipeline.Ingest(session, first));
            Assert.True(pipeline.Ingest(session, second));

            Assert.Equal(2, session.EnqueuedCount);
            Assert.Equal(0, session.DuplicateCount);
        }

        [Fact]
        public void SameTimestamp_DifferentDomOperations_AreNotDuplicates()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var insert = new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Insert, Position = 3, Price = 5000.25, Volume = 12, SourceTimestampUtc = ts };
            var update = new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Update, Position = 3, Price = 5000.25, Volume = 12, SourceTimestampUtc = ts };

            Assert.True(pipeline.Ingest(session, insert));
            Assert.True(pipeline.Ingest(session, update));

            Assert.Equal(2, session.EnqueuedCount);
            Assert.Equal(0, session.DuplicateCount);
        }

        [Fact]
        public void IdenticalEventSameStream_IsStillDetectedAsDuplicate()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var a = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 5, SourceTimestampUtc = ts };
            var b = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 5, SourceTimestampUtc = ts };

            Assert.True(pipeline.Ingest(session, a));
            Assert.False(pipeline.Ingest(session, b));

            Assert.Equal(1, session.EnqueuedCount);
            Assert.Equal(1, session.DuplicateCount);
        }

        [Fact]
        public void SourceSequence_DisambiguatesOtherwiseIdenticalEvents()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(100, 100, clock);
            var session = NewSession();
            var ts = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

            var a = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 5, SourceTimestampUtc = ts, SourceSequence = 101 };
            var b = new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25, Volume = 5, SourceTimestampUtc = ts, SourceSequence = 102 };

            Assert.True(pipeline.Ingest(session, a));
            Assert.True(pipeline.Ingest(session, b));

            Assert.Equal(2, session.EnqueuedCount);
            Assert.Equal(0, session.DuplicateCount);
        }
    }
}
