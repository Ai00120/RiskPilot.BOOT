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
    }
}
