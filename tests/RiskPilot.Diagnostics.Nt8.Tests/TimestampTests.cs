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

        [Fact]
        public void Ingest_FlagsImplausibleFutureTimestamp()
        {
            var fixedNow = new DateTime(2026, 11, 12, 8, 30, 0, DateTimeKind.Utc);
            var clock = new Fakes.FakeClock(fixedNow);
            var pipeline = new EventIngestPipeline(10, 10, clock);
            var session = NewSession();

            pipeline.Ingest(session, new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                SourceTimestampUtc = fixedNow.AddHours(3)
            });

            Assert.Equal(1, session.TimestampInvalidCount);
        }

        [Fact]
        public void Ingest_AcceptsReasonableTimestampWithinSkew()
        {
            var fixedNow = new DateTime(2026, 11, 12, 8, 30, 0, DateTimeKind.Utc);
            var clock = new Fakes.FakeClock(fixedNow);
            var pipeline = new EventIngestPipeline(10, 10, clock);
            var session = NewSession();

            pipeline.Ingest(session, new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                SourceTimestampUtc = fixedNow.AddMinutes(1)
            });

            Assert.Equal(0, session.TimestampInvalidCount);
        }

        [Fact]
        public void Engine_SurfacesTimestampAnomalyAsFail()
        {
            var fixedNow = new DateTime(2026, 11, 12, 8, 30, 0, DateTimeKind.Utc);
            var session = NewSession();
            session.RecordTimestampInvalid();

            var engine = new Core.Engine.DiagnosticEngine(new Fakes.FakeDataProvider(), new Core.Logging.DiagnosticLog(64), new Fakes.FakeClock(fixedNow));
            var report = engine.Run(session);

            Assert.Equal(Core.Status.DiagnosticStatus.FAIL, report.AggregateStatus());
            Assert.Contains(report.Results, r => r.CheckId == "M0.EVENT_INTEGRITY");
        }
    }
}
