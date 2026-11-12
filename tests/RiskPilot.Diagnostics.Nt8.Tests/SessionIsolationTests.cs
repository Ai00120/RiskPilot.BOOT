using System;
using System.Linq;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class SessionIsolationTests
    {
        [Fact]
        public void SessionId_HasExpectedFormat()
        {
            var id = SessionId.New(new DateTime(2026, 11, 12, 8, 0, 59, DateTimeKind.Utc));
            Assert.True(SessionId.IsValid(id));
            Assert.StartsWith("diag_20261112_080059_", id);
        }

        [Fact]
        public void SessionId_RejectsMalformedValues()
        {
            Assert.False(SessionId.IsValid(null));
            Assert.False(SessionId.IsValid(""));
            Assert.False(SessionId.IsValid("rec_20261111_200059_deadbeef"));
            Assert.False(SessionId.IsValid("diag_2026_080059_" + new string('a', 32)));
        }

        [Fact]
        public void TwoSessions_HaveDistinctIdsAndIsolatedCounters()
        {
            var now = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
            var first = new SessionContext(SessionId.New(now), now);
            var second = new SessionContext(SessionId.New(now), now);

            Assert.NotEqual(first.SessionId, second.SessionId);

            first.RecordEnqueued();
            first.RecordEnqueued();
            first.RecordDropped(3);

            Assert.Equal(2, first.EnqueuedCount);
            Assert.Equal(3, first.DroppedCount);
            Assert.Equal(0, second.EnqueuedCount);
            Assert.Equal(0, second.DroppedCount);
        }

        [Fact]
        public void Records_CarryTheirOwningSessionId()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new Core.Queue.EventIngestPipeline(10, 10, clock);
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);

            var record = new MarketDataRecord { InstrumentId = "ES 12-26", SourceTimestampUtc = DateTime.UtcNow };
            pipeline.Ingest(session, record);

            Assert.Equal(session.SessionId, record.SessionId);
        }

        [Fact]
        public void SessionContext_RequiresId()
        {
            Assert.Throws<ArgumentException>(() => new SessionContext(null, DateTime.UtcNow));
        }

        [Fact]
        public void HasDataLoss_ReflectsDropsAndGaps()
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            Assert.False(session.HasDataLoss);
            session.RecordDropped(1);
            Assert.True(session.HasDataLoss);

            var other = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            other.RecordSequenceGap(2);
            Assert.True(other.HasDataLoss);
        }
    }
}
