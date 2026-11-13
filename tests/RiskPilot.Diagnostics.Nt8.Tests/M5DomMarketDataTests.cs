using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// M5 (DOM / Market Data) data-reliability tests. They drive the real production probe,
    /// consumer, assembler and host capture adapter and assert the truthfulness contract:
    /// a check is only PASS on real observed data, absent data is INCONCLUSIVE, and a stale or
    /// invalid observation is reported rather than silently accepted.
    /// </summary>
    public class M5DomMarketDataTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        private static ProbeContext Context(FakeDataProvider provider, DateTime now)
        {
            var session = new SessionContext(SessionId.New(now), now);
            return new ProbeContext(provider, session, new FakeClock(now), "corr00000000");
        }

        private static ProbeContext Context(FakeDataProvider provider)
        {
            return Context(provider, Now);
        }

        private static DiagnosticResult Find(IEnumerable<DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }

        private static FakeDataProvider ProviderWith(DomMarketDataObservation observation)
        {
            return new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { DomMarketData = observation }
            };
        }

        private static DomCounterBlock ObservableHost(long sync = 10)
        {
            return new DomCounterBlock { Layer = "HOST_ADAPTER", Observable = true, SnapshotSyncCount = sync };
        }

        // ---------------- Valid data ----------------

        [Fact]
        public void ValidData_ObservedSurface_PassesPresenceAndIntegrityChecks()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                DepthObserved = true,
                Level1QuoteObserved = true,
                LastTradeObserved = true,
                LastEventUtc = Now.AddSeconds(-0.5),
                HostAdapter = ObservableHost()
            });

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.DOM_SURFACE").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.LEVEL1").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.DEPTH").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.SNAPSHOT_SYNC").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.VALIDITY").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.DATA_INTEGRITY").Status);

            // Freshness is always INCONCLUSIVE (it is a point-in-time age, never a verdict),
            // but it must be a real FRESH reading derived from the observed event.
            var freshness = Find(results, "M5.FRESHNESS");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, freshness.Status);
            Assert.Contains("FRESH", freshness.ReasonCodes);
        }

        // ---------------- Missing data ----------------

        [Fact]
        public void MissingData_EmptyObservation_NeverPasses()
        {
            var provider = ProviderWith(new DomMarketDataObservation());

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.DoesNotContain(results, r => r.Status == DiagnosticStatus.PASS);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.DOM_SURFACE").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.DEPTH").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.LEVEL1").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.FRESHNESS").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.DATA_INTEGRITY").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.VALIDITY").Status);
        }

        [Fact]
        public void Validity_ObservableHostButNoDepth_IsInconclusiveNotPass()
        {
            // A zero invalid count is only "clean" once a depth stream was observed. With an
            // observable host surface but no depth event there was nothing to validate, so the
            // check must not report a clean PASS on absent data.
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                HostAdapter = ObservableHost()
            });

            var validity = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.VALIDITY");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, validity.Status);
            Assert.Contains("VALIDITY_NOT_OBSERVED", validity.ReasonCodes);
        }

        [Fact]
        public void MissingData_NoObservation_ReportsUnobservableSurface()
        {
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot { DomMarketData = null } };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            var surface = Find(results, "M5.DOM_SURFACE");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, surface.Status);
            Assert.Contains("UNOBSERVABLE", surface.ReasonCodes);
        }

        [Fact]
        public void MissingData_Level1QuoteOnly_DoesNotPassDepth()
        {
            // A Level I quote is a different stream from Level II depth and must never be
            // presented as depth evidence.
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                DepthObserved = false
            });

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.DOM_SURFACE").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.DEPTH").Status);
            Assert.Contains("DEPTH_NOT_OBSERVED", Find(results, "M5.DEPTH").ReasonCodes);
        }

        [Fact]
        public void MissingData_NoAge_ReportsFreshnessUnobservable()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true
            });

            var freshness = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.FRESHNESS");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, freshness.Status);
            Assert.Contains("UNOBSERVABLE", freshness.ReasonCodes);
        }

        // ---------------- Stale data ----------------

        [Fact]
        public void StaleData_IsReportedAsStale_NotFresh()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                LastEventUtc = Now.AddSeconds(-(M5DomMarketDataProbe.StaleAfterSeconds + 30))
            });

            var freshness = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.FRESHNESS");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, freshness.Status);
            Assert.Contains("STALE", freshness.ReasonCodes);
            Assert.Contains("stale=True", freshness.Observation);
        }

        [Fact]
        public void FreshData_WithinThreshold_IsReportedAsFresh()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                LastEventUtc = Now.AddSeconds(-1)
            });

            var freshness = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.FRESHNESS");

            Assert.Contains("FRESH", freshness.ReasonCodes);
        }

        [Fact]
        public void FutureEventAge_IsNotReportedAsNegative()
        {
            // An event stamped in the future relative to the tool clock cannot yield a trusted
            // age, so freshness stays unobservable rather than reporting a negative age.
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                LastEventUtc = Now.AddSeconds(30)
            });

            var freshness = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.FRESHNESS");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, freshness.Status);
            Assert.Contains("UNOBSERVABLE", freshness.ReasonCodes);
        }

        [Fact]
        public void ExplicitAge_TakesPrecedenceOverDerivedAge()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                Level1QuoteObserved = true,
                LastDomEventAgeSeconds = 2.5,
                LastEventUtc = Now.AddSeconds(-1000)
            });

            var freshness = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.FRESHNESS");

            Assert.Contains("FRESH", freshness.ReasonCodes);
            Assert.Contains("lastDomEventAgeSeconds=2.5", freshness.Observation);
        }

        // ---------------- Invalid data ----------------

        [Fact]
        public void InvalidData_MalformedEvent_FailsValidity()
        {
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                DepthObserved = true,
                MalformedCount = 2,
                HostAdapter = ObservableHost()
            });

            var validity = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.VALIDITY");

            Assert.Equal(DiagnosticStatus.FAIL, validity.Status);
            Assert.Contains("INVALID_EVENTS_OBSERVED", validity.ReasonCodes);
        }

        [Fact]
        public void InvalidData_OutOfBounds_FailsSnapshotSync_EvenWithoutFailure()
        {
            // Out-of-bounds depth operations are a real mirror-drift signal: an index violation
            // must never be reported as a clean pass even when no sync failure was counted.
            var provider = ProviderWith(new DomMarketDataObservation
            {
                DataSurfaceObservable = true,
                DepthObserved = true,
                HostAdapter = new DomCounterBlock
                {
                    Layer = "HOST_ADAPTER",
                    Observable = true,
                    SnapshotSyncCount = 5,
                    SnapshotSyncFailureCount = 0,
                    UpdateOutOfBoundsCount = 3
                }
            });

            var sync = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.SNAPSHOT_SYNC");

            Assert.Equal(DiagnosticStatus.FAIL, sync.Status);
            Assert.Contains("SNAPSHOT_OUT_OF_BOUNDS", sync.ReasonCodes);
            Assert.Contains("UPDATE_OOB=3", sync.ReasonCodes);
        }

        [Fact]
        public void InvalidData_SequenceGap_FailsDataIntegrity_EvenWithoutObservedSurface()
        {
            var provider = ProviderWith(new DomMarketDataObservation { SequenceGapCount = 4 });

            var integrity = Find(new M5DomMarketDataProbe().Evaluate(Context(provider)), "M5.DATA_INTEGRITY");

            Assert.Equal(DiagnosticStatus.FAIL, integrity.Status);
        }

        // ---------------- Consumer: reset counting + freshness tracking ----------------

        [Fact]
        public void Consumer_ResetOperationWithResetFlag_IsCountedOnce()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Reset,
                IsReset = true,
                IngestedAtUtc = Now
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostReset));
        }

        [Fact]
        public void Consumer_NonResetOperationWithResetFlag_IsStillCountedAsReset()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Update,
                IsReset = true,
                IngestedAtUtc = Now
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostReset));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostUpdate));
        }

        [Fact]
        public void Consumer_TracksNewestMarketDataEventInstant()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new MarketDataRecord { InstrumentId = "ES", Kind = MarketDataKind.Quote, IngestedAtUtc = Now });
            Assert.Equal(Now, session.LastMarketDataEventUtc);

            // An older event must not move the newest instant backwards.
            consumer.Consume(new MarketDataRecord { InstrumentId = "ES", Kind = MarketDataKind.Trade, IngestedAtUtc = Now.AddSeconds(-5) });
            Assert.Equal(Now, session.LastMarketDataEventUtc);

            consumer.Consume(new DomRecord { InstrumentId = "ES", Operation = DomOperation.Insert, Price = 5000.0, IngestedAtUtc = Now.AddSeconds(3) });
            Assert.Equal(Now.AddSeconds(3), session.LastMarketDataEventUtc);
        }

        // ---------------- Consumer: validity counting from real records ----------------

        [Fact]
        public void Consumer_ValidDepthRecord_CountsNoInvalidFields()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Update,
                Position = 2,
                Price = 4999.75,
                Volume = 7,
                IngestedAtUtc = Now
            });

            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPrice));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidSize));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPosition));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostMalformed));
        }

        [Fact]
        public void Consumer_NegativePrice_IsCountedAsInvalidPrice()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Update,
                Position = 2,
                Price = -1.0,
                Volume = 7,
                IngestedAtUtc = Now
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPrice));
        }

        [Fact]
        public void Consumer_NonFinitePrice_IsCountedAsInvalidPrice()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Update, Position = 2, Price = double.NaN, Volume = 7, IngestedAtUtc = Now });
            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Insert, Position = 3, Price = double.PositiveInfinity, Volume = 1, IngestedAtUtc = Now });

            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPrice));
        }

        [Fact]
        public void Consumer_NegativeVolume_IsCountedAsInvalidSize()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Update,
                Position = 2,
                Price = 5000.0,
                Volume = -1,
                IngestedAtUtc = Now
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidSize));
        }

        [Fact]
        public void Consumer_NegativePosition_IsCountedAsInvalidPosition()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord
            {
                InstrumentId = "ES 12-26",
                Operation = DomOperation.Update,
                Position = -1,
                Price = 5000.0,
                Volume = 3,
                IngestedAtUtc = Now
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPosition));
        }

        [Fact]
        public void Consumer_UnknownOperationOrMissingInstrument_IsCountedAsMalformed()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Unknown, IngestedAtUtc = Now });
            consumer.Consume(new DomRecord { InstrumentId = string.Empty, Operation = DomOperation.Update, Price = 5000.0, IngestedAtUtc = Now });

            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.DomHostMalformed));
            // A malformed record contributes no depth-operation evidence.
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostUpdate));
        }

        [Fact]
        public void Consumer_Reset_DoesNotCountInvalidFields()
        {
            // A reset carries no price/size/position, so it must never be counted as invalid.
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);

            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Reset, IsReset = true, IngestedAtUtc = Now });

            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPrice));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidSize));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.DomHostInvalidPosition));
        }

        // ---------------- Assembler: real evidence propagation ----------------

        [Fact]
        public void Assembler_PropagatesAvailabilityDepthAndDerivedFreshness()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            session.Counters.Increment(RuntimeCounterKeys.QuoteObserved);
            session.Counters.Increment(RuntimeCounterKeys.LastTradeObserved);
            session.Counters.Increment(RuntimeCounterKeys.DomHostInsert, 2);

            var consumer = new M5DomMarketDataConsumer(session);
            consumer.Consume(new MarketDataRecord { InstrumentId = "ES", Kind = MarketDataKind.Quote, IngestedAtUtc = Now.AddSeconds(-2) });

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var dom = merged.DomMarketData;

            Assert.True(dom.DataSurfaceObservable);
            Assert.True(dom.DepthObserved);
            Assert.True(dom.Level1QuoteObserved);
            Assert.True(dom.LastTradeObserved);
            Assert.Equal(Now.AddSeconds(-2), dom.LastEventUtc);
        }

        [Fact]
        public void Assembler_MalformedDepthRecord_MakesSurfaceObservableAndFailsValidity()
        {
            // A malformed depth record must not be hidden behind an "unobservable" surface: the
            // host delivered an event we could not use, and the loss must be visible as a FAIL.
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);
            consumer.Consume(new DomRecord { InstrumentId = string.Empty, Operation = DomOperation.Update, Price = 5000.0, IngestedAtUtc = Now });

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var provider = new FakeDataProvider { Snapshot = merged };
            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.True(merged.DomMarketData.DataSurfaceObservable);
            Assert.Equal(1, merged.DomMarketData.MalformedCount);
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.VALIDITY").Status);
            Assert.Contains("INVALID_EVENTS_OBSERVED", Find(results, "M5.VALIDITY").ReasonCodes);
        }

        [Fact]
        public void Assembler_InvalidDepthField_FailsValidity_FromRealIngest()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);
            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Update, Position = 2, Price = -1.0, Volume = 7, IngestedAtUtc = Now });

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var provider = new FakeDataProvider { Snapshot = merged };
            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.VALIDITY").Status);
            Assert.Contains("INVALID_PRICE=1", Find(results, "M5.VALIDITY").ReasonCodes);
        }

        [Fact]
        public void Assembler_ValidDepthRecords_ValidityIsPass_OnRealEvidence()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M5DomMarketDataConsumer(session);
            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Update, Position = 2, Price = 4999.75, Volume = 7, IngestedAtUtc = Now });

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var provider = new FakeDataProvider { Snapshot = merged };
            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));

            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.VALIDITY").Status);
        }

        // ---------------- Host capture adapter (offline stub surface) ----------------

        [Collection("StubHostState")]
        public class HostCapture
        {
            private static void Reset()
            {
                Instrument.All.Clear();
                Account.All.Clear();
                Connection.Connections.Clear();
            }

            private static Instrument EsInstrument()
            {
                return new Instrument
                {
                    FullName = "ES 12-26",
                    Exchange = Exchange.Globex,
                    MasterInstrument = new MasterInstrument { Name = "ES" }
                };
            }

            [Fact]
            public void Capture_MapsLevel1AndDepthEvents_IntoNormalizedRecords()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    Instrument.All.Add(instrument);

                    var captured = new List<NormalizedRecord>();
                    var capture = new MarketDataCapture(r => { captured.Add(r); return true; }, () => true);

                    Assert.True(capture.TrySubscribe());
                    Assert.True(capture.IsSubscribed);

                    instrument.MarketData.RaiseUpdate(new MarketDataEventArgs { Instrument = instrument, MarketDataType = MarketDataType.Bid, Bid = 5000.0, Time = Now });
                    instrument.MarketData.RaiseUpdate(new MarketDataEventArgs { Instrument = instrument, MarketDataType = MarketDataType.Last, Price = 5000.25, Volume = 3, Time = Now });
                    instrument.MarketDepth.RaiseUpdate(new MarketDepthEventArgs { Instrument = instrument, Operation = Operation.Update, MarketDataType = MarketDataType.Bid, Position = 2, Price = 4999.75, Volume = 7, Time = Now });

                    Assert.Equal(3, captured.Count);

                    var quote = captured.OfType<MarketDataRecord>().First(r => r.Kind == MarketDataKind.Quote);
                    Assert.Equal("ES 12-26", quote.InstrumentId);
                    Assert.Equal(5000.0, quote.Bid);

                    var trade = captured.OfType<MarketDataRecord>().First(r => r.Kind == MarketDataKind.Trade);
                    Assert.Equal(5000.25, trade.Last);
                    Assert.Equal(3, trade.Volume);

                    var depth = captured.OfType<DomRecord>().Single();
                    Assert.Equal(DomOperation.Update, depth.Operation);
                    Assert.Equal(2, depth.Position);
                    Assert.Equal(4999.75, depth.Price);
                    Assert.Equal(7, depth.Volume);

                    capture.Detach();
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Capture_NoLoadedInstrument_DoesNotSubscribe()
            {
                Reset();
                try
                {
                    var capture = new MarketDataCapture(r => true, () => true);
                    Assert.False(capture.TrySubscribe());
                    Assert.False(capture.IsSubscribed);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Capture_AfterDetach_StopsIngesting()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    Instrument.All.Add(instrument);

                    var captured = new List<NormalizedRecord>();
                    var capture = new MarketDataCapture(r => { captured.Add(r); return true; }, () => true);
                    Assert.True(capture.TrySubscribe());

                    instrument.MarketData.RaiseUpdate(new MarketDataEventArgs { Instrument = instrument, MarketDataType = MarketDataType.Bid, Bid = 1.0, Time = Now });
                    Assert.Single(captured);

                    capture.Detach();
                    instrument.MarketData.RaiseUpdate(new MarketDataEventArgs { Instrument = instrument, MarketDataType = MarketDataType.Bid, Bid = 2.0, Time = Now });
                    Assert.Single(captured);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Capture_MapsNonLevel1MarketDataType_ToUnknown()
            {
                Assert.Equal(MarketDataKind.Quote, MarketDataCapture.MapMarketDataKind(MarketDataType.Ask));
                Assert.Equal(MarketDataKind.Trade, MarketDataCapture.MapMarketDataKind(MarketDataType.Last));
                Assert.Equal(MarketDataKind.Unknown, MarketDataCapture.MapMarketDataKind(MarketDataType.Settlement));
                Assert.Equal(DomOperation.Insert, MarketDataCapture.MapDomOperation(Operation.Add));
                Assert.Equal(DomOperation.Unknown, MarketDataCapture.MapDomOperation((Operation)999));
            }
        }

        // ---------------- Runtime integration: captured data reaches M5 ----------------

        [Fact]
        public void Runtime_CapturedMarketData_ProducesObservedM5Surface()
        {
            var provider = new FakeDataProvider();
            var runtime = new DiagnosticRuntime(provider, new FakeClock(Now), 512, 64, 512);
            runtime.Initialize();
            runtime.Start();

            Assert.True(runtime.Ingest(new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, Bid = 5000, Ask = 5000.25 }));
            Assert.True(runtime.Ingest(new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, Last = 5000.25 }));
            Assert.True(runtime.Ingest(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Insert, Position = 0, Price = 5000, Volume = 5 }));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.Session.ProcessedCount < 3 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            var report = runtime.RunDiagnosticPass();
            var surface = Find(report.Results, "M5.DOM_SURFACE");
            Assert.Equal(DiagnosticStatus.PASS, surface.Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M5.LEVEL1").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M5.DEPTH").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M5.DATA_INTEGRITY").Status);

            runtime.Dispose();
        }
    }
}
