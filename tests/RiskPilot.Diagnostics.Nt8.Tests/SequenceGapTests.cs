using System;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class SequenceGapTests
    {
        [Fact]
        public void FirstObservation_IsNotAGap()
        {
            var detector = new SequenceGapDetector();
            Assert.Equal(0, detector.Observe("quotes", 100));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void ContiguousSequence_IsNotAGap()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 1);
            Assert.Equal(0, detector.Observe("quotes", 2));
            Assert.Equal(0, detector.Observe("quotes", 3));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void MissingSequence_IsReported()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 10);
            var missing = detector.Observe("quotes", 15);
            Assert.Equal(4, missing);
            Assert.Equal(1, detector.TotalGaps);
            Assert.Equal(4, detector.TotalMissing);
            Assert.True(detector.HasGaps);
        }

        [Fact]
        public void StreamsAreIsolated()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 1);
            detector.Observe("trades", 50);
            Assert.Equal(0, detector.Observe("quotes", 2));
            Assert.Equal(0, detector.Observe("trades", 51));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void BackwardsSequence_IsIgnored()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 10);
            Assert.Equal(0, detector.Observe("quotes", 9));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void Pipeline_DetectsGapPerStreamAndFlagsDataLoss()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new Core.Queue.EventIngestPipeline(100, 100, clock);
            var session = new SessionContext(SessionId.New(System.DateTime.UtcNow), System.DateTime.UtcNow);

            pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 100 });
            pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 105 });

            Assert.Equal(4, session.SequenceGapCount);
            Assert.True(session.HasDataLoss);
        }

        [Fact]
        public void Pipeline_GapsAreIsolatedBetweenStreams()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new Core.Queue.EventIngestPipeline(100, 100, clock);
            var session = new SessionContext(SessionId.New(System.DateTime.UtcNow), System.DateTime.UtcNow);

            pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 1 });
            pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, SourceSequence = 900 });
            pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 2 });

            Assert.Equal(0, session.SequenceGapCount);
            Assert.False(session.HasDataLoss);
        }
    }
}
