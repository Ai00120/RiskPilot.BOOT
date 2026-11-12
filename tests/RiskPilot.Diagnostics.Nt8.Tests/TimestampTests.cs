using System;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class TimestampTests
    {
        private static SessionContext NewSession()
        {
            return new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
        }

        [Fact]
        public void Ingest_StampsIngestedTimestampFromClock()
        {
            var fixedNow = new DateTime(2026, 11, 12, 8, 30, 0, DateTimeKind.Utc);
            var clock = new Fakes.FakeClock(fixedNow);
            var pipeline = new EventIngestPipeline(10, 10, clock);
            var session = NewSession();
            var record = new MarketDataRecord { InstrumentId = "ES 12-26" };

            pipeline.Ingest(session, record);

            Assert.Equal(fixedNow, record.IngestedAtUtc);
            Assert.Equal(DateTimeKind.Utc, record.IngestedAtUtc.Kind);
        }

        [Fact]
        public void Ingest_DefaultsSourceTimeBasisToSessionBasis()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(10, 10, clock);
            var session = NewSession();
            var record = new MarketDataRecord { InstrumentId = "ES 12-26" };

            pipeline.Ingest(session, record);

            Assert.Equal(SourceTimeBasis.ServerTimeUnverified, record.SourceTimeBasis);
            Assert.Equal(SourceTimeBasis.ServerTimeUnverified, session.SourceTimeBasis);
        }

        [Fact]
        public void SourceTimeBasis_MapsToExpectedWireString()
        {
            Assert.Equal("SERVER_TIME_UNVERIFIED", SourceTimeBasis.ServerTimeUnverified.ToWire());
            Assert.Equal("UNKNOWN", SourceTimeBasis.Unknown.ToWire());
        }

        [Fact]
        public void Ingest_AssignsMonotonicSequence()
        {
            var clock = new Core.Clock.SystemClock();
            var pipeline = new EventIngestPipeline(10, 10, clock);
            var session = NewSession();

            var first = new MarketDataRecord { InstrumentId = "ES 12-26", SourceTimestampUtc = DateTime.UtcNow };
            var second = new MarketDataRecord { InstrumentId = "ES 12-26", SourceTimestampUtc = DateTime.UtcNow.AddSeconds(1) };

            pipeline.Ingest(session, first);
            pipeline.Ingest(session, second);

            Assert.True(second.Sequence > first.Sequence);
        }
    }
}
